using EmoteShelf;

static void Check(string name, int? actual, int? expected)
{
    if (actual != expected) throw new Exception($"{name}: expected {expected}, got {actual}");
}

Check("initial ground sit", PoseSlot.FromPaths(["chara/human/c0101/animation/a0001/bt_common/emote/j_pose00_loop.pap"], "j_"), 0);
Check("Asian Squat", PoseSlot.FromPaths(["chara/human/c0101/animation/a0001/bt_common/emote/j_pose01_loop.pap"], "j_"), 1);
Check("Comfy bottom", PoseSlot.FromPaths(["chara/human/c0101/animation/a0001/bt_common/emote/j_pose03_loop.pap"], "j_"), 3);
Check("Patunia idle", PoseSlot.FromPaths(["chara/human/c0101/animation/a0001/bt_common/emote/pose03_start.pap"], ""), 3);
Check("ambiguous mod", PoseSlot.FromPaths(["j_pose01_loop.pap", "j_pose02_loop.pap"], "j_"), null);
if (!PoseSlot.AllFromPaths(["j_pose02_start.pap", "j_pose01_loop.pap", "j_pose02_loop.pap"], "j_").SequenceEqual([1, 2]))
    throw new Exception("Multi-slot mod: expected [1, 2]");
if (!PoseSlot.AllFromPaths(["pose06_loop.pap", "pose01_start.pap", "pose03_loop.pap"], "").SequenceEqual([1, 3, 6]))
    throw new Exception("Multi-slot idle mod: expected [1, 3, 6]");
Check("unrelated emote", PoseSlot.FromPaths(["dance03_loop.pap"], "j_"), null);
var testRoot = Path.Combine(Path.GetTempPath(), "EmoteShelfPoseTests-" + Guid.NewGuid().ToString("N"));
try
{
    var modRoot = Path.Combine(testRoot, "GroundSitFixture");
    Directory.CreateDirectory(modRoot);
    File.WriteAllText(Path.Combine(modRoot, "meta.json"), """
        {
          "Description": "Replaces /groundsit",
          "Groups": [{
            "Name": "Squat A", "Type": "Single", "Options": [
              { "Name": "[ x ]", "Files": {} },
              { "Name": "Ears ON", "Files": {
                "chara/human/c0101/animation/a0001/bt_common/emote/j_pose01_loop.pap": "squat a\\ears on\\j_pose01_loop.pap"
              }}
            ]
          }]
        }
        """);
    File.WriteAllText(Path.Combine(modRoot, "default_mod.json"), """
        {"Files": {"chara/human/c0101/animation/a0001/bt_common/emote/j_pose01_loop.pap": "default.pap"}}
        """);
    var scanned = ModScanner.Scan(testRoot, new Dictionary<string, string> { ["GroundSitFixture"] = "GroundSitFixture" },
        new EmoteCatalog(), _ => []);
    if (scanned.Count != 1 || scanned[0].Command != "/groundsit" || scanned[0].PoseIndex != 1 ||
        !scanned[0].PoseSlots.SequenceEqual([1]) || scanned[0].BasePaths.Length != 1)
        throw new Exception("Scanner did not map j_pose01 to ground-sit index 1.");
    if (scanned[0].Variants.Length != 2 ||
        !scanned[0].Variants.Single(v => v.Option == "Ears ON").Files.TryGetValue(
            "chara/human/c0101/animation/a0001/bt_common/emote/j_pose01_loop.pap", out var optionFile) ||
        optionFile != @"squat a\ears on\j_pose01_loop.pap")
        throw new Exception("Scanner did not retain the selected option's exact local animation file.");
    var idleRoot = Path.Combine(testRoot, "IdleFixture");
    Directory.CreateDirectory(idleRoot);
    File.WriteAllText(Path.Combine(idleRoot, "default_mod.json"), """
        {"Files": {"chara/human/c0101/animation/a0001/bt_common/emote/pose03_loop.pap": "pose03_loop.pap"}}
        """);
    var idle = ModScanner.Scan(testRoot, new Dictionary<string, string> { ["IdleFixture"] = "IdleFixture" },
        new EmoteCatalog(), _ => []);
    if (idle.Count != 1 || idle[0].Command != "/changepose" || idle[0].PoseIndex != 3 ||
        !idle[0].PoseSlots.SequenceEqual([3]))
        throw new Exception("Scanner did not map /changepose pose03 to idle index 3.");
    var multiRoot = Path.Combine(testRoot, "MultiFixture");
    Directory.CreateDirectory(multiRoot);
    File.WriteAllText(Path.Combine(multiRoot, "meta.json"), """
        {"Description":"Replaces /groundsit; use /cpose3","Groups":[
          {"Name":"Pose 1","Type":"Single","Options":[{"Name":"On","Files":{
            "chara/human/c0101/animation/a0001/bt_common/emote/j_pose01_loop.pap":"pose1.pap"}}]},
          {"Name":"Pose 2","Type":"Single","Options":[{"Name":"On","Files":{
            "chara/human/c0101/animation/a0001/bt_common/emote/j_pose02_loop.pap":"pose2.pap"}}]}
        ]}
        """);
    var multi = ModScanner.Scan(testRoot, new Dictionary<string, string> { ["MultiFixture"] = "MultiFixture" },
        new EmoteCatalog(), _ => []);
    if (multi.Count != 1 || multi[0].PoseIndex is not null || !multi[0].PoseSlots.SequenceEqual([1, 2]))
        throw new Exception("Scanner trusted an invalid description instead of the available pose files.");
    var songRoot = Path.Combine(testRoot, "SongFixture");
    Directory.CreateDirectory(songRoot);
    File.WriteAllText(Path.Combine(songRoot, "meta.json"), """
        {"DefaultData":{"Files":{"chara/human/c0101/animation/a0001/bt_common/emote/j_pose01_loop.pap":"base.pap"}},
        "Groups":[{"Name":"Song","Type":"Single","Options":[
        {"Name":"To Zanarkand","Files":{"sound/guitar.scd":"song1.scd"}},
        {"Name":"Town","Files":{"sound/guitar.scd":"song2.scd"}},
        {"Name":"None","Files":{"chara/human/c0101/animation/a0001/bt_common/emote/j_pose01_loop.pap":"silent.pap"}}]},
        {"Name":"Music","Type":"Multi","Options":[{"Name":"On","Files":{"sound/music.scd":"music.scd"}}]}]}
        """);
    var song = ModScanner.Scan(testRoot, new Dictionary<string, string> { ["SongFixture"] = "SongFixture" }, new EmoteCatalog(), _ => []).Single();
    if (song.BasePaths.Length != 1 || song.Variants.Count(v => v.Group == "Song") != 3 || !song.Variants.Single(v => v.Group == "Music").Multi)
        throw new Exception("Song-only options, DefaultData or Multi group were lost.");
}
finally
{
    if (Path.GetFullPath(testRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
        Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
}
Console.WriteLine("Pose slot regression tests passed.");
var ownershipRoot = Path.Combine(Path.GetTempPath(), "EmoteShelfOwnershipFixture");
if (!ResolvedAnimation.BelongsToMod(ownershipRoot, Path.Combine(ownershipRoot, "lip sync", "dance.pap")) ||
    !ResolvedAnimation.BelongsToMod(ownershipRoot, Path.Combine(ownershipRoot, "animation", "dance.pap")) ||
    ResolvedAnimation.BelongsToMod(ownershipRoot, Path.Combine(ownershipRoot + "OtherMod", "dance.pap")) ||
    ResolvedAnimation.BelongsToMod(ownershipRoot, Path.Combine(ownershipRoot, "..", "other.pap")) ||
    ResolvedAnimation.BelongsToMod(ownershipRoot, "chara/human/emote/dance.pap") ||
    ResolvedAnimation.BelongsToMod(ownershipRoot, null))
    throw new Exception("Mod ownership checks rejected a same-mod override or accepted another mod.");
Console.WriteLine("Same-mod lip-sync override and mod-boundary regression tests passed.");
if (FolderTree.Parent("Dance/Pair/Favorites") != "Dance/Pair" || FolderTree.Name("Dance/Pair") != "Pair" ||
    FolderTree.Rebase("Dance/Pair/Favorites", "Dance/Pair", "New") != "New/Favorites" ||
    FolderTree.Contains("Dance", "Dancer") || !FolderTree.Contains("Dance", "Dance/Pair") ||
    !FolderTree.Ancestors("Dance/Pair/Favorites").SequenceEqual(["Dance/Pair", "Dance"]))
    throw new Exception("Nested folder path regression.");
Console.WriteLine("Nested folder rename/move/ancestor tests passed.");
if (PosePlayback.ShouldStandUp("/groundsit", true) || !PosePlayback.ShouldStandUp("/dance", true))
    throw new Exception("Pose playback sent an unsolicited step or unnecessarily reseated the player.");
foreach (var ground in new[] { false, true })
foreach (var active in new[] { false, true })
foreach (var automatic in new[] { false, true })
foreach (var known in new[] { false, true })
{
    var plan = PosePlayback.Prepare(ground, active, automatic, known);
    if (plan.Cycle != (automatic && known) || plan.SetSavedGroundSlot != (automatic && known && ground && !active) ||
        plan.SettleMilliseconds != (ground && !active ? 500 : 150) ||
        (active && plan.Action != PosePlayback.Entry.RefreshActivePose) ||
        (!active && ground && plan.Action != PosePlayback.Entry.EnterGroundSit) ||
        (!active && !ground && plan.Action != PosePlayback.Entry.PrepareIdle))
        throw new Exception("Pose preparation or manual/automatic separation regressed.");
}
Console.WriteLine("Manual pose entry and retained ground-sit tests passed.");
if (PairRules.Identity("Alice", 1, 2, 3) != PairRules.Identity(" ALICE ", 1, 2, 3) ||
    PairRules.Identity("Alice", 1, 2, 3) == PairRules.Identity("Alice", 2, 2, 3) ||
    PairRules.Identity("Alice", 1, 2, 3) == PairRules.Identity("Alice", 1, 2, 4) ||
    PairRules.StartTime(100, 200, 1000) != 1150 ||
    PairRules.Nearby(System.Numerics.Vector3.Zero, new(0, 1, 0)) ||
    PairRules.Nearby(System.Numerics.Vector3.Zero, new(float.NaN, 0, 0)) ||
    !PairRules.PeerMayHaveStarted("scheduled", 1000, 990) || PairRules.PeerMayHaveStarted("paired", 1000, 990))
    throw new Exception("Pair identity, clock compensation, range or start-boundary regression.");
try { PairRules.StartTime(100, 800, 2000); throw new Exception("High-latency sample was accepted."); }
catch (InvalidOperationException) { }
await PairClientTests.Run();
var plt = new byte[512];
void Half(int at, ushort value) => BitConverter.GetBytes(value).CopyTo(plt, at);
void Word(int at, uint value) => BitConverter.GetBytes(value).CopyTo(plt, at);
Half(0, 1); Half(2, 1); Word(8, 228); Word(12, 238);
Word(16, 0); Word(20, 0); Word(24, 0);
Word(28, 1401); Half(32, 1); Half(34, 0); plt[36] = 1;
Word(196, 801); Half(200, 1); Half(202, 0);
System.Text.Encoding.ASCII.GetBytes("bt_common\0").CopyTo(plt, 228);
System.Text.Encoding.ASCII.GetBytes("emote/test_loop\0").CopyTo(plt, 238);
var compatibility = new PapCompatibility(plt);
if (compatibility.Matches("chara/human/c0801/animation/a0001/bt_common/emote/test_loop.pap", 1401) != true ||
    compatibility.Matches("chara/human/c1401/animation/a0001/bt_common/emote/test_loop.pap", 1401) != false ||
    compatibility.Matches("chara/human/c0801/animation/a0001/bt_common/emote/unknown.pap", 1401) is not null ||
    new PapCompatibility([]).Matches("anything", 1401) is not null)
    throw new Exception("PAP fallback filtering regression.");
Console.WriteLine("PAP redirect and unknown-path filtering tests passed.");
if (args.Contains("--pair-relay-test")) await PairClientTests.Live();
else if (args.Length == 1)
{
    var realMod = Path.GetFullPath(args[0]);
    var realName = Path.GetFileName(realMod);
    var real = ModScanner.Scan(Path.GetDirectoryName(realMod)!, new Dictionary<string, string> { [realName] = realName }, new EmoteCatalog(), _ => []);
    var guitar = real.Single(m => m.Command == "/hum");
    if (guitar.BasePaths.Length == 0 || !guitar.Variants.Where(v => v.Group == "Song").Select(v => v.Option).Order().SequenceEqual(new[] { "None", "To Zanarkand", "Town" }.Order()))
        throw new Exception("Installed Guitar Solo songs were not indexed correctly.");
    Console.WriteLine("Read-only installed Guitar Solo check passed: To Zanarkand, Town, None; base animation retained.");
}
