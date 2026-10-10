using System.Net;
using EmoteShelf;

internal static class LinkDiagnosticsTests
{
    private sealed class RejectRegistration : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("private server response must not be logged") });
    }

    public static async Task Run()
    {
        foreach (var enabled in new[] { false, true })
        {
            using var client = new LinkClient(new string('a', 64), new RejectRegistration(), enabled);
            await client.Completion;
            if (client.Snapshot.State != "error" || !client.LastIssue.Contains("register: HTTP 503"))
                throw new Exception("Registration failure lost stage/status.");
            var lines = new List<string>();
            while (client.TryReadDiagnostic(out var line)) lines.Add(line!);
            if ((lines.Count > 0) != enabled || lines.Any(l => l.Contains("private server") || l.Contains(new string('a', 64))))
                throw new Exception("Diagnostic toggle/privacy regression.");
        }
        Console.WriteLine("Link diagnostic stage, HTTP status, toggle and privacy tests passed.");
    }
}
