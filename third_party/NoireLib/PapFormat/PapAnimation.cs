using NoireLib.Animations.PapFormat.Parsing;
using NoireLib.Animations.PapFormat.Tmb;
using System;
using System.IO;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// One named animation entry inside a <see cref="PapFile"/>: a havok binding plus the TMB timeline that
/// drives it. The game only plays a .pap's animation when <see cref="Name"/> matches what the emote asks for.
/// </summary>
public class PapAnimation
{
    /// <summary>The .pap this animation belongs to.</summary>
    public readonly PapFile File;
    /// <summary>The index of this animation's havok binding within the file's havok data.</summary>
    public short HavokIndex { get; set; }

    /// <summary>
    /// The longest animation name a .pap can carry: the header's Name field is 32 zero-padded bytes and the value
    /// is null-terminated.
    /// </summary>
    public const int MaxNameLength = 31;

    /// <summary>The name the game matches against an emote's expected animation name.</summary>
    public readonly ParsedPaddedString Name = new("Name", "cbbm_replace_this", MaxNameLength + 1, 0x00);
    private readonly ParsedShort Type = new("Type", 0);
    private readonly ParsedInt Face = new("Face Animation", 4, 0);

    /// <summary>This animation's timeline, populated by <see cref="ReadTmb"/> once its bytes are reached.</summary>
    public TmbFile Tmb { get; private set; } = null!;

    /// <summary>Starts a new animation entry belonging to a file.</summary>
    /// <param name="file">The .pap the animation belongs to.</param>
    public PapAnimation(PapFile file)
    {
        File = file;
    }

    /// <summary>Parses an animation entry's name, type, havok index and face flag.</summary>
    /// <param name="file">The .pap the animation belongs to.</param>
    /// <param name="reader">The reader positioned at the entry.</param>
    public PapAnimation(PapFile file, BinaryReader reader)
    {
        File = file;

        Name.Read(reader);
        Type.Read(reader);
        HavokIndex = reader.ReadInt16();
        Face.Read(reader);
    }

    /// <summary>Writes this animation's fixed-size entry, but not its TMB, which is written separately.</summary>
    /// <param name="writer">The writer positioned at the entry.</param>
    public void Write(BinaryWriter writer)
    {
        Name.Write(writer);
        Type.Write(writer);
        writer.Write(HavokIndex);
        Face.Write(writer);
    }

    /// <summary>Parses this animation's <see cref="Tmb"/> from the reader's current position.</summary>
    /// <param name="reader">The reader positioned at the timeline.</param>
    public void ReadTmb(BinaryReader reader)
    {
        Tmb = new TmbFile(reader);
    }

    /// <summary>This animation's serialized TMB bytes.</summary>
    /// <returns>The bytes, or an empty array when the animation has no timeline.</returns>
    public byte[] GetTmbBytes()
    {
        return Tmb?.ToBytes() ?? [];
    }

    /// <summary>
    /// A deep copy belonging to the same <see cref="File"/>, sharing the <see cref="HavokIndex"/> and so playing the
    /// same motion, but with an independent <see cref="Tmb"/> so renaming one copy's timeline never touches the
    /// other's.
    /// </summary>
    /// <returns>The copy.</returns>
    public PapAnimation Clone()
    {
        var copy = new PapAnimation(File)
        {
            HavokIndex = HavokIndex,
        };

        copy.Type.Value = Type.Value;
        copy.Face.Value = Face.Value;
        copy.Name.Value = Name.Value;

        if (Tmb != null)
            copy.Tmb = new TmbFile(new BinaryReader(new MemoryStream(Tmb.ToBytes())));

        return copy;
    }

    /// <summary>The animation name as currently set.</summary>
    /// <returns>The name.</returns>
    public string GetName() => Name.Value;

    /// <summary>
    /// Renames this animation, refusing a name longer than <see cref="MaxNameLength"/> bytes, which would overrun
    /// the header field and shift every offset after it.
    /// </summary>
    /// <param name="newName">The new animation name.</param>
    /// <exception cref="ArgumentException">The name exceeds <see cref="MaxNameLength"/> bytes.</exception>
    public void SetName(string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);

        var used = System.Text.Encoding.UTF8.GetByteCount(newName);
        if (used > MaxNameLength)
            throw new ArgumentException(
                $"Animation name '{newName}' is {used} bytes; a .pap holds at most {MaxNameLength}.", nameof(newName));

        Name.Value = newName;
    }
}
