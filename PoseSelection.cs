namespace EmoteShelf;

public static class PoseSelection
{
    public static byte Predecessor(int target, int highest)
    {
        if (highest < 0 || highest > byte.MaxValue || target < 0 || target > highest)
            throw new ArgumentOutOfRangeException(nameof(target));
        return (byte)(target == 0 ? highest : target - 1);
    }
}
