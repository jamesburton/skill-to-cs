using System.CommandLine;

namespace SkillToCs.Commands;

public static class SharedOptions
{
    public static Option<bool> Json { get; } = new("--json") { Description = "Output structured JSON for agent consumption" };
    public static Option<bool> Verbose { get; } = new("--verbose") { Description = "Show detailed output" };
    public static Option<string> Path { get; } = new("--path") { Description = "Target directory (defaults to current directory)", DefaultValueFactory = _ => "." };
    public static Option<bool> DryRun { get; } = new("--dry-run") { Description = "Preview changes without writing files" };
    public static Option<bool> NoColor { get; } = new("--no-color") { Description = "Disable ANSI color output" };
}
