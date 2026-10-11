using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmoteShelf;

// Network data is a bounded selection, never an arbitrary chat command or local path.
internal sealed record TransferLaunch(string Label, string Command, int? Pose,
    Dictionary<string, List<string>> Options)
{
    public string PairId { get; init; } = "";
    public string Family { get; init; } = "";
    public string SenderCommand { get; init; } = "";
    public string SenderRole { get; init; } = "";
    internal string Encode()
    {
        var value = JsonSerializer.Serialize(this);
        _ = Decode(value);
        return value;
    }

    internal static TransferLaunch? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > 8192) throw new InvalidDataException("transfer_launch_invalid");
        TransferLaunch? selection;
        try { selection = JsonSerializer.Deserialize<TransferLaunch>(value, new JsonSerializerOptions { MaxDepth = 8 }); }
        catch (JsonException) { throw new InvalidDataException("transfer_launch_invalid"); }
        static bool Text(string? s, int max) => s is { Length: > 0 } && s.Length <= max && !s.Any(char.IsControl);
        if (selection is null || !Text(selection.Label, 200) || !Text(selection.Command, 64) ||
            !Regex.IsMatch(selection.Command, "^/[a-zA-Z]+$") || selection.Pose is < 0 or > 15 ||
            selection.Options is null || selection.Options.Count > 64 ||
            selection.Options.Any(p => !Text(p.Key, 200) || p.Value is null || p.Value.Count > 64 || p.Value.Any(v => !Text(v, 200))))
            throw new InvalidDataException("transfer_launch_invalid");
        var pair = new[] { selection.PairId, selection.Family, selection.SenderCommand, selection.SenderRole };
        if (pair.Any(s => s != "") && pair.Any(s => s is null || !Regex.IsMatch(s, "^[0-9a-f]{64}$")))
            throw new InvalidDataException("transfer_launch_invalid");
        return selection;
    }
}
