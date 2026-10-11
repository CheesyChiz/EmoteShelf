using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NoireLib.Animations.PapFormat.Tmb;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class TmbReader
{
    public BinaryReader Reader { get; }
    public long StartPosition { get; private set; }

    /// <summary> Where every offset-timeline read so far pointed, which is where the id blocks begin. </summary>
    public readonly List<long> TimelinePositions = [];
    private readonly Dictionary<int, TmbItemWithId> ItemsWithId = [];

    public TmbReader(BinaryReader reader)
    {
        Reader = reader;
        StartPosition = reader.BaseStream.Position;
    }

    public void UpdateStartPosition()
    {
        StartPosition = Reader.BaseStream.Position;
    }

    public byte ReadByte() => Reader.ReadByte();
    public short ReadInt16() => Reader.ReadInt16();
    public int ReadInt32() => Reader.ReadInt32();
    public float ReadSingle() => Reader.ReadSingle();

    public string ReadString(int length)
    {
        var bytes = Reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    public string ReadOffsetString() => ReadOptionalOffsetString() ?? string.Empty;

    // Null when the field holds offset 0, which means the item carries no string at all rather than an empty one.
    public string? ReadOptionalOffsetString()
    {
        var offset = Reader.ReadInt32();
        if (offset == 0) return null;

        var savePos = Reader.BaseStream.Position;
        Reader.BaseStream.Position = StartPosition + 8 + offset;

        var chars = new List<byte>();
        byte c;
        while ((c = Reader.ReadByte()) != 0)
        {
            chars.Add(c);
        }
        var result = Encoding.UTF8.GetString(chars.ToArray());

        Reader.BaseStream.Position = savePos;
        return result;
    }

    public List<int> ReadOffsetTimeline()
    {
        var offset = Reader.ReadInt32();
        var count = Reader.ReadInt32();

        if (offset == 0) return [];

        var savePos = Reader.BaseStream.Position;
        Reader.BaseStream.Position = StartPosition + 8 + offset;
        TimelinePositions.Add(Reader.BaseStream.Position);

        var result = new List<int>();
        for (var i = 0; i < count; i++)
        {
            result.Add(Reader.ReadInt16());
        }

        Reader.BaseStream.Position = savePos;
        return result;
    }

    public bool ReadAtOffset(Action<BinaryReader> func)
    {
        var offset = Reader.ReadInt32();
        if (offset == 0) return false;

        var savePos = Reader.BaseStream.Position;
        Reader.BaseStream.Position = StartPosition + 8 + offset;
        func(Reader);
        Reader.BaseStream.Position = savePos;
        return true;
    }

    public void RegisterItemWithId(TmbItemWithId item)
    {
        ItemsWithId[item.Id] = item;
    }

    public List<T> Pick<T>(List<int> ids) where T : TmbItemWithId
    {
        var result = new List<T>();
        foreach (var id in ids)
        {
            if (ItemsWithId.TryGetValue(id, out var item) && item is T typedItem)
            {
                result.Add(typedItem);
            }
        }
        return result;
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
