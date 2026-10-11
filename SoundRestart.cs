namespace EmoteShelf;

internal sealed class SoundRestart
{
    internal enum State { Inactive, ApplySettings, AwaitRedraw, Settle, Ready, Failed }
    internal State Stage { get; private set; }
    private long deadline;
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
