using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PmFolderRunner.Core;

/// <summary>
/// Sends a single request and records its status, elapsed time, and a short,
/// redacted snippet of the response body.
/// </summary>
public static class RequestExecutor
{
    private const int BodyReadBytes = 2048;
    private const int SnippetChars = 200;

    private static readonly Regex BearerToken =
        new(@"(?i)\bBearer\s+[A-Za-z0-9._\-]+", RegexOptions.Compiled);

    public static async Task<RequestResult> FireAsync(
        HttpClient client,
        JsonObject entry,
        VariableResolver vars,
        string? token,
        bool collectionBearer,
        double timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var name = entry["name"].Str() ?? "<unnamed>";
        var folder = entry["_folderPath"].Str() ?? string.Empty;
        var stopwatch = Stopwatch.StartNew();

        object status;
        string snippet;

        try
        {
            using var request = RequestBuilder.Build(entry, vars, token, collectionBearer);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            status = (int)response.StatusCode;
            snippet = await ReadSnippetAsync(response, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;

            status = "ERR";
            snippet = $"Timeout after {timeoutSeconds:0.#}s";
        }
        catch (Exception e)
        {
            status = "ERR";
            snippet = $"{e.GetType().Name}: {e.Message}";
        }

        stopwatch.Stop();

        snippet = Redact(snippet, token);
        if (snippet.Length > SnippetChars)
            snippet = snippet[..SnippetChars];

        var expectedStatus = entry["_expectedStatus"] is JsonValue ev
            && ev.TryGetValue<int>(out var es) ? es : (int?)null;

        return new RequestResult(name, folder, status, stopwatch.Elapsed.TotalMilliseconds,
            snippet, expectedStatus);
    }

    private static async Task<string> ReadSnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[BodyReadBytes];
        var total = 0;

        while (total < BodyReadBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, BodyReadBytes - total), ct);
            if (read == 0)
                break;
            total += read;
        }

        // UTF8.GetString uses the replacement char for invalid bytes rather than throwing.
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    /// <summary>Strips the bearer token (and any "Bearer ..." sequence) from printed text.</summary>
    public static string Redact(string text, string? token)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (!string.IsNullOrEmpty(token))
            text = text.Replace(token, "***REDACTED***");

        return BearerToken.Replace(text, "Bearer ***REDACTED***");
    }
}

