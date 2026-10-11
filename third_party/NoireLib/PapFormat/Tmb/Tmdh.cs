using NoireLib.Animations.PapFormat.Parsing;

namespace NoireLib.Animations.PapFormat.Tmb;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class Tmdh : TmbItemWithId
{
    public override string Magic => "TMDH";
    public override int Size => 0x10;
    public override int ExtraSize => 0;

    private readonly ParsedShort Unk1 = new("Unknown 1");
    private readonly ParsedShort Length = new("Length");
    private readonly ParsedShort Unk3 = new("Unknown 3");

    public Tmdh(TmbFile file, TmbReader reader) : base(file, reader)
    {
        Unk1.Read(reader);
        Length.Read(reader);
        Unk3.Read(reader);
    }

    /// <summary>The timeline's length in frames.</summary>
    /// <returns>The length.</returns>
    public short GetLength() => Length.Value;

    /// <summary>
    /// Overrides the timeline's length in frames. A numeric edit is invisible to the preserved-bytes fast path, so
    /// the caller must also call <see cref="TmbFile.InvalidateSourceLayout"/> for it to land.
    /// </summary>
    /// <param name="frames">The new length in frames.</param>
    public void SetLength(short frames) => Length.Value = frames;

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        Unk1.Write(writer);
        Length.Write(writer);
        Unk3.Write(writer);
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
