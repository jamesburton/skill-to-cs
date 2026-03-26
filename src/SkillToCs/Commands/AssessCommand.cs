using System.CommandLine;
using SkillToCs.Assessment;
using SkillToCs.Assessment.Detectors;
using SkillToCs.Engine;
using SkillToCs.Models;
using SkillToCs.Output;

namespace SkillToCs.Commands;

public static class AssessCommand
{
    public static Command Create(RuleRegistry registry)
    {
        var command = new Command("assess", "Assess project structure and detect applicable rules");

        command.Add(SharedOptions.Path);
        command.Add(SharedOptions.Json);
        command.Add(SharedOptions.Verbose);

        command.SetAction(async parseResult =>
        {
            var path = parseResult.GetValue(SharedOptions.Path) ?? ".";
            var json = parseResult.GetValue(SharedOptions.Json);
            var ct = CancellationToken.None;

            var runner = CreateRunner();
            var detectionResult = await runner.RunAllAsync(path, ct);

            var ctx = new ProjectContext(path);
            var applicable = registry.GetApplicable(ctx);

            var assessment = new ProjectAssessment(
                detectionResult.RootPath,
                detectionResult.AssessedAt,
                detectionResult.Detections,
                detectionResult.AllOpportunities,
                applicable.Select(r => r.Name).ToList());

            if (json)
            {
                var output = new
                {
                    rootPath = assessment.RootPath,
                    assessedAt = assessment.AssessedAt,
                    detections = assessment.Detections.Select(d => new
                    {
                        detectorName = d.DetectorName,
                        category = d.Category,
                        properties = d.Properties,
                        opportunities = d.Opportunities.Select(o => new
                        {
                            name = o.Name,
                            description = o.Description,
                            category = o.Category,
                            sourceFiles = o.SourceFiles,
                            capabilities = o.Capabilities.ToString()
                        })
                    }),
                    applicableRules = applicable.Select(r => new
                    {
                        name = r.Name,
                        description = r.Description,
                        category = r.Category,
                        subtype = r.Subtype.ToString()
                    })
                };

                JsonOutput.Write(output);
            }
            else
            {
                ConsoleOutput.Assessment(assessment);

                if (assessment.Detections.Count > 0)
                {
                    var rows = assessment.Detections.Select(d => new Dictionary<string, string>
                    {
                        ["Detector"] = d.DetectorName,
                        ["Category"] = d.Category,
                        ["Properties"] = FormatProperties(d.Properties),
                        ["Opportunities"] = string.Join(", ", d.Opportunities.Select(o => o.Name))
                    }).ToList();

                    ConsoleOutput.Table("Detections", rows);
                }
            }
        });

        return command;
    }

    private static DetectorRunner CreateRunner()
    {
        var runner = new DetectorRunner();
        runner.Register(new DotNetDetector());
        runner.Register(new EditorConfigDetector());
        runner.Register(new TestDetector());
        runner.Register(new GitDetector());
        runner.Register(new ToolManifestDetector());
        runner.Register(new AgentConfigDetector());
        return runner;
    }

    private static string FormatProperties(Dictionary<string, object> properties)
    {
        var parts = new List<string>();
        foreach (var (key, value) in properties)
        {
            var display = value switch
            {
                IList<string> list => $"{key}=[{string.Join(",", list)}]",
                bool b => b ? key : "",
                _ => $"{key}={value}"
            };

            if (!string.IsNullOrEmpty(display))
                parts.Add(display);
        }

        return string.Join("; ", parts);
    }
}
