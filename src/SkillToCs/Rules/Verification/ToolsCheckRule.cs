using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkillToCs.Engine;
using SkillToCs.Models;

namespace SkillToCs.Rules.Verification;

public sealed partial class ToolsCheckRule : IRule
{
    public string Name => "tools-check";
    public string Description => "Verify .NET local tools are restored";
    public string Category => "workflow";
    public RuleSubtype Subtype => RuleSubtype.Verification;
    public HeuristicPolicy HeuristicPolicy => HeuristicPolicy.Default;

    public RuleSchema Describe() => new(
        Name,
        Description,
        Parameters:
        [
            new ParameterDef("fixMode", new ParamType.BoolType(), Required: false,
                DefaultValue: false, Description: "Restore missing tools automatically")
        ],
        Blocks: [],
        Examples: []);

    public async Task<IReadOnlyList<ScannedInstance>> ScanAsync(ProjectContext ctx, CancellationToken ct)
    {
        var expectedTools = await ReadToolsManifestAsync(ctx, ct);
        var instances = new List<ScannedInstance>();

        foreach (var (name, version) in expectedTools)
        {
            instances.Add(new ScannedInstance(
                Name,
                FilePath: Path.Combine(ctx.RootPath, ".config", "dotnet-tools.json"),
                Line: 0,
                new Dictionary<string, object?> { ["toolName"] = name, ["version"] = version },
                DisplayLabel: $"{name} v{version}"));
        }

        return instances;
    }

    public Task<GenerationResult> GenerateAsync(RuleParams parameters, ProjectContext ctx, CancellationToken ct) =>
        Task.FromResult(GenerationResult.Error("Verification rules do not generate code"));

    public async Task<VerificationResult> VerifyAsync(ProjectContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var violations = new List<Violation>();

        var expectedTools = await ReadToolsManifestAsync(ctx, ct);
        var installedTools = await GetInstalledToolsAsync(ctx, ct);
        var manifestPath = Path.Combine(ctx.RootPath, ".config", "dotnet-tools.json");

        foreach (var (name, expectedVersion) in expectedTools)
        {
            if (!installedTools.TryGetValue(name, out var installedVersion))
            {
                violations.Add(new Violation(
                    manifestPath,
                    Line: null,
                    RuleId: "TOOL_MISSING",
                    Message: $"Tool '{name}' v{expectedVersion} is not installed locally",
                    ViolationSeverity.Error,
                    Fixable: true));
            }
            else if (!string.Equals(installedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new Violation(
                    manifestPath,
                    Line: null,
                    RuleId: "TOOL_VERSION",
                    Message: $"Tool '{name}' version mismatch: expected {expectedVersion}, found {installedVersion}",
                    ViolationSeverity.Warning,
                    Fixable: true));
            }
        }

        sw.Stop();

        var status = violations.Count == 0 ? VerificationStatus.Pass : VerificationStatus.Fail;

        return new VerificationResult(
            Name,
            status,
            violations,
            Inferences: [],
            new VerificationStats(
                FilesChecked: 1,
                Passed: expectedTools.Count - violations.Count,
                Failed: violations.Count,
                sw.Elapsed));
    }

    public bool AppliesTo(ProjectContext ctx) =>
        File.Exists(Path.Combine(ctx.RootPath, ".config", "dotnet-tools.json"));

    private static async Task<Dictionary<string, string>> ReadToolsManifestAsync(
        ProjectContext ctx, CancellationToken ct)
    {
        var manifestPath = Path.Combine(ctx.RootPath, ".config", "dotnet-tools.json");
        var tools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(manifestPath))
            return tools;

        var json = await File.ReadAllTextAsync(manifestPath, ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("tools", out var toolsElement))
        {
            foreach (var tool in toolsElement.EnumerateObject())
            {
                var version = tool.Value.TryGetProperty("version", out var versionProp)
                    ? versionProp.GetString() ?? string.Empty
                    : string.Empty;

                tools[tool.Name] = version;
            }
        }

        return tools;
    }

    private static async Task<Dictionary<string, string>> GetInstalledToolsAsync(
        ProjectContext ctx, CancellationToken ct)
    {
        var (_, stdout, _) = await RunProcessAsync("dotnet", "tool list --local", ctx.RootPath, ct);
        var tools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ToolListPattern().Match(line);
            if (match.Success)
            {
                var name = match.Groups["name"].Value;
                var version = match.Groups["version"].Value;
                tools[name] = version;
            }
        }

        return tools;
    }

    [GeneratedRegex(@"^(?<name>\S+)\s+(?<version>\S+)", RegexOptions.Multiline)]
    private static partial Regex ToolListPattern();

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
