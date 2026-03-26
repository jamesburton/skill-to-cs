namespace SkillToCs.Engine;

public sealed record RuleSchema(
    string Name,
    string Description,
    IReadOnlyList<ParameterDef> Parameters,
    IReadOnlyList<BlockRule> Blocks,
    IReadOnlyList<RuleExample> Examples);

public sealed record ParameterDef(
    string Name,
    ParamType Type,
    bool Required,
    object? DefaultValue = null,
    string? Description = null);

public abstract record ParamType
{
    public sealed record StringType : ParamType;
    public sealed record IntType : ParamType;
    public sealed record BoolType : ParamType;
    public sealed record EnumType(string[] Values) : ParamType;
    public sealed record ArrayType(ParamType Items) : ParamType;
    public sealed record ObjectType(IReadOnlyList<ParameterDef> Properties) : ParamType;
}

public enum BlockSeverity
{
    Error,
    Warning
}

public sealed record BlockRule(string Condition, string Message, BlockSeverity Severity);

public sealed record RuleExample(string Name, string? Description, Dictionary<string, object?> Parameters);
