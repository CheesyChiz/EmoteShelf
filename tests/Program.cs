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
    if (scanned[0].Variants.Length != 1 ||
        !scanned[0].Variants[0].Files.TryGetValue(
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
}
finally
{
    if (Path.GetFullPath(testRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
        Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
}
Console.WriteLine("Pose slot regression tests passed.");
