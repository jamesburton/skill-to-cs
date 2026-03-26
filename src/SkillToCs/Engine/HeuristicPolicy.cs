namespace SkillToCs.Engine;

public enum HeuristicAction
{
    Act,
    Suggest,
    Ask
}

public sealed record HeuristicPolicy(
    double ActThreshold = 0.95,
    double SuggestThreshold = 0.80,
    bool AllowOverrideInConfig = true)
{
    public static HeuristicPolicy Default { get; } = new();

    public HeuristicAction Evaluate(double confidence) => confidence switch
    {
        _ when confidence >= ActThreshold => HeuristicAction.Act,
        _ when confidence >= SuggestThreshold => HeuristicAction.Suggest,
        _ => HeuristicAction.Ask
    };
}
