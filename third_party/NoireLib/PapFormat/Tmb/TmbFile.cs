using NoireLib.Animations.PapFormat.Tmb.Entries;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NoireLib.Animations.PapFormat.Tmb;

/// <summary>
/// A parsed TMB timeline: the actors/tracks/entries (C009, C010, ...) that drive one <see cref="PapFormat.PapAnimation"/>.
/// Re-serializing an unmodified file returns its original bytes verbatim; only edits that touch an entry's
/// editable string (see <see cref="InvalidateSourceLayout"/>) force the timeline to be rebuilt from the parsed model.
/// </summary>
public class TmbFile
{
    /// <summary>
    /// The offset of the string-offset field inside an item of each magic, the single description of that part
    /// of the wire format.
    /// </summary>
    public static readonly Dictionary<string, int> StringFieldOffsets = new()
    {
        // TMPP: magic 4 + size 4 put the face-library path offset 8 bytes in. Listed although it is never
        // editable here, so a string-table rebuild carries its string; untracked, its offset dangles past the
        // end of the rebuilt file and the re-read fails in Tmpp.
        ["TMPP"] = 0x08,
        ["C002"] = 0x18,
        [C009.MAGIC] = 0x14,
        [C010.MAGIC] = 0x20,
        ["C012"] = 0x14,
        ["C063"] = 0x14,
        ["C173"] = 0x14,
    };

    /// <summary>
    /// The offsets of the float blocks an item of each magic names by (offset, count), the other kind of field
    /// a rebuilt timeline places rather than copies. C012's four are its animated scale, rotation, position and
    /// rgba curves, which live in the extra section and move with it.
    /// </summary>
    public static readonly Dictionary<string, int[]> ExtraFloatFieldOffsets = new()
    {
        ["C012"] = [0x20, 0x28, 0x30, 0x38],
    };

    /// <summary> The timeline's TMDH header item. </summary>
    public Tmdh HeaderTmdh { get; private set; } = null!;
    /// <summary> The timeline's optional TMPP (face library path) header item; <see cref="Tmb.Tmpp.IsAssigned"/> is false when absent. </summary>
    public Tmpp HeaderTmpp { get; private set; } = null!;
    /// <summary> The timeline's TMAL header item, listing the actors below. </summary>
    public Tmal HeaderTmal { get; private set; } = null!;

    /// <summary> Every actor (TMAC) in the timeline. </summary>
    public readonly List<Tmac> Actors = [];
    /// <summary> Every track (TMTR) across every actor. </summary>
    public readonly List<Tmtr> AllTracks = [];
    /// <summary> Every timeline entry (C009, C010, ...) across every track. </summary>
    public readonly List<TmbEntry> AllEntries = [];

    private readonly byte[] SourceBytes = [];
    private bool PreserveSourceLayout = true;

    private readonly record struct TmbStringReference(int ItemOffset, int FieldOffset, string Value, int OriginalOffset);

    /// <summary> Parses a TMB timeline from <paramref name="binaryReader"/>'s current position. </summary>
    /// <param name="binaryReader">The reader positioned at the TMLB magic.</param>
    public TmbFile(BinaryReader binaryReader)
    {
        var startPos = binaryReader.BaseStream.Position;
        var reader = new TmbReader(binaryReader);

        try
        {
            reader.ReadInt32(); // TMLB
            var size = reader.ReadInt32();
            var numEntries = reader.ReadInt32();

            HeaderTmdh = new Tmdh(this, reader);
            HeaderTmpp = new Tmpp(this, reader);
            HeaderTmal = new Tmal(this, reader);

            // The header items are counted in numEntries; TMPP is absent from files with no face library.
            var expectedItems = numEntries - (HeaderTmpp.IsAssigned ? 3 : 2);

            for (var i = 0; i < expectedItems; i++)
            {
                var itemPos = reader.Reader.BaseStream.Position;
                ParseItem(reader, Actors, AllTracks, AllEntries);
            }

            HeaderTmal.PickActors(reader);

            foreach (var actor in Actors)
                actor.PickTracks(reader);

            foreach (var track in AllTracks)
                track.PickEntries(reader);

            RefreshIds();

            binaryReader.BaseStream.Position = startPos;
            SourceBytes = binaryReader.ReadBytes(size);

            CaptureTrackConditions(startPos, size, reader.TimelinePositions);

            binaryReader.BaseStream.Position = startPos + size;
        }
        catch (System.Exception ex)
        {
            NoireLogger.LogError(ex, $"Error reading TMB at position {binaryReader.BaseStream.Position}");
            NoireLogger.LogError($"Actors: {Actors.Count}, Tracks: {AllTracks.Count}, Entries: {AllEntries.Count}");
            throw;
        }
    }

    // Hands each track the bytes its condition offset points at. The blocks sit end to end between the last item and
    // the id blocks, and each says its own length; the room up to whatever comes next is only the ceiling that length
    // must fit under. Taking the ceiling instead swallows the curve data a C012 names and copies it into every
    // rebuild.
    private void CaptureTrackConditions(long startPos, int size, IReadOnlyList<long> timelinePositions)
    {
        var withCondition = AllTracks.Where(track => track.ConditionOffset != 0)
            .OrderBy(track => track.ConditionPosition).ToList();

        if (withCondition.Count == 0)
            return;

        var sectionEnd = size;

        foreach (var position in timelinePositions)
        {
            var relative = (int)(position - startPos);

            if (relative > 0 && relative < sectionEnd)
                sectionEnd = relative;
        }

        for (var index = 0; index < withCondition.Count; index++)
        {
            var track = withCondition[index];
            var start = (int)(track.ConditionPosition - startPos);
            var ceiling = index + 1 < withCondition.Count
                ? (int)(withCondition[index + 1].ConditionPosition - startPos)
                : sectionEnd;

            if (start < 0 || ceiling > SourceBytes.Length || ceiling <= start)
            {
                NoireLogger.LogDebug($"A track's condition block reads as {start}..{ceiling} in a "
                    + $"{SourceBytes.Length} byte timeline, which cannot be right; it is left out.");

                continue;
            }

            var declared = Tmtr.DeclaredConditionLength(SourceBytes, start);
            var end = declared > 0 && start + declared <= ceiling ? start + declared : ceiling;

            track.SetCondition(SourceBytes[start..end]);
        }
    }

    private void ParseItem(TmbReader reader, List<Tmac> actors, List<Tmtr> tracks, List<TmbEntry> entries)
    {
        var savePos = reader.Reader.BaseStream.Position;
        var magic = reader.ReadString(4);
        var size = reader.ReadInt32();
        reader.Reader.BaseStream.Position = savePos;

        TmbItem entry;

        try
        {
            if (magic == "TMAC")
            {
                var actor = new Tmac(this, reader);
                actors.Add(actor);
                entry = actor;
            }
            else if (magic == "TMTR")
            {
                var track = new Tmtr(this, reader);
                tracks.Add(track);
                entry = track;
            }
            else if (magic == "C009")
            {
                var c009 = new C009(this, reader);
                entries.Add(c009);
                entry = c009;
            }
            else if (magic == "C010")
            {
                var c010 = new C010(this, reader);
                entries.Add(c010);
                entry = c010;
            }
            else
            {
                var rawEntry = new TmbEntryRaw(this, reader, magic, size);
                entries.Add(rawEntry);
                entry = rawEntry;
            }

            if (entry is TmbItemWithId entryId)
            {
                reader.RegisterItemWithId(entryId);
            }
        }
        catch (System.Exception ex)
        {
            NoireLogger.LogError(ex, $"Error parsing {magic} at pos={savePos}, size={size:X}");
            throw;
        }
    }

    /// <summary> Reassigns sequential ids to every actor, track and entry, in that order, starting at 2. </summary>
    public void RefreshIds()
    {
        short id = 2;
        foreach (var actor in Actors) actor.Id = id++;
        foreach (var track in AllTracks) track.Id = id++;
        foreach (var entry in AllEntries) entry.Id = id++;
    }

    /// <summary>
    /// Marks this timeline as restructured so <see cref="ToBytes"/> rebuilds it from the parsed model rather than
    /// patching the original bytes, which already covers an edited string field on its own.
    /// </summary>
    public void InvalidateSourceLayout()
    {
        PreserveSourceLayout = false;
    }

    /// <summary>Drops every entry of one magic, from every track it sits in.</summary>
    /// <param name="magic">The entry magic to drop.</param>
    /// <returns>How many were dropped.</returns>
    /// <remarks>
    /// A rebuild relays the string table: only drop a magic whose payload holds no string offset of its own.
    /// </remarks>
    public int RemoveEntries(string magic)
    {
        var removed = 0;

        foreach (var track in AllTracks)
            removed += track.Entries.RemoveAll(entry => entry.Magic == magic);

        if (removed == 0)
            return 0;

        AllEntries.RemoveAll(entry => entry.Magic == magic);
        InvalidateSourceLayout();

        return removed;
    }


    /// <summary>
    /// Serializes this timeline, returning the original bytes with any edited strings and numeric fields patched
    /// in place, or a full rebuild from the parsed model when the layout was invalidated.
    /// </summary>
    /// <returns>The serialized timeline.</returns>
    public byte[] ToBytes()
    {
        if (TryWritePreservedBytes(out var preservedBytes))
            return preservedBytes;

        return WriteRebuiltBytes();
    }

    private bool TryWritePreservedBytes(out byte[] bytes)
    {
        bytes = [];

        if (!PreserveSourceLayout || SourceBytes.Length < 12)
            return false;

        var itemsCount = ReadInt32(SourceBytes, 8);
        if (itemsCount <= 0)
            return false;

        var c009Entries = AllEntries.OfType<C009>().ToList();
        var c010Entries = AllEntries.OfType<C010>().ToList();
        var stringReferences = new List<TmbStringReference>();

        // Wire order per magic mirrors AllEntries order per magic (both come from the same sequential parse), so
        // each item matches back to its model object to re-emit its time.
        var entriesByMagic = new Dictionary<string, List<TmbEntry>>(StringComparer.Ordinal);
        var entryIndexByMagic = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in AllEntries)
        {
            if (!entriesByMagic.TryGetValue(entry.Magic, out var sameMagic))
            {
                sameMagic = [];
                entriesByMagic[entry.Magic] = sameMagic;
            }

            sameMagic.Add(entry);
        }

        // Numeric fields re-written from the parsed model into the preserved bytes, byte-identical when
        // unchanged. This is the only safe route for a numeric edit: a full rebuild relocates the string
        // section while raw unknown-magic entries re-emit their payload verbatim, leaving any string offset
        // inside one (a C063 sound entry's scd path, for instance) dangling, which crashes the game.
        var numericPatches = new List<(int Offset, byte[] Value)>();

        var itemOffset = 12;
        var c009Index = 0;
        var c010Index = 0;
        var stringSectionStart = SourceBytes.Length;
        var hasEditableStringChanges = false;

        for (var i = 0; i < itemsCount; i++)
        {
            if (itemOffset + 8 > SourceBytes.Length)
                return false;

            var magic = Encoding.UTF8.GetString(SourceBytes, itemOffset, 4);
            var itemSize = ReadInt32(SourceBytes, itemOffset + 4);

            if (itemSize < 8 || itemOffset + itemSize > SourceBytes.Length)
                return false;

            // TMDH: magic 4 + size 4 + id 2 + Unk1 2 put Length 12 bytes in (see Tmdh's read order).
            if (magic == "TMDH" && itemSize >= 14)
                numericPatches.Add((itemOffset + 12, BitConverter.GetBytes(HeaderTmdh.GetLength())));

            // Every entry's time: magic 4 + size 4 + id 2 put it 10 bytes in, matched to its model object by
            // per-magic wire order. A count mismatch means the model no longer describes these bytes.
            if (entriesByMagic.TryGetValue(magic, out var sameMagicEntries) && itemSize >= 12)
            {
                var entryIndex = entryIndexByMagic.GetValueOrDefault(magic);
                if (entryIndex >= sameMagicEntries.Count)
                    return false;

                numericPatches.Add((itemOffset + 10, BitConverter.GetBytes(sameMagicEntries[entryIndex].GetTime())));
                entryIndexByMagic[magic] = entryIndex + 1;
            }

            if (StringFieldOffsets.TryGetValue(magic, out var fieldOffset))
            {
                if (fieldOffset + sizeof(int) > itemSize)
                    return false;

                var originalOffset = ReadInt32(SourceBytes, itemOffset + fieldOffset);
                var originalPath = ReadOffsetString(SourceBytes, itemOffset, originalOffset);
                string path;

                if (magic == C009.MAGIC)
                {
                    if (c009Index >= c009Entries.Count)
                        return false;

                    path = c009Entries[c009Index].Path.Value ?? string.Empty;
                    hasEditableStringChanges |= !string.Equals(path, originalPath, StringComparison.Ordinal);

                    // C009: magic 4 + size 4 + id 2 + time 2 put Duration 12 bytes in (see C009's layout).
                    numericPatches.Add((itemOffset + 12, BitConverter.GetBytes(c009Entries[c009Index].GetDuration())));
                    c009Index++;
                }
                else if (magic == C010.MAGIC)
                {
                    if (c010Index >= c010Entries.Count)
                        return false;

                    path = c010Entries[c010Index].Path.Value ?? string.Empty;
                    hasEditableStringChanges |= !string.Equals(path, originalPath, StringComparison.Ordinal);

                    // C010: Duration sits 12 bytes in as in C009, and the playback segment floats at 24
                    // (start) and 28 (end). All three move together, or a clamped clip plays its full segment.
                    numericPatches.Add((itemOffset + 12, BitConverter.GetBytes(c010Entries[c010Index].GetDuration())));
                    numericPatches.Add((itemOffset + 24, BitConverter.GetBytes(c010Entries[c010Index].GetAnimationStart())));
                    numericPatches.Add((itemOffset + 28, BitConverter.GetBytes(c010Entries[c010Index].GetAnimationEnd())));
                    c010Index++;
                }
                else
                {
                    path = originalPath;
                }

                stringReferences.Add(new(itemOffset, fieldOffset, path, originalOffset));

                if (originalOffset != 0)
                {
                    var absoluteOffset = itemOffset + 8 + originalOffset;
                    if (absoluteOffset < 0 || absoluteOffset >= SourceBytes.Length)
                        return false;

                    stringSectionStart = Math.Min(stringSectionStart, absoluteOffset);
                }
            }

            itemOffset += itemSize;
        }

        if (c009Index != c009Entries.Count || c010Index != c010Entries.Count)
            return false;

        if (!hasEditableStringChanges)
        {
            var copy = SourceBytes.ToArray();

            foreach (var (offset, value) in numericPatches)
                value.CopyTo(copy, offset);

            bytes = copy;
            return true;
        }

        var patchedBytes = SourceBytes.Take(stringSectionStart).ToList();

        // Numeric patches always land inside the item region, which precedes every string, so the truncated
        // copy above already contains all their offsets.
        foreach (var (offset, value) in numericPatches)
        {
            for (var i = 0; i < value.Length; i++)
                patchedBytes[offset + i] = value[i];
        }
        var rebuiltStrings = new List<byte>();
        var writtenStrings = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var reference in stringReferences)
        {
            if (string.IsNullOrEmpty(reference.Value))
            {
                WriteInt32(patchedBytes, reference.ItemOffset + reference.FieldOffset, 0);
                continue;
            }

            if (!writtenStrings.TryGetValue(reference.Value, out var relativeOffset))
            {
                relativeOffset = rebuiltStrings.Count;
                rebuiltStrings.AddRange(Encoding.UTF8.GetBytes(reference.Value));
                rebuiltStrings.Add(0);
                writtenStrings[reference.Value] = relativeOffset;
            }

            var absoluteOffset = stringSectionStart + relativeOffset;
            var offset = absoluteOffset - (reference.ItemOffset + 8);
            WriteInt32(patchedBytes, reference.ItemOffset + reference.FieldOffset, offset);
        }

        patchedBytes.AddRange(rebuiltStrings);
        WriteInt32(patchedBytes, 4, patchedBytes.Count);
        bytes = patchedBytes.ToArray();
        return true;
    }

    private byte[] WriteRebuiltBytes()
    {
        var startPos = 0; // Always starts at 0 in memory stream

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(System.Text.Encoding.UTF8.GetBytes("TMLB"));
        writer.Write(0); // placeholder for size

        RefreshIds();

        var timelineCount = Actors.Count + Actors.Select(x => x.Tracks.Count).Sum() + AllTracks.Select(x => x.Entries.Count).Sum();

        var items = new List<TmbItem> { HeaderTmdh };
        if (HeaderTmpp.IsAssigned) items.Add(HeaderTmpp);
        items.Add(HeaderTmal);
        items.AddRange(Actors);
        items.AddRange(AllTracks);
        items.AddRange(AllEntries);

        var itemLength = items.Sum(x => x.Size);
        var extraLength = items.Sum(x => x.ExtraSize);
        var timelineLength = timelineCount * sizeof(short);

        var tmbWriter = new TmbWriter(itemLength, extraLength, timelineLength);

        writer.Write(items.Count);
        foreach (var item in items)
        {
            var beforePos = tmbWriter.Position;
            tmbWriter.StartPosition = tmbWriter.Position;
            item.Write(tmbWriter);
            var written = tmbWriter.Position - beforePos;
        }

        tmbWriter.WriteTo(writer);
        tmbWriter.Dispose();

        var endPos = writer.BaseStream.Position;
        writer.BaseStream.Position = startPos + 4;
        writer.Write((int)(endPos - startPos));
        writer.BaseStream.Position = endPos;

        return ms.ToArray();
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));
    }

    private static void WriteInt32(List<byte> data, int offset, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);

        for (var i = 0; i < buffer.Length; i++)
            data[offset + i] = buffer[i];
    }

    private static string ReadOffsetString(byte[] data, int itemOffset, int offset)
    {
        if (offset == 0)
            return string.Empty;

        var stringOffset = itemOffset + 8 + offset;
        if (stringOffset < 0 || stringOffset >= data.Length)
            return string.Empty;

        var endOffset = stringOffset;
        while (endOffset < data.Length && data[endOffset] != 0)
            endOffset++;

        return Encoding.UTF8.GetString(data, stringOffset, endOffset - stringOffset);
    }

    /// <summary> Collects every C009 entry in the timeline, across every actor and track. </summary>
    /// <returns>The C009 entries.</returns>
    public List<C009> GetAllC009Entries()
    {
        return Actors.SelectMany(actor => actor.GetAllC009Entries()).ToList();
    }
}
