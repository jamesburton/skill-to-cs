using SkillToCs.Models;

namespace SkillToCs.Engine;

public interface IRule
{
    string Name { get; }
    string Description { get; }
    string Category { get; }
    RuleSubtype Subtype { get; }
    HeuristicPolicy HeuristicPolicy { get; }
    RuleSchema Describe();
    Task<IReadOnlyList<ScannedInstance>> ScanAsync(ProjectContext ctx, CancellationToken ct);
    Task<GenerationResult> GenerateAsync(RuleParams parameters, ProjectContext ctx, CancellationToken ct);
    Task<VerificationResult> VerifyAsync(ProjectContext ctx, CancellationToken ct);
    bool AppliesTo(ProjectContext ctx);
}

public enum RuleSubtype
{
    Generation,
    Verification
}
