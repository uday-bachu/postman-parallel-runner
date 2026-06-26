namespace PmFolderRunner.Core;

/// <summary>
/// Outcome of a single request. <see cref="Status"/> is an int HTTP status code,
/// or the string "ERR" when the request never produced a response.
/// <see cref="ExpectedStatus"/> is the status code parsed from the request's test
/// script, or null when the script declared none.
/// </summary>
public sealed record RequestResult(
    string Name,
    string Folder,       // slash-joined folder path, e.g. "voidclaim" (empty for top-level)
    object Status,
    double Ms,
    string Snippet,
    int? ExpectedStatus)
{
    public bool IsNetworkError => Status is string;

    /// <summary>
    /// Pass/fail verdict: null when no expected status was declared, true when the
    /// actual status matched it, false otherwise (including network errors).
    /// </summary>
    public bool? Passed =>
        ExpectedStatus is null
            ? null
            : !IsNetworkError && Status is int code && code == ExpectedStatus.Value;

    /// <summary>Qualified name for display when folder context is needed.</summary>
    public string QualifiedName => string.IsNullOrEmpty(Folder)
        ? Name
        : $"{Folder}/{Name}";
}

