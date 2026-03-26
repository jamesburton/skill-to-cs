using System.CommandLine;
using SkillToCs.Engine;
using SkillToCs.Output;

namespace SkillToCs.Commands;

public static class DescribeCommand
{
    public static Command Create(RuleRegistry registry)
    {
        var command = new Command("describe", "Describe a rule and its parameters");

        var ruleArgument = new Argument<string>("rule") { Description = "Rule name to describe" };

        command.Add(ruleArgument);
        command.Add(SharedOptions.Json);

        command.SetAction(parseResult =>
        {
            var ruleName = parseResult.GetValue(ruleArgument);
            var json = parseResult.GetValue(SharedOptions.Json);

            var rule = registry.GetByName(ruleName ?? "");
            if (rule is null)
            {
                ConsoleOutput.Error($"Rule '{ruleName}' not found.");
                return;
            }

            var schema = rule.Describe();

            if (json)
            {
                JsonOutput.Write(schema);
            }
            else
            {
                ConsoleOutput.KeyValue(schema.Name, new Dictionary<string, string>
                {
                    ["Description"] = schema.Description,
                    ["Parameters"] = schema.Parameters.Count.ToString(),
                    ["Examples"] = schema.Examples.Count.ToString(),
                    ["Blocks"] = schema.Blocks.Count.ToString()
                });

                if (schema.Parameters.Count > 0)
                {
                    var rows = schema.Parameters.Select(p => new Dictionary<string, string>
                    {
                        ["Name"] = p.Name,
                        ["Type"] = FormatParamType(p.Type),
                        ["Required"] = p.Required ? "Yes" : "No",
                        ["Default"] = p.DefaultValue?.ToString() ?? "-",
                        ["Description"] = p.Description ?? ""
                    }).ToList();

                    ConsoleOutput.Table("Parameters", rows);
                }

                if (schema.Examples.Count > 0)
                {
                    var exampleRows = schema.Examples.Select(e => new Dictionary<string, string>
                    {
                        ["Name"] = e.Name,
                        ["Description"] = e.Description ?? "",
                        ["Params"] = string.Join(", ", e.Parameters.Select(kv => $"{kv.Key}={kv.Value}"))
                    }).ToList();

                    ConsoleOutput.Table("Examples", exampleRows);
                }
            }
        });

        return command;
    }

    private static string FormatParamType(ParamType type) => type switch
    {
        ParamType.StringType => "string",
        ParamType.IntType => "int",
        ParamType.BoolType => "bool",
        ParamType.EnumType e => $"enum({string.Join("|", e.Values)})",
        ParamType.ArrayType a => $"array<{FormatParamType(a.Items)}>",
        ParamType.ObjectType => "object",
        _ => type.GetType().Name
    };
}
