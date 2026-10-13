using NoireLib.Animations.PapFormat;
using Penumbra.Api.Enums;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private sealed record CarrierRequest(long Ticket, Bookmark Bookmark, ulong Actor, uint Territory, Guid Collection, string Root, bool Pair);
    private sealed record CarrierResult(string Directory, string Command);
    private readonly CarrierRequestGate carrierGate = new();
    private CarrierRequest? carrierQueued, carrierRunning;
    private Task<CarrierResult>? carrierBuild;
    private CarrierResult? carrierReady;
    private string carrierImport = "";
    private long carrierImportDeadline;

    private static bool IsCarrierCopy(string directory) => directory.StartsWith("EmoteShelfCarrier_", StringComparison.Ordinal);
    private void CancelCarrierPlayback() { carrierGate.Cancel(); carrierQueued = null; }

    private void QueueCarrier(Bookmark bookmark, bool pair = false)
    {
        var player = Objects.LocalPlayer ?? throw new InvalidOperationException("carrier_actor_unavailable");
        var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
        if (!valid || collection.Id == Guid.Empty) throw new InvalidOperationException("carrier_collection_unavailable");
        var snapshot = SnapshotBookmark(bookmark);
        if (snapshot.SavedOptions is null)
        {
            var source = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory && m.Command == bookmark.Command)
                ?? throw new InvalidOperationException("carrier_settings_unavailable");
            snapshot.SavedOptions = ReadModOptions(collection.Id, source);
        }
        carrierQueued = new(carrierGate.Next(), snapshot, player.GameObjectId, ClientState.TerritoryType, collection.Id, getRoot.Invoke(), pair);
        status = T("Готовлю анимацию для запуска через доступную эмоцию…", "Preparing playback through an unlocked emote…");
    }

    private bool CarrierContextValid(CarrierRequest request)
    {
        if (!carrierGate.IsCurrent(request.Ticket)) return false;
        if (request.Pair && pairSession is null) return false;
        if (Objects.LocalPlayer is not { } player ||
            player.GameObjectId != request.Actor || ClientState.TerritoryType != request.Territory ||
            !string.Equals(request.Root, getRoot.Invoke(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("carrier_context_changed");
        var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
        if (!valid || collection.Id != request.Collection) throw new InvalidOperationException("carrier_context_changed");
        return true;
    }

    private Task<CarrierResult> BeginCarrier(CarrierRequest request)
    {
        if (catalog is null || !IntegrationLoaded("Penumbra")) throw new InvalidOperationException("carrier_unavailable");
        var bookmark = request.Bookmark;
        var paths = CarrierConversion.SourcePaths(Path.Combine(request.Root, bookmark.ModDirectory));
        var sourceSlots = catalog.TimelineSlots(bookmark.Command);
        var candidates = new List<(string Command, List<CarrierConversion.Redirect> Redirects)>();
        foreach (var command in new[] { "/harvestdance", "/mandervilledance", "/balldance", "/stepdance", "/dance", "/wave", "/bow" })
        {
            if (command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase) || catalog.IsUnlocked(command, Unlocks) != true ||
                catalog.IsLoop(command) != catalog.IsLoop(bookmark.Command)) continue;
            var targetSlots = catalog.TimelineSlots(command);
            var redirects = new List<CarrierConversion.Redirect>();
            var compatible = true;
            foreach (var path in paths)
            {
                string? target;
                try { target = CarrierConversion.TargetPath(path, sourceSlots, targetSlots); }
                catch (InvalidDataException) { compatible = false; break; }
                if (target is null) continue;
                var data = Data.GetFile(target)?.Data;
                var names = data is null ? [] : PapAnimationNames.Read(data).ToArray();
                if (names.Length == 0) { compatible = false; break; }
                redirects.Add(new(path, target, names));
            }
            if (compatible && redirects.Count > 0) candidates.Add((command, redirects));
        }
        if (candidates.Count == 0) throw new InvalidOperationException("carrier_no_compatible_unlocked_emote");
        return Task.Run(() =>
        {
            Exception? last = null;
            foreach (var candidate in candidates)
            {
                try { return new CarrierResult(CarrierConversion.Create(request.Root, bookmark.ModDirectory, candidate.Command, candidate.Redirects, bookmark.Command), candidate.Command); }
                catch (InvalidDataException ex) { last = ex; }
            }
            throw last ?? new InvalidDataException("carrier_no_compatible_unlocked_emote");
        });
    }

    private void UpdateCarrier()
    {
        try
        {
            if (carrierBuild is { IsCompleted: true } build)
            {
                carrierBuild = null;
                CarrierResult result;
                try { result = build.GetAwaiter().GetResult(); }
                catch when (carrierRunning is null || !carrierGate.IsCurrent(carrierRunning.Ticket)) { carrierRunning = null; return; }
                if (carrierRunning is null || !CarrierContextValid(carrierRunning))
                { carrierRunning = null; return; }
                carrierReady = result; carrierImport = result.Directory;
                if (!getMods.Invoke().ContainsKey(carrierImport) && addReceivedMod.Invoke(carrierImport) != PenumbraApiEc.Success)
                    throw new InvalidOperationException("carrier_import_failed");
                carrierImportDeadline = Environment.TickCount64 + 5000;
            }
            if (carrierImport.Length > 0)
            {
                if (carrierRunning is null || !CarrierContextValid(carrierRunning))
                { carrierImport = ""; carrierReady = null; carrierRunning = null; return; }
                if (!getMods.Invoke().ContainsKey(carrierImport))
                {
                    if (Environment.TickCount64 > carrierImportDeadline) throw new InvalidOperationException("carrier_import_timeout");
                    return;
                }
                // Play only applies our temporary collection settings, never persistent enablement.
                var ec = receivedSettings.Invoke(carrierImport, out var settings, ignoreTemporaryCollections: true);
                if (ec != PenumbraApiEc.Success) throw new InvalidOperationException("carrier_settings_unavailable");
                CarrierSettings.DisablePersistent(settings.Keys,
                    collection => setReceivedModEnabled.Invoke(collection, carrierImport, false)
                        is PenumbraApiEc.Success or PenumbraApiEc.NothingChanged,
                    (collection, ignoreTemporary) =>
                    {
                        var (code, state) = getSettingsWithTemp.Invoke(collection, carrierImport, ignoreTemporary: ignoreTemporary);
                        return (code == PenumbraApiEc.Success, state?.Item1);
                    });
                var bookmark = SnapshotBookmark(carrierRunning.Bookmark);
                bookmark.ModDirectory = carrierImport; bookmark.Command = carrierReady!.Command;
                if (catalog?.IsUnlocked(bookmark.Command, Unlocks) != true) throw new InvalidOperationException("carrier_unlock_changed");
                Log.Information("Automatic carrier ready: command={Command}", bookmark.Command);
                var forPair = carrierRunning.Pair;
                carrierImport = ""; carrierReady = null; carrierRunning = null;
                Scan(onlyDirectory: bookmark.ModDirectory);
                Play(bookmark, forPair);
            }
            if (carrierBuild is null && carrierImport.Length == 0 && carrierQueued is { } queued)
            {
                carrierQueued = null;
                if (!CarrierContextValid(queued)) return;
                carrierRunning = queued;
                carrierBuild = BeginCarrier(queued);
                Log.Information("Automatic carrier preparation started: command={Command}", queued.Bookmark.Command);
            }
        }
        catch (Exception ex)
        {
            carrierImport = ""; carrierReady = null; carrierRunning = null;
            CarrierFailure(ex);
        }
    }

    private void CarrierFailure(Exception ex)
    {
        var code = ex.Message.StartsWith("carrier_") && ex.Message.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? ex.Message : "carrier_conversion_failed";
        status = T("Не удалось подготовить анимацию через доступную эмоцию. Совместимый носитель не найден или файлы мода не поддерживаются. Оригинал не изменён. Код: ",
            "Could not prepare playback through an unlocked emote. No compatible carrier was found or the mod files are unsupported. The original is unchanged. Code: ") + code;
        if (code == "carrier_context_changed")
            status = T("Запуск отменён: во время подготовки сменился персонаж, зона или коллекция Penumbra. Нажми на анимацию снова.", "Playback cancelled: the character, zone or Penumbra collection changed during preparation. Click the animation again.");
        Log.Warning("Automatic carrier failed: code={Code} exception={Exception}", code, ex.GetType().Name);
        if (pairSession is not null) CancelPair(status);
        errorReports.Send("launch_cancelled", "prepare_mod", link?.Snapshot.State ?? "none", LinkFresh, LightlessLoaded(), "", null, null,
            new ReportContext("none", false, false, true, false, false, 0));
    }
}
