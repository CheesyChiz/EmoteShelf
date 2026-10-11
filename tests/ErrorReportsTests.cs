using System.Net;
using System.Text.Json;
using EmoteShelf;

internal static class ErrorReportsTests
{
    public static async Task Run()
    {
        var handler = new Capture();
        var reports = new ErrorReports(handler);
        try
        {
            await Task.Delay(30);
            if (handler.Count != 0) throw new Exception("Diagnostics sent before an explicit report.");
            reports.Send("launch_cancelled", "post_prepare_alignment", "linked", true, true,
                new string('c', 64), .05695f, 0, new("paired", true, false, true, false, false, 0));
            var payload = await handler.Body.Task.WaitAsync(TimeSpan.FromSeconds(2));
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.GetProperty("detail").GetString() != "post_prepare_alignment" ||
                json.RootElement.GetProperty("context").GetProperty("Ready").GetBoolean())
                throw new Exception("Report lost failure context.");
            if (json.RootElement.EnumerateObject().Count() != 11 || payload.Contains("Bearer") || payload.Contains("name"))
                throw new Exception("Unexpected diagnostic fields.");
        }
        finally { reports.Dispose(); await reports.Completion.WaitAsync(TimeSpan.FromSeconds(2)); }
        Console.WriteLine("Report inactivity, bounded metadata, failure context and shutdown tests passed.");
    }
    private sealed class Capture : HttpMessageHandler
    {
        public int Count;
        public TaskCompletionSource<string> Body = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Count);
            Body.TrySetResult(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(HttpStatusCode.Accepted);
        }
    }
}
