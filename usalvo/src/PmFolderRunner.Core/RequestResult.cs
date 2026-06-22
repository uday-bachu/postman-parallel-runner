namespace PmFolderRunner.Core;

/// <summary>
/// Outcome of a single request. <see cref="Status"/> is an int HTTP status code,
/// or the string "ERR" when the request never produced a response.
/// </summary>
public sealed record RequestResult(
    string Name,
    string Folder,       // slash-joined folder path, e.g. "voidclaim" (empty for top-level)
    object Status,
    double Ms,
    string Snippet)
{
    public bool IsSuccess => Status is int code && code < 300;

    public bool IsUnder400 => Status is int code && code < 400;

    /// <summary>Qualified name for display when folder context is needed.</summary>
    public string QualifiedName => string.IsNullOrEmpty(Folder)
        ? Name
        : $"{Folder}/{Name}";
}
