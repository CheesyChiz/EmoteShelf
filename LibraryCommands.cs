namespace EmoteShelf;

internal static class LibraryCommands
{
    internal static bool ValidAlias(string value) => value.Length is > 0 and <= 32 &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');

    internal static (string Verb, string Argument) Parse(string text)
    {
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (parts.ElementAtOrDefault(0)?.ToLowerInvariant() ?? "", parts.ElementAtOrDefault(1)?.Trim() ?? "");
    }
}
