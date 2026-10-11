namespace EmoteShelf;

// Frame-bounded local resource loading. Never treats a timeout as readiness.
internal sealed class PairWarmup
{
    private ushort[] timelines = [];
    private int index, frames;
    private long deadline;
    internal bool Active { get; private set; }
    internal static ushort[] Plan(IEnumerable<string> paths, IEnumerable<(ushort Id, string Key)> rows)
    {
        var available = rows.Where(row => row.Id != 0 && row.Key.Length > 0).ToArray();
        var result = new HashSet<ushort>();
        foreach (var path in paths)
        {
            var matches = available.Where(row => path.EndsWith("/" + row.Key + ".pap", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) throw new InvalidOperationException("warmup_timeline_missing: " + path);
            foreach (var row in matches) result.Add(row.Id);
        }
        if (result.Count == 0) throw new InvalidOperationException("warmup_timeline_missing");
        return result.ToArray();
    }
    internal void Begin(ushort[] ids, long now)
    {
        if (Active || ids.Length == 0 || ids.Any(id => id == 0)) throw new InvalidOperationException("warmup_plan");
        timelines = ids.Distinct().ToArray(); index = 0; frames = 0;
        deadline = now + 5000; Active = true;
    }
    internal void Tick(long now, Action<ushort> play)
    {
        if (!Active) return;
        if (now >= deadline) throw new InvalidOperationException("warmup_timeout");
        if (frames == 0) play(timelines[index]);
        if (++frames == 3) play(0); // Restore idle after two complete update intervals.
        if (frames < 5) return; // Observe two idle frames before the next resource or readiness.
        frames = 0;
        if (++index == timelines.Length) Active = false;
    }
    internal void Cancel(Action<ushort> play)
    {
        var restore = Active && frames is > 0 and < 3;
        Active = false; timelines = []; frames = index = 0;
        if (restore) play(0);
    }
}
