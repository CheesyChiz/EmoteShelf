namespace EmoteShelf;

internal static class EmotePresentation
{
    // Presentation only: never remove facial resources or change playback/bookmarks.
    internal static bool IsSecondary(string command, IEnumerable<string> visibleCommands)
        => command.Equals("/content", StringComparison.OrdinalIgnoreCase) &&
           visibleCommands.Any(other => !other.Equals("/content", StringComparison.OrdinalIgnoreCase));
}
