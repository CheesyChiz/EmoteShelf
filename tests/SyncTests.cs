using EmoteShelf;
internal static class SyncTests
{
    internal static void Run()
    {
        if (Math.Abs(SyncRules.Drift(.05, 9.95, 10)-.1) > .00001 || SyncRules.Drift(1, 11, 10) != 0)
            throw new Exception("Wrapped phase drift failed");
        foreach (var path in new[] { "../secret.pap", "C:/secret.pap", "chara/../../a.pap", "chara/a.dll" })
            if (SyncRules.ValidPath(path)) throw new Exception("Unsafe resource path accepted");
        if (SyncRules.ValidPhase(double.NaN, 1) || SyncRules.ValidPhase(2, 1) || SyncRules.ValidPhase(0, 9999))
            throw new Exception("Invalid phase accepted");
        var digest = new ResourceDigest("chara/human/test.pap", new string('a', 64));
        if (!SyncRules.ValidAssets([digest]) || SyncRules.ValidAssets([digest, digest])) throw new Exception("Digest validation failed");
        if (!ResourceProof.Equal([digest], [digest]) || ResourceProof.Equal([digest], [digest with { Sha256 = new string('b', 64) }]))
            throw new Exception("Resource identity check failed");
        var root = Path.Combine(Path.GetTempPath(), "EmoteShelfProof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "default_mod.json"), """{"Files":{"chara/a.pap":"a.pap","chara/other.pap":"b.pap","sound/test.scd":"a.scd"}}""");
            File.WriteAllText(Path.Combine(root, "group_001.json"), """{"Name":"Song","Options":[{"Name":"A","Files":{"sound/a.scd":"a.scd"}},{"Name":"B","Files":{"sound/b.scd":"b.scd"}}]}""");
            var paths = ResourceProof.Paths(root, new() { ["Song"] = ["A"] }, ["chara/a.pap"]);
            if (!paths.SequenceEqual(new[] { "chara/a.pap", "sound/a.scd", "sound/test.scd" })) throw new Exception("Selected resource manifest includes wrong role/options");
            File.WriteAllText(Path.Combine(root, "meta.json"), """{"DefaultData":{"Files":{"sound/base.scd":"base.scd"}},"Groups":[{"Name":"V4","Options":[{"Name":"A","Files":{"sound/v4.scd":"v4.scd"}},{"Name":"B","Files":{"sound/unselected.scd":"other.scd"}}]}]}""");
            var v4 = ResourceProof.Paths(root, new() { ["V4"] = ["A"] }, ["chara/a.pap"]);
            if (!v4.Contains("sound/base.scd") || !v4.Contains("sound/v4.scd") || v4.Contains("sound/unselected.scd"))
                throw new Exception("V4 resource manifest selections failed");
            var asset = Path.Combine(root, "a.pap"); File.WriteAllText(asset, "one");
            var first = ResourceProof.Hash(new() { ["chara/a.pap"] = asset });
            File.WriteAllText(asset, "two");
            if (ResourceProof.Equal(first, ResourceProof.Hash(new() { ["chara/a.pap"] = asset }))) throw new Exception("Changed asset accepted");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Selected resource manifests, changed-file checksums, schema and wrapped phase tests passed.");
    }
}
