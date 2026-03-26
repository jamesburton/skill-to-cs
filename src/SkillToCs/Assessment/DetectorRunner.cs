using SkillToCs.Models;

namespace SkillToCs.Assessment;

public sealed class DetectorRunner
{
    private readonly List<IDetector> _detectors = [];

    public void Register(IDetector detector) =>
        _detectors.Add(detector);

    public async Task<ProjectAssessment> RunAllAsync(string rootPath, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(rootPath);
        var detections = new List<Detection>();
        var allOpportunities = new List<ScriptOpportunity>();

        var ordered = _detectors.OrderBy(d => d.Priority);

        foreach (var detector in ordered)
        {
            try
            {
                var detection = await detector.DetectAsync(fullPath, ct);
                if (detection is not null)
                {
                    detections.Add(detection);
                    allOpportunities.AddRange(detection.Opportunities);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Detector '{detector.Name}' failed: {ex.Message}");
            }
        }

        return new ProjectAssessment(
            fullPath,
            DateTimeOffset.UtcNow,
            detections,
            allOpportunities,
            []);
    }
}
