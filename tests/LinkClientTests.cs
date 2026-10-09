using EmoteShelf;

internal static class LinkClientTests
{
    public static async Task Live()
    {
        using var a = new LinkClient(PairRules.Hash(Guid.NewGuid().ToString()));
        using var b = new LinkClient(PairRules.Hash(Guid.NewGuid().ToString()));
        async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 8000;
            while (!condition())
            {
                if (a.Snapshot.State == "error" || b.Snapshot.State == "error" || Environment.TickCount64 > deadline)
                    throw new Exception($"Link test failed: {a.Snapshot} / {b.Snapshot}");
                await Task.Delay(50);
            }
        }
        await Until(() => a.Snapshot.State == "idle" && b.Snapshot.State == "idle");
        a.Act("invite", b.Identity);
        await Until(() => a.Snapshot.State == "outgoing" && b.Snapshot.State == "incoming");
        if (b.Snapshot.Partner != a.Identity) throw new Exception("Wrong invitation identity");
        b.Act("accept", invitation: b.Snapshot.Invitation);
        await Until(() => a.Snapshot.State == "linked" && b.Snapshot.State == "linked");
        b.Act("disconnect");
        await Until(() => a.Snapshot.State == "idle" && b.Snapshot.State == "idle");
        a.Dispose(); b.Dispose();
        await Task.WhenAll(a.Completion, b.Completion).WaitAsync(TimeSpan.FromSeconds(4));
        Console.WriteLine("Live HTTPS invite, explicit acceptance, single partner and disconnect passed (not an in-game test).");
    }
}
