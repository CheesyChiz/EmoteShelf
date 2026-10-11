using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NoireLib.Animations.PapFormat.Tmb;

/// <summary>One chunk of a TMB stream: where it starts, what it is, and how long it runs.</summary>
/// <param name="Offset">The chunk's offset within the window it was found in.</param>
/// <param name="Tag">The four-character tag, such as "TMTR" or "C010".</param>
/// <param name="Size">The chunk's declared size in bytes, including its own header.</param>
public readonly record struct TmbChunk(int Offset, string Tag, int Size);

/// <summary>A name-shaped run of bytes found in a window.</summary>
/// <param name="Offset">Where the run starts within the window.</param>
/// <param name="Text">The run, decoded as ASCII.</param>
public readonly record struct TmbNameSighting(int Offset, string Text);

/// <summary>
/// Identifies a TMB chunk stream inside a raw byte window that may start mid-chunk, stop mid-word, or not be a TMB
/// at all; nothing here throws. Use <see cref="TmbFile"/> for a whole well-formed file instead. The caller must
/// prove the window's bytes are readable before handing them over.
/// </summary>
public static class TmbChunkScanner
{
    /// <summary>The smallest chunk, being its own tag and size word.</summary>
    public const int MinChunkSize = 8;

    /// <summary>Sanity ceiling for a size word; real chunks are tens of bytes.</summary>
    public const int MaxChunkSize = 0x10000;

    /// <summary>The tag a TMB stream opens with.</summary>
    public const string StreamTag = "TMLB";

    /// <summary>The stream header: tag, total size, entry count.</summary>
    public const int StreamHeaderSize = 0x0C;

    /// <summary>Whether a byte can appear in a chunk tag, which is uppercase letters and digits only.</summary>
    /// <param name="value">The byte to test.</param>
    /// <returns>True when the byte is tag-shaped.</returns>
    public static bool IsTagByte(byte value)
        => (value >= (byte)'A' && value <= (byte)'Z')
           || (value >= (byte)'0' && value <= (byte)'9');

    /// <summary>
    /// Reads a four-character tag, refusing bytes that are not tag-shaped rather than decoding arbitrary bytes.
    /// </summary>
    /// <param name="data">The window to read from.</param>
    /// <param name="offset">Where in the window to read.</param>
    /// <param name="tag">The tag, when one was there.</param>
    /// <returns>True when a tag was read.</returns>
    public static bool TryReadTag(byte[] data, int offset, out string tag)
    {
        tag = string.Empty;

        if (data == null || offset < 0 || offset + 4 > data.Length)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if (!IsTagByte(data[offset + i]))
                return false;
        }

        tag = Encoding.ASCII.GetString(data, offset, 4);
        return true;
    }

    /// <summary>Reads a little-endian 32-bit integer.</summary>
    /// <param name="data">The window to read from.</param>
    /// <param name="offset">Where in the window to read.</param>
    /// <returns>The value read.</returns>
    public static int ReadInt32(byte[] data, int offset)
        => data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

    /// <summary>
    /// Reads one chunk header, accepting the size word only when it is at least a header long, under the sanity
    /// ceiling, a multiple of four and inside the window, so a tag-shaped run inside a name cannot anchor a walk.
    /// </summary>
    /// <param name="data">The window to read from.</param>
    /// <param name="offset">Where in the window the chunk should start.</param>
    /// <param name="chunk">The chunk, when one was there.</param>
    /// <returns>True when a plausible chunk was read.</returns>
    public static bool TryReadChunk(byte[] data, int offset, out TmbChunk chunk)
    {
        chunk = default;

        if (!TryReadTag(data, offset, out var tag))
            return false;

        if (offset + MinChunkSize > data.Length)
            return false;

        var size = ReadInt32(data, offset + 4);
        if (size < MinChunkSize || size > MaxChunkSize || size % 4 != 0)
            return false;

        if (offset + size > data.Length)
            return false;

        chunk = new TmbChunk(offset, tag, size);
        return true;
    }

    /// <summary>
    /// Walks chunks forward from an offset. Bounded by both a chunk count and an end offset, so a window of
    /// garbage terminates the walk instead of running it to the end of the buffer.
    /// </summary>
    /// <param name="data">The window to walk.</param>
    /// <param name="start">Where the first chunk should start.</param>
    /// <param name="maxChunks">The most chunks to return.</param>
    /// <param name="endExclusive">The offset no chunk may run past.</param>
    /// <returns>The chunks read, in order, stopping at the first thing that is not one.</returns>
    public static List<TmbChunk> Walk(byte[] data, int start, int maxChunks, int endExclusive)
    {
        var chunks = new List<TmbChunk>();

        if (data == null || start < 0)
            return chunks;

        var cursor = start;
        while (chunks.Count < maxChunks && TryReadChunk(data, cursor, out var chunk)
               && chunk.Offset + chunk.Size <= endExclusive)
        {
            chunks.Add(chunk);
            cursor += chunk.Size;
        }

        return chunks;
    }

    /// <summary>
    /// Whether a byte can appear in a name; narrower than printable, since TMB payloads are mostly small integers
    /// that a loose test turns into name sightings.
    /// </summary>
    /// <param name="value">The byte to test.</param>
    /// <returns>True when the byte is name-shaped.</returns>
    public static bool IsNameByte(byte value)
        => (value >= (byte)'a' && value <= (byte)'z')
           || (value >= (byte)'A' && value <= (byte)'Z')
           || (value >= (byte)'0' && value <= (byte)'9')
           || value == (byte)'_' || value == (byte)'/' || value == (byte)'.' || value == (byte)'-';

    /// <summary>
    /// The name-shaped runs inside one stream; the range matters, since a window usually holds neighbouring
    /// streams whose names would otherwise be attributed to this one.
    /// </summary>
    /// <param name="data">The window to search.</param>
    /// <param name="start">Where to start searching.</param>
    /// <param name="endExclusive">Where to stop.</param>
    /// <param name="minLength">The shortest run worth reporting.</param>
    /// <param name="maxResults">The most runs to return.</param>
    /// <returns>The runs found, in order.</returns>
    public static List<TmbNameSighting> FindNames(byte[] data, int start, int endExclusive, int minLength, int maxResults)
    {
        var found = new List<TmbNameSighting>();

        if (data == null || minLength <= 0 || maxResults <= 0)
            return found;

        var first = Math.Max(0, start);
        var last = Math.Min(data.Length, endExclusive);
        var runStart = -1;

        for (var index = first; index <= last; index++)
        {
            var isName = index < last && IsNameByte(data[index]);
            if (isName)
            {
                if (runStart < 0)
                    runStart = index;

                continue;
            }

            if (runStart >= 0 && index - runStart >= minLength)
            {
                found.Add(new TmbNameSighting(runStart, Encoding.ASCII.GetString(data, runStart, index - runStart)));

                if (found.Count >= maxResults)
                    return found;
            }

            runStart = -1;
        }

        return found;
    }

    /// <summary>
    /// Reads a TMLB stream header, returning false rather than throwing when there is no plausible one at
    /// the offset.
    /// </summary>
    /// <param name="data">The window to read from.</param>
    /// <param name="offset">Where the header should start.</param>
    /// <param name="totalSize">The stream's declared total size.</param>
    /// <param name="entryCount">The stream's declared entry count.</param>
    /// <returns>True when a plausible header was read.</returns>
    public static bool TryReadStreamHeader(byte[] data, int offset, out int totalSize, out int entryCount)
    {
        totalSize = 0;
        entryCount = 0;

        if (!TryReadTag(data, offset, out var tag) || tag != StreamTag)
            return false;

        if (offset + StreamHeaderSize > data.Length)
            return false;

        var size = ReadInt32(data, offset + 4);
        var count = ReadInt32(data, offset + 8);

        if (size < StreamHeaderSize || size > MaxChunkSize || count < 0 || count > MaxChunkSize / MinChunkSize)
            return false;

        totalSize = size;
        entryCount = count;
        return true;
    }

    /// <summary>
    /// A content-derived identity for a stream, built from its size, entry count and tag sequence, for telling two
    /// resident timelines apart when retargeting has given several the same animation name.
    /// </summary>
    /// <param name="totalSize">The stream's declared total size.</param>
    /// <param name="entryCount">The stream's declared entry count.</param>
    /// <param name="chunks">The chunks walked out of the stream.</param>
    /// <param name="names">The name sightings found inside it.</param>
    /// <param name="maxTags">How many tags to spell out before summarising the rest as a count.</param>
    /// <param name="maxNames">How many names to include.</param>
    /// <returns>The fingerprint.</returns>
    public static string Fingerprint(
        int totalSize, int entryCount, IReadOnlyList<TmbChunk> chunks,
        IReadOnlyList<TmbNameSighting> names, int maxTags, int maxNames)
        => $"0x{totalSize:X}/{entryCount}e/"
           + (chunks.Count == 0 ? "-" : string.Join("-", chunks.Take(maxTags).Select(chunk => chunk.Tag)))
           + (chunks.Count > maxTags ? $"+{chunks.Count - maxTags}" : "")
           + "/" + (names.Count == 0 ? "-" : string.Join(",", names.Take(maxNames).Select(name => name.Text)));
}
