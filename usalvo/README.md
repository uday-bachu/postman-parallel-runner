# usalvo

`usalvo` is a small, dependency-free .NET CLI for running a [Postman](https://www.postman.com/)
collection's requests in parallel and printing a compact summary.

It is intentionally lightweight. The tool sends each request as defined in the collection, applies
variable substitution, and reports status codes, timings, and short response snippets. It does not
execute Postman pre-request or test scripts, but it can read a single expected HTTP status code out
of a request's test script and mark that request pass or fail against the actual response.

## When it is useful

`usalvo` exists for one thing: firing a whole Postman collection concurrently instead of one
request at a time. Reach for it when:

- you want a suite of independent requests to finish in roughly the time of the slowest single
  request, not the sum of them all � Newman runs sequentially, `usalvo` fans them out across workers
- you need quick concurrent load on your endpoints: combine `--workers` and `--iterations` to replay
  the collection many times at a controlled concurrency cap
- you are hunting concurrency-sensitive failures � rate limiting, connection-pool exhaustion, race
  conditions � that never surface when requests run strictly in series
- you want a single parallel pass/fail sweep over many health, status, or lookup endpoints in CI,
  with no Newman or extra packages to install

## Execution model

`usalvo` recursively pulls every request from the collection, including nested folders, and runs
them in one batch. Folder structure is preserved only as display context; it does not control
execution order.

By default, up to 50 requests run concurrently. Pass `--workers <n>` to set a different cap; the
value must be 1 or greater. To fire every request at once with no cap, use `--burst` — handy for a
deliberate load spike, though large collections may exhaust ephemeral ports. `--workers` and
`--burst` cannot be combined.

## Pass/fail and exit codes

If a request's test script contains an HTTP status code — the first three-digit `1xx`–`5xx` number
found anywhere in the script text — `usalvo` treats it as the expected status and compares it
against the actual response:

- match → the request is marked `PASS`
- mismatch → the request is marked `FAIL`
- no status code in the script (or no test script at all) → no verdict, shown as `-`

A network error (`ERR`) always counts as a failure, whether or not a status code was declared.

The process exit code reflects the run: `usalvo` exits `1` if any request is `FAIL` or `ERR`, and
`0` only when every request either passed or had no assertion. This makes it usable as a CI gate.
With `--json`, each result carries a `passed` field that is `true`, `false`, or `null` (no verdict).

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or newer (needed to install the tool; the
  tool itself runs on the .NET 10 runtime)

## Install

`usalvo` is distributed as a .NET global tool. The `dotnet` commands below are identical across
bash, PowerShell, and cmd.

bash:

```bash
dotnet tool install -g usalvo
```

PowerShell:

```powershell
dotnet tool install -g usalvo
```

Update or remove it later with:

bash:

```bash
dotnet tool update -g usalvo
dotnet tool uninstall -g usalvo
```

PowerShell:

```powershell
dotnet tool update -g usalvo
dotnet tool uninstall -g usalvo
```

This puts a `usalvo` command on your PATH, usable from bash, PowerShell, and cmd.

## Quick start

Run a collection as-is:

bash:

```bash
usalvo ./sample.postman_collection.json
```

PowerShell:

```powershell
usalvo .\sample.postman_collection.json
```

## Usage

```text
usalvo <collection.json> [options]

  --token <token>        Bearer token (or env PM_TOKEN). Prompted if omitted.
  --var NAME=VALUE        Set a variable; repeatable (or env PM_VAR_<NAME>).
  -e, --environment <f>   Postman environment JSON file (reads its values[] array).
  -g, --globals <f>       Postman globals JSON file (reads its values[] array).
  --workers <n>           Max concurrent requests. Must be 1 or greater. Default 50.
  --burst                 Fire all requests at once, ignoring --workers.
                          Large collections may exhaust ephemeral ports.
  --timeout <seconds>     Per-request timeout. Default 30.
  --insecure              Skip TLS certificate verification.
  --json                  Output results as JSON to stdout (suppresses console table).
  -n, --iterations <n>    Run the collection N times and report aggregated results.
                          Default 1.
  -h, --help              Show help.
```

## Examples

Override a single collection variable:

bash:

```bash
usalvo ./sample.postman_collection.json \
  --var name=team
```

PowerShell:

```powershell
usalvo .\sample.postman_collection.json `
  --var name=team
```

Run a bearer-auth collection and emit machine-readable JSON:

bash:

```bash
usalvo ./authcheck.postman_collection.json \
  --token "eyJ..." \
  --json
```

PowerShell:

```powershell
usalvo .\authcheck.postman_collection.json `
  --token "eyJ..." `
  --json
```

Use a Postman environment export plus an override, and tune concurrency:

bash:

```bash
usalvo ./OrderApi.postman_collection.json \
  --environment ./staging.postman_environment.json \
  --var environmentName=staging \
  --workers 8 \
  --timeout 20
```

PowerShell:

```powershell
usalvo .\OrderApi.postman_collection.json `
  --environment .\staging.postman_environment.json `
  --var environmentName=staging `
  --workers 8 `
  --timeout 20
```

Fire every request at once for a deliberate load spike:

bash:

```bash
usalvo ./sample.postman_collection.json \
  --burst
```

PowerShell:

```powershell
usalvo .\sample.postman_collection.json `
  --burst
```

## Variables

Variables fill `{{placeholder}}` tokens in URLs, headers, and raw bodies. Every value the tool
needs comes through the same generic mechanism � there are no special-cased variable names. You can
supply a value from any of the sources below, and when the same key appears in more than one source,
the highest-precedence value wins.

| Precedence | Source |
|-----------:|--------|
| 1 (highest) | `--var NAME=VALUE` on the command line |
| 2 | `PM_VAR_<NAME>` environment variable |
| 3 | Postman environment file (`--environment`) |
| 4 | Postman globals file (`--globals`) |
| 5 (lowest) | Collection `variable[]` block |

For example, a `{{host}}` placeholder in your collection can be filled with
`--var host=https://staging.example.com`, with `PM_VAR_HOST=...` in the environment, or from a
Postman environment file � whichever has higher precedence wins.

Environment and globals files are read from the standard Postman export format: a `values` array
containing `{ "key": ..., "value": ..., "enabled": ... }` entries. Disabled values are skipped.

Any placeholder still unresolved after merging all sources is prompted for before the run starts.

## Sample collections

The repository includes two example collections under `Examples/`:

- `sample.postman_collection.json`: generic public API requests with concrete `httpbin` URLs and a JSON POST body
- `authcheck.postman_collection.json`: a simple auth-focused example showing bearer token injection against `httpbin`

## Troubleshooting

### 401 after an HTTP to HTTPS redirect

If a request starts on `http://` and the server redirects to `https://`, .NET may drop the
`Authorization` header when following the redirect across scheme or host boundaries. The redirected
request then arrives without credentials and may return `401`.

Point your collection (or your variable values) at the final `https://` URL directly when possible.
If the target uses a self-signed certificate, you can also add `--insecure`.

bash:

```bash
usalvo ./ProtectedApi.postman_collection.json \
  --token "<bearer-token>" \
  --insecure
```

PowerShell:

```powershell
usalvo .\ProtectedApi.postman_collection.json `
  --token "<bearer-token>" `
  --insecure
```

## Supported today

- Recursive flattening of nested Postman folders
- Variable substitution in URLs, headers, and raw bodies
- Bearer auth injection from `--token` or `PM_TOKEN`
- Raw request bodies
- URLs defined either as raw strings or structured Postman URL parts
- Per-request timeout handling
- Optional TLS verification bypass
- Redaction of bearer tokens in printed response snippets
- Expected-status extraction from test scripts, with per-request PASS/FAIL and a matching exit code
- Bounded concurrency (default 50 workers) with an opt-in `--burst` mode for firing all at once

## Current limitations

- Only bearer auth is handled automatically
- Only raw body mode is supported
- Postman pre-request and test scripts are not executed; only a single expected HTTP status code is
  read out of each test script
- Assertions beyond status code (response body, headers, timing) are not evaluated

## Build from source

If you are working on the tool itself rather than just using it:

bash:

```bash
dotnet build -c Release
dotnet run --project src/PmFolderRunner.Cli -- ./Examples/sample.postman_collection.json
```

PowerShell:

```powershell
dotnet build -c Release
dotnet run --project src\PmFolderRunner.Cli -- .\Examples\sample.postman_collection.json
```

To pack and install your local build as the global tool:

bash:

```bash
dotnet pack src/PmFolderRunner.Cli -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg usalvo
```

PowerShell:

```powershell
dotnet pack src\PmFolderRunner.Cli -c Release -o .\nupkg
dotnet tool install -g --add-source .\nupkg usalvo
```

## Project layout

```text
PmFolderRunner.sln
src/
  PmFolderRunner.Core/   parsing, variable resolution, request execution
  PmFolderRunner.Cli/    CLI parsing and application entry point
Examples/
  sample.postman_collection.json
  authcheck.postman_collection.json
```