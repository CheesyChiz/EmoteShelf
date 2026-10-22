using System.Numerics;

namespace EmoteShelf;

public static class PanelLayout
{
    public static Vector2 Size(int count, int columns, Vector2 cell, Vector2 spacing, Vector2 padding)
    {
        columns = Math.Max(1, columns);
        var width = Math.Min(Math.Max(1, count), columns);
        var rows = Math.Max(1, (count + columns - 1) / columns);
        return new Vector2(width, rows) * cell + new Vector2(width - 1, rows - 1) * spacing + padding * 2;
    }

    public static Vector2 Cell(int index, int count, int columns, bool left, bool up)
    {
        columns = Math.Max(1, columns);
        var width = Math.Min(Math.Max(1, count), columns);
        var rows = Math.Max(1, (count + columns - 1) / columns);
        return new(left ? width - 1 - index % columns : index % columns,
            up ? rows - 1 - index / columns : index / columns);
    }

    public static Vector2 ResizePosition(Vector2 position, Vector2 previousSize, Vector2 size, bool left, bool up)
        => position - (size - previousSize) * new Vector2(left ? 1 : 0, up ? 1 : 0);
}
