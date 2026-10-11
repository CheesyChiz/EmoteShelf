using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace EmoteShelf;

internal sealed record PackageOffer(string Id, string Name, int Size, string Sha256, string State, bool Incoming, int Offset, int Remaining, string Launch = "");
internal sealed record ReceivedPackage(string Id, byte[] Bytes);

internal sealed class TransferClient : IDisposable
{
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentQueue<(object Message, byte[]? Payload)> commands = new();
    private readonly LinkClient link;
    private readonly string partner;
    private PackageOffer? offer;
    private PackageOffer? acceptedOffer;
    private ReceivedPackage? received;
    private byte[]? outgoing;
    private int proposalPending;
    internal bool ProposalPending => Volatile.Read(ref proposalPending) != 0;
    private string outgoingId = "", acceptedId = "", downloadedId = "", error = "";
    internal PackageOffer? Offer => Volatile.Read(ref offer);
    internal string Error => Volatile.Read(ref error);
    private string outcome = "";
    internal string Outcome => Volatile.Read(ref outcome);
    internal ReceivedPackage? TakeReceived() => Interlocked.Exchange(ref received, null);
    internal Task Completion { get; }
    internal TransferClient(LinkClient link, string partner, HttpMessageHandler? handler = null)
    {
        this.link = link; this.partner = partner;
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new("https://anarchyclan.ru/"), Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 360000 };
        Completion = Run();
    }
    internal void Act(string action, string id)
    {
        if (commands.Count < 4) commands.Enqueue((new { action, id }, null));
    }
    internal async Task Propose(string path, string launch = "")
    {
        _ = TransferLaunch.Decode(launch);
        if (Offer is not null || Interlocked.Exchange(ref proposalPending, 1) != 0) throw new InvalidOperationException("transfer_busy");
        try
        {
        if (!Path.GetExtension(path).Equals(".pmp", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("package_file_type");
        if (new FileInfo(path).Length > ModPackage.MaxCompressed) throw new InvalidDataException("package_size");
        // Bounded even if another process grows the file while reading it.
        await using var input = File.OpenRead(path);
        using var output = new MemoryStream();
        var buffer = new byte[65536]; int read;
        while ((read = await input.ReadAsync(buffer, stop.Token)) > 0)
        {
            if (output.Length + read > ModPackage.MaxCompressed) throw new InvalidDataException("package_size");
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray();
        await Task.Run(() => ModPackage.Validate(bytes), stop.Token);
        commands.Enqueue((new { action = "offer", name = Path.GetFileName(path)[..Math.Min(100, Path.GetFileName(path).Length)],
            size = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), launch }, bytes));
        }
        catch { Volatile.Write(ref proposalPending, 0); throw; }
    }
    private async Task<JsonElement> Send(object message)
    {
        return await SendCore(message);
    }

    internal async Task ProposePackage(byte[] bytes, string name, string launch = "")
    {
        _ = TransferLaunch.Decode(launch);
        if (Offer is not null || Interlocked.Exchange(ref proposalPending, 1) != 0) throw new InvalidOperationException("transfer_busy");
        try
        {
            await Task.Run(() => ModPackage.Validate(bytes), stop.Token);
            if (name.Length is 0 or > 100 || name.Any(char.IsControl)) throw new InvalidDataException("package_metadata");
            commands.Enqueue((new { action = "offer", name, size = bytes.Length,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), launch }, bytes));
        }
        catch { Volatile.Write(ref proposalPending, 0); throw; }
    }

    private async Task<JsonElement> SendCore(object message)
    {
        if (link.Snapshot.State != "linked" || link.Snapshot.Partner != partner || Environment.TickCount64 - link.Snapshot.ReceivedAt >= 4000)
            throw new InvalidOperationException("transfer_not_linked");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v5/transfer") { Content = JsonContent.Create(message) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", link.TransferCapability ?? throw new InvalidOperationException("transfer_not_linked"));
        using var response = await http.SendAsync(request, stop.Token);
        if (!response.IsSuccessStatusCode)
        {
            var failure = "transfer_http_" + (int)response.StatusCode;
            try
            {
                using var details = JsonDocument.Parse(await response.Content.ReadAsStringAsync(stop.Token));
                var code = details.RootElement.GetProperty("error").GetString();
                if (code is not null && code.Length < 80 && code.StartsWith("transfer_") && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) failure = code;
            }
            catch (JsonException) { }
            catch (KeyNotFoundException) { }
            throw new HttpRequestException(failure);
        }
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(stop.Token));
        return doc.RootElement.Clone();
    }
    private async Task Run()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var queued = commands.TryDequeue(out var command);
                    if (queued && command.Payload is not null) outgoing = command.Payload;
                    var result = await Send(queued ? command.Message : new { action = "poll" });
                    var raw = result.GetProperty("transfer");
                    if (result.TryGetProperty("outcome", out var ended) && ended.GetString() is { Length: > 0 } reason)
                        Volatile.Write(ref outcome, reason is "completed" or "declined" or "cancelled" or "expired" or "integrity_failed" ? reason : "");
                    var next = raw.ValueKind == JsonValueKind.Null ? null : raw.Deserialize<PackageOffer>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (next is not null && (next.Size is <= 0 or > ModPackage.MaxCompressed || next.Offset < 0 || next.Offset > next.Size ||
                        next.Id.Length != 32 || !next.Id.All(char.IsAsciiHexDigit) || next.Sha256.Length != 64 || !next.Sha256.All(char.IsAsciiHexDigit) ||
                        next.Name.Length > 100 || next.Name.Any(char.IsControl) || next.State is not ("offered" or "accepted" or "ready")))
                        throw new InvalidDataException("transfer_response");
                    var previous = offer;
                    Volatile.Write(ref offer, next);
                    if (next is not null) Volatile.Write(ref outcome, "");
                    Volatile.Write(ref error, "");
                    if (next is null) { outgoing = null; outgoingId = ""; acceptedId = ""; if (previous is not null) Volatile.Write(ref proposalPending, 0); }
                    else if (!next.Incoming && outgoing is { } uploadPayload)
                    {
                        if (outgoingId.Length == 0) outgoingId = next.Id;
                        if (next.Id == outgoingId && next.State == "accepted")
                        {
                            var offset = next.Offset;
                            await Send(new { action = "upload", id = next.Id, offset,
                                chunk = Convert.ToBase64String(uploadPayload, offset, Math.Min(256*1024, uploadPayload.Length-offset)) });
                        }
                        if (next.State == "ready") outgoing = null;
                    }
                    else if (next.Incoming && next.State == "ready" && acceptedId == next.Id && downloadedId != next.Id)
                    {
                        var accepted = Volatile.Read(ref acceptedOffer);
                        if (accepted is null || accepted.Id != next.Id || accepted.Sha256 != next.Sha256 || accepted.Size != next.Size || accepted.Name != next.Name || accepted.Launch != next.Launch)
                            throw new InvalidDataException("transfer_hash");
                        var payload = new byte[next.Size]; var offset = 0;
                        while (offset < payload.Length)
                        {
                            var data = await Send(new { action = "download", id = next.Id, offset });
                            var chunk = Convert.FromBase64String(data.GetProperty("chunk").GetString()!);
                            if (chunk.Length is 0 or > 256*1024 || offset+chunk.Length > payload.Length) throw new InvalidDataException("transfer_chunk");
                            chunk.CopyTo(payload, offset); offset += chunk.Length;
                            await Task.Delay(100, stop.Token);
                        }
                        if (Convert.ToHexStringLower(SHA256.HashData(payload)) != next.Sha256) throw new InvalidDataException("transfer_hash");
                        ModPackage.Validate(payload);
                        downloadedId = next.Id;
                        Volatile.Write(ref received, new ReceivedPackage(next.Id, payload));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stop.IsCancellationRequested)
                {
                    Volatile.Write(ref error, ex is InvalidDataException or InvalidOperationException or HttpRequestException ? ex.Message : "transfer_failed");
                    // Keep errors visible; never repeatedly retry a rejected archive automatically.
                    if (offer is { Incoming: true } bad && acceptedId == bad.Id) downloadedId = bad.Id;
                    outgoing = null;
                    Volatile.Write(ref proposalPending, 0);
                    await Task.Delay(3000, stop.Token);
                }
                await Task.Delay(500, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { outgoing = null; received = null; http.Dispose(); }
    }
    internal void Accept(string id)
    {
        if (Offer is not { Incoming: true, State: "offered" } current || current.Id != id || acceptedId == id) return;
        Volatile.Write(ref acceptedOffer, current);
        acceptedId = id; Act("accept", id);
    }
    public void Dispose() => stop.Cancel();
}
