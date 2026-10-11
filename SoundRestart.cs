namespace EmoteShelf;

internal sealed class SoundRestart
{
    internal enum State { Inactive, ApplySettings, AwaitIdle, AwaitRedraw, Settle, Ready, Failed }
    internal State Stage { get; private set; }
    private long deadline;
    private long idleSince;
    internal void StopRequested(long now) { Stage = State.AwaitIdle; deadline = now + 5000; idleSince = 0; }
    internal bool ObserveIdle(long now, bool idle)
    {
        if (Stage != State.AwaitIdle) return false;
        if (now >= deadline) { Stage = State.Failed; return false; }
        if (!idle) { idleSince = 0; return false; }
        if (idleSince == 0) idleSince = now;
        return now - idleSince >= 250;
    }
    internal static bool Needed(string command, bool playing, EmoteMod mod,
        IReadOnlyDictionary<string, IReadOnlyList<string>> options)
        => playing && command.ToLowerInvariant() is not ("/groundsit" or "/cpose" or "/changepose" or "/sit") &&
           (mod.BaseHasSound || mod.Variants.Any(v => v.HasSound && options.TryGetValue(v.Group, out var selected) &&
               selected.Contains(v.Option, StringComparer.OrdinalIgnoreCase)));
    internal void Begin() => Stage = State.ApplySettings;
    internal void Requested(long now) { Stage = State.AwaitRedraw; deadline = now + 5000; }
    internal void Confirm(long now)
    {
        if (Stage == State.AwaitRedraw) { Stage = State.Settle; deadline = now + 150; }
    }
    internal State Advance(long now)
    {
        if (now >= deadline)
        {
            if (Stage == State.AwaitRedraw) Stage = State.Failed;
            else if (Stage == State.Settle) Stage = State.Ready;
        }
        return Stage;
    }
    internal void Reset() => Stage = State.Inactive;
}
