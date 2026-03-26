namespace SkillToCs.Engine;

public enum FragmentType
{
    NewFile,
    InsertAfter,
    InsertBefore,
    Replace
}

public sealed record CodeFragment(
    string TargetFile,
    InsertionPoint? InsertionPoint,
    string Content,
    FragmentType Type,
    string? IdempotencyKey = null);
