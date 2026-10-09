using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace EmoteShelf;

// Local opt-in approach only. Never writes position or accepts remote coordinates.
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
    }

    public void Cancel() { Active = false; Arrived = false; }

    private void Fail(string reason) { Cancel(); Error = reason; }

    private void ReadInput(nint context, float* lateral, float* forward, float* turn, byte* mode, byte* extra, byte additive)
    {
        hook.Original(context, lateral, forward, turn, mode, extra, additive);
        if (!Active) return;
        if (*lateral != 0 || *forward != 0 || *turn != 0) { Fail("Alignment cancelled by movement input."); return; }
        if (additive != 0) return;
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
            character->GameObject.SetRotation(facing);
            Active = false;
            Arrived = true;
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
