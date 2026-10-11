using EmoteShelf;
using System.IO.Compression;
using System.Text;

internal static class ModPackageTests
{
    internal static byte[] Package(string file = "anim.pap", string? target = null, int attributes = 0)
    {
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
            var first = ModPackage.ExtractNew(Package(), root);
            var second = ModPackage.ExtractNew(Package(), root);
            if (first == second || !File.Exists(Path.Combine(root, first, "anim.pap"))) throw new Exception("Import isolation failed");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine("Package traversal, executable, symlink, reference and isolated extraction tests passed.");
    }
}
