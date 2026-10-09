namespace EmoteShelf;

public static class ResolvedAnimation
{
    // Exact option files are not authoritative: Multi/Combining groups and
    // group priorities can legitimately replace them within the selected mod.
    public static bool BelongsToMod(string root, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? resolved)
        => !string.IsNullOrWhiteSpace(resolved) && Path.IsPathRooted(resolved) &&
           Path.GetFullPath(resolved).StartsWith(
               Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
               StringComparison.OrdinalIgnoreCase);
}
