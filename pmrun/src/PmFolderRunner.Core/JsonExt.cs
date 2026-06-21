using System.Text.Json.Nodes;

namespace PmFolderRunner.Core;

/// <summary>
/// Small, null-tolerant helpers for pulling primitive values out of the loosely
/// typed Postman JSON tree without throwing on missing or unexpected node types.
/// </summary>
public static class JsonExt
{
    /// <summary>Returns the string value of a node, or null if it is missing or not a string.</summary>
    public static string? Str(this JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var s))
            return s;
        return null;
    }

    /// <summary>Returns the boolean value of a node, or false if it is missing or not a bool.</summary>
    public static bool Bool(this JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<bool>(out var b))
            return b;
        return false;
    }
}
