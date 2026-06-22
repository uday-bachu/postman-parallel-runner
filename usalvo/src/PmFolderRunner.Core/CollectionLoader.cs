using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmFolderRunner.Core;

/// <summary>
/// Reads Postman JSON: the collection itself, plus environment / globals value
/// files. Also flattens nested folders down to a flat list of request items.
///
/// This is the single place that touches Postman file shapes, which keeps the
/// "add another variable source later" change localized.
/// </summary>
public static class CollectionLoader
{
    public static JsonNode Load(string path)
    {
        using var stream = File.OpenRead(path);
        var node = JsonNode.Parse(
            stream,
            nodeOptions: null,
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

        return node ?? throw new InvalidDataException($"'{path}' parsed to a null JSON document.");
    }

    /// <summary>
    /// Recursively pulls every request out of nested Postman folders. Folders are
    /// just containers for ordering, but their names are stamped onto each request
    /// (as <c>_folderPath</c>) so the runner can disambiguate same-named requests.
    /// </summary>
    public static List<JsonObject> Flatten(JsonArray? items, string folderPath = "")
    {
        var result = new List<JsonObject>();
        if (items is null)
            return result;

        foreach (var node in items)
        {
            if (node is not JsonObject obj)
                continue;

            if (obj["item"] is JsonArray nested)
            {
                var folderName = obj["name"].Str() ?? string.Empty;
                var childPath = string.IsNullOrEmpty(folderPath)
                    ? folderName
                    : $"{folderPath}/{folderName}";
                result.AddRange(Flatten(nested, childPath));
            }
            else if (obj["request"] is not null)
            {
                // Stamp the folder path onto the item so RequestExecutor can read it.
                obj["_folderPath"] = JsonValue.Create(folderPath);
                result.Add(obj);
            }
        }

        return result;
    }

    /// <summary>
    /// Reads a Postman environment or globals export, both of which store their
    /// variables in a <c>values</c> array of {key, value, enabled} objects.
    /// </summary>
    public static Dictionary<string, string> ReadValuesFile(string path)
    {
        var dict = new Dictionary<string, string>();
        var root = Load(path);
        if (root is JsonObject obj && obj["values"] is JsonArray values)
            AddEntries(values, dict);
        return dict;
    }

    /// <summary>Reads the collection's own top-level <c>variable</c> block.</summary>
    public static Dictionary<string, string> ReadCollectionVariables(JsonNode root)
    {
        var dict = new Dictionary<string, string>();
        if (root is JsonObject obj && obj["variable"] is JsonArray variables)
            AddEntries(variables, dict);
        return dict;
    }

    private static void AddEntries(JsonArray array, Dictionary<string, string> dict)
    {
        foreach (var node in array)
        {
            if (node is not JsonObject obj)
                continue;

            // Postman files use either "disabled":true (collections) or
            // "enabled":false (environments/globals). Honour both.
            if (obj["disabled"].Bool())
                continue;
            if (obj.ContainsKey("enabled") && !obj["enabled"].Bool())
                continue;

            var key = obj["key"].Str();
            if (string.IsNullOrEmpty(key))
                continue;

            dict[key] = obj["value"].Str() ?? string.Empty;
        }
    }
}
