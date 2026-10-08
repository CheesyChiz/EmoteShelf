using Dalamud.Configuration;

namespace EmoteShelf;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool OverlayVisible = true;
    public List<Bookmark> Bookmarks = [];
    public Dictionary<string, string> CommandOverrides = new(StringComparer.OrdinalIgnoreCase);
    public bool OverlayLocked;
    public int Columns = 4;
    public float IconSize = 44;
    public bool English;
}

[Serializable]
public sealed class Bookmark
{
    public string ModDirectory = "";
    public string Name = "";
    public string Command = "";
    public uint IconId;
    public Dictionary<string, List<string>>? SavedOptions;
}
