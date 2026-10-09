using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace EmoteShelf;

// Short local approach followed by bounded fine correction. No remote coordinates.
internal sealed unsafe class PairAlignment : IDisposable
{
    private delegate void WalkInput(nint context, float* lateral, float* forward, float* turn, byte* mode, byte* extra, byte additive);
    private readonly Hook<WalkInput> hook;
    private readonly IObjectTable objects;
    private Vector3 destination;
    private float facing;
    private long expires;
    private long lastProgress;
    private float bestDistance;
    private bool correcting;
    private int correctionFrames, stableFrames;
    private readonly List<string> samples = [];
    private long started;
    public string Diagnostics => string.Join(" | ", samples);

    // Bounded, relative-only samples: no actor names, world coordinates or network tokens.
    private void Sample(string phase)
    {
        if (samples.Count >= 32) return;
        if (objects.LocalPlayer is not { } player) { samples.Add(phase + ": no actor"); return; }
        var delta = destination - player.Position;
        var mode = ((Character*)player.Address)->Mode;
        samples.Add(FormattableString.Invariant($"{Environment.TickCount64 - started}ms {phase} frame={correctionFrames} stable={stableFrames} delta=({delta.X:F5},{delta.Y:F5},{delta.Z:F5}) distance={delta.Length():F5} angleRad={PairRules.AngleDistance(player.Rotation, facing):F5} mode={mode}"));
    }
    public bool Active { get; private set; }
    public bool Arrived { get; private set; }
    public string Error { get; private set; } = "";

    public PairAlignment(IGameInteropProvider interop, IObjectTable objects)
    {
        this.objects = objects;
        hook = interop.HookFromSignature<WalkInput>("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", ReadInput);
        hook.Enable();
    }

    public void Begin(Vector3 point, float rotation)
    {
        if (objects.LocalPlayer is not { } player || !PairRules.Nearby(player.Position, point))
            throw new InvalidOperationException("Move within 2 yalms on the same level first.");
        destination = point;
        facing = rotation;
        expires = Environment.TickCount64 + 2500;
        lastProgress = Environment.TickCount64;
        bestDistance = Vector3.Distance(player.Position, point);
        Arrived = false;
        Error = "";
        Active = true;
        correcting = false;
        correctionFrames = stableFrames = 0;
        started = Environment.TickCount64;
        samples.Clear();
        Sample("begin");
    }

    public void Cancel() { if (Active) Sample("cancel"); Active = false; Arrived = false; correcting = false; }

    // Once per framework frame, not per input-hook invocation. Require observed
    // stability on subsequent frames; a position write itself is not confirmation.
    public void Update()
    {
        if (!Active || !correcting) return;
        if (objects.LocalPlayer is not { } player || Environment.TickCount64 >= expires)
        { Fail("Alignment confirmation timed out."); return; }
        var character = (Character*)player.Address;
        Sample("before");
        if (character->Mode != CharacterModes.Normal || Vector3.Distance(player.Position, destination) > .05f)
        { Fail("Fine alignment cancelled: character moved or changed state."); return; }
        stableFrames = PairRules.Aligned(player.Position, player.Rotation, destination, facing) ? stableFrames + 1 : 0;
        if (stableFrames >= 3) { Sample("arrived"); Active = false; Arrived = true; return; }
        if (++correctionFrames > 12) { Fail("Position/facing did not stabilize; pair launch cancelled."); return; }
        character->GameObject.SetPosition(destination.X, destination.Y, destination.Z);
        character->GameObject.SetRotation(facing);
        Sample("after");
    }

    private void Fail(string reason) { Cancel(); Error = reason; }

    private void ReadInput(nint context, float* lateral, float* forward, float* turn, byte* mode, byte* extra, byte additive)
    {
        hook.Original(context, lateral, forward, turn, mode, extra, additive);
        if (!Active) return;
        if (*lateral != 0 || *forward != 0 || *turn != 0)
        {
            if (samples.Count < 32) samples.Add(FormattableString.Invariant($"input lateral={*lateral:F5} forward={*forward:F5} turn={*turn:F5} additive={additive}"));
            Fail("Alignment cancelled by movement input."); return;
        }
        if (additive != 0) return;
        if (correcting) return;
        var now = Environment.TickCount64;
        if (now >= expires) { Fail("Alignment timed out; move closer and retry."); return; }
        if (objects.LocalPlayer is not { } player) { Fail("Character unavailable."); return; }
        var character = (Character*)player.Address;
        if (character->Mode != CharacterModes.Normal) { Fail("Stand normally before aligning."); return; }
        var delta = destination - player.Position;
        if (MathF.Abs(delta.Y) > .25f) { Fail("Alignment stopped: different ground height."); return; }
        var distance = new Vector2(delta.X, delta.Z).Length();
        if (distance <= .03f)
        {
            if (Vector3.Distance(player.Position, destination) > .05f)
            { Fail("Too much height difference for fine alignment."); return; }
            correcting = true;
            Sample("fine-start");
            return;
        }
        if (distance < bestDistance - .01f) { bestDistance = distance; lastProgress = now; }
        if (now - lastProgress > 700) { Fail("Alignment blocked; no position correction was applied."); return; }
        var manager = CameraManager.Instance();
        var camera = manager == null ? null : manager->GetActiveCamera();
        if (camera == null) { Fail("Camera unavailable."); return; }
        var yaw = ((FFXIVClientStructs.FFXIV.Client.Game.Camera*)camera)->DirH + MathF.PI;
        var relative = MathF.Atan2(delta.X, delta.Z) - yaw;
        var speed = Math.Clamp(distance / .4f, .08f, 1f);
        *lateral = MathF.Sin(relative) * speed;
        *forward = MathF.Cos(relative) * speed;
    }

    public void Dispose() { Cancel(); hook.Dispose(); }
}
