namespace SkillToCs.Models;

public record Detection(
    string DetectorName,
    string Category,
    Dictionary<string, object> Properties,
    IReadOnlyList<ScriptOpportunity> Opportunities
);

public record ScriptOpportunity(
    string Name,
    string Description,
    string Category,
    string[] SourceFiles,
    ScriptCapability Capabilities
);

[Flags]
public enum ScriptCapability
{
    None = 0,
    Check = 1,
    Fix = 2,
    Generate = 4,
    Scan = 8
}
