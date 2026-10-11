using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EmoteShelf;

internal static class ModPackage
{
    internal const int MaxCompressed = 32 * 1024 * 1024;
    internal const int MaxExpanded = 128 * 1024 * 1024;
    internal static string DisplayFileName(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c) && !"/\\:*?\"<>|".Contains(c)).ToArray()).Trim();
        if (clean.Length == 0) clean = "Mod";
        if (clean.Length > 96) clean = clean[..(char.IsHighSurrogate(clean[95]) ? 95 : 96)];
        return clean + ".pmp";
    }
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".json", ".mdl", ".mtrl", ".tex", ".atex", ".avfx", ".pap", ".tmb", ".scd", ".sklb", ".skp", ".eid", ".phyb", ".imc", ".eqp", ".eqdp", ".est", ".gmp", ".kdb", ".png", ".jpg", ".jpeg" };

    internal static string SafePath(string path)
    {
        path = path.Replace('\\', '/');
        if (path.Length is 0 or > 220 || path.StartsWith('/') || path.Any(c => c < 32 || ":*?\"<>|".Contains(c)))
            throw new InvalidDataException("package_path");
        foreach (var part in path.Split('/'))
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
                throw new InvalidDataException("package_path");
        }
        return path;
    }

    internal static byte[] PackMod(string root, string directory)
    {
        if (SafePath(directory).Contains('/')) throw new InvalidDataException("package_path");
        var source = Path.Combine(root, directory);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        void Add(string relative)
        {
            relative = SafePath(relative);
            if (files.ContainsKey(relative)) return;
            if (files.Count >= 4096 || !Extensions.Contains(Path.GetExtension(relative))) throw new InvalidDataException("package_file_type");
            var path = Path.Combine(source, relative);
            for (var current = path; current.Length >= source.Length; current = Path.GetDirectoryName(current)!)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("package_link");
                if (current == source) break;
            }
            using var input = File.OpenRead(path);
            var size = input.Length;
            if (size > 64 * 1024 * 1024 || (total += size) > MaxExpanded ||
                relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && size > 2 * 1024 * 1024)
                throw new InvalidDataException("package_size");
            var bytes = new byte[(int)size]; input.ReadExactly(bytes);
            if (input.ReadByte() != -1) throw new InvalidDataException("package_source_changed");
            files.Add(relative, bytes);
        }
        void References(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array) { foreach (var item in node.EnumerateArray()) References(item); }
            if (node.ValueKind != JsonValueKind.Object) return;
            foreach (var property in node.EnumerateObject())
                if (property.NameEquals("Files"))
                    foreach (var file in property.Value.EnumerateObject()) Add(file.Value.GetString()!);
                else References(property.Value);
        }
        var manifests = new[] { "meta.json", "default_mod.json" }.Concat(
            Directory.EnumerateFiles(source, "group_*.json").Select(Path.GetFileName)!);
        foreach (var name in manifests)
        {
            if (name == "default_mod.json" && !File.Exists(Path.Combine(source, name))) continue;
            Add(name!);
            using var json = JsonDocument.Parse(files[name!], new JsonDocumentOptions { MaxDepth = 32 });
            References(json.RootElement);
        }
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var file in files)
            {
                using (var output = archive.CreateEntry(file.Key, CompressionLevel.Optimal).Open()) output.Write(file.Value);
                if (buffer.Length > MaxCompressed) throw new InvalidDataException("package_size");
            }
        var result = buffer.ToArray();
        Validate(result);
        return result;
    }

    internal static void Validate(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxCompressed) throw new InvalidDataException("package_size");
        using var stream = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count is 0 or > 4096) throw new InvalidDataException("package_entries");
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            // Do not permit links, reparse points, directory entries or ambiguous Windows names.
            var kind = (entry.ExternalAttributes >> 16) & 0xf000;
            if (kind != 0 && kind != 0x8000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("package_link");
            var path = SafePath(entry.FullName);
            if (!files.Add(path) || !Extensions.Contains(Path.GetExtension(path))) throw new InvalidDataException("package_file_type");
            total += entry.Length;
            if (entry.Length > 64 * 1024 * 1024 || total > MaxExpanded) throw new InvalidDataException("package_expansion");
            using var contents = entry.Open();
            var buffer = new byte[65536]; long read = 0; int count;
            while ((count = contents.Read(buffer)) > 0)
                if ((read += count) > entry.Length) throw new InvalidDataException("package_expansion");
            if (read != entry.Length) throw new InvalidDataException("package_truncated");
        }
        if (!files.Contains("meta.json")) throw new InvalidDataException("package_metadata");
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            if (entry.Length > 2 * 1024 * 1024) throw new InvalidDataException("package_metadata");
            using var doc = JsonDocument.Parse(entry.Open(), new JsonDocumentOptions { MaxDepth = 32 });
            if (entry.FullName.Equals("meta.json", StringComparison.OrdinalIgnoreCase) &&
                (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("Name", out var name) ||
                 name.ValueKind != JsonValueKind.String || name.GetString()!.Length is 0 or > 200 || name.GetString()!.Any(char.IsControl)))
                throw new InvalidDataException("package_metadata");
            CheckJson(doc.RootElement, files);
        }
    }

    private static void CheckJson(JsonElement value, HashSet<string> files)
    {
        if (value.ValueKind == JsonValueKind.Array) { foreach (var child in value.EnumerateArray()) CheckJson(child, files); return; }
        if (value.ValueKind != JsonValueKind.Object) return;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in value.EnumerateObject())
        {
            if (!keys.Add(prop.Name)) throw new InvalidDataException("package_metadata");
            if (prop.Name.Equals("Files", StringComparison.OrdinalIgnoreCase) || prop.Name.Equals("FileSwaps", StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("package_metadata");
                foreach (var file in prop.Value.EnumerateObject())
                {
                    SafePath(file.Name);
                    if (file.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("package_metadata");
                    var target = SafePath(file.Value.GetString()!);
                    if (prop.Name.Equals("Files", StringComparison.OrdinalIgnoreCase) && !files.Contains(target))
                        throw new InvalidDataException("package_missing_file");
                }
            }
            CheckJson(prop.Value, files);
        }
    }

    internal static string ExtractNew(byte[] bytes, string root, CancellationToken cancellation = default, Action? extractionCheckpoint = null)
    {
        cancellation.ThrowIfCancellationRequested();
        Validate(bytes);
        cancellation.ThrowIfCancellationRequested();
        var directory = "EmoteShelfReceived_" + Guid.NewGuid().ToString("N");
        var target = Path.Combine(Path.GetFullPath(root), directory);
        if (Directory.Exists(target)) throw new IOException("package_destination");
        // Only complete mods become direct children of Penumbra's root. Keep partial
        // files one level deeper, on the same volume for an atomic final rename.
        var staging = Path.Combine(Path.GetFullPath(root), ".EmoteShelf-staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
        using var zip = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var path = Path.Combine(staging, SafePath(entry.FullName).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var source = entry.Open();
            var buffer = new byte[65536];
            int count;
            while ((count = source.Read(buffer)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, count);
            }
            extractionCheckpoint?.Invoke();
        }
        cancellation.ThrowIfCancellationRequested();
        // Keep the sender's display name exactly as packaged. Only the generated
        // directory and removed stable IDs provide import isolation, not a name prefix.
        var metaPath = Path.Combine(staging, "meta.json");
        var meta = JsonNode.Parse(File.ReadAllText(metaPath))!.AsObject();
        foreach (var key in meta.Select(p => p.Key).Where(k => k.Equals("Id", StringComparison.OrdinalIgnoreCase) || k.Equals("ModId", StringComparison.OrdinalIgnoreCase) || k.Equals("Identifier", StringComparison.OrdinalIgnoreCase)).ToArray()) meta.Remove(key);
        File.WriteAllText(metaPath, meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        cancellation.ThrowIfCancellationRequested();
        Directory.Move(staging, target);
        return directory;
        }
        finally
        {
            // This exact GUID directory is ours and has never been registered with Penumbra.
            // Cleanup failure must not mask the original extraction/cancellation error.
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
