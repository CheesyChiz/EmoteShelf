namespace EmoteShelf;

internal static class BookmarkValidation
{
    // Return identifiers, not localized log messages. Compare names exactly as Penumbra does.
    internal static string[] MissingOptions(IReadOnlyDictionary<string, List<string>>? saved,
        IReadOnlyDictionary<string, string[]> available)
    {
        if (saved is null) return [];
        return saved.SelectMany(group => !available.TryGetValue(group.Key, out var options)
            ? new[] { group.Key }
            : group.Value.Where(value => !options.Contains(value, StringComparer.Ordinal))
                .Select(value => group.Key + " → " + value)).ToArray();
    }
}
