using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SkillToCs.Engine;
using SkillToCs.Models;

namespace SkillToCs.Rules.Generation;

public sealed class ApiEndpointRule : IRule
{
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    private static readonly string[] MapMethodNames = ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete"];
    private static readonly string[] DefaultErrorCodes = ["400", "404", "500"];

    public string Name => "api-endpoint";
    public string Description => "Generates minimal API endpoint mappings with DTOs, authorization, and typed results.";
    public string Category => "api";
    public RuleSubtype Subtype => RuleSubtype.Generation;
    public HeuristicPolicy HeuristicPolicy => HeuristicPolicy.Default;

    public RuleSchema Describe() => new(
        Name,
        Description,
        Parameters:
        [
            new ParameterDef("rootPath", new ParamType.StringType(), Required: true,
                Description: "API area path, e.g. \"/api/users\""),
            new ParameterDef("method", new ParamType.EnumType(HttpMethods), Required: false,
                DefaultValue: "GET", Description: "HTTP method"),
            new ParameterDef("path", new ParamType.StringType(), Required: true,
                Description: "Route template, e.g. \"/{id:int}\""),
            new ParameterDef("queryParameters", new ParamType.ArrayType(new ParamType.StringType()), Required: false,
                DefaultValue: Array.Empty<string>(), Description: "Query parameters, e.g. [\"page:int\", \"search:string\"]"),
            new ParameterDef("requestModel", new ParamType.StringType(), Required: false,
                Description: "Input DTO name, optionally with properties e.g. \"CreateUserRequest { string Name, string Email }\""),
            new ParameterDef("responseModel", new ParamType.StringType(), Required: false,
                Description: "Output DTO name"),
            new ParameterDef("roles", new ParamType.ArrayType(new ParamType.StringType()), Required: false,
                DefaultValue: Array.Empty<string>(), Description: "Required authorization roles"),
            new ParameterDef("errorCodes", new ParamType.ArrayType(new ParamType.StringType()), Required: false,
                DefaultValue: DefaultErrorCodes, Description: "HTTP error status codes to include in result type")
        ],
        Blocks:
        [
            new BlockRule("method == 'GET' && requestModel != null", "GET endpoints should not have a request body",
                BlockSeverity.Error),
            new BlockRule("method == 'DELETE' && responseModel != null",
                "DELETE endpoints typically return NoContent", BlockSeverity.Warning)
        ],
        Examples:
        [
            new RuleExample("GetUserById", "Fetch a single user by ID", new Dictionary<string, object?>
            {
                ["rootPath"] = "/api/users",
                ["method"] = "GET",
                ["path"] = "/{id:int}",
                ["responseModel"] = "UserDto",
                ["errorCodes"] = new[] { "404" }
            }),
            new RuleExample("CreateUser", "Create a new user", new Dictionary<string, object?>
            {
                ["rootPath"] = "/api/users",
                ["method"] = "POST",
                ["path"] = "/",
                ["requestModel"] = "CreateUserRequest { string Name, string Email }",
                ["responseModel"] = "UserDto",
                ["roles"] = new[] { "Admin" },
                ["errorCodes"] = new[] { "400", "409" }
            }),
            new RuleExample("SearchUsers", "Search users with pagination", new Dictionary<string, object?>
            {
                ["rootPath"] = "/api/users",
                ["method"] = "GET",
                ["path"] = "/",
                ["queryParameters"] = new[] { "page:int", "search:string" },
                ["responseModel"] = "UserDto[]"
            })
        ]);

    public bool AppliesTo(ProjectContext ctx)
    {
        // Check if any .csproj references Microsoft.NET.Sdk.Web
        foreach (var proj in ctx.CSharpProjects)
        {
            var content = File.ReadAllText(proj);
            if (content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Fallback: check if any .cs file contains MapGet/MapPost/etc.
        foreach (var file in ctx.SourceFiles)
        {
            var tree = ctx.GetSyntaxTree(file);
            var root = tree.GetRoot();
            var hasMapCall = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => inv.Expression is MemberAccessExpressionSyntax ma &&
                            MapMethodNames.Contains(ma.Name.Identifier.Text));

            if (hasMapCall)
                return true;
        }

        return false;
    }

    public async Task<IReadOnlyList<ScannedInstance>> ScanAsync(ProjectContext ctx, CancellationToken ct)
    {
        var instances = new List<ScannedInstance>();

        var endpointFiles = ctx.FindFiles("**/*Endpoints.cs");

        foreach (var file in endpointFiles)
        {
            ct.ThrowIfCancellationRequested();

            var tree = ctx.GetSyntaxTree(file);
            var root = tree.GetRoot();

            var invocations = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(inv => inv.Expression is MemberAccessExpressionSyntax ma &&
                              MapMethodNames.Contains(ma.Name.Identifier.Text));

            foreach (var invocation in invocations)
            {
                var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
                var methodName = memberAccess.Name.Identifier.Text;
                var httpMethod = methodName.Replace("Map", "").ToUpperInvariant();

                // Extract route template from first argument
                var args = invocation.ArgumentList.Arguments;
                var routeTemplate = args.Count > 0
                    ? ExtractStringLiteral(args[0].Expression)
                    : "/";

                // Extract query/route parameters from the delegate if present
                var queryParams = new List<string>();
                var delegateArg = args.Count > 1 ? args[1].Expression : null;
                if (delegateArg is ParenthesizedLambdaExpressionSyntax lambda && lambda.ParameterList is not null)
                {
                    foreach (var param in lambda.ParameterList.Parameters)
                    {
                        if (param.AttributeLists.Any(al => al.Attributes.Any(a =>
                                a.Name.ToString().Contains("FromQuery"))))
                        {
                            var paramType = param.Type?.ToString() ?? "string";
                            queryParams.Add($"{param.Identifier.Text}:{paramType}");
                        }
                    }
                }

                var lineSpan = invocation.GetLocation().GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                var paramsDict = new Dictionary<string, object?>
                {
                    ["method"] = httpMethod,
                    ["path"] = routeTemplate
                };

                if (queryParams.Count > 0)
                    paramsDict["queryParameters"] = queryParams.ToArray();

                var displayLabel = $"{httpMethod} {routeTemplate}";

                instances.Add(new ScannedInstance(Name, file, line, paramsDict, displayLabel));
            }
        }

        return await Task.FromResult<IReadOnlyList<ScannedInstance>>(instances);
    }

    public async Task<GenerationResult> GenerateAsync(RuleParams parameters, ProjectContext ctx, CancellationToken ct)
    {
        var rootPath = parameters.Get<string>("rootPath") ?? "";
        var method = parameters.Get<string>("method") ?? "GET";
        var path = parameters.Get<string>("path") ?? "/";
        var queryParameters = parameters.Get<string[]>("queryParameters") ?? [];
        var requestModel = parameters.Get<string>("requestModel");
        var responseModel = parameters.Get<string>("responseModel");
        var roles = parameters.Get<string[]>("roles") ?? [];
        var errorCodes = parameters.Get<string[]>("errorCodes") ?? DefaultErrorCodes;

        var fullPath = NormalizePath(rootPath + path);
        var groupSegment = rootPath.TrimStart('/');
        var lastSegment = rootPath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Default";
        var className = PascalCase(lastSegment) + "Endpoints";
        var fileName = className + ".cs";

        var fragments = new List<CodeFragment>();
        var inferences = new List<InferenceNote>();
        var questions = new List<InputQuestion>();

        // Resolve request model
        string? requestModelName = null;
        string? requestModelRecord = null;
        if (!string.IsNullOrEmpty(requestModel))
        {
            var (name, props) = ParseModelSpec(requestModel);
            requestModelName = name;

            if (props.Count > 0)
            {
                requestModelRecord = RenderDtoRecord(name, props);
            }
            else if (!ctx.TypeExists(name))
            {
                questions.Add(new InputQuestion(
                    "requestModel",
                    $"Type '{name}' does not exist. Please provide properties for the request model.",
                    $"e.g. \"{name} {{ string Name, string Email }}\"",
                    null));
            }
        }

        // Resolve response model
        string? responseModelName = null;
        string? responseModelRecord = null;
        if (!string.IsNullOrEmpty(responseModel))
        {
            var isCollection = responseModel.EndsWith("[]");
            var baseName = isCollection ? responseModel[..^2] : responseModel;
            responseModelName = responseModel;

            if (baseName.Contains('{'))
            {
                var (name, props) = ParseModelSpec(baseName);
                responseModelName = isCollection ? name + "[]" : name;
                responseModelRecord = RenderDtoRecord(name, props);
            }
            else if (!ctx.TypeExists(baseName))
            {
                questions.Add(new InputQuestion(
                    "responseModel",
                    $"Type '{baseName}' does not exist. Please provide properties for the response model.",
                    $"e.g. \"{baseName} {{ int Id, string Name }}\"",
                    null));
            }
        }

        if (questions.Count > 0)
            return GenerationResult.NeedInput(questions);

        // Idempotency check
        var idempotencyKey = $"Map{method}(\"{fullPath}\"";

        // Find existing file or plan new file
        var existingFile = ctx.FindFile($"**/{fileName}");
        if (existingFile is not null)
        {
            var existingContent = File.ReadAllText(existingFile);
            if (existingContent.Contains(idempotencyKey, StringComparison.Ordinal))
            {
                inferences.Add(new InferenceNote(
                    $"Skipped duplicate endpoint {method} {fullPath}",
                    1.0,
                    "Endpoint mapping already exists in file."));

                return GenerationResult.Success(fragments, inferences);
            }

            // Insert into existing file after last MapXxx call
            var insertionPoint = LocationResolver.FindInsertionPointAfterLast(
                existingFile,
                node => node is InvocationExpressionSyntax inv &&
                        inv.Expression is MemberAccessExpressionSyntax ma &&
                        MapMethodNames.Contains(ma.Name.Identifier.Text),
                ctx);

            var mappingCode = RenderEndpointMapping(method, path, queryParameters, requestModelName,
                responseModelName, roles, errorCodes);

            if (insertionPoint is not null)
            {
                fragments.Add(new CodeFragment(
                    existingFile,
                    insertionPoint,
                    mappingCode,
                    FragmentType.InsertAfter,
                    idempotencyKey));
            }

            inferences.Add(new InferenceNote(
                $"Appended {method} {fullPath} to existing {fileName}",
                0.95,
                "Found existing endpoints file; inserted after last mapping."));
        }
        else
        {
            // Generate new file
            var endpointsDir = ctx.ResolveDirectory("Endpoints")
                               ?? Path.Combine(ctx.RootPath, "Endpoints");

            var targetFile = Path.Combine(endpointsDir, fileName);

            var fileContent = RenderEndpointFile(className, groupSegment, method, path,
                queryParameters, requestModelName, responseModelName, roles, errorCodes);

            fragments.Add(new CodeFragment(
                targetFile,
                null,
                fileContent,
                FragmentType.NewFile,
                idempotencyKey));

            inferences.Add(new InferenceNote(
                $"Created new {fileName} with {method} {fullPath}",
                0.90,
                "No existing endpoints file found; generated complete file with MapGroup."));
        }

        // Add DTO record fragments if needed
        if (requestModelRecord is not null)
        {
            var dtoDir = ctx.ResolveDirectory("Models")
                         ?? Path.Combine(ctx.RootPath, "Models");
            var dtoFile = Path.Combine(dtoDir, requestModelName + ".cs");

            fragments.Add(new CodeFragment(
                dtoFile,
                null,
                requestModelRecord,
                FragmentType.NewFile,
                $"record:{requestModelName}"));
        }

        if (responseModelRecord is not null)
        {
            var baseName = responseModelName?.EndsWith("[]") == true
                ? responseModelName[..^2]
                : responseModelName;
            var dtoDir = ctx.ResolveDirectory("Models")
                         ?? Path.Combine(ctx.RootPath, "Models");
            var dtoFile = Path.Combine(dtoDir, baseName + ".cs");

            fragments.Add(new CodeFragment(
                dtoFile,
                null,
                responseModelRecord,
                FragmentType.NewFile,
                $"record:{baseName}"));
        }

        return await Task.FromResult(GenerationResult.Success(fragments, inferences));
    }

    public async Task<VerificationResult> VerifyAsync(ProjectContext ctx, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var violations = new List<Violation>();
        var inferences = new List<InferenceNote>();
        var filesChecked = 0;
        var passed = 0;
        var failed = 0;

        var endpointFiles = ctx.FindFiles("**/*Endpoints.cs");

        foreach (var file in endpointFiles)
        {
            ct.ThrowIfCancellationRequested();
            filesChecked++;

            var tree = ctx.GetSyntaxTree(file);
            var root = tree.GetRoot();

            // Check naming convention
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
            if (fileNameWithoutExt != PascalCase(fileNameWithoutExt))
            {
                violations.Add(new Violation(file, null, "ApiEndpoint.Naming",
                    $"Endpoints file name '{fileNameWithoutExt}' should be PascalCase.",
                    ViolationSeverity.Warning, Fixable: true));
            }

            var invocations = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(inv => inv.Expression is MemberAccessExpressionSyntax ma &&
                              MapMethodNames.Contains(ma.Name.Identifier.Text))
                .ToList();

            foreach (var invocation in invocations)
            {
                var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
                var methodName = memberAccess.Name.Identifier.Text;
                var httpMethod = methodName.Replace("Map", "").ToUpperInvariant();
                var lineSpan = invocation.GetLocation().GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                // Check: GET with request body (look for [FromBody] in lambda)
                if (httpMethod == "GET")
                {
                    var args = invocation.ArgumentList.Arguments;
                    var delegateArg = args.Count > 1 ? args[1].Expression : null;
                    if (HasFromBodyParameter(delegateArg))
                    {
                        violations.Add(new Violation(file, line, "ApiEndpoint.GetWithBody",
                            "GET endpoints should not have a request body.",
                            ViolationSeverity.Error, Fixable: false));
                        failed++;
                        continue;
                    }
                }

                // Check: DELETE with response model (has typed Ok<T> result)
                if (httpMethod == "DELETE")
                {
                    var fullText = invocation.ToFullString();
                    if (Regex.IsMatch(fullText, @"Ok<\w+>"))
                    {
                        violations.Add(new Violation(file, line, "ApiEndpoint.DeleteWithResponse",
                            "DELETE endpoints typically return NoContent.",
                            ViolationSeverity.Warning, Fixable: true));
                    }
                }

                passed++;
            }
        }

        sw.Stop();

        var status = violations.Any(v => v.Severity == ViolationSeverity.Error)
            ? VerificationStatus.Fail
            : VerificationStatus.Pass;

        return await Task.FromResult(new VerificationResult(
            Name,
            status,
            violations,
            inferences,
            new VerificationStats(filesChecked, passed, failed, sw.Elapsed)));
    }

    // --- Private helpers ---

    private static string RenderEndpointFile(
        string className, string groupSegment, string method, string path,
        string[] queryParameters, string? requestModel, string? responseModel,
        string[] roles, string[] errorCodes)
    {
        var mapping = RenderEndpointMapping(method, path, queryParameters, requestModel, responseModel, roles,
            errorCodes);

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Http.HttpResults;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ExtractNamespaceFromGroup(groupSegment)};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");
        sb.AppendLine($"    public static RouteGroupBuilder Map{className}(this IEndpointRouteBuilder routes)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var group = routes.MapGroup(\"/{groupSegment}\");");
        sb.AppendLine();
        sb.Append(IndentBlock(mapping, "        "));
        sb.AppendLine();
        sb.AppendLine("        return group;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string RenderEndpointMapping(
        string method, string path, string[] queryParameters, string? requestModel,
        string? responseModel, string[] roles, string[] errorCodes)
    {
        var sb = new StringBuilder();

        // Build handler parameter list
        var handlerParams = new List<string>();
        var routeParams = ExtractRouteParams(path);
        foreach (var (name, type) in routeParams)
            handlerParams.Add($"{type} {name}");

        foreach (var qp in queryParameters)
        {
            var (name, type) = ParseParamSpec(qp);
            handlerParams.Add($"[AsParameters] {type} {name}");
        }

        if (requestModel is not null)
            handlerParams.Add($"{requestModel} request");

        // Build result type
        var resultType = BuildResultType(errorCodes, responseModel, method);

        // Method name for map call
        var mapMethod = $"Map{PascalCase(method.ToLowerInvariant())}";
        var routePath = NormalizePath(path);

        sb.Append($"group.{mapMethod}(\"{routePath}\", ({string.Join(", ", handlerParams)}) =>");

        // Handler body
        sb.AppendLine();
        sb.AppendLine("{");

        if (method == "DELETE")
        {
            sb.AppendLine("    return TypedResults.NoContent();");
        }
        else if (responseModel is not null)
        {
            sb.AppendLine($"    // TODO: Implement {method} logic");
            sb.AppendLine($"    throw new NotImplementedException();");
        }
        else
        {
            sb.AppendLine($"    // TODO: Implement {method} logic");
            sb.AppendLine($"    return TypedResults.Ok();");
        }

        sb.Append("})");

        // Fluent chain
        if (!string.IsNullOrEmpty(resultType))
            sb.Append($"\n    .Produces<{resultType}>(StatusCodes.Status200OK)");

        if (roles.Length > 0)
        {
            foreach (var role in roles)
                sb.Append($"\n    .RequireAuthorization(\"{role}\")");
        }

        sb.Append(';');

        return sb.ToString();
    }

    private static string RenderDtoRecord(string name, List<(string Type, string Name)> properties)
    {
        var propsStr = string.Join(", ", properties.Select(p => $"{p.Type} {p.Name}"));

        var sb = new StringBuilder();
        sb.AppendLine($"namespace Models;");
        sb.AppendLine();
        sb.AppendLine($"public record {name}({propsStr});");

        return sb.ToString();
    }

    private static string BuildResultType(string[] errorCodes, string? responseModel, string method)
    {
        var types = new List<string>();

        if (method == "DELETE")
        {
            types.Add("NoContent");
        }
        else if (responseModel is not null)
        {
            var isCollection = responseModel.EndsWith("[]");
            var baseName = isCollection ? responseModel[..^2] : responseModel;
            var modelRef = isCollection ? $"List<{baseName}>" : baseName;
            types.Add($"Ok<{modelRef}>");
        }
        else
        {
            types.Add("Ok");
        }

        foreach (var code in errorCodes)
        {
            types.Add(code switch
            {
                "400" => "BadRequest",
                "401" => "UnauthorizedHttpResult",
                "403" => "ForbidHttpResult",
                "404" => "NotFound",
                "409" => "Conflict",
                "422" => "UnprocessableEntity",
                "500" => "ProblemHttpResult",
                _ => $"StatusCodeHttpResult"
            });
        }

        return types.Count switch
        {
            0 => "",
            1 => types[0],
            _ => $"Results<{string.Join(", ", types)}>"
        };
    }

    private static string PascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Handle already PascalCase
        if (char.IsUpper(input[0]) && !input.Contains('-') && !input.Contains('_'))
            return input;

        var parts = input.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    private static List<(string Name, string Type)> ExtractRouteParams(string path)
    {
        var results = new List<(string Name, string Type)>();
        var matches = Regex.Matches(path, @"\{(\w+)(?::(\w+))?\}");

        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            var constraint = match.Groups[2].Success ? match.Groups[2].Value : "string";
            var csharpType = constraint switch
            {
                "int" => "int",
                "long" => "long",
                "guid" or "Guid" => "Guid",
                "bool" => "bool",
                "decimal" => "decimal",
                "double" => "double",
                "float" => "float",
                "datetime" => "DateTime",
                _ => "string"
            };

            results.Add((name, csharpType));
        }

        return results;
    }

    private static (string Name, List<(string Type, string Name)> Properties) ParseModelSpec(string spec)
    {
        var braceIndex = spec.IndexOf('{');
        if (braceIndex < 0)
            return (spec.Trim(), []);

        var name = spec[..braceIndex].Trim();
        var propsSection = spec[(braceIndex + 1)..].TrimEnd('}', ' ');
        var properties = new List<(string Type, string Name)>();

        var propParts = propsSection.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in propParts)
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2)
                properties.Add((tokens[0], tokens[1]));
        }

        return (name, properties);
    }

    private static (string Name, string Type) ParseParamSpec(string spec)
    {
        var parts = spec.Split(':', 2, StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? (parts[0], parts[1]) : (parts[0], "string");
    }

    private static string ExtractStringLiteral(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) =>
                literal.Token.ValueText,
            InterpolatedStringExpressionSyntax interpolated => interpolated.ToString(),
            _ => expression.ToString().Trim('"')
        };
    }

    private static bool HasFromBodyParameter(ExpressionSyntax? expression)
    {
        if (expression is ParenthesizedLambdaExpressionSyntax lambda && lambda.ParameterList is not null)
        {
            return lambda.ParameterList.Parameters.Any(p =>
                p.AttributeLists.Any(al =>
                    al.Attributes.Any(a => a.Name.ToString().Contains("FromBody"))));
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        path = path.Replace("//", "/");
        if (!path.StartsWith('/'))
            path = "/" + path;
        if (path.Length > 1 && path.EndsWith('/'))
            path = path[..^1];
        return path;
    }

    private static string ExtractNamespaceFromGroup(string groupSegment)
    {
        var parts = groupSegment.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(".", parts.Select(PascalCase));
    }

    private static string IndentBlock(string block, string indent)
    {
        var lines = block.Split('\n');
        return string.Join('\n', lines.Select(line =>
            string.IsNullOrWhiteSpace(line) ? line : indent + line));
    }
}
