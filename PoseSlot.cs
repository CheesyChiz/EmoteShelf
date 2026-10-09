using System.Text.RegularExpressions;

namespace EmoteShelf;

/// <summary>Reads the zero-based game pose slot from animation filenames.</summary>
public static class PoseSlot
{
    public static int[] AllFromPaths(IEnumerable<string> paths, string prefix)
    {
        var pattern = $@"^{Regex.Escape(prefix)}pose(\d{{2}})_(?:start|loop)\.pap$";
        return paths.Select(path => Regex.Match(Path.GetFileName(path), pattern, RegexOptions.IgnoreCase))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value))
            .Distinct().Order().ToArray();
    }

    public static int? FromPaths(IEnumerable<string> paths, string prefix)
    {
        var slots = AllFromPaths(paths, prefix);
        return slots.Length == 1 ? slots[0] : null;
    }
}
