using EmoteShelf;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

internal static class TransferClientTests
{
    private sealed class Fake(bool corrupt = false, bool changeLaunch = false) : HttpMessageHandler
    {
        internal bool Accepted, Downloaded, Completed;
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
                if (action == "accept") Accepted = true;
                if (action == "complete") Completed = true;
                if (action == "download")
                {
                    if (!Accepted) throw new Exception("Unsolicited download");
                    Downloaded = true;
                    response = new { chunk = Convert.ToBase64String(corrupt ? new byte[Bytes.Length] : Bytes) };
                }
                else response = new { transfer = Completed ? null : new { id = Id, name = "Test.pmp", size = Bytes.Length,
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(Bytes)), state = Accepted ? "ready" : "offered", incoming = true, offset = 0, remaining = 600,
                    launch = changeLaunch && Accepted ? new TransferLaunch("Changed", "/wave", null, []).Encode() : "" } };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }
    private static async Task Until(Func<bool> check)
    {
        for (var i = 0; i < 100; i++) { if (check()) return; await Task.Delay(50); }
        throw new Exception("Transfer client test timed out");
    }
    internal static async Task Run()
    {
        var launch = new TransferLaunch("Wave", "/wave", null, new() { ["Style"] = ["Default"] });
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
