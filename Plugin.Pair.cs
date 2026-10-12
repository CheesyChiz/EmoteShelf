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
    private bool pairDance;
    private bool pairStationary;
    private Vector3 PairDestination(Vector3 anchor, float yaw) => pairDance ? PairRules.DancePosition(anchor, yaw) : anchor;
    private Vector3 PairDestinationOnGround(Vector3 anchor, float yaw, Vector3 actor)
        => PairRules.GroundedDestination(PairDestination(anchor, yaw), actor, pairDance);
    private bool PairGeometryReady(IPlayerCharacter local, IPlayerCharacter peer)
        => pairStationary || (string.CompareOrdinal(pairSelf, pairOther) < 0
            ? PairRules.ReadyToLaunch(PairDestinationOnGround(local.Position, local.Rotation, peer.Position), local.Rotation, peer.Position, peer.Rotation)
            : PairRules.ReadyToLaunch(local.Position, local.Rotation, PairDestinationOnGround(peer.Position, peer.Rotation, local.Position), peer.Rotation));
    private int pairStableFrames;
    private bool pairReadySent;
    private string lastLaunchFailure = "";

    private string ExplainPairFailure(string reason)
    {
        var advice = pairCheckpoint switch
        {
            "alignment" or "post_prepare_alignment" or "final_alignment" => T(
                "Не подтверждено выравнивание. Встаньте рядом на ровном месте и не двигайтесь до запуска.",
                "Alignment was not confirmed. Stand nearby on level ground and stay still until playback."),
            "local_movement" or "partner_movement" => T("Кто-то сдвинулся или повернулся. Остановитесь и повторите запуск.",
                "A player moved or turned. Stop moving and retry."),
            "prepare_mod" or "prepare_validation" or "validate_mod" => T("Проверьте выбранные варианты в Penumbra и дождитесь синхронизации Lightless.",
                "Check the selected Penumbra options and wait for Lightless synchronization."),
            "pair_transport" => T("Согласование запуска прервано; подробная причина от партнёра не получена этим клиентом. Возможны потеря связи, закрытие или зависание игры. Это не подтверждённый диагноз; серверные отчёты сопоставляются по ID попытки.",
                "Launch coordination ended; this client did not receive the partner's detailed reason. Connection loss, game exit or a stalled game are possible, not confirmed. Server reports are correlated by attempt ID."),
            "heartbeat" or "start_deadline" => T("Ответ сервера или сигнал старта задержался. Повторите запуск; при повторении переподключите пару.",
                "The relay response or start signal was late. Retry; reconnect the pair if it repeats."),
            "partner_identity" => T("Подключённый партнёр не найден рядом. Проверьте мир, зону и соединение пары.",
                "The linked partner is not visible nearby. Check world, zone and pair connection."),
            "actor_state" => T("Оба должны стоять рядом, вне боя, без маунта и каста.",
                "Both players must stand nearby, out of combat, unmounted and not casting."),
            "lightless_check" => T("Дождитесь синхронизации с партнёром в Lightless.", "Wait for Lightless synchronization with your partner."),
            "proposal" or "deadline" => T("Предложение больше не активно. Отправьте новое и дождитесь принятия.",
                "The proposal is no longer active. Send a new one and wait for acceptance."),
            _ => T("Проверьте условие выше. Если сбой повторяется, скопируйте диагностику в настройках.",
                "Check the condition above. If it repeats, copy diagnostics from Settings.")
        };
        var measurements = "";
        if (pairCheckpoint is "alignment" or "post_prepare_alignment" or "final_alignment" &&
            Objects.LocalPlayer is { } local && LinkedPartner is { } peer)
            measurements = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                T("\nРасстояние: {0:F4} ялма (допуск ≤0,1); угол: {1:F2}° (допуск ≤0,57°).",
                  "\nDistance: {0:F4} yalms (limit ≤0.1); angle: {1:F2}° (limit ≤0.57°)."),
                string.CompareOrdinal(pairSelf, pairOther) < 0
                    ? Vector3.Distance(PairDestinationOnGround(local.Position, local.Rotation, peer.Position), peer.Position)
                    : Vector3.Distance(local.Position, PairDestinationOnGround(peer.Position, peer.Rotation, local.Position)),
                PairRules.AngleDistance(local.Rotation, peer.Rotation) * 180 / MathF.PI);
        return Localization.Runtime(config.Language, reason) + measurements + "\n" + advice;
    }
    private bool pairRefining, pairRefined;
    private long pairRefineSettledAt;
    private string pairCheckpoint = "preflight", lastPairStage = "";
    private void PairStage(string stage)
    {
        if (stage == lastPairStage) return;
        lastPairStage = stage;
        PairDebug($"attempt={pairOfferId} stage={stage}", true);
    }
    private long pairConfirmUntil;
    private long pairDeadline;
    private string pairCollectionWarning = "";
    private long lightlessCheckAt;
    private bool lightlessLoaded;
    private long lightlessActorCheckAt;
    private nint lightlessActor;
    private bool lightlessActorHandled;
    private string lightlessIpcError = "";

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
            try
            {
                lightlessActorHandled = Pi.GetIpcSubscriber<List<nint>>("LightlessSync.GetHandledAddresses").InvokeFunc().Contains(address);
                lightlessIpcError = "";
            }
            catch (Exception ex)
            {
                lightlessActorHandled = false;
                var error = ex.GetType().Name;
                if (lightlessIpcError != error) Log.Warning("Pair Lightless API check failed: " + error);
                lightlessIpcError = error;
            }
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

    private void BeginPair(Bookmark bookmark, bool? dance = null, bool? inviteOnly = null)
    {
        if (link?.Snapshot.Launch is not null)
        {
            linkNotice = T("Уже есть предложение анимации. Прими или отклони его в уведомлении.", "An animation proposal is already pending. Accept or decline it in the notification.");
            linkNoticeUntil = Environment.TickCount64 + 5000;
            return;
        }
        var id = PairRules.Hash(Guid.NewGuid().ToString());
        var stationary = inviteOnly ?? (dance is null && DefaultDanceInvitation(bookmark.ModDirectory, bookmark.Command));
        BeginPairCore(bookmark, id, dance: stationary || (dance ?? DefaultDance(bookmark.ModDirectory, bookmark.Command)), inviteOnly: stationary);
        if (pairSession is not null && pairBookmark is not null)
        {
            var mod = discovered.First(m => m.Directory == bookmark.ModDirectory && m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase));
            link!.Act("propose", offer: new LaunchOffer(id, PairRules.Family(mod.Name), PairRules.Hash(bookmark.Command.ToLowerInvariant()), OfferRole(bookmark), pairDance,
                pairDance ? pairBookmark.SavedOptions : null, pairStationary));
        }
    }

    private void BeginPairCore(Bookmark bookmark, string offerId, string? sharedFamily = null, bool restart = false, bool dance = false, bool inviteOnly = false)
    {
        CancelCarrierPlayback();
        CancelPair();
        lastLaunchFailure = "";
        pairDance = dance;
        pairStationary = inviteOnly;
        pairCheckpoint = "preflight";
        lastPairStage = "";
        pairCollectionWarning = "";
        try
        {
            if (inviteOnly && (!dance || link?.Snapshot.InvitationPeer != true))
                throw new InvalidOperationException("Update both clients and reconnect for stationary dance.");
            if (!config.PairEnabled) throw new InvalidOperationException(T("Включи экспериментальный запуск в паре в настройках.", "Enable experimental pair launch in Settings."));
            if (link?.Snapshot.State == "linked" && !link.Snapshot.CurrentPeer)
                throw new InvalidOperationException(T("Партнёру нужно обновить Emote Shelf до 0.3.9.1 и переподключиться.", "Your partner needs to update Emote Shelf to 0.3.9.1 and reconnect."));
            if (!LightlessLoaded()) throw new InvalidOperationException(T("Для запуска в паре нужен загруженный Lightless Sync. Обычные эмоции работают без него.", "Pair launch requires loaded Lightless Sync. Solo playback does not."));
            if (Objects.LocalPlayer is not { } local || LinkedPartner is not { } target || target.GameObjectId == local.GameObjectId)
                throw new InvalidOperationException(T("Сначала пригласи партнёра через контекстное меню и дождись принятия. Партнёр должен быть рядом.", "Invite a partner through the context menu and wait for acceptance. Your partner must be nearby."));
            if (!LightlessHandles(target.Address)) throw new InvalidOperationException(T("Lightless не сообщает, что обрабатывает выбранного партнёра. Сначала установите синхронизацию в Lightless.", "Lightless does not report handling this partner. Set up synchronization in Lightless first."));
            if ((!CanPair(local) && !(restart && CanRestartActor(local))) || (!CanPair(target) && !(restart && CanRestartActor(target))))
                throw new InvalidOperationException(T("Сначала оба встаньте и остановите эмоции. Запуск в паре недоступен в бою, на маунте или при касте.", "Both players must stand and stop their emotes first. Pair launch is unavailable in combat, mounted or while casting."));
            if (dance && (IsPoseCommand(bookmark.Command) || bookmark.Command == "/sit"))
                throw new InvalidOperationException(T("Для совместного танца выбери обычную эмоцию, не сидячую позу.", "Choose a regular emote for a shared dance, not a sitting pose."));
            if (!PairRules.NearbyForMode(local.Position, target.Position, dance))
                throw new InvalidOperationException(T("Подойдите ближе: максимум 10 ялмов, на одном уровне.", "Move closer: within 10 yalms and on the same level."));
            if (!ModScanner.ValidCommand(bookmark.Command))
                throw new InvalidOperationException(T("Эмоция недоступна.", "Emote is unavailable."));
            var mod = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory && m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(T("Обнови список модов.", "Refresh the mod list."));
            var (selfValid, _, selfCollection) = getCollection.Invoke(local.ObjectIndex);
            ValidateBookmark(bookmark, mod);
            var (targetValid, _, targetCollection) = getCollection.Invoke(target.ObjectIndex);
            if (selfValid && targetValid && selfCollection.Id == targetCollection.Id)
                pairCollectionWarning = T("У тебя и партнёра одна коллекция Penumbra: выбранные настройки могут менять обоих. Для разных ролей нужны корректно настроенные коллекции/синхронизация модов.",
                    "You and your partner share a Penumbra collection: selected options may affect both. Different roles need correctly configured collections/mod synchronization.");
            // Freeze this client's role/options; no network response can replace them.
            pairBookmark = new Bookmark { ModDirectory = bookmark.ModDirectory, Name = bookmark.Name, Command = bookmark.Command,
                IconId = bookmark.IconId, PoseIndex = bookmark.PoseIndex, Offset = inviteOnly ? null : bookmark.Offset,
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
            pairRestart = restart;
            pairFamily = sharedFamily ?? PairRules.Family(mod.Name);
            if (restart && !PairGeometryReady(local, target))
                throw new InvalidOperationException("Restart requires the accepted pair to remain aligned.");
            pairSession = new(pairSelf, pairOther, PairRules.Hash(pairFamily + offerId), true);
            pairMessage = T("Отправлено предложение анимации. Партнёр выбирает роль в уведомлении и подтверждает запуск.", "Animation proposed. Your partner chooses a role in the notification and accepts the launch.");
        }
        catch (Exception ex)
        {
            lastPairFailure = $"attempt={offerId} launch rejected checkpoint={pairCheckpoint} exception={ex.GetType().Name}";
            PairDebug(lastPairFailure, true);
            ReportPairError("launch_cancelled");
            link?.Act("cancel_launch", invitation: offerId);
            CancelPair(ex.Message);
        }
    }

    private void BeginPair(EmoteMod mod)
    {
        try { BeginPair(CreateBookmark(mod, null)); }
        catch (Exception ex) { CancelPair(ex.Message); }
    }

    private void CancelPair(string? reason = null, bool failure = true)
    {
        CancelPairWarmup();
        CancelCarrierPlayback();
        RestoreOffset();
        if (pairSession is not null && reason is not null && failure)
            ReportPairError(pairAlignStarted && pairAlignment?.Error.Length > 0 ? "alignment_failed" : "launch_cancelled");
        if (pairSession is not null) LogPairDiagnostics(reason ?? "Local cancellation");
        StopPairSync();
        if (reason is not null && failure)
        {
            // Keep the actionable explanation visible until dismissed, even after disconnect.
            lastLaunchFailure = ExplainPairFailure(reason);
            reason = lastLaunchFailure;
        }
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
        pairRefining = pairRefined = false;
        pairRefineSettledAt = 0;
        pairStableFrames = 0;
        if (reason is not null) { pairMessage = reason; status = reason; pairNoticeUntil = Environment.TickCount64 + 8000; }
    }

    private void LogPairDiagnostics(string outcome)
    {
        outcome = Localization.English(outcome);
        var now = Environment.TickCount64;
        var snapshot = pairSession?.Snapshot;
        var local = Objects.LocalPlayer;
        var peer = LinkedPartner;
        var geometry = local is not null && peer is not null
            ? FormattableString.Invariant($"distance={Vector3.Distance(local.Position, peer.Position):F5} angleRad={PairRules.AngleDistance(local.Rotation, peer.Rotation):F5} localCanPair={CanPair(local)} peerCanPair={CanPair(peer)}")
            : "actor unavailable";
        // The proposal identifier is not a bearer credential. Its short prefix correlates both clients.
        var attempt = pairOfferId.Length >= 8 ? pairOfferId[..8] : "none";
        // Closed diagnostic context, not arbitrary exception text (which may contain paths).
        var diagnostic = $"attempt={pairOfferId} result={(outcome == "Dispatched" ? "dispatched" : "cancelled")} checkpoint={pairCheckpoint} stage={lastPairStage} state={snapshot?.State} anchor={snapshot?.Anchor} ageMs={now - (snapshot?.ReceivedAt ?? now)} alignStarted={pairAlignStarted} preparing={pairPreparing} ready={pairReadySent} stable={pairStableFrames}; {geometry}; correction={(pairAlignStarted ? pairAlignment?.Diagnostics : "not started")}";
        if (outcome != "Dispatched") lastPairFailure = diagnostic;
        PairDebug(diagnostic, true);
        Log.Information($"Pair diagnostic v{typeof(Plugin).Assembly.GetName().Version} attempt={attempt} outcome={outcome}; state={snapshot?.State} anchor={snapshot?.Anchor} ageMs={now - (snapshot?.ReceivedAt ?? now)} alignStarted={pairAlignStarted} preparationStarted={pairStartedPreparation} preparing={pairPreparing} ready={pairReadySent} stable={pairStableFrames}; {geometry}; correction={(pairAlignStarted ? pairAlignment?.Diagnostics : "not started")}");
    }

    private void UpdatePair()
    {
        if (pairSession is null) return;
        try
        {
            var now = Environment.TickCount64;
            var snapshot = pairSession.Snapshot;
            PairStage($"{snapshot.State}/align={pairAlignStarted}/prepare={pairStartedPreparation}/held={pairHeldCommand.Length > 0}/ready={pairReadySent}");
            while (pairSession.TryReadDiagnostic(out var networkEvent)) PairDebug($"attempt={pairOfferId} {networkEvent}", true);
            pairCheckpoint = "deadline";
            if (!config.PairEnabled || now >= pairDeadline) throw new InvalidOperationException(T("Ожидание пары отменено или истекло.", "Pair launch cancelled or timed out."));
            pairCheckpoint = "partner_identity";
            if (Objects.LocalPlayer is not { } local || LinkedPartner is not { } target ||
                target.GameObjectId != pairTargetId ||
                PairIdentity(local) != pairSelf || PairIdentity(target) != pairOther)
                throw new InvalidOperationException(T("Пара отменена: подключённый партнёр не найден рядом, сменился персонаж или зона. Таргет не требуется.", "Pair cancelled: linked partner is not visible, or character/world/zone changed. A target is not required."));
            pairCheckpoint = "lightless_check";
            if (!LightlessHandles(target.Address)) throw new InvalidOperationException(T("Lightless больше не обрабатывает партнёра; запуск отменён.", "Lightless no longer handles the partner; launch cancelled."));
            pairCheckpoint = "actor_state";
            if ((!CanPair(local) && !(pairRestart && CanRestartActor(local))) || (!CanPair(target) && !(pairRestart && CanRestartActor(target)) && !PairRules.PeerMayHaveStarted(snapshot.State, snapshot.StartAt, now)) ||
                target.IsDead || !PairRules.NearbyForMode(local.Position, target.Position, pairDance))
                throw new InvalidOperationException(T("Пара отменена: движение, состояние или расстояние не подходят.", "Pair cancelled: character state or distance is unsuitable."));
            pairCheckpoint = "pose_setting";
            pairCheckpoint = "pair_transport";
            if (snapshot.State == "error") throw new InvalidOperationException(resourceError.Length > 0
                ? ResourceExplanation() + "\n" + ResourceDiagnostic() : snapshot.Error);
            pairCheckpoint = "heartbeat";
            if (now - snapshot.ReceivedAt > 4000) throw new InvalidOperationException(T("Сервер не отвечает; запуск отменён.", "Relay stopped responding; launch cancelled."));
            var proposal = link?.Snapshot.Launch;
            pairCheckpoint = "proposal";
            if (proposal?.Offer.Id == pairOfferId) pairOfferSeen = true;
            else if (pairOfferSeen || now - pairOfferQueuedAt > 5000)
                throw new InvalidOperationException(T("Предложение отклонено, отменено или истекло.", "Animation proposal declined, cancelled or expired."));
            if (proposal?.Offer.Id != pairOfferId || proposal.Status != "accepted") return;
            pairCheckpoint = "alignment";
            if (pairAlignment?.Error.Length > 0 && pairAlignStarted) throw new InvalidOperationException(pairAlignment.Error);
            var mayApproach = snapshot.Align && !snapshot.Anchor &&
                ((pairAlignStarted && !pairStartedPreparation) || pairRefining);
            pairCheckpoint = "local_movement";
            if (!mayApproach && (Vector3.Distance(local.Position, pairOrigin) > .08f ||
                    PairRules.AngleDistance(local.Rotation, pairFacing) > .08f))
                throw new InvalidOperationException(T("Пара отменена: ты начал двигаться или поворачиваться.", "Pair cancelled: you moved or turned."));
            if (snapshot.State is "connecting" or "waiting") return;
            pairCheckpoint = "alignment_consent";
            if (!snapshot.Align) throw new InvalidOperationException(T("Партнёр не подтвердил обязательное выравнивание. Обновите плагин у обоих.", "Partner did not enable required alignment. Update both plugins."));
            if (!pairStationary && snapshot.Align && !snapshot.Anchor && !pairStartedPreparation && !pairRestart)
            {
                pairCheckpoint = "partner_movement";
                if (Vector3.Distance(target.Position, pairTargetOrigin) > .08f || PairRules.AngleDistance(target.Rotation, pairTargetFacing) > .08f)
                    throw new InvalidOperationException(T("Партнёр сдвинулся; повторите выравнивание.", "Your partner moved; retry alignment."));
                if (!pairAlignStarted)
                {
                    pairCheckpoint = "alignment";
                    pairAlignment ??= new PairAlignment(Interop, Objects);
                    pairAlignment.Begin(PairDestination(pairTargetOrigin, pairTargetFacing), pairTargetFacing, pairDance);
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
                pairCheckpoint = "prepare_mod";
                pairStartedPreparation = pairPreparing = true;
                pairMessage = T("Подготавливаю выбранную роль…", "Preparing your selected role…");
                Play(pairBookmark!, true);
                if (pairSession is not null && pendingCommand.Length == 0 && waitingForStand is null && carrierQueued is null && carrierBuild is null)
                    throw new InvalidOperationException(status);
                return;
            }
            if (snapshot.State != "scheduled")
            {
                if (!pairPreparing && pairHeldCommand.Length > 0 && !pairReadySent)
                {
                    if (pairWarmup.Active)
                    {
                        pairWarmup.Tick(now, PlayWarmupTimeline);
                        pairMessage = T("Загружаю анимацию для синхронизации…", "Loading animation for synchronization…");
                        return;
                    }
                    pairCheckpoint = "resource_verification";
                    if (!PairResourcesReady())
                    {
                        if (now > pairDeadline - 1500)
                            throw new InvalidOperationException(ResourceExplanation() + "\n" + ResourceDiagnostic());
                        pairConfirmUntil = now + 5000;
                        pairMessage = ResourceExplanation();
                        return;
                    }
                    pairCheckpoint = "post_prepare_alignment";
                    if (now > pairConfirmUntil) throw new InvalidOperationException(T("Координаты и поворот не совпали после подготовки. Запуск отменён.", "Position/facing did not match after preparation. Launch cancelled."));
                    if (!pairStationary && !snapshot.Anchor && !pairRefined && !pairRestart)
                    {
                        if (Vector3.Distance(target.Position, pairTargetOrigin) > .08f ||
                            PairRules.AngleDistance(target.Rotation, pairTargetFacing) > .08f)
                            throw new InvalidOperationException("Partner moved during final correction.");
                        if (!pairRefining)
                        {
                            pairAlignment ??= new PairAlignment(Interop, Objects);
                            pairAlignment.BeginFinalCorrection(PairDestination(target.Position, target.Rotation), target.Rotation, pairDance);
                            pairAlignStarted = pairRefining = true;
                            PairDebug($"attempt={pairOfferId} final correction started", true);
                            pairMessage = T("Довожу координаты и поворот после подготовки…", "Refining position and facing after preparation…");
                            return;
                        }
                        if (!pairAlignment!.Arrived) return;
                        pairOrigin = local.Position;
                        pairFacing = local.Rotation;
                        pairRefining = false;
                        pairRefined = true;
                        pairRefineSettledAt = now + 350;
                        PairDebug($"attempt={pairOfferId} final correction confirmed; waiting for settling", true);
                    }
                    if (now < pairRefineSettledAt) return;
                    pairStableFrames = PairGeometryReady(local, target) ? pairStableFrames + 1 : 0;
                    if (pairStableFrames >= 3) { pairSession.Prepared(); pairReadySent = true; }
                    else pairMessage = T("Проверяю совпадение координат и поворота после подготовки…", "Confirming position and facing after preparation…");
                }
                if (pairReadySent) pairMessage = T("Выравнивание подтверждено. Ожидаю готовность партнёра…", "Alignment confirmed. Waiting for your partner…");
                return;
            }
            pairCheckpoint = "prepared_action";
            if (pairHeldCommand.Length == 0) throw new InvalidOperationException("No locally prepared action.");
            pairMessage = string.Format(T("Общий запуск через {0:0.0} с", "Shared start in {0:0.0}s"), Math.Max(0, snapshot.StartAt - now) / 1000f);
            if (now < snapshot.StartAt) return;
            pairCheckpoint = "start_deadline";
            if (now - snapshot.StartAt > 250 || now - snapshot.ReceivedAt > 650)
                throw new InvalidOperationException(T("Пропущено время старта или потеряна связь. Повторите запуск.", "Start was missed or connection lost. Please retry."));
            pairCheckpoint = "validate_mod";
            if (!PairResourcesReady()) throw new InvalidOperationException(T("Файлы партнёра не подтверждены. Дождитесь синхронизации Lightless и обновите Emote Shelf у обоих.", "Partner resources are not confirmed. Wait for Lightless synchronization and update Emote Shelf on both clients."));
            ValidatePairSelection();
            pairCheckpoint = "final_alignment";
            if (!PairGeometryReady(local, target))
                throw new InvalidOperationException(T("Перед стартом координаты или поворот разошлись. Запуск отменён.", "Position or facing diverged before start. Launch cancelled."));
            pairCheckpoint = "dispatch";
            if (snapshot.Align && !pairStationary) ApplyPairFacing(pairFacing);
            pairUntargetedPlayback = true;
            DispatchPreparedCommand(pairHeldCommand);
            ScheduleOffset(pairBookmark);
            RememberPairPlayback();
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

    private void DrawPairModeSelector()
    {
        var modes = new[] { T("Парная анимация — в одной точке", "Paired animation — same position"), T("Совместный танец — одна эмоция, 2 ялма между вами", "Shared dance — same emote, 2 yalms apart") };
        ImGui.SetNextItemWidth(Math.Min(440, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##pairMode", modes[config.SharedDance ? 1 : 0]))
        {
            for (var i = 0; i < modes.Length; i++)
                if (ImGui.Selectable(modes[i], config.SharedDance == (i == 1))) { config.SharedDance = i == 1; Save(); }
            ImGui.EndCombo();
        }
    }
    private void DrawPairSettings()
    {
        ImGui.TextWrapped(T("Категория запуска задаётся в карточке эмоции или настройках закладки.", "Set the launch category on the emote card or in bookmark settings."));
        if (ImGui.Checkbox(T("Запуск в паре — экспериментально", "Pair launch — experimental"), ref config.PairEnabled)) { StopLink(); Save(); }
        if (!config.PairEnabled) return;
        DrawLinkSettings();
        DrawSessionTools();
        DrawSyncStatus();
        ImGui.TextDisabled(LightlessLoaded() ? Localization.Runtime(config.Language, "Lightless Sync: OK") : T("Требуется Lightless Sync", "Lightless Sync required"));
    }

    private void DrawPairHelp()
    {
        ImGui.TextWrapped(T("Включите парный режим у обоих. Правый клик по персонажу → Emote Shelf отправляет приглашение; партнёр принимает его в уведомлении.",
            "Enable pair mode on both clients. Right-click a player → Emote Shelf to invite; your partner accepts in the notification."));
        ImGui.TextWrapped(LightlessLoaded() ? T("Lightless Sync загружен. Перед запуском дождитесь завершения синхронизации у обоих.", "Lightless Sync is loaded. Wait for synchronization to finish on both sides before launch.") :
            T("Требуется Lightless Sync у обоих участников. Одиночные эмоции его не требуют.", "Lightless Sync is required for both participants. Solo emotes do not require it."));
        ImGui.TextWrapped(T("После установления связи «В паре» или Shift+иконка отправляет предложение анимации. Партнёр выбирает свою роль в уведомлении и принимает запуск либо отказывается. Таргет держать не нужно. Сервер Emote Shelf видит IP и хеши персонажей/мода/выбора; личность персонажа не проверяется. Только с доверенным партнёром.",
            "Once linked, Pair or Shift+icon proposes an animation. Your partner selects their role in the notification and accepts or declines. Keeping a target is not required. The Emote Shelf relay sees IP addresses and character/mod/selection hashes; character identity is not authenticated. Use with trusted partners only."));
        ImGui.TextWrapped(T("Выравнивание обязательно: короткий подход, точная доводка координат и угла, проверка перед запуском.", "Alignment is required: short approach, fine position/facing correction, and confirmation before launch."));
        ImGui.TextWrapped(T("До 2 ялмов. Один стоит, второй подходит. После подготовки доводка до 0,1 ялма стремится к точности ≤0,01; запуск допускает остаток ≤0,1 ялма. Поворот проверяется отдельно. Принятие запуска разрешает выравнивание. Рост и масштаб не меняются.",
            "Within 2 yalms. One stands, the other approaches. Post-preparation correction up to 0.1 yalms aims for ≤0.01; playback accepts a residual ≤0.1 yalms. Facing is checked separately. Accepting a launch authorizes alignment. Height and scale are unchanged."));
        ImGui.TextWrapped(T("Моды и внешний вид передаёт Lightless, не наш сервер. Проверяем, что Lightless обрабатывает партнёра, но его API не подтверждает завершение загрузки конкретной анимации.",
            "Lightless transfers mods/appearance, not our relay. We check that it handles the partner, but its API does not confirm that this animation finished downloading."));
    }

}
