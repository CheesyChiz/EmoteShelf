using EmoteShelf;

internal static class SoundRestartTests
{
    internal static void Run()
    {
        var mod = new EmoteMod("test", "test", "test", "/hum", 0, [],
            [new("Music", "On", [], null) { HasSound = true }, new("Music", "Off", [], null)], null);
        IReadOnlyDictionary<string, IReadOnlyList<string>> on = new Dictionary<string, IReadOnlyList<string>> { ["Music"] = new[] { "On" } };
        IReadOnlyDictionary<string, IReadOnlyList<string>> off = new Dictionary<string, IReadOnlyList<string>> { ["Music"] = new[] { "Off" } };
        if (!SoundRestart.Needed("/hum", true, mod, on) || SoundRestart.Needed("/hum", true, mod, off) ||
            SoundRestart.Needed("/hum", false, mod, on)) throw new Exception("Sound selection gating failed");
        foreach (var pose in new[] { "/groundsit", "/cpose", "/changepose", "/sit" })
            if (SoundRestart.Needed(pose, true, mod with { BaseHasSound = true }, on)) throw new Exception("Pose sound redraw exclusion failed");
        var gate = new SoundRestart();
        gate.Begin(); gate.StopRequested(100);
        if (gate.ObserveIdle(200, true) || gate.ObserveIdle(400, false) || gate.ObserveIdle(450, true) ||
            gate.ObserveIdle(699, true) || !gate.ObserveIdle(700, true)) throw new Exception("Neutral state must remain stable");
        gate.StopRequested(100);
        if (gate.ObserveIdle(5100, true) || gate.Stage != SoundRestart.State.Failed)
            throw new Exception("Idle timeout did not fail closed");
        gate.Begin(); gate.Confirm(0);
        if (gate.Stage != SoundRestart.State.ApplySettings) throw new Exception("Premature redraw accepted");
        gate.Requested(100); gate.Confirm(200);
        if (gate.Advance(349) != SoundRestart.State.Settle || gate.Advance(350) != SoundRestart.State.Ready)
            throw new Exception("Sound redraw settle failed");
        gate.Begin(); gate.Requested(0);
        if (gate.Advance(4999) != SoundRestart.State.AwaitRedraw || gate.Advance(5000) != SoundRestart.State.Failed)
            throw new Exception("Sound redraw timeout failed");
        gate.Confirm(5001);
        if (gate.Stage != SoundRestart.State.Failed) throw new Exception("Late confirmation revived failed restart");
        gate.Reset();
        if (gate.Stage != SoundRestart.State.Inactive) throw new Exception("Sound reset failed");
        Console.WriteLine("Selected sound restart, pose exclusion, redraw confirmation and timeout tests passed.");
    }
}
