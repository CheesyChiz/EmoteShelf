namespace EmoteShelf;

public sealed class EmoteCatalog
{
    public (string Name, string Command, uint Icon)? Resolve(string path)
        => path.Contains("j_pose", StringComparison.OrdinalIgnoreCase)
            ? ("Sit on Ground", "/groundsit", 0)
            : path.Contains("pose", StringComparison.OrdinalIgnoreCase)
                ? ("Standing pose", "/changepose", 0) : null;

    public (string Name, string Command, uint Icon)? ResolveChangedItem(string item) => null;
    public bool IsKnownCommand(string command) => command is "/groundsit" or "/cpose" or "/changepose";
}
