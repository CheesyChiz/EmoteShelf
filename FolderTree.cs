namespace EmoteShelf;

public static class FolderTree
{
    public static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
    public static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
    public static bool Contains(string parent, string path) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);
    public static string Join(string parent, string name) => parent.Length == 0 ? name : parent + "/" + name;
    public static IEnumerable<string> Ancestors(string path)
    {
        for (var parent = Parent(path); parent.Length > 0; parent = Parent(parent)) yield return parent;
    }
    public static string Rebase(string path, string oldRoot, string newRoot) => Contains(oldRoot, path) ? newRoot + path[oldRoot.Length..] : path;
}
