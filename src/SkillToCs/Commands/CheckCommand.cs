using System.CommandLine;
using System.Diagnostics;
using SkillToCs.Engine;
using SkillToCs.Models;
using SkillToCs.Output;

namespace SkillToCs.Commands;

public static class CheckCommand
{
    public static Command Create(RuleRegistry registry)
    {
        var command = new Command("check", "Run all checks against the project");

        var failFastOption = new Option<bool>("--fail-fast") { Description = "Stop on first failure" };
        var categoryOption = new Option<string?>("--category") { Description = "Run only rules in this category" };
        var skillOption = new Option<string?>("--skill") { Description = "Run a single named skill" };

        command.Add(SharedOptions.Path);
        command.Add(SharedOptions.Json);
        command.Add(failFastOption);
        command.Add(categoryOption);
        command.Add(skillOption);

        command.SetAction(async parseResult =>
        {
            var path = parseResult.GetValue(SharedOptions.Path) ?? ".";
            var json = parseResult.GetValue(SharedOptions.Json);
            var failFast = parseResult.GetValue(failFastOption);
            var category = parseResult.GetValue(categoryOption);
            var skill = parseResult.GetValue(skillOption);

            var ctx = new ProjectContext(path);
            var ct = CancellationToken.None;
            var sw = Stopwatch.StartNew();

            // Determine which rules to check
            IReadOnlyList<IRule> rulesToCheck;
            if (skill is not null)
            {
                var rule = registry.GetByName(skill);
                if (rule is null)
                {
                    ConsoleOutput.Error($"Rule '{skill}' not found.");
                    return;
                }
                rulesToCheck = [rule];
            }
            else if (category is not null)
            {
                rulesToCheck = registry.GetByCategory(category);
                if (rulesToCheck.Count == 0)
                {
                    ConsoleOutput.Error($"No rules found in category '{category}'.");
                    return;
                }
            }
            else
            {
                // All verification rules that apply
                rulesToCheck = registry.GetApplicable(ctx)
                    .Where(r => r.Subtype == RuleSubtype.Verification)
                    .ToList();
            }

            var results = new List<VerificationResult>();

            foreach (var rule in rulesToCheck)
            {
                var result = await rule.VerifyAsync(ctx, ct);
                results.Add(result);

                if (failFast && result.Status == VerificationStatus.Fail)
                    break;
            }

            sw.Stop();

            var overallStatus = results.Any(r => r.Status == VerificationStatus.Error)
                ? CheckStatus.Error
                : results.Any(r => r.Status == VerificationStatus.Fail)
                    ? CheckStatus.SomeFailed
                    : CheckStatus.AllPassed;

            var checkResult = new CheckResult(results, overallStatus, sw.Elapsed);

            if (json)
                JsonOutput.Write(checkResult);
            else
                ConsoleOutput.CheckResults(checkResult);
        });

        return command;
    }
}
