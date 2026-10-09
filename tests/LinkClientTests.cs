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
        var offer = new LaunchOffer(PairRules.Hash(Guid.NewGuid().ToString()), PairRules.Family("synthetic"), PairRules.Hash("local-command"), PairRules.Hash("local-role"));
        a.Act("propose", offer: offer);
        await Until(() => b.Snapshot.Launch?.Offer.Id == offer.Id);
        if (!b.Snapshot.Launch!.Incoming || b.Snapshot.Launch.Status != "waiting") throw new Exception("Proposal bypassed consent");
        b.Act("reject_launch", invitation: offer.Id);
        await Until(() => a.Snapshot.Launch is null && b.Snapshot.Launch is null);
        var accepted = offer with { Id = PairRules.Hash(Guid.NewGuid().ToString()) };
        var launchFamily = PairRules.Hash(accepted.Family + accepted.Id);
        using var initiator = new PairClient(a.Identity, b.Identity, launchFamily, false);
        a.Act("propose", offer: accepted);
        await Until(() => b.Snapshot.Launch?.Offer.Id == accepted.Id);
        await Until(() => initiator.Snapshot.State == "waiting");
        b.Act("accept_launch", invitation: accepted.Id);
        await Until(() => a.Snapshot.Launch?.Status == "accepted" && b.Snapshot.Launch?.Status == "accepted");
        using var recipient = new PairClient(b.Identity, a.Identity, launchFamily, false);
        await Until(() => initiator.Snapshot.State == "paired" && recipient.Snapshot.State == "paired");
        initiator.Prepared(); recipient.Prepared();
        await Until(() => initiator.Snapshot.State == "scheduled" && recipient.Snapshot.State == "scheduled");
        await Until(() => Environment.TickCount64 >= Math.Max(initiator.Snapshot.StartAt, recipient.Snapshot.StartAt));
        initiator.Complete(); recipient.Complete();
        await Task.WhenAll(initiator.Completion, recipient.Completion).WaitAsync(TimeSpan.FromSeconds(4));
        a.Act("finish_launch", invitation: accepted.Id);
        await Task.Delay(700);
        if (b.Snapshot.Launch is null) throw new Exception("First completion cleared partner too early");
        b.Act("finish_launch", invitation: accepted.Id);
        await Until(() => a.Snapshot.Launch is null && b.Snapshot.Launch is null);
        b.Act("disconnect");
        await Until(() => a.Snapshot.State == "idle" && b.Snapshot.State == "idle");
        a.Dispose(); b.Dispose();
        await Task.WhenAll(a.Completion, b.Completion).WaitAsync(TimeSpan.FromSeconds(4));
        Console.WriteLine("Live HTTPS link, animation proposal, decline, acceptance, completion and disconnect passed (not an in-game test).");
    }
}
