using System.CommandLine;
using SkillToCs.Engine;
using SkillToCs.Output;

namespace SkillToCs.Commands;

public static class ScanCommand
{
    public static Command Create(RuleRegistry registry)
    {
        var command = new Command("scan", "Scan project for rule instances");

        var ruleArgument = new Argument<string?>("rule") { Description = "Rule name to scan, or all if omitted", DefaultValueFactory = _ => null };
        var instanceOption = new Option<string?>("--instance") { Description = "Extract a specific instance as clone template" };

        command.Add(ruleArgument);
        command.Add(SharedOptions.Path);
        command.Add(SharedOptions.Json);
        command.Add(instanceOption);

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

                var instances = await rule.ScanAsync(ctx, ct);

                if (json)
                    JsonOutput.Write(instances);
                else
                    ConsoleOutput.ScanResults(rule.Name, instances);
            }
            else
            {
                // Scan all applicable rules
                var applicable = registry.GetApplicable(ctx);
                var allInstances = new List<Models.ScannedInstance>();

                foreach (var rule in applicable)
                {
                    var instances = await rule.ScanAsync(ctx, ct);
                    allInstances.AddRange(instances);
                }

                if (json)
                {
                    JsonOutput.Write(allInstances);
                }
                else
                {
                    foreach (var group in allInstances.GroupBy(i => i.RuleName))
                    {
                        ConsoleOutput.ScanResults(group.Key, group.ToList());
                    }

                    if (allInstances.Count == 0)
                        ConsoleOutput.Info("No instances found.");
                }
            }
        });

        return command;
    }
}
