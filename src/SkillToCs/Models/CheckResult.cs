namespace SkillToCs.Models;

public record CheckResult(
    IReadOnlyList<VerificationResult> Results,
    CheckStatus OverallStatus,
    TimeSpan TotalDuration
)
{
    public int TotalPassed => Results.Count(r => r.Status == VerificationStatus.Pass);
    public int TotalFailed => Results.Count(r => r.Status == VerificationStatus.Fail);
    public int TotalErrors => Results.Count(r => r.Status == VerificationStatus.Error);
}

public enum CheckStatus { AllPassed, SomeFailed, Error }
