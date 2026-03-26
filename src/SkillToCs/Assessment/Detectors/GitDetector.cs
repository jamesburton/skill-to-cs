using SkillToCs.Models;

namespace SkillToCs.Assessment.Detectors;

public sealed class GitDetector : IDetector
{
    public string Name => "git";
    public int Priority => 40;

    public Task<Detection?> DetectAsync(string rootPath, CancellationToken ct)
    {
        var hasGit = Directory.Exists(Path.Combine(rootPath, ".git"));
        if (!hasGit)
            return Task.FromResult<Detection?>(null);

        var hasHusky = Directory.Exists(Path.Combine(rootPath, ".husky"));
        var hasGitHooks = false;

        var hooksDir = Path.Combine(rootPath, ".git", "hooks");
        if (Directory.Exists(hooksDir))
        {
            hasGitHooks = Directory.GetFiles(hooksDir)
                .Any(f => !Path.GetFileName(f).EndsWith(".sample", StringComparison.Ordinal));
        }

        var hasHooks = hasHusky || hasGitHooks;
        var hasGitignore = File.Exists(Path.Combine(rootPath, ".gitignore"));

        var properties = new Dictionary<string, object>
        {
            ["hasGit"] = hasGit,
            ["hasHooks"] = hasHooks,
            ["hasGitignore"] = hasGitignore,
            ["hasHusky"] = hasHusky
        };

        var opportunities = new List<ScriptOpportunity>();

        if (hasHooks)
        {
            var sourceFiles = new List<string>();
            if (hasHusky) sourceFiles.Add(Path.Combine(rootPath, ".husky"));
            if (hasGitHooks) sourceFiles.Add(hooksDir);

            opportunities.Add(new("pre-commit-check",
                "Verify git hooks are configured and executable",
                "workflow", sourceFiles.ToArray(), ScriptCapability.Check));
        }

        Detection result = new(Name, "source-control", properties, opportunities);
        return Task.FromResult<Detection?>(result);
    }
}
