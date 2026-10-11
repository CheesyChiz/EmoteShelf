using Dalamud.Configuration;

namespace EmoteShelf;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool OverlayVisible = true;
    public bool SharedDance;
    public List<Bookmark> Bookmarks = [];
    public List<PairPreset> PairPresets = [];
    public Dictionary<string, string> CommandOverrides = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> HiddenMods = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> PoseOverrides = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Folders = [];
    public Dictionary<string, string> ModFolders = new(StringComparer.OrdinalIgnoreCase);
    public bool OverlayLocked;
    public int Columns = 4;
    public bool PanelGrowLeft;
    public bool PanelGrowUp;
    public float IconSize = 44;
    public float PanelOpacity = 0.85f;
    public bool English;
    public bool PairEnabled;
    public bool MaintainPairPhase = true;
    public bool PairDebug;
    // Old optional-upload preference does not acknowledge the new mandatory policy.
    public int DiagnosticsNoticeVersion;
    public int TransferNoticeVersion;
    public int TransferReceiveNoticeVersion;
    public float PairNoticeX = -1;
    public float PairNoticeY = -1;
    public HashSet<string> ClosedFolders = new(StringComparer.OrdinalIgnoreCase);
    public string Language = "";
    public string LastPmpDirectory = "";
}

[Serializable]
public sealed class Bookmark
{
    public string Alias = "";
    public string RoleLabel = "";
    public PositionOffset? Offset;
    public string? BrioPose;
    public string ModDirectory = "";
    public string Name = "";
    public string Command = "";
    public uint IconId;
    public int? PoseIndex;
    public Dictionary<string, List<string>>? SavedOptions;
}

[Serializable]
public sealed class PairPreset
{
    public string Name = "";
    public Bookmark First = new();
    public Bookmark Second = new();
}
