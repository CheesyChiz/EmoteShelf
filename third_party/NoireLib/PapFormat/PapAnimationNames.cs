using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// Reads just the animation names out of a .pap. The game plays a .pap only when its declared name matches the one
/// the emote expects, so an arbitrary .pap at an arbitrary emote path silently does nothing.
/// </summary>
public static class PapAnimationNames
{
    private const int Magic = 0x20706170;

    private const int NameLength  = 32;

    private const int EntryLength = 40;

    /// <summary>The names declared in a .pap, in order.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <returns>The names, or an empty list when the bytes are not a readable .pap.</returns>
    public static IReadOnlyList<string> Read(byte[] data)
    {
        // The smallest header: magic, version, count, model id, model type, variant, and three offsets.
        if (data.Length < 26)
            return [];

        try
        {
            using var stream = new MemoryStream(data, false);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt32() != Magic)
                return [];

            reader.ReadInt32();                        // version
            var count = reader.ReadInt16();
            if (count <= 0)
                return [];

            reader.ReadInt16();                        // model id
            reader.ReadByte();                         // model type
            reader.ReadByte();                         // variant
            var infoOffset = reader.ReadInt32();

            if (infoOffset < 0 || (long)infoOffset + (long)count * EntryLength > data.Length)
                return [];

            stream.Position = infoOffset;

            var names = new List<string>(count);
            for (var index = 0; index < count; ++index)
            {
                var raw = reader.ReadBytes(NameLength);
                if (raw.Length < NameLength)
                    break;

                names.Add(Encoding.UTF8.GetString(raw).TrimEnd('\0'));
                reader.ReadBytes(EntryLength - NameLength);
            }

            return names;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Whether a file declaring one name will play where another is expected.</summary>
    /// <param name="actual">The name the file declares, or null when it could not be read.</param>
    /// <param name="required">The name the emote expects, or null when it is not known.</param>
    /// <returns>True when the names match, and for either name being null.</returns>
    public static bool Matches(string? actual, string? required)
        => actual == null
        || required == null
        || string.Equals(actual, required, StringComparison.Ordinal);
}
