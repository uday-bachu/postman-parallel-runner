using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace PmFolderRunner.Core;

/// <summary>
/// Turns a Postman request item into an <see cref="HttpRequestMessage"/>.
///
/// Each concern lives in its own block so future work is localized:
///   - auth handling   (currently bearer only)
///   - body handling   (currently raw only)
///   - URL resolution  (raw string, falling back to host/path/query parts)
/// </summary>
public static class RequestBuilder
{
    public static HttpRequestMessage Build(
        JsonObject entry, VariableResolver vars, string? token, bool collectionBearer)
    {
        var request = entry["request"]!.AsObject();
        var method = (request["method"].Str() ?? "GET").ToUpperInvariant();
        var url = ResolveUrl(request["url"], vars);

        // --- Headers ---------------------------------------------------------
        var headers = new List<(string Key, string Value)>();
        if (request["header"] is JsonArray headerArray)
        {
            foreach (var node in headerArray)
            {
                if (node is not JsonObject header)
                    continue;
                if (header["disabled"].Bool())
                    continue;

                var key = header["key"].Str();
                if (string.IsNullOrEmpty(key))
                    continue;

                headers.Add((key, vars.Substitute(header["value"].Str())));
            }
        }

        // --- Auth (bearer only for now) -------------------------------------
        // A request that already carries its own non-empty Authorization header
        // (e.g. a Basic "shared secret") wins over inherited collection auth,
        // matching Postman's behavior. Only inject the bearer token when either
        // the request explicitly opts into bearer auth, or the collection uses
        // bearer auth AND the request has no explicit Authorization header.
        var hasExplicitAuthHeader = headers.Any(h =>
            string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(h.Value));

        var auth = request["auth"] as JsonObject;
        var requestBearer = auth?["type"].Str() == "bearer";
        var applyBearer = !string.IsNullOrEmpty(token)
            && (requestBearer || (collectionBearer && !hasExplicitAuthHeader));
        if (applyBearer)
        {
            headers.RemoveAll(h => string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase));
            headers.Add(("Authorization", $"Bearer {token}"));
        }

        var message = new HttpRequestMessage(new HttpMethod(method), url);

        // --- Body (raw only for now) ----------------------------------------
        var contentType = headers
            .Where(h => string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Value)
            .FirstOrDefault();

        var body = request["body"] as JsonObject;
        if (body?["mode"].Str() == "raw" && body["raw"] is JsonValue rawValue)
        {
            var raw = vars.Substitute(rawValue.Str());
            var content = new StringContent(raw, Encoding.UTF8);
            if (!string.IsNullOrEmpty(contentType))
            {
                content.Headers.Remove("Content-Type");
                content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
            message.Content = content;
        }

        // .NET routes content headers (Content-Type, etc.) onto the body, not the
        // request line. Try the request first, then fall back to the content so
        // valid collections never throw on an otherwise-fine header.
        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                continue; // already applied to the content above

            if (!message.Headers.TryAddWithoutValidation(key, value))
                message.Content?.Headers.TryAddWithoutValidation(key, value);
        }

        return message;
    }

    private static string ResolveUrl(JsonNode? urlNode, VariableResolver vars)
    {
        if (urlNode is null)
            return string.Empty;

        // url may be a bare string in some exports.
        if (urlNode is JsonValue urlValue)
            return vars.Substitute(urlValue.Str());

        var obj = urlNode.AsObject();

        // Prefer the raw URL when present and usable.
        var raw = obj["raw"].Str();
        if (!string.IsNullOrWhiteSpace(raw))
            return vars.Substitute(raw);

        // Otherwise assemble from structured parts.
        var protocol = vars.Substitute(obj["protocol"].Str() ?? "https");
        var host = JoinPart(obj["host"], ".", vars);
        var path = JoinPart(obj["path"], "/", vars);
        var query = BuildQuery(obj["query"], vars);

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(protocol))
            sb.Append(protocol).Append("://");
        sb.Append(host);
        if (!string.IsNullOrEmpty(path))
        {
            if (!path.StartsWith('/'))
                sb.Append('/');
            sb.Append(path);
        }
        sb.Append(query);
        return sb.ToString();
    }

    private static string JoinPart(JsonNode? node, string separator, VariableResolver vars)
    {
        if (node is JsonValue value)
            return vars.Substitute(value.Str());

        if (node is JsonArray array)
            return string.Join(
                separator,
                array.Select(x => vars.Substitute(x.Str())).Where(s => !string.IsNullOrEmpty(s)));

        return string.Empty;
    }

    private static string BuildQuery(JsonNode? node, VariableResolver vars)
    {
        if (node is not JsonArray array)
            return string.Empty;

        var parts = new List<string>();
        foreach (var item in array)
        {
            if (item is not JsonObject q)
                continue;
            if (q["disabled"].Bool())
                continue;

            var key = vars.Substitute(q["key"].Str());
            if (string.IsNullOrEmpty(key))
                continue;

            var value = vars.Substitute(q["value"].Str());
            parts.Add($"{WebUtility.UrlEncode(key)}={WebUtility.UrlEncode(value)}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
