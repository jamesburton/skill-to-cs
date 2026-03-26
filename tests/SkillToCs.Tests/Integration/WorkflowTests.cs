using SkillToCs.Engine;
using SkillToCs.Rules.Generation;

namespace SkillToCs.Tests.Integration;

public class WorkflowTests
{
    private static string GetSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "SkillToCs.slnx")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "samples", "minimal-api", "MinimalApi");
    }

    [Fact]
    public async Task AssessThenScanThenDescribe_WorksTogether()
    {
        var ctx = new ProjectContext(GetSampleProjectPath());

        // Register rules
        var registry = new RuleRegistry();
        registry.Register(new ApiEndpointRule());
        registry.Register(new ServiceRule());

        // Assess: find applicable rules
        var applicableRules = registry.GetApplicable(ctx);
        Assert.NotEmpty(applicableRules);

        // Scan each applicable rule
        foreach (var rule in applicableRules)
        {
            var instances = await rule.ScanAsync(ctx, CancellationToken.None);
            Assert.NotNull(instances);

            // Describe each applicable rule
            var schema = rule.Describe();
            Assert.NotNull(schema);
            Assert.NotEmpty(schema.Name);
            Assert.NotEmpty(schema.Parameters);
        }
    }
}
