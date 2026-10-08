using Dalamud.Configuration;

namespace EmoteShelf;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool OverlayVisible = true;
    public List<Bookmark> Bookmarks = [];
    public Dictionary<string, string> CommandOverrides = new(StringComparer.OrdinalIgnoreCase);
}

[Serializable]
public sealed class Bookmark
{
    public string ModDirectory = "";
    public string Name = "";
    public string Command = "";
    public Dictionary<string, List<string>>? SavedOptions;
}
