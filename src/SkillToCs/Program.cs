using System.CommandLine;
using SkillToCs.Commands;
using SkillToCs.Engine;
using SkillToCs.Rules.Generation;
using SkillToCs.Rules.Verification;

var registry = new RuleRegistry();

// Register generation rules
registry.Register(new ApiEndpointRule());
registry.Register(new ServiceRule());
registry.Register(new TestClassRule());

// Register verification rules
registry.Register(new BuildCheckRule());
registry.Register(new FormatCheckRule());
registry.Register(new TestRunnerRule());
registry.Register(new ToolsCheckRule());

// TODO: Register additional rules as they are built
// registry.Register(new MiddlewareRule());
// registry.Register(new ConfigurationRule());

var rootCommand = new RootCommand("skill-to-cs — Assess projects and generate idempotent verification/generation scripts");

rootCommand.Add(InitCommand.Create());
rootCommand.Add(AssessCommand.Create(registry));
rootCommand.Add(DescribeCommand.Create(registry));
rootCommand.Add(ScanCommand.Create(registry));
rootCommand.Add(GenerateCommand.Create(registry));
rootCommand.Add(VerifyCommand.Create(registry));
rootCommand.Add(CatalogCommand.Create(registry));
rootCommand.Add(CheckCommand.Create(registry));
rootCommand.Add(FeedbackCommand.Create());

return await rootCommand.Parse(args).InvokeAsync();
