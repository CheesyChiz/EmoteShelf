using NoireLib.Animations.PapFormat.Parsing;
using NoireLib.Animations.PapFormat.Tmb;
using System;
using System.Collections.Generic;
using System.IO;

namespace NoireLib.Animations.PapFormat.Tmb.Entries;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/// <summary>
/// A TMB entry of an unrecognised type, kept as raw bytes so a read/write round trip preserves it.
/// </summary>
public class TmbEntryRaw : TmbEntry
{
    // magic (4) + size (4) + id (2) + time (2), read by the base constructors.
    private const int HeaderLength = 12;

    // A field the payload cannot carry verbatim, and the bytes leading up to it.
    private sealed record Segment(byte[] Before, string? Path, float[]? Floats, bool HasPath);

    private readonly string _magic;
    private readonly List<Segment> _segments = [];
    private readonly byte[] _trailing;

    public override string Magic => _magic;
    public override string DisplayName => $"Unknown Entry ({_magic})";
    public override int Size { get; }

    public override int ExtraSize
    {
        get
        {
            var total = 0;

            foreach (var segment in _segments)
                total += (segment.Floats?.Length ?? 0) * sizeof(float);

            return total;
        }
    }

    public TmbEntryRaw(TmbFile file, TmbReader reader, string magic, int size) : base(file, reader)
    {
        _magic = magic;
        Size = size;

        var consumed = 0;

        foreach (var (fieldIndex, isPath) in FieldsOf(magic, size))
        {
            var before = reader.Reader.ReadBytes(fieldIndex - consumed);

            if (isPath)
            {
                var path = reader.ReadOptionalOffsetString();
                _segments.Add(new Segment(before, path, null, true));
                consumed = fieldIndex + sizeof(int);
                continue;
            }

            _segments.Add(new Segment(before, null, ReadFloatBlock(reader), false));
            consumed = fieldIndex + sizeof(int) * 2;
        }

        _trailing = reader.Reader.ReadBytes(size - HeaderLength - consumed);
    }

    /// <summary> Builds an entry of a magic this reader has no model for, from its payload words. </summary>
    /// <param name="file">The timeline the entry belongs to.</param>
    /// <param name="magic">The entry magic, such as "C014".</param>
    /// <param name="time">The frame the entry fires on.</param>
    /// <param name="payload">The entry's payload words, in wire order.</param>
    public TmbEntryRaw(TmbFile file, string magic, short time, params int[] payload) : base(file)
    {
        _magic = magic;
        Size = HeaderLength + payload.Length * sizeof(int);

        var data = new byte[payload.Length * sizeof(int)];
        for (var index = 0; index < payload.Length; index++)
            BitConverter.GetBytes(payload[index]).CopyTo(data, index * sizeof(int));

        _trailing = data;
        SetTime(time);
    }

    // A block of floats the payload names by offset and count, read from wherever it points.
    private static float[] ReadFloatBlock(TmbReader reader)
    {
        var offset = reader.ReadInt32();
        var count = reader.ReadInt32();

        if (offset == 0 || count <= 0)
            return [];

        var savePosition = reader.Reader.BaseStream.Position;
        reader.Reader.BaseStream.Position = reader.StartPosition + 8 + offset;

        var floats = new float[count];
        for (var index = 0; index < count; index++)
            floats[index] = reader.Reader.ReadSingle();

        reader.Reader.BaseStream.Position = savePosition;

        return floats;
    }

    // Where this magic's pointing fields sit inside the payload, in wire order.
    private static List<(int Index, bool IsPath)> FieldsOf(string magic, int size)
    {
        var fields = new List<(int Index, bool IsPath)>();

        if (TmbFile.StringFieldOffsets.TryGetValue(magic, out var stringOffset)
            && stringOffset >= HeaderLength && stringOffset + sizeof(int) <= size)
        {
            fields.Add((stringOffset - HeaderLength, true));
        }

        if (TmbFile.ExtraFloatFieldOffsets.TryGetValue(magic, out var extraOffsets))
        {
            foreach (var extraOffset in extraOffsets)
            {
                if (extraOffset >= HeaderLength && extraOffset + sizeof(int) * 2 <= size)
                    fields.Add((extraOffset - HeaderLength, false));
            }
        }

        fields.Sort((left, right) => left.Index.CompareTo(right.Index));

        return fields;
    }

    /// <summary> Whether this entry carries anything the writer has to place, rather than plain bytes. </summary>
    public bool HasPointingFields => _segments.Count > 0;

    protected override List<ParsedBase> GetParsed() => [];

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);

        foreach (var segment in _segments)
        {
            writer.Writer.Write(segment.Before);

            if (segment.HasPath)
            {
                if (segment.Path == null)
                    writer.Write(0);
                else
                    writer.WriteOffsetString(segment.Path);

                continue;
            }

            var floats = segment.Floats ?? [];

            if (floats.Length == 0)
            {
                writer.Write(0);
                writer.Write(0);
                continue;
            }

            writer.WriteExtra(extra =>
            {
                foreach (var value in floats)
                    extra.Write(value);
            });

            writer.Write(floats.Length);
        }

        writer.Writer.Write(_trailing);
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
