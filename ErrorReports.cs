using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace EmoteShelf;

internal sealed record ReportContext(string PairState, bool Anchor, bool AlignStarted, bool PreparationStarted,
    bool Preparing, bool Ready, int StableFrames)
{
    public string ResourceStatus { get; init; } = "none";
    public int LocalAssets { get; init; }
    public int PeerAssets { get; init; }
    public bool LocalVerified { get; init; }
    public bool PeerVerified { get; init; }
    public int SyncHttpStatus { get; init; }
}

// Closed metadata only. Plugin gates Send behind a versioned first-use disclosure.
internal sealed class ErrorReports : IDisposable
{
    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<(string Id, string Data)> queue = Channel.CreateBounded<(string, string)>(32);
    private readonly Dictionary<string, long> recent = [];
    private string status = "";
    public string Status => Volatile.Read(ref status);
    public Task Completion { get; }
    public ErrorReports(HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) };
        Completion = Run();
    }
    public void Send(string code, string detail, string state, bool fresh, bool lightless, string attempt,
        float? distance, float? angle, ReportContext context)
    {
        var now = Environment.TickCount64;
        var key = $"{attempt}/{code}/{detail}";
        foreach (var old in recent.Where(x => now - x.Value > 900000).Select(x => x.Key).ToArray()) recent.Remove(old);
        if (recent.TryGetValue(key, out var previous) && now - previous < (attempt.Length > 0 ? 900000 : 60000)) return;
        var id = Guid.NewGuid().ToString("N");
        var data = JsonSerializer.Serialize(new
        {
            id, version = typeof(ErrorReports).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            attempt, code, detail, state, fresh, lightless, context,
            distance = distance is >= 0 and <= 100 ? distance : null,
            angle = angle is >= 0 and <= 3.142f ? angle : null,
        });
        if (!queue.Writer.TryWrite((id, data)))
        { Volatile.Write(ref status, "Report queue full (32); local diagnostics retained."); return; }
        if (recent.Count >= 256) recent.Remove(recent.MinBy(x => x.Value).Key);
        recent[key] = now;
        Volatile.Write(ref status, "Report queued: " + id);
    }
    private async Task Run()
    {
        long nextRequest = 0;
        try
        {
            await foreach (var report in queue.Reader.ReadAllAsync(stop.Token))
            {
                var delivered = false;
                for (var retry = 0; retry < 4 && !delivered; retry++)
                {
                    var delay = nextRequest - Environment.TickCount64;
                    if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), stop.Token);
                    nextRequest = Environment.TickCount64 + 60000;
                    try
                    {
                        using var body = new StringContent(report.Data, Encoding.UTF8, "application/json");
                        using var response = await http.PostAsync("https://anarchyclan.ru/v3/report", body, stop.Token).ConfigureAwait(false);
                        if (response.StatusCode == HttpStatusCode.Accepted)
                        { delivered = true; Volatile.Write(ref status, "Report received: " + report.Id); }
                        else if ((int)response.StatusCode < 500 && response.StatusCode != HttpStatusCode.TooManyRequests)
                        { Volatile.Write(ref status, $"Report rejected: HTTP {(int)response.StatusCode}; ID {report.Id}."); break; }
                        else Volatile.Write(ref status, $"Report delayed: HTTP {(int)response.StatusCode}; retry {retry + 1}/4; ID {report.Id}");
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
                    catch { Volatile.Write(ref status, $"Report network failure; retry {retry + 1}/4; ID {report.Id}"); }
                }
                if (!delivered) Volatile.Write(ref status, "Report not delivered; ID " + report.Id + ". Local diagnostics retained.");
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { http.Dispose(); }
    }
    public void Dispose() { queue.Writer.TryComplete(); stop.Cancel(); }
}
