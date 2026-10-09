namespace EmoteShelf;

public static class PosePlayback
{
    public enum Entry { PrepareIdle, RefreshActivePose, EnterGroundSit }

    public readonly record struct Plan(Entry Action, int SettleMilliseconds, bool Cycle, bool SetSavedGroundSlot);

    // Keep entry preparation independent of whether automatic cycling is enabled.
    // Never write the saved slot in an active pose: that can also change telemetry.
    public static Plan Prepare(bool groundSit, bool alreadyInPose, bool automatic, bool knownTarget)
    {
        var action = alreadyInPose ? Entry.RefreshActivePose : groundSit ? Entry.EnterGroundSit : Entry.PrepareIdle;
        return new(action, action == Entry.EnterGroundSit ? 500 : 150,
            automatic && knownTarget, automatic && knownTarget && action == Entry.EnterGroundSit);
    }

    public static bool ShouldStandUp(string command, bool alreadyGroundSitting)
        => alreadyGroundSitting && !command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase);
}
