using System.Security.Cryptography;
using System.Text.Json;

namespace EmoteShelf;
internal static class ResourceProof
{
    internal static string[] Paths(string directory, Dictionary<string, string[]> options, string[] required)
    {
        var paths = new HashSet<string>(required, StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(f => Path.GetFileName(f) is "meta.json" or "default_mod.json" || Path.GetFileName(f).StartsWith("group_")))
        {
            if (new FileInfo(file).Length > 2*1024*1024) throw new InvalidDataException("resource_metadata_size");
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            if (Path.GetFileName(file) is "meta.json" or "default_mod.json") Collect(root);
            if (root.TryGetProperty("Groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
                foreach (var group in groups.EnumerateArray()) CollectGroup(group);
            else CollectGroup(root);
        }
        void CollectGroup(JsonElement root)
        {
            if (root.TryGetProperty("Name", out var name) && options.TryGetValue(name.GetString() ?? "", out var selected) &&
                     root.TryGetProperty("Options", out var choices))
                foreach (var choice in choices.EnumerateArray())
                    if (choice.TryGetProperty("Name", out var choiceName) && selected.Contains(choiceName.GetString())) Collect(choice);
        }
        if (paths.Count is 0 or > 128) throw new InvalidDataException("resource_count");
        return paths.Order(StringComparer.Ordinal).ToArray();
        void Collect(JsonElement node)
        {
            if (node.TryGetProperty("DefaultData", out var defaults)) Collect(defaults);
            foreach (var key in new[] { "Files", "FileSwaps" })
                if (node.TryGetProperty(key, out var files) && files.ValueKind == JsonValueKind.Object)
                    foreach (var file in files.EnumerateObject())
                    {
                        var path = file.Name.Replace('\\', '/').ToLowerInvariant();
                        // A default group may replace many unrelated emotes. Verify only
                        // the selected command's PAPs, plus its selected ancillary assets.
                        if (SyncRules.ValidPath(path) && (!path.EndsWith(".pap") || required.Contains(path))) paths.Add(path);
                    }
        }
    }
    internal static ResourceDigest[] Hash(Dictionary<string, string> files)
    {
        long total = 0;
        var result = new List<ResourceDigest>();
        foreach (var (game, file) in files.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!SyncRules.ValidPath(game) || !Path.IsPathRooted(file)) throw new InvalidDataException("resource_unresolved");
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            total += stream.Length;
            if (stream.Length > 64*1024*1024 || total > 128*1024*1024) throw new InvalidDataException("resource_size");
            result.Add(new(game, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()));
        }
        return result.ToArray();
    }
    internal static bool Equal(ResourceDigest[] a, ResourceDigest[] b) => a.Length == b.Length &&
        a.OrderBy(p => p.Path).SequenceEqual(b.OrderBy(p => p.Path));
}
