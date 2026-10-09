using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EmoteShelf;

public sealed record PairSnapshot(string State, bool Align, bool Anchor, long StartAt, long ReceivedAt, string Error = "");

// No game API on the networking thread. Plugin.Update consumes immutable snapshots.
public sealed class PairClient : IDisposable
{
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private PairSnapshot snapshot = new("connecting", false, false, 0, Environment.TickCount64);
    private int prepared;
    private int completed;
    private readonly Task worker;
    public PairSnapshot Snapshot => Volatile.Read(ref snapshot);
    public Task Completion => worker;

    public PairClient(string self, string target, string family, bool align, HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("https://anarchyclan.ru/"),
            Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 4096,
        };
        worker = Run(self, target, family, align);
    }

    public void Prepared() => Interlocked.Exchange(ref prepared, 1);
    public void Complete() => Interlocked.Exchange(ref completed, 1);
    public void Dispose() => stop.Cancel();

    private async Task<JsonElement> Send(HttpMethod method, string path, string? token, object? body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Known Content-Length: the bounded relay intentionally rejects streaming bodies.
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
        return document.RootElement.Clone();
    }

    private async Task Run(string self, string target, string family, bool align)
    {
        string? token = null;
        var sentPrepared = false;
        long startAt = 0;
        var deadline = Environment.TickCount64 + 60000;
        try
        {
            var ready = await Send(HttpMethod.Post, "v1/ready", null, new { self, target, animation = family, align }, stop.Token).ConfigureAwait(false);
            token = ready.GetProperty("token").GetString();
            if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidOperationException("Invalid relay response.");
            while (!stop.IsCancellationRequested && Volatile.Read(ref completed) == 0)
            {
                if (Environment.TickCount64 >= deadline) throw new TimeoutException("Pair readiness expired.");
                if (Volatile.Read(ref prepared) != 0 && !sentPrepared)
                {
                    await Send(HttpMethod.Post, "v1/prepared", token, null, stop.Token).ConfigureAwait(false);
                    sentPrepared = true;
                }
                var sent = Environment.TickCount64;
                var state = await Send(HttpMethod.Get, "v1/session", token, null, stop.Token).ConfigureAwait(false);
                var received = Environment.TickCount64;
                var name = state.GetProperty("status").GetString() ?? "";
                if (name is not ("waiting" or "paired" or "scheduled")) throw new InvalidOperationException("The pair was cancelled or expired.");
                if (name == "scheduled")
                {
                    if (!sentPrepared) throw new InvalidOperationException("Unexpected start before local preparation.");
                    if (startAt == 0)
                    {
                        // A slow first sample is not a clock measurement. Try the next
                        // heartbeat instead of declaring a good pair broken by one spike.
                        if (received - sent > 600) { await Task.Delay(50, stop.Token).ConfigureAwait(false); continue; }
                        var measured = PairRules.StartTime(sent, received, state.GetProperty("delay_ms").GetInt32());
                        if (measured - received < 250) throw new InvalidOperationException("Start signal arrived too late; please retry.");
                        startAt = measured;
                    }
                    // The deadline is immutable. Later RTT spikes are not a reason to
                    // recalculate it. Framework checks heartbeat age before executing.
                }
                Volatile.Write(ref snapshot, new PairSnapshot(name, state.GetProperty("align").GetBoolean(),
                    state.GetProperty("anchor").GetBoolean(), startAt, sent + (received - sent) / 2));
                await Task.Delay(150, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Only a short local diagnostic is shown; no bearer tokens or request bodies.
            Volatile.Write(ref snapshot, new PairSnapshot("error", false, false, 0, Environment.TickCount64,
                ex is HttpRequestException requestError ? $"Relay connection failed ({requestError.StatusCode?.ToString() ?? "network"}); please retry." : ex.Message));
        }
        finally
        {
            if (token is not null)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(2000);
                    if (Volatile.Read(ref completed) != 0)
                    {
                        await Task.Delay(150, cleanup.Token).ConfigureAwait(false);
                        await Send(HttpMethod.Post, "v1/finished", token, null, cleanup.Token).ConfigureAwait(false);
                    }
                    else await Send(HttpMethod.Delete, "v1/session", token, null, cleanup.Token).ConfigureAwait(false);
                }
                catch { /* The server also expires abandoned leases. */ }
            }
            http.Dispose();
            // Do not dispose stop here: Dispose may race worker completion.
        }
    }
}
