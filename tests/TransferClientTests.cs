using EmoteShelf;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

internal static class TransferClientTests
{
    private sealed class Fake(bool corrupt = false, bool changeLaunch = false, bool upload = false, int loseComplete = 0, int loseAccept = 0) : HttpMessageHandler
    {
        internal bool Accepted, Downloaded, Completed, Failed;
        internal int FailureAttempts;
        internal int CompleteAttempts;
        internal int AcceptAttempts;
        internal bool Rejected, Second;
        internal const string SecondId = "cccccccccccccccccccccccccccccccc";
        internal void NextOffer() { Accepted = false; Rejected = false; Second = true; }
        internal byte[]? Upload;
        internal readonly MemoryStream Uploaded = new();
        private bool dropped;
        internal readonly byte[] Bytes = ModPackageTests.Package();
        internal const string Partner = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        internal const string Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (request.RequestUri!.AbsolutePath == "/v5/transfer" && request.Content!.Headers.ContentLength is not > 0)
                throw new Exception("Transfer request is missing Content-Length before serialization");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancel));
            var data = doc.RootElement;
            object response;
            if (request.RequestUri!.AbsolutePath == "/v2/connect") response = new { token = new string('x', 43) };
            else if (request.RequestUri.AbsolutePath == "/v2/link") response = new { state = "linked", partner = Partner, invitation = "", remaining = 0 };
            else
            {
                if (request.Headers.Authorization?.Parameter != new string('x', 43)) throw new Exception("Transfer capability missing");
                var action = data.GetProperty("action").GetString();
                if (upload)
                {
                    if (request.Content.Headers.ContentLength > 360000) throw new Exception("Relay body limit exceeded");
                    if (action == "offer") Accepted = true;
                    if (action == "upload")
                    {
                        if (data.GetProperty("offset").GetInt32() != Uploaded.Length) throw new Exception("Duplicate upload after lost acknowledgement");
                        Uploaded.Write(Convert.FromBase64String(data.GetProperty("chunk").GetString()!));
                        if (!dropped) { dropped = true; throw new TaskCanceledException("Simulated lost response after commit"); }
                    }
                    response = new { transfer = !Accepted ? null : new { id = Id, name = "Test.pmp", size = Upload!.Length,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(Upload)), state = Uploaded.Length == Upload.Length ? "ready" : "accepted",
                        incoming = false, offset = Uploaded.Length, remaining = 600, launch = "" } };
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response)) };
                }
                if (action == "accept")
                {
                    AcceptAttempts++;
                    if (!Second && (loseAccept == 3 || loseAccept == 1 && AcceptAttempts == 1))
                        throw new TaskCanceledException("Lost accept before commit");
                    Accepted = true;
                    if (!Second && loseAccept == 2 && AcceptAttempts == 1)
                        throw new TaskCanceledException("Lost accept after commit");
                }
                if (action == "reject") Rejected = true;
                if (Rejected)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"transfer\":null,\"outcome\":\"declined\"}") };
                if (action == "complete")
                {
                    CompleteAttempts++;
                    if (loseComplete == 1 && CompleteAttempts == 1) throw new TaskCanceledException("Lost before commit");
                    Completed = true;
                    if (loseComplete == 2 && CompleteAttempts == 1) throw new TaskCanceledException("Lost after commit");
                }
                if (Completed)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"transfer\":null,\"outcome\":\"completed\"}") };
                if (action == "fail")
                {
                    Failed = true;
                    if (++FailureAttempts == 1) throw new TaskCanceledException("Lost failure acknowledgement");
                }
                if (Failed)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"transfer\":null,\"outcome\":\"installation_failed\"}") };
                if (action == "download")
                {
                    if (!Accepted) throw new Exception("Unsolicited download");
                    Downloaded = true;
                    response = new { chunk = Convert.ToBase64String(corrupt ? new byte[Bytes.Length] : Bytes) };
                }
                else response = new { transfer = Completed ? null : new { id = Second ? SecondId : Id, name = "Test.pmp", size = Bytes.Length,
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(Bytes)), state = Accepted ? "ready" : "offered", incoming = true, offset = 0, remaining = 600,
                    launch = changeLaunch && Accepted ? new TransferLaunch("Changed", "/wave", null, []).Encode() : "" } };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }
    private static async Task Until(Func<bool> check)
    {
        for (var i = 0; i < 200; i++) { if (check()) return; await Task.Delay(50); }
        throw new Exception("Transfer client test timed out");
    }
    internal static async Task Run()
    {
        foreach (var mode in new[] { 1, 2, 3 })
        {
            using var relay = new Fake(loseAccept: mode);
            using var link = new LinkClient(new string('a', 64), relay);
            await Until(() => link.Snapshot.State == "linked");
            using var transfer = new TransferClient(link, Fake.Partner, relay);
            await Until(() => transfer.Offer is not null);
            transfer.Accept(Fake.Id);
            transfer.Accept(Fake.Id); // Repeated UI click must not enqueue another acceptance.
            if (mode == 3)
            {
                await Until(() => relay.Rejected && transfer.Offer is null);
                if (relay.AcceptAttempts != 4 || relay.Downloaded) throw new Exception("Acceptance retry bound failed");
                relay.NextOffer();
                await Until(() => transfer.Offer?.Id == Fake.SecondId);
                if (transfer.Error.Length != 0 || transfer.Download is not null) throw new Exception("Previous failure leaked into new offer");
                transfer.Accept(Fake.SecondId);
            }
            ReceivedPackage? received = null;
            await Until(() => (received ??= transfer.TakeReceived()) is not null);
            if (received!.Id != (mode == 3 ? Fake.SecondId : Fake.Id) || transfer.Error.Length != 0 ||
                relay.AcceptAttempts != (mode == 3 ? 5 : 2)) throw new Exception("Acceptance recovery failed");
            transfer.Dispose(); await transfer.Completion;
            link.Dispose(); await link.Completion;
        }
        foreach (var mode in new[] { 1, 2 })
        {
            using var relay = new Fake(loseComplete: mode);
            using var link = new LinkClient(new string('a', 64), relay);
            await Until(() => link.Snapshot.State == "linked");
            using var transfer = new TransferClient(link, Fake.Partner, relay);
            await Until(() => transfer.Offer is not null);
            transfer.Accept(Fake.Id);
            ReceivedPackage? received = null;
            await Until(() => (received ??= transfer.TakeReceived()) is not null);
            transfer.Act("complete", Fake.Id);
            await Until(() => transfer.Outcome == "completed");
            if (relay.CompleteAttempts != 2 || transfer.Offer is not null || relay.Failed)
                throw new Exception("Completion was not retried safely");
            transfer.Dispose(); await transfer.Completion;
            link.Dispose(); await link.Completion;
        }
        using (var relay = new Fake())
        {
            using var link = new LinkClient(new string('a', 64), relay);
            await Until(() => link.Snapshot.State == "linked");
            using var transfer = new TransferClient(link, Fake.Partner, relay);
            await Until(() => transfer.Offer is not null);
            transfer.Accept(Fake.Id);
            ReceivedPackage? received = null;
            await Until(() => (received ??= transfer.TakeReceived()) is not null);
            transfer.Act("fail", Fake.Id);
            await Until(() => transfer.Outcome == "installation_failed");
            if (relay.FailureAttempts != 2 || relay.Completed || transfer.Offer is not null)
                throw new Exception("Import failure did not survive a lost acknowledgement or falsely completed");
            transfer.Dispose(); await transfer.Completion;
            link.Dispose(); await link.Completion;
        }
        foreach (var bytes in new[] { RandomNumberGenerator.GetBytes(256 * 1024), Enumerable.Repeat((byte)251, 256 * 1024).ToArray() })
        using (var content = TransferClient.RequestContent(new { action = "upload", id = Fake.Id, offset = 0,
            chunk = Convert.ToBase64String(bytes) }))
        {
            await content.LoadIntoBufferAsync();
            if (content.Headers.ContentLength > 360000) throw new Exception("Serialized upload exceeds relay body limit");
            using var body = JsonDocument.Parse(await content.ReadAsStringAsync());
            if (!Convert.FromBase64String(body.RootElement.GetProperty("chunk").GetString()!).SequenceEqual(bytes))
                throw new Exception("Upload serialization changed package bytes");
        }
        var launch = new TransferLaunch("Wave", "/wave", null, new() { ["Style"] = ["Default"] });
        var dance = launch with { Dance = true, PairId = new string('1', 64), Family = new string('2', 64),
            SenderCommand = PairRules.Hash("/wave"), SenderRole = new string('3', 64) };
        if (TransferLaunch.Decode(dance.Encode()) is not { Dance: true, Command: "/wave" })
            throw new Exception("Shared dance transfer mode lost");
        try { (dance with { Command = "/hum" }).Encode(); throw new Exception("Different dance commands accepted"); }
        catch (InvalidDataException) { }
        using (var relay = new Fake(upload: true))
        {
            using var packed = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(packed, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                using (var entry = zip.CreateEntry("meta.json").Open()) entry.Write(Encoding.UTF8.GetBytes("{\"Name\":\"Test\",\"FileVersion\":3}"));
                using (var entry = zip.CreateEntry("default_mod.json").Open()) entry.Write(Encoding.UTF8.GetBytes("{\"Files\":{\"chara/test.pap\":\"anim.pap\"}}"));
                using (var entry = zip.CreateEntry("anim.pap").Open()) entry.Write(RandomNumberGenerator.GetBytes(21 * 1024 * 1024));
            }
            relay.Upload = packed.ToArray();
            using var link = new LinkClient(new string('a', 64), relay);
            await Until(() => link.Snapshot.State == "linked");
            using var transfer = new TransferClient(link, Fake.Partner, relay);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            await transfer.ProposePackage(relay.Upload, "Test.pmp");
            await Until(() => transfer.Offer?.State == "ready");
            if (!relay.Upload.SequenceEqual(relay.Uploaded.ToArray())) throw new Exception("Resumed upload bytes differ");
            Console.WriteLine($"Synthetic 21 MiB client upload including one lost acknowledgement: {timer.Elapsed.TotalSeconds:F2}s (in-memory transport, not Internet throughput).");
            transfer.Dispose(); await transfer.Completion;
            link.Dispose(); await link.Completion;
        }
        if (TransferLaunch.Decode(launch.Encode())?.Command != "/wave") throw new Exception("Launch selection round trip failed");
        foreach (var invalid in new[] { "/wave\n/logout", "/wave <t>", "/wave;logout" })
        {
            try { _ = (launch with { Command = invalid }).Encode(); throw new Exception("Unsafe launch command accepted"); }
            catch (InvalidDataException) { }
        }
        foreach (var (corrupt, changeLaunch) in new[] { (false, false), (true, false), (false, true) })
        {
            using var relay = new Fake(corrupt, changeLaunch);
            using var link = new LinkClient(new string('a', 64), relay);
            await Until(() => link.Snapshot.State == "linked");
            using var transfer = new TransferClient(link, Fake.Partner, relay);
            await Until(() => transfer.Offer is not null);
            await Task.Delay(600);
            if (relay.Downloaded || transfer.TakeReceived() is not null) throw new Exception("Downloaded before acceptance");
            transfer.Accept(Fake.Id);
            if (corrupt || changeLaunch)
            {
                await Until(() => transfer.Error == "transfer_hash");
                if (transfer.TakeReceived() is not null) throw new Exception("Corrupt package delivered for import");
            }
            else
            {
                ReceivedPackage? received = null;
                await Until(() => (received ??= transfer.TakeReceived()) is not null);
                if (!received!.Bytes.SequenceEqual(relay.Bytes)) throw new Exception("Package bytes changed");
                if (transfer.Download is not { Verified: true } progress || progress.Id != Fake.Id || progress.Bytes != relay.Bytes.Length)
                    throw new Exception("Download progress did not confirm verified byte count");
                if (relay.Completed) throw new Exception("Completed before import confirmation");
                transfer.Act("complete", Fake.Id);
                await Until(() => relay.Completed);
            }
            transfer.Dispose(); await transfer.Completion;
            link.Dispose(); await link.Completion;
        }
        Console.WriteLine("Transfer client explicit consent, integrity, import barrier and shutdown tests passed.");
    }
}
