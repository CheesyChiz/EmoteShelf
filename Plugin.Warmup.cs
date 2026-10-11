using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;

namespace EmoteShelf;
public sealed partial class Plugin
{
    private readonly PairWarmup pairWarmup = new();
    private nint warmupActor;
    private ulong warmupActorId;
    private ushort warmupBase;

    private unsafe void BeginPairWarmup()
    {
        if (Objects.LocalPlayer is not { } player || !CanPair(player))
            throw new InvalidOperationException("warmup_actor_state");
        // Resolve only the selected PAP timelines, including the chosen pose/carrier.
        var paths = FilterLocalResources(pendingExpectedPaths.ToArray());
        var ids = PairWarmup.Plan(paths, Data.GetExcelSheet<ActionTimeline>()
            .Where(row => row.RowId is > 0 and <= ushort.MaxValue)
            .Select(row => ((ushort)row.RowId, row.Key.ToString())));
        warmupActor = player.Address; warmupActorId = player.GameObjectId;
        warmupBase = ((Character*)warmupActor)->Timeline.BaseOverride;
        pairWarmup.Begin(ids, Environment.TickCount64);
        PairDebug($"local animation warmup: timelines={ids.Length}", true);
    }

    private unsafe void PlayWarmupTimeline(ushort id)
    {
        if (Objects.LocalPlayer is not { } player || player.Address != warmupActor || player.GameObjectId != warmupActorId)
            throw new InvalidOperationException("warmup_actor_changed");
        if (!CanPair(player)) throw new InvalidOperationException("warmup_actor_state");
        var actor = (Character*)player.Address;
        if (id == 0) actor->Timeline.BaseOverride = warmupBase;
        // Local timeline only: no server emote, target turning or saved-pose changes.
        // As in Synastry's preloader, briefly loading the timeline lets Lightless capture it.
        actor->Timeline.TimelineSequencer.PlayTimeline(id == 0 ? (ushort)3 : id);
    }

    private void CancelPairWarmup()
    {
        try { pairWarmup.Cancel(PlayWarmupTimeline); }
        catch (Exception ex) { Log.Warning("Warmup cleanup skipped: {Reason}", ex.Message); }
        warmupActor = 0; warmupActorId = 0;
    }
}
