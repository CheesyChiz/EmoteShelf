using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using NoireLib.Animations.PapFormat;

namespace EmoteShelf;

// Creates an independent snapshot. Never rewrites or removes source-mod files.
internal static class CarrierConversion
{
    internal sealed record Redirect(string Source, string Target, string[] RequiredNames);

    internal static string? TargetPath(string path, IReadOnlyDictionary<int, string> source, IReadOnlyDictionary<int, string> target)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        if (!normalized.StartsWith("chara/") || !normalized.Contains("/emote/") || !normalized.EndsWith(".pap")) return null;
        foreach (var (slot, key) in source.OrderByDescending(p => p.Value.Length))
        {
            var suffix = "/" + key + ".pap";
            if (!normalized.EndsWith(suffix)) continue;
            if (!target.TryGetValue(slot, out var replacement)) throw new InvalidDataException("carrier_slot_missing");
            return normalized[..^suffix.Length] + "/" + replacement + ".pap";
        }
        return null;
    }

    internal static string[] SourcePaths(string directory)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Manifests(directory))
        {
            if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException("carrier_manifest_size");
            NoLinks(directory, file);
            var node = JsonNode.Parse(File.ReadAllText(file)) ?? throw new InvalidDataException("carrier_manifest");
            foreach (var map in Maps(node))
                foreach (var pair in map) paths.Add(pair.Key.Replace('\\', '/').ToLowerInvariant());
        }
        return paths.ToArray();
    }

    private static IEnumerable<string> Manifests(string root) => new[] { Path.Combine(root, "default_mod.json") }
        .Concat(Directory.EnumerateFiles(root, "group_*.json", SearchOption.TopDirectoryOnly)).Where(File.Exists);

    private static IEnumerable<JsonObject> Maps(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToArray())
            {
                if (pair.Key == "Files" && pair.Value is JsonObject map) yield return map;
                else if (pair.Key == "FileSwaps" && pair.Value is JsonObject swaps && swaps.Count > 0)
                    throw new InvalidDataException("carrier_file_swaps_unsupported");
                else foreach (var nested in Maps(pair.Value)) yield return nested;
            }
        else if (node is JsonArray array)
            foreach (var item in array)
                foreach (var nested in Maps(item)) yield return nested;
    }

    private static void NoLinks(string root, string path)
    {
        var current = Path.GetFullPath(path);
        var boundary = Path.GetFullPath(root);
        while (current.Length >= boundary.Length)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("carrier_link");
            if (current.Equals(boundary, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current)!;
        }
    }

    internal static string Create(string modRoot, string sourceDirectory, string command, IReadOnlyList<Redirect> redirects)
    {
        if (Path.GetFileName(sourceDirectory) != sourceDirectory || sourceDirectory is "" or "." or ".." || redirects.Count == 0)
            throw new InvalidDataException("carrier_source");
        var source = Path.Combine(modRoot, sourceDirectory);
        var blobs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        byte[] Read(string relative)
        {
            relative = ModPackage.SafePath(relative);
            if (blobs.TryGetValue(relative, out var cached)) return cached;
            var path = Path.Combine(source, relative);
            NoLinks(source, path);
            var size = new FileInfo(path).Length;
            if (size > 64 * 1024 * 1024 || total + size > 128 * 1024 * 1024 || blobs.Count >= 4096)
                throw new InvalidDataException("carrier_size");
            using var stream = File.OpenRead(path);
            if (stream.Length != size) throw new InvalidDataException("carrier_source_changed");
            var bytes = new byte[(int)size]; stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new InvalidDataException("carrier_source_changed");
            total += size; blobs[relative] = bytes; return bytes;
        }
        var outputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        JsonNode ReadJson(string relative)
        {
            if (new FileInfo(Path.Combine(source, relative)).Length > 2 * 1024 * 1024)
                throw new InvalidDataException("carrier_manifest_size");
            return JsonNode.Parse(Read(relative)) ?? throw new InvalidDataException("carrier_manifest");
        }
        var count = 0;
        foreach (var file in Manifests(source))
        {
            var name = Path.GetFileName(file);
            var root = ReadJson(name);
            foreach (var map in Maps(root))
                foreach (var pair in map.ToArray())
                {
                    var local = pair.Value?.GetValue<string>() ?? throw new InvalidDataException("carrier_mapping");
                    var original = Read(local);
                    var redirect = redirects.FirstOrDefault(r => r.Source.Equals(pair.Key.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
                    if (redirect is null) continue;
                    if (map.ContainsKey(redirect.Target) && !redirect.Target.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("carrier_target_occupied");
                    var bytes = PapRetargeter.Retarget(original, redirect.RequiredNames, true, out _);
                    var actual = PapAnimationNames.Read(bytes);
                    if (redirect.RequiredNames.Length == 0 || redirect.RequiredNames.Any(n => !actual.Contains(n, StringComparer.Ordinal)))
                        throw new InvalidDataException("carrier_names_missing");
                    total += bytes.Length;
                    if (total > 128 * 1024 * 1024) throw new InvalidDataException("carrier_size");
                    var generated = $"es-carrier/{count++:D5}.pap";
                    outputs[generated] = bytes;
                    map.Remove(pair.Key); map[redirect.Target] = generated;
                }
            outputs[name] = JsonSerializer.SerializeToUtf8Bytes(root);
        }
        if (count == 0) throw new InvalidDataException("carrier_no_files");
        var meta = ReadJson("meta.json").AsObject();
        meta.Remove("Id"); meta.Remove("ModId");
        meta["Name"] = "[ES Carrier " + command + "] " + meta["Name"]?.GetValue<string>();
        meta["Description"] = (meta["Description"]?.GetValue<string>() ?? "") + "\nIndependent carrier snapshot created by Emote Shelf. Original mod unchanged. Source: " + sourceDirectory;
        outputs["meta.json"] = JsonSerializer.SerializeToUtf8Bytes(meta);
        foreach (var pair in blobs) outputs.TryAdd(pair.Key, pair.Value);
        // Content-addressed copies can be reused across clicks and plugin restarts.
        // Include file names and lengths to avoid ambiguous concatenation.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, bytes) in outputs.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name + "\0" + bytes.Length + "\0"));
            hash.AppendData(bytes);
        }
        var directory = "EmoteShelfCarrier_" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        var destination = Path.Combine(modRoot, directory);
        if (Directory.Exists(destination))
        {
            foreach (var (relative, bytes) in outputs)
            {
                var path = Path.Combine(destination, ModPackage.SafePath(relative));
                NoLinks(destination, path);
                if (new FileInfo(path).Length != bytes.Length || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException("carrier_cache_changed");
            }
            if (Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Count() != outputs.Count)
                throw new InvalidDataException("carrier_cache_changed");
            return directory;
        }
        Directory.CreateDirectory(destination);
        foreach (var (relative, bytes) in outputs)
        {
            var path = Path.Combine(destination, ModPackage.SafePath(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            output.Write(bytes);
        }
        return directory;
    }
}
