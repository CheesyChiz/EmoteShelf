using Dalamud.Configuration;

namespace EmoteShelf;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool OverlayVisible = true;
    public List<Bookmark> Bookmarks = [];
    public Dictionary<string, string> CommandOverrides = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> HiddenMods = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> PoseOverrides = new(StringComparer.OrdinalIgnoreCase);
    public bool OverlayLocked;
    public int Columns = 4;
    public float IconSize = 44;
    public float PanelOpacity = 0.85f;
    public bool English;
    public string Language = "";
}

[Serializable]
public sealed class Bookmark
{
    public string ModDirectory = "";
    public string Name = "";
    public string Command = "";
    public uint IconId;
    public int? PoseIndex;
    public Dictionary<string, List<string>>? SavedOptions;
}
