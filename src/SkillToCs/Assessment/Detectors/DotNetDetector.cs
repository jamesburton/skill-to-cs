using System.Xml.Linq;
using SkillToCs.Models;

namespace SkillToCs.Assessment.Detectors;

public sealed class DotNetDetector : IDetector
{
    public string Name => "dotnet";
    public int Priority => 10;

    public async Task<Detection?> DetectAsync(string rootPath, CancellationToken ct)
    {
        var csprojFiles = Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Length == 0)
            return null;

        string? framework = null;
        string? sdk = null;
        var nullableEnabled = false;
        var implicitUsings = false;
        var enforceCodeStyle = false;
        var analyzers = new List<string>();

        foreach (var csproj in csprojFiles)
        {
            ct.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(csproj, ct);

            try
            {
                var doc = XDocument.Parse(content);
                var root = doc.Root;
                if (root is null) continue;

                // Extract SDK type from Project element
                var sdkAttr = root.Attribute("Sdk")?.Value;
                if (sdkAttr is not null && sdk is null)
                {
                    sdk = sdkAttr switch
                    {
                        "Microsoft.NET.Sdk.Web" => "Web",
                        "Microsoft.NET.Sdk.Worker" => "Worker",
                        "Microsoft.NET.Sdk.BlazorWebAssembly" => "BlazorWasm",
                        "Microsoft.NET.Sdk" => "Library",
                        _ => sdkAttr
                    };
                }

                foreach (var pg in root.Descendants("PropertyGroup"))
                {
                    var tf = pg.Element("TargetFramework")?.Value;
                    if (tf is not null) framework ??= tf;

                    if (pg.Element("Nullable")?.Value is "enable") nullableEnabled = true;
                    if (pg.Element("ImplicitUsings")?.Value is "enable") implicitUsings = true;
                    if (pg.Element("EnforceCodeStyleInBuild")?.Value is "true") enforceCodeStyle = true;
                }

                var knownAnalyzers = new[]
                {
                    "StyleCop.Analyzers", "Roslynator.Analyzers",
                    "SonarAnalyzer.CSharp", "Microsoft.CodeAnalysis.NetAnalyzers",
                    "Meziantou.Analyzer"
                };

                foreach (var pkgRef in root.Descendants("PackageReference"))
                {
                    var include = pkgRef.Attribute("Include")?.Value;
                    if (include is not null && knownAnalyzers.Any(a =>
                            include.Contains(a, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!analyzers.Contains(include))
                            analyzers.Add(include);
                    }
                }
            }
            catch
            {
                // Skip malformed csproj
            }
        }

        var properties = new Dictionary<string, object>
        {
            ["framework"] = framework ?? "unknown",
            ["sdk"] = sdk ?? "unknown",
            ["nullable"] = nullableEnabled,
            ["implicitUsings"] = implicitUsings,
            ["enforceCodeStyle"] = enforceCodeStyle,
            ["analyzers"] = analyzers,
            ["projectCount"] = csprojFiles.Length
        };

        var opportunities = new List<ScriptOpportunity>
        {
            new("build-check", "Run dotnet build and verify zero warnings/errors",
                "verification", csprojFiles, ScriptCapability.Check)
        };

        if (analyzers.Count > 0)
        {
            opportunities.Add(new("analyzer-check",
                "Verify analyzer rules are configured and passing",
                "verification", csprojFiles, ScriptCapability.Check));
        }

        return new Detection(Name, "build-system", properties, opportunities);
    }
}
