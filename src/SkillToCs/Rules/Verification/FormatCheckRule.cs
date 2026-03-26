using System.Diagnostics;
using System.Text.RegularExpressions;
using SkillToCs.Engine;
using SkillToCs.Models;

namespace SkillToCs.Rules.Verification;

public sealed partial class FormatCheckRule : IRule
{
    public string Name => "format-check";
    public string Description => "Verify code formatting via dotnet format";
    public string Category => "standards";
    public RuleSubtype Subtype => RuleSubtype.Verification;
    public HeuristicPolicy HeuristicPolicy => HeuristicPolicy.Default;

    public RuleSchema Describe() => new(
        Name,
        Description,
        Parameters:
        [
            new ParameterDef("severity", new ParamType.EnumType(["error", "warn", "info"]), Required: false,
                DefaultValue: "error", Description: "Minimum severity level for formatting checks"),
            new ParameterDef("fixMode", new ParamType.BoolType(), Required: false,
                DefaultValue: false, Description: "Apply fixes instead of just reporting")
        ],
        Blocks: [],
        Examples: []);

    public async Task<IReadOnlyList<ScannedInstance>> ScanAsync(ProjectContext ctx, CancellationToken ct)
    {
        var (_, stdout, stderr) = await RunProcessAsync(
            "dotnet", "format --verify-no-changes --severity error", ctx.RootPath, ct);

        var instances = new List<ScannedInstance>();
        var combined = stdout + Environment.NewLine + stderr;

        foreach (var filePath in ParseAffectedFiles(combined))
        {
            instances.Add(new ScannedInstance(
                Name,
                filePath,
                Line: 0,
                new Dictionary<string, object?> { ["action"] = "format" },
                DisplayLabel: $"Needs formatting: {Path.GetFileName(filePath)}"));
        }

        return instances;
    }

    public Task<GenerationResult> GenerateAsync(RuleParams parameters, ProjectContext ctx, CancellationToken ct) =>
        Task.FromResult(GenerationResult.Error("Verification rules do not generate code"));

    public async Task<VerificationResult> VerifyAsync(ProjectContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var violations = new List<Violation>();

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            "dotnet", "format --verify-no-changes --severity error", ctx.RootPath, ct);

        var combined = stdout + Environment.NewLine + stderr;
        var affectedFiles = ParseAffectedFiles(combined);

        foreach (var filePath in affectedFiles)
        {
            violations.Add(new Violation(
                filePath,
                Line: null,
                RuleId: "FORMAT001",
                Message: "File does not match expected formatting",
                ViolationSeverity.Warning,
                Fixable: true));
        }

        sw.Stop();

        var status = exitCode == 0 ? VerificationStatus.Pass : VerificationStatus.Fail;

        return new VerificationResult(
            Name,
            status,
            violations,
            Inferences: [],
            new VerificationStats(
                ctx.SourceFiles.Count,
                Passed: ctx.SourceFiles.Count - affectedFiles.Count,
                Failed: affectedFiles.Count,
                sw.Elapsed));
    }

    public bool AppliesTo(ProjectContext ctx) =>
        ctx.CSharpProjects.Count > 0 || ctx.FindFiles("**/*.sln").Count > 0;

    private static List<string> ParseAffectedFiles(string output)
    {
        var files = new List<string>();
        var matches = AffectedFilePattern().Matches(output);

        foreach (Match match in matches)
            files.Add(match.Groups["path"].Value);

        // Also capture lines that are just file paths ending in .cs
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !files.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
                && !trimmed.Contains(' '))
            {
                files.Add(trimmed);
            }
        }

        return files;
    }

    [GeneratedRegex(@"(?<path>[^\s]+\.cs)\s")]
    private static partial Regex AffectedFilePattern();

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName, string arguments, string workingDirectory, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, stdout, stderr);
    }
}
