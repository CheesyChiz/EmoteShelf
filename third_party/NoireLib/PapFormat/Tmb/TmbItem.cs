using NoireLib.Animations.PapFormat.Parsing;

namespace NoireLib.Animations.PapFormat.Tmb;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public abstract class TmbItem
{
    public abstract string Magic { get; }
    public abstract int Size { get; }
    public abstract int ExtraSize { get; }

    protected TmbFile File { get; }

    protected TmbItem(TmbFile file)
    {
        File = file;
    }

    protected TmbItem(TmbFile file, TmbReader reader)
    {
        File = file;
        reader.UpdateStartPosition();
        reader.ReadString(4); // magic
        reader.ReadInt32(); // size
    }

    public virtual void Write(TmbWriter writer)
    {
        writer.WriteString(Magic);
        writer.Write(Size);
    }
}

public abstract class TmbItemWithId : TmbItem
{
    public short Id { get; set; }

    protected TmbItemWithId(TmbFile file) : base(file) { }
    protected TmbItemWithId(TmbFile file, TmbReader reader) : base(file, reader)
    {
        Id = reader.ReadInt16();
    }

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        writer.Write(Id);
    }
}

public abstract class TmbItemWithTime : TmbItemWithId
{
    private readonly ParsedShort Time = new("Time", 0);

    protected TmbItemWithTime(TmbFile file) : base(file) { }

    protected TmbItemWithTime(TmbFile file, TmbReader reader) : base(file, reader)
    {
        Time.Read(reader.Reader);
    }

    /// <summary> Gets the item's start time in frames. </summary>
    /// <returns>The start time in frames.</returns>
    public short GetTime() => Time.Value;

    /// <summary>
    /// Sets the item's start time in frames, re-emitted in place by the preserved-bytes writer. A channel
    /// lasts until its latest entry, so clamping a channel's length also requires pulling late entries back.
    /// </summary>
    /// <param name="frames">The start time in frames.</param>
    public void SetTime(short frames) => Time.Value = frames;

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        Time.Write(writer.Writer);
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
