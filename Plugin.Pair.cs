using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.IoC;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Penumbra.Api.Enums;

namespace EmoteShelf;

public sealed partial class Plugin
{
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static ICondition Conditions { get; set; } = null!;
    [PluginService] private static IGameInteropProvider Interop { get; set; } = null!;
    private PairClient? pairSession;
    private PairAlignment? pairAlignment;
    private Bookmark? pairBookmark;
    private ulong pairTargetId;
    private string pairSelf = "", pairOther = "", pairHeldCommand = "", pairMessage = "", pairTargetName = "";
    private bool pairPreparing, pairStartedPreparation, pairAlignStarted;
    private Vector3 pairOrigin, pairTargetOrigin;
    private float pairFacing;
    private float pairTargetFacing;
    private bool pairUntargetedPlayback;
    private int pairStableFrames;
    private bool pairReadySent;
    private long pairConfirmUntil;
    private long pairDeadline;
    private string pairCollectionWarning = "";
    private long lightlessCheckAt;
    private bool lightlessLoaded;
    private long lightlessActorCheckAt;
    private nint lightlessActor;
    private bool lightlessActorHandled;

    private bool LightlessLoaded()
    {
        if (Environment.TickCount64 >= lightlessCheckAt)
        {
            lightlessCheckAt = Environment.TickCount64 + 1000;
            lightlessLoaded = Pi.InstalledPlugins.Any(p => p.IsLoaded && p.InternalName == "LightlessSync");
        }
        return lightlessLoaded;
    }

    private bool LightlessHandles(nint address)
    {
        if (!LightlessLoaded()) return false;
        if (lightlessActor != address || Environment.TickCount64 >= lightlessActorCheckAt)
        {
            lightlessActor = address;
            lightlessActorCheckAt = Environment.TickCount64 + 250;
            try { lightlessActorHandled = Pi.GetIpcSubscriber<List<nint>>("LightlessSync.GetHandledAddresses").InvokeFunc().Contains(address); }
            catch { lightlessActorHandled = false; }
        }
        return lightlessActorHandled;
    }

    private string PairIdentity(IPlayerCharacter player)
        => PairRules.Identity(player.Name.TextValue, player.HomeWorld.RowId, player.CurrentWorld.RowId, ClientState.TerritoryType);

    private unsafe bool CanPair(IPlayerCharacter player)
        => !player.IsDead && !player.IsCasting && !Conditions[ConditionFlag.InCombat] &&
           !Conditions[ConditionFlag.Mounted] && !Conditions[ConditionFlag.BetweenAreas] &&
           !Conditions[ConditionFlag.BetweenAreas51] && !Conditions[ConditionFlag.Jumping] &&
           ((Character*)player.Address)->Mode == CharacterModes.Normal;

    private void BeginPair(Bookmark bookmark)
    {
        if (link?.Snapshot.Launch is not null)
        {
            linkNotice = T("Уже есть предложение анимации. Прими или отклони его в уведомлении.", "An animation proposal is already pending. Accept or decline it in the notification.");
            linkNoticeUntil = Environment.TickCount64 + 5000;
            return;
        }
        var id = PairRules.Hash(Guid.NewGuid().ToString());
        BeginPairCore(bookmark, id);
        if (pairSession is not null && pairBookmark is not null)
        {
            var mod = discovered.First(m => m.Directory == bookmark.ModDirectory && m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase));
            link!.Act("propose", offer: new LaunchOffer(id, PairRules.Family(mod.Name), PairRules.Hash(bookmark.Command.ToLowerInvariant()), OfferRole(bookmark)));
        }
    }

    private void BeginPairCore(Bookmark bookmark, string offerId)
    {
        CancelPair();
        pairCollectionWarning = "";
        try
        {
            if (!config.PairEnabled) throw new InvalidOperationException(T("Включи экспериментальный запуск в паре в настройках.", "Enable experimental pair launch in Settings."));
            if (!LightlessLoaded()) throw new InvalidOperationException(T("Для запуска в паре нужен загруженный Lightless Sync. Обычные эмоции работают без него.", "Pair launch requires loaded Lightless Sync. Solo playback does not."));
            if (Objects.LocalPlayer is not { } local || LinkedPartner is not { } target || target.GameObjectId == local.GameObjectId)
                throw new InvalidOperationException(T("Сначала пригласи партнёра через контекстное меню и дождись принятия. Партнёр должен быть рядом.", "Invite a partner through the context menu and wait for acceptance. Your partner must be nearby."));
            if (!LightlessHandles(target.Address)) throw new InvalidOperationException(T("Lightless не сообщает, что обрабатывает выбранного партнёра. Сначала установите синхронизацию в Lightless.", "Lightless does not report handling this partner. Set up synchronization in Lightless first."));
            if (!CanPair(local) || !CanPair(target))
                throw new InvalidOperationException(T("Сначала оба встаньте и остановите эмоции. Запуск в паре недоступен в бою, на маунте или при касте.", "Both players must stand and stop their emotes first. Pair launch is unavailable in combat, mounted or while casting."));
            if (!PairRules.Nearby(local.Position, target.Position))
                throw new InvalidOperationException(T("Подойдите ближе: максимум 2 ялма, на одном уровне.", "Move closer: within 2 yalms and on the same level."));
            if (IsPoseCommand(bookmark.Command) && !config.AutomaticPose)
                throw new InvalidOperationException(T("Для общего запуска sit/idle нужен экспериментальный автовыбор поз. Ручной режим остаётся ручным.", "Pair launch of sit/idle requires experimental automatic pose selection. Manual mode remains manual."));
            if (!ModScanner.ValidCommand(bookmark.Command) || catalog?.IsUnlocked(bookmark.Command, Unlocks) == false)
                throw new InvalidOperationException(T("Эмоция недоступна.", "Emote is unavailable."));
            var mod = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory && m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(T("Обнови список модов.", "Refresh the mod list."));
            var (selfValid, _, selfCollection) = getCollection.Invoke(local.ObjectIndex);
            var (targetValid, _, targetCollection) = getCollection.Invoke(target.ObjectIndex);
            if (selfValid && targetValid && selfCollection.Id == targetCollection.Id)
                pairCollectionWarning = T("У тебя и партнёра одна коллекция Penumbra: выбранные настройки могут менять обоих. Для разных ролей нужны корректно настроенные коллекции/синхронизация модов.",
                    "You and your partner share a Penumbra collection: selected options may affect both. Different roles need correctly configured collections/mod synchronization.");
            // Freeze this client's role/options; no network response can replace them.
            pairBookmark = new Bookmark { ModDirectory = bookmark.ModDirectory, Name = bookmark.Name, Command = bookmark.Command,
                IconId = bookmark.IconId, PoseIndex = bookmark.PoseIndex,
                SavedOptions = bookmark.SavedOptions?.ToDictionary(x => x.Key, x => x.Value.ToList()) };
            pendingCommand = "";
            pendingPoseIndex = -1;
            waitingForStand = null;
            manualPoseBookmark = null;
            pairSelf = PairIdentity(local);
            pairOther = PairIdentity(target);
            pairTargetId = target.GameObjectId;
            pairTargetName = target.Name.TextValue;
            pairOrigin = local.Position;
            pairFacing = local.Rotation;
            pairTargetOrigin = target.Position;
            pairTargetFacing = target.Rotation;
            pairDeadline = Environment.TickCount64 + 60000;
            pairOfferId = offerId;
            pairOfferSeen = false;
            pairOfferQueuedAt = Environment.TickCount64;
            pairSession = new(pairSelf, pairOther, PairRules.Hash(PairRules.Family(mod.Name) + offerId), true);
            pairMessage = T("Отправлено предложение анимации. Партнёр выбирает роль в уведомлении и подтверждает запуск.", "Animation proposed. Your partner chooses a role in the notification and accepts the launch.");
        }
        catch (Exception ex) { CancelPair(ex.Message); }
    }

    private void BeginPair(EmoteMod mod)
    {
        try { BeginPair(CreateBookmark(mod, null)); }
        catch (Exception ex) { CancelPair(ex.Message); }
    }

    private void CancelPair(string? reason = null)
    {
        if (pairSession is not null) LogPairDiagnostics(reason ?? "Local cancellation");
        pairUntargetedPlayback = false;
        if (pairOfferId.Length > 0) link?.Act("cancel_launch", invitation: pairOfferId);
        pairOfferId = "";
        if (pairSession is not null)
        {
            reason ??= T("Запуск в паре отменён.", "Pair launch cancelled.");
            pairSession.Dispose();
            pendingCommand = "";
            pendingPoseIndex = -1;
            waitingForStand = null;
            ClearPendingValidation();
        }
        pairSession = null;
        pairAlignment?.Cancel();
        pairBookmark = null;
        pairHeldCommand = "";
        pairPreparing = pairStartedPreparation = pairAlignStarted = false;
        pairReadySent = false;
        pairStableFrames = 0;
        if (reason is not null) { pairMessage = reason; status = reason; pairNoticeUntil = Environment.TickCount64 + 8000; }
    }

    private void LogPairDiagnostics(string outcome)
    {
        var now = Environment.TickCount64;
        var snapshot = pairSession?.Snapshot;
        var local = Objects.LocalPlayer;
        var peer = LinkedPartner;
        var geometry = local is not null && peer is not null
            ? FormattableString.Invariant($"distance={Vector3.Distance(local.Position, peer.Position):F5} angleRad={PairRules.AngleDistance(local.Rotation, peer.Rotation):F5} localCanPair={CanPair(local)} peerCanPair={CanPair(peer)}")
            : "actor unavailable";
        // The proposal identifier is not a bearer credential. Its short prefix correlates both clients.
        var attempt = pairOfferId.Length >= 8 ? pairOfferId[..8] : "none";
        Log.Information($"Pair diagnostic v{typeof(Plugin).Assembly.GetName().Version} attempt={attempt} outcome={outcome}; state={snapshot?.State} anchor={snapshot?.Anchor} ageMs={now - (snapshot?.ReceivedAt ?? now)} alignStarted={pairAlignStarted} preparationStarted={pairStartedPreparation} preparing={pairPreparing} ready={pairReadySent} stable={pairStableFrames}; {geometry}; correction={(pairAlignStarted ? pairAlignment?.Diagnostics : "not started")}");
    }

    private void UpdatePair()
    {
        if (pairSession is null) return;
        try
        {
            var now = Environment.TickCount64;
            var snapshot = pairSession.Snapshot;
            if (!config.PairEnabled || now >= pairDeadline) throw new InvalidOperationException(T("Ожидание пары отменено или истекло.", "Pair launch cancelled or timed out."));
            if (Objects.LocalPlayer is not { } local || LinkedPartner is not { } target ||
                target.GameObjectId != pairTargetId ||
                PairIdentity(local) != pairSelf || PairIdentity(target) != pairOther)
                throw new InvalidOperationException(T("Пара отменена: подключённый партнёр не найден рядом, сменился персонаж или зона. Таргет не требуется.", "Pair cancelled: linked partner is not visible, or character/world/zone changed. A target is not required."));
            if (!LightlessHandles(target.Address)) throw new InvalidOperationException(T("Lightless больше не обрабатывает партнёра; запуск отменён.", "Lightless no longer handles the partner; launch cancelled."));
            if (!CanPair(local) || (!CanPair(target) && !PairRules.PeerMayHaveStarted(snapshot.State, snapshot.StartAt, now)) ||
                target.IsDead || !PairRules.Nearby(local.Position, target.Position))
                throw new InvalidOperationException(T("Пара отменена: движение, состояние или расстояние не подходят.", "Pair cancelled: character state or distance is unsuitable."));
            if (pairBookmark is not null && IsPoseCommand(pairBookmark.Command) && !config.AutomaticPose)
                throw new InvalidOperationException(T("Автовыбор поз отключён; общий запуск отменён.", "Automatic poses disabled; pair launch cancelled."));
            if (snapshot.State == "error") throw new InvalidOperationException(snapshot.Error);
            if (now - snapshot.ReceivedAt > 4000) throw new InvalidOperationException(T("Сервер не отвечает; запуск отменён.", "Relay stopped responding; launch cancelled."));
            var proposal = link?.Snapshot.Launch;
            if (proposal?.Offer.Id == pairOfferId) pairOfferSeen = true;
            else if (pairOfferSeen || now - pairOfferQueuedAt > 5000)
                throw new InvalidOperationException(T("Предложение отклонено, отменено или истекло.", "Animation proposal declined, cancelled or expired."));
            if (proposal?.Offer.Id != pairOfferId || proposal.Status != "accepted") return;
            if (pairAlignment?.Error.Length > 0 && pairAlignStarted) throw new InvalidOperationException(pairAlignment.Error);
            var mayApproach = snapshot.Align && !snapshot.Anchor && pairAlignStarted && !pairStartedPreparation;
            if (!mayApproach && (Vector3.Distance(local.Position, pairOrigin) > .08f ||
                    PairRules.AngleDistance(local.Rotation, pairFacing) > .08f))
                throw new InvalidOperationException(T("Пара отменена: ты начал двигаться или поворачиваться.", "Pair cancelled: you moved or turned."));
            if (snapshot.State is "connecting" or "waiting") return;
            if (!snapshot.Align) throw new InvalidOperationException(T("Партнёр не подтвердил обязательное выравнивание. Обновите плагин у обоих.", "Partner did not enable required alignment. Update both plugins."));
            if (snapshot.Align && !snapshot.Anchor && !pairStartedPreparation)
            {
                if (Vector3.Distance(target.Position, pairTargetOrigin) > .08f || PairRules.AngleDistance(target.Rotation, pairTargetFacing) > .08f)
                    throw new InvalidOperationException(T("Партнёр сдвинулся; повторите выравнивание.", "Your partner moved; retry alignment."));
                if (!pairAlignStarted)
                {
                    pairAlignment ??= new PairAlignment(Interop, Objects);
                    pairAlignment.Begin(pairTargetOrigin, pairTargetFacing);
                    pairAlignStarted = true;
                    pairMessage = T("Подхожу к партнёру. Любое ручное движение отменит запуск.", "Approaching partner. Movement input cancels the launch.");
                    return;
                }
                if (!pairAlignment!.Arrived) return;
                pairOrigin = local.Position;
                pairFacing = pairTargetFacing;
            }
            if (!pairStartedPreparation)
            {
                pairStartedPreparation = pairPreparing = true;
                pairMessage = T("Подготавливаю выбранную роль…", "Preparing your selected role…");
                Play(pairBookmark!, true);
                if (pairSession is not null && pendingCommand.Length == 0 && waitingForStand is null)
                    throw new InvalidOperationException(status);
                return;
            }
            if (snapshot.State != "scheduled")
            {
                if (!pairPreparing && pairHeldCommand.Length > 0 && !pairReadySent)
                {
                    if (now > pairConfirmUntil) throw new InvalidOperationException(T("Координаты и поворот не совпали после подготовки. Запуск отменён.", "Position/facing did not match after preparation. Launch cancelled."));
                    pairStableFrames = PairRules.Aligned(local.Position, local.Rotation, target.Position, target.Rotation) ? pairStableFrames + 1 : 0;
                    if (pairStableFrames >= 3) { pairSession.Prepared(); pairReadySent = true; }
                    else pairMessage = T("Проверяю совпадение координат и поворота после подготовки…", "Confirming position and facing after preparation…");
                }
                if (pairReadySent) pairMessage = T("Выравнивание подтверждено. Ожидаю готовность партнёра…", "Alignment confirmed. Waiting for your partner…");
                return;
            }
            if (pairHeldCommand.Length == 0) throw new InvalidOperationException("No locally prepared action.");
            pairMessage = string.Format(T("Общий запуск через {0:0.0} с", "Shared start in {0:0.0}s"), Math.Max(0, snapshot.StartAt - now) / 1000f);
            if (now < snapshot.StartAt) return;
            if (now - snapshot.StartAt > 250 || now - snapshot.ReceivedAt > 650)
                throw new InvalidOperationException(T("Пропущено время старта или потеряна связь. Повторите запуск.", "Start was missed or connection lost. Please retry."));
            ValidatePairSelection();
            if (!PairRules.Aligned(local.Position, local.Rotation, target.Position, target.Rotation))
                throw new InvalidOperationException(T("Перед стартом координаты или поворот разошлись. Запуск отменён.", "Position or facing diverged before start. Launch cancelled."));
            if (snapshot.Align) ApplyPairFacing(pairFacing);
            pairUntargetedPlayback = true;
            DispatchPreparedCommand(pairHeldCommand);
            LogPairDiagnostics("Dispatched");
            pairSession.Complete();
            link?.Act("finish_launch", invitation: pairOfferId);
            pairOfferId = "";
            pairSession = null;
            pairHeldCommand = "";
            pairBookmark = null;
            pairPreparing = pairStartedPreparation = pairAlignStarted = false;
            ClearPendingValidation();
            pairMessage = T("Команды запущены по общему таймеру. Совпадение фазы sit/idle пока не гарантируется.", "Commands launched on the shared timer. Matching sit/idle phase is not guaranteed yet.");
            status = pairMessage;
            pairNoticeUntil = Environment.TickCount64 + 5000;
        }
        catch (Exception ex) { CancelPair(ex.Message); Log.Warning(ex, "Pair launch cancelled"); }
    }

    private void ValidatePairSelection()
    {
        var (ec, current) = getSettingsWithTemp.Invoke(pendingCollection, pendingModDirectory, "", false, false, 0);
        if (ec != PenumbraApiEc.Success || current is null || !current.Value.Item1 || current.Value.Item2 != pendingPriority ||
            pendingOptions is null || pendingOptions.Any(p => !current.Value.Item3.TryGetValue(p.Key, out var actual) ||
                !p.Value.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(actual.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Prepared Penumbra options changed; pair cancelled.");
        foreach (var path in pendingExpectedPaths)
            if (resolvePath.Invoke(pendingCollection, path, out var effective) != PenumbraApiEc.Success || !ResolvedAnimation.BelongsToMod(pendingExpectedModRoot, effective))
                throw new InvalidOperationException("Prepared animation was replaced; pair cancelled.");
    }

    private unsafe void ApplyPairFacing(float rotation)
    {
        if (!float.IsFinite(rotation) || Objects.LocalPlayer is not { } local)
            throw new InvalidOperationException("Pair facing unavailable.");
        ((Character*)local.Address)->GameObject.SetRotation(rotation);
    }

    private unsafe void ExecutePairEmote(string command)
    {
        var id = catalog?.IdFor(command) ?? 0;
        if (id == 0 || id > ushort.MaxValue || catalog?.IsUnlocked(command == "/cpose" ? "/changepose" : command, Unlocks) != true)
            throw new InvalidOperationException(T("Эмоция недоступна для запуска без цели.", "Emote unavailable for untargeted pair playback."));
        var manager = FFXIVClientStructs.FFXIV.Client.Game.Control.EmoteManager.Instance();
        var table = FFXIVClientStructs.FFXIV.Client.Game.Control.EmoteController.PlayEmoteOption.StaticVirtualTablePointer;
        if (manager == null || table == null || !manager->CanExecuteEmote((ushort)id))
            throw new InvalidOperationException(T("Игра пока не разрешает эту эмоцию. Повторите запуск стоя и без другой эмоции.", "The game cannot execute this emote yet. Retry standing without another emote."));
        // Explicit no-target option, including a valid vtable. Do not clear/change
        // the user's target, global auto-face preference, or another actor's rotation.
        var option = new FFXIVClientStructs.FFXIV.Client.Game.Control.EmoteController.PlayEmoteOption
        { VirtualTable = table, TargetId = 0xE0000000UL };
        if (!manager->ExecuteEmote((ushort)id, &option))
            throw new InvalidOperationException(T("Игра отклонила парную эмоцию.", "The game rejected the pair emote."));
    }

    private void DrawPairSettings()
    {
        if (ImGui.Checkbox(T("Запуск в паре — экспериментально", "Pair launch — experimental"), ref config.PairEnabled)) { StopLink(); Save(); }
        if (!config.PairEnabled) return;
        DrawLinkSettings();
        ImGui.TextWrapped(LightlessLoaded() ? T("Lightless Sync загружен. Перед запуском дождитесь завершения синхронизации у обоих.", "Lightless Sync is loaded. Wait for synchronization to finish on both sides before launch.") :
            T("Требуется Lightless Sync у обоих участников. Одиночные эмоции его не требуют.", "Lightless Sync is required for both participants. Solo emotes do not require it."));
        ImGui.TextWrapped(T("После установления связи «В паре» или Shift+иконка отправляет предложение анимации. Партнёр выбирает свою роль в уведомлении и принимает запуск либо отказывается. Таргет держать не нужно. Сервер Emote Shelf видит IP и хеши персонажей/мода/выбора; личность персонажа не проверяется. Только с доверенным партнёром.",
            "Once linked, Pair or Shift+icon proposes an animation. Your partner selects their role in the notification and accepts or declines. Keeping a target is not required. The Emote Shelf relay sees IP addresses and character/mod/selection hashes; character identity is not authenticated. Use with trusted partners only."));
        ImGui.TextWrapped(T("Выравнивание обязательно: короткий подход, точная доводка координат и угла, проверка перед запуском.", "Alignment is required: short approach, fine position/facing correction, and confirmation before launch."));
        ImGui.TextWrapped(T("До 2 ялмов. Один стоит, второй подходит; финальная коррекция позиции — не более 0.05 ялма. Принятие запуска разрешает выравнивание. При ошибке эмоция не запускается. Рост и масштаб не меняются.",
            "Within 2 yalms. One stands, the other approaches; final position correction is at most 0.05 yalms. Accepting a launch authorizes alignment. Failure cancels playback. Height and scale are unchanged."));
        ImGui.TextWrapped(T("Моды и внешний вид передаёт Lightless, не наш сервер. Проверяем, что Lightless обрабатывает партнёра, но его API не подтверждает завершение загрузки конкретной анимации.",
            "Lightless transfers mods/appearance, not our relay. We check that it handles the partner, but its API does not confirm that this animation finished downloading."));
    }

}
