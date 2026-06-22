using System.Collections;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmFolderRunner.Core;

namespace PmFolderRunner.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine();
            Options.PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            Options.PrintUsage();
            return 0;
        }

        if (string.IsNullOrEmpty(options.CollectionPath))
        {
            Console.Error.WriteLine("Error: a collection JSON path is required.");
            Console.Error.WriteLine();
            Options.PrintUsage();
            return 2;
        }

        if (!File.Exists(options.CollectionPath))
        {
            Console.Error.WriteLine($"Error: collection file not found: {options.CollectionPath}");
            return 2;
        }

        JsonNode root;
        try
        {
            root = CollectionLoader.Load(options.CollectionPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: failed to parse collection: {ex.Message}");
            return 2;
        }

        if (root is not JsonObject)
        {
            Console.Error.WriteLine("Error: collection root is not a JSON object.");
            return 2;
        }

        // --- Build the variable table in precedence order (lowest first) -----
        var variables = new Dictionary<string, string>();

        // 5 (lowest): the collection's own variable[] block.
        foreach (var kv in CollectionLoader.ReadCollectionVariables(root))
            variables[kv.Key] = kv.Value;

        // 4: globals file.
        if (!string.IsNullOrEmpty(options.GlobalsPath))
        {
            if (!File.Exists(options.GlobalsPath))
            {
                Console.Error.WriteLine($"Error: globals file not found: {options.GlobalsPath}");
                return 2;
            }
            foreach (var kv in CollectionLoader.ReadValuesFile(options.GlobalsPath))
                variables[kv.Key] = kv.Value;
        }

        // 3: environment file.
        if (!string.IsNullOrEmpty(options.EnvironmentPath))
        {
            if (!File.Exists(options.EnvironmentPath))
            {
                Console.Error.WriteLine($"Error: environment file not found: {options.EnvironmentPath}");
                return 2;
            }
            foreach (var kv in CollectionLoader.ReadValuesFile(options.EnvironmentPath))
                variables[kv.Key] = kv.Value;
        }

        // 2: PM_VAR_* and PM_BASEURL environment variables.
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            var key = e.Key?.ToString() ?? string.Empty;
            if (key.StartsWith("PM_VAR_", StringComparison.Ordinal))
                variables[key["PM_VAR_".Length..].ToLowerInvariant()] = e.Value?.ToString() ?? string.Empty;
        }

        var envBaseUrl = Environment.GetEnvironmentVariable("PM_BASEURL");
        if (!string.IsNullOrEmpty(envBaseUrl))
            variables["baseurl"] = envBaseUrl.TrimEnd('/');

        // 1 (highest): --var and --baseurl from the command line.
        foreach (var (key, value) in options.Vars)
            variables[key] = value;

        if (!string.IsNullOrEmpty(options.BaseUrl))
            variables["baseurl"] = options.BaseUrl.TrimEnd('/');

        // --- Token: --token, else PM_TOKEN, else prompt ---------------------
        var token = options.Token ?? Environment.GetEnvironmentVariable("PM_TOKEN");
        if (token is null)
        {
            Console.Write("Bearer token for bearer-auth requests (Enter to skip): ");
            token = (Console.ReadLine() ?? string.Empty).Trim();
        }

        var collectionBearer = (root["auth"] as JsonObject)?["type"].Str() == "bearer";

        // --- Collect requests ------------------------------------------------
        var requests = CollectionLoader.Flatten(root["item"] as JsonArray);
        if (requests.Count == 0)
        {
            Console.Error.WriteLine("Collection has no requests to run.");
            return 1;
        }

        // Prompt for any placeholders still unresolved across the requests.
        var resolver = new VariableResolver(variables);
        var clonedRequests = new JsonArray(requests.Select(r => (JsonNode?)r.DeepClone()).ToArray());
        foreach (var missing in resolver.FindUnresolved(clonedRequests))
        {
            Console.Write($"Value for {{{{{missing}}}}}: ");
            variables[missing] = (Console.ReadLine() ?? string.Empty).Trim();
        }

        // --- HTTP client -----------------------------------------------------
        using var handler = new HttpClientHandler
        {
            MaxConnectionsPerServer = int.MaxValue,
        };
        if (options.Insecure)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        using var client = new HttpClient(handler)
        {
            // Per-request timeouts are handled inside the executor, so disable the
            // single shared client-level timeout to avoid double-counting.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        // --- Run -------------------------------------------------------------
        var maxWorkers = BatchRunner.ResolveWorkerCount(options.Workers, requests.Count);

        // In --json mode the banner/table are suppressed so stdout stays machine-readable.
        if (!options.OutputJson)
            ConsoleReporter.PrintRunHeader(options.CollectionPath, requests.Count, maxWorkers);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Each iteration's results are kept separately (for the JSON "iteration" field)
        // and also accumulated into one flat list (for aggregation and counts).
        var perIteration = new List<List<RequestResult>>();
        var allResults = new List<RequestResult>();

        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            if (!options.OutputJson && options.Iterations > 1)
                Console.WriteLine($"\n--- Iteration {iteration + 1} of {options.Iterations} ---");

            var batch = await BatchRunner.RunAsync(
                client,
                requests,
                resolver,
                token,
                collectionBearer,
                options.Workers,
                options.Timeout);

            perIteration.Add(batch);
            allResults.AddRange(batch);
        }

        stopwatch.Stop();

        // --- Report ----------------------------------------------------------
        if (options.OutputJson)
        {
            var json = options.Iterations > 1
                ? BuildIterationsJson(options, maxWorkers, stopwatch, perIteration, allResults)
                : BuildSingleRunJson(options, maxWorkers, stopwatch, allResults);

            Console.WriteLine(JsonSerializer.Serialize(json,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            if (options.Iterations > 1)
                ConsoleReporter.PrintAggregated(allResults, options.Iterations);
            else
                ConsoleReporter.PrintResults(allResults);

            ConsoleReporter.PrintSummary(allResults, stopwatch.Elapsed.TotalSeconds);
        }

        return 0;
    }

    private static object BuildSingleRunJson(
        Options options,
        int maxWorkers,
        System.Diagnostics.Stopwatch stopwatch,
        IReadOnlyList<RequestResult> results) => new
        {
            timestamp = DateTime.UtcNow,
            collection = options.CollectionPath,
            workers = maxWorkers,
            wallMs = (long)stopwatch.Elapsed.TotalMilliseconds,
            totalRequests = results.Count,
            passed = results.Count(r => r.IsUnder400),
            failed = results.Count(r => !r.IsUnder400),
            results = results.Select(r => new
            {
                name = r.Name,
                folder = r.Folder,
                status = r.Status,
                ms = (long)r.Ms,
                passed = r.IsUnder400,
                snippet = r.Snippet,
            }),
        };

    private static object BuildIterationsJson(
        Options options,
        int maxWorkers,
        System.Diagnostics.Stopwatch stopwatch,
        IReadOnlyList<List<RequestResult>> perIteration,
        IReadOnlyList<RequestResult> allResults)
    {
        var results = new List<object>();
        for (var i = 0; i < perIteration.Count; i++)
        {
            foreach (var r in perIteration[i])
            {
                results.Add(new
                {
                    iteration = i + 1,
                    name = r.Name,
                    folder = r.Folder,
                    status = r.Status,
                    ms = (long)r.Ms,
                    passed = r.IsUnder400,
                    snippet = r.Snippet,
                });
            }
        }

        var aggregated = allResults
            .GroupBy(r => r.QualifiedName)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                qualifiedName = g.Key,
                totalRuns = g.Count(),
                passed = g.Count(r => r.IsUnder400),
                failed = g.Count(r => !r.IsUnder400),
                avgMs = (long)Math.Round(g.Average(r => r.Ms)),
            });

        return new
        {
            timestamp = DateTime.UtcNow,
            collection = options.CollectionPath,
            workers = maxWorkers,
            wallMs = (long)stopwatch.Elapsed.TotalMilliseconds,
            iterations = options.Iterations,
            totalRequests = allResults.Count,
            passed = allResults.Count(r => r.IsUnder400),
            failed = allResults.Count(r => !r.IsUnder400),
            results,
            aggregated,
        };
    }
}
