using SkillToCs.Engine;

namespace SkillToCs.Tests.Engine;

public class ParameterSchemaTests
{
    private static RuleSchema CreateTestSchema(
        IReadOnlyList<ParameterDef>? parameters = null,
        IReadOnlyList<BlockRule>? blocks = null)
    {
        return new RuleSchema(
            "test-rule",
            "A test rule",
            parameters ?? [],
            blocks ?? [],
            []);
    }

    [Fact]
    public void Validate_RequiredFieldMissing_ReturnsError()
    {
        var schema = CreateTestSchema(parameters:
        [
            new ParameterDef("name", new ParamType.StringType(), Required: true)
        ]);

        var ruleParams = new RuleParams(new Dictionary<string, object?>(), schema);
        var result = ruleParams.Validate();

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("name"));
    }

    [Fact]
    public void Validate_AllRequiredPresent_ReturnsValid()
    {
        var schema = CreateTestSchema(parameters:
        [
            new ParameterDef("name", new ParamType.StringType(), Required: true),
            new ParameterDef("path", new ParamType.StringType(), Required: true)
        ]);

        var values = new Dictionary<string, object?>
        {
            ["name"] = "UserService",
            ["path"] = "/api/users"
        };

        var ruleParams = new RuleParams(values, schema);
        var result = ruleParams.Validate();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_BlockRuleTriggered_ReturnsError()
    {
        var schema = CreateTestSchema(
            parameters:
            [
                new ParameterDef("method", new ParamType.StringType(), Required: false),
                new ParameterDef("requestModel", new ParamType.StringType(), Required: false)
            ],
            blocks:
            [
                new BlockRule(
                    "method == 'GET' && requestModel != null",
                    "GET endpoints should not have a request body",
                    BlockSeverity.Error)
            ]);

        var values = new Dictionary<string, object?>
        {
            ["method"] = "GET",
            ["requestModel"] = "CreateUserRequest"
        };

        var ruleParams = new RuleParams(values, schema);
        var result = ruleParams.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("GET endpoints"));
    }

    [Fact]
    public void Validate_BlockRuleNotTriggered_ReturnsValid()
    {
        var schema = CreateTestSchema(
            parameters:
            [
                new ParameterDef("method", new ParamType.StringType(), Required: false),
                new ParameterDef("requestModel", new ParamType.StringType(), Required: false)
            ],
            blocks:
            [
                new BlockRule(
                    "method == 'GET' && requestModel != null",
                    "GET endpoints should not have a request body",
                    BlockSeverity.Error)
            ]);

        var values = new Dictionary<string, object?>
        {
            ["method"] = "POST",
            ["requestModel"] = "CreateUserRequest"
        };

        var ruleParams = new RuleParams(values, schema);
        var result = ruleParams.Validate();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DefaultsApplied_WhenNotProvided()
    {
        var schema = CreateTestSchema(parameters:
        [
            new ParameterDef("method", new ParamType.StringType(), Required: false, DefaultValue: "GET")
        ]);

        var ruleParams = new RuleParams(new Dictionary<string, object?>(), schema);
        var value = ruleParams.Get<string>("method");

        Assert.Equal("GET", value);
    }

    [Fact]
    public void ParseJson_RoundTrips()
    {
        var schema = CreateTestSchema(parameters:
        [
            new ParameterDef("name", new ParamType.StringType(), Required: true),
            new ParameterDef("count", new ParamType.IntType(), Required: false)
        ]);

        var json = """{"name": "UserService", "count": 5}""";
        var ruleParams = RuleParams.Parse(json, schema);

        Assert.Equal("UserService", ruleParams.Get<string>("name"));
        Assert.Equal(5, ruleParams.Get<int>("count"));
    }

    [Fact]
    public void ParseCliArgs_ParsesCorrectly()
    {
        var schema = CreateTestSchema(parameters:
        [
            new ParameterDef("method", new ParamType.StringType(), Required: false),
            new ParameterDef("path", new ParamType.StringType(), Required: false)
        ]);

        var args = new[] { "--method", "POST", "--path", "/" };
        var ruleParams = RuleParams.ParseCliArgs(args, schema);

        Assert.Equal("POST", ruleParams.Get<string>("method"));
        Assert.Equal("/", ruleParams.Get<string>("path"));
    }
}
