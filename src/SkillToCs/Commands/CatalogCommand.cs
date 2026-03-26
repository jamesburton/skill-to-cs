using System.CommandLine;
using SkillToCs.Engine;
using SkillToCs.Models;
using SkillToCs.Output;

namespace SkillToCs.Commands;

public static class CatalogCommand
{
    public static Command Create(RuleRegistry registry)
    {
        var command = new Command("catalog", "Build a catalog of available rules");

        var agentsMdOption = new Option<bool>("--agents-md") { Description = "Output AGENTS.md compatible section" };
        var updateOption = new Option<bool>("--update") { Description = "Update AGENTS.md in place" };

        command.Add(SharedOptions.Path);
        command.Add(SharedOptions.Json);
        command.Add(agentsMdOption);
        command.Add(updateOption);

        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(SharedOptions.Path) ?? ".";
            var json = parseResult.GetValue(SharedOptions.Json);

            var allRules = registry.GetAll();

            var entries = allRules.Select(r => new CatalogEntry(
                r.Name,
                r.Description,
                r.Category,
                r.Subtype.ToString(),
                r.Subtype == RuleSubtype.Generation
                    ? ["scan", "generate", "verify"]
                    : ["check", "verify"],
                null,
                null
            )).ToList();

            var catalog = new SkillCatalog(
                "0.1.0",
                DateTimeOffset.UtcNow,
                entries,
                new CatalogLayers(null, path, new Dictionary<string, string>()));

            // Write to .skill-to-cs/catalog.json
            var catalogDir = Path.Combine(path, ".skill-to-cs");
            if (!Directory.Exists(catalogDir))
                Directory.CreateDirectory(catalogDir);

            var catalogPath = Path.Combine(catalogDir, "catalog.json");
            var catalogJson = JsonOutput.Serialize(catalog);
            File.WriteAllText(catalogPath, catalogJson);

            if (json)
            {
                JsonOutput.Write(catalog);
            }
            else
            {
                var rows = entries.Select(e => new Dictionary<string, string>
                {
                    ["Name"] = e.Name,
                    ["Category"] = e.Category,
                    ["Subtype"] = e.RuleSubtype,
                    ["Description"] = e.Description,
                    ["Modes"] = string.Join(", ", e.Modes)
                }).ToList();

                ConsoleOutput.Table("Rule Catalog", rows);
                ConsoleOutput.Success($"Catalog written to {catalogPath}");
            }
        });

        return command;
    }
}
