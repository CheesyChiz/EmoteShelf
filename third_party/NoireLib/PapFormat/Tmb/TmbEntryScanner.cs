using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace NoireLib.Animations.PapFormat.Tmb;

/// <summary> One TMB item a scan turned up. Path is null for magics that carry no string field, such as C053. </summary>
/// <param name="Magic">The item's four-character magic.</param>
/// <param name="Path">The item's offset string, when it has one and it is set.</param>
public readonly record struct TmbEntryInfo(string Magic, string? Path);

/// <summary>
/// Walks the TMB items inside a .pap or a standalone .tmb and reports what it finds, without building the parsed
/// model. Unlike <see cref="TmbFile"/> it never throws on malformed data and it reaches items the parsed model keeps
/// as opaque raw entries, such as C063 sound paths.
/// </summary>
public static class TmbEntryScanner
{
    private const int PapMagic = 0x20706170;

    // Magic, version, count, model id, model type, variant, and three offsets.
    private const int PapHeaderLength = 26;

    // A TMB stream opens with its magic, its size and its item count.
    private const int TmbHeaderLength = 12;

    // Sanity ceiling on the animation count in a pap header.
    private const int MaxAnimations = 256;

    // Every item carries at least its magic and its size.
    private const int MinItemLength = 8;

    /// <summary>
    /// Every TMB item inside a .pap, across all of its animations.
    /// </summary>
    /// <param name="papBytes">The .pap file's bytes.</param>
    /// <param name="magics">Only report items with these magics, or null for all of them.</param>
    /// <returns>The items found, in file order. Empty when the bytes are not a readable .pap.</returns>
    public static IReadOnlyList<TmbEntryInfo> ScanPap(byte[] papBytes, IReadOnlySet<string>? magics = null)
    {
        var results = new List<TmbEntryInfo>();

        if (papBytes == null || papBytes.Length < PapHeaderLength)
            return results;

        if (BinaryPrimitives.ReadInt32LittleEndian(papBytes.AsSpan(0, 4)) != PapMagic)
            return results;

        var animationCount = BinaryPrimitives.ReadInt16LittleEndian(papBytes.AsSpan(8, 2));

        if (animationCount is <= 0 or > MaxAnimations)
            return results;

        var infoOffset = BinaryPrimitives.ReadInt32LittleEndian(papBytes.AsSpan(14, 4));
        var footerPosition = BinaryPrimitives.ReadInt32LittleEndian(papBytes.AsSpan(22, 4));

        if (infoOffset < 0 || infoOffset >= papBytes.Length || footerPosition < 0 || footerPosition > papBytes.Length)
            return results;

        var position = footerPosition;

        // The inter-TMB padding is measured from where the footer starts, not from zero.
        var alignmentOffset = footerPosition % 4;

        for (var index = 0; index < animationCount; index++)
        {
            if (position + TmbHeaderLength > papBytes.Length)
                break;

            var blobSize = BinaryPrimitives.ReadInt32LittleEndian(papBytes.AsSpan(position + 4, 4));

            if (blobSize <= 0 || position + blobSize > papBytes.Length)
                break;

            ScanStream(papBytes, position, blobSize, magics, results);

            var next = position + blobSize;

            if (index < animationCount - 1)
            {
                var leftover = (next - alignmentOffset) % 4;

                if (leftover != 0)
                    next += 4 - leftover;
            }

            position = next;
        }

        return results;
    }

    /// <summary>
    /// Every TMB item in a standalone .tmb file.
    /// </summary>
    /// <param name="tmbBytes">The .tmb file's bytes.</param>
    /// <param name="magics">Only report items with these magics, or null for all of them.</param>
    /// <returns>The items found, in file order. Empty when the bytes are not a readable TMB.</returns>
    public static IReadOnlyList<TmbEntryInfo> ScanTmb(byte[] tmbBytes, IReadOnlySet<string>? magics = null)
    {
        var results = new List<TmbEntryInfo>();

        if (tmbBytes == null || tmbBytes.Length < TmbHeaderLength)
            return results;

        if (Encoding.ASCII.GetString(tmbBytes, 0, 4) != TmbChunkScanner.StreamTag)
            return results;

        ScanStream(tmbBytes, 0, tmbBytes.Length, magics, results);

        return results;
    }

    /// <summary>
    /// The face-library path a .pap or .tmb declares, taken from the TMPP item's string.
    /// </summary>
    /// <param name="bytes">The file's bytes; a .pap or a standalone .tmb.</param>
    /// <returns>The path, or null when there is none.</returns>
    public static string? FindFaceLibrary(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 4)
            return null;

        var magics = new HashSet<string>(StringComparer.Ordinal) { "TMPP" };

        var entries = bytes.Length >= 4 && Encoding.ASCII.GetString(bytes, 0, 4) == TmbChunkScanner.StreamTag
            ? ScanTmb(bytes, magics)
            : ScanPap(bytes, magics);

        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.Path))
                return entry.Path;
        }

        return null;
    }

    private static void ScanStream(
        byte[] data, int streamStart, int streamSize, IReadOnlySet<string>? magics, List<TmbEntryInfo> results)
    {
        var limit = Math.Min(data.Length, streamStart + streamSize);

        if (streamStart + TmbHeaderLength > limit)
            return;

        var itemCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(streamStart + 8, 4));
        var position = streamStart + TmbHeaderLength;

        for (var index = 0; index < itemCount; index++)
        {
            if (position + MinItemLength > limit)
                break;

            var magic = Encoding.UTF8.GetString(data, position, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position + 4, 4));

            if (size < MinItemLength || position + size > limit)
                break;

            if (magics == null || magics.Contains(magic))
            {
                var path = TmbFile.StringFieldOffsets.TryGetValue(magic, out var fieldOffset)
                    ? ReadOffsetString(data, position, size, fieldOffset)
                    : null;

                results.Add(new TmbEntryInfo(magic, path));
            }

            position += size;
        }
    }

    // The field holds an offset relative to (item start + 8), or 0 for "no string set".
    private static string? ReadOffsetString(byte[] data, int itemStart, int itemSize, int fieldOffset)
    {
        if (fieldOffset + 4 > itemSize || itemStart + fieldOffset + 4 > data.Length)
            return null;

        var relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(itemStart + fieldOffset, 4));

        if (relativeOffset == 0)
            return null;

        var stringStart = itemStart + 8 + relativeOffset;

        if (stringStart < 0 || stringStart >= data.Length)
            return null;

        var end = stringStart;
        while (end < data.Length && data[end] != 0)
            end++;

        return Encoding.UTF8.GetString(data, stringStart, end - stringStart);
    }
}
