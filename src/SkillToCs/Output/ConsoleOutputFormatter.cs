using SkillToCs.Engine;
using SkillToCs.Models;
using Spectre.Console;

namespace SkillToCs.Output;

public static class ConsoleOutput
{
    public static void Success(string message)
    {
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
    }

    public static void Warning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");
    }

    public static void Error(string message)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }

    public static void Info(string message)
    {
        AnsiConsole.MarkupLine($"[blue]{Markup.Escape(message)}[/]");
    }

    public static void Table(string title, IEnumerable<Dictionary<string, string>> rows)
    {
        var rowList = rows.ToList();
        if (rowList.Count == 0)
        {
            Info("No data to display.");
            return;
        }

        var table = new Table()
            .Title(Markup.Escape(title))
            .Border(TableBorder.Rounded);

        var columns = rowList[0].Keys.ToList();
        foreach (var col in columns)
        {
            table.AddColumn(new TableColumn(Markup.Escape(col)));
        }

        foreach (var row in rowList)
        {
            var cells = columns.Select(c =>
                row.TryGetValue(c, out var value) ? Markup.Escape(value) : "");
            table.AddRow(cells.ToArray());
        }

        AnsiConsole.Write(table);
    }

    public static void KeyValue(string title, Dictionary<string, string> pairs)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("Key")
            .AddColumn("Value");

        foreach (var (key, value) in pairs)
        {
            table.AddRow(
                $"[blue]{Markup.Escape(key)}[/]",
                Markup.Escape(value));
        }

        var panel = new Panel(table)
            .Header(Markup.Escape(title))
            .Border(BoxBorder.Rounded);

        AnsiConsole.Write(panel);
    }

    public static void DiffPreview(IEnumerable<FileChange> changes)
    {
        var changeList = changes.ToList();
        if (changeList.Count == 0)
        {
            Info("No file changes.");
            return;
        }

        var table = new Table()
            .Title("File Changes")
            .Border(TableBorder.Rounded)
            .AddColumn("Action")
            .AddColumn("File")
            .AddColumn(new TableColumn("Added").RightAligned())
            .AddColumn(new TableColumn("Removed").RightAligned());

        foreach (var change in changeList)
        {
            var actionMarkup = change.Action switch
            {
                FileChangeAction.Created or FileChangeAction.WouldCreate
                    => $"[green]{change.Action}[/]",
                FileChangeAction.Modified or FileChangeAction.WouldModify
                    => $"[yellow]{change.Action}[/]",
                FileChangeAction.Skipped or FileChangeAction.WouldSkip
                    => $"[dim]{change.Action}[/]",
                _ => Markup.Escape(change.Action.ToString())
            };

            table.AddRow(
                actionMarkup,
                Markup.Escape(change.FilePath),
                $"[green]+{change.LinesAdded}[/]",
                $"[red]-{change.LinesRemoved}[/]");
        }

        AnsiConsole.Write(table);

        foreach (var change in changeList.Where(c => c.DiffPreview is not null))
        {
            var panel = new Panel(Markup.Escape(change.DiffPreview!))
                .Header(Markup.Escape(change.FilePath))
                .Border(BoxBorder.Rounded);

            AnsiConsole.Write(panel);
        }
    }

    public static void Assessment(ProjectAssessment assessment)
    {
        var kvPairs = new Dictionary<string, string>
        {
            ["Root Path"] = assessment.RootPath,
            ["Assessed At"] = assessment.AssessedAt.ToString("u"),
            ["Applicable Rules"] = assessment.ApplicableRules.Count.ToString(),
            ["Detections"] = assessment.Detections.Count.ToString(),
            ["Opportunities"] = assessment.AllOpportunities.Count.ToString()
        };

        KeyValue("Project Assessment", kvPairs);

        if (assessment.ApplicableRules.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[blue]Applicable Rules:[/]");
            foreach (var rule in assessment.ApplicableRules)
            {
                AnsiConsole.MarkupLine($"  [green]\u2713[/] {Markup.Escape(rule)}");
            }
        }

        if (assessment.AllOpportunities.Count > 0)
        {
            var table = new Table()
                .Title("Script Opportunities")
                .Border(TableBorder.Rounded)
                .AddColumn("Name")
                .AddColumn("Category")
                .AddColumn("Description")
                .AddColumn("Capabilities");

            foreach (var opp in assessment.AllOpportunities)
            {
                table.AddRow(
                    Markup.Escape(opp.Name),
                    Markup.Escape(opp.Category),
                    Markup.Escape(opp.Description),
                    Markup.Escape(opp.Capabilities.ToString()));
            }

            AnsiConsole.Write(table);
        }
    }

    public static void ScanResults(string ruleName, IReadOnlyList<ScannedInstance> instances)
    {
        AnsiConsole.MarkupLine($"\n[blue]Scan Results:[/] {Markup.Escape(ruleName)}");
        AnsiConsole.MarkupLine($"  Found [yellow]{instances.Count}[/] instance(s)\n");

        if (instances.Count == 0)
            return;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("File")
            .AddColumn(new TableColumn("Line").RightAligned())
            .AddColumn("Label");

        foreach (var instance in instances)
        {
            table.AddRow(
                Markup.Escape(instance.FilePath),
                instance.Line.ToString(),
                Markup.Escape(instance.DisplayLabel ?? ""));
        }

        AnsiConsole.Write(table);
    }

    public static void VerificationResults(VerificationResult result)
    {
        var statusMarkup = result.Status switch
        {
            VerificationStatus.Pass => "[green]PASS[/]",
            VerificationStatus.Fail => "[red]FAIL[/]",
            VerificationStatus.Error => "[red]ERROR[/]",
            _ => result.Status.ToString()
        };

        AnsiConsole.MarkupLine($"\n[blue]Verification:[/] {Markup.Escape(result.RuleName)} {statusMarkup}");
        AnsiConsole.MarkupLine(
            $"  Files: {result.Stats.FilesChecked}  " +
            $"Passed: [green]{result.Stats.Passed}[/]  " +
            $"Failed: [red]{result.Stats.Failed}[/]  " +
            $"Duration: {result.Stats.Duration.TotalMilliseconds:F0}ms");

        if (result.Violations.Count > 0)
        {
            AnsiConsole.MarkupLine($"\n  [red]Violations ({result.Violations.Count}):[/]");

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Severity")
                .AddColumn("File")
                .AddColumn(new TableColumn("Line").RightAligned())
                .AddColumn("Rule")
                .AddColumn("Message")
                .AddColumn("Fixable");

            foreach (var v in result.Violations)
            {
                var severityMarkup = v.Severity switch
                {
                    ViolationSeverity.Error => "[red]Error[/]",
                    ViolationSeverity.Warning => "[yellow]Warning[/]",
                    ViolationSeverity.Info => "[blue]Info[/]",
                    _ => v.Severity.ToString()
                };

                table.AddRow(
                    severityMarkup,
                    Markup.Escape(v.FilePath),
                    v.Line?.ToString() ?? "-",
                    Markup.Escape(v.RuleId),
                    Markup.Escape(v.Message),
                    v.Fixable ? "[green]Yes[/]" : "[dim]No[/]");
            }

            AnsiConsole.Write(table);
        }

        if (result.Inferences.Count > 0)
        {
            AnsiConsole.MarkupLine($"\n  [blue]Inferences ({result.Inferences.Count}):[/]");
            foreach (var inf in result.Inferences)
            {
                AnsiConsole.MarkupLine(
                    $"    [yellow]{Markup.Escape(inf.Decision)}[/] " +
                    $"(confidence: {inf.Confidence:P0}) - " +
                    $"{Markup.Escape(inf.Rationale)}");
            }
        }
    }

    public static void CheckResults(CheckResult result)
    {
        var statusMarkup = result.OverallStatus switch
        {
            CheckStatus.AllPassed => "[green]ALL PASSED[/]",
            CheckStatus.SomeFailed => "[yellow]SOME FAILED[/]",
            CheckStatus.Error => "[red]ERROR[/]",
            _ => result.OverallStatus.ToString()
        };

        AnsiConsole.MarkupLine($"\n[blue]Check Results:[/] {statusMarkup}");
        AnsiConsole.MarkupLine(
            $"  Total: {result.Results.Count}  " +
            $"Passed: [green]{result.TotalPassed}[/]  " +
            $"Failed: [red]{result.TotalFailed}[/]  " +
            $"Errors: [red]{result.TotalErrors}[/]  " +
            $"Duration: {result.TotalDuration.TotalMilliseconds:F0}ms\n");

        foreach (var vr in result.Results)
        {
            VerificationResults(vr);
        }
    }
}
