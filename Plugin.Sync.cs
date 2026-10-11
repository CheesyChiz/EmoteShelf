using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Penumbra.Api.Enums;

namespace EmoteShelf;
public sealed partial class Plugin
{
    private SyncClient? pairSync;
    private Task<string[]>? resourcePaths;
    private Task<ResourceDigest[]>? resourceHashes, peerHashes;
    private ResourceDigest[] localResources = [], checkingPeer = [];
    private bool peerResourcesVerified;
    private long nextResourceCheck, nextPhaseRestart, lastPhaseSample;
    private int phaseMismatchSamples;
    private double observedPhaseDrift = double.NaN;
    private string resourceState = "";

    private void StopPairSync()
    {
        pairSync?.Dispose(); pairSync = null;
        resourcePaths = null; resourceHashes = peerHashes = null;
        localResources = []; checkingPeer = []; peerResourcesVerified = false;
        observedPhaseDrift = double.NaN; phaseMismatchSamples = 0;
    }
    private Dictionary<string, string> ResolveResources(IEnumerable<string> paths, ushort actor)
    {
        var (valid, _, collection) = getCollection.Invoke(actor);
        if (!valid) throw new InvalidOperationException("resource_collection");
        var result = new Dictionary<string, string>();
        foreach (var path in paths)
        {
            if (!SyncRules.ValidPath(path) || resolvePath.Invoke(collection.Id, path, out var file) != PenumbraApiEc.Success || !Path.IsPathRooted(file))
                throw new InvalidOperationException("resource_unresolved");
            result.Add(path, file);
        }
        return result;
    }
    private bool PairResourcesReady()
    {
        if (pairSync is null)
        {
            if (link?.TransferCapability is not { } token) return false;
            pairSync = new(token, pairOfferId);
            var root = pendingExpectedModRoot;
            var options = pendingOptions?.ToDictionary(x => x.Key, x => x.Value.ToArray()) ?? [];
            var paths = pendingExpectedPaths.ToArray();
            resourcePaths = Task.Run(() => ResourceProof.Paths(root, options, paths));
            resourceState = "waiting";
        }
        var remote = pairSync.Observation;
        return localResources.Length > 0 && peerResourcesVerified && remote.Peer?.Verified == true &&
               Environment.TickCount64 - remote.Received < 2000 && remote.Age < 2;
    }
    private void UpdatePairSync()
    {
        if (pairSync is null) return;
        if (!LinkFresh || link?.Snapshot.State != "linked" || Objects.LocalPlayer is not { } local || LinkedPartner is not { } partner ||
            (pairSession is null && lastAcceptedPair is null)) { StopPairSync(); return; }
        var now = Environment.TickCount64;
        if (pairSession is null && lastAcceptedPair is { } accepted && now > accepted.Until)
        { lastAcceptedPair = null; StopPairSync(); return; }
        try
        {
            if (resourcePaths is { IsCompleted: true } pathsTask)
            {
                resourcePaths = null;
                var files = ResolveResources(FilterLocalResources(pathsTask.GetAwaiter().GetResult()), local.ObjectIndex);
                if (files.Count == 0) throw new InvalidOperationException("resource_no_matching_model");
                resourceHashes = Task.Run(() => ResourceProof.Hash(files));
            }
            if (resourceHashes is { IsCompleted: true } hashTask)
            { resourceHashes = null; localResources = hashTask.GetAwaiter().GetResult(); }
            var observation = pairSync.Observation;
            if (peerHashes is { IsCompleted: true } peerTask)
            {
                peerHashes = null;
                peerResourcesVerified = ResourceProof.Equal(peerTask.GetAwaiter().GetResult(), checkingPeer) &&
                    observation.Peer is { } current && ResourceProof.Equal(current.Assets, checkingPeer);
                resourceState = peerResourcesVerified ? "verified" : "mismatch";
            }
            if (observation.Peer is { Assets.Length: > 0 } other && peerHashes is null && now >= nextResourceCheck && pairSession is not null)
            {
                nextResourceCheck = now + 1000;
                if (!ResourceProof.Equal(checkingPeer, other.Assets)) peerResourcesVerified = false;
                checkingPeer = other.Assets;
                var files = ResolveResources(other.Assets.Select(a => a.Path), partner.ObjectIndex);
                peerHashes = Task.Run(() => ResourceProof.Hash(files));
            }
            double phase = 0, duration = 0;
            if (pairSession is null && lastAcceptedPair is not null && StillPlayingAcceptedEmote()) ReadLocalPhase(out phase, out duration);
            pairSync.Set(localResources, peerResourcesVerified, phase, duration);
            if (pairSession is null && duration > 0 && observation.Peer is { Duration: > 0 } sample &&
                observation.Received != lastPhaseSample && now-observation.Received < 1500 && observation.Rtt <= 200 &&
                Math.Abs(duration-sample.Duration) <= .02 && sample.Age <= 1)
            {
                lastPhaseSample = observation.Received;
                observedPhaseDrift = SyncRules.Drift(phase, sample.Phase+observation.Age+(observation.Rtt/2000.0)+(now-observation.Received)/1000.0, duration);
                phaseMismatchSamples = observedPhaseDrift > Math.Max(.20, observation.Rtt/1000.0) ? phaseMismatchSamples+1 : 0;
                if (config.MaintainPairPhase && phaseMismatchSamples >= 4 && now >= nextPhaseRestart &&
                    now > acceptedEmoteCheckAfter + 5000 && string.CompareOrdinal(link.Identity, link.Snapshot.Partner) < 0)
                {
                    nextPhaseRestart = now + 30000; phaseMismatchSamples = 0;
                    PairDebug($"phase driftMs={observedPhaseDrift*1000:F0}; requesting agreed restart", true);
                    RestartAcceptedPair();
                }
            }
            else if (duration == 0 || observation.Peer is null || now-observation.Received > 2000) observedPhaseDrift = double.NaN;
        }
        catch (Exception ex)
        {
            peerResourcesVerified = false; resourceState = "unavailable";
            pairSync.Set(localResources, false, 0, 0);
            if (now >= nextResourceCheck) { nextResourceCheck = now + 1000; PairDebug("resource proof unavailable: " + ex.GetType().Name, true); }
        }
    }
    private unsafe bool ReadLocalPhase(out double phase, out double duration)
    {
        phase = duration = 0;
        if (Objects.LocalPlayer is not { } player || !CanRewindLoop(player.Address, false)) return false;
        var actor = (Character*)player.Address;
        var human = (Human*)actor->DrawObject;
        var animation = human->Skeleton->PartialSkeletons[0].GetHavokAnimatedSkeleton(0);
        // Blended/ambiguous tracks are not a reliable phase measurement.
        if (animation->AnimationControls.Length != 1) return false;
        var control = animation->AnimationControls[0].Value;
        var binding = control->hkaAnimationControl.Binding.ptr;
        if (binding == null || binding->Animation.ptr == null) return false;
        phase = control->hkaAnimationControl.LocalTime;
        duration = binding->Animation.ptr->Duration;
        if (!SyncRules.ValidPhase(phase, duration) || duration < .25) { phase = duration = 0; return false; }
        return true;
    }
    private unsafe string[] FilterLocalResources(string[] paths)
    {
        if (Objects.LocalPlayer is not { } player) return paths;
        if (papCompatibility is null)
        {
            var table = Data.GetFile<Lumina.Data.FileResource>("chara/xls/animation/papLoadTable.plt");
            if (table is not null) papCompatibility = new(table.Data);
        }
        var draw = ((Character*)player.Address)->DrawObject;
        if (draw == null || draw->GetObjectType() != ObjectType.CharacterBase || ((CharacterBase*)draw)->GetModelType() != CharacterBase.ModelType.Human) return paths;
        var model = ((Human*)draw)->RaceSexId;
        return paths.Where(p => papCompatibility?.Matches(p, model) != false).ToArray();
    }
    private void DrawSyncStatus()
    {
        if (ImGui.Checkbox(T("Поддерживать фазу пары", "Maintain pair phase"), ref config.MaintainPairPhase)) Save();
        ImGui.TextWrapped(T("Устойчивое расхождение вызывает общий перезапуск принятой анимации, не чаще раза в 30 секунд. Сложные смешанные анимации не корректируются. Точная фаза музыки не измеряется.", "Persistent drift triggers a shared restart of the accepted animation, at most once every 30 seconds. Complex blended animations are not corrected. Exact music phase is not measured."));
        if (pairSync is null) return;
        ImGui.TextWrapped(double.IsNaN(observedPhaseDrift) ? T("Фаза пока не измерена", "Phase not measured yet") :
            string.Format(T("Расхождение фазы: примерно {0:F0} мс", "Estimated phase drift: {0:F0} ms"), observedPhaseDrift*1000));
        ImGui.TextWrapped(resourceState == "verified" ? T("Файлы партнёра совпали по контрольным суммам", "Partner files match their checksums") :
            T("Ожидаем совпадения файлов партнёра", "Waiting for matching partner files"));
    }
}
