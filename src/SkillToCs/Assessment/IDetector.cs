using SkillToCs.Models;

namespace SkillToCs.Assessment;

public interface IDetector
{
    string Name { get; }
    int Priority { get; }
    Task<Detection?> DetectAsync(string rootPath, CancellationToken ct);
}
