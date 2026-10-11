using Dalamud.Bindings.ImGui;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using System.Numerics;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private TransferClient? transfers;
    private bool selectTransfersTab;
    private string transferLaunchDraft = "";
    private string transferSourceDirectory = "";
    private string transferModSearch = "";
    private Dictionary<string, string> transferMods = [];
    private long transferModsRefresh;
    private bool transferManualPackage;
    private Bookmark? transferSenderRole, transferReceiverRole;
    private TransferLaunch? acceptedTransferLaunch;
    private string transferPartner = "", transferPath = "", transferMessage = "", lastTransferError = "";
    private bool transferAcknowledged;
    private string lastTransferOutcome = "";
    private Task? transferSending;
    private Task<string>? transferExtracting;
    private string importingDirectory = "", importingOffer = "";
    private long importDeadline;
    private TransferClient? transferAcceptedClient;
    private Guid transferAcceptCollection;
    private string transferAcceptIdentity = "", transferAcceptOffer = "";
    private readonly AddMod addReceivedMod = new(Pi);
    private readonly GetSettingsInAllCollections receivedSettings = new(Pi);
    private readonly TrySetMod setReceivedModEnabled = new(Pi);
    private readonly DeleteMod removeReceivedMod = new(Pi);

    private void StopTransfers()
    {
        transfers?.Dispose(); transfers = null; transferPartner = "";
    }

    private void UpdateTransfers()
    {
        if (LinkFresh && link!.Snapshot.State == "linked")
        {
            if (transfers is null || transferPartner != link.Snapshot.Partner)
            {
                StopTransfers(); transferPartner = link.Snapshot.Partner;
                transfers = new TransferClient(link, transferPartner, diagnostic: detail => Log.Warning("Mod transfer transport: {Detail}", detail));
            }
        }
        else if (transfers is not null) StopTransfers();
        if (transfers is { Error.Length: > 0 } client && client.Error != lastTransferError)
        {
            lastTransferError = client.Error;
            TransferFailure(client.Error);
        }
        if (transfers is { } currentTransfer && currentTransfer.Outcome != lastTransferOutcome)
        {
            lastTransferOutcome = currentTransfer.Outcome;
            transferMessage = lastTransferOutcome switch
            {
                "completed" => T("Получатель подтвердил установку и включение мода.", "The recipient confirmed installation and activation."),
                "declined" => T("Получатель отклонил передачу.", "The recipient declined the transfer."),
                "cancelled" => T("Отправитель отменил передачу.", "The sender cancelled the transfer."),
                "expired" => T("Срок передачи истёк. Пакет удалён с сервера.", "The transfer expired. The package was removed from the server."),
                "integrity_failed" => T("Контрольная сумма не совпала. Пакет отклонён; отправь его заново.", "The checksum did not match. The package was rejected; send it again."),
                _ => transferMessage,
            };
        }
        if (transferSending is { IsCompleted: true } sending)
        {
            if (sending.IsFaulted) TransferFailure(sending.Exception!.GetBaseException().Message);
            else transferMessage = T("Предложение отправлено. Файл загрузится после принятия партнёром.", "Offer sent. The file uploads after your partner accepts.");
            transferSending = null;
        }
        if (transfers?.TakeReceived() is { } package)
        {
            try
            {
                if (transferExtracting is not null || importingDirectory.Length > 0) throw new InvalidOperationException("transfer_busy");
                var root = getRoot.Invoke();
                if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("penumbra_unavailable");
                importingOffer = package.Id;
                transferExtracting = Task.Run(() => ModPackage.ExtractNew(package.Bytes, root));
            }
            catch (Exception ex) { TransferFailure(ex.Message); }
        }
        if (transferExtracting is { IsCompleted: true } extraction)
        {
            transferExtracting = null;
            try
            {
                importingDirectory = extraction.GetAwaiter().GetResult();
                // Unique folder and fresh metadata identity: never merge into a user's existing mod.
                var result = addReceivedMod.Invoke(importingDirectory);
                if (result != PenumbraApiEc.Success) throw new InvalidOperationException("penumbra_import_" + result);
                importDeadline = Environment.TickCount64 + 5000;
            }
            catch (Exception ex) { importingDirectory = ""; TransferFailure(ex.Message); }
        }
        if (importingDirectory.Length > 0)
        {
            try
            {
                if (!getMods.Invoke().ContainsKey(importingDirectory))
                {
                    if (Environment.TickCount64 > importDeadline) throw new InvalidOperationException("penumbra_import_timeout");
                    return;
                }
                var ec = receivedSettings.Invoke(importingDirectory, out var settings);
                if (ec != PenumbraApiEc.Success) throw new InvalidOperationException("penumbra_verify_" + ec);
                foreach (var collection in settings.Keys)
                {
                    var disabled = setReceivedModEnabled.Invoke(collection, importingDirectory, false);
                    if (disabled is not (PenumbraApiEc.Success or PenumbraApiEc.NothingChanged))
                        throw new InvalidOperationException("penumbra_disable_" + disabled);
                }
                ec = receivedSettings.Invoke(importingDirectory, out settings);
                if (ec != PenumbraApiEc.Success || settings.Values.Any(s => s.Item1)) throw new InvalidOperationException("penumbra_disable_unconfirmed");
                // Acceptance authorizes activation only for the same character/collection
                // that the recipient saw, never a collection chosen after switching actors.
                if (transfers is null || transfers != transferAcceptedClient || !LinkFresh ||
                    transfers.Offer is not { Incoming: true, State: "ready" } activeOffer || activeOffer.Id != importingOffer ||
                    transferAcceptOffer != importingOffer || transferAcceptCollection == Guid.Empty ||
                    Objects.LocalPlayer is not { } local || LinkIdentity(local) != transferAcceptIdentity)
                    throw new InvalidOperationException("penumbra_collection_changed");
                var (validCollection, _, currentCollection) = getCollection.Invoke(local.ObjectIndex);
                if (!validCollection || currentCollection.Id != transferAcceptCollection)
                    throw new InvalidOperationException("penumbra_collection_changed");
                var enabled = setReceivedModEnabled.Invoke(transferAcceptCollection, importingDirectory, true);
                if (enabled is not (PenumbraApiEc.Success or PenumbraApiEc.NothingChanged))
                    throw new InvalidOperationException("penumbra_enable_" + enabled);
                var (verifyCode, verified) = getSettingsWithTemp.Invoke(transferAcceptCollection, importingDirectory);
                if (verifyCode != PenumbraApiEc.Success || verified is null || !verified.Value.Item1)
                    throw new InvalidOperationException("penumbra_enable_unconfirmed");
                var installedDirectory = importingDirectory;
                var installedOffer = importingOffer;
                transferMessage = T("Мод установлен и включён в выбранной коллекции Penumbra. Команда запуска анимации не отправлялась.",
                    "The mod was installed and enabled in the selected Penumbra collection. No emote command was sent.");
                importingDirectory = ""; importingOffer = "";
                Scan();
                if (acceptedTransferLaunch is { } launch)
                {
                    acceptedTransferLaunch = null;
                    // Playback failure must not roll back a successfully installed mod.
                    try
                    {
                        var mod = discovered.FirstOrDefault(m => m.Directory == installedDirectory && m.Command.Equals(launch.Command, StringComparison.OrdinalIgnoreCase));
                        if (mod is null || catalog is null)
                            throw new InvalidOperationException("transfer_launch_unavailable");
                        var bookmark = new Bookmark { ModDirectory = installedDirectory, Name = launch.Label,
                            Command = mod.Command, IconId = mod.Icon, PoseIndex = launch.Pose, SavedOptions = launch.Options };
                        ValidateBookmark(bookmark, mod);
                        if (mod.Variants.Any(v => launch.Options.TryGetValue(v.Group, out var choices) && choices.Contains(v.Option) && !VariantCompatible(v, mod)))
                            throw new InvalidOperationException("transfer_launch_incompatible");
                        if (launch.PairId.Length > 0)
                            queuedTransferPair = new(bookmark, launch.PairId, launch.Family, transferPartner,
                                ClientState.TerritoryType, Environment.TickCount64 + 60000);
                        else Play(bookmark);
                        transferMessage = T("Мод установлен и включён. Подготовка анимации передана проигрывателю; её состояние показано во вкладке «Эмоции».", "The mod is installed and enabled. Playback preparation was requested; see Emotes for its status.");
                    }
                    catch (Exception ex)
                    {
                        transfers?.Act("reject", installedOffer);
                        TransferFailure(ex.Message);
                        return;
                    }
                }
                // A shared launch is created by complete: only confirm once the local role
                // has passed validation and its accepted continuation is installed.
                transfers?.Act("complete", installedOffer);
            }
            catch (Exception ex)
            {
                // Do not claim completion before Penumbra confirms installation and activation.
                // Roll back only this fresh generated import, never a pre-existing user mod.
                if (importingDirectory.StartsWith("EmoteShelfReceived_", StringComparison.Ordinal) &&
                    Guid.TryParseExact(importingDirectory["EmoteShelfReceived_".Length..], "N", out _))
                {
                    try { if (transferAcceptCollection != Guid.Empty) setReceivedModEnabled.Invoke(transferAcceptCollection, importingDirectory, false); }
                    catch (Exception rollback) { Log.Warning("Received import deactivation failed: {Type}", rollback.GetType().Name); }
                    try { removeReceivedMod.Invoke(importingDirectory); }
                    catch (Exception rollback) { Log.Warning("Received import rollback failed: {Type}", rollback.GetType().Name); }
                }
                TransferFailure(ex.Message);
                transferMessage += "\n" + T("Плагин запросил отмену нового импорта. Проверь список модов в Penumbra.", "The plugin requested rollback of the new import. Check the mod list in Penumbra.");
                importingDirectory = "";
            }
        }
    }

    private void TransferFailure(string code)
    {
        // Only bounded machine codes, never exception paths or archive names, in technical logs.
        var safe = code.Length <= 80 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? code : "transfer_failed";
        Log.Warning("Mod transfer failed: code={Code}", safe);
        var detail = safe.StartsWith("package_") ? "package_validation" : safe.StartsWith("penumbra_") ? "package_import"
            : safe.Contains("hash") ? "package_integrity" : safe.Contains("busy") || safe.Contains("quota") ? "package_busy" : "package_transport";
        errorReports.Send("transfer_failed", detail, link?.Snapshot.State ?? "none", LinkFresh, LightlessLoaded(), "", null, null,
            new ReportContext("none", false, false, false, false, false, 0));
        transferMessage = T("Передача не завершена. Проверь соединение и пакет PMP; файл может быть повреждён, слишком большим или содержать неподдерживаемые данные.",
            "Transfer did not complete. Check the connection and PMP package; it may be damaged, too large or contain unsupported data.") + "\n" + safe;
        var reason = safe switch
        {
            "transfer_timeout" or "transfer_network" => T("Связь с сервером прервалась. Плагин повторит передачу до трёх раз; при повторном сбое отправьте мод заново.", "The relay connection was interrupted. The plugin retries up to three times; if it still fails, send the mod again."),
            "package_size" or "package_expansion" or "package_entries" => T("Превышен лимит размера или количества файлов. Экспортируй меньший пакет.", "The size or file-count limit was exceeded. Export a smaller package."),
            "package_path" or "package_link" or "package_file_type" => T("Архив содержит запрещённый путь, ссылку или тип файла. Он не установлен.", "The archive contains a forbidden path, link or file type. It was not installed."),
            "package_missing_file" or "package_metadata" or "package_truncated" => T("В пакете отсутствуют файлы или повреждены данные. Экспортируй PMP заново.", "The package has missing files or invalid data. Export the PMP again."),
            "transfer_hash" => T("Контрольная сумма не совпала. Пакет отклонён; отправь его заново.", "The checksum did not match. The package was rejected; send it again."),
            "transfer_expired" or "transfer_not_linked" => T("Связь с партнёром или срок передачи истекли. Переподключитесь и повторите отправку.", "The partner link or transfer expired. Reconnect and send again."),
            "transfer_busy" or "transfer_quota" => T("Уже есть передача или сервер достиг лимита. Заверши текущую передачу либо попробуй позже.", "A transfer is already active or server capacity is full. Finish the transfer or try later."),
            "transfer_http_404" => T("На сервере ещё нет этой функции. Нужна новая версия сервера.", "This feature is not available on the server yet. A server update is required."),
            "transfer_launch_unavailable" or "transfer_launch_incompatible" => T("Мод установлен, но анимация не запущена: эмоция не найдена, не разблокирована или варианты не подходят персонажу. Проверь её во вкладке «Эмоции».", "The mod is installed, but playback did not start: the emote is missing, locked or incompatible with your character. Check it on Emotes."),
            "penumbra_collection_changed" => T("Персонаж или коллекция изменились после принятия. Включение отменено. Попроси партнёра отправить пакет заново для нужного персонажа.", "Your character or collection changed after acceptance. Activation was cancelled. Ask your partner to send the package again for the intended character."),
            _ => "",
        };
        if (reason.Length > 0) transferMessage = reason + "\n" + safe;
    }

    private void DrawTransfers()
    {
        if (Environment.TickCount64 >= transferModsRefresh)
        {
            transferModsRefresh = Environment.TickCount64 + 5000;
            try { transferMods = getMods.Invoke(); }
            catch { transferMods = []; }
        }
        var busy = transfers?.Offer is not null || transfers?.ProposalPending == true || transferSending is not null;
        ImGui.BeginDisabled(busy);
        if (ImGui.Checkbox(T("Использовать готовый PMP", "Use an exported PMP"), ref transferManualPackage))
        {
            transferLaunchDraft = ""; transferSenderRole = transferReceiverRole = null;
            queuedTransferPair = null;
        }
        if (!transferManualPackage)
        {
            var selectedName = transferMods.GetValueOrDefault(transferSourceDirectory);
            if (ImGui.BeginCombo(T("Мод для передачи", "Mod to send"), selectedName ?? "—"))
            {
                ImGui.InputText(T("Поиск модов", "Search mods"), ref transferModSearch, 200);
                foreach (var mod in transferMods.OrderBy(m => m.Value, StringComparer.OrdinalIgnoreCase))
                {
                    if (!mod.Value.Contains(transferModSearch, StringComparison.OrdinalIgnoreCase)) continue;
                    if (ImGui.Selectable(mod.Value + "##send" + mod.Key, mod.Key == transferSourceDirectory))
                    {
                        transferSourceDirectory = mod.Key;
                        transferLaunchDraft = ""; transferSenderRole = transferReceiverRole = null;
                        queuedTransferPair = null; transferMessage = "";
                    }
                }
                ImGui.EndCombo();
            }
        }
        ImGui.EndDisabled();
        if (transferManualPackage)
        ImGui.TextWrapped(T("Экспортируй нужный мод в PMP через Penumbra и укажи файл ниже. До 32 МиБ; сервер удаляет пакет через 10 минут или при разрыве пары. Это передача копии мода, не синхронизация Lightless.",
            "Export the mod as a PMP in Penumbra and select its path below. Up to 32 MiB; the server removes it after 10 minutes or unlinking. This sends a copy, not Lightless synchronization."));
        if (config.TransferNoticeVersion != 1)
        {
            ImGui.TextWrapped(T("Отправляй только моды, которые имеешь право распространять. Сервер получит пакет и его название, а партнёр — копию после принятия. Проверка архива не гарантирует безопасность содержимого.",
                "Only send mods you are allowed to redistribute. The server receives the package and its name; your partner receives a copy after accepting. Archive checks do not guarantee content safety."));
            ImGui.Checkbox(T("Понимаю условия и беру ответственность за отправляемые моды", "I understand and take responsibility for the mods I send"), ref transferAcknowledged);
            ImGui.BeginDisabled(!transferAcknowledged);
            if (ImGui.Button(T("Подтвердить один раз", "Confirm once"))) { config.TransferNoticeVersion = 1; Save(); }
            ImGui.EndDisabled();
        }
        if (transferManualPackage) ImGui.InputText(T("Путь к PMP", "PMP path"), ref transferPath, 1024);
        else ImGui.TextWrapped(T("Пакет выбранного мода будет собран автоматически. Ручной экспорт не нужен.", "The selected mod will be packaged automatically. No manual export is needed."));
        if (transferLaunchDraft.Length > 0)
        {
            if (transferSenderRole is not null)
            {
                ImGui.TextWrapped(T("Твоя роль: ", "Your role: ") + transferSenderRole.Name);
                if (ImGui.BeginCombo(T("Роль партнёра", "Partner role"), transferReceiverRole?.Name ?? "—"))
                {
                    foreach (var mod in discovered.Where(m => m.Directory == transferSourceDirectory))
                        if (ImGui.Selectable(mod.EmoteName + "##direct" + mod.Command))
                        {
                            try { transferReceiverRole = CreateBookmark(mod, null); }
                            catch (Exception ex) { TransferFailure(ex.Message); }
                        }
                    foreach (var role in config.Bookmarks.Where(b => b.ModDirectory == transferSourceDirectory && OfferRole(b) != OfferRole(transferSenderRole)))
                        if (ImGui.Selectable(role.Name)) transferReceiverRole = SnapshotBookmark(role);
                    ImGui.EndCombo();
                }
                DrawTransferRoleOptions();
            }
            ImGui.TextWrapped(T("После установки: ", "After installation: ") + TransferLaunch.Decode(transferLaunchDraft)!.Label);
            if (ImGui.SmallButton(T("Без запуска", "Without playback"))) { transferLaunchDraft = ""; transferSenderRole = transferReceiverRole = null; }
        }
        ImGui.BeginDisabled(config.TransferNoticeVersion != 1 || transfers is null || busy || (transferManualPackage ? transferPath.Length == 0 : !transferMods.ContainsKey(transferSourceDirectory)) || (transferSenderRole is not null && (transferReceiverRole is null || OfferRole(transferSenderRole) == OfferRole(transferReceiverRole))));
        if (ImGui.Button(T("Предложить мод партнёру", "Offer mod to partner")))
        {
            var client = transfers!; var path = transferPath.Trim().Trim('"'); var launch = transferLaunchDraft;
            if (transferSenderRole is { } sender && transferReceiverRole is { } receiver)
            {
                var id = PairRules.Hash(Guid.NewGuid().ToString());
                var family = PairRules.Hash("transfer-pair:" + id);
                launch = new TransferLaunch(receiver.Name[..Math.Min(200, receiver.Name.Length)], receiver.Command, receiver.PoseIndex,
                    receiver.SavedOptions?.ToDictionary(p => p.Key, p => p.Value.ToList()) ?? [])
                { PairId = id, Family = family, SenderCommand = PairRules.Hash(sender.Command.ToLowerInvariant()), SenderRole = OfferRole(sender) }.Encode();
                queuedTransferPair = new(SnapshotBookmark(sender), id, family, transferPartner,
                    ClientState.TerritoryType, Environment.TickCount64 + 660000);
            }
            if (transferManualPackage) transferSending = Task.Run(() => client.Propose(path, launch));
            else
            {
                var root = getRoot.Invoke(); var directory = transferSourceDirectory;
                var packageName = ModPackage.DisplayFileName(transferMods[directory]);
                transferSending = Task.Run(async () => await client.ProposePackage(ModPackage.PackMod(root, directory), packageName, launch));
            }
        }
        ImGui.EndDisabled();
        if (transfers is null) ImGui.TextWrapped(T("Сначала установи связь с партнёром.", "Link with your partner first."));
        DrawTransferOffer();
        if (transferMessage.Length > 0) ImGui.TextWrapped(transferMessage);
    }

    private void DrawTransferOffer()
    {
        if (transfers?.Offer is not { } offer) return;
        if (LinkedPartner is { } partner) ImGui.TextUnformatted(partner.Name.TextValue);
        ImGui.TextUnformatted(offer.Name);
        ImGui.TextDisabled($"{offer.Size / 1048576.0:F1} MiB · {offer.Remaining}s");
        if (offer.Incoming && offer.State == "offered")
        {
            try
            {
                if (Objects.LocalPlayer is { } player)
                {
                    var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
                    if (valid) ImGui.TextWrapped(string.Format(T("Коллекция: {0}", "Collection: {0}"), collection.Name));
                }
            }
            catch { /* Acceptance rechecks availability before authorizing a download. */ }
            TransferLaunch? launch;
            try { launch = TransferLaunch.Decode(offer.Launch); }
            catch
            {
                ImGui.TextWrapped(T("Предложение запуска повреждено. Отклони пакет и попроси отправить его заново.", "The playback offer is invalid. Decline the package and ask for a new offer."));
                if (ImGui.Button(T("Отклонить", "Decline"))) transfers.Act("reject", offer.Id);
                return;
            }
            if (config.TransferReceiveNoticeVersion != 1)
                ImGui.TextWrapped(T("Принимая мод, ты берёшь на себя риск использования файлов от отправителя. Мод может вызывать ошибки игры или нежелательные изменения; проверка архива не гарантирует безопасность. Доверяй только знакомым отправителям. Кнопка ниже один раз подтверждает это предупреждение и разрешает скачать, установить и включить именно этот пакет.",
                    "By accepting a mod, you take responsibility for using files supplied by its sender. A mod may cause game errors or unwanted changes; archive checks cannot guarantee safety. Only trust senders you know. The button below acknowledges this notice once and authorizes downloading, installing and enabling this package."));
            ImGui.TextWrapped(T("Пакет будет проверен, установлен отдельно и включён в текущей коллекции Penumbra. Это затронет всех персонажей с этой коллекцией. Мод может сразу изменить внешность, idle, звук и эффекты; команда анимации запускается только при отдельном предложении ниже.",
                "The package will be checked, installed separately and enabled in your current Penumbra collection, affecting all characters using it. Appearance, idle, sound and effects may change immediately; an emote command runs only with the playback offer below."));
            if (launch is not null)
            {
                ImGui.TextWrapped(T("После установки: ", "After installation: ") + launch.Label + " · " + launch.Command);
                if (launch.Pose is { } pose) ImGui.TextUnformatted(T("Слот: ", "Slot: ") + pose.ToString("00"));
                foreach (var option in launch.Options) ImGui.TextWrapped(option.Key + ": " + string.Join(", ", option.Value));
                ImGui.TextWrapped(launch.PairId.Length > 0
                    ? T("Принятие разрешает установку, включение мода, выравнивание и совместный запуск указанной роли с отправителем.", "Accepting authorizes installation, activation, alignment and a shared start of the displayed role with the sender.")
                    : T("Кнопка разрешает также запуск этой анимации у тебя после установки. Это не синхронный запуск пары.", "The button also authorizes playing this animation on your character after installation. This is not a synchronized pair launch."));
            }
            if (ImGui.Button(launch is null ? T("Принять, установить и включить", "Accept, install and enable") : T("Принять, установить и запустить", "Accept, install and play"))) AcceptTransfer(offer);
            ImGui.SameLine();
            if (ImGui.Button(T("Отклонить", "Decline"))) transfers.Act("reject", offer.Id);
        }
        else
        {
            if (transfers.Error.Length == 0)
                ImGui.TextUnformatted(offer.State == "offered" ? T("Ожидаем принятия", "Waiting for acceptance") : T("Передача и проверка…", "Transferring and checking…"));
            if (ImGui.Button(T("Отмена", "Cancel"))) transfers.Act(offer.Incoming ? "reject" : "cancel", offer.Id);
        }
    }

    private void AcceptTransfer(PackageOffer offer)
    {
        try
        {
            if (transfers is null || !IntegrationLoaded("Penumbra") || Objects.LocalPlayer is not { } player)
                throw new InvalidOperationException("penumbra_unavailable");
            var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
            if (!valid || collection.Id == Guid.Empty) throw new InvalidOperationException("penumbra_collection_changed");
            transferAcceptedClient = transfers;
            transferAcceptCollection = collection.Id;
            transferAcceptIdentity = LinkIdentity(player);
            transferAcceptOffer = offer.Id;
            acceptedTransferLaunch = TransferLaunch.Decode(offer.Launch);
            config.TransferReceiveNoticeVersion = 1;
            Save();
            transfers.Accept(offer.Id);
        }
        catch (Exception ex) { TransferFailure(ex.Message); }
    }

    private void DrawTransferNotice()
    {
        if (transfers?.Offer is not { Incoming: true, State: "offered" }) return;
        ImGui.SetNextWindowSize(new Vector2(380, 0), ImGuiCond.FirstUseEver);
        if (config.PairNoticeX >= 0 && config.PairNoticeY >= 0)
            ImGui.SetNextWindowPos(new Vector2(config.PairNoticeX, config.PairNoticeY), ImGuiCond.Appearing);
        if (ImGui.Begin(T("Получение мода", "Incoming mod") + "###transferNotice", ImGuiWindowFlags.AlwaysAutoResize)) DrawTransferOffer();
        ImGui.End();
    }

    private void PrepareTransferLaunch(Bookmark bookmark)
    {
        transferSenderRole = SnapshotBookmark(bookmark);
        transferReceiverRole = config.Bookmarks.Where(b => b.ModDirectory == bookmark.ModDirectory && OfferRole(b) != OfferRole(bookmark))
            .Select(SnapshotBookmark).FirstOrDefault();
        if (transferReceiverRole is null)
        {
            var other = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory && m.Command != bookmark.Command)
                ?? discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory);
            if (other is not null) transferReceiverRole = CreateBookmark(other, null);
        }
        transferSourceDirectory = bookmark.ModDirectory;
        transferManualPackage = false;
        transferLaunchDraft = new TransferLaunch(bookmark.Name[..Math.Min(200, bookmark.Name.Length)], bookmark.Command,
            bookmark.PoseIndex, bookmark.SavedOptions?.ToDictionary(p => p.Key, p => p.Value.ToList()) ?? []).Encode();
        selectTransfersTab = true;
    }
}
