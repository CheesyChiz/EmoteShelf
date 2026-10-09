using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EmoteShelf;

/// <summary>Maps game animation timeline keys to their actual emote commands.</summary>
public sealed class EmoteCatalog
{
    private readonly Dictionary<string, (string Name, string Command, uint Icon)> timelines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Name, string Command, uint Icon)> names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Emote> commands = new(StringComparer.OrdinalIgnoreCase);

    public EmoteCatalog(IDataManager data)
    {
        foreach (var emote in data.GetExcelSheet<Emote>())
        {
            var textCommand = emote.TextCommand.ValueNullable;
            if (textCommand is null) continue;
            var command = textCommand.Value.Command.ToString().Trim();
            if (!command.StartsWith('/')) command = "/" + command;
            if (!ModScanner.ValidCommand(command)) continue;
            commands.TryAdd(command, emote);
            var name = emote.Name.ToString().Trim();
            if (name.Length == 0) name = command;
            names.TryAdd(name, (name, command, emote.Icon));
            foreach (var row in emote.ActionTimeline)
            {
                var timeline = row.ValueNullable;
                if (timeline is null) continue;
                var key = timeline.Value.Key.ToString().Trim().ToLowerInvariant();
                if (key.Length > 0) timelines.TryAdd(key, (name, command, emote.Icon));
            }
        }
    }

    public bool? IsUnlocked(string command, IUnlockState unlocks)
        => commands.TryGetValue(command, out var emote) ? unlocks.IsEmoteUnlocked(emote) : null;

    public bool IsKnownCommand(string command) => commands.ContainsKey(command);

    public (string Name, string Command, uint Icon)? Resolve(string gamePath)
    {
        var file = Path.GetFileNameWithoutExtension(gamePath).ToLowerInvariant();
        if (timelines.TryGetValue(file, out var exact)) return exact;
        if (timelines.TryGetValue("emote/" + file, out exact)) return exact;
        // A .pap often adds a phase suffix to the ActionTimeline key.
        var match = timelines.Where(x => file.StartsWith(Path.GetFileName(x.Key) + "_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Key.Length).FirstOrDefault();
        return match.Key is null ? null : match.Value;
    }

    public (string Name, string Command, uint Icon)? ResolveChangedItem(string changedItem)
    {
        var name = changedItem.Trim();
        if (name.StartsWith("Emote:", StringComparison.OrdinalIgnoreCase)) name = name[6..].Trim();
        return names.TryGetValue(name, out var found) ? found : null;
    }
}
