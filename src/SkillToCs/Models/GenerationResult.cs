using SkillToCs.Engine;

namespace SkillToCs.Models;

public record GenerationResult
{
    public bool IsSuccess { get; init; }
    public bool NeedsInput { get; init; }
    public bool IsError { get; init; }
    public IReadOnlyList<CodeFragment> Fragments { get; init; } = [];
    public IReadOnlyList<InputQuestion> Questions { get; init; } = [];
    public IReadOnlyList<InferenceNote> Inferences { get; init; } = [];
    public string? ErrorMessage { get; init; }

    public static GenerationResult Success(
        IReadOnlyList<CodeFragment> fragments,
        IReadOnlyList<InferenceNote>? inferences = null) =>
        new()
        {
            IsSuccess = true,
            Fragments = fragments,
            Inferences = inferences ?? []
        };

    public static GenerationResult NeedInput(
        IReadOnlyList<InputQuestion> questions,
        IReadOnlyList<CodeFragment>? completedFragments = null) =>
        new()
        {
            NeedsInput = true,
            Questions = questions,
            Fragments = completedFragments ?? []
        };

    public static GenerationResult Error(string message) =>
        new()
        {
            IsError = true,
            ErrorMessage = message
        };
}

public record InputQuestion(
    string ParameterName,
    string Question,
    string? Context,
    IReadOnlyList<SuggestedOption>? Options
);

public record SuggestedOption(string Label, string Value);

public record InferenceNote(
    string Decision,
    double Confidence,
    string Rationale
);
