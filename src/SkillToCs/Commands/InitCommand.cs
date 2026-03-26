using System.CommandLine;
using SkillToCs.Engine;

namespace SkillToCs.Commands;

public static class InitCommand
{
    public static Command Create()
    {
        var command = new Command("init", "Initialize a .skill-to-cs/ configuration directory");

        var forceOption = new Option<bool>("--force") { Description = "Overwrite existing config" };
        var fullOption = new Option<bool>("--full") { Description = "Run assess + generate after init" };

        command.Add(SharedOptions.Path);
        command.Add(forceOption);
        command.Add(fullOption);

        command.SetAction(parseResult =>
        {
            var path = Path.GetFullPath(parseResult.GetValue(SharedOptions.Path) ?? ".");
            var force = parseResult.GetValue(forceOption);
            var full = parseResult.GetValue(fullOption);

            var configDir = Path.Combine(path, ".skill-to-cs");
            var configPath = Path.Combine(configDir, "config.json");

            // Check if already initialized
            if (Directory.Exists(configDir) && !force)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Directory already exists: {configDir}");
                Console.WriteLine("Use --force to overwrite existing configuration.");
                Console.ResetColor();
                return;
            }

            // Create .skill-to-cs/ directory
            Directory.CreateDirectory(configDir);

            // Generate default config.json
            var config = new SkillConfig();
            config.Save(configPath);

            Console.WriteLine($"Initialized .skill-to-cs/ in {path}");
            Console.WriteLine($"  Created: {configPath}");

            // Ensure .skill-to-cs/bin/ is in .gitignore
            EnsureGitignoreEntry(path);

            if (full)
            {
                Console.WriteLine();
                Console.WriteLine("--full specified: assess + generate would run here.");
                Console.WriteLine("(Not yet wired up — run 'skill-to-cs assess' and 'skill-to-cs generate' manually.)");
            }
        });

        return command;
    }

    private static void EnsureGitignoreEntry(string projectPath)
    {
        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        const string entry = ".skill-to-cs/bin/";

        if (File.Exists(gitignorePath))
        {
            var content = File.ReadAllText(gitignorePath);
            if (content.Contains(entry, StringComparison.Ordinal))
            {
                Console.WriteLine($"  .gitignore already contains '{entry}'");
                return;
            }

            // Append the entry, ensuring we start on a new line
            var suffix = content.Length > 0 && !content.EndsWith('\n') ? Environment.NewLine : "";
            File.AppendAllText(gitignorePath, $"{suffix}{entry}{Environment.NewLine}");
        }
        else
        {
            File.WriteAllText(gitignorePath, $"{entry}{Environment.NewLine}");
        }

        Console.WriteLine($"  Added '{entry}' to .gitignore");
    }
}
