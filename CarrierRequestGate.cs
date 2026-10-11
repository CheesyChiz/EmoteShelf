namespace EmoteShelf;

// A completed background conversion may only launch the latest requested playback.
internal sealed class CarrierRequestGate
{
    private long revision;
    internal long Next() => ++revision;
    internal void Cancel() => ++revision;
    internal bool IsCurrent(long ticket) => ticket == revision;
}
