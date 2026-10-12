using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace EmoteShelf;

public static class PairRules
{
    public static bool MayStopPreviousEmote(bool accepted, string pairState)
        => accepted && pairState == "paired";
    public const float FinalCorrectionLimit = .10f;
    public const float LaunchDistanceTolerance = .10f;
    public static Vector3 DancePosition(Vector3 anchor, float yaw)
        => anchor + new Vector3(MathF.Cos(yaw) * 2, 0, -MathF.Sin(yaw) * 2);
    public static Vector3 GroundedDestination(Vector3 destination, Vector3 actor, bool dance)
        => dance && float.IsFinite(actor.Y) && MathF.Abs(destination.Y - actor.Y) <= .25f
            ? new Vector3(destination.X, actor.Y, destination.Z) : destination;
    public static bool NearbyForMode(Vector3 a, Vector3 b, bool dance)
        => WithinApproachRange(a, b, 10);
    public static bool WithinApproachRange(Vector3 a, Vector3 b, float maximum)
        => float.IsFinite(a.X + a.Y + a.Z + b.X + b.Y + b.Z) &&
            Vector3.Distance(a, b) <= maximum && MathF.Abs(a.Y - b.Y) <= .25f;
    public static bool ReadyToLaunch(Vector3 a, float yawA, Vector3 b, float yawB)
        => Vector3.Distance(a, b) <= LaunchDistanceTolerance && AngleDistance(yawA, yawB) <= .01f;
    public static bool CanFineCorrect(Vector3 from, Vector3 to, float yaw)
        => float.IsFinite(yaw) && Nearby(from, to) && Vector3.Distance(from, to) <= FinalCorrectionLimit;
    public static bool ShowPairHud(bool enabled, bool fresh, string? state, bool preview)
        => preview || (enabled && fresh && state is "linked" or "incoming" or "outgoing");
    public static bool Aligned(Vector3 a, float yawA, Vector3 b, float yawB)
        => Vector3.Distance(a, b) <= .01f && AngleDistance(yawA, yawB) <= .01f;
    public static float AngleDistance(float a, float b)
        => float.IsFinite(a) && float.IsFinite(b) ? MathF.Abs(MathF.IEEERemainder(a - b, MathF.Tau)) : float.PositiveInfinity;
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string Identity(string name, uint homeWorld, uint currentWorld, uint territory)
        => Hash($"es-pair-v2\n{name.Trim().ToUpperInvariant()}\n{homeWorld}\n{currentWorld}\n{territory}");
    public static string Family(string modName) => Hash("es-family-v1\n" + modName.Trim().ToUpperInvariant());
    public static long StartTime(long sent, long received, int delay)
    {
        if (received < sent || received - sent > 600 || delay < 0 || delay > 5000)
            throw new InvalidOperationException("Unreliable relay timing; please retry.");
        return sent + (received - sent) / 2 + delay;
    }
    public static bool Nearby(Vector3 a, Vector3 b)
        => float.IsFinite(a.X + a.Y + a.Z + b.X + b.Y + b.Z) &&
           Vector3.Distance(a, b) <= 2f && MathF.Abs(a.Y - b.Y) <= .25f;
    public static bool PeerMayHaveStarted(string state, long startAt, long now)
        => state == "scheduled" && startAt > 0 && now >= startAt - 250;
}
