using NoireLib.Animations.PapFormat.Parsing;
using NoireLib.Animations.PapFormat.Tmb;
using System.Collections.Generic;

namespace NoireLib.Animations.PapFormat.Tmb.Entries;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class C010 : TmbEntry
{
    public const string MAGIC = "C010";
    public const string DISPLAY_NAME = "Animation";

    public override string Magic => MAGIC;
    public override string DisplayName => DISPLAY_NAME;
    public override int Size => 0x28;
    public override int ExtraSize => 0;

    private readonly ParsedInt Duration = new("Duration", 4, 50);
    private readonly ParsedInt Unk1 = new("Unknown 1", 4, 0);
    private readonly ParsedInt Flags = new("Flags", 4, 0);
    private readonly ParsedFloat AnimationStart = new("Animation Start Frame", 0f);
    private readonly ParsedFloat AnimationEnd = new("Animation End Frame", 0f);
    public readonly TmbOffsetString Path = new("Path");
    private readonly ParsedInt Unk2 = new("Unknown 2", 4, 0);

    public C010(TmbFile file) : base(file) { }

    public C010(TmbFile file, TmbReader reader) : base(file, reader) { }

    /// <summary> Gets the clip's duration in frames. </summary>
    /// <returns>The duration in frames.</returns>
    public int GetDuration() => Duration.Value;

    /// <summary> Sets the clip's duration in frames, re-emitted in place by the preserved-bytes writer. </summary>
    /// <param name="frames">The duration in frames.</param>
    public void SetDuration(int frames) => Duration.Value = frames;

    /// <summary> Gets the playback segment start, in frames of the referenced animation. </summary>
    /// <returns>The start frame.</returns>
    public float GetAnimationStart() => AnimationStart.Value;

    /// <summary> Sets the playback segment start, re-emitted in place by the preserved-bytes writer. </summary>
    /// <param name="frame">The start frame.</param>
    public void SetAnimationStart(float frame) => AnimationStart.Value = frame;

    /// <summary> Gets the playback segment end, in frames of the referenced animation. </summary>
    /// <returns>The end frame.</returns>
    public float GetAnimationEnd() => AnimationEnd.Value;

    /// <summary> Sets the playback segment end, re-emitted in place by the preserved-bytes writer. </summary>
    /// <param name="frame">The end frame.</param>
    public void SetAnimationEnd(float frame) => AnimationEnd.Value = frame;

    protected override List<ParsedBase> GetParsed() => [
        Duration,
        Unk1,
        Flags,
        AnimationStart,
        AnimationEnd,
        Path,
        Unk2
    ];
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
