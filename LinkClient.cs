using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EmoteShelf;

public sealed record LinkSnapshot(string State, string Partner, string Invitation, int Remaining, long ReceivedAt, string Error = "");

// Only immutable snapshots cross into the framework/UI thread.
public sealed class LinkClient : IDisposable
{
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentQueue<(string Action, string Target, string Invitation)> actions = new();
    private LinkSnapshot snapshot = new("connecting", "", "", 0, Environment.TickCount64);
    public LinkSnapshot Snapshot => Volatile.Read(ref snapshot);
    public Task Completion { get; }
    public string Identity { get; }
    public LinkClient(string identity, HttpMessageHandler? handler = null)
    {
        Identity = identity;
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri("https://anarchyclan.ru/"), Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 4096 };
        Completion = Run();
    }
    public void Act(string action, string target = "", string invitation = "")
    {
        if (actions.Count < 4) actions.Enqueue((action, target, invitation));
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
        try
        {
            token = (await Send("v2/connect", null, new { identity = Identity }, stop.Token)).GetProperty("token").GetString();
            if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidOperationException("Invalid relay response");
            while (!stop.IsCancellationRequested)
            {
                var message = actions.TryDequeue(out var queued) ? queued : (Action: "poll", Target: "", Invitation: "");
                var error = "";
                JsonElement data;
                try { data = await Send("v2/link", token, new { action = message.Action, target = message.Target, invitation = message.Invitation }, stop.Token); }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest && message.Action != "poll")
                {
                    error = "Partner unavailable, busy, or invitation expired. Please retry later.";
                    data = await Send("v2/link", token, new { action = "poll", target = "", invitation = "" }, stop.Token);
                }
                var state = data.GetProperty("state").GetString() ?? "";
                var partner = data.GetProperty("partner").GetString() ?? "";
                if (state is not ("idle" or "incoming" or "outgoing" or "linked") ||
                    (state != "idle" && (partner.Length != 64 || partner.Any(c => !char.IsAsciiHexDigit(c)))))
                    throw new InvalidOperationException("Invalid relay state");
                Volatile.Write(ref snapshot, new(state, partner, data.GetProperty("invitation").GetString() ?? "",
                    data.GetProperty("remaining").GetInt32(), Environment.TickCount64, error));
                await Task.Delay(400, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch { Volatile.Write(ref snapshot, new("error", "", "", 0, Environment.TickCount64, "Pair connection lost. Reconnect in Settings.")); }
        finally
        {
            if (token is not null)
                try { using var cleanup = new CancellationTokenSource(2000); await Send("v2/link", token, new { action = "close", target = "", invitation = "" }, cleanup.Token); } catch { }
            http.Dispose();
        }
    }
}
