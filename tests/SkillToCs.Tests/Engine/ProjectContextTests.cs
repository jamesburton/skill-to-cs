using SkillToCs.Engine;

namespace SkillToCs.Tests.Engine;

public class ProjectContextTests
{
    private static string GetSampleProjectPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "SkillToCs.slnx")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "samples", "minimal-api", "MinimalApi");
    }

    private readonly ProjectContext _ctx = new(GetSampleProjectPath());

    [Fact]
    public void FindsCSharpProjects()
    {
        Assert.NotEmpty(_ctx.CSharpProjects);
        Assert.Contains(_ctx.CSharpProjects, p => p.EndsWith("MinimalApi.csproj"));
    }

    [Fact]
    public void FindsSourceFiles()
    {
        Assert.NotEmpty(_ctx.SourceFiles);
        Assert.Contains(_ctx.SourceFiles, f => f.EndsWith(".cs"));
    }

    [Fact]
    public void TypeExists_FindsExistingType()
    {
        Assert.True(_ctx.TypeExists("UserDto"));
    }

    [Fact]
    public void TypeExists_ReturnsFalseForMissing()
    {
        Assert.False(_ctx.TypeExists("NonExistent"));
    }

    [Fact]
    public void FindTypeFile_ReturnsCorrectPath()
    {
        var path = _ctx.FindTypeFile("UserDto");
        Assert.NotNull(path);
        Assert.Contains("Models", path);
        Assert.EndsWith("UserDto.cs", path);
    }

    [Fact]
    public void GetSyntaxTree_ParsesCorrectly()
    {
        var sourceFile = _ctx.SourceFiles.First(f => f.EndsWith("UserDto.cs"));
        var tree = _ctx.GetSyntaxTree(sourceFile);
        var root = tree.GetRoot();

        Assert.NotNull(root);
        Assert.True(root.DescendantNodes().Any());
    }
}
