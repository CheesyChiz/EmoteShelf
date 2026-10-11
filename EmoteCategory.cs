namespace EmoteShelf;

internal static class EmoteCategory
{
    internal static string Key(string directory, string command) => directory.ToLowerInvariant() + "|" + command.Trim().ToLowerInvariant();
    internal static bool Dance(IReadOnlyDictionary<string, bool> categories, string directory, string command, bool? once = null)
        => once ?? categories.GetValueOrDefault(Key(directory, command));
}
