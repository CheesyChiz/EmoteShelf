using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmoteShelf;

public sealed record EmoteVariant(string Group, string Option, string[] Paths);
public sealed record EmoteMod(string Directory, string Name, string EmoteName, string Command, uint Icon, string[] Paths,
    EmoteVariant[] Variants);

public static partial class ModScanner
{
    [GeneratedRegex(@"(?<![\w/])/[a-z][a-z0-9]{2,}(?![\w/])", RegexOptions.IgnoreCase)]
    private static partial Regex SlashCommand();

    public static List<EmoteMod> Scan(string root, IDictionary<string, string> mods, EmoteCatalog catalog,
        Func<string, IEnumerable<string>> changedItems)
    {
        var result = new List<EmoteMod>();
        foreach (var (directory, displayName) in mods)
        {
            // Penumbra supplies directory names; never use an untrusted mod name as a path.
            if (string.IsNullOrWhiteSpace(directory) || Path.GetFileName(directory) != directory) continue;
            var modRoot = Path.GetFullPath(Path.Combine(root, directory));
            if (!modRoot.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) continue;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var variants = new List<EmoteVariant>();
            string description = "";
            foreach (var file in new[] { "meta.json", "default_mod.json" }.Concat(
                         Directory.Exists(modRoot) ? Directory.EnumerateFiles(modRoot, "group_*.json", SearchOption.TopDirectoryOnly) : []))
            {
                var full = Path.IsPathRooted(file) ? file : Path.Combine(modRoot, file);
                if (!File.Exists(full)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(full));
                    if (Path.GetFileName(full).Equals("meta.json", StringComparison.OrdinalIgnoreCase)
                        && doc.RootElement.TryGetProperty("Description", out var d)) description = d.GetString() ?? "";
                    Collect(doc.RootElement, paths);
                    CollectVariants(doc.RootElement, variants);
                }
                catch (IOException) { }
                catch (JsonException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (paths.Count == 0) continue;
            var byCommand = new Dictionary<string, (string Name, uint Icon, List<string> Paths)>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var match = catalog.Resolve(path);
                if (match is null) continue;
                var (name, command, icon) = match.Value;
                if (!byCommand.TryGetValue(command, out var entry)) entry = (name, icon, []);
                entry.Paths.Add(path);
                byCommand[command] = entry;
            }
            try
            {
                foreach (var changed in changedItems(directory))
                {
                    var match = catalog.ResolveChangedItem(changed);
                    if (match is null) continue;
                    var (name, command, icon) = match.Value;
                    if (!byCommand.ContainsKey(command)) byCommand[command] = (name, icon, [.. paths]);
                }
            }
            catch { /* Some Penumbra versions may not expose changed items. */ }
            if (byCommand.Count == 0)
            {
                var inferred = InferCommands(description);
                foreach (var command in inferred) byCommand[command] = (command, 0, [.. paths]);
            }
            if (byCommand.Count == 0) byCommand[""] = ("Unknown emote", 0, [.. paths]);
            foreach (var (command, entry) in byCommand)
            {
                var relevant = variants.Where(v => v.Paths.Any(p => entry.Paths.Contains(p, StringComparer.OrdinalIgnoreCase)))
                    .DistinctBy(v => (v.Group, v.Option)).ToArray();
                result.Add(new EmoteMod(directory, displayName, entry.Name, command, entry.Icon,
                    [.. entry.Paths.Order(StringComparer.OrdinalIgnoreCase)], relevant));
            }
        }
        return [.. result.OrderBy(m => m.EmoteName, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static void Collect(JsonElement element, HashSet<string> paths)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name is "Files" or "FileSwaps" && prop.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var file in prop.Value.EnumerateObject())
                    {
                        var path = file.Name.Replace('\\', '/').ToLowerInvariant();
                        if (path.Contains("/emote/") && path.EndsWith(".pap")) paths.Add(path);
                    }
                }
                Collect(prop.Value, paths);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) Collect(child, paths);
    }

    private static void CollectVariants(JsonElement root, List<EmoteVariant> variants)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("Groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
                foreach (var group in groups.EnumerateArray()) AddGroup(group, variants);
            else if (root.TryGetProperty("Options", out _)) AddGroup(root, variants);
        }
    }

    private static void AddGroup(JsonElement group, List<EmoteVariant> variants)
    {
        if (group.ValueKind != JsonValueKind.Object ||
            !group.TryGetProperty("Type", out var type) || type.ValueKind != JsonValueKind.String ||
            !string.Equals(type.GetString(), "Single", StringComparison.OrdinalIgnoreCase) ||
            !group.TryGetProperty("Options", out var options) || options.ValueKind != JsonValueKind.Array) return;
        var groupName = group.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
        if (groupName.Length == 0) return;
        foreach (var option in options.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object) continue;
            var optionName = option.TryGetProperty("Name", out var name) ? name.GetString() ?? "" : "";
            if (optionName.Length == 0) continue;
            var optionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Collect(option, optionPaths);
            if (optionPaths.Count > 0) variants.Add(new EmoteVariant(groupName, optionName, [.. optionPaths]));
        }
    }

    public static List<string> InferCommands(string description)
    {
        // Only trust a command explicitly described as the replaced emote.
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in description.Split('\n'))
        {
            if (!line.Contains("replac", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("замен", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Match m in SlashCommand().Matches(line)) result.Add(m.Value.ToLowerInvariant());
        }
        return [.. result.Order(StringComparer.OrdinalIgnoreCase)];
    }

    public static bool ValidCommand(string value)
        => SlashCommand().Match(value.Trim()) is { Success: true } match && match.Value == value.Trim();
}
