using System.Diagnostics;
using System.Text.RegularExpressions;
using SkillToCs.Engine;
using SkillToCs.Models;

namespace SkillToCs.Rules.Verification;

public sealed partial class BuildCheckRule : IRule
{
    public string Name => "build-check";
    public string Description => "Verify project builds with all analyzers enabled";
    public string Category => "build";
    public RuleSubtype Subtype => RuleSubtype.Verification;
    public HeuristicPolicy HeuristicPolicy => HeuristicPolicy.Default;

    public RuleSchema Describe() => new(
        Name,
        Description,
        Parameters:
        [
            new ParameterDef("configuration", new ParamType.StringType(), Required: false,
                DefaultValue: "Debug", Description: "Build configuration"),
            new ParameterDef("treatWarningsAsErrors", new ParamType.BoolType(), Required: false,
                DefaultValue: false, Description: "Treat warnings as errors")
        ],
        Blocks: [],
        Examples: []);

    public Task<IReadOnlyList<ScannedInstance>> ScanAsync(ProjectContext ctx, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScannedInstance>>([]);

    public Task<GenerationResult> GenerateAsync(RuleParams parameters, ProjectContext ctx, CancellationToken ct) =>
        Task.FromResult(GenerationResult.Error("Verification rules do not generate code"));

    public async Task<VerificationResult> VerifyAsync(ProjectContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var violations = new List<Violation>();

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            "dotnet", "build --no-restore", ctx.RootPath, ct);

        var combined = stdout + Environment.NewLine + stderr;
        var matches = DiagnosticPattern().Matches(combined);

        foreach (Match match in matches)
        {
            var filePath = match.Groups["path"].Value;
            var line = int.TryParse(match.Groups["line"].Value, out var l) ? l : (int?)null;
            var level = match.Groups["level"].Value;
            var code = match.Groups["code"].Value;
            var message = match.Groups["message"].Value;

            var severity = level.Equals("error", StringComparison.OrdinalIgnoreCase)
                ? ViolationSeverity.Error
                : ViolationSeverity.Warning;

            violations.Add(new Violation(filePath, line, code, message, severity, Fixable: false));
        }

        sw.Stop();

        var errorCount = violations.Count(v => v.Severity == ViolationSeverity.Error);
        var status = exitCode == 0 && errorCount == 0 ? VerificationStatus.Pass : VerificationStatus.Fail;

        return new VerificationResult(
            Name,
            status,
            violations,
            Inferences: [],
            new VerificationStats(
                ctx.CSharpProjects.Count,
                Passed: status == VerificationStatus.Pass ? ctx.CSharpProjects.Count : 0,
                Failed: status == VerificationStatus.Fail ? ctx.CSharpProjects.Count : 0,
                sw.Elapsed));
    }

    public bool AppliesTo(ProjectContext ctx) => ctx.CSharpProjects.Count > 0;

    [GeneratedRegex(@"(?<path>[^\s(]+)\((?<line>\d+),\d+\):\s+(?<level>error|warning)\s+(?<code>\w+):\s+(?<message>.+)")]
    private static partial Regex DiagnosticPattern();

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
