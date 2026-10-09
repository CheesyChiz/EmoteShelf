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
Console.WriteLine("Pose slot regression tests passed.");
