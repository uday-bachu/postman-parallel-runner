using System.Globalization;

namespace PmFolderRunner.Cli;

/// <summary>
/// Hand-rolled argument parsing. Kept dependency-free on purpose so the tool
/// builds with just the .NET SDK and never needs a NuGet restore.
/// </summary>
internal sealed class Options
{
    public string? CollectionPath { get; private set; }
    public string? BaseUrl { get; private set; }
    public string? Token { get; private set; }
    public List<(string Key, string Value)> Vars { get; } = new();
    public int Workers { get; private set; }
    public double Timeout { get; private set; } = 30;
    public bool Insecure { get; private set; }
    public string? EnvironmentPath { get; private set; }
    public string? GlobalsPath { get; private set; }
    public bool OutputJson { get; private set; }
    public int Iterations { get; private set; } = 1;
    public bool ShowHelp { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // Be tolerant when users copy multi-line examples across shells.
            // Some shells pass continuation markers as standalone args.
            if (arg.Length > 0 && arg.All(c => c is '\\' or '`' or '^'))
                continue;

            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;

                case "--baseurl":
                    options.BaseUrl = RequireValue(args, ref i, arg);
                    break;

                case "--token":
                    options.Token = RequireValue(args, ref i, arg);
                    break;

                case "--var":
                    var pair = RequireValue(args, ref i, arg);
                    var eq = pair.IndexOf('=');
                    if (eq < 0)
                        throw new ArgumentException($"--var expects NAME=VALUE, got: {pair}");
                    options.Vars.Add((pair[..eq].Trim(), pair[(eq + 1)..]));
                    break;

                case "--workers":
                    var workersRaw = RequireValue(args, ref i, arg);
                    if (!int.TryParse(workersRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var workers))
                        throw new ArgumentException($"--workers expects an integer, got: {workersRaw}");
                    options.Workers = workers;
                    break;

                case "--timeout":
                    var timeoutRaw = RequireValue(args, ref i, arg);
                    if (!double.TryParse(timeoutRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var timeout))
                        throw new ArgumentException($"--timeout expects a number, got: {timeoutRaw}");
                    options.Timeout = timeout;
                    break;

                case "--insecure":
                    options.Insecure = true;
                    break;

                case "--json":
                    options.OutputJson = true;
                    break;

                case "-n":
                case "--iterations":
                    var iterationsRaw = RequireValue(args, ref i, arg);
                    if (!int.TryParse(iterationsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations))
                        throw new ArgumentException($"--iterations expects an integer, got: {iterationsRaw}");
                    if (iterations < 1)
                        throw new ArgumentException("--iterations must be 1 or greater");
                    options.Iterations = iterations;
                    break;

                case "-e":
                case "--environment":
                    options.EnvironmentPath = RequireValue(args, ref i, arg);
                    break;

                case "-g":
                case "--globals":
                    options.GlobalsPath = RequireValue(args, ref i, arg);
                    break;

                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"unknown option: {arg}");
                    if (options.CollectionPath is not null)
                        throw new ArgumentException($"unexpected extra argument: {arg}");
                    options.CollectionPath = arg;
                    break;
            }
        }

        return options;
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value.");
        return args[++i];
    }

    public static void PrintUsage()
    {
        var lines = new[]
        {
            "pmrun - run a Postman collection's requests in parallel.",
            "",
            "USAGE",
            "  pmrun <collection.json> [options]",
            "",
            "OPTIONS",
            "  --baseurl <url>        Value for {{baseurl}} (or env PM_BASEURL).",
            "  --token <token>        Bearer token (or env PM_TOKEN). Prompted if omitted.",
            "  --var NAME=VALUE       Extra variable; repeatable (or env PM_VAR_<NAME>).",
            "  -e, --environment <f>  Postman environment JSON file (reads its values[] array).",
            "  -g, --globals <f>      Postman globals JSON file (reads its values[] array).",
            "  --workers <n>          Max concurrent requests. 0 = all at once (default).",
            "  --timeout <seconds>    Per-request timeout. Default 30.",
            "  --insecure             Skip TLS certificate verification.",
            "  --json                 Output results as JSON to stdout (suppresses console table).",
            "  -n, --iterations <n>   Run the collection N times and report aggregated results. Default 1.",
            "  -h, --help             Show this help.",
            "",
            "VARIABLE PRECEDENCE (highest wins)",
            "  --var / --baseurl  >  PM_* env  >  --environment  >  --globals  >  collection variable[]",
        };

        Console.WriteLine(string.Join(Environment.NewLine, lines));
    }
}
