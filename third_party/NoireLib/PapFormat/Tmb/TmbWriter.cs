using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NoireLib.Animations.PapFormat.Tmb;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class TmbWriter
{
    private readonly MemoryStream BodyMs;
    private readonly MemoryStream ExtraMs;
    private readonly MemoryStream TimelineMs;
    private readonly MemoryStream StringMs;

    public readonly BinaryWriter Writer;
    public readonly BinaryWriter ExtraWriter;
    public readonly BinaryWriter TimelineWriter;
    public readonly BinaryWriter StringWriter;

    public int BodySize;
    public int ExtraSize;
    public int TimelineSize;

    public long Position => Writer.BaseStream.Position;
    public long StartPosition;

    private readonly Dictionary<string, int> WrittenStrings = [];

    public TmbWriter(int bodySize, int extraSize, int timelineSize)
    {
        BodySize = bodySize;
        ExtraSize = extraSize;
        TimelineSize = timelineSize;

        BodyMs = new();
        ExtraMs = new();
        TimelineMs = new();
        StringMs = new();

        Writer = new(BodyMs);
        ExtraWriter = new(ExtraMs);
        TimelineWriter = new(TimelineMs);
        StringWriter = new(StringMs);
    }

    public void Write(byte value) => Writer.Write(value);
    public void Write(short value) => Writer.Write(value);
    public void Write(int value) => Writer.Write(value);
    public void Write(float value) => Writer.Write(value);

    public void WriteString(string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        Writer.Write(bytes);
    }

    public void WriteOffsetString(string str)
    {
        if (WrittenStrings.TryGetValue(str, out var existingOffset))
        {
            var actualPos = (int)((BodySize - (StartPosition + 8)) + ExtraSize + TimelineSize + existingOffset);
            Writer.Write(actualPos);
        }
        else
        {
            var newStringOffset = (int)StringWriter.BaseStream.Position;
            var actualPos = (int)((BodySize - (StartPosition + 8)) + ExtraSize + TimelineSize + newStringOffset);
            Writer.Write(actualPos);

            var bytes = Encoding.UTF8.GetBytes(str);
            StringWriter.Write(bytes);
            StringWriter.Write((byte)0);
            WrittenStrings[str] = newStringOffset;
        }
    }

    public void WriteOffsetTimeline<T>(List<T> entries) where T : TmbItemWithId
    {
        var actualPos = (int)((BodySize - (StartPosition + 8)) + ExtraSize + TimelineWriter.BaseStream.Position);
        Writer.Write(actualPos);
        Writer.Write(entries.Count);

        foreach (var entry in entries)
        {
            TimelineWriter.Write((short)entry.Id);
        }
    }

    public void WriteExtra(Action<BinaryWriter> func)
    {
        var actualPos = (int)((BodySize - (StartPosition + 8)) + ExtraWriter.BaseStream.Position);
        Writer.Write(actualPos);
        func(ExtraWriter);
    }

    public void WriteTo(BinaryWriter writer)
    {
        writer.Write(BodyMs.ToArray());
        writer.Write(ExtraMs.ToArray());
        writer.Write(TimelineMs.ToArray());
        writer.Write(StringMs.ToArray());
    }

    public void Dispose()
    {
        Writer.Close();
        ExtraWriter.Close();
        TimelineWriter.Close();
        StringWriter.Close();

        BodyMs.Close();
        ExtraMs.Close();
        TimelineMs.Close();
        StringMs.Close();
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
