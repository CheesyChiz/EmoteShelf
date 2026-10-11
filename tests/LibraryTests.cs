using EmoteShelf;

internal static class LibraryTests
{
    internal static void Run()
    {
        foreach (var invalid in new[] { "", "two words", "/dance", "x\n/yell", "a;b", "../x", new string('a', 33) })
            if (LibraryCommands.ValidAlias(invalid)) throw new Exception("Unsafe alias accepted");
        if (!LibraryCommands.ValidAlias("dance_A-2")) throw new Exception("Valid alias rejected");
        if (LibraryCommands.Parse("  PLAY dance_A-2  ") != ("play", "dance_A-2")) throw new Exception("Alias parsing");
        var schema = new Dictionary<string, string[]> { ["Role"] = ["A", "B"], ["Extras"] = ["Music", "Face"] };
        var saved = new Dictionary<string, List<string>> { ["Role"] = ["A"], ["Extras"] = ["Music", "Face"] };
        if (BookmarkValidation.MissingOptions(saved, schema).Length != 0) throw new Exception("Valid preset rejected");
        schema["Role"] = ["B"];
        if (!BookmarkValidation.MissingOptions(saved, schema).SequenceEqual(["Role → A"])) throw new Exception("Removed option not detected");
        schema.Remove("Extras");
        if (BookmarkValidation.MissingOptions(saved, schema).Length != 2) throw new Exception("Removed group not detected");
        if (BookmarkValidation.MissingOptions(null, schema).Length != 0 || saved["Role"][0] != "A")
            throw new Exception("Validation modified preset");
        Console.WriteLine("Library aliases and stale preset validation passed.");
    }
}
