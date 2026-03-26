using SkillToCs.Models;

namespace SkillToCs.Assessment.Detectors;

public sealed class EditorConfigDetector : IDetector
{
    public string Name => "editorconfig";
    public int Priority => 20;

    public async Task<Detection?> DetectAsync(string rootPath, CancellationToken ct)
    {
        var editorConfigs = Directory.GetFiles(rootPath, ".editorconfig", SearchOption.AllDirectories);
        if (editorConfigs.Length == 0)
            return null;

        var ruleCount = 0;
        var hasNamingRules = false;
        var hasFormattingRules = false;

        foreach (var configFile in editorConfigs)
        {
            ct.ThrowIfCancellationRequested();
            var lines = await File.ReadAllLinesAsync(configFile, ct);

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("dotnet_", StringComparison.Ordinal) ||
                    trimmed.StartsWith("csharp_", StringComparison.Ordinal))
                {
                    ruleCount++;

                    if (trimmed.StartsWith("dotnet_naming_", StringComparison.Ordinal))
                        hasNamingRules = true;

                    if (trimmed.StartsWith("csharp_formatting_", StringComparison.Ordinal) ||
                        trimmed.StartsWith("dotnet_formatting_", StringComparison.Ordinal) ||
                        trimmed.StartsWith("csharp_new_line", StringComparison.Ordinal) ||
                        trimmed.StartsWith("csharp_indent", StringComparison.Ordinal) ||
                        trimmed.StartsWith("csharp_space", StringComparison.Ordinal))
                    {
                        hasFormattingRules = true;
                    }
                }
            }
        }

        var properties = new Dictionary<string, object>
        {
            ["ruleCount"] = ruleCount,
            ["hasNamingRules"] = hasNamingRules,
            ["hasFormattingRules"] = hasFormattingRules,
            ["fileCount"] = editorConfigs.Length
        };

        var opportunities = new List<ScriptOpportunity>
        {
            new("format-check", "Verify code formatting matches .editorconfig rules",
                "verification", editorConfigs, ScriptCapability.Check | ScriptCapability.Fix)
        };

        if (hasNamingRules)
        {
            opportunities.Add(new("naming-check",
                "Verify naming conventions match .editorconfig rules",
                "verification", editorConfigs, ScriptCapability.Check));
        }

        return new Detection(Name, "code-style", properties, opportunities);
    }
}
