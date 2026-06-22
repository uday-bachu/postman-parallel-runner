using System.Text.Json.Nodes;

namespace PmFolderRunner.Core;

/// <summary>
/// Runs every request in the collection in parallel. <paramref name="workers"/>
/// caps concurrency; 0 means fire them all at once.
///
/// Reporting is intentionally kept out of here so a different output format
/// (JSON, JUnit, ...) can consume the same results later.
/// </summary>
public static class BatchRunner
{
    public static int ResolveWorkerCount(int requested, int requestCount) =>
        requested > 0 ? requested : Math.Max(1, requestCount);

    public static async Task<List<RequestResult>> RunAsync(
        HttpClient client,
        IReadOnlyList<JsonObject> requests,
        VariableResolver vars,
        string? token,
        bool collectionBearer,
        int workers,
        double timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
            return new List<RequestResult>();

        var maxWorkers = ResolveWorkerCount(workers, requests.Count);
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
