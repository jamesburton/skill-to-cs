using System.CommandLine;
using SkillToCs.Engine;
using SkillToCs.Output;

namespace SkillToCs.Commands;

public static class VerifyCommand
{
    public static Command Create(RuleRegistry registry)
    {
        var command = new Command("verify", "Verify rule compliance");

        var ruleArgument = new Argument<string?>("rule") { Description = "Rule to verify, or all if omitted", DefaultValueFactory = _ => null };

        command.Add(ruleArgument);
        command.Add(SharedOptions.Path);
        command.Add(SharedOptions.Json);

        command.SetAction(async parseResult =>
        {
            var ruleName = parseResult.GetValue(ruleArgument);
            var path = parseResult.GetValue(SharedOptions.Path) ?? ".";
            var json = parseResult.GetValue(SharedOptions.Json);

            var ctx = new ProjectContext(path);
            var ct = CancellationToken.None;

            if (ruleName is not null)
            {
                var rule = registry.GetByName(ruleName);
                if (rule is null)
                {
                    ConsoleOutput.Error($"Rule '{ruleName}' not found.");
                    return;
                }

                var result = await rule.VerifyAsync(ctx, ct);

                if (json)
                    JsonOutput.Write(result);
                else
                    ConsoleOutput.VerificationResults(result);
            }
            else
            {
                // Verify all applicable rules
                var applicable = registry.GetApplicable(ctx);
                var results = new List<Models.VerificationResult>();

                foreach (var rule in applicable)
                {
                    var result = await rule.VerifyAsync(ctx, ct);
                    results.Add(result);
                }

                if (json)
                {
                    JsonOutput.Write(results);
                }
                else
                {
                    foreach (var result in results)
                    {
                        ConsoleOutput.VerificationResults(result);
                    }

                    if (results.Count == 0)
                        ConsoleOutput.Info("No applicable verification rules found.");
                }
            }
        });

        return command;
    }
}
