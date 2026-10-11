using EmoteShelf;
internal static class OffsetTests
{
    internal static void Run()
    {
        if (PositionOffset.Read("{\"TempOffset\":null}") is not null) throw new Exception("Null offset changed.");
        var value = PositionOffset.Read("{\"TempOffset\":{\"X\":0.2,\"Y\":-0.1,\"Z\":0.3,\"R\":1}}")!;
        if (!value.Valid || !value.Near(new(.2f, -.1f, .3f, 1)) || value.Near(null)) throw new Exception("Offset read/match.");
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("ru-RU");
            if (!value.Command().Contains("left 0.2 height -0.1 forward 0.3")) throw new Exception("Offset command depends on locale.");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        foreach (var invalid in new[] { new PositionOffset(float.NaN, 0, 0, 0), new(3, 0, 0, 0), new(0, 0, 0, float.PositiveInfinity) })
            if (invalid.Valid) throw new Exception("Unsafe offset accepted.");
        Console.WriteLine("Offset bounds, snapshot, equality and invariant-command tests passed.");
    }
}
