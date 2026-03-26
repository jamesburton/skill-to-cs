namespace SkillToCs.Models;

public record ProjectAssessment(
    string RootPath,
    DateTimeOffset AssessedAt,
    IReadOnlyList<Detection> Detections,
    IReadOnlyList<ScriptOpportunity> AllOpportunities,
    IReadOnlyList<string> ApplicableRules
);
