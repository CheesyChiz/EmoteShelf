using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// Reads and injects TMPP face-library declarations across the TMB timelines embedded in a .pap, on raw bytes only.
/// A TMPP is the 12-byte TMB item naming the face-animation .pap loaded alongside an emote. <see cref="Inject"/>
/// splices bytes rather than rebuilding the timeline, so relative offsets in unrecognized entries stay valid.
/// </summary>
public static class PapFaceLibrary
{
    private const int PapMagic = 0x20706170;

    // Magic, version, count, model id, model type, variant, and three offsets.
    private const int PapHeaderLength = 26;

    private const int AnimationCountOffset = 8;

    // Offset of the footer field (where the embedded TMB region starts) inside a .pap header.
    private const int FooterFieldOffset = 22;

    private const int TmlbHeaderLength = 12;

    // TMDH is always the first item and always this size.
    private const int TmdhLength = 0x10;

    // TMPP is magic, size and one offset string; it carries no id or time.
    private const int TmppLength = 0x0C;

    // Offset of the TMPP inside a TMB, present or injected: right after TMDH.
    private const int TmppInsertOffset = TmlbHeaderLength + TmdhLength;

    // One embedded TMB: where it sits in the pap, and the face library it declares.
    private readonly record struct TmbSlice(int Start, int Length, string? FaceLibrary);

    /// <summary>
    /// The face libraries declared by TMPP items across the pap's embedded TMBs, in file order, each reported once.
    /// </summary>
    /// <param name="papBytes"> The .pap's raw bytes, never modified. </param>
    /// <returns>The declared face libraries, empty when none is declared.</returns>
    /// <exception cref="InvalidDataException">
    /// The bytes are not a structurally recognizable .pap, or an embedded TMB does not have the TMDH-first,
    /// TMAL-or-TMPP-second shape the walker requires.
    /// </exception>
    public static IReadOnlyList<string> Read(byte[] papBytes)
    {
        ArgumentNullException.ThrowIfNull(papBytes);

        var tmbs = WalkTmbs(papBytes, out _, out _);
        var libraries = new List<string>();

        foreach (var tmb in tmbs)
        {
            if (!string.IsNullOrEmpty(tmb.FaceLibrary) && !libraries.Contains(tmb.FaceLibrary))
                libraries.Add(tmb.FaceLibrary);
        }

        return libraries;
    }

    /// <summary>
    /// Gives every embedded TMB that lacks a TMPP one naming <paramref name="faceLibraryName"/>, leaving a TMB that
    /// already carries a TMPP byte-identical. The result is re-parsed before it is returned.
    /// </summary>
    /// <param name="papBytes"> The .pap's raw bytes, never modified. </param>
    /// <param name="faceLibraryName">
    /// The face library to declare, relative to the skeleton's nonresident animation folder, such as "wrysmile" or
    /// "emot/joy", stored as-is.
    /// </param>
    /// <returns>A fresh array holding the patched .pap.</returns>
    /// <exception cref="ArgumentException"> <paramref name="faceLibraryName"/> is null, empty or contains a NUL. </exception>
    /// <exception cref="InvalidDataException">
    /// The input is not a structurally recognizable .pap (see <see cref="Read"/>), or the spliced result failed to
    /// read back correctly.
    /// </exception>
    public static byte[] Inject(byte[] papBytes, string faceLibraryName)
    {
        ArgumentNullException.ThrowIfNull(papBytes);

        if (string.IsNullOrEmpty(faceLibraryName))
            throw new ArgumentException("A face library name is required; there is nothing to inject without one.",
                nameof(faceLibraryName));

        if (faceLibraryName.Contains('\0'))
            throw new ArgumentException("A face library name cannot contain a NUL character; it would cut the stored string short.",
                nameof(faceLibraryName));

        var tmbs = WalkTmbs(papBytes, out var footerOffset, out var tmbRegionEnd);
        var nameBytes = Encoding.UTF8.GetBytes(faceLibraryName);

        using var output = new MemoryStream(papBytes.Length + tmbs.Count * (TmppLength + nameBytes.Length + 1));

        // The TMBs sit at the tail of the file, so nothing ahead of the TMB region shifts when they grow.
        output.Write(papBytes, 0, footerOffset);

        var expectedLibraries = new List<string>(tmbs.Count);

        for (var index = 0; index < tmbs.Count; index++)
        {
            if (index > 0)
            {
                var padding = PaddingBeforeNextTmb((int)output.Length, footerOffset);
                for (var i = 0; i < padding; i++)
                    output.WriteByte(0);
            }

            var tmb = tmbs[index];

            if (tmb.FaceLibrary != null)
            {
                // A TMB that already declares a library, even an empty one, is copied byte-identical.
                output.Write(papBytes, tmb.Start, tmb.Length);
                expectedLibraries.Add(tmb.FaceLibrary);
            }
            else
            {
                var patched = BuildInjectedTmb(papBytes, tmb, nameBytes);
                output.Write(patched, 0, patched.Length);
                expectedLibraries.Add(faceLibraryName);
            }
        }

        // Bytes after the last TMB are preserved, even though PapFile never writes any.
        output.Write(papBytes, tmbRegionEnd, papBytes.Length - tmbRegionEnd);

        var result = output.ToArray();
        Verify(result, expectedLibraries);
        return result;
    }

    // One TMB with the TMPP spliced in: bytes up to the insertion point, the 12-byte item, the remaining bytes
    // shifted as one block, then the name string and its terminator. Only the total size and item count are
    // rewritten, since every stored offset is forward-pointing and relative to its own item's start+8.
    private static byte[] BuildInjectedTmb(byte[] pap, TmbSlice tmb, byte[] nameBytes)
    {
        var newSize = tmb.Length + TmppLength + nameBytes.Length + 1;
        var bytes = new byte[newSize];

        Buffer.BlockCopy(pap, tmb.Start, bytes, 0, TmppInsertOffset);

        WriteAsciiMagic(bytes, TmppInsertOffset, "TMPP");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(TmppInsertOffset + 4), TmppLength);
        // The name lands at (old size + 12) in the new TMB; the offset is relative to the TMPP's own start+8.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(TmppInsertOffset + 8),
            tmb.Length + TmppLength - (TmppInsertOffset + 8));

        Buffer.BlockCopy(pap, tmb.Start + TmppInsertOffset, bytes, TmppInsertOffset + TmppLength,
            tmb.Length - TmppInsertOffset);

        Buffer.BlockCopy(nameBytes, 0, bytes, tmb.Length + TmppLength, nameBytes.Length);
        bytes[newSize - 1] = 0;

        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), newSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), ReadInt32(bytes, 8) + 1);

        return bytes;
    }

    // Walks the pap's embedded TMB region without parsing the timelines, returning one slice per TMB and the region's
    // bounds. Anything that does not match the TMLB, TMDH, then TMPP-or-TMAL shape is refused.
    private static List<TmbSlice> WalkTmbs(byte[] pap, out int footerOffset, out int tmbRegionEnd)
    {
        if (pap.Length < PapHeaderLength)
            throw new InvalidDataException(
                $"Not a readable .pap: {pap.Length} bytes is shorter than the {PapHeaderLength}-byte header.");

        var magic = ReadInt32(pap, 0);
        if (magic != PapMagic)
            throw new InvalidDataException($"Not a readable .pap: invalid magic 0x{magic:X8}.");

        int count = BinaryPrimitives.ReadInt16LittleEndian(pap.AsSpan(AnimationCountOffset));
        if (count < 0)
            throw new InvalidDataException($"Not a readable .pap: negative animation count {count}.");

        footerOffset = ReadInt32(pap, FooterFieldOffset);
        if (footerOffset < PapHeaderLength || footerOffset > pap.Length)
            throw new InvalidDataException($"Not a readable .pap: TMB region offset {footerOffset} falls outside the file.");

        var tmbs = new List<TmbSlice>(count);
        var position = footerOffset;

        for (var index = 0; index < count; index++)
        {
            if (index > 0)
                position += PaddingBeforeNextTmb(position, footerOffset);

            if (position + TmlbHeaderLength > pap.Length)
                throw new InvalidDataException($"Embedded TMB {index + 1}/{count} at offset {position} overruns the file.");

            if (!HasMagic(pap, position, "TMLB"))
                throw new InvalidDataException($"Embedded TMB {index + 1}/{count} at offset {position} does not start with TMLB.");

            var size = ReadInt32(pap, position + 4);
            if (size < TmppInsertOffset + 8 || position + (long)size > pap.Length)
                throw new InvalidDataException($"Embedded TMB {index + 1}/{count} declares an unusable size of {size} bytes.");

            var itemCount = ReadInt32(pap, position + 8);
            if (itemCount < 2)
                throw new InvalidDataException(
                    $"Embedded TMB {index + 1}/{count} declares {itemCount} items; TMDH and TMAL alone make two.");

            if (!HasMagic(pap, position + TmlbHeaderLength, "TMDH")
                || ReadInt32(pap, position + TmlbHeaderLength + 4) != TmdhLength)
                throw new InvalidDataException(
                    $"Embedded TMB {index + 1}/{count} does not open with a {TmdhLength}-byte TMDH; refusing to guess where a TMPP would go.");

            string? faceLibrary = null;

            if (HasMagic(pap, position + TmppInsertOffset, "TMPP"))
            {
                if (size < TmppInsertOffset + TmppLength || ReadInt32(pap, position + TmppInsertOffset + 4) != TmppLength)
                    throw new InvalidDataException($"Embedded TMB {index + 1}/{count} carries a TMPP of an unexpected size.");

                faceLibrary = ReadTmbOffsetString(pap, position, size, position + TmppInsertOffset + 8, index, count);
            }
            else if (!HasMagic(pap, position + TmppInsertOffset, "TMAL"))
            {
                throw new InvalidDataException(
                    $"Embedded TMB {index + 1}/{count} has neither TMPP nor TMAL after its TMDH; refusing to splice into a structure this walker does not recognize.");
            }

            tmbs.Add(new TmbSlice(position, size, faceLibrary));
            position += size;
        }

        tmbRegionEnd = position;
        return tmbs;
    }

    // Re-parses the spliced bytes with PapFile, then re-walks them and checks every TMB declares the library it was
    // meant to end up with.
    private static void Verify(byte[] result, List<string> expectedLibraries)
    {
        try
        {
            _ = new PapFile(new BinaryReader(new MemoryStream(result)));
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "The injected .pap failed to read back as a structurally valid .pap; refusing to return it.", ex);
        }

        var tmbs = WalkTmbs(result, out _, out _);
        if (tmbs.Count != expectedLibraries.Count)
            throw new InvalidDataException("The injected .pap read back with a different TMB count; refusing to return it.");

        for (var index = 0; index < tmbs.Count; index++)
        {
            if (!string.Equals(tmbs[index].FaceLibrary, expectedLibraries[index], StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Embedded TMB {index + 1}/{tmbs.Count} does not declare its face library after being read back; refusing to return it.");
        }
    }

    // Pads every inter-TMB gap but the last to a 4-byte boundary, measured against the TMB region's own alignment
    // rather than absolute zero, matching PapFile.
    private static int PaddingBeforeNextTmb(int position, int footerOffset)
    {
        var leftover = (position - footerOffset % 4) % 4;
        return leftover == 0 ? 0 : 4 - leftover;
    }

    // The string a TMB offset field points at: 0 is the empty string, anything else is relative to the field's own
    // position and must land on a NUL-terminated run inside the same TMB.
    private static string ReadTmbOffsetString(byte[] pap, int tmbStart, int tmbSize, int fieldPosition, int index, int count)
    {
        var offset = ReadInt32(pap, fieldPosition);
        if (offset == 0)
            return string.Empty;

        var stringPosition = (long)fieldPosition + offset;
        if (stringPosition < tmbStart || stringPosition >= tmbStart + (long)tmbSize)
            throw new InvalidDataException(
                $"Embedded TMB {index + 1}/{count} has a TMPP string offset pointing outside its own bytes.");

        var end = (int)stringPosition;
        var limit = tmbStart + tmbSize;
        while (end < limit && pap[end] != 0)
            end++;

        if (end == limit)
            throw new InvalidDataException(
                $"Embedded TMB {index + 1}/{count} has a TMPP string that runs off the end of its bytes unterminated.");

        return Encoding.UTF8.GetString(pap, (int)stringPosition, end - (int)stringPosition);
    }

    private static int ReadInt32(byte[] data, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));

    private static bool HasMagic(byte[] data, int offset, string magic)
    {
        for (var i = 0; i < magic.Length; i++)
        {
            if (data[offset + i] != (byte)magic[i])
                return false;
        }
        return true;
    }

    private static void WriteAsciiMagic(byte[] data, int offset, string magic)
    {
        for (var i = 0; i < magic.Length; i++)
            data[offset + i] = (byte)magic[i];
    }
}
