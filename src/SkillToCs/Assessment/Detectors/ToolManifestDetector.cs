using System.Text.Json;
using SkillToCs.Models;

namespace SkillToCs.Assessment.Detectors;

public sealed class ToolManifestDetector : IDetector
{
    public string Name => "tool-manifest";
    public int Priority => 50;

    public async Task<Detection?> DetectAsync(string rootPath, CancellationToken ct)
    {
        var manifestPath = Path.Combine(rootPath, ".config", "dotnet-tools.json");
        if (!File.Exists(manifestPath))
            return null;

        var toolNames = new List<string>();

        try
        {
            var content = await File.ReadAllTextAsync(manifestPath, ct);
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("tools", out var tools))
            {
                foreach (var tool in tools.EnumerateObject())
                {
                    toolNames.Add(tool.Name);
                }
            }
        }
        catch
        {
            // Malformed manifest; report what we can
        }

        var properties = new Dictionary<string, object>
        {
            ["toolCount"] = toolNames.Count,
            ["tools"] = toolNames
        };

        var opportunities = new List<ScriptOpportunity>
        {
            new("tools-check", "Verify all .NET local tools are restored and functional",
                "tooling", [manifestPath], ScriptCapability.Check)
        };

        return new Detection(Name, "tooling", properties, opportunities);
    }
}
