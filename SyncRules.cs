using System.Text.RegularExpressions;

namespace EmoteShelf;
internal static class SyncRules
{
    internal static bool ValidPath(string path) => path is { Length: > 0 and <= 220 } && !path.Contains("..") &&
        Regex.IsMatch(path, @"^(chara|sound|vfx)/[a-z0-9_/.-]+\.(pap|tmb|scd|avfx|atex|tex|mdl|mtrl|sklb)$");
    internal static bool ValidAssets(ResourceDigest[] assets) => assets is { Length: <= 128 } &&
        assets.All(a => a is not null && ValidPath(a.Path) && a.Sha256 is not null && Regex.IsMatch(a.Sha256, "^[0-9a-f]{64}$")) &&
        assets.Select(a => a.Path).Distinct().Count() == assets.Length;
    internal static bool ValidPhase(double phase, double duration) => double.IsFinite(phase) && double.IsFinite(duration) &&
        phase >= 0 && duration >= 0 && duration <= 3600 && phase <= duration;
    internal static double Drift(double local, double remote, double duration)
    {
        if (duration <= 0 || !double.IsFinite(local) || !double.IsFinite(remote)) return double.NaN;
        var difference = Math.Abs((local-remote) % duration);
        return Math.Min(difference, duration-difference);
    }
}
