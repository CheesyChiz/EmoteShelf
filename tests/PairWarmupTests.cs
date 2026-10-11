using EmoteShelf;

internal static class PairWarmupTests
{
    internal static void Run()
    {
        var warmup = new PairWarmup();
        var plan = PairWarmup.Plan(["a/emote/dance_loop.pap", "b/emote/dance_loop.pap"], [(10, "emote/dance_loop"), (20, "emote/other")]);
        if (!plan.SequenceEqual(new ushort[] { 10 })) throw new Exception("Warmup selected unrelated timeline");
        try { PairWarmup.Plan(["a/emote/missing.pap"], [(10, "emote/dance_loop")]); throw new Exception("Missing timeline ignored"); }
        catch (InvalidOperationException) { }
        var calls = new List<ushort>();
        warmup.Begin([10, 10, 20], 0);
        for (var i = 0; i < 10; i++) warmup.Tick(i * 16, calls.Add);
        if (warmup.Active || !calls.SequenceEqual(new ushort[] { 10, 0, 20, 0 })) throw new Exception("Warmup sequence/dedup/idle failed");
        warmup.Begin([10], 0); warmup.Tick(0, calls.Add); warmup.Cancel(calls.Add);
        if (warmup.Active || calls[^1] != 0) throw new Exception("Warmup cancellation did not restore idle");
        warmup.Begin([10], 0);
        try { warmup.Tick(5000, calls.Add); throw new Exception("Warmup timeout bypassed"); }
        catch (InvalidOperationException) { warmup.Cancel(calls.Add); }
        warmup.Begin([20], 6000);
        for (var i = 0; i < 4; i++) warmup.Tick(6000 + i, calls.Add);
        if (!warmup.Active) throw new Exception("Warmup skipped idle settling");
        warmup.Tick(6004, calls.Add);
        if (warmup.Active) throw new Exception("Warmup failed to finish");
        Console.WriteLine("Pair warmup frame budget, idle restoration, cancellation, deduplication and timeout tests passed.");
    }
}
