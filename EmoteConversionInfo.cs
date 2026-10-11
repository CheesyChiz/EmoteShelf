using System.Text.Json;

namespace EmoteShelf;

internal sealed record EmoteConversionInfo(string Source, string Target, string SourceDirectory = "")
{
    internal static EmoteConversionInfo? Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 256 * 1024 || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1) return null;
            var source = root.GetProperty("sourceCommand").GetString() ?? "";
            var target = root.GetProperty("carrierCommand").GetString() ?? "";
            var directory = root.TryGetProperty("sourceDirectory", out var d) ? d.GetString() ?? "" : "";
            if (!ModScanner.ValidCommand(source) || !ModScanner.ValidCommand(target) || source == target ||
                (directory.Length > 0 && (Path.GetFileName(directory) != directory || directory is "." or ".."))) return null;
            return new(source, target, directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }
}
