using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace EmoteShelf;

// Closed schema only. Never send log strings, actor identities or file paths.
internal sealed class ErrorReports : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(3) };
    private CancellationTokenSource consent = new();
    private readonly Dictionary<string, long> sent = [];
    private long nextSend;
    private int busy;
    private string status = "";
    public string Status => Volatile.Read(ref status);
    public void Revoke()
    {
        consent.Cancel();
        consent.Dispose();
        consent = new();
        Volatile.Write(ref status, "Disabled. Previously received reports expire within 14 days.");
    }
    public void Send(string code, string detail, string state, bool fresh, bool lightless, string attempt, float? distance, float? angle)
    {
        var now = Environment.TickCount64;
        if (now < nextSend || sent.GetValueOrDefault(code) > now || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        nextSend = now + 60000;
        sent[code] = now + 600000;
        var id = Guid.NewGuid().ToString("N");
        var data = JsonSerializer.Serialize(new
        {
            id, version = typeof(ErrorReports).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            attempt, code, detail, state, fresh, lightless,
            distance = distance is >= 0 and <= 100 ? distance : null,
            angle = angle is >= 0 and <= 3.142f ? angle : null,
        });
        _ = Upload(data, id, consent.Token);
    }
    private async Task Upload(string data, string id, CancellationToken cancel)
    {
        try
        {
            using var body = new StringContent(data, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("https://anarchyclan.ru/v3/report", body, cancel).ConfigureAwait(false);
            Volatile.Write(ref status, response.StatusCode == System.Net.HttpStatusCode.Accepted
                ? "Report received: " + id : $"Report not received: HTTP {(int)response.StatusCode}. Gameplay is unaffected.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch { Volatile.Write(ref status, "Report could not be sent. No automatic retry; gameplay is unaffected."); }
        finally { Interlocked.Exchange(ref busy, 0); }
    }
    public void Dispose() { consent.Cancel(); http.Dispose(); consent.Dispose(); }
}
