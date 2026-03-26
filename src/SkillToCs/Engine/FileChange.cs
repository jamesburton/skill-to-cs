namespace SkillToCs.Engine;

public record FileChange(
    string FilePath,
    FileChangeAction Action,
    int LinesAdded,
    int LinesRemoved = 0,
    string? DiffPreview = null
);

public enum FileChangeAction { Created, Modified, Skipped, WouldCreate, WouldModify, WouldSkip }
