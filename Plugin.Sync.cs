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
    private string resourceError = "", lastResourceDiagnostic = "";
    private long nextResourceDiagnostic;
    private long nextLocalResourceCheck;

    private void StopPairSync()
    {
        pairSync?.Dispose(); pairSync = null;
        resourcePaths = null; resourceHashes = peerHashes = null;
        localResources = []; checkingPeer = []; peerResourcesVerified = false;
        observedPhaseDrift = double.NaN; phaseMismatchSamples = 0;
        resourceError = lastResourceDiagnostic = "";
        nextResourceDiagnostic = nextResourceCheck = 0;
        nextLocalResourceCheck = 0;
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
        var detail = ResourceDiagnostic();
        if (detail != lastResourceDiagnostic || Environment.TickCount64 >= nextResourceDiagnostic)
        {
            lastResourceDiagnostic = detail;
            nextResourceDiagnostic = Environment.TickCount64 + 5000;
            PairDebug("resource verification: " + detail, true);
        }
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
            if (resourceHashes is null && resourcePaths is { IsCompleted: true } pathsTask && now >= nextLocalResourceCheck)
            {
                nextLocalResourceCheck = now + 1000;
                var files = ResolveResources(FilterLocalResources(pathsTask.GetAwaiter().GetResult()), local.ObjectIndex);
                if (files.Count == 0) throw new InvalidOperationException("resource_no_matching_model");
                resourceError = "";
                resourceHashes = Task.Run(() => ResourceProof.Hash(files));
            }
            if (resourceHashes is { IsCompleted: true } hashTask)
            {
                resourceHashes = null;
                localResources = hashTask.GetAwaiter().GetResult();
                resourcePaths = null;
                resourceError = "";
            }
            var observation = pairSync.Observation;
            if (peerHashes is { IsCompleted: true } peerTask)
            {
                peerHashes = null;
                peerResourcesVerified = ResourceProof.Equal(peerTask.GetAwaiter().GetResult(), checkingPeer) &&
                    observation.Peer is { } current && ResourceProof.Equal(current.Assets, checkingPeer);
                resourceState = peerResourcesVerified ? "verified" : "mismatch";
                resourceError = peerResourcesVerified ? "" : "resource_hash_mismatch";
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
            resourceError = ex is InvalidOperationException or System.IO.InvalidDataException &&
                ex.Message.StartsWith("resource_") ? ex.Message : "resource_" + ex.GetType().Name;
            if (now >= nextResourceDiagnostic)
            { nextResourceDiagnostic = now + 5000; PairDebug("resource proof unavailable: " + resourceError, true); }
        }
    }
    private int ResourceHttpStatus()
    {
        var error = pairSync?.Observation.Error ?? "";
        return error.StartsWith("sync_http_") && error.Length >= 13 &&
            int.TryParse(error.AsSpan(10, 3), out var code) && code is >= 100 and <= 599 ? code : 0;
    }
    private string ResourceReportStatus()
    {
        if (pairSync is null) return "none";
        if (pairSync.Observation.Error.Length > 0) return "transport";
        if (resourceError == "resource_hash_mismatch") return "mismatch";
        if (resourceError.Length > 0) return "unavailable";
        if (localResources.Length == 0) return "local_pending";
        if (pairSync.Observation.Peer is null) return "peer_missing";
        return peerResourcesVerified && pairSync.Observation.Peer.Verified ? "verified" : "peer_pending";
    }
    private string ResourceDiagnostic()
    {
        var remote = pairSync?.Observation;
        return $"state={resourceState}; localAssets={localResources.Length}; peerAssets={remote?.Peer?.Assets.Length ?? 0}; " +
            $"localVerified={peerResourcesVerified}; peerVerified={remote?.Peer?.Verified ?? false}; " +
            $"transport={remote?.Error ?? "not_started"}; error={resourceError}";
    }
    private string ResourceExplanation()
    {
        var remote = pairSync?.Observation;
        if (remote?.Error.Length > 0)
            return T("Не удалось обменяться проверкой файлов с сервером.", "Resource verification exchange with the server failed.") + " " + remote.Error;
        if (resourceError.Length > 0)
            return T("Файлы анимации недоступны или не совпадают.", "Animation files are unavailable or do not match.") + " " + resourceError;
        return T("Ожидаем подтверждение файлов от второго клиента.", "Waiting for file confirmation from the other client.");
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
