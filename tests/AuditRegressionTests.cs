using EmoteShelf;

internal static class AuditRegressionTests
{
    internal static void Run()
    {
        var danceTarget = PairRules.DancePosition(System.Numerics.Vector3.Zero, 0);
        var dancer = new System.Numerics.Vector3(2.00161f, .05f, -.00013f);
        var grounded = PairRules.GroundedDestination(danceTarget, dancer, true);
        if (grounded.Y != dancer.Y || grounded.X != danceTarget.X || grounded.Z != danceTarget.Z ||
            !PairRules.ReadyToLaunch(dancer, 0, grounded, 0) ||
            PairRules.GroundedDestination(danceTarget, dancer, false) != danceTarget ||
            PairRules.ReadyToLaunch(dancer, 1.22f, grounded, 0))
            throw new Exception("Dance slope handling must preserve spacing, facing and ordinary pair geometry");
        var highDancer = new System.Numerics.Vector3(2, .3f, 0);
        if (PairRules.ReadyToLaunch(highDancer, 0,
            PairRules.GroundedDestination(danceTarget, highDancer, true), 0))
            throw new Exception("Dance slope handling must reject large height differences");
        foreach (var dance in new[] { false, true })
            if (!PairRules.NearbyForMode(System.Numerics.Vector3.Zero, new(10, 0, 0), dance) ||
                PairRules.NearbyForMode(System.Numerics.Vector3.Zero, new(10.01f, 0, 0), dance) ||
                PairRules.NearbyForMode(System.Numerics.Vector3.Zero, new(1, 1, 0), dance) ||
                PairRules.ReadyToLaunch(System.Numerics.Vector3.Zero, 0, new(10, 0, 0), 0))
                throw new Exception("Invitation distance must not relax launch geometry or height safety");
        var categories = new Dictionary<string, bool> { [EmoteCategory.Key("Dance", "/stepdance")] = true };
        if (!EmoteCategory.Dance(categories, "DANCE", "/STEPDANCE") ||
            EmoteCategory.Dance(categories, "Other", "/stepdance") ||
            EmoteCategory.Dance(categories, "Dance", "/groundsit") ||
            EmoteCategory.Dance(categories, "Dance", "/stepdance", false) ||
            !EmoteCategory.Dance(categories, "Dance", "/stepdance"))
            throw new Exception("Per-emote category or one-shot override changed saved defaults");
        var root = Path.Combine(Path.GetTempPath(), "es-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var mods = new Dictionary<string, string>();
            var malformed = new[] { "[]", "{\"Description\":12}",
                "{\"Type\":\"Single\",\"Name\":12,\"Options\":[]}",
                "{\"Type\":\"Single\",\"Name\":\"Role\",\"Options\":[1]}" };
            for (var i = 0; i < malformed.Length; i++)
            {
                var name = "bad" + i;
                mods.Add(name, name);
                Directory.CreateDirectory(Path.Combine(root, name));
                File.WriteAllText(Path.Combine(root, name, "meta.json"), malformed[i]);
            }
            mods.Add("good", "Good");
            Directory.CreateDirectory(Path.Combine(root, "good"));
            const string gamePath = "chara/human/c0101/animation/a0001/bt_common/emote/loop_emot19.pap";
            if (ModScanner.IsEmotePath("Pose Select/Bottom/" + gamePath) || !ModScanner.IsEmotePath(gamePath))
                throw new Exception("Package-local metadata path was treated as a game resource");
            File.WriteAllText(Path.Combine(root, "good", "default_mod.json"),
                "{\"Files\":{\"" + gamePath + "\":\"test.pap\"}}");
            var found = ModScanner.Scan(root, mods, new EmoteCatalog(), _ => []);
            if (!found.Any(m => m.Directory == "good" && m.Command == "/hum"))
                throw new Exception("Malformed metadata prevented scanning a valid mod");

            var asset = Path.Combine(root, "large.pap");
            using (var file = File.Create(asset)) file.SetLength(65L * 1024 * 1024);
            if (ResourceProof.Hash(new() { [gamePath] = asset }).Length != 1)
                throw new Exception("Permitted asset rejected by resource proof");
            using (var file = File.OpenWrite(asset)) file.SetLength((long)ModPackage.MaxAsset + 1);
            try
            {
                ResourceProof.Hash(new() { [gamePath] = asset });
                throw new Exception("Oversized resource accepted");
            }
            catch (InvalidDataException ex) when (ex.Message == "resource_size") { }
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Audit regressions: malformed metadata isolation and resource size boundaries passed.");
    }
}
