using SkillToCs.Engine;

namespace SkillToCs.Tests.Engine;

public class BlockEvaluatorTests
{
    private static RuleParams CreateParams(Dictionary<string, object?> values)
    {
        var schema = new RuleSchema("test", "test", [], [], []);
        return new RuleParams(values, schema);
    }

    [Fact]
    public void SimpleEquality_True()
    {
        var parameters = CreateParams(new Dictionary<string, object?> { ["method"] = "GET" });
        var result = BlockEvaluator.Evaluate("method == 'GET'", parameters);
        Assert.True(result);
    }

    [Fact]
    public void SimpleEquality_False()
    {
        var parameters = CreateParams(new Dictionary<string, object?> { ["method"] = "POST" });
        var result = BlockEvaluator.Evaluate("method == 'GET'", parameters);
        Assert.False(result);
    }

    [Fact]
    public void NullCheck_True()
    {
        var parameters = CreateParams(new Dictionary<string, object?> { ["requestModel"] = "SomeModel" });
        var result = BlockEvaluator.Evaluate("requestModel != null", parameters);
        Assert.True(result);
    }

    [Fact]
    public void NullCheck_False()
    {
        var parameters = CreateParams(new Dictionary<string, object?>());
        var result = BlockEvaluator.Evaluate("requestModel != null", parameters);
        Assert.False(result);
    }

    [Fact]
    public void AndCondition_BothTrue()
    {
        var parameters = CreateParams(new Dictionary<string, object?>
        {
            ["method"] = "GET",
            ["requestModel"] = "SomeModel"
        });
        var result = BlockEvaluator.Evaluate("method == 'GET' && requestModel != null", parameters);
        Assert.True(result);
    }

    [Fact]
    public void AndCondition_OneFalse()
    {
        var parameters = CreateParams(new Dictionary<string, object?>
        {
            ["method"] = "POST",
            ["requestModel"] = "SomeModel"
        });
        var result = BlockEvaluator.Evaluate("method == 'GET' && requestModel != null", parameters);
        Assert.False(result);
    }

    [Fact]
    public void OrCondition()
    {
        var paramsGet = CreateParams(new Dictionary<string, object?> { ["method"] = "GET" });
        var paramsPost = CreateParams(new Dictionary<string, object?> { ["method"] = "POST" });
        var paramsPut = CreateParams(new Dictionary<string, object?> { ["method"] = "PUT" });

        Assert.True(BlockEvaluator.Evaluate("method == 'GET' || method == 'POST'", paramsGet));
        Assert.True(BlockEvaluator.Evaluate("method == 'GET' || method == 'POST'", paramsPost));
        Assert.False(BlockEvaluator.Evaluate("method == 'GET' || method == 'POST'", paramsPut));
    }
}
