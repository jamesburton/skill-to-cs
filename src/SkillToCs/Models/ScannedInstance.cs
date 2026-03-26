namespace SkillToCs.Models;

public record ScannedInstance(
    string RuleName,
    string FilePath,
    int Line,
    Dictionary<string, object?> Parameters,
    string? DisplayLabel = null
);
