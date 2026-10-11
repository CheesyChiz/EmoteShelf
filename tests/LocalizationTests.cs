using System.Reflection;
using System.Text.RegularExpressions;
using EmoteShelf;

internal static class LocalizationTests
{
    internal static void Run()
    {
        // Force the raw-table parser to run before inspecting its complete dictionary.
        _ = Localization.Get("ja", "", "Settings");
        var entries = (Dictionary<string, (string Ja, string De, string Fr)>)typeof(Localization)
            .GetField("Text", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        static string Slots(string value) => string.Join(";", Regex.Matches(value, @"\{\d+(?:[^}]*)\}")
            .Select(m => m.Value).Order());
        foreach (var (key, translations) in entries)
            foreach (var text in new[] { translations.Ja, translations.De, translations.Fr })
                if (string.IsNullOrWhiteSpace(text) || Slots(key) != Slots(text))
                    throw new Exception("Missing translation or mismatched format placeholders: " + key);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var configuration = File.ReadAllText(Path.Combine(root, "Configuration.cs"));
        if (configuration.Contains("AutomaticPose") || configuration.Contains("ManualPoseMigration"))
            throw new Exception("Retired automatic-pose preferences must not override always-on playback.");
        foreach (var path in Directory.GetFiles(root, "Plugin*.cs"))
        {
            var source = File.ReadAllText(path);
            if (source.Contains("config.AutomaticPose"))
                throw new Exception("Playback still depends on the retired pose toggle: " + path);
            foreach (Match match in Regex.Matches(source, "\\bT\\(\"(?:[^\"\\\\]|\\\\.)*\",\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\)"))
            {
                var key = System.Text.Json.JsonSerializer.Deserialize<string>("\"" + match.Groups[1].Value + "\"")!;
                if (!entries.ContainsKey(key) && !key.StartsWith("1. On Emotes"))
                    throw new Exception("UI text is missing translations: " + key);
            }
        }
        Console.WriteLine($"Localization: {entries.Count} entries, placeholders and UI coverage passed.");
    }
}
