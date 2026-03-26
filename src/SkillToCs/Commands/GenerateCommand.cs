using System.CommandLine;
using SkillToCs.Engine;
using SkillToCs.Output;

namespace SkillToCs.Commands;

public static class GenerateCommand
{
    public static Command Create(RuleRegistry registry)
    {
        var command = new Command("generate", "Generate code from a rule");

        var ruleArgument = new Argument<string>("rule") { Description = "Rule to apply" };
        var paramsOption = new Option<string?>("--params") { Description = "JSON parameters" };
        var paramsFileOption = new Option<string?>("--params-file") { Description = "Path to JSON parameters file" };
        var continueOption = new Option<string?>("--continue") { Description = "Resume session ID" };

        command.Add(ruleArgument);
        command.Add(SharedOptions.Path);
        command.Add(SharedOptions.Json);
        command.Add(SharedOptions.DryRun);
        command.Add(paramsOption);
        command.Add(paramsFileOption);
        command.Add(continueOption);

        command.SetAction(async parseResult =>
        {
            var ruleName = parseResult.GetValue(ruleArgument);
            var path = parseResult.GetValue(SharedOptions.Path) ?? ".";
            var json = parseResult.GetValue(SharedOptions.Json);
            var dryRun = parseResult.GetValue(SharedOptions.DryRun);
            var paramsJson = parseResult.GetValue(paramsOption);
            var paramsFile = parseResult.GetValue(paramsFileOption);

            var rule = registry.GetByName(ruleName ?? "");
            if (rule is null)
            {
                ConsoleOutput.Error($"Rule '{ruleName}' not found.");
                return;
            }

            var ctx = new ProjectContext(path);
            var schema = rule.Describe();

            // Parse parameters from --params JSON or --params-file
            RuleParams ruleParams;
            if (!string.IsNullOrEmpty(paramsFile))
            {
                var fileContent = await File.ReadAllTextAsync(paramsFile);
                ruleParams = RuleParams.Parse(fileContent, schema);
            }
            else if (!string.IsNullOrEmpty(paramsJson))
            {
                ruleParams = RuleParams.Parse(paramsJson, schema);
            }
            else
            {
                ruleParams = RuleParams.Parse("{}", schema);
            }

            // Validate parameters
            var validation = ruleParams.Validate();
            if (!validation.IsValid)
            {
                if (json)
                {
                    JsonOutput.Write(new { valid = false, errors = validation.Errors, warnings = validation.Warnings });
                }
                else
                {
                    foreach (var error in validation.Errors)
                        ConsoleOutput.Error(error);
                    foreach (var warning in validation.Warnings)
                        ConsoleOutput.Warning(warning);
                }
                return;
            }

            foreach (var warning in validation.Warnings)
            {
                if (!json)
                    ConsoleOutput.Warning(warning);
            }

            // Generate
            var ct = CancellationToken.None;
            var result = await rule.GenerateAsync(ruleParams, ctx, ct);

            if (result.NeedsInput)
            {
                if (json)
                {
                    JsonOutput.Write(new { needsInput = true, questions = result.Questions });
                }
                else
                {
                    ConsoleOutput.Warning("Additional input required:");
                    foreach (var q in result.Questions)
                    {
                        ConsoleOutput.Info($"  {q.ParameterName}: {q.Question}");
                        if (q.Context is not null)
                            ConsoleOutput.Info($"    Context: {q.Context}");
                        if (q.Options is not null)
                        {
                            foreach (var opt in q.Options)
                                ConsoleOutput.Info($"    - {opt.Label}: {opt.Value}");
                        }
                    }
                }
                return;
            }

            if (result.IsError)
            {
                if (json)
                    JsonOutput.Write(new { error = result.ErrorMessage });
                else
                    ConsoleOutput.Error(result.ErrorMessage ?? "Unknown generation error.");
                return;
            }

            // Success: either dry-run preview or write
            if (dryRun)
            {
                var writeResult = AtomicWriter.WriteFragments(result.Fragments, dryRun: true);

                if (json)
                    JsonOutput.Write(new { dryRun = true, changes = writeResult.Changes, inferences = result.Inferences });
                else
                    ConsoleOutput.DiffPreview(writeResult.Changes);
            }
            else
            {
                var writeResult = AtomicWriter.WriteFragments(result.Fragments, dryRun: false);

                if (json)
                {
                    JsonOutput.Write(new { success = true, changes = writeResult.Changes, inferences = result.Inferences });
                }
                else
                {
                    ConsoleOutput.DiffPreview(writeResult.Changes);

                    foreach (var inf in result.Inferences)
                    {
                        ConsoleOutput.Info($"  [{inf.Confidence:P0}] {inf.Decision} — {inf.Rationale}");
                    }

                    ConsoleOutput.Success("Generation complete.");
                }
            }
        });

        return command;
    }
}
