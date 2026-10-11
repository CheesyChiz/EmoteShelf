using NoireLib.Animations.PapFormat.Parsing;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NoireLib.Animations.PapFormat;

/// <summary> The skeleton family a .pap's animations were authored against. </summary>
public enum SkeletonType
{
    /// <summary> A human/humanoid skeleton. </summary>
    Human = 0,
    /// <summary> A monster skeleton. </summary>
    Monster = 1,
    /// <summary> A demihuman skeleton. </summary>
    DemiHuman = 2,
    /// <summary> A weapon skeleton. </summary>
    Weapon = 3,
}

/// <summary>
/// A parsed .pap: the container format that holds one or more named animations, each pairing a havok
/// animation binding with a TMB timeline. Reads and writes the container's own header/offsets/padding;
/// each <see cref="PapAnimation"/> owns its own name and TMB.
/// </summary>
public class PapFile
{
    /// <summary> The FFXIV model id this file was authored against. </summary>
    public readonly ParsedShort ModelId = new("Model Id", 0);
    /// <summary> The <see cref="SkeletonType"/> this file was authored against, stored as its raw int value. </summary>
    public readonly ParsedInt ModelType = new("Skeleton Type", 1, 0);
    /// <summary> The model variant this file was authored against. </summary>
    public readonly ParsedInt Variant = new("Variant", 1, 0);

    /// <summary> The animations declared by this file, in file order. </summary>
    public readonly List<PapAnimation> Animations = [];

    private byte[] HavokData = [];
    private byte[] PostAnimationPadding = [];
    private readonly string? HkxTempLocation;
    private int ModdedTmbOffset4 = 0;
    private int ModdedPapMod4 = 0;

    /// <summary> Starts an empty .pap that will dump its havok blob to <paramref name="hkxTempPath"/>. </summary>
    /// <param name="hkxTempPath">Where to dump the havok blob.</param>
    public PapFile(string hkxTempPath)
    {
        HkxTempLocation = hkxTempPath;
    }

    /// <summary> Parses a .pap from <paramref name="reader"/>, starting at its current position. </summary>
    /// <param name="reader">The source to parse from; left positioned just past the file on return.</param>
    /// <param name="hkxTempPath">Where to dump the embedded havok blob, or null to skip the dump.</param>
    /// <exception cref="InvalidDataException">The magic is wrong or the animation info overruns the havok block.</exception>
    public PapFile(BinaryReader reader, string? hkxTempPath = null)
    {
        HkxTempLocation = hkxTempPath;

        var startPos = reader.BaseStream.Position;

        var magic = reader.ReadInt32();
        if (magic != 0x20706170) // "pap "
            throw new InvalidDataException($"Invalid PAP magic: 0x{magic:X8}");

        var version = reader.ReadInt32();
        var numAnimations = reader.ReadInt16();

        ModelId.Read(reader);
        ModelType.Read(reader);
        Variant.Read(reader);

        var infoOffset = reader.ReadInt32();
        var havokPosition = reader.ReadInt32();
        var footerPosition = reader.ReadInt32();

        reader.BaseStream.Position = startPos + infoOffset;

        for (var i = 0; i < numAnimations; i++)
            Animations.Add(new PapAnimation(this, reader));

        var currentPos = reader.BaseStream.Position;
        var expectedHavokPos = startPos + havokPosition;
        var paddingSize = (int)(expectedHavokPos - currentPos);
        if (paddingSize > 0)
        {
            PostAnimationPadding = reader.ReadBytes(paddingSize);
            NoireLogger.LogDebug($"PapFile: Captured {paddingSize} bytes of post-animation padding");
        }
        else if (paddingSize < 0)
            throw new InvalidDataException($"Animation info overran havok position by {-paddingSize} bytes");

        var havokDataSize = footerPosition - havokPosition;
        HavokData = reader.ReadBytes(havokDataSize);
        if (HkxTempLocation != null)
            File.WriteAllBytes(HkxTempLocation, HavokData);

        ModdedPapMod4 = (int)((reader.BaseStream.Position - startPos) % 4);

        reader.BaseStream.Position = startPos + footerPosition;
        ModdedTmbOffset4 = (int)((reader.BaseStream.Position - startPos) % 4);

        for (var i = 0; i < numAnimations; i++)
        {
            Animations[i].ReadTmb(reader);
            reader.ReadBytes(CalculatePadding(reader.BaseStream.Position, i, numAnimations, ModdedTmbOffset4));
        }
    }

    private static int CalculatePadding(long position, int idx, int total, int customOffset)
    {
        if (total > 1 && idx < total - 1)
        {
            var leftOver = (position - customOffset) % 4;
            return (int)(leftOver == 0 ? 0 : 4 - leftOver);
        }
        return 0;
    }

    /// <summary>
    /// Serializes this file back to bytes, recomputing its header offsets and padding.
    /// </summary>
    /// <returns>The serialized .pap.</returns>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var tmbData = Animations.Select(x => x.GetTmbBytes()).ToList();

        var startPos = writer.BaseStream.Position;

        writer.Write(0x20706170); // magic "pap "
        writer.Write(0x00020001); // version
        writer.Write((short)Animations.Count);

        ModelId.Write(writer);
        ModelType.Write(writer);
        Variant.Write(writer);

        var offsetPos = writer.BaseStream.Position;
        writer.Write(0); // info offset placeholder
        writer.Write(0); // havok offset placeholder
        writer.Write(0); // footer offset placeholder

        var infoPos = writer.BaseStream.Position;

        foreach (var anim in Animations)
            anim.Write(writer);

        writer.Write(PostAnimationPadding);

        var havokPos = writer.BaseStream.Position;

        writer.Write(HavokData);

        PadTo(writer, writer.BaseStream.Position, 4, ModdedPapMod4);

        var timelinePos = writer.BaseStream.Position;
        for (var idx = 0; idx < tmbData.Count; idx++)
        {
            writer.Write(tmbData[idx]);
            Pad(writer, CalculatePadding(writer.BaseStream.Position, idx, tmbData.Count, ModdedTmbOffset4));
        }

        var endPos = writer.BaseStream.Position;
        writer.BaseStream.Position = offsetPos;

        var infoOffset = (int)(infoPos - startPos);
        var havokOffset = (int)(havokPos - startPos);
        var timelineOffset = (int)(timelinePos - startPos);

        writer.Write(infoOffset);
        writer.Write(havokOffset);
        writer.Write(timelineOffset);
        writer.BaseStream.Position = endPos;

        return ms.ToArray();
    }

    private static void Pad(BinaryWriter writer, int count)
    {
        for (var i = 0; i < count; i++)
        {
            writer.Write((byte)0);
        }
    }

    private static void PadTo(BinaryWriter writer, long position, int alignment, int mod)
    {
        var currentMod = (int)(position % alignment);
        if (currentMod == mod) return;

        var padding = (alignment - currentMod + mod) % alignment;
        Pad(writer, padding);
    }

    /// <summary> Reads and parses the .pap at <paramref name="path"/>, dumping its havok blob to <paramref name="hkxTempPath"/>. </summary>
    /// <param name="path">The .pap to read.</param>
    /// <param name="hkxTempPath">Where to dump the embedded havok blob.</param>
    /// <returns>The parsed file.</returns>
    public static PapFile FromFile(string path, string hkxTempPath)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        return new PapFile(reader, hkxTempPath);
    }
}
