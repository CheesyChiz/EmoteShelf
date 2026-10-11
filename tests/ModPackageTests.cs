using EmoteShelf;
using System.IO.Compression;
using System.Text;

internal static class ModPackageTests
{
    internal static byte[] Package(string file = "anim.pap", string? target = null, int attributes = 0)
    {
        var label = ModPackage.DisplayFileName("PM ～ BABYMONSTER - ‘DRIP’");
        if (!label.Contains("BABYMONSTER") || !label.EndsWith(".pmp") ||
            ModPackage.DisplayFileName(new string('x', 150)).Length > 100 ||
            ModPackage.DisplayFileName("bad\n/name").Contains('\n'))
            throw new Exception("Transfer package label invalid");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string path, string content, int attrs = 0)
            {
                var entry = zip.CreateEntry(path); entry.ExternalAttributes = attrs;
                using var output = entry.Open(); output.Write(Encoding.UTF8.GetBytes(content));
            }
            Add("meta.json", "{\"Name\":\"Test\",\"FileVersion\":3}");
            Add("default_mod.json", System.Text.Json.JsonSerializer.Serialize(new { Files = new Dictionary<string, string> { ["chara/test.pap"] = target ?? file } }));
            Add(file, "test", attributes);
        }
        return stream.ToArray();
    }
    internal static void Run()
    {
        ModPackage.Validate(Package());
        ModPackage.Validate(Package("face.kdb"));
        foreach (var bad in new[] { "../evil.pap", "C:/evil.pap", "x:stream.pap", "CON.pap", "x./evil.pap", "run.dll", "run.ps1", "x\\..\\evil.tex", "a//b.tex" })
        {
            try { ModPackage.Validate(Package(bad)); throw new Exception("Unsafe package accepted: " + bad); }
            catch (InvalidDataException) { }
        }
        try { ModPackage.Validate(Package(target: "missing.pap")); throw new Exception("Missing dependency accepted"); } catch (InvalidDataException) { }
        try { ModPackage.Validate(Package(attributes: unchecked((int)0xA0000000))); throw new Exception("Symlink accepted"); } catch (InvalidDataException) { }
        var root = Path.Combine(Path.GetTempPath(), "EmoteShelfPackageTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var source = Path.Combine(root, "Source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "meta.json"), """
                {"Name":"All variants","DefaultData":{"Files":{"chara/base.pap":"base.pap"}},
                "Groups":[{"Name":"Choice","Type":"Single","DefaultSettings":0,"Options":[
                {"Name":"A","Files":{"chara/anim.pap":"a.pap"}},
                {"Name":"B","Files":{"chara/anim.pap":"b.pap","chara/face.kdb":"face.kdb"}}]}]}
                """);
            foreach (var name in new[] { "base.pap", "a.pap", "b.pap", "face.kdb", "unrelated.exe" })
                File.WriteAllText(Path.Combine(source, name), name);
            var whole = ModPackage.PackMod(root, "Source");
            using (var zip = new ZipArchive(new MemoryStream(whole)))
            {
                if (!zip.Entries.Select(e => e.FullName).Order().SequenceEqual(
                    new[] { "meta.json", "base.pap", "a.pap", "b.pap", "face.kdb" }.Order()))
                    throw new Exception("Whole-mod packaging dropped an unselected option or included an unrelated file");
            }
            var first = ModPackage.ExtractNew(Package(), root);
            var second = ModPackage.ExtractNew(Package(), root);
            if (first == second || !File.Exists(Path.Combine(root, first, "anim.pap"))) throw new Exception("Import isolation failed");
            foreach (var imported in new[] { first, second })
            {
                using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, imported, "meta.json")));
                if (metadata.RootElement.GetProperty("Name").GetString() != "Test")
                    throw new Exception("Import changed the sender's display name");
            }
            const string originalName = "[Автор] Танец ～ ‘DRIP’ 舞踊";
            var sourceMeta = Path.Combine(source, "meta.json");
            var original = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(sourceMeta))!;
            original["Name"] = originalName;
            File.WriteAllText(sourceMeta, original.ToJsonString());
            var namedCopy = ModPackage.ExtractNew(ModPackage.PackMod(root, "Source"), root);
            using var namedMetadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, namedCopy, "meta.json")));
            if (namedMetadata.RootElement.GetProperty("Name").GetString() != originalName)
                throw new Exception("Whole-mod transfer changed Unicode display name");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine("Package traversal, executable, symlink, reference and isolated extraction tests passed.");
    }
}
