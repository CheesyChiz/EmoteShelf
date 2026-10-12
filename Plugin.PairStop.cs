using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private readonly SoundRestart pairStopGate = new();
    private bool pairStopStarted, pairStopComplete;
    private bool CanReceivePair(IPlayerCharacter actor) => CanPair(actor) || CanRestartActor(actor);
    private static unsafe bool ActorEmoting(IPlayerCharacter actor)
        => ((Character*)actor.Address)->EmoteController.IsEmoting();

    private bool StopAcceptedPairEmote(IPlayerCharacter local, IPlayerCharacter peer, long now)
    {
        if (!pairStopStarted)
        {
            pairStopStarted = true;
            soundStopNudged = false;
            soundFailure = "none";
            playbackActor = local.GameObjectId;
            playbackTerritory = ClientState.TerritoryType;
            pairOrigin = local.Position; pairFacing = local.Rotation;
            pairTargetOrigin = peer.Position; pairTargetFacing = peer.Rotation;
            pairStopGate.StopRequested(now);
            if (IsPlayingEmote()) StopSoundEmote();
        }
        if (soundStopNudged && pairAlignment?.EmoteStopFailed == true)
        {
            Log.Warning("Emote stop motion failed: {Reason}", pairAlignment.EmoteStopFailure);
            soundFailure = "motion_" + pairAlignment.EmoteStopFailure;
            throw new InvalidOperationException("sound_stop_motion_failed");
        }
        if (!soundStopNudged && CanPair(local) && IsPlayingEmote()) BeginSoundStopMotion();
        if (pairAlignment?.StoppingEmote != true &&
            (System.Numerics.Vector3.Distance(local.Position, pairOrigin) > .08f ||
             PairRules.AngleDistance(local.Rotation, pairFacing) > .08f))
            throw new InvalidOperationException("Local movement interrupted emote stopping.");
        // A local observation of the peer cannot acknowledge its completed
        // restoration. Each client confirms itself through the relay barrier.
        var idle = pairAlignment?.StoppingEmote != true && SoundEmoteIsIdle();
        if (!pairStopGate.ObserveIdle(now, idle))
        {
            if (pairStopGate.Stage == SoundRestart.State.Failed)
            {
                soundFailure = "idle_timeout";
                throw new InvalidOperationException("sound_restart_idle_timeout");
            }
            pairMessage = T("Останавливаю предыдущую эмоцию после принятия запроса…", "Stopping the previous emote after acceptance…");
            return false;
        }
        pairStopComplete = true;
        pairSession!.Stopped();
        pairStopGate.Reset();
        pairOrigin = local.Position; pairFacing = local.Rotation;
        pairTargetOrigin = peer.Position; pairTargetFacing = peer.Rotation;
        return true;
    }
}
