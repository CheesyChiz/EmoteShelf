using NoireLib.Animations.PapFormat.Parsing;
using NoireLib.Animations.PapFormat.Tmb;
using System.Collections.Generic;

namespace NoireLib.Animations.PapFormat.Tmb.Entries;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class C009 : TmbEntry
{
    public const string MAGIC = "C009";
    public const string DISPLAY_NAME = "Animation (PAP Only)";

    public override string Magic => MAGIC;
    public override string DisplayName => DISPLAY_NAME;
    public override int Size => 0x18;
    public override int ExtraSize => 0;

    private readonly ParsedInt Duration = new("Duration", 4, 50);
    private readonly ParsedInt Unk1 = new("Unknown 1", 4, 0);
    public readonly TmbOffsetString Path = new("Path");

    public C009(TmbFile file) : base(file) { }

    public C009(TmbFile file, TmbReader reader) : base(file, reader) { }

    /// <summary>The clip's duration in frames.</summary>
    /// <returns>The duration in frames.</returns>
    public int GetDuration() => Duration.Value;

    /// <summary>
    /// Overrides the clip's duration in frames, which the preserved-bytes fast path cannot see, so the caller must
    /// also call <see cref="TmbFile.InvalidateSourceLayout"/> for it to land.
    /// </summary>
    /// <param name="frames">The new duration in frames.</param>
    public void SetDuration(int frames) => Duration.Value = frames;

    protected override List<ParsedBase> GetParsed() => [Duration, Unk1, Path];
}

public class TmbOffsetString : ParsedBase
{
    public string Value { get; set; }

    public TmbOffsetString(string name, string value = "") : base(name)
    {
        Value = value;
    }

    public override void Read(TmbReader reader)
    {
        Value = reader.ReadOffsetString();
    }

    public override void Read(System.IO.BinaryReader reader)
    {
        throw new System.NotImplementedException("TmbOffsetString requires TmbReader");
    }

    public override void Write(TmbWriter writer)
    {
        writer.WriteOffsetString(Value);
    }

    public override void Write(System.IO.BinaryWriter writer)
    {
        throw new System.NotImplementedException("TmbOffsetString requires TmbWriter");
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
