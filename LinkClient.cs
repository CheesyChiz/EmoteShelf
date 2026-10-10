using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmoteShelf;

public sealed record LaunchOffer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("family")] string Family,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("role")] string Role);
public sealed record LaunchInvitation(LaunchOffer Offer, bool Incoming, string Status, int Remaining);
public sealed record LinkSnapshot(string State, string Partner, string Invitation, int Remaining, long ReceivedAt, string Error = "", LaunchInvitation? Launch = null);

// Only immutable snapshots cross into the framework/UI thread.
public sealed class LinkClient : IDisposable
{
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentQueue<(string Action, string Target, string Invitation, LaunchOffer? Offer)> actions = new();
    private LinkSnapshot snapshot = new("connecting", "", "", 0, Environment.TickCount64);
    public LinkSnapshot Snapshot => Volatile.Read(ref snapshot);
    public Task Completion { get; }
    public string Identity { get; }
    private readonly ConcurrentQueue<string> diagnostics = new();
    public volatile bool DiagnosticEnabled;
    public string LastIssue => Volatile.Read(ref lastIssue);
    private string lastIssue = "";
    public bool TryReadDiagnostic(out string? entry) => diagnostics.TryDequeue(out entry);
    private void Trace(string message)
    {
        if (!DiagnosticEnabled) return;
        while (diagnostics.Count >= 64) diagnostics.TryDequeue(out _);
        diagnostics.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff}Z {message}");
    }
    public LinkClient(string identity, HttpMessageHandler? handler = null, bool diagnosticEnabled = false)
    {
        DiagnosticEnabled = diagnosticEnabled;
        Identity = identity;
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri("https://anarchyclan.ru/"), Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 4096 };
        Completion = Run();
    }
    public void Act(string action, string target = "", string invitation = "", LaunchOffer? offer = null)
    {
        if (actions.Count < 4) { actions.Enqueue((action, target, invitation, offer)); Trace("queued " + action); }
        else { Volatile.Write(ref lastIssue, "Request queue full. Wait for the current request, then retry."); Trace("queue full; rejected " + action); }
    }
    public void Dispose() => stop.Cancel();
    private async Task<JsonElement> Send(string path, string? token, object body, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }
    private async Task Run()
    {
        string? token = null;
        var stage = "register";
        try
        {
            Trace("register start");
            token = (await Send("v2/connect", null, new { identity = Identity }, stop.Token)).GetProperty("token").GetString();
            if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidOperationException("Invalid relay response");
            Trace("register accepted");
            var previousState = "";
            while (!stop.IsCancellationRequested)
            {
                var message = actions.TryDequeue(out var queued) ? queued : (Action: "poll", Target: "", Invitation: "", Offer: (LaunchOffer?)null);
                var error = "";
                stage = message.Action;
                var sentAt = Environment.TickCount64;
                if (stage != "poll") Trace("send " + stage);
                JsonElement data;
                try { data = await Send("v2/link", token, new { action = message.Action, target = message.Target, invitation = message.Invitation, offer = message.Offer }, stop.Token); }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest && message.Action != "poll")
                {
                    error = "Partner unavailable, busy, or invitation expired. Please retry later.";
                    Volatile.Write(ref lastIssue, $"{stage}: HTTP 400. Relay rejected the request; it does not report which eligibility check failed. Verify both clients are connected and have no pending invitation.");
                    Trace(stage + " rejected: HTTP 400; resynchronizing");
                    data = await Send("v2/link", token, new { action = "poll", target = "", invitation = "" }, stop.Token);
                }
                var state = data.GetProperty("state").GetString() ?? "";
                var partner = data.GetProperty("partner").GetString() ?? "";
                if (state is not ("idle" or "incoming" or "outgoing" or "linked") ||
                    (state != "idle" && (partner.Length != 64 || partner.Any(c => !char.IsAsciiHexDigit(c)))))
                    throw new InvalidOperationException("Invalid relay state");
                LaunchInvitation? launch = null;
                if (data.TryGetProperty("launch", out var raw) && raw.ValueKind != JsonValueKind.Null)
                {
                    var offer = raw.Deserialize<LaunchOffer>() ?? throw new InvalidOperationException("Missing offer");
                    if (new[] { offer.Id, offer.Family, offer.Command, offer.Role }.Any(v => v is null || v.Length != 64 || v.Any(c => !char.IsAsciiHexDigit(c))))
                        throw new InvalidOperationException("Invalid offer");
                    var launchState = raw.GetProperty("status").GetString() ?? "";
                    if (launchState is not ("waiting" or "accepted") || state != "linked") throw new InvalidOperationException("Invalid proposal state");
                    launch = new(offer, raw.GetProperty("incoming").GetBoolean(), launchState, raw.GetProperty("remaining").GetInt32());
                }
                Volatile.Write(ref snapshot, new(state, partner, data.GetProperty("invitation").GetString() ?? "",
                    data.GetProperty("remaining").GetInt32(), Environment.TickCount64, error, launch));
                var stateKey = state + "/" + (launch?.Status ?? "none");
                if (stateKey != previousState || stage != "poll")
                    Trace($"response action={stage} state={stateKey} elapsedMs={Environment.TickCount64 - sentAt}");
                previousState = stateKey;
                await Task.Delay(400, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            var detail = ex is HttpRequestException request && request.StatusCode is { } code
                ? $"HTTP {(int)code}" : ex is OperationCanceledException ? "timeout (3s)" : ex.GetType().Name;
            Volatile.Write(ref lastIssue, $"{stage}: {detail}. Reconnect in Settings. If registration fails, wait 10 seconds for the previous session to expire.");
            Trace("failed " + LastIssue);
            Volatile.Write(ref snapshot, new("error", "", "", 0, Environment.TickCount64,
                $"Pair connection failed ({detail}). Reconnect in Settings."));
        }
        finally
        {
            if (token is not null)
                try { using var cleanup = new CancellationTokenSource(2000); await Send("v2/link", token, new { action = "close", target = "", invitation = "" }, cleanup.Token); } catch { }
            http.Dispose();
        }
    }
}
