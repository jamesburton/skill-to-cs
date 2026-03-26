using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkillToCs.Engine;

public sealed class KnowledgeStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _basePath;

    public KnowledgeStore(string projectPath)
    {
        _basePath = Path.Combine(projectPath, ".skill-to-cs", "knowledge");
    }

    /// <summary>
    /// Load all pitfalls for a given rule.
    /// </summary>
    public IReadOnlyList<Pitfall> GetPitfalls(string ruleName)
    {
        var path = GetPitfallsPath(ruleName);
        return LoadList<Pitfall>(path);
    }

    /// <summary>
    /// Load all patterns for a given rule.
    /// </summary>
    public IReadOnlyList<Pattern> GetPatterns(string ruleName)
    {
        var path = GetPatternsPath(ruleName);
        return LoadList<Pattern>(path);
    }

    /// <summary>
    /// Add a pitfall for a rule and persist it.
    /// </summary>
    public void AddPitfall(string ruleName, Pitfall pitfall)
    {
        var path = GetPitfallsPath(ruleName);
        var list = LoadMutableList<Pitfall>(path);
        list.Add(pitfall);
        SaveList(path, list);
    }

    /// <summary>
    /// Add a pattern for a rule and persist it.
    /// </summary>
    public void AddPattern(string ruleName, Pattern pattern)
    {
        var path = GetPatternsPath(ruleName);
        var list = LoadMutableList<Pattern>(path);
        list.Add(pattern);
        SaveList(path, list);
    }

    /// <summary>
    /// Find pitfalls whose Context matches parameter values in the given context.
    /// Uses simple contains/starts-with matching.
    /// </summary>
    public IReadOnlyList<Pitfall> FindMatchingPitfalls(string ruleName, Dictionary<string, object?> context)
    {
        var pitfalls = GetPitfalls(ruleName);
        if (pitfalls.Count == 0 || context.Count == 0)
            return [];

        var matches = new List<Pitfall>();

        foreach (var pitfall in pitfalls)
        {
            if (MatchesContext(pitfall.Context, context))
                matches.Add(pitfall);
        }

        return matches;
    }

    /// <summary>
    /// Simple context matching: parse the pitfall context string for patterns like
    /// "paramName starts with value" or "paramName contains value", and check against
    /// the provided context dictionary.
    /// Falls back to plain substring matching against all context values.
    /// </summary>
    private static bool MatchesContext(string pitfallContext, Dictionary<string, object?> context)
    {
        // Try to parse structured patterns: "paramName starts with value"
        var startsWithMatch = TryParsePattern(pitfallContext, "starts with");
        if (startsWithMatch is not null)
        {
            var (paramName, expectedValue) = startsWithMatch.Value;
            if (context.TryGetValue(paramName, out var actualValue) && actualValue is not null)
            {
                var actual = actualValue.ToString() ?? "";
                if (actual.StartsWith(expectedValue, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Try "paramName contains value"
        var containsMatch = TryParsePattern(pitfallContext, "contains");
        if (containsMatch is not null)
        {
            var (paramName, expectedValue) = containsMatch.Value;
            if (context.TryGetValue(paramName, out var actualValue) && actualValue is not null)
            {
                var actual = actualValue.ToString() ?? "";
                if (actual.Contains(expectedValue, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Fallback: check if any context value contains keywords from the pitfall context
        foreach (var (_, value) in context)
        {
            if (value is null) continue;
            var valueStr = value.ToString() ?? "";
            if (string.IsNullOrEmpty(valueStr)) continue;

            // Check if the pitfall context mentions this value
            if (pitfallContext.Contains(valueStr, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Try to parse "paramName operator value" from a context string.
    /// </summary>
    private static (string ParamName, string Value)? TryParsePattern(string context, string operatorKeyword)
    {
        var idx = context.IndexOf(operatorKeyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var paramPart = context[..idx].Trim();
        var valuePart = context[(idx + operatorKeyword.Length)..].Trim().Trim('"', '\'');

        if (string.IsNullOrEmpty(paramPart) || string.IsNullOrEmpty(valuePart))
            return null;

        return (paramPart, valuePart);
    }

    private string GetPitfallsPath(string ruleName) =>
        Path.Combine(_basePath, $"{ruleName}.pitfalls.json");

    private string GetPatternsPath(string ruleName) =>
        Path.Combine(_basePath, $"{ruleName}.patterns.json");

    private static IReadOnlyList<T> LoadList<T>(string path)
    {
        if (!File.Exists(path))
            return [];

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<T>>(json, s_jsonOptions) ?? [];
    }

    private static List<T> LoadMutableList<T>(string path)
    {
        if (!File.Exists(path))
            return [];

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<T>>(json, s_jsonOptions) ?? [];
    }

    private static void SaveList<T>(string path, List<T> items)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(items, s_jsonOptions);
        File.WriteAllText(path, json);
    }
}

public sealed record Pitfall(
    string Id,
    string Context,
    string Learning,
    DateTimeOffset Added,
    string Source
);

public sealed record Pattern(
    string Id,
    string Context,
    string Learning,
    DateTimeOffset Added,
    string Source,
    int ConfirmationCount
);
