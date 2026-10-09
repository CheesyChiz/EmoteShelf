using System.Numerics;
using System.Text;

namespace EmoteShelf;

// Format reference: WorkingRobot/XIViewer, viewer/src/character/stance.rs.
// Unknown formats/paths return null, never a reason to hide a variant.
public sealed class PapCompatibility(byte[] data)
{
    private ushort U16(int at) => BitConverter.ToUInt16(data, at);
    private uint U32(int at) => BitConverter.ToUInt32(data, at);
    private ulong U64(int at) => BitConverter.ToUInt64(data, at);
    private string Text(int at)
    {
        var end = Array.IndexOf(data, (byte)0, at);
        if (end < at) throw new FormatException();
        return Encoding.UTF8.GetString(data, at, end - at);
    }

    public bool? Matches(string path, ushort model)
    {
        try
        {
            var parts = path.Split('/');
            if (parts.Length < 8 || parts[0] != "chara" || parts[1] != "human" || !path.EndsWith(".pap")) return null;
            var requestedModel = ushort.Parse(parts[2][1..]);
            var set = ushort.Parse(parts[4][1..]);
            var held = parts[5];
            var file = string.Join('/', parts.Skip(6))[..^4];
            var packCount = U16(0);
            var bodyCount = U16(2);
            var dirs = checked((int)U32(8));
            var names = checked((int)U32(12));
            var redirects = 16 + packCount * 12 + bodyCount * 168;
            if (dirs < redirects || names < dirs || names >= data.Length) return null;
            var body = -1;
            for (var i = 0; i < bodyCount; i++)
            {
                var at = 16 + packCount * 12 + i * 168;
                if (U32(at) == model && U16(at + 4) == set) { body = at; break; }
            }
            if (body < 0) return null;
            for (var i = 0; i < packCount; i++)
            {
                var at = 16 + i * 12;
                if (Text(dirs + checked((int)U32(at + 4))) != held) continue;
                var name = Text(names + checked((int)U32(at + 8)));
                var match = (U32(at) & 0x400) != 0 ? name.Contains('*') && file.StartsWith(name[..name.IndexOf('*')], StringComparison.Ordinal) : file == name;
                if (!match) continue;
                var word = i >> 8;
                if (word >= 20) return null;
                var bit = (i >> 2) & 63;
                var bits = U64(body + 8 + word * 8);
                if ((bits & (1UL << bit)) == 0) return model == requestedModel;
                var rank = BitOperations.PopCount(bits & ((1UL << bit) - 1));
                for (var w = 0; w < word; w++) rank += BitOperations.PopCount(U64(body + 8 + w * 8));
                var offset = redirects + (4 * (U16(body + 6) + rank) + (i & 3)) * 8;
                if (offset + 8 > dirs) return null;
                if (U32(offset) == 0 || U16(offset + 4) == 0) return null;
                return U32(offset) == requestedModel && U16(offset + 4) == set && Text(dirs + U16(offset + 6)) == held;
            }
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or FormatException or OverflowException) { return null; }
    }
}
