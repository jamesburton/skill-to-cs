using SkillToCs.Engine;
using SkillToCs.Rules.Generation;

namespace SkillToCs.Tests.Rules;

public class ServiceRuleTests
{
    private static string GetSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "SkillToCs.slnx")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "samples", "minimal-api", "MinimalApi");
    }

    private readonly ServiceRule _rule = new();
    private readonly ProjectContext _ctx = new(GetSampleProjectPath());

    [Fact]
    public void AppliesTo_DotNetProject_ReturnsTrue()
    {
        Assert.True(_rule.AppliesTo(_ctx));
    }

    [Fact]
    public async Task ScanAsync_FindsServices()
    {
        var instances = await _rule.ScanAsync(_ctx, CancellationToken.None);

        Assert.NotEmpty(instances);
        Assert.Contains(instances, i =>
            i.Parameters["name"]?.ToString() == "UserService");
    }

    [Fact]
    public void Describe_HasExpectedParams()
    {
        var schema = _rule.Describe();

        Assert.Equal(5, schema.Parameters.Count);
        Assert.Contains(schema.Parameters, p => p.Name == "name");
        Assert.Contains(schema.Parameters, p => p.Name == "interface");
        Assert.Contains(schema.Parameters, p => p.Name == "methods");
        Assert.Contains(schema.Parameters, p => p.Name == "lifetime");
        Assert.Contains(schema.Parameters, p => p.Name == "inject");
    }
}
