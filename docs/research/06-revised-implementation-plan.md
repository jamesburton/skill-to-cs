# Revised Implementation Plan: skill-to-cs

> Incorporating the rule engine — bidirectional, parameterised, idempotent skills

---

## Architecture Overview (Revised)

```
skill-to-cs (dotnet tool)
│
├── Core Engine
│   ├── IRule interface              Describe / Scan / Generate / Verify lifecycle
│   ├── RuleRegistry                 Discover and load rules (built-in + custom)
│   ├── ParameterSchema              Types, defaults, blocks, validation
│   ├── LocationResolver             Find files, insertion points, groups
│   └── CodeFragmentWriter           Insert code in the right places, idempotently
│
├── Commands
│   ├── init                         Bootstrap .skill-to-cs/ in a project
│   ├── assess                       Scan project, detect which rules apply
│   ├── describe <rule>              Show parameter schema, defaults, examples
│   ├── scan <rule>                  Extract existing instances as parameter sets
│   ├── generate <rule> [--params]   Apply rule with parameters, produce code
│   ├── verify [rule]                Check existing code conforms to rules
│   ├── catalog                      Build agent-readable index
│   ├── check                        Run all verification rules
│   └── publish                      AOT-compile scripts to native binaries
│
├── Built-in Rules
│   ├── Generation Rules             api-endpoint, service, test-class, ...
│   └── Verification Rules           build-check, format-check, naming-check, ...
│
└── Output
    ├── .skill-to-cs/catalog.json    Agent discovery index
    ├── .skill-to-cs/rules/          Custom rules (user or project)
    └── Code files                   Inserted in the right places
```

### Two Rule Subtypes

| Subtype | Modes | Purpose |
|---------|-------|---------|
| **Verification Rule** | verify, check | Checks existing code (build-check, format-check) |
| **Generation Rule** | describe, scan, generate, verify | Full lifecycle — reads, creates, and verifies code |

Both implement `IRule`. Verification rules simply return `NotSupported` for describe/scan/generate.

---

## V1: Foundation — Prove the Engine

**Goal:** Working CLI with the rule engine core, 3 generation rules, 4 verification rules, and the interactive surface. A developer can scan, generate, and verify immediately.

---

### Phase 1: Solution Structure & CLI Shell

**Deliverable:** Compilable project with command routing, models, and the `IRule` interface.

#### Project Structure

```
skill-to-cs/
├── src/
│   └── SkillToCs/
│       ├── SkillToCs.csproj
│       ├── Program.cs
│       │
│       ├── Commands/
│       │   ├── InitCommand.cs
│       │   ├── AssessCommand.cs
│       │   ├── DescribeCommand.cs
│       │   ├── ScanCommand.cs
│       │   ├── GenerateCommand.cs
│       │   ├── VerifyCommand.cs
│       │   ├── CatalogCommand.cs
│       │   ├── CheckCommand.cs
│       │   └── SharedOptions.cs        # --json, --verbose, --path, --dry-run
│       │
│       ├── Engine/
│       │   ├── IRule.cs                 # Core interface
│       │   ├── IRuleLifecycle.cs        # Describe/Scan/Generate/Verify
│       │   ├── RuleRegistry.cs          # Discovery and loading
│       │   ├── ParameterSchema.cs       # Schema, defaults, blocks, validation
│       │   ├── RuleParams.cs            # Parsed + validated parameter bag
│       │   ├── LocationResolver.cs      # File finding, insertion point detection
│       │   ├── CodeFragment.cs          # A piece of code with a target location
│       │   ├── CodeFragmentWriter.cs    # Writes fragments idempotently
│       │   └── ProjectContext.cs        # Cached project knowledge (files, types, structure)
│       │
│       ├── Models/
│       │   ├── Detection.cs
│       │   ├── ScannedInstance.cs
│       │   ├── GenerationResult.cs
│       │   ├── VerificationResult.cs
│       │   ├── SkillCatalog.cs
│       │   └── CheckResult.cs
│       │
│       ├── Rules/
│       │   ├── Generation/
│       │   │   ├── ApiEndpointRule.cs
│       │   │   ├── ServiceRule.cs
│       │   │   └── TestClassRule.cs
│       │   └── Verification/
│       │       ├── BuildCheckRule.cs
│       │       ├── FormatCheckRule.cs
│       │       ├── TestRunnerRule.cs
│       │       └── ToolsCheckRule.cs
│       │
│       ├── Assessment/
│       │   ├── IDetector.cs
│       │   ├── DetectorRunner.cs
│       │   ├── Detectors/
│       │   │   ├── DotNetDetector.cs
│       │   │   ├── EditorConfigDetector.cs
│       │   │   ├── TestDetector.cs
│       │   │   ├── GitDetector.cs
│       │   │   ├── CIDetector.cs
│       │   │   ├── ToolManifestDetector.cs
│       │   │   └── AgentConfigDetector.cs
│       │   └── OpportunityMapper.cs
│       │
│       └── Output/
│           ├── JsonOutputFormatter.cs
│           └── ConsoleOutputFormatter.cs
│
├── tests/
│   ├── SkillToCs.Tests/
│   │   ├── Engine/
│   │   │   ├── ParameterSchemaTests.cs
│   │   │   ├── RuleRegistryTests.cs
│   │   │   ├── LocationResolverTests.cs
│   │   │   └── CodeFragmentWriterTests.cs
│   │   ├── Rules/
│   │   │   ├── ApiEndpointRuleTests.cs
│   │   │   ├── ServiceRuleTests.cs
│   │   │   └── TestClassRuleTests.cs
│   │   └── Assessment/
│   │       └── DetectorTests.cs
│   └── SkillToCs.IntegrationTests/
│       └── WorkflowTests.cs
│
└── samples/
    ├── minimal-api/              # Sample project for testing
    └── multi-project/            # Solution with multiple projects
```

#### NuGet Packages

| Package | Purpose |
|---------|---------|
| `System.CommandLine` | CLI parsing, subcommands |
| `Spectre.Console` | Rich terminal output |
| `System.Text.Json` | JSON (source-generated) |
| `Microsoft.CodeAnalysis.CSharp` | Roslyn — parse C# for scan/location |
| `Microsoft.Extensions.FileSystemGlobbing` | Glob patterns |
| `YamlDotNet` | Parse CI configs |

#### Tasks

1. Create solution and projects with `dotnet new`
2. Add NuGet references
3. Define `IRule` interface and core engine types (empty implementations)
4. Wire up `System.CommandLine` with all subcommands (stub handlers)
5. Implement `SharedOptions` (--json, --verbose, --path, --dry-run, --no-color)
6. Implement exit code convention (0/1/2)
7. Pack as dotnet tool (`PackAsTool`, `ToolCommandName`)
8. Tests: verify command routing, help output, exit codes

#### IRule Interface (Core)

```csharp
public interface IRule
{
    // Identity
    string Name { get; }
    string Description { get; }
    string Category { get; }           // "api", "architecture", "testing", "verification"
    RuleSubtype Subtype { get; }       // Generation or Verification

    // Lifecycle
    RuleSchema Describe();
    Task<IReadOnlyList<ScannedInstance>> ScanAsync(ProjectContext ctx, CancellationToken ct);
    Task<GenerationResult> GenerateAsync(RuleParams parameters, ProjectContext ctx, CancellationToken ct);
    Task<VerificationResult> VerifyAsync(ProjectContext ctx, CancellationToken ct);

    // Detection
    bool AppliesTo(ProjectContext ctx);  // Does this rule make sense for this project?
}
```

---

### Phase 2: Parameter Schema & Validation Engine

**Deliverable:** The parameter system that powers all rules — types, defaults, blocks, validation, JSON/CLI input parsing.

#### Tasks

1. **ParameterSchema model**
   ```csharp
   public record ParameterSchema(
       string Name,
       string Description,
       IReadOnlyList<ParameterDef> Parameters,
       IReadOnlyList<BlockRule> Blocks,
       IReadOnlyList<Example> Examples
   );

   public record ParameterDef(
       string Name,
       ParamType Type,
       bool Required,
       object? DefaultValue,
       string? Description
   );

   public abstract record ParamType
   {
       public record StringType() : ParamType;
       public record IntType() : ParamType;
       public record BoolType() : ParamType;
       public record EnumType(string[] Values) : ParamType;
       public record ArrayType(ParamType Items) : ParamType;
       public record ObjectType(IReadOnlyList<ParameterDef> Properties) : ParamType;
   }

   public record BlockRule(
       string Condition,     // Expression evaluated against params
       string Message,       // Human-readable explanation
       Severity Severity     // Error (hard block) or Warning
   );
   ```

2. **RuleParams** — parsed, validated parameter bag
   - Parse from CLI arguments (`--method POST --path /`)
   - Parse from JSON (`--params '{"method":"POST","path":"/"}'`)
   - Parse from JSON file (`--params-file endpoint.json`)
   - Apply defaults for missing optional parameters
   - Validate required parameters present
   - Evaluate block rules, reject or warn

3. **Block evaluation engine**
   - Simple expression evaluator for block conditions
   - Supports: `==`, `!=`, `&&`, `||`, `!= null`, `== null`
   - Reports which block was triggered and why

4. **Tests:**
   - Default application
   - Required field validation
   - Block rule evaluation
   - JSON and CLI parsing
   - Type coercion (string "true" → bool, string "5" → int)

---

### Phase 3: ProjectContext & Location Resolution

**Deliverable:** The system that understands the project — finds files, parses structure, resolves where code should go.

#### Tasks

1. **ProjectContext** — cached knowledge about the project
   ```csharp
   public class ProjectContext
   {
       // Cached data
       public string RootPath { get; }
       public IReadOnlyList<ProjectFile> CSharpProjects { get; }     // .csproj files
       public IReadOnlyList<SourceFile> SourceFiles { get; }         // .cs files
       public IReadOnlyDictionary<string, SyntaxTree> ParsedTrees { get; }  // Roslyn trees

       // Queries
       public SourceFile? FindFile(string globPattern);
       public IEnumerable<SourceFile> FindFiles(string globPattern);
       public bool TypeExists(string typeName);
       public SourceFile? FindTypeFile(string typeName);
       public string ResolveDirectory(string conventionPath);  // e.g., "Endpoints" → "src/Api/Endpoints"
   }
   ```

2. **Lazy Roslyn parsing** — only parse files when rules need them (not upfront)
   - Parse on first access, cache the tree
   - Use `CSharpSyntaxTree.ParseText()` (no compilation needed for most scanning)

3. **LocationResolver** — find insertion points
   ```csharp
   public class LocationResolver
   {
       // Find where a new endpoint group would go in an extensions file
       public InsertionPoint FindGroupInsertionPoint(SourceFile file, string groupName);

       // Find where a new method mapping goes within a group (ordered by HTTP method)
       public InsertionPoint FindMethodInsertionPoint(SourceFile file, string method);

       // Find where to add a DI registration in Program.cs or extensions
       public InsertionPoint FindDIRegistrationPoint(ProjectContext ctx, string serviceType);

       // Generic: find the end of a class body, a namespace, etc.
       public InsertionPoint FindEndOfBlock(SyntaxNode node);
   }
   ```

4. **Convention detection** — understand project layout
   - Where do endpoints live? (scan for `*Endpoints.cs`, `*Controller.cs`)
   - Where do models/DTOs live? (scan for `Models/`, `Dtos/`, or colocated)
   - Where is DI registration? (`Program.cs` directly, or `*Extensions.cs` files)
   - These become defaults that rules use; users can override in config

5. **Tests:**
   - ProjectContext with sample project fixtures
   - Location resolution against known files
   - Convention detection against various project layouts

---

### Phase 4: CodeFragmentWriter — Idempotent Code Insertion

**Deliverable:** The engine that writes generated code into the right places, idempotently.

#### Tasks

1. **CodeFragment model**
   ```csharp
   public record CodeFragment(
       string TargetFile,              // Absolute path
       InsertionPoint Point,           // Where in the file
       string Content,                 // The code to insert
       FragmentType Type,              // NewFile, InsertAfter, InsertBefore, Replace
       string? IdempotencyKey          // How to detect if already present
   );

   public record InsertionPoint(
       int Line,                       // Line number (1-based)
       int Column,                     // Column (for inline insertion)
       string? AfterMarker,            // Insert after this text pattern
       string? BeforeMarker            // Insert before this text pattern
   );
   ```

2. **Idempotency detection**
   - Before inserting, check if the content (or its idempotency key) already exists in the target
   - For endpoints: key = `Map{Method}("{path}"` — if that pattern exists, skip
   - For DI registrations: key = `services.Add{Lifetime}<{Interface}, {Implementation}>` — if present, skip
   - For new files: key = file existence — if file exists with matching content hash, skip
   - Report: "already exists, skipping" vs "inserted" vs "would insert (dry-run)"

3. **Atomic writes**
   - Write to temp file first, rename on success
   - If inserting into existing file: read → modify in memory → write temp → rename
   - Preserve existing formatting and line endings

4. **Multi-fragment coordination**
   - A single generation may produce multiple fragments across multiple files
   - Apply all or none (transactional) — if any fragment fails, roll back all
   - Report all changes as a manifest

5. **Diff preview** (`--dry-run`)
   - Show what would change without writing
   - Format: file path + line + added content (similar to `git diff`)

6. **Tests:**
   - Insert into existing file, verify idempotency (run twice, same result)
   - New file creation
   - Multi-fragment transactional write
   - Dry-run produces diff without writing
   - Content already exists → skip

---

### Phase 5: First Generation Rule — `api-endpoint`

**Deliverable:** The flagship rule, fully functional end-to-end.

#### Tasks

1. **Implement `ApiEndpointRule`**
   - Parameter schema (rootPath, method, path, queryParameters, requestModel, responseModel, roles, errorCodes)
   - Defaults (method=GET, errorCodes=[400,404,500])
   - Blocks (GET + requestModel, DELETE + responseModel)
   - `AppliesTo()` → checks for Minimal API patterns (finds `MapGet/MapPost` or `Microsoft.NET.Sdk.Web`)

2. **Scan implementation**
   - Find all `*Endpoints.cs` files
   - Parse with Roslyn to extract `MapGet`, `MapPost`, `MapPut`, `MapPatch`, `MapDelete` invocations
   - Extract parameters: route template, handler parameters, return type, auth attributes
   - Return as `ScannedInstance` with full parameter set

3. **Generate implementation**
   - Resolve endpoints file from `rootPath`
   - If file doesn't exist, scaffold entire file with route group setup
   - If file exists, find insertion point by method ordering
   - Generate handler delegate with:
     - Route/query parameter binding
     - `[FromBody]` for request model
     - `TypedResults` return type union
     - Auth policy if roles specified
   - Generate request/response DTOs if they don't exist
   - Generate DI registration if new file created

4. **Verify implementation**
   - Scan all endpoints
   - Check against block rules (GET with body, etc.)
   - Check naming conventions (PascalCase methods, consistent route patterns)
   - Report violations

5. **Code templates** — the actual C# code strings that get generated
   - Endpoint file scaffold (new file template)
   - Individual endpoint mapping (insertion template)
   - Request DTO record
   - Response DTO record
   - Result type union based on error codes

6. **Tests:**
   - Scan sample project, verify extracted parameters match
   - Generate into empty project, verify valid compilable code
   - Generate into existing endpoints file, verify insertion at correct point
   - Idempotency: generate same endpoint twice, verify no duplication
   - Block validation: GET + requestModel → error
   - Dry-run: verify diff output without writes

---

### Phase 6: Two More Generation Rules — `service` and `test-class`

**Deliverable:** Prove the engine generalises beyond endpoints.

#### `service` Rule

Parameters:
```json
{
  "name": "UserService",
  "interface": true,           // default: true — generate IUserService
  "methods": [
    { "name": "GetById", "params": "int id", "returns": "UserDto?" },
    { "name": "Create", "params": "CreateUserRequest req", "returns": "UserDto" }
  ],
  "lifetime": "Scoped",       // Scoped | Transient | Singleton
  "inject": ["IUserRepository", "ILogger<UserService>"]
}
```

Generates:
- `IUserService.cs` (interface with method signatures)
- `UserService.cs` (class implementing interface, constructor injection, method stubs)
- DI registration in appropriate extensions file

Scan: finds classes implementing service patterns (interface + implementation + DI registration).

#### `test-class` Rule

Parameters:
```json
{
  "name": "UserServiceTests",
  "targetType": "UserService",
  "framework": "xunit",       // auto-detected from project
  "methods": ["GetById_ReturnsUser_WhenExists", "GetById_ReturnsNull_WhenNotFound"],
  "inject": ["IUserRepository"]   // auto-detected from target constructor
}
```

Generates:
- Test class with constructor and mock setup
- Test methods with Arrange/Act/Assert sections
- NuGet references for mocking framework if needed

Scan: finds test classes, extracts target type and test methods.

#### Tasks

1. Implement `ServiceRule` (schema, scan, generate, verify)
2. Implement `TestClassRule` (schema, scan, generate, verify)
3. Tests for both rules (same pattern as Phase 5)

---

### Phase 7: Verification Rules

**Deliverable:** The original verification scripts, now implemented as rules.

#### Tasks

1. **BuildCheckRule** — `dotnet build --no-restore -warnaserror`, parse output
2. **FormatCheckRule** — `dotnet format --verify-no-changes`, with `--fix` support
3. **TestRunnerRule** — `dotnet test`, parse TRX results
4. **ToolsCheckRule** — compare installed tools against manifest, with `--fix` support

Each implements `IRule` with:
- `Subtype = Verification`
- `Describe()` → returns schema (minimal — maybe just a threshold or filter)
- `ScanAsync()` → returns current state (build status, format violations, test results)
- `GenerateAsync()` → throws `NotSupportedException` (verification rules don't generate)
- `VerifyAsync()` → runs the check, returns structured result

5. **`check` command** — runs all verification rules, aggregates results
6. **Tests:** Each verification rule against sample projects

---

### Phase 8: Assessment, Catalog & Interactive Surface

**Deliverable:** The commands that tie everything together.

#### Tasks

1. **`assess` command** (refined)
   - Runs all detectors (same as original plan)
   - Now also indicates which rules apply and what `scan` would find
   - Shows: "12 api-endpoint instances found, 3 service instances, build-check applicable"

2. **`describe` command**
   - `skill-to-cs describe api-endpoint` → rich parameter table
   - `skill-to-cs describe api-endpoint --json` → full schema for agents
   - Includes defaults, blocks, and examples

3. **`scan` command**
   - `skill-to-cs scan api-endpoint` → table of existing instances
   - `skill-to-cs scan api-endpoint --json` → full instance list with parameters
   - `skill-to-cs scan api-endpoint --instance "/api/users:POST:/" --json` → single instance as clone template
   - `skill-to-cs scan` (no rule) → summary across all rules

4. **`catalog` command** (refined)
   - Includes both generation and verification rules
   - For generation rules, includes instance count from latest scan
   - `--agents-md` → outputs AGENTS.md-compatible section
   - Detects stale data (rule source changed)

5. **`init` command**
   - Bootstrap `.skill-to-cs/` directory
   - Generate default `config.json`
   - Optionally run assess + catalog in one shot (`init --full`)
   - Add `.skill-to-cs/bin/` to `.gitignore`

6. **Config file format** (refined)
   ```json
   {
     "version": "1.0.0",
     "conventions": {
       "endpointsDirectory": "src/Api/Endpoints",
       "modelsDirectory": "src/Api/Models",
       "servicesDirectory": "src/Core/Services",
       "testsDirectory": "tests/UnitTests"
     },
     "rules": {
       "api-endpoint": {
         "defaults": {
           "errorCodes": ["400", "404", "500"],
           "roles": []
         }
       },
       "service": {
         "defaults": {
           "lifetime": "Scoped",
           "interface": true
         }
       },
       "disabled": ["ci-local"]
     },
     "verification": {
       "coverageThreshold": 80,
       "treatWarningsAsErrors": true,
       "excludePaths": ["**/Generated/**"]
     }
   }
   ```

7. **Tests:** Full workflow integration — init → assess → scan → generate → verify → check

---

### Phase 9: User-Level Layering & Polish

**Deliverable:** User defaults, docs, error handling, NuGet publish.

#### Tasks

1. **User config directory** (`~/.skill-to-cs/`)
   - User defaults for rules (e.g., always use Scoped lifetime)
   - User-level custom rules
   - `"clear": true` at project level to ignore user defaults

2. **Config merge logic** — user < project < component (deep merge)

3. **Error messages** — actionable errors for common problems
   - "No .NET project found" → suggest running from solution root
   - "Rule 'api-endpoint' doesn't apply to this project" → explain why
   - "Parameter 'rootPath' is required" → show example

4. **`--verbose` mode** — detailed output showing detection reasoning

5. **README.md** — installation, quick start, command reference

6. **Sample project** — a `.NET 8` Minimal API with existing endpoints for demo

7. **NuGet package** — `dotnet tool install -g skill-to-cs`

---

## V2: Deep Integration (Phases 10-14)

### Phase 10: Richer Generation Rules

Add the remaining built-in rules:

| Rule | Parameters | Generates |
|------|-----------|-----------|
| `controller-action` | Similar to api-endpoint, but MVC | Controller action + DTOs |
| `mediatr-handler` | name, type (Query/Command), request, response | Handler + Request + Response |
| `masstransit-consumer` | name, message, retryPolicy | Consumer + Message + Registration |
| `ef-entity` | name, properties, relationships | Entity + Config + Migration hint |
| `repository` | name, entity, methods | Repository + Interface + DI |
| `middleware` | name, order, inject | Middleware class + Registration |
| `background-service` | name, interval, inject | BackgroundService + Registration |

### Phase 11: Rule Composition

- Meta-rules that orchestrate sub-rules
- `feature` meta-rule: endpoint + handler + DTOs + tests in one command
- Dependency resolution: if rule A needs output from rule B, run B first
- Composable mapping: `--mapRequest`, `--mapResponse`, `--mapper AutoMapper`

### Phase 12: SKILL.md & AGENTS.md Generation

- Generate SKILL.md wrappers for `.claude/skills/` integration
- Generate AGENTS.md sections with `<!-- skill-to-cs:start/end -->` markers
- Auto-update on `catalog` command

### Phase 13: AOT Publish Pipeline

- `skill-to-cs publish` → compile verification scripts to native binaries
- Multi-RID support
- Catalog references both script and binary paths

### Phase 14: Custom Rule Authoring

- Documentation for writing custom rules
- Rule template scaffolding: `skill-to-cs new-rule billing-event`
- Validation: `skill-to-cs validate-rule my-rule.cs`
- User rules in `~/.skill-to-cs/rules/`, project rules in `.skill-to-cs/rules/`

---

## V3: Ecosystem (Phases 15-18)

### Phase 15: Pre-Commit & CI Generation

Generate integration configs from the rule catalog.

### Phase 16: Watch Mode

Monitor source configs and project structure, regenerate catalog on changes.

### Phase 17: MCP Server Bridge

`skill-to-cs serve` exposes `list_rules`, `describe_rule`, `scan_rule`, `generate`, `verify` as MCP tools. Dynamic toolset pattern — only meta-tools registered, rules discovered lazily.

### Phase 18: Rule Packages & Registry

Distribute rules as NuGet packages. `skill-to-cs install Acme.Rules.ApiConventions`.

---

## Milestones (Revised)

| Milestone | Phases | Key Outcome |
|-----------|--------|-------------|
| **v1.0-alpha** | 1-4 | CLI + engine + parameter system + code writer |
| **v1.0-beta** | 5-7 | 3 generation rules + 4 verification rules, all working |
| **v1.0** | 8-9 | Full workflow, interactive surface, user layering, published |
| **v2.0** | 10-14 | 10+ rules, composition, agent integration, custom rules |
| **v3.0** | 15-18 | CI/pre-commit gen, MCP bridge, community ecosystem |

---

## Testing Strategy (Revised)

### Test Fixtures

```
samples/
├── minimal-api/                  # Minimal API with 3 endpoints
│   ├── MinimalApi.csproj
│   ├── Program.cs
│   ├── Endpoints/
│   │   └── UsersEndpoints.cs     # Known endpoints for scan tests
│   └── Models/
│       ├── UserDto.cs
│       └── CreateUserRequest.cs
│
├── multi-project/                # Solution with API + Library + Tests
│   ├── MultiProject.sln
│   ├── src/Api/
│   ├── src/Core/
│   └── tests/UnitTests/
│
├── empty-project/                # Bare .NET project, no patterns yet
│   └── Empty.csproj
│
└── kitchen-sink/                 # Everything: MediatR, MassTransit, EF, etc.
    └── ...
```

### Test Categories

| Category | What | How |
|----------|------|-----|
| **Unit** | Parameter validation, block rules, schema | Pure logic, no filesystem |
| **Engine** | Location resolution, code fragment writing | In-memory file system or temp dirs |
| **Rule** | Each rule's scan/generate/verify | Against fixture projects |
| **Integration** | Full CLI workflow end-to-end | Invoke CLI, check file output |
| **Idempotency** | Run generate twice, verify no change | Core guarantee — every rule |
| **Round-trip** | Scan → extract params → generate with those params → verify identical | Proves scan/generate symmetry |

### The Round-Trip Test

The most important test pattern for this system:

```
1. Scan existing code → get parameter sets
2. Delete the scanned code
3. Generate with those same parameters
4. Diff original vs generated → should match (modulo formatting)
```

This proves the scan ↔ generate symmetry is correct.
