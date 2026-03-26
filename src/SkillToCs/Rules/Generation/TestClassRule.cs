using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SkillToCs.Engine;
using SkillToCs.Models;

namespace SkillToCs.Rules.Generation;

public sealed class TestClassRule : IRule
{
    public string Name => "test-class";
    public string Description => "Generates a test class with mocks, setup, and test method stubs.";
    public string Category => "Generation";
    public RuleSubtype Subtype => RuleSubtype.Generation;
    public HeuristicPolicy HeuristicPolicy => HeuristicPolicy.Default;

    private static readonly string[] TestAttributes = ["Fact", "Theory", "Test", "TestMethod", "TestCase"];
    private static readonly string[] FrameworkPackages = ["xunit", "NUnit", "MSTest.TestFramework"];

    private static readonly RuleSchema Schema = new(
        "test-class",
        "Generates a test class with mocks, setup, and test method stubs.",
        [
            new ParameterDef("name", new ParamType.StringType(), Required: true,
                Description: "Test class name, e.g. UserServiceTests"),
            new ParameterDef("targetType", new ParamType.StringType(), Required: true,
                Description: "Class being tested, e.g. UserService"),
            new ParameterDef("framework", new ParamType.EnumType(["xunit", "nunit", "mstest"]),
                Required: false, DefaultValue: null,
                Description: "Test framework; auto-detected if omitted"),
            new ParameterDef("methods", new ParamType.ArrayType(new ParamType.StringType()), Required: false,
                DefaultValue: null,
                Description: "Test method names, e.g. GetById_ReturnsUser_WhenExists"),
            new ParameterDef("inject", new ParamType.ArrayType(new ParamType.StringType()), Required: false,
                DefaultValue: null,
                Description: "Dependencies to mock; auto-detected from target constructor if omitted")
        ],
        [],
        [
            new RuleExample("xUnit test class", "Generate tests for UserService",
                new Dictionary<string, object?>
                {
                    ["name"] = "UserServiceTests",
                    ["targetType"] = "UserService",
                    ["framework"] = "xunit",
                    ["methods"] = new[] { "GetById_ReturnsUser_WhenExists", "GetById_ReturnsNull_WhenNotFound" },
                    ["inject"] = new[] { "IUserRepository", "ILogger<UserService>" }
                })
        ]);

    public RuleSchema Describe() => Schema;

    public bool AppliesTo(ProjectContext ctx) =>
        ctx.CSharpProjects.Any(p =>
            p.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".Test.csproj", StringComparison.OrdinalIgnoreCase));

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
                // Check if this class contains test methods (methods with test attributes)
                var testMethods = cls.Members.OfType<MethodDeclarationSyntax>()
                    .Where(m => m.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .Any(a => TestAttributes.Contains(a.Name.ToString())))
                    .ToList();

                if (testMethods.Count == 0)
                    continue;

                var className = cls.Identifier.Text;

                // Infer target type from naming convention (XxxTests -> Xxx)
                var targetType = className.EndsWith("Tests", StringComparison.Ordinal)
                    ? className[..^5]
                    : className.EndsWith("Test", StringComparison.Ordinal)
                        ? className[..^4]
                        : className;

                // Extract constructor parameters
                var ctor = cls.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
                var injected = ctor?.ParameterList.Parameters
                    .Select(p => p.Type?.ToString() ?? "object")
                    .ToList() ?? [];

                var methodNames = testMethods.Select(m => m.Identifier.Text).ToList();

                var parameters = new Dictionary<string, object?>
                {
                    ["name"] = className,
                    ["targetType"] = targetType,
                    ["methods"] = methodNames,
                    ["inject"] = injected
                };

                var lineSpan = cls.GetLocation().GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                instances.Add(new ScannedInstance(
                    Name,
                    file,
                    line,
                    parameters,
                    $"{className} ({testMethods.Count} tests)"));
            }
        }

        return instances;
    }

    public async Task<GenerationResult> GenerateAsync(
        RuleParams parameters, ProjectContext ctx, CancellationToken ct)
    {
        var name = parameters.Get<string>("name");
        var targetType = parameters.Get<string>("targetType");

        if (string.IsNullOrWhiteSpace(name))
            return GenerationResult.Error("Parameter 'name' is required.");
        if (string.IsNullOrWhiteSpace(targetType))
            return GenerationResult.Error("Parameter 'targetType' is required.");

        var framework = parameters.Get<string>("framework");
        var methods = parameters.Get<List<string>>("methods");
        var inject = parameters.Get<List<string>>("inject");

        var inferences = new List<InferenceNote>();

        // Find test project
        var testProject = FindTestProject(targetType, ctx);
        if (testProject is null)
        {
            return GenerationResult.Error(
                $"No test project found. Expected a *.Tests.csproj or *.Test.csproj that references the project containing '{targetType}'.");
        }

        var testProjectDir = Path.GetDirectoryName(testProject)!;

        // Auto-detect framework
        framework ??= DetectFramework(testProject);
        if (framework is not null)
        {
            inferences.Add(new InferenceNote(
                $"Detected test framework: {framework}",
                0.95,
                $"Based on package references in {Path.GetFileName(testProject)}"));
        }

        framework ??= "xunit"; // ultimate fallback

        // Detect mocking library
        var mockLibrary = DetectMockLibrary(testProject);
        inferences.Add(new InferenceNote(
            $"Using mocking library: {mockLibrary}",
            mockLibrary == "Moq" ? 0.90 : 0.85,
            $"Based on package references in {Path.GetFileName(testProject)}"));

        // Auto-detect inject from target constructor if not specified
        if (inject is null || inject.Count == 0)
        {
            inject = await DetectConstructorDependencies(targetType, ctx, ct);
            if (inject.Count > 0)
            {
                inferences.Add(new InferenceNote(
                    $"Auto-detected {inject.Count} constructor dependencies from {targetType}",
                    0.92,
                    "Parsed constructor parameters using Roslyn"));
            }
        }

        // Auto-generate test method names if not specified
        if (methods is null || methods.Count == 0)
        {
            methods = await GenerateDefaultTestMethodNames(targetType, ctx, ct);
            if (methods.Count > 0)
            {
                inferences.Add(new InferenceNote(
                    $"Generated {methods.Count} test method names from {targetType}'s public methods",
                    0.80,
                    "Naming pattern: MethodName_ReturnsX_WhenValid / MethodName_Throws_WhenInvalid"));
            }
        }

        // Determine test file directory (mirror source structure)
        var testDir = ResolveTestDirectory(targetType, testProjectDir, ctx);

        // Generate test class
        var ns = InferNamespace(testDir, testProject);
        var testFile = Path.Combine(testDir, $"{name}.cs");

        var content = GenerateTestClassContent(
            name, targetType, ns, framework, mockLibrary, methods, inject);

        var fragments = new List<CodeFragment>
        {
            new(testFile, null, content, FragmentType.NewFile, $"class:{name}")
        };

        return GenerationResult.Success(fragments, inferences);
    }

    public async Task<VerificationResult> VerifyAsync(ProjectContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var violations = new List<Violation>();
        var filesChecked = 0;
        var passed = 0;

        // Collect all public classes in non-test source files
        var testProjectDirs = ctx.CSharpProjects
            .Where(p => p.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".Test.csproj", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetDirectoryName(p)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mainSourceFiles = ctx.SourceFiles
            .Where(f => !testProjectDirs.Any(td =>
                f.StartsWith(td, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Collect all test class names
        var testClassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ctx.SourceFiles.Where(f =>
            testProjectDirs.Any(td => f.StartsWith(td, StringComparison.OrdinalIgnoreCase))))
        {
            ct.ThrowIfCancellationRequested();
            var tree = ctx.GetSyntaxTree(file);
            var root = await tree.GetRootAsync(ct);
            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                testClassNames.Add(cls.Identifier.Text);
        }

        // Check each public class in main project has a test class
        foreach (var file in mainSourceFiles)
        {
            ct.ThrowIfCancellationRequested();
            var tree = ctx.GetSyntaxTree(file);
            var root = await tree.GetRootAsync(ct);

            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!cls.Modifiers.Any(m => m.Text == "public"))
                    continue;

                // Skip abstracts, statics, and records used as DTOs
                if (cls.Modifiers.Any(m => m.Text is "abstract" or "static"))
                    continue;

                filesChecked++;
                var className = cls.Identifier.Text;
                var expectedTestName = $"{className}Tests";
                var altTestName = $"{className}Test";

                if (testClassNames.Contains(expectedTestName) || testClassNames.Contains(altTestName))
                {
                    passed++;
                }
                else
                {
                    var lineSpan = cls.GetLocation().GetLineSpan();
                    violations.Add(new Violation(
                        file,
                        lineSpan.StartLinePosition.Line + 1,
                        "TST001",
                        $"Public class '{className}' has no corresponding test class '{expectedTestName}'.",
                        ViolationSeverity.Info,
                        true));
                }
            }
        }

        sw.Stop();
        var status = violations.Any(v => v.Severity == ViolationSeverity.Error)
            ? VerificationStatus.Fail
            : VerificationStatus.Pass;

        return new VerificationResult(
            Name,
            status,
            violations,
            [],
            new VerificationStats(filesChecked, passed, violations.Count, sw.Elapsed));
    }

    private static string? FindTestProject(string targetType, ProjectContext ctx)
    {
        var testProjects = ctx.CSharpProjects
            .Where(p => p.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".Test.csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (testProjects.Count == 0)
            return null;

        if (testProjects.Count == 1)
            return testProjects[0];

        // Try to find the test project that references the project containing targetType
        var targetFile = ctx.FindTypeFile(targetType);
        if (targetFile is null)
            return testProjects[0];

        // Find which source project contains the target type
        var sourceProject = ctx.CSharpProjects
            .Where(p => !p.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase)
                && !p.EndsWith(".Test.csproj", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(p => targetFile.StartsWith(
                Path.GetDirectoryName(p)!, StringComparison.OrdinalIgnoreCase));

        if (sourceProject is null)
            return testProjects[0];

        var sourceProjectName = Path.GetFileNameWithoutExtension(sourceProject);

        // Find matching test project by name convention
        return testProjects.FirstOrDefault(tp =>
            Path.GetFileNameWithoutExtension(tp)
                .StartsWith(sourceProjectName, StringComparison.OrdinalIgnoreCase))
            ?? testProjects[0];
    }

    private static string? DetectFramework(string testProjectPath)
    {
        try
        {
            var content = File.ReadAllText(testProjectPath);

            if (content.Contains("xunit", StringComparison.OrdinalIgnoreCase))
                return "xunit";
            if (content.Contains("NUnit", StringComparison.OrdinalIgnoreCase))
                return "nunit";
            if (content.Contains("MSTest", StringComparison.OrdinalIgnoreCase))
                return "mstest";
        }
        catch
        {
            // Ignore read errors
        }

        return null;
    }

    private static string DetectMockLibrary(string testProjectPath)
    {
        try
        {
            var content = File.ReadAllText(testProjectPath);

            if (content.Contains("NSubstitute", StringComparison.OrdinalIgnoreCase))
                return "NSubstitute";
            if (content.Contains("Moq", StringComparison.OrdinalIgnoreCase))
                return "Moq";
        }
        catch
        {
            // Ignore read errors
        }

        return "Moq"; // default
    }

    private static async Task<List<string>> DetectConstructorDependencies(
        string targetType, ProjectContext ctx, CancellationToken ct)
    {
        var targetFile = ctx.FindTypeFile(targetType);
        if (targetFile is null)
            return [];

        var tree = ctx.GetSyntaxTree(targetFile);
        var root = await tree.GetRootAsync(ct);

        var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == targetType);

        var ctor = cls?.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor is null)
            return [];

        return ctor.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "object")
            .ToList();
    }

    private static async Task<List<string>> GenerateDefaultTestMethodNames(
        string targetType, ProjectContext ctx, CancellationToken ct)
    {
        var targetFile = ctx.FindTypeFile(targetType);
        if (targetFile is null)
            return [];

        var tree = ctx.GetSyntaxTree(targetFile);
        var root = await tree.GetRootAsync(ct);

        var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == targetType);

        if (cls is null)
            return [];

        var publicMethods = cls.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => m.Modifiers.Any(mod => mod.Text == "public"))
            .ToList();

        var testNames = new List<string>();

        foreach (var method in publicMethods)
        {
            var methodName = method.Identifier.Text;
            var returnType = method.ReturnType.ToString();

            // Strip Task<> wrapper for display
            var cleanReturn = returnType;
            if (cleanReturn.StartsWith("Task<", StringComparison.Ordinal) && cleanReturn.EndsWith(">"))
                cleanReturn = cleanReturn[5..^1];
            else if (cleanReturn == "Task")
                cleanReturn = "void";

            // Clean up return type for method name
            var returnLabel = cleanReturn.Replace("?", "").Replace("<", "").Replace(">", "");
            if (returnLabel is "void" or "")
                returnLabel = "Success";

            testNames.Add($"{methodName}_Returns{returnLabel}_WhenValid");
            testNames.Add($"{methodName}_Throws_WhenInvalid");
        }

        return testNames;
    }

    private static string ResolveTestDirectory(
        string targetType, string testProjectDir, ProjectContext ctx)
    {
        // Try to mirror the source directory structure
        var targetFile = ctx.FindTypeFile(targetType);
        if (targetFile is null)
            return testProjectDir;

        // Find which source project directory the target is in
        var sourceProject = ctx.CSharpProjects
            .Where(p => !p.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase)
                && !p.EndsWith(".Test.csproj", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(p => targetFile.StartsWith(
                Path.GetDirectoryName(p)!, StringComparison.OrdinalIgnoreCase));

        if (sourceProject is null)
            return testProjectDir;

        var sourceDir = Path.GetDirectoryName(sourceProject)!;
        var targetDir = Path.GetDirectoryName(targetFile)!;
        var relative = Path.GetRelativePath(sourceDir, targetDir);

        if (relative == ".")
            return testProjectDir;

        return Path.Combine(testProjectDir, relative);
    }

    private static string GenerateTestClassContent(
        string className, string targetType, string ns, string framework,
        string mockLibrary, List<string> methods, List<string> inject)
    {
        var sb = new StringBuilder();

        // Using statements
        switch (framework)
        {
            case "xunit":
                sb.AppendLine("using Xunit;");
                break;
            case "nunit":
                sb.AppendLine("using NUnit.Framework;");
                break;
            case "mstest":
                sb.AppendLine("using Microsoft.VisualStudio.TestTools.UnitTesting;");
                break;
        }

        if (mockLibrary == "NSubstitute")
            sb.AppendLine("using NSubstitute;");
        else
            sb.AppendLine("using Moq;");

        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        // Class-level attribute for MSTest
        if (framework == "mstest")
            sb.AppendLine("[TestClass]");

        sb.AppendLine($"public sealed class {className}");
        sb.AppendLine("{");

        // Mock fields
        foreach (var dep in inject)
        {
            var fieldName = ToFieldName(dep);
            if (mockLibrary == "NSubstitute")
                sb.AppendLine($"    private readonly {dep} {fieldName};");
            else
                sb.AppendLine($"    private readonly Mock<{dep}> {fieldName};");
        }

        // SUT field
        sb.AppendLine($"    private readonly {targetType} _sut;");
        sb.AppendLine();

        // Constructor / Setup
        if (framework == "nunit")
        {
            sb.AppendLine("    [SetUp]");
            sb.AppendLine("    public void SetUp()");
        }
        else
        {
            sb.AppendLine($"    public {className}()");
        }

        sb.AppendLine("    {");

        // Initialize mocks
        foreach (var dep in inject)
        {
            var fieldName = ToFieldName(dep);
            if (mockLibrary == "NSubstitute")
                sb.AppendLine($"        {fieldName} = Substitute.For<{dep}>();");
            else
                sb.AppendLine($"        {fieldName} = new Mock<{dep}>();");
        }

        // Create SUT
        var ctorArgs = string.Join(", ", inject.Select(d =>
        {
            var fieldName = ToFieldName(d);
            return mockLibrary == "NSubstitute" ? fieldName : $"{fieldName}.Object";
        }));

        sb.AppendLine($"        _sut = new {targetType}({ctorArgs});");
        sb.AppendLine("    }");

        // Test methods
        foreach (var method in methods)
        {
            sb.AppendLine();

            var attribute = framework switch
            {
                "xunit" => "[Fact]",
                "nunit" => "[Test]",
                "mstest" => "[TestMethod]",
                _ => "[Fact]"
            };

            sb.AppendLine($"    {attribute}");
            sb.AppendLine($"    public async Task {method}()");
            sb.AppendLine("    {");
            sb.AppendLine("        // Arrange");
            sb.AppendLine();
            sb.AppendLine("        // Act");
            sb.AppendLine();
            sb.AppendLine("        // Assert");
            sb.AppendLine("        throw new NotImplementedException();");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string ToFieldName(string typeName)
    {
        var clean = typeName.StartsWith('I') && typeName.Length > 1 && char.IsUpper(typeName[1])
            ? typeName[1..]
            : typeName;

        var genericIndex = clean.IndexOf('<');
        if (genericIndex > 0)
            clean = clean[..genericIndex];

        return $"_{char.ToLowerInvariant(clean[0])}{clean[1..]}";
    }

    private static string InferNamespace(string directory, string testProjectPath)
    {
        var projectDir = Path.GetDirectoryName(testProjectPath)!;
        var projectName = Path.GetFileNameWithoutExtension(testProjectPath);
        var relative = Path.GetRelativePath(projectDir, directory);

        if (relative == ".")
            return projectName;

        var nsParts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => p != "." && p != "..");

        return $"{projectName}.{string.Join('.', nsParts)}";
    }
}
