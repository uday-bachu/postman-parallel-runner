using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PmFolderRunner.Core;

/// <summary>
/// Resolves Postman <c>{{placeholder}}</c> tokens against a flat dictionary of
/// variables. Unknown placeholders are left untouched, exactly like the original
/// Python script.
/// </summary>
public sealed class VariableResolver
{
    private static readonly Regex Placeholder = new(@"\{\{(.*?)\}\}", RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, string> _variables;

    public VariableResolver(IReadOnlyDictionary<string, string> variables) => _variables = variables;

    public IReadOnlyDictionary<string, string> Variables => _variables;

    /// <summary>Replaces every known <c>{{name}}</c> with its value; leaves unknown ones as-is.</summary>
    public string Substitute(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return Placeholder.Replace(
            text,
            m => _variables.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
    }

    /// <summary>
    /// Returns the sorted set of placeholder names that appear anywhere in the given
    /// JSON node but have no value in the dictionary. Used to prompt the user.
    /// </summary>
    public IReadOnlyList<string> FindUnresolved(JsonNode? items)
    {
        if (items is null)
            return Array.Empty<string>();

        var used = new HashSet<string>();
        CollectPlaceholders(items, used);

        return used
            .Where(name => !_variables.ContainsKey(name)
                        || string.IsNullOrWhiteSpace(_variables[name]))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static void CollectPlaceholders(JsonNode? node, HashSet<string> used)
    {
        switch (node)
        {
            case null:
                return;

            case JsonObject obj:
                if (obj["disabled"].Bool())
                    return;
                if (obj.ContainsKey("enabled") && !obj["enabled"].Bool())
                    return;

                foreach (var kv in obj)
                    CollectPlaceholders(kv.Value, used);
                return;

            case JsonArray array:
                foreach (var item in array)
                    CollectPlaceholders(item, used);
                return;

            case JsonValue value when value.TryGetValue<string>(out var text):
                foreach (Match m in Placeholder.Matches(text))
                    used.Add(m.Groups[1].Value);
                return;
        }
    }
}
