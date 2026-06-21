# pmrun

`pmrun` is a small, dependency-free .NET CLI for running a [Postman](https://www.postman.com/)
collection's requests in parallel and printing a compact summary.

It is intentionally lightweight. The tool sends each request as defined in the collection, applies
variable substitution, and reports status codes, timings, and short response snippets. It does not
execute Postman pre-request scripts or test scripts.

## When it is useful

`pmrun` is a good fit when you want to:

- smoke-test a collection of API endpoints quickly
- hit a set of health, status, or lookup endpoints in parallel
- replay a small collection against different environments
- collect a simple pass/fail summary without pulling in Newman or extra packages

## Execution model

`pmrun` recursively pulls every request from the collection, including nested folders, and runs
them in one batch. Folder structure is preserved only as display context; it does not control
execution order.

If you pass `--workers 0`, all requests are fired at once. If you pass a positive number, that
value is used as the concurrency cap.

## Requirements

- [.NET SDK 8.0](https://dotnet.microsoft.com/download) or newer

No NuGet restore is needed for this project.

## Build and run

```bash
dotnet build -c Release
dotnet run --project src/PmFolderRunner.Cli -- <collection.json> [options]
```

To publish a standalone output folder:

```bash
dotnet publish src/PmFolderRunner.Cli -c Release -o ./publish
./publish/pmrun <collection.json> [options]
```

## Quick start

Run the included sample collection as-is:

```bash
dotnet run --project src/PmFolderRunner.Cli -- \
  Examples/sample.postman_collection.json
```

```powershell
dotnet run --project src/PmFolderRunner.Cli -- `
  Examples/sample.postman_collection.json
```

## Usage

```text
pmrun <collection.json> [options]

  --baseurl <url>        Value for {{baseurl}} (or env PM_BASEURL).
  --token <token>        Bearer token (or env PM_TOKEN). Prompted if omitted.
  --var NAME=VALUE       Extra variable; repeatable (or env PM_VAR_<NAME>).
  -e, --environment <f>  Postman environment JSON file (reads its values[] array).
  -g, --globals <f>      Postman globals JSON file (reads its values[] array).
  --workers <n>          Max concurrent requests. 0 = all at once (default).
  --timeout <seconds>    Per-request timeout. Default 30.
  --insecure             Skip TLS certificate verification.
  --json                 Output results as JSON to stdout (suppresses console table).
  -n, --iterations <n>   Run the collection N times and report aggregated results.
                         Default 1.
  -h, --help             Show help.
```

## Real-world examples

Run a collection against a staging API:

```bash
pmrun MyApi.postman_collection.json \
  --baseurl https://staging.example.com \
  --workers 8 \
  --timeout 20
```

Run a bearer-auth collection and emit machine-readable JSON:

```bash
pmrun ProtectedApi.postman_collection.json \
  --baseurl https://api.example.com \
  --token "eyJ..." \
  --json
```

Use a Postman environment export plus one override:

```bash
pmrun OrderApi.postman_collection.json \
  --environment Environments/staging.postman_environment.json \
  --baseurl https://staging.example.com \
  --var customerId=12345
```

## Variables

Variables fill `{{placeholder}}` tokens. When the same key appears in more than one source, the
highest-precedence value wins.

| Precedence | Source |
|-----------:|--------|
| 1 | `--var NAME=VALUE` and `--baseurl` |
| 2 | `PM_VAR_<NAME>` and `PM_BASEURL` |
| 3 | Postman environment file |
| 4 | Postman globals file |
| 5 | Collection `variable[]` block |

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

Use the final `https://` URL directly when possible. If the target uses a self-signed certificate,
you can also add `--insecure`.

```powershell
dotnet run --project src/PmFolderRunner.Cli -- `
  ProtectedApi.postman_collection.json `
  --baseurl https://your-host.example.com `
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

## Current limitations

- Only bearer auth is handled automatically
- Only raw body mode is supported
- Postman pre-request and test scripts are not executed
- The tool reports request outcomes, not assertion results

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
