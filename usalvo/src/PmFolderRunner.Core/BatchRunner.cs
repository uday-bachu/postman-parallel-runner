using System.Text.Json.Nodes;

namespace PmFolderRunner.Core;

/// <summary>
/// Runs every request in the collection in parallel. <paramref name="maxWorkers"/>
/// is the already-resolved concurrency cap (see <see cref="ResolveWorkerCount"/>).
///
/// Reporting is intentionally kept out of here so a different output format
/// (JSON, JUnit, ...) can consume the same results later.
/// </summary>
public static class BatchRunner
{
    /// <summary>
    /// Resolves the concurrency cap. With <paramref name="burst"/> set, every request
    /// fires at once (cap = <paramref name="requestCount"/>); otherwise the requested
    /// worker count is used (always ≥ 1, enforced by Options).
    /// </summary>
    public static int ResolveWorkerCount(int requested, int requestCount, bool burst) =>
        burst ? requestCount : requested;

    public static async Task<List<RequestResult>> RunAsync(
        HttpClient client,
        IReadOnlyList<JsonObject> requests,
        VariableResolver vars,
        string? token,
        bool collectionBearer,
        int maxWorkers,
        double timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
            return new List<RequestResult>();

        using var gate = new SemaphoreSlim(maxWorkers);

        var tasks = requests.Select(async entry =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await RequestExecutor.FireAsync(
                    client, entry, vars, token, collectionBearer, timeoutSeconds, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }
}

