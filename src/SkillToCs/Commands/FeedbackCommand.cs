using System.CommandLine;
using SkillToCs.Engine;

namespace SkillToCs.Commands;

public static class FeedbackCommand
{
    public static Command Create()
    {
        var command = new Command("feedback", "Record feedback for a rule instance");

        var ruleArgument = new Argument<string>("rule") { Description = "Rule that produced the issue" };
        var instanceOption = new Option<string>("--instance") { Description = "Instance identifier", Required = true };
        var issueOption = new Option<string>("--issue") { Description = "What went wrong", Required = true };
        var correctionOption = new Option<string?>("--correction") { Description = "What it should have been" };
        var scopeOption = new Option<string>("--scope") { Description = "Scope: project or user", DefaultValueFactory = _ => "project" };

        command.Add(ruleArgument);
        command.Add(instanceOption);
        command.Add(issueOption);
        command.Add(correctionOption);
        command.Add(scopeOption);
        command.Add(SharedOptions.Path);

        command.SetAction(parseResult =>
        {
            var rule = parseResult.GetValue(ruleArgument)!;
            var instance = parseResult.GetValue(instanceOption)!;
            var issue = parseResult.GetValue(issueOption)!;
            var correction = parseResult.GetValue(correctionOption);
            var scope = parseResult.GetValue(scopeOption) ?? "project";
            var path = Path.GetFullPath(parseResult.GetValue(SharedOptions.Path) ?? ".");

            // Determine base path based on scope
            var basePath = scope.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                : path;

            var store = new KnowledgeStore(basePath);

            // Build a context string from the instance and issue
            var contextParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(instance))
                contextParts.Add($"instance: {instance}");
            if (!string.IsNullOrWhiteSpace(correction))
                contextParts.Add($"correction: {correction}");

            var pitfall = new Pitfall(
                Id: Guid.NewGuid().ToString("N"),
                Context: string.Join("; ", contextParts),
                Learning: issue,
                Added: DateTimeOffset.UtcNow,
                Source: "human-feedback"
            );

            store.AddPitfall(rule, pitfall);

            Console.WriteLine($"Recorded feedback for rule '{rule}':");
            Console.WriteLine($"  Instance:   {instance}");
            Console.WriteLine($"  Issue:      {issue}");
            if (correction is not null)
                Console.WriteLine($"  Correction: {correction}");
            Console.WriteLine($"  Scope:      {scope}");
            Console.WriteLine($"  Pitfall ID: {pitfall.Id}");
        });

        return command;
    }
}
