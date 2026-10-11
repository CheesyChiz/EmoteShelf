using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace EmoteShelf;

internal sealed record ResourceDigest(string Path, string Sha256);
internal sealed record SyncFrame(string Attempt, ResourceDigest[] Assets, bool Verified, double Phase, double Duration, double Age);
internal sealed record SyncObservation(SyncFrame? Peer, double Age, long Received, long Rtt, string Error);

internal sealed class SyncClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource stop = new();
    private SyncFrame frame;
    private long sampledAt;
    private readonly object gate = new();
    private SyncObservation observation = new(null, 0, 0, 0, "");
    internal SyncObservation Observation => Volatile.Read(ref observation);
    internal string Attempt { get; }
    internal SyncClient(string capability, string attempt)
    { Attempt = attempt; frame = new(attempt, [], false, 0, 0, 0); sampledAt = Environment.TickCount64; _ = Run(capability); }
    internal void Set(ResourceDigest[] assets, bool verified, double phase, double duration)
    { lock (gate) { frame = new(Attempt, assets, verified, phase, duration, 0); sampledAt = Environment.TickCount64; } }
    public void Dispose() => stop.Cancel();
    private async Task Run(string capability)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 48000 };
        while (!stop.IsCancellationRequested)
        {
            try
            {
                SyncFrame outgoing;
                lock (gate) outgoing = frame with { Age = Math.Min(2, (Environment.TickCount64 - sampledAt) / 1000.0) };
                var sent = Environment.TickCount64;
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://anarchyclan.ru/v6/sync");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", capability);
                request.Content = JsonContent.Create(outgoing, options: Json);
                // Bounded relay requires a Content-Length, not chunked encoding.
                await request.Content.LoadIntoBufferAsync();
                using var response = await http.SendAsync(request, stop.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json, stop.Token);
                var peer = result.GetProperty("peer").Deserialize<SyncFrame>(Json);
                if (peer is not null && (peer.Attempt != Attempt || !SyncRules.ValidAssets(peer.Assets) || !SyncRules.ValidPhase(peer.Phase, peer.Duration)))
                    throw new InvalidDataException();
                var age = peer is null ? 0 : result.GetProperty("age").GetDouble();
                if (!double.IsFinite(age) || age is < 0 or > 4) throw new InvalidDataException();
                var now = Environment.TickCount64;
                Volatile.Write(ref observation, new(peer, age, now, now-sent, ""));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex) { Volatile.Write(ref observation, new(null, 0, Environment.TickCount64, 0, ex is HttpRequestException h ? "sync_http_" + (int?)h.StatusCode : "sync_unavailable")); }
            try { await Task.Delay(500, stop.Token); } catch (OperationCanceledException) { return; }
        }
    }
}
