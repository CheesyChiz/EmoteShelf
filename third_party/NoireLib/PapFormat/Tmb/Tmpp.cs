using NoireLib.Animations.PapFormat.Tmb.Entries;

namespace NoireLib.Animations.PapFormat.Tmb;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class Tmpp : TmbItem
{
    public override string Magic => "TMPP";
    public override int Size => 0x0C;
    public override int ExtraSize => 0;

    public bool IsAssigned { get; private set; } = false;
    private readonly TmbOffsetString Path = new("Face Library Path");

    public Tmpp(TmbFile file, TmbReader reader) : base(file, reader)
    {
        reader.Reader.BaseStream.Position = reader.Reader.BaseStream.Position - 8; // rewind over the magic and size

        var savePos = reader.Reader.BaseStream.Position;
        var magic = reader.ReadString(4); // TMAL or TMPP

        if (magic == "TMPP")
        {
            IsAssigned = true;
            reader.ReadInt32(); // size
            Path.Read(reader);
        }
        else
        {
            // The chunk is optional, so a TMAL here means there is none and the stream must not move.
            reader.Reader.BaseStream.Position = savePos;
        }
    }

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        Path.Write(writer);
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
