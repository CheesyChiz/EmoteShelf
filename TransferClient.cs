using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace EmoteShelf;

internal sealed record PackageOffer(string Id, string Name, int Size, string Sha256, string State, bool Incoming, int Offset, int Remaining, string Launch = "");
internal sealed record ReceivedPackage(string Id, byte[] Bytes);
internal sealed record DownloadProgress(string Id, int Bytes, bool Verified = false);

internal sealed class TransferClient : IDisposable
{
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentQueue<(object Message, byte[]? Payload)> commands = new();
    private readonly LinkClient link;
    private readonly string partner;
    private readonly Action<string>? diagnostic;
    private string stage = "poll", failedId = "";
    private int failures;
    private object? pendingTerminal;
    // This is an application/json HTTP body, never HTML. Preserve Base64 '+' bytes
    // instead of expanding them to six-byte Unicode escapes beyond the relay limit.
    internal static JsonContent RequestContent(object message) => JsonContent.Create(message,
        options: new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    private PackageOffer? offer;
    private PackageOffer? acceptedOffer;
    private ReceivedPackage? received;
    private DownloadProgress? downloadProgress;
    internal DownloadProgress? Download => Volatile.Read(ref downloadProgress);
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
    internal TransferClient(LinkClient link, string partner, HttpMessageHandler? handler = null, Action<string>? diagnostic = null)
    {
        this.link = link; this.partner = partner;
        this.diagnostic = diagnostic;
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
        failedId = "";
        failures = 0;
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
            failedId = "";
            failures = 0;
            commands.Enqueue((new { action = "offer", name, size = bytes.Length,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), launch }, bytes));
        }
        catch { Volatile.Write(ref proposalPending, 0); throw; }
    }

    private async Task<JsonElement> SendCore(object message)
    {
        if (link.Snapshot.State != "linked" || link.Snapshot.Partner != partner || Environment.TickCount64 - link.Snapshot.ReceivedAt >= 4000)
            throw new InvalidOperationException("transfer_not_linked");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v5/transfer") { Content = RequestContent(message) };
        using (var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(stop.Token)))
            stage = body.RootElement.GetProperty("action").GetString() ?? "unknown";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", link.TransferCapability ?? throw new InvalidOperationException("transfer_not_linked"));
        await request.Content.LoadIntoBufferAsync();
        if (request.Content.Headers.ContentLength > 360000) throw new InvalidDataException("transfer_body_size");
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
                    (object Message, byte[]? Payload) command = default;
                    var queued = false;
                    if (pendingTerminal is not null)
                    {
                        command = (pendingTerminal, null);
                        queued = true;
                    }
                    else queued = commands.TryDequeue(out command);
                    if (queued && JsonSerializer.SerializeToElement(command.Message).GetProperty("action").GetString() is "fail" or "complete" or "reject" or "cancel")
                        pendingTerminal = command.Message;
                    if (queued && command.Payload is not null) outgoing = command.Payload;
                    var result = await Send(queued ? command.Message : new { action = "poll" });
                    var raw = result.GetProperty("transfer");
                    if (result.TryGetProperty("outcome", out var ended) && ended.GetString() is { Length: > 0 } reason)
                        Volatile.Write(ref outcome, reason is "completed" or "declined" or "cancelled" or "expired" or "integrity_failed" or "installation_failed" ? reason : "");
                    var next = raw.ValueKind == JsonValueKind.Null ? null : raw.Deserialize<PackageOffer>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (next is not null && (next.Size is <= 0 or > ModPackage.MaxCompressed || next.Offset < 0 || next.Offset > next.Size ||
                        next.Id.Length != 32 || !next.Id.All(char.IsAsciiHexDigit) || next.Sha256.Length != 64 || !next.Sha256.All(char.IsAsciiHexDigit) ||
                        next.Name.Length > 100 || next.Name.Any(char.IsControl) || next.State is not ("offered" or "accepted" or "ready")))
                        throw new InvalidDataException("transfer_response");
                    pendingTerminal = null;
                    var previous = offer;
                    Volatile.Write(ref offer, next);
                    if (next is not null) Volatile.Write(ref outcome, "");
                    if (failedId.Length == 0) Volatile.Write(ref error, "");
                    if (next is null) { outgoing = null; outgoingId = ""; acceptedId = ""; if (previous is not null) Volatile.Write(ref proposalPending, 0); }
                    else if (next.Id == failedId)
                        await Send(new { action = next.Incoming ? "reject" : "cancel", id = next.Id });
                    else if (!next.Incoming && outgoing is { } uploadPayload)
                    {
                        if (outgoingId.Length == 0) outgoingId = next.Id;
                        while (next.Id == outgoingId && next.State == "accepted" && commands.IsEmpty)
                        {
                            var offset = next.Offset;
                            var count = Math.Min(256*1024, uploadPayload.Length-offset);
                            if (count <= 0) throw new InvalidDataException("transfer_response");
                            var acknowledgement = await Send(new { action = "upload", id = next.Id, offset,
                                chunk = Convert.ToBase64String(uploadPayload, offset, count) });
                            var confirmed = acknowledgement.GetProperty("transfer").Deserialize<PackageOffer>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (confirmed is null || confirmed.Id != next.Id || confirmed.Incoming || confirmed.Size != next.Size ||
                                confirmed.Sha256 != next.Sha256 || confirmed.Offset != offset + count ||
                                confirmed.State != (confirmed.Offset == confirmed.Size ? "ready" : "accepted"))
                                throw new InvalidDataException("transfer_response");
                            next = confirmed;
                            Volatile.Write(ref offer, next);
                            failures = 0;
                        }
                        if (next.State == "ready") outgoing = null;
                    }
                    else if (next.Incoming && next.State == "ready" && acceptedId == next.Id && downloadedId != next.Id)
                    {
                        var accepted = Volatile.Read(ref acceptedOffer);
                        if (accepted is null || accepted.Id != next.Id || accepted.Sha256 != next.Sha256 || accepted.Size != next.Size || accepted.Name != next.Name || accepted.Launch != next.Launch)
                            throw new InvalidDataException("transfer_hash");
                        var payload = new byte[next.Size]; var offset = 0;
                        Volatile.Write(ref downloadProgress, new(next.Id, 0));
                        while (offset < payload.Length && commands.IsEmpty)
                        {
                            var data = await Send(new { action = "download", id = next.Id, offset });
                            var chunk = Convert.FromBase64String(data.GetProperty("chunk").GetString()!);
                            if (chunk.Length is 0 or > 256*1024 || offset+chunk.Length > payload.Length) throw new InvalidDataException("transfer_chunk");
                            chunk.CopyTo(payload, offset); offset += chunk.Length;
                            Volatile.Write(ref downloadProgress, new(next.Id, offset));
                        }
                        if (offset < payload.Length) continue; // Process cancellation before downloading more bytes.
                        if (Convert.ToHexStringLower(SHA256.HashData(payload)) != next.Sha256) throw new InvalidDataException("transfer_hash");
                        ModPackage.Validate(payload);
                        Volatile.Write(ref downloadProgress, new(next.Id, payload.Length, true));
                        downloadedId = next.Id;
                        Volatile.Write(ref received, new ReceivedPackage(next.Id, payload));
                    }
                    failures = 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stop.IsCancellationRequested)
                {
                    var code = ex switch
                    {
                        OperationCanceledException => "transfer_timeout",
                        HttpRequestException h when !h.Message.StartsWith("transfer_") => "transfer_network",
                        JsonException or KeyNotFoundException or FormatException => "transfer_response",
                        InvalidDataException or InvalidOperationException or HttpRequestException => ex.Message,
                        _ => "transfer_internal",
                    };
                    if (code.Length > 80 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) code = "transfer_internal";
                    diagnostic?.Invoke($"stage={stage} code={code} type={ex.GetType().Name} attempt={failures + 1}");
                    Volatile.Write(ref error, code);
                    // Poll again before retrying uploads: a timed-out response may have
                    // committed its chunk already. The relay offset is authoritative.
                    if ((stage is "upload" or "download" or "poll" or "fail" or "complete" or "reject" or "cancel") &&
                        (code is "transfer_timeout" or "transfer_network" or "transfer_http_429" or "transfer_http_502" or "transfer_http_503" or "transfer_http_504") && ++failures <= 3)
                    {
                        await Task.Delay(1000, stop.Token);
                        continue;
                    }
                    // Keep errors visible; never repeatedly retry a rejected archive automatically.
                    pendingTerminal = null;
                    failedId = offer?.Id ?? "";
                    if (offer is { Incoming: true } bad && acceptedId == bad.Id) downloadedId = bad.Id;
                    outgoing = null;
                    Volatile.Write(ref proposalPending, 0);
                    await Task.Delay(3000, stop.Token);
                }
                await Task.Delay(500, stop.Token); // Idle/status polling only; chunks have no artificial delay.
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
