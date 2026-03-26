namespace SkillToCs.Models;

public record VerificationResult(
    string RuleName,
    VerificationStatus Status,
    IReadOnlyList<Violation> Violations,
    IReadOnlyList<InferenceNote> Inferences,
    VerificationStats Stats
);

public enum VerificationStatus { Pass, Fail, Error }

public record Violation(
    string FilePath,
    int? Line,
    string RuleId,
    string Message,
    ViolationSeverity Severity,
    bool Fixable
);

public enum ViolationSeverity { Error, Warning, Info }

public record VerificationStats(
    int FilesChecked,
    int Passed,
    int Failed,
    TimeSpan Duration
);
