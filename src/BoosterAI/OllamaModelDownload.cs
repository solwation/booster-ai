using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BoosterAI;

internal sealed record ModelDownloadProgress(string Status, long Completed, long Total)
{
    public double Percent => Total > 0 ? Math.Clamp(100.0 * Completed / Total, 0, 100) : 0;
}

/// <summary>Explicit model installation only; never used by a metadata or hardware probe.</summary>
internal static class OllamaModelDownload
{
    public static async Task PullAsync(Uri endpoint, DownloadableModel model, Action<ModelDownloadProgress> progress, CancellationToken cancellation)
    {
        if (LocalOllamaProbe.Endpoint(endpoint.AbsoluteUri) is null) throw new ArgumentException("Only loopback HTTP endpoints are supported.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { BaseAddress = endpoint, Timeout = Timeout.InfiniteTimeSpan };
        await PullAsync(client, model, progress, cancellation);
    }

    internal static async Task PullAsync(HttpClient client, DownloadableModel model, Action<ModelDownloadProgress> progress,
        CancellationToken cancellation, TimeSpan? inactivityTimeout = null)
    {
        // Callers cannot turn this UI into a registry URL, cloud-model or arbitrary-tag downloader.
        if (!DownloadableModels.All.Contains(model)) throw new ArgumentException("Only reviewed catalog entries can be installed.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull")
        { Content = JsonContent.Create(new { model = model.Id, stream = true }) };
        using var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        headersDeadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096);
        var line = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(inactivityTimeout ?? TimeSpan.FromSeconds(30));
            var read = await reader.ReadAsync(buffer.AsMemory(), deadline.Token);
            if (read == 0)
            {
                if (line.Length > 0 && Report(line.ToString(), progress)) return;
                throw new IOException("Model download ended without a success response.");
            }
            foreach (var c in buffer.AsSpan(0, read))
            {
                if (c == '\n')
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (line.Length > 0 && Report(line.ToString(), progress)) return;
                    line.Clear();
                }
                else if (c != '\r')
                {
                    if (line.Length >= 16_384) throw new IOException("Model download status exceeded its limit.");
                    line.Append(c);
                }
            }
        }
    }

    private static bool Report(string line, Action<ModelDownloadProgress> progress)
    {
        using var doc = JsonDocument.Parse(line);
        var value = doc.RootElement;
        if (value.TryGetProperty("error", out _)) throw new IOException("Ollama reported a model download error.");
        if (!value.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(status.GetString()))
            throw new IOException("Model download status is missing.");
        long Number(string key) => value.TryGetProperty(key, out var n) && n.TryGetInt64(out var number) && number >= 0 ? number : 0;
        progress(new(status.GetString()!, Number("completed"), Number("total")));
        return status.GetString() == "success";
    }
}
