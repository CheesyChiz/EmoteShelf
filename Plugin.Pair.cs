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
    private bool pairPreparing, pairStartedPreparation, pairAlignStarted, pairPopup;
    private Vector3 pairOrigin, pairTargetOrigin;
    private float pairFacing;
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
        CancelPair();
        pairPopup = true;
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
            pairDeadline = Environment.TickCount64 + 60000;
            pairSession = new(pairSelf, pairOther, PairRules.Family(mod.Name), config.PairAlign);
            pairMessage = T("Ожидаю, пока партнёр выберет свою роль и нажмёт «В паре».", "Waiting for your partner to choose their role and click Pair.");
        }
        catch (Exception ex) { CancelPair(ex.Message); }
    }

    private void BeginPair(EmoteMod mod)
    {
        try { BeginPair(CreateBookmark(mod, null)); }
        catch (Exception ex) { pairPopup = true; CancelPair(ex.Message); }
    }

    private void CancelPair(string? reason = null)
    {
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
        if (reason is not null) { pairMessage = reason; status = reason; }
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
                throw new InvalidOperationException(T("Пара отменена: таргет, персонаж или зона изменились.", "Pair cancelled: target, character or zone changed."));
            if (!LightlessHandles(target.Address)) throw new InvalidOperationException(T("Lightless больше не обрабатывает партнёра; запуск отменён.", "Lightless no longer handles the partner; launch cancelled."));
            if (!CanPair(local) || (!CanPair(target) && !PairRules.PeerMayHaveStarted(snapshot.State, snapshot.StartAt, now)) ||
                target.IsDead || !PairRules.Nearby(local.Position, target.Position))
                throw new InvalidOperationException(T("Пара отменена: движение, состояние или расстояние не подходят.", "Pair cancelled: character state or distance is unsuitable."));
            if (pairBookmark is not null && IsPoseCommand(pairBookmark.Command) && !config.AutomaticPose)
                throw new InvalidOperationException(T("Автовыбор поз отключён; общий запуск отменён.", "Automatic poses disabled; pair launch cancelled."));
            if (snapshot.State == "error") throw new InvalidOperationException(snapshot.Error);
            if (now - snapshot.ReceivedAt > 4000) throw new InvalidOperationException(T("Сервер не отвечает; запуск отменён.", "Relay stopped responding; launch cancelled."));
            if (pairAlignment?.Error.Length > 0 && pairAlignStarted) throw new InvalidOperationException(pairAlignment.Error);
            var mayApproach = snapshot.Align && !snapshot.Anchor && pairAlignStarted && !pairStartedPreparation;
            if (!mayApproach && (Vector3.Distance(local.Position, pairOrigin) > .08f ||
                    MathF.Abs(MathF.IEEERemainder(local.Rotation - pairFacing, MathF.Tau)) > .08f))
                throw new InvalidOperationException(T("Пара отменена: ты начал двигаться или поворачиваться.", "Pair cancelled: you moved or turned."));
            if (snapshot.State is "connecting" or "waiting") return;
            if (snapshot.Align && !snapshot.Anchor && !pairStartedPreparation)
            {
                if (Vector3.Distance(target.Position, pairTargetOrigin) > .08f)
                    throw new InvalidOperationException(T("Партнёр сдвинулся; повторите выравнивание.", "Your partner moved; retry alignment."));
                if (!pairAlignStarted)
                {
                    pairAlignment ??= new PairAlignment(Interop, Objects);
                    pairAlignment.Begin(target.Position, target.Rotation);
                    pairAlignStarted = true;
                    pairMessage = T("Подхожу к партнёру. Любое ручное движение отменит запуск.", "Approaching partner. Movement input cancels the launch.");
                    return;
                }
                if (!pairAlignment!.Arrived) return;
                pairOrigin = local.Position;
                pairFacing = local.Rotation;
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
                if (!pairPreparing) pairMessage = T("Роль готова. Не двигайся; ожидаю готовность партнёра…", "Role prepared. Stay still; waiting for your partner…");
                return;
            }
            if (pairHeldCommand.Length == 0) throw new InvalidOperationException("No locally prepared action.");
            pairMessage = string.Format(T("Общий запуск через {0:0.0} с", "Shared start in {0:0.0}s"), Math.Max(0, snapshot.StartAt - now) / 1000f);
            if (now < snapshot.StartAt) return;
            if (now - snapshot.StartAt > 250 || now - snapshot.ReceivedAt > 650)
                throw new InvalidOperationException(T("Пропущено время старта или потеряна связь. Повторите запуск.", "Start was missed or connection lost. Please retry."));
            ValidatePairSelection();
            DispatchPreparedCommand(pairHeldCommand);
            pairSession.Complete();
            pairSession = null;
            pairHeldCommand = "";
            pairBookmark = null;
            pairPreparing = pairStartedPreparation = pairAlignStarted = false;
            ClearPendingValidation();
            pairMessage = T("Команды запущены по общему таймеру. Совпадение фазы sit/idle пока не гарантируется.", "Commands launched on the shared timer. Matching sit/idle phase is not guaranteed yet.");
            status = pairMessage;
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

    private void DrawPairSettings()
    {
        if (ImGui.Checkbox(T("Запуск в паре — экспериментально", "Pair launch — experimental"), ref config.PairEnabled)) { StopLink(); Save(); }
        if (!config.PairEnabled) return;
        DrawLinkSettings();
        ImGui.TextWrapped(LightlessLoaded() ? T("Lightless Sync загружен. Перед запуском дождитесь завершения синхронизации у обоих.", "Lightless Sync is loaded. Wait for synchronization to finish on both sides before launch.") :
            T("Требуется Lightless Sync у обоих участников. Одиночные эмоции его не требуют.", "Lightless Sync is required for both participants. Solo emotes do not require it."));
        ImGui.TextWrapped(T("После принятия приглашения каждый выбирает свою роль одного мода и нажимает «В паре» или Shift+иконку. Таргет держать не нужно. Используется сервер Emote Shelf: он видит IP и хеши персонажей/мода; личность игрового персонажа не проверяется сервером. Только с доверенным партнёром.",
            "After accepting an invitation, each player chooses their role of the same mod and clicks Pair or Shift+icon. Keeping a target is not required. The Emote Shelf relay sees IP addresses and character/mod hashes; game identity is not authenticated. Use with trusted partners only."));
        if (ImGui.Checkbox(T("Разрешить короткий подход и выравнивание перед запуском", "Allow a short approach and alignment before launch"), ref config.PairAlign)) { CancelPair(); Save(); }
        ImGui.TextWrapped(T("Выравнивание — только с разрешения обоих, до 2 ялмов, без телепортации. Один стоит, второй подходит. Это общий запуск команд, не точная синхронизация кадров; sit/idle особенно требуют проверки.",
            "Alignment requires both players to opt in, within 2 yalms, without teleporting. One stands still, the other approaches. This is a shared command start, not frame-accurate synchronization; sit/idle particularly need testing."));
        ImGui.TextWrapped(T("Моды и внешний вид передаёт Lightless, не наш сервер. Проверяем, что Lightless обрабатывает партнёра, но его API не подтверждает завершение загрузки конкретной анимации.",
            "Lightless transfers mods/appearance, not our relay. We check that it handles the partner, but its API does not confirm that this animation finished downloading."));
    }

    private void DrawPairWindow()
    {
        if (!pairPopup) return;
        ImGui.SetNextWindowSize(new Vector2(440, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin(T("Запуск в паре", "Pair launch") + "##pair", ref pairPopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 420);
            ImGui.TextUnformatted(pairTargetName);
            ImGui.TextWrapped(pairMessage);
            ImGui.TextWrapped(T("Дождитесь синхронизации в Lightless. Наличие партнёра в API не подтверждает загрузку новой анимации.",
                "Wait for Lightless synchronization. An actor in its API does not confirm new animation assets have loaded."));
            if (pairCollectionWarning.Length > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, .75f, .35f, 1));
                ImGui.TextWrapped(pairCollectionWarning);
                ImGui.PopStyleColor();
            }
            if (pairSession is not null && ImGui.Button(T("Отменить", "Cancel"))) CancelPair(T("Запуск отменён.", "Launch cancelled."));
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
        if (!pairPopup) CancelPair();
    }
}
