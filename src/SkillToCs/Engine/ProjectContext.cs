using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace SkillToCs.Engine;

public sealed class ProjectContext
{
    private readonly Lazy<IReadOnlyList<string>> _csharpProjects;
    private readonly Lazy<IReadOnlyList<string>> _sourceFiles;
    private readonly ConcurrentDictionary<string, SyntaxTree> _syntaxTreeCache = new(StringComparer.OrdinalIgnoreCase);

    public ProjectContext(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);

        _csharpProjects = new Lazy<IReadOnlyList<string>>(() =>
            FindFiles("**/*.csproj"));

        _sourceFiles = new Lazy<IReadOnlyList<string>>(() =>
            FindFiles("**/*.cs"));
    }

    public string RootPath { get; }

    public IReadOnlyList<string> CSharpProjects => _csharpProjects.Value;

    public IReadOnlyList<string> SourceFiles => _sourceFiles.Value;

    public SyntaxTree GetSyntaxTree(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return _syntaxTreeCache.GetOrAdd(fullPath, static path =>
        {
            var text = File.ReadAllText(path);
            return CSharpSyntaxTree.ParseText(text, path: path);
        });
    }

    public string? FindFile(string globPattern)
    {
        var results = MatchGlob(globPattern);
        return results.FirstOrDefault();
    }

    public IReadOnlyList<string> FindFiles(string globPattern) =>
        MatchGlob(globPattern);

    public bool TypeExists(string typeName) =>
        FindTypeFile(typeName) is not null;

    public string? FindTypeFile(string typeName)
    {
        foreach (var file in SourceFiles)
        {
            var tree = GetSyntaxTree(file);
            var root = tree.GetRoot();

            var hasType = root.DescendantNodes().Any(n => n switch
            {
                ClassDeclarationSyntax c => c.Identifier.Text == typeName,
                RecordDeclarationSyntax r => r.Identifier.Text == typeName,
                StructDeclarationSyntax s => s.Identifier.Text == typeName,
                InterfaceDeclarationSyntax i => i.Identifier.Text == typeName,
                EnumDeclarationSyntax e => e.Identifier.Text == typeName,
                _ => false
            });

            if (hasType)
                return file;
        }

        return null;
    }

    public string? ResolveDirectory(string conventionName)
    {
        // Search for a directory matching the convention name under root
        var candidate = Path.Combine(RootPath, conventionName);
        if (Directory.Exists(candidate))
            return candidate;

        // Try scanning subdirectories for a match
        try
        {
            return Directory.EnumerateDirectories(RootPath, conventionName, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private List<string> MatchGlob(string globPattern)
    {
        var matcher = new Matcher();
        matcher.AddInclude(globPattern);

        var directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(RootPath));
        var result = matcher.Execute(directoryInfo);

        return result.Files
            .Select(f => Path.GetFullPath(Path.Combine(RootPath, f.Path)))
            .ToList();
    }
}
