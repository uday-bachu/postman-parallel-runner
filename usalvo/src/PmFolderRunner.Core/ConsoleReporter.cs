namespace PmFolderRunner.Core;

/// <summary>
/// Renders the run to the console in the same shape as the original script:
/// a banner, a sorted STATUS/TIME/REQUEST table, and a one-line summary.
/// </summary>
public static class ConsoleReporter
{
    private const int Width = 90;

    public static void PrintRunHeader(string collectionPath, int requestCount, int maxWorkers)
    {
        var bar = new string('#', Width);
        Console.WriteLine();
        Console.WriteLine(bar);
        Console.WriteLine($"RUN START | Collection: {collectionPath}");
        Console.WriteLine($"Requests: {requestCount} | Parallel workers: {maxWorkers}");
        Console.WriteLine(bar);
    }

    public static void PrintResults(IReadOnlyList<RequestResult> results)
    {
        var ordered = results.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();

        Console.WriteLine();
        Console.WriteLine($"{"",-6} {"STATUS",-7} {"TIME(ms)",9}  {"REQUEST",-45}");
        Console.WriteLine(new string('-', Width));

        foreach (var r in ordered)
        {
            // A leading PASS/FAIL label makes the table scannable without ANSI colour.
            var label = r.IsUnder400 ? "PASS" : "FAIL";
            Console.WriteLine($"{label,-6} {r.Status,-7} {r.Ms,9:0}  {Truncate(r.Name, 45),-45}");

            if (!r.IsSuccess)
            {
                // Show folder context so ambiguous (same-named) requests are clear.
                if (!string.IsNullOrEmpty(r.Folder))
                    Console.WriteLine($"        [{r.Folder}]");

                var snippet = (r.Snippet ?? string.Empty).Trim();
                if (snippet.Length > 0)
                    Console.WriteLine($"        {Truncate(snippet, 120)}");
            }
        }

        Console.WriteLine(new string('-', Width));
    }

    /// <summary>
    /// Aggregates results across multiple iterations, one row per request
    /// (keyed by qualified name): total runs, passes, failures, and average time.
    /// </summary>
    public static void PrintAggregated(IReadOnlyList<RequestResult> allResults, int iterations)
    {
        var groups = allResults
            .GroupBy(r => r.QualifiedName)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        Console.WriteLine();
        Console.WriteLine($"Aggregated over {iterations} iteration(s):");
        Console.WriteLine($"{"REQUEST",-55} {"RUNS",5} {"PASS",5} {"FAIL",5} {"AVG(ms)",9}");
        Console.WriteLine(new string('-', Width));

        foreach (var g in groups)
        {
            var total  = g.Count();
            var passed = g.Count(r => r.IsUnder400);
            var failed = total - passed;
            var avgMs  = g.Average(r => r.Ms);

            var allPassed = failed == 0;
            var label     = allPassed ? "    " : "FAIL";

            Console.WriteLine(
                $"{label} {Truncate(g.Key, 50),-50} {total,5} {passed,5} {failed,5} {avgMs,9:0}");
        }

        Console.WriteLine(new string('-', Width));
    }

    public static void PrintSummary(IReadOnlyList<RequestResult> results, double wallSeconds)
    {
        var under400 = results.Count(r => r.IsUnder400);
        var failures = results.Count - under400;
        var bar = new string('#', Width);

        Console.WriteLine();
        Console.WriteLine(bar);
        Console.WriteLine(
            $"RUN COMPLETE | Requests: {results.Count} | " +
            $"Under-400: {under400} | Failures/ERR: {failures} | Total wall: {wallSeconds:0.00}s");
        Console.WriteLine(bar);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
