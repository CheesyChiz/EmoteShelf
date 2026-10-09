using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace EmoteShelf;

public static class PairRules
{
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
