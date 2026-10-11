using NoireLib.Animations.PapFormat.Parsing;
using NoireLib.Animations.PapFormat.Tmb;
using System.Collections.Generic;

namespace NoireLib.Animations.PapFormat.Tmb.Entries;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/// <summary> A timed TMB entry whose payload is described by a list of parsed fields. </summary>
public abstract class TmbEntry : TmbItemWithTime
{
    /// <summary> Gets the human readable name of the entry type, such as "Animation". </summary>
    public abstract string DisplayName { get; }

    /// <summary> Creates an entry with default field values. </summary>
    /// <param name="file">The file the entry belongs to.</param>
    protected TmbEntry(TmbFile file) : base(file) { }

    /// <summary> Creates an entry and reads its fields from the stream. </summary>
    /// <param name="file">The file the entry belongs to.</param>
    /// <param name="reader">The reader positioned at the entry's payload.</param>
    protected TmbEntry(TmbFile file, TmbReader reader) : base(file, reader)
    {
        var parsed = GetParsed();
        foreach (var item in parsed)
        {
            item.Read(reader);
        }
    }

    /// <summary> Lists the entry's fields in wire order. </summary>
    /// <returns>The parsed fields, read and written in that order.</returns>
    protected abstract List<ParsedBase> GetParsed();

    /// <summary> Writes the entry's fields without the item header. </summary>
    /// <param name="writer">The writer to emit into.</param>
    public virtual void WriteData(TmbWriter writer)
    {
        foreach (var parsed in GetParsed())
        {
            parsed.Write(writer);
        }
    }

    /// <summary> Writes the item header followed by the entry's fields. </summary>
    /// <param name="writer">The writer to emit into.</param>
    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        foreach (var parsed in GetParsed())
        {
            parsed.Write(writer);
        }
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
