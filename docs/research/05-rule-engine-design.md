# Rule Engine Design: Parameterised, Bidirectional Skills

> Refinement of the core architecture — skills as constrained, idempotent generation rules

---

## The Shift

The original plan treated skills as **verification scripts** — they check things. This refinement makes skills **bidirectional rules** that can:

1. **Scan** — read existing code, extract instances as parameter sets
2. **Describe** — show the parameter schema (what inputs are needed)
3. **Generate** — given parameters, produce code in the right places in the right files
4. **Verify** — check that existing code conforms to the rule

The key insight: **once you constrain a skill to prescriptive parameters with defaults and constraints, generation becomes idempotent.** Same inputs → same output → same file locations → deterministic.

---

## Anatomy of a Rule

### The Endpoint Example

A rule for "add an API endpoint" takes structured parameters:

```json
{
  "rule": "api-endpoint",
  "parameters": {
    "rootPath": { "type": "string", "required": true, "description": "API area (e.g. /api/users)" },
    "method": { "type": "enum", "values": ["GET", "POST", "PUT", "PATCH", "DELETE"], "default": "GET" },
    "path": { "type": "string", "required": true, "description": "Route path with parameters (e.g. /{id})" },
    "queryParameters": { "type": "array", "items": "QueryParam", "default": [] },
    "requestModel": { "type": "string", "required": false, "description": "Input DTO type name" },
    "responseModel": { "type": "string", "required": false, "description": "Output DTO type name (omit for 200 OK)" },
    "roles": { "type": "array", "items": "string", "default": [] },
    "errorCodes": { "type": "array", "items": "ErrorCode", "default": ["400", "404", "500"] }
  }
}
```

### What the Rule Knows

Given those parameters, the rule can deterministically resolve:

| Decision | How It's Resolved |
|----------|------------------|
| **Which file** | `rootPath` → match to extensions file by path prefix (e.g. `/api/users` → `UsersEndpoints.cs`) |
| **Which group** | Next path segment after root → endpoint group within the file |
| **Which method** | `method` parameter → `app.MapGet(...)`, `app.MapPost(...)`, etc. |
| **Route** | `path` with route parameters extracted → route template string |
| **Input binding** | Route params + query params → parameter list; `requestModel` → `[FromBody]` parameter |
| **Output type** | `responseModel` present → `TypedResults.Ok<T>()`, absent → `Results.Ok()` |
| **Auth** | `roles` → `.RequireAuthorization(policy)` chain |
| **Error handling** | `errorCodes` → result union type `Results<Ok<T>, BadRequest, NotFound>` |

### Defaults and Blocks

**Defaults** reduce what must be specified:
- `method` defaults to `GET`
- `errorCodes` defaults to `[400, 404, 500]`
- `queryParameters` defaults to `[]`
- Auth defaults to none (no roles = anonymous)

**Blocks** are constraints that prevent invalid combinations:
- `GET` + `requestModel` → block (GET shouldn't have a body)
- `DELETE` + `responseModel` → warn (DELETE typically returns no content)
- Route parameter in `path` without matching query/route param definition → block

---

## Rule Lifecycle

```
┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
│ DESCRIBE  │────→│   SCAN   │────→│ GENERATE │────→│  VERIFY  │
│           │     │          │     │          │     │          │
│ Show      │     │ Read     │     │ Apply    │     │ Check    │
│ parameter │     │ existing │     │ params   │     │ existing │
│ schema    │     │ code,    │     │ to code, │     │ matches  │
│ with      │     │ return   │     │ insert   │     │ rule     │
│ defaults, │     │ instances│     │ in right │     │ shape    │
│ blocks,   │     │ as param │     │ places   │     │          │
│ examples  │     │ sets     │     │          │     │          │
└──────────┘     └──────────┘     └──────────┘     └──────────┘
        ↑                │                                │
        │                ↓                                │
        │    ┌──────────────────────┐                     │
        └────│ INTERACTIVE SURFACE  │←────────────────────┘
             │                      │
             │ View existing        │
             │ instances, clone     │
             │ as template for new, │
             │ modify params,       │
             │ regenerate           │
             └──────────────────────┘
```

### Describe Mode

The rule exposes its parameter schema in a format agents and humans can consume:

```
skill-to-cs describe api-endpoint --json
```

```json
{
  "rule": "api-endpoint",
  "description": "Add or verify a Minimal API endpoint",
  "parameters": { ... },
  "defaults": { "method": "GET", "errorCodes": ["400", "404", "500"] },
  "blocks": [
    { "condition": "method == 'GET' && requestModel != null", "message": "GET endpoints should not have a request body" }
  ],
  "examples": [
    {
      "name": "Simple GET",
      "params": { "rootPath": "/api/users", "method": "GET", "path": "/{id}", "responseModel": "UserDto" }
    }
  ]
}
```

### Scan Mode

The rule reads existing code and extracts instances as parameter sets — **the reverse of generation**:

```
skill-to-cs scan api-endpoint --json
```

```json
{
  "rule": "api-endpoint",
  "instances": [
    {
      "file": "src/Api/Endpoints/UsersEndpoints.cs",
      "line": 14,
      "params": {
        "rootPath": "/api/users",
        "method": "GET",
        "path": "/{id:int}",
        "queryParameters": [],
        "requestModel": null,
        "responseModel": "UserDto",
        "roles": ["admin", "user"],
        "errorCodes": ["404"]
      }
    },
    {
      "file": "src/Api/Endpoints/UsersEndpoints.cs",
      "line": 28,
      "params": {
        "rootPath": "/api/users",
        "method": "POST",
        "path": "/",
        "requestModel": "CreateUserRequest",
        "responseModel": "UserDto",
        "roles": ["admin"],
        "errorCodes": ["400", "409"]
      }
    }
  ]
}
```

This is the **interactive surface** — an agent or human sees what exists, picks an instance as a template, modifies parameters, and generates a new one.

### Generate Mode

Given parameters, produce code and insert it in the right places:

```
skill-to-cs generate api-endpoint \
  --rootPath /api/users \
  --method PUT \
  --path "/{id:int}" \
  --requestModel UpdateUserRequest \
  --responseModel UserDto \
  --roles admin \
  --errorCodes 400,404
```

The rule:
1. Finds `UsersEndpoints.cs` (matched by `rootPath` prefix)
2. Locates the endpoint group section
3. Inserts a new `MapPut` registration in the right position (ordered by method)
4. Generates the handler delegate with proper parameter binding
5. If `UpdateUserRequest` doesn't exist, scaffolds the DTO file
6. If `UserDto` already exists, references it; if not, scaffolds it

**Idempotent:** Running the same command again detects the endpoint already exists (by method + path match) and either skips or reports "already exists."

### Verify Mode

Check that existing instances conform to the rule's constraints:

```
skill-to-cs verify api-endpoint --json
```

```json
{
  "rule": "api-endpoint",
  "status": "fail",
  "violations": [
    {
      "file": "src/Api/Endpoints/OrdersEndpoints.cs",
      "line": 42,
      "instance": { "method": "GET", "path": "/search" },
      "violation": "GET endpoint has [FromBody] parameter (requestModel bound)",
      "block": "method == 'GET' && requestModel != null"
    }
  ]
}
```

---

## Rule Definition Format

Each rule is a `.cs` file (or a set of them) that implements a standard interface. The rule definition combines:

### 1. Parameter Schema

```csharp
public class ApiEndpointRule : IRule
{
    public string Name => "api-endpoint";
    public string Description => "Add or verify a Minimal API endpoint";

    public RuleSchema Schema => new()
    {
        Parameters =
        {
            new("rootPath", ParamType.String, required: true,
                description: "API area (e.g., /api/users)"),
            new("method", ParamType.Enum("GET", "POST", "PUT", "PATCH", "DELETE"),
                defaultValue: "GET"),
            new("path", ParamType.String, required: true,
                description: "Route template (e.g., /{id:int})"),
            new("queryParameters", ParamType.Array(ParamType.Object(
                ("name", ParamType.String),
                ("type", ParamType.String),
                ("required", ParamType.Bool)
            )), defaultValue: Array.Empty<object>()),
            new("requestModel", ParamType.String, required: false),
            new("responseModel", ParamType.String, required: false),
            new("roles", ParamType.Array(ParamType.String), defaultValue: Array.Empty<string>()),
            new("errorCodes", ParamType.Array(ParamType.String),
                defaultValue: new[] { "400", "404", "500" }),
        },
        Blocks =
        {
            new("method == 'GET' && requestModel != null",
                "GET endpoints should not have a request body"),
            new("method == 'DELETE' && responseModel != null",
                "DELETE endpoints typically return NoContent", Severity.Warning),
        },
        Examples =
        {
            new("Simple GET by ID", new {
                rootPath = "/api/users", method = "GET",
                path = "/{id:int}", responseModel = "UserDto"
            }),
        }
    };
}
```

### 2. Location Resolution

How the rule finds where to put code:

```csharp
public class ApiEndpointRule : IRule
{
    public LocationResult ResolveLocation(RuleParams p, ProjectContext ctx)
    {
        // 1. Find endpoints file by rootPath prefix
        var rootSegment = p.RootPath.Split('/').Last();  // "users"
        var fileName = $"{rootSegment.ToPascalCase()}Endpoints.cs";

        // 2. Search for existing file
        var existing = ctx.FindFile($"**/{fileName}");
        if (existing == null)
        {
            // File doesn't exist — will scaffold it
            var dir = ctx.ResolveEndpointsDirectory(p.RootPath);
            return LocationResult.NewFile(Path.Combine(dir, fileName));
        }

        // 3. Find insertion point within existing file
        var groupMarker = FindMethodGroup(existing, p.Method);
        return LocationResult.InsertAfter(existing, groupMarker);
    }
}
```

### 3. Code Generation

Templates for the generated code:

```csharp
public class ApiEndpointRule : IRule
{
    public GenerationResult Generate(RuleParams p, LocationResult location)
    {
        var fragments = new List<CodeFragment>();

        // Endpoint registration
        fragments.Add(new CodeFragment(
            location.InsertionPoint,
            RenderEndpointRegistration(p)
        ));

        // Request model (if needed and doesn't exist)
        if (p.RequestModel != null && !location.Context.TypeExists(p.RequestModel))
        {
            fragments.Add(new CodeFragment(
                location.Context.ModelsDirectory,
                RenderRequestModel(p)
            ));
        }

        // Response model (if needed and doesn't exist)
        if (p.ResponseModel != null && !location.Context.TypeExists(p.ResponseModel))
        {
            fragments.Add(new CodeFragment(
                location.Context.ModelsDirectory,
                RenderResponseModel(p)
            ));
        }

        return new GenerationResult(fragments);
    }
}
```

### 4. Scanner (Reverse)

How the rule reads existing code to extract instances:

```csharp
public class ApiEndpointRule : IRule
{
    public IEnumerable<ScannedInstance> Scan(ProjectContext ctx)
    {
        foreach (var file in ctx.FindFiles("**/*Endpoints.cs"))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            var root = tree.GetRoot();

            // Find all MapXxx invocations
            var mapCalls = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(IsMapMethodCall);

            foreach (var call in mapCalls)
            {
                yield return new ScannedInstance(
                    File: file,
                    Line: call.GetLocation().GetLineSpan().StartLinePosition.Line,
                    Params: ExtractParams(call, file)
                );
            }
        }
    }
}
```

---

## Implementation Patterns — Beyond Endpoints

The rule engine is generic. The endpoint example is one rule. Others follow the same shape:

### Service Rule

```
skill-to-cs generate service \
  --name UserService \
  --interface IUserService \
  --methods "GetById(int id): UserDto?, Create(CreateUserRequest req): UserDto" \
  --lifetime Scoped \
  --inject "IUserRepository, ILogger<UserService>"
```

Resolves: service file location, interface file, DI registration in `Program.cs` or the relevant extensions class.

### MediatR Handler Rule

```
skill-to-cs generate mediatr-handler \
  --name GetUserById \
  --type Query \
  --requestModel "GetUserByIdQuery { int Id }" \
  --responseModel UserDto \
  --inject "IUserRepository, IMapper"
```

Resolves: handler file, request/response records, pipeline registration.

### MassTransit Consumer Rule

```
skill-to-cs generate masstransit-consumer \
  --name OrderCreatedConsumer \
  --message OrderCreatedEvent \
  --saga false \
  --retryPolicy "Interval(3, TimeSpan.FromSeconds(5))" \
  --inject "IOrderService, ILogger"
```

Resolves: consumer file, message contract, consumer registration in bus config.

### Mapping Rule (Composable)

For cases where input/output mapping is needed, rules can compose:

```
skill-to-cs generate api-endpoint \
  --rootPath /api/users \
  --method POST \
  --path / \
  --requestModel CreateUserApiRequest \
  --responseModel UserApiResponse \
  --mapRequest "CreateUserApiRequest -> CreateUserCommand via UserMapper.ToCommand" \
  --mapResponse "UserDto -> UserApiResponse via UserMapper.ToApiResponse"
```

Or when a common mapper convention exists:

```
skill-to-cs generate api-endpoint \
  --rootPath /api/users \
  --method POST \
  --path / \
  --apiRequest CreateUserApiRequest \
  --apiResponse UserApiResponse \
  --mapper AutoMapper
```

The rule detects the mapper convention and generates the appropriate mapping call.

---

## Composition: Rules Calling Rules

Complex operations compose simpler rules:

```
skill-to-cs generate feature \
  --name CreateUser \
  --endpoint "POST /api/users" \
  --handler MediatR \
  --requestModel CreateUserRequest \
  --responseModel UserDto
```

This **feature** meta-rule orchestrates:
1. `api-endpoint` rule → creates the endpoint
2. `mediatr-handler` rule → creates the command + handler
3. `model` rule → creates request/response DTOs
4. `mapping` rule → creates mapper if needed
5. `test` rule → scaffolds test class for the handler

Each sub-rule is independently idempotent — if the DTO already exists, it's skipped.

---

## The Interactive Surface

### View What Exists

```
skill-to-cs scan api-endpoint
```

```
  API Endpoints (12 found)

  ┌───────────────┬────────┬──────────────┬─────────────────┬─────────────────┐
  │ Root          │ Method │ Path         │ Request          │ Response        │
  ├───────────────┼────────┼──────────────┼─────────────────┼─────────────────┤
  │ /api/users    │ GET    │ /            │ —               │ List<UserDto>   │
  │ /api/users    │ GET    │ /{id:int}    │ —               │ UserDto         │
  │ /api/users    │ POST   │ /            │ CreateUserReq   │ UserDto         │
  │ /api/users    │ PUT    │ /{id:int}    │ UpdateUserReq   │ UserDto         │
  │ /api/users    │ DELETE │ /{id:int}    │ —               │ —               │
  │ /api/orders   │ GET    │ /            │ —               │ List<OrderDto>  │
  │ /api/orders   │ GET    │ /{id:guid}   │ —               │ OrderDto        │
  │ /api/orders   │ POST   │ /            │ CreateOrderReq  │ OrderDto        │
  │ ...           │        │              │                 │                 │
  └───────────────┴────────┴──────────────┴─────────────────┴─────────────────┘
```

### Clone as Template

```
skill-to-cs scan api-endpoint --instance "/api/users:POST:/" --json
```

Returns the parameter set for that specific instance. An agent or human takes that JSON, modifies it (change rootPath to `/api/products`, change models), and feeds it back to `generate`.

### Diff Preview

```
skill-to-cs generate api-endpoint --rootPath /api/products --method POST ... --dry-run
```

Shows exactly what would change:

```
  Would create: src/Api/Endpoints/ProductsEndpoints.cs (new file)
  Would create: src/Api/Models/CreateProductRequest.cs (new file)
  Would modify: src/Api/Extensions/EndpointExtensions.cs
    + line 24: app.MapProductEndpoints();
```

---

## Rule Discovery and Registration

### Built-in Rules (ship with the tool)

| Rule | Category | Scans | Generates |
|------|----------|-------|-----------|
| `api-endpoint` | API | Minimal API endpoints | Endpoint + DTOs |
| `controller-action` | API | MVC controller actions | Action + DTOs |
| `service` | Architecture | Service classes + interfaces | Service + interface + DI reg |
| `repository` | Architecture | Repository classes | Repository + interface + DI reg |
| `mediatr-handler` | CQRS | MediatR handlers | Command/Query + Handler |
| `masstransit-consumer` | Messaging | MassTransit consumers | Consumer + Message + Registration |
| `ef-entity` | Data | EF Core entities | Entity + Configuration + Migration |
| `test-class` | Testing | Test classes | Test class with fixture |
| `middleware` | Pipeline | Middleware classes | Middleware + Registration |
| `background-service` | Hosting | Hosted services | BackgroundService + Registration |

### Custom Rules (user/project defined)

Rules are `.cs` files in `.skill-to-cs/rules/` or `~/.skill-to-cs/rules/`:

```
.skill-to-cs/
├── rules/
│   ├── api-endpoint.cs        # Override built-in (project conventions)
│   └── billing-event.cs       # Custom rule for this project
├── skills/                    # Generated verification scripts
└── catalog.json
```

A custom rule can **extend** a built-in rule (add defaults, add parameters, change templates) or **replace** it entirely.

### Rule Packages (v3)

Distribute rules as NuGet packages for org-wide conventions:

```
dotnet tool install Acme.SkillToCs.Rules
```

Brings in company-specific rules for their API conventions, naming standards, etc.

---

## How This Changes the Plan

### V1 Additions

The v1 plan (phases 1-6) needs these additions:

**Phase 2 (Assessment) now also includes:**
- Scanning existing code to build the "what exists" model
- Each detector doesn't just detect opportunities — it scans instances

**Phase 3 (Generation) becomes the Rule Engine:**
- `IScriptTemplate` becomes `IRule` with the full lifecycle (describe/scan/generate/verify)
- Templates become location-aware (know where to insert code)
- Parameter schemas with defaults, blocks, and validation

**New Phase (between 3 and 4): Interactive Surface**
- `scan` command shows what exists per rule
- `describe` command shows rule parameters
- `--dry-run` on generate shows diff preview
- `--clone` extracts an existing instance as a parameter set

### V1 Scope Constraint

For v1, ship with **3 built-in rules** to prove the engine works:
1. `api-endpoint` (Minimal API) — the flagship example
2. `service` — service + interface + DI registration
3. `test-class` — test scaffolding

Plus the original verification scripts (build-check, format-check, etc.) as **verification rules** — a simpler rule subtype that only has verify mode.

### V2 Expands Rules

- Full set of 10+ built-in rules
- Custom rule authoring support
- Composition (meta-rules calling sub-rules)
- Mapping conventions (AutoMapper, manual mappers)

### V3 Adds Ecosystem

- Rule packages via NuGet
- Community rule sharing
- MCP bridge exposing rules as tools

---

## Token Efficiency of This Approach

An agent using skill-to-cs to add an endpoint:

**Without skill-to-cs (current state):**
1. Agent reads CLAUDE.md for conventions (~500 tokens)
2. Agent reads existing endpoint file to understand pattern (~1000 tokens)
3. Agent reads models directory to understand DTO patterns (~500 tokens)
4. Agent reads DI registration to understand registration pattern (~300 tokens)
5. Agent generates code, possibly gets it wrong, iterates (~2000 tokens output)
6. **Total: ~4300+ tokens, non-deterministic**

**With skill-to-cs:**
1. Agent runs `skill-to-cs scan api-endpoint --json` → reads instance list (~200 tokens)
2. Agent runs `skill-to-cs describe api-endpoint --json` → reads schema (~150 tokens)
3. Agent runs `skill-to-cs generate api-endpoint --params '...' --dry-run --json` → preview (~100 tokens)
4. Agent confirms and runs without `--dry-run` → done (~50 tokens)
5. **Total: ~500 tokens, deterministic**

**~8x token reduction, deterministic output, idempotent.**
