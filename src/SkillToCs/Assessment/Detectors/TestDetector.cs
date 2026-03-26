using SkillToCs.Models;

namespace SkillToCs.Assessment.Detectors;

public sealed class TestDetector : IDetector
{
    public string Name => "test";
    public int Priority => 30;

    public async Task<Detection?> DetectAsync(string rootPath, CancellationToken ct)
    {
        var allCsproj = Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories);
        var testProjects = allCsproj.Where(p =>
        {
            var name = Path.GetFileNameWithoutExtension(p);
            return name.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Tests", StringComparison.OrdinalIgnoreCase);
        }).ToArray();

        if (testProjects.Length == 0)
            return null;

        string? testFramework = null;
        var hasCoverage = false;

        foreach (var proj in testProjects)
        {
            ct.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(proj, ct);

            if (testFramework is null)
            {
                if (content.Contains("xunit", StringComparison.OrdinalIgnoreCase))
                    testFramework = "xunit";
                else if (content.Contains("NUnit", StringComparison.OrdinalIgnoreCase))
                    testFramework = "NUnit";
                else if (content.Contains("MSTest", StringComparison.OrdinalIgnoreCase))
                    testFramework = "MSTest";
            }

            if (content.Contains("coverlet", StringComparison.OrdinalIgnoreCase))
                hasCoverage = true;
        }

        var properties = new Dictionary<string, object>
        {
            ["testFramework"] = testFramework ?? "unknown",
            ["testProjectCount"] = testProjects.Length,
            ["hasCoverage"] = hasCoverage
        };

        var opportunities = new List<ScriptOpportunity>
        {
            new("test-runner", "Run test suite and report results",
                "verification", testProjects, ScriptCapability.Check)
        };

        if (hasCoverage)
        {
            opportunities.Add(new("coverage-check",
                "Run tests with coverage and verify thresholds",
                "verification", testProjects, ScriptCapability.Check));
        }

        return new Detection(Name, "testing", properties, opportunities);
    }
}
