namespace SkillToCs.Models;

public record SkillCatalog(
    string Version,
    DateTimeOffset Generated,
    IReadOnlyList<CatalogEntry> Rules,
    CatalogLayers Layers
);

public record CatalogEntry(
    string Name,
    string Description,
    string Category,
    string RuleSubtype,
    string[] Modes,
    int? InstanceCount,
    string? SourceHash
);

public record CatalogLayers(
    string? UserPath,
    string ProjectPath,
    Dictionary<string, string> ComponentPaths
);
