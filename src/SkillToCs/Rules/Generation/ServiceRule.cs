using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SkillToCs.Engine;
using SkillToCs.Models;

namespace SkillToCs.Rules.Generation;

public sealed class ServiceRule : IRule
{
    public string Name => "service";
    public string Description => "Generates a service class with interface, DI registration, and method stubs.";
    public string Category => "Generation";
    public RuleSubtype Subtype => RuleSubtype.Generation;
    public HeuristicPolicy HeuristicPolicy => HeuristicPolicy.Default;

    private static readonly RuleSchema Schema = new(
        "service",
        "Generates a service class with interface, DI registration, and method stubs.",
        [
            new ParameterDef("name", new ParamType.StringType(), Required: true,
                Description: "Service class name, e.g. UserService"),
            new ParameterDef("interface", new ParamType.BoolType(), Required: false, DefaultValue: true,
                Description: "Whether to generate an I{Name} interface"),
            new ParameterDef("methods", new ParamType.ArrayType(new ParamType.StringType()), Required: false,
                DefaultValue: null,
                Description: "Method signatures, e.g. GetById(int id): UserDto?"),
            new ParameterDef("lifetime", new ParamType.EnumType(["Scoped", "Transient", "Singleton"]),
                Required: false, DefaultValue: "Scoped",
                Description: "DI service lifetime"),
            new ParameterDef("inject", new ParamType.ArrayType(new ParamType.StringType()), Required: false,
                DefaultValue: null,
                Description: "Dependencies to inject via constructor, e.g. IUserRepository")
        ],
        [],
        [
            new RuleExample("Basic service", "Generate a user service with repository injection",
                new Dictionary<string, object?>
                {
                    ["name"] = "UserService",
                    ["interface"] = true,
                    ["methods"] = new[] { "GetById(int id): UserDto?", "Create(CreateUserRequest req): UserDto" },
                    ["lifetime"] = "Scoped",
                    ["inject"] = new[] { "IUserRepository", "ILogger<UserService>" }
                })
        ]);

    public RuleSchema Describe() => Schema;

    public bool AppliesTo(ProjectContext ctx) =>
        ctx.CSharpProjects.Count > 0;

    public async Task<IReadOnlyList<ScannedInstance>> ScanAsync(ProjectContext ctx, CancellationToken ct)
    {
        var instances = new List<ScannedInstance>();

        foreach (var file in ctx.SourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            var tree = ctx.GetSyntaxTree(file);
            var root = await tree.GetRootAsync(ct);

            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var cls in classes)
            {
                if (cls.BaseList is null)
                    continue;

                var className = cls.Identifier.Text;

                // Find interface in base list that matches I{ClassName} pattern
                var matchingInterface = cls.BaseList.Types
                    .Select(t => t.Type.ToString())
                    .FirstOrDefault(t => t == $"I{className}");

                if (matchingInterface is null)
                    continue;

                // Extract constructor parameters (injected dependencies)
                var ctor = cls.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
                var injected = ctor?.ParameterList.Parameters
                    .Select(p => p.Type?.ToString() ?? "object")
                    .ToList() ?? [];

                // Extract method signatures
                var methods = cls.Members.OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Modifiers.Any(mod => mod.Text == "public"))
                    .Select(m => m.Identifier.Text)
                    .ToList();

                var parameters = new Dictionary<string, object?>
                {
                    ["name"] = className,
                    ["interface"] = true,
                    ["methods"] = methods,
                    ["inject"] = injected
                };

                var lineSpan = cls.GetLocation().GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                instances.Add(new ScannedInstance(
                    Name,
                    file,
                    line,
                    parameters,
                    $"{className} : {matchingInterface}"));
            }
        }

        return instances;
    }

    public Task<GenerationResult> GenerateAsync(RuleParams parameters, ProjectContext ctx, CancellationToken ct)
    {
        var name = parameters.Get<string>("name");
        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(GenerationResult.Error("Parameter 'name' is required."));

        var generateInterface = parameters.Get<bool?>("interface") ?? true;
        var methods = parameters.Get<List<string>>("methods");
        var lifetime = parameters.Get<string>("lifetime") ?? "Scoped";
        var inject = parameters.Get<List<string>>("inject") ?? [];

        // If no methods specified, ask
        if (methods is null || methods.Count == 0)
        {
            return Task.FromResult(GenerationResult.NeedInput(
            [
                new InputQuestion(
                    "methods",
                    $"What methods should {name} have?",
                    "Specify method signatures like: GetById(int id): UserDto?",
                    [
                        new SuggestedOption("CRUD example",
                            "GetById(int id): EntityDto?,GetAll(): List<EntityDto>,Create(CreateRequest req): EntityDto,Update(int id, UpdateRequest req): EntityDto,Delete(int id): bool")
                    ])
            ]));
        }

        var interfaceName = $"I{name}";
        var fragments = new List<CodeFragment>();
        var inferences = new List<InferenceNote>();

        // Determine target directory
        var servicesDir = ctx.ResolveDirectory("Services");
        if (servicesDir is null)
        {
            // Look for any directory containing existing service files
            var existingServiceFile = ctx.SourceFiles
                .FirstOrDefault(f => f.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase));

            if (existingServiceFile is not null)
            {
                servicesDir = Path.GetDirectoryName(existingServiceFile);
                inferences.Add(new InferenceNote(
                    $"Using directory '{servicesDir}' based on existing service files",
                    0.85,
                    "Found existing service files in this directory"));
            }
            else
            {
                // Default: create Services/ alongside the first .csproj
                var projectDir = ctx.CSharpProjects.Count > 0
                    ? Path.GetDirectoryName(ctx.CSharpProjects[0])
                    : ctx.RootPath;
                servicesDir = Path.Combine(projectDir!, "Services");
                inferences.Add(new InferenceNote(
                    $"Creating new Services/ directory at '{servicesDir}'",
                    0.90,
                    "No existing services directory found, using convention"));
            }
        }

        // Determine namespace from directory structure
        var ns = InferNamespace(servicesDir!, ctx);

        // Parse methods
        var parsedMethods = methods.Select(ParseMethodSignature).ToList();

        // Generate interface
        if (generateInterface)
        {
            var interfaceFile = Path.Combine(servicesDir!, $"{interfaceName}.cs");
            var interfaceContent = GenerateInterfaceContent(interfaceName, ns, parsedMethods);
            fragments.Add(new CodeFragment(
                interfaceFile,
                null,
                interfaceContent,
                FragmentType.NewFile,
                $"interface:{interfaceName}"));
        }

        // Generate implementation
        var implFile = Path.Combine(servicesDir!, $"{name}.cs");
        var implContent = GenerateImplementationContent(
            name, generateInterface ? interfaceName : null, ns, parsedMethods, inject);
        fragments.Add(new CodeFragment(
            implFile,
            null,
            implContent,
            FragmentType.NewFile,
            $"class:{name}"));

        // Generate DI registration
        var diFragment = GenerateDiRegistration(name, interfaceName, lifetime, generateInterface, ctx);
        if (diFragment is not null)
            fragments.Add(diFragment);

        return Task.FromResult(GenerationResult.Success(fragments, inferences));
    }

    public async Task<VerificationResult> VerifyAsync(ProjectContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var violations = new List<Violation>();
        var filesChecked = 0;
        var passed = 0;

        foreach (var file in ctx.SourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            var tree = ctx.GetSyntaxTree(file);
            var root = await tree.GetRootAsync(ct);
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

            foreach (var cls in classes)
            {
                var className = cls.Identifier.Text;
                if (!className.EndsWith("Service"))
                    continue;

                filesChecked++;
                var interfaceName = $"I{className}";

                // Check naming convention
                var hasInterface = cls.BaseList?.Types
                    .Any(t => t.Type.ToString() == interfaceName) ?? false;

                if (!hasInterface && !ctx.TypeExists(interfaceName))
                {
                    var lineSpan = cls.GetLocation().GetLineSpan();
                    violations.Add(new Violation(
                        file,
                        lineSpan.StartLinePosition.Line + 1,
                        "SVC001",
                        $"Service '{className}' does not have a matching interface '{interfaceName}'.",
                        ViolationSeverity.Warning,
                        true));
                }
                else
                {
                    passed++;
                }

                // Check DI registration (simple heuristic: search for AddScoped/AddTransient/AddSingleton with the interface)
                var isRegistered = ctx.SourceFiles.Any(f =>
                {
                    var content = File.ReadAllText(f);
                    return content.Contains($"<{interfaceName},", StringComparison.Ordinal)
                        || content.Contains($"<{interfaceName}>", StringComparison.Ordinal);
                });

                if (!isRegistered)
                {
                    var lineSpan = cls.GetLocation().GetLineSpan();
                    violations.Add(new Violation(
                        file,
                        lineSpan.StartLinePosition.Line + 1,
                        "SVC002",
                        $"Service '{className}' does not appear to be registered in DI.",
                        ViolationSeverity.Warning,
                        true));
                }
            }
        }

        sw.Stop();
        var status = violations.Count == 0 ? VerificationStatus.Pass : VerificationStatus.Fail;

        return new VerificationResult(
            Name,
            status,
            violations,
            [],
            new VerificationStats(filesChecked, passed, violations.Count, sw.Elapsed));
    }

    private static ParsedMethod ParseMethodSignature(string sig)
    {
        // Parse "GetById(int id): UserDto?" into components
        var match = Regex.Match(sig.Trim(), @"^(\w+)\(([^)]*)\)\s*:\s*(.+)$");
        if (!match.Success)
            return new ParsedMethod(sig.Trim(), [], "void");

        var methodName = match.Groups[1].Value;
        var paramsStr = match.Groups[2].Value;
        var returnType = match.Groups[3].Value.Trim();

        var paramList = string.IsNullOrWhiteSpace(paramsStr)
            ? []
            : paramsStr.Split(',').Select(p => p.Trim()).ToList();

        return new ParsedMethod(methodName, paramList, returnType);
    }

    private static string GenerateInterfaceContent(
        string interfaceName, string ns, List<ParsedMethod> methods)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"public interface {interfaceName}");
        sb.AppendLine("{");

        foreach (var m in methods)
        {
            var asyncName = m.Name.EndsWith("Async") ? m.Name : $"{m.Name}Async";
            var taskReturn = WrapInTask(m.ReturnType);
            var paramStr = string.Join(", ", m.Parameters);
            sb.AppendLine($"    {taskReturn} {asyncName}({paramStr});");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateImplementationContent(
        string className, string? interfaceName, string ns,
        List<ParsedMethod> methods, List<string> inject)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        var baseList = interfaceName is not null ? $" : {interfaceName}" : "";
        sb.AppendLine($"public sealed class {className}{baseList}");
        sb.AppendLine("{");

        // Fields for injected dependencies
        foreach (var dep in inject)
        {
            var fieldName = ToFieldName(dep);
            sb.AppendLine($"    private readonly {dep} {fieldName};");
        }

        if (inject.Count > 0)
            sb.AppendLine();

        // Constructor
        var ctorParams = string.Join(", ", inject.Select(d => $"{d} {ToParamName(d)}"));
        sb.AppendLine($"    public {className}({ctorParams})");
        sb.AppendLine("    {");
        foreach (var dep in inject)
        {
            var fieldName = ToFieldName(dep);
            var paramName = ToParamName(dep);
            sb.AppendLine($"        {fieldName} = {paramName};");
        }
        sb.AppendLine("    }");

        // Methods
        foreach (var m in methods)
        {
            sb.AppendLine();
            var asyncName = m.Name.EndsWith("Async") ? m.Name : $"{m.Name}Async";
            var taskReturn = WrapInTask(m.ReturnType);
            var paramStr = string.Join(", ", m.Parameters);
            sb.AppendLine($"    public async {taskReturn} {asyncName}({paramStr})");
            sb.AppendLine("    {");
            sb.AppendLine("        throw new NotImplementedException();");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static CodeFragment? GenerateDiRegistration(
        string name, string interfaceName, string lifetime,
        bool hasInterface, ProjectContext ctx)
    {
        // Look for ServiceCollectionExtensions or Program.cs
        var extensionsFile = ctx.SourceFiles
            .FirstOrDefault(f => Path.GetFileName(f)
                .Contains("ServiceCollectionExtensions", StringComparison.OrdinalIgnoreCase));

        var targetFile = extensionsFile
            ?? ctx.SourceFiles.FirstOrDefault(f =>
                Path.GetFileName(f).Equals("Program.cs", StringComparison.OrdinalIgnoreCase));

        if (targetFile is null)
            return null;

        var registrationLine = hasInterface
            ? $"services.Add{lifetime}<{interfaceName}, {name}>();"
            : $"services.Add{lifetime}<{name}>();";

        var insertion = LocationResolver.FindInsertionPointAfterLast(
            targetFile,
            node => node is ExpressionStatementSyntax expr
                && expr.ToString().Contains("services.Add", StringComparison.Ordinal),
            ctx);

        insertion ??= LocationResolver.FindInsertionPointAfterLast(
            targetFile,
            node => node is ExpressionStatementSyntax expr
                && expr.ToString().Contains("builder.Services", StringComparison.Ordinal),
            ctx);

        return new CodeFragment(
            targetFile,
            insertion,
            registrationLine,
            insertion is not null ? FragmentType.InsertAfter : FragmentType.NewFile,
            $"services.Add{interfaceName}");
    }

    private static string WrapInTask(string returnType) =>
        returnType == "void" ? "Task" : $"Task<{returnType}>";

    private static string ToFieldName(string typeName)
    {
        var clean = typeName.StartsWith('I') && typeName.Length > 1 && char.IsUpper(typeName[1])
            ? typeName[1..]
            : typeName;

        // Handle generic types: ILogger<UserService> -> logger
        var genericIndex = clean.IndexOf('<');
        if (genericIndex > 0)
            clean = clean[..genericIndex];

        return $"_{char.ToLowerInvariant(clean[0])}{clean[1..]}";
    }

    private static string ToParamName(string typeName)
    {
        var fieldName = ToFieldName(typeName);
        return fieldName.TrimStart('_');
    }

    private static string InferNamespace(string directory, ProjectContext ctx)
    {
        // Find the closest .csproj and build namespace from relative path
        var projectFile = ctx.CSharpProjects
            .OrderByDescending(p => directory.StartsWith(
                Path.GetDirectoryName(p)!, StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(p)!.Length : 0)
            .FirstOrDefault();

        if (projectFile is null)
            return "Services";

        var projectDir = Path.GetDirectoryName(projectFile)!;
        var projectName = Path.GetFileNameWithoutExtension(projectFile);
        var relative = Path.GetRelativePath(projectDir, directory);

        if (relative == ".")
            return projectName;

        var nsParts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => p != "." && p != "..");

        return $"{projectName}.{string.Join('.', nsParts)}";
    }

    private sealed record ParsedMethod(string Name, List<string> Parameters, string ReturnType);
}
