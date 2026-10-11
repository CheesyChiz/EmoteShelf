using NoireLib.Animations.PapFormat.Tmb;
using System.Collections.Generic;
using System.IO;

namespace NoireLib.Animations.PapFormat.Parsing;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public abstract class ParsedBase
{
    public string Name { get; }

    protected ParsedBase(string name)
    {
        Name = name;
    }

    public virtual void Read(TmbReader reader) => Read(reader.Reader);
    public abstract void Read(BinaryReader reader);

    public virtual void Write(TmbWriter writer) => Write(writer.Writer);
    public abstract void Write(BinaryWriter writer);
}

public class ParsedFloat : ParsedBase
{
    public float Value { get; set; }

    public ParsedFloat(string name, float value = 0f) : base(name)
    {
        Value = value;
    }

    public override void Read(BinaryReader reader)
    {
        Value = reader.ReadSingle();
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(Value);
    }
}

public class ParsedInt : ParsedBase
{
    public int Value { get; set; }
    private readonly int Size;

    public ParsedInt(string name, int size = 4, int value = 0) : base(name)
    {
        Size = size;
        Value = value;
    }

    public override void Read(BinaryReader reader)
    {
        Value = Size switch
        {
            1 => reader.ReadByte(),
            2 => reader.ReadInt16(),
            4 => reader.ReadInt32(),
            _ => 0
        };
    }

    public override void Write(BinaryWriter writer)
    {
        switch (Size)
        {
            case 1:
                writer.Write((byte)Value);
                break;
            case 2:
                writer.Write((short)Value);
                break;
            case 4:
                writer.Write(Value);
                break;
        }
    }
}

public class ParsedShort : ParsedBase
{
    public short Value { get; set; }

    public ParsedShort(string name, short value = 0) : base(name)
    {
        Value = value;
    }

    public override void Read(BinaryReader reader)
    {
        Value = reader.ReadInt16();
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(Value);
    }
}

public class ParsedBool : ParsedBase
{
    public bool Value { get; set; }
    private int Size;

    public ParsedBool(string name, bool value = false, int size = 4) : base(name)
    {
        Value = value;
        Size = size;
    }

    public override void Read(BinaryReader reader)
    {
        Value = (Size switch
        {
            4 => reader.ReadInt32(),
            2 => reader.ReadInt16(),
            1 => reader.ReadByte(),
            _ => reader.ReadByte(),
        }) == 1;
    }

    public override void Write(BinaryWriter writer)
    {
        var intValue = Value ? 1 : 0;
        if (Size == 4)
            writer.Write(intValue);
        else if (Size == 2)
            writer.Write((short)intValue);
        else
            writer.Write((byte)intValue);
    }
}

public class ParsedString : ParsedBase
{
    public string Value { get; set; }

    public ParsedString(string name, string value = "") : base(name)
    {
        Value = value;
    }

    public override void Read(BinaryReader reader)
    {
        var chars = new List<byte>();
        byte c;
        while ((c = reader.ReadByte()) != 0)
        {
            chars.Add(c);
        }
        Value = System.Text.Encoding.UTF8.GetString(chars.ToArray());
    }

    public override void Write(BinaryWriter writer)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(Value);
        writer.Write(bytes);
        writer.Write((byte)0);
    }
}

public class ParsedPaddedString : ParsedString
{
    private readonly int Length;
    private readonly byte Padding;

    // Every count below is in bytes, never in characters. ParsedString writes UTF-8 plus a
    // terminating null, so a value whose characters fit can still overrun the slot in bytes.
    public int MaxByteLength => Length - 1;

    public ParsedPaddedString(string name, string value, int length, byte padding) : base(name, value)
    {
        Length = length;
        Padding = padding;
    }

    public ParsedPaddedString(string name, int length, byte padding) : base(name)
    {
        Length = length;
        Padding = padding;
    }

    public override void Read(BinaryReader reader)
    {
        // Bounded by the slot rather than by the terminator: a malformed field that fills its slot with
        // no room for a null would otherwise run the scan on into whatever follows, or off the stream.
        var slot = reader.ReadBytes(Length);

        var end = System.Array.IndexOf(slot, (byte)0);
        if (end < 0)
            end = slot.Length;

        Value = System.Text.Encoding.UTF8.GetString(slot, 0, end);
    }

    public override void Write(BinaryWriter writer)
    {
        var used = System.Text.Encoding.UTF8.GetByteCount(Value);

        // Refuse rather than overrun. One byte past the slot shifts every offset after it and
        // silently produces a file the game cannot read.
        if (used > MaxByteLength)
            throw new InvalidDataException(
                $"Field '{Name}' holds {used} bytes but its slot fits {MaxByteLength} plus a terminator.");

        base.Write(writer);

        for (var i = 0; i < (Length - used - 1); i++)
        {
            writer.Write(used == 0 ? (byte)0 : Padding);
        }
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
