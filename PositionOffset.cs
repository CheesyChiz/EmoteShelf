using System.Text.Json;
using System.Globalization;

namespace EmoteShelf;

public sealed record PositionOffset(float X, float Y, float Z, float R)
{
    public bool Valid => new[] { X, Y, Z, R }.All(float.IsFinite) &&
        Math.Abs(X) <= 2 && Math.Abs(Y) <= 2 && Math.Abs(Z) <= 2 && Math.Abs(R) <= MathF.PI * 2;
    internal static PositionOffset? Read(string json)
    {
        if (json.Length > 262144) throw new InvalidDataException("heels_response_size");
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("TempOffset", out var temp) || temp.ValueKind == JsonValueKind.Null) return null;
        float Get(string name) => temp.TryGetProperty(name, out var v) ? v.GetSingle() : 0;
        var result = new PositionOffset(Get("X"), Get("Y"), Get("Z"), Get("R"));
        if (!result.Valid) throw new InvalidDataException("heels_offset_range");
        return result;
    }
    internal string Command()
    {
        if (!Valid) throw new InvalidDataException("heels_offset_range");
        return string.Create(CultureInfo.InvariantCulture, $"/heels temp set left {X:R} height {Y:R} forward {Z:R} rotate {R * 180 / MathF.PI:R} silent");
    }
    internal bool Near(PositionOffset? other) => other is not null &&
        Math.Abs(X-other.X) + Math.Abs(Y-other.Y) + Math.Abs(Z-other.Z) + Math.Abs(R-other.R) < .0001f;
}
