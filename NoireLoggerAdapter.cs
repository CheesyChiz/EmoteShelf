// Emote Shelf adapter; not part of the imported NoireLib source.
namespace NoireLib;

internal static class NoireLogger
{
    public static void LogDebug(string message, string? prefix = null) => System.Diagnostics.Trace.WriteLine(prefix + message);
    public static void LogError(string message) => System.Diagnostics.Trace.WriteLine(message);
    public static void LogError(Exception error, string message)
        => System.Diagnostics.Trace.WriteLine(message + " exception=" + error.GetType().Name);
}
