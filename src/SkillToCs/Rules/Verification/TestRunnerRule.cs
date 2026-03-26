using System.Diagnostics;
using System.Text.RegularExpressions;
using SkillToCs.Engine;
using SkillToCs.Models;

namespace SkillToCs.Rules.Verification;

public sealed partial class TestRunnerRule : IRule
{
    public string Name => "test-runner";
    public string Description => "Run tests and verify they pass";
    public string Category => "build";
    public RuleSubtype Subtype => RuleSubtype.Verification;
    public HeuristicPolicy HeuristicPolicy => HeuristicPolicy.Default;

    public RuleSchema Describe() => new(
        Name,
        Description,
        Parameters:
        [
            new ParameterDef("filter", new ParamType.StringType(), Required: false,
                DefaultValue: null, Description: "Test filter expression"),
            new ParameterDef("noBuild", new ParamType.BoolType(), Required: false,
                DefaultValue: true, Description: "Skip build before running tests")
        ],
        Blocks: [],
        Examples: []);

    public async Task<IReadOnlyList<ScannedInstance>> ScanAsync(ProjectContext ctx, CancellationToken ct)
    {
        var (_, stdout, _) = await RunProcessAsync(
            "dotnet", "test --no-build --list-tests", ctx.RootPath, ct);

        var instances = new List<ScannedInstance>();
        var inTestList = false;

        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("The following Tests are available:", StringComparison.OrdinalIgnoreCase))
            {
                inTestList = true;
                continue;
            }

            if (inTestList && !string.IsNullOrWhiteSpace(trimmed))
            {
                instances.Add(new ScannedInstance(
                    Name,
                    FilePath: string.Empty,
                    Line: 0,
                    new Dictionary<string, object?> { ["testName"] = trimmed },
                    DisplayLabel: trimmed));
            }
        }

        return instances;
    }

    public Task<GenerationResult> GenerateAsync(RuleParams parameters, ProjectContext ctx, CancellationToken ct) =>
        Task.FromResult(GenerationResult.Error("Verification rules do not generate code"));

    public async Task<VerificationResult> VerifyAsync(ProjectContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var violations = new List<Violation>();

        var args = "--no-build --logger \"console;verbosity=normal\"";

        var (exitCode, stdout, stderr) = await RunProcessAsync("dotnet", $"test {args}", ctx.RootPath, ct);

        var combined = stdout + Environment.NewLine + stderr;

        var passedCount = 0;
        var failedCount = 0;
        var skippedCount = 0;

        var summaryMatch = TestSummaryPattern().Match(combined);
        if (summaryMatch.Success)
        {
            _ = int.TryParse(summaryMatch.Groups["passed"].Value, out passedCount);
            _ = int.TryParse(summaryMatch.Groups["failed"].Value, out failedCount);
            _ = int.TryParse(summaryMatch.Groups["skipped"].Value, out skippedCount);
        }

        // Parse individual test failures
        var failureMatches = FailedTestPattern().Matches(combined);
        foreach (Match match in failureMatches)
        {
            var testName = match.Groups["test"].Value.Trim();
            violations.Add(new Violation(
                FilePath: string.Empty,
                Line: null,
                RuleId: "TEST_FAIL",
                Message: $"Test failed: {testName}",
                ViolationSeverity.Error,
                Fixable: false));
        }

        // If we have a non-zero exit code but no parsed failures, add a general violation
        if (exitCode != 0 && violations.Count == 0)
        {
            violations.Add(new Violation(
                FilePath: string.Empty,
                Line: null,
                RuleId: "TEST_RUN",
                Message: "Test run failed. Check test output for details.",
                ViolationSeverity.Error,
                Fixable: false));
        }

        sw.Stop();

        var testProjects = GetTestProjects(ctx);
        var status = exitCode == 0 && failedCount == 0 ? VerificationStatus.Pass : VerificationStatus.Fail;

        return new VerificationResult(
            Name,
            status,
            violations,
            Inferences:
            [
                new InferenceNote(
                    $"Tests: {passedCount} passed, {failedCount} failed, {skippedCount} skipped",
                    Confidence: 1.0,
                    Rationale: "Parsed from dotnet test output")
            ],
            new VerificationStats(
                testProjects.Count,
                passedCount,
                failedCount,
                sw.Elapsed));
    }

    public bool AppliesTo(ProjectContext ctx) => GetTestProjects(ctx).Count > 0;

    private static IReadOnlyList<string> GetTestProjects(ProjectContext ctx)
    {
        var testProjects = new List<string>();
        testProjects.AddRange(ctx.FindFiles("**/*.Tests.csproj"));
        testProjects.AddRange(ctx.FindFiles("**/*.Test.csproj"));
        return testProjects;
    }

    [GeneratedRegex(@"Passed:\s*(?<passed>\d+).*Failed:\s*(?<failed>\d+).*Skipped:\s*(?<skipped>\d+)", RegexOptions.Singleline)]
    private static partial Regex TestSummaryPattern();

    [GeneratedRegex(@"Failed\s+(?<test>.+)$", RegexOptions.Multiline)]
    private static partial Regex FailedTestPattern();

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
