using System.Text.Json.Nodes;
using EmoteShelf;
using NoireLib.Animations.PapFormat;

internal static class CarrierTests
{
    internal static void Run()
    {
        var gate = new CarrierRequestGate();
        var oldClick = gate.Next();
        var latestClick = gate.Next();
        if (gate.IsCurrent(oldClick) || !gate.IsCurrent(latestClick)) throw new Exception("Old carrier click can launch.");
        gate.Cancel();
        if (gate.IsCurrent(latestClick)) throw new Exception("Cancelled carrier click can launch.");
        const string sourcePath = "chara/human/c0101/animation/a0001/bt_common/emote/source_loop.pap";
        const string targetPath = "chara/human/c0101/animation/a0001/bt_common/emote/target_loop.pap";
        if (CarrierConversion.TargetPath(sourcePath, new Dictionary<int, string> { [1] = "source_loop" },
                new Dictionary<int, string> { [1] = "target_loop" }) != targetPath)
            throw new Exception("Carrier timeline-slot mapping failed.");
        Reject(() => CarrierConversion.TargetPath(sourcePath, new Dictionary<int, string> { [1] = "source_loop" }, new Dictionary<int, string>()));
        var root = Path.Combine(Path.GetTempPath(), "EmoteShelfCarrierTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Original"));
        try
        {
            var original = Path.Combine(root, "Original");
            var pap = Fixture();
            File.WriteAllBytes(Path.Combine(original, "original.pap"), pap);
            File.WriteAllBytes(Path.Combine(original, "sound.scd"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(original, "meta.json"), "{\"Name\":\"Fixture\",\"Author\":\"Test\"}");
            var manifest = new JsonObject { ["Files"] = new JsonObject { [sourcePath] = "original.pap", ["sound/test.scd"] = "sound.scd" } }.ToJsonString();
            File.WriteAllText(Path.Combine(original, "default_mod.json"), manifest);
            File.WriteAllText(Path.Combine(original, "group_001.json"), new JsonObject { ["Name"] = "Variant", ["Options"] = new JsonArray(JsonNode.Parse(manifest)) }.ToJsonString());
            var redirect = new CarrierConversion.Redirect(sourcePath, targetPath, ["target_loop"]);
            var packed = ModPackage.PackMod(root, "Original");
            ModPackage.Validate(packed);
            using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(packed)))
                if (zip.GetEntry("original.pap") is null || zip.GetEntry("sound.scd") is null || zip.GetEntry("group_001.json") is null)
                    throw new Exception("Automatic package omitted referenced variants or audio.");
            var result = CarrierConversion.Create(root, "Original", "/dance", [redirect]);
            var copy = Path.Combine(root, result);
            if (CarrierConversion.Create(root, "Original", "/dance", [redirect]) != result)
                throw new Exception("Repeated playback duplicated its carrier copy.");
            var copied = JsonNode.Parse(File.ReadAllText(Path.Combine(copy, "default_mod.json")))!;
            var local = copied["Files"]![targetPath]!.GetValue<string>();
            if (copied["Files"]![sourcePath] is not null ||
                !PapAnimationNames.Read(File.ReadAllBytes(Path.Combine(copy, local))).Contains("target_loop") ||
                !File.ReadAllBytes(Path.Combine(copy, "sound.scd")).SequenceEqual(new byte[] { 1, 2, 3 }) ||
                !File.ReadAllText(Path.Combine(copy, "group_001.json")).Contains("target_loop"))
                throw new Exception("Carrier did not preserve options/sound or retarget PAP names.");
            if (!File.ReadAllBytes(Path.Combine(original, "original.pap")).SequenceEqual(pap) || File.ReadAllText(Path.Combine(original, "default_mod.json")) != manifest)
                throw new Exception("Carrier modified the original.");
            var count = Directory.GetDirectories(root).Length;
            var v4 = Path.Combine(root, "V4");
            Directory.CreateDirectory(v4);
            var special = sourcePath.Replace("/emote/", "/emote_sp/");
            File.WriteAllBytes(Path.Combine(v4, "original.pap"), pap);
            var v4meta = new JsonObject { ["Name"] = "V4", ["Identifier"] = Guid.NewGuid().ToString(),
                ["DefaultData"] = new JsonObject { ["Files"] = new JsonObject { [special] = "original.pap" } },
                ["Groups"] = new JsonArray(new JsonObject { ["Name"] = "Style", ["Type"] = "Single",
                    ["Options"] = new JsonArray(new JsonObject { ["Name"] = "A", ["Files"] = new JsonObject { [special] = "original.pap" } }) }) };
            File.WriteAllText(Path.Combine(v4, "meta.json"), v4meta.ToJsonString());
            if (!CarrierConversion.SourcePaths(v4).Contains(special) ||
                CarrierConversion.TargetPath(special, new Dictionary<int,string>{{1,"emote_sp/source_loop"}},
                    new Dictionary<int,string>{{1,"emote/target_loop"}}) != targetPath)
                throw new Exception("V4 special-emote source discovery failed");
            var v4copy = CarrierConversion.Create(root, "V4", "/dance", [new(special, targetPath, ["target_loop"])]);
            var rewritten = JsonNode.Parse(File.ReadAllText(Path.Combine(root, v4copy, "meta.json")))!;
            if (rewritten["Identifier"] is not null || rewritten["DefaultData"]!["Files"]![targetPath] is null ||
                rewritten["Groups"]![0]!["Options"]![0]!["Files"]![targetPath] is null)
                throw new Exception("V4 rewritten mappings or new identity lost");
            var imported = ModPackage.ExtractNew(ModPackage.PackMod(root, "V4"), root);
            if (JsonNode.Parse(File.ReadAllText(Path.Combine(root, imported, "meta.json")))!["Identifier"] is not null)
                throw new Exception("Received mod retained sender identity");
            count = Directory.GetDirectories(root).Length;
            Reject(() => CarrierConversion.Create(root, "..", "/dance", [redirect]));
            File.WriteAllBytes(Path.Combine(original, "original.pap"), [0, 1, 2]);
            Reject(() => CarrierConversion.Create(root, "Original", "/dance", [redirect]));
            Reject(() => ModPackage.PackMod(root, "../Original"));
            File.WriteAllBytes(Path.Combine(original, "original.pap"), pap);
            File.WriteAllText(Path.Combine(original, "default_mod.json"), new JsonObject { ["Files"] = new JsonObject { [sourcePath] = "../escape.pap" } }.ToJsonString());
            Reject(() => CarrierConversion.Create(root, "Original", "/dance", [redirect]));
            File.WriteAllText(Path.Combine(original, "default_mod.json"), new JsonObject { ["Files"] = new JsonObject { [sourcePath] = "original.pap", [targetPath] = "original.pap" } }.ToJsonString());
            Reject(() => CarrierConversion.Create(root, "Original", "/dance", [redirect]));
            if (Directory.GetDirectories(root).Length != count) throw new Exception("Invalid carrier created an output directory.");
            File.WriteAllText(Path.Combine(original, "default_mod.json"), manifest);
            File.WriteAllBytes(Path.Combine(original, "sound.scd"), [4, 5, 6]);
            if (CarrierConversion.Create(root, "Original", "/dance", [redirect]) == result)
                throw new Exception("Source update reused stale carrier assets.");
            File.WriteAllBytes(Path.Combine(original, "sound.scd"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(copy, local), [0]);
            Reject(() => CarrierConversion.Create(root, "Original", "/dance", [redirect]));
            Console.WriteLine("Carrier conversion tests passed (synthetic PAP, options, sound, source integrity and rejection paths).");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new Exception("Invalid carrier input was accepted.");
    }

    // Synthetic structural fixture; no game or third-party mod assets.
    private static byte[] Fixture()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write("TMLB"u8); writer.Write(44); writer.Write(2);
            writer.Write("TMDH"u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)0); writer.Write((short)0); writer.Write((short)0);
            writer.Write("TMAL"u8); writer.Write(16); writer.Write(0); writer.Write(0);
        }
        stream.Position = 0;
        var pap = new PapFile("");
        var animation = new PapAnimation(pap);
        animation.SetName("source_loop");
        animation.ReadTmb(new BinaryReader(stream));
        pap.Animations.Add(animation);
        return pap.ToBytes();
    }
}
