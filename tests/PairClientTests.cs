using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using EmoteShelf;

internal static class PairClientTests
{
    public static async Task Live()
    {
        var self = PairRules.Hash(Guid.NewGuid().ToString());
        var target = PairRules.Hash(Guid.NewGuid().ToString());
        var family = PairRules.Hash(Guid.NewGuid().ToString());
        using var a = new PairClient(self, target, family, false);
        using var b = new PairClient(target, self, family, false);
        try { await Until(() => a.Snapshot.State == "paired" && b.Snapshot.State == "paired"); }
        catch { throw new Exception($"Live relay readiness failed: A={a.Snapshot.State} {a.Snapshot.Error}; B={b.Snapshot.State} {b.Snapshot.Error}"); }
        a.Prepared(); b.Prepared();
        try { await Until(() => a.Snapshot.State == "scheduled" && b.Snapshot.State == "scheduled"); }
        catch { throw new Exception($"Live relay scheduling failed: A={a.Snapshot.State} {a.Snapshot.Error}; B={b.Snapshot.State} {b.Snapshot.Error}"); }
        var skew = Math.Abs(a.Snapshot.StartAt - b.Snapshot.StartAt);
        if (skew > 150) throw new Exception("Live relay clock estimates differ by more than 150ms.");
        await Until(() => Environment.TickCount64 >= Math.Max(a.Snapshot.StartAt, b.Snapshot.StartAt));
        if (a.Snapshot.State != "scheduled" || b.Snapshot.State != "scheduled")
            throw new Exception($"Live pair cancelled before start: A={a.Snapshot.State} {a.Snapshot.Error}; B={b.Snapshot.State} {b.Snapshot.Error}");
        a.Complete(); b.Complete();
        await Task.WhenAll(a.Completion, b.Completion).WaitAsync(TimeSpan.FromSeconds(4));
        Console.WriteLine($"Live HTTPS pair barrier passed; estimated deadline difference {skew}ms. This does not test the game.");
    }

    public static async Task Run()
    {
        var relay = new FakeRelay();
        using var a = new PairClient(new('a', 64), new('b', 64), new('c', 64), true, new FakeHandler(relay));
        using var b = new PairClient(new('b', 64), new('a', 64), new('c', 64), true, new FakeHandler(relay));
        await Until(() => a.Snapshot.State == "paired" && b.Snapshot.State == "paired");
        a.Prepared();
        await Task.Delay(200);
        if (a.Snapshot.State != "paired") throw new Exception("Started without both clients prepared.");
        b.Prepared();
        await Until(() => a.Snapshot.State == "scheduled" && b.Snapshot.State == "scheduled");
        if (Math.Abs(a.Snapshot.StartAt - b.Snapshot.StartAt) > 50) throw new Exception("Client start clocks disagree.");
        await Until(() => Environment.TickCount64 >= Math.Max(a.Snapshot.StartAt, b.Snapshot.StartAt));
        a.Complete(); b.Complete();
        await Task.WhenAll(a.Completion, b.Completion).WaitAsync(TimeSpan.FromSeconds(3));
        if (relay.Finished != 2 || relay.Deleted != 0 || !relay.HeadersOnly) throw new Exception("Completion/capability isolation regressed.");

        var cancelled = new FakeRelay();
        var c = new PairClient(new('a', 64), new('b', 64), new('c', 64), false, new FakeHandler(cancelled));
        await Until(() => c.Snapshot.State == "paired");
        c.Dispose();
        await c.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        if (cancelled.Deleted != 1) throw new Exception("Cancellation did not revoke consent.");

        var invalid = new FakeRelay { UnexpectedStart = true };
        using var d = new PairClient(new('a', 64), new('b', 64), new('c', 64), false, new FakeHandler(invalid));
        await d.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        if (d.Snapshot.State != "error" || invalid.Deleted != 1) throw new Exception("Unprepared remote start accepted.");
        Console.WriteLine("Pair client barrier, shared clock, cancellation and unsolicited-start tests passed.");
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 4000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new Exception("Pair client test timed out.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeRelay
    {
        public readonly object Gate = new();
        public int Tokens, Finished, Deleted;
        public bool HeadersOnly = true, UnexpectedStart;
        public readonly HashSet<string> Prepared = [];
        public long StartAt;
    }

    private sealed class FakeHandler(FakeRelay relay) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (relay.Gate)
            {
                var path = request.RequestUri!.AbsolutePath;
                var token = request.Headers.Authorization?.Parameter;
                object body = new { ok = true };
                if (path == "/v1/ready")
                {
                    if (request.Content?.Headers.ContentLength is not > 0) throw new Exception("Readiness needs a bounded Content-Length.");
                    body = new { token = new string((char)('a' + relay.Tokens++), 43) };
                }
                else
                {
                    relay.HeadersOnly &= token?.Length == 43 && !path.Contains(token);
                    if (request.Method == HttpMethod.Delete) relay.Deleted++;
                    else if (path == "/v1/prepared")
                    {
                        relay.Prepared.Add(token!);
                        if (relay.Prepared.Count == 2 && relay.StartAt == 0) relay.StartAt = Environment.TickCount64 + 700;
                    }
                    else if (path == "/v1/finished") relay.Finished++;
                    else body = new { status = relay.StartAt > 0 || relay.UnexpectedStart ? "scheduled" : "paired",
                        delay_ms = (int)Math.Max(0, relay.StartAt - Environment.TickCount64), align = true, anchor = token![0] == 'a' };
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });
            }
        }
    }
}
