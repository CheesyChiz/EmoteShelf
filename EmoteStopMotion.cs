using System.Numerics;

namespace EmoteShelf;

// One input pulse, then only a bounded restoration of this local actor's transform.
internal sealed class EmoteStopMotion
{
    internal enum State { Inactive, Pulse, Restore, Complete, Failed }
    internal State Stage { get; private set; }
    internal Vector3 Origin { get; private set; }
    internal float Facing { get; private set; }
    private long deadline, pulsedAt;
    private int stable;
    internal bool Active => Stage is State.Pulse or State.Restore;
    internal void Begin(Vector3 origin, float facing, long now)
    {
        if (!float.IsFinite(origin.X + origin.Y + origin.Z + facing))
            throw new InvalidOperationException("sound_stop_invalid_position");
        Origin = origin; Facing = facing; deadline = now + 1500;
        pulsedAt = 0; stable = 0; Stage = State.Pulse;
    }
    internal bool Validate(Vector3 position, long now, bool safe, bool manualInput)
    {
        if (!Active) return false;
        if (!safe || manualInput || now >= deadline ||
            !float.IsFinite(position.X + position.Y + position.Z) ||
            Vector3.Distance(position, Origin) > .10f || MathF.Abs(position.Y - Origin.Y) > .03f)
        { Stage = State.Failed; return false; }
        return true;
    }
    internal void Pulsed(long now)
    {
        if (Stage != State.Pulse) return;
        Stage = State.Restore; pulsedAt = now;
    }
    internal bool Restore(Vector3 position, float facing, long now)
    {
        if (Stage != State.Restore || now <= pulsedAt) return false;
        stable = PairRules.Aligned(position, facing, Origin, Facing) ? stable + 1 : 0;
        if (now - pulsedAt >= 350 && stable >= 3) { Stage = State.Complete; return false; }
        return true;
    }
    internal void Cancel() { Stage = State.Inactive; stable = 0; }
}
