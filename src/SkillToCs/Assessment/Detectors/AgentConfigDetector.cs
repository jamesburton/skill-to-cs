using SkillToCs.Models;

namespace SkillToCs.Assessment.Detectors;

public sealed class AgentConfigDetector : IDetector
{
    public string Name => "agent-config";
    public int Priority => 60;

    public Task<Detection?> DetectAsync(string rootPath, CancellationToken ct)
    {
        var hasClaudeMd = File.Exists(Path.Combine(rootPath, "CLAUDE.md"));
        var hasAgentsMd = File.Exists(Path.Combine(rootPath, "AGENTS.md"));
        var hasCursorRules = Directory.Exists(Path.Combine(rootPath, ".cursor", "rules"));
        var hasSkills = Directory.Exists(Path.Combine(rootPath, ".claude", "skills"));

        if (!hasClaudeMd && !hasAgentsMd && !hasCursorRules && !hasSkills)
            return Task.FromResult<Detection?>(null);

        var sourceFiles = new List<string>();
        if (hasClaudeMd) sourceFiles.Add(Path.Combine(rootPath, "CLAUDE.md"));
        if (hasAgentsMd) sourceFiles.Add(Path.Combine(rootPath, "AGENTS.md"));
        if (hasCursorRules) sourceFiles.Add(Path.Combine(rootPath, ".cursor", "rules"));
        if (hasSkills) sourceFiles.Add(Path.Combine(rootPath, ".claude", "skills"));

        var properties = new Dictionary<string, object>
        {
            ["hasClaudeMd"] = hasClaudeMd,
            ["hasAgentsMd"] = hasAgentsMd,
            ["hasCursorRules"] = hasCursorRules,
            ["hasSkills"] = hasSkills
        };

        var opportunities = new List<ScriptOpportunity>
        {
            new("agent-catalog", "Catalog agent configuration files and skills",
                "agent", sourceFiles.ToArray(), ScriptCapability.Scan)
        };

        Detection result = new(Name, "agent", properties, opportunities);
        return Task.FromResult<Detection?>(result);
    }
}
