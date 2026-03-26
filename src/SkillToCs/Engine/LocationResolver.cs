using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SkillToCs.Engine;

public sealed record InsertionPoint(
    string FilePath,
    int Line,
    int Column,
    string? AfterMarker = null,
    string? BeforeMarker = null);

public static class LocationResolver
{
    public static InsertionPoint FindEndOfBlock(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        var filePath = span.Path;
        var endLine = span.EndLinePosition.Line + 1; // 1-based

        // Find the closing brace token
        var closeBrace = node.ChildTokens()
            .LastOrDefault(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CloseBraceToken));

        if (closeBrace != default)
        {
            var braceSpan = closeBrace.GetLocation().GetLineSpan();
            endLine = braceSpan.StartLinePosition.Line + 1;
        }

        return new InsertionPoint(filePath, endLine, 0);
    }

    public static InsertionPoint? FindInsertionPointAfterLast(
        string filePath,
        Func<SyntaxNode, bool> predicate,
        ProjectContext ctx)
    {
        var tree = ctx.GetSyntaxTree(filePath);
        var root = tree.GetRoot();

        var lastMatch = root.DescendantNodes().LastOrDefault(predicate);
        if (lastMatch is null)
            return null;

        var span = lastMatch.GetLocation().GetLineSpan();
        var line = span.EndLinePosition.Line + 1; // 1-based, after the node

        return new InsertionPoint(filePath, line + 1, 0);
    }

    public static InsertionPoint? FindInsertionPointInMethod(
        string filePath,
        string methodName,
        ProjectContext ctx)
    {
        var tree = ctx.GetSyntaxTree(filePath);
        var root = tree.GetRoot();

        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);

        if (method?.Body is null)
            return null;

        return FindEndOfBlock(method.Body);
    }
}
