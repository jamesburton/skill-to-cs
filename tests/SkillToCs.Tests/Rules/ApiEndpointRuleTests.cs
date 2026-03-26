using SkillToCs.Engine;
using SkillToCs.Models;
using SkillToCs.Rules.Generation;

namespace SkillToCs.Tests.Rules;

public class ApiEndpointRuleTests
{
    private static string GetSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "SkillToCs.slnx")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "samples", "minimal-api", "MinimalApi");
    }

    private readonly ApiEndpointRule _rule = new();
    private readonly ProjectContext _ctx = new(GetSampleProjectPath());

    [Fact]
    public void AppliesTo_WebProject_ReturnsTrue()
    {
        Assert.True(_rule.AppliesTo(_ctx));
    }

    [Fact]
    public void Describe_ReturnsValidSchema()
    {
        var schema = _rule.Describe();

        Assert.Equal("api-endpoint", schema.Name);
        Assert.Equal(8, schema.Parameters.Count);
        Assert.Contains(schema.Parameters, p => p.Name == "rootPath");
        Assert.Contains(schema.Parameters, p => p.Name == "method");
        Assert.Contains(schema.Parameters, p => p.Name == "path");
        Assert.Contains(schema.Parameters, p => p.Name == "responseModel");
    }

    [Fact]
    public async Task ScanAsync_FindsEndpoints()
    {
        var instances = await _rule.ScanAsync(_ctx, CancellationToken.None);

        Assert.Equal(3, instances.Count);
    }

    [Fact]
    public async Task ScanAsync_ExtractsCorrectMethods()
    {
        var instances = await _rule.ScanAsync(_ctx, CancellationToken.None);

        var methods = instances.Select(i => i.Parameters["method"]?.ToString()).ToList();
        Assert.Contains("GET", methods);
        Assert.Contains("POST", methods);
    }

    [Fact]
    public async Task GenerateAsync_WithFullParams_ReturnsSuccess()
    {
        var schema = _rule.Describe();
        var values = new Dictionary<string, object?>
        {
            ["rootPath"] = "/api/products",
            ["method"] = "POST",
            ["path"] = "/",
            ["requestModel"] = "CreateProductRequest { string Name, decimal Price }",
            ["responseModel"] = "ProductDto { int Id, string Name, decimal Price }"
        };

        var ruleParams = new RuleParams(values, schema);
        var result = await _rule.GenerateAsync(ruleParams, _ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Fragments);
    }

    [Fact]
    public async Task GenerateAsync_WithMissingModel_ReturnsNeedInput()
    {
        var schema = _rule.Describe();
        var values = new Dictionary<string, object?>
        {
            ["rootPath"] = "/api/orders",
            ["method"] = "GET",
            ["path"] = "/{id:int}",
            ["responseModel"] = "OrderDto"
        };

        var ruleParams = new RuleParams(values, schema);
        var result = await _rule.GenerateAsync(ruleParams, _ctx, CancellationToken.None);

        Assert.True(result.NeedsInput);
        Assert.NotEmpty(result.Questions);
        Assert.Contains(result.Questions, q => q.ParameterName == "responseModel");
    }

    [Fact]
    public async Task VerifyAsync_CleanProject_ReturnsPassed()
    {
        var result = await _rule.VerifyAsync(_ctx, CancellationToken.None);

        Assert.Equal(VerificationStatus.Pass, result.Status);
    }
}
