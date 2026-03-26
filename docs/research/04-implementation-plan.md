# Implementation Plan: skill-to-cs

> Phased delivery — quick wins in v1, full vision by v3

---

## Architecture Overview

```
skill-to-cs (dotnet tool)
├── assess          Scan project, detect scriptable opportunities
├── generate        Create .cs verification scripts from assessment
├── catalog         Build/update catalog.json for agent discovery
├── check           Run all scripts in check mode, report results
├── publish         AOT-compile scripts to native binaries
└── init            Bootstrap .skill-to-cs/ in a project
```

**The tool itself** is a standard .NET 10 console app, distributed as a `dotnet tool` (global or local). **The output** is single-file .NET 10 C# scripts that agents and humans invoke directly.

**Dogfooding:** The tool uses the same patterns it generates — idempotent commands, `--json` output, structured exit codes.

---

## V1: Foundation & Quick Wins

**Goal:** Working CLI that scans a .NET project and generates useful verification scripts immediately. A developer can run `skill-to-cs init && skill-to-cs assess && skill-to-cs generate` and have scripts they can use today.

---

### Phase 1: Project Scaffolding & CLI Shell

**Deliverable:** A `dotnet tool` with the command structure wired up but no logic yet.

#### Tasks

1. **Create solution structure**
   ```
   skill-to-cs/
   ├── src/
   │   └── SkillToCs/
   │       ├── SkillToCs.csproj
   │       ├── Program.cs                  # Entry point, command routing
   │       ├── Commands/
   │       │   ├── InitCommand.cs
   │       │   ├── AssessCommand.cs
   │       │   ├── GenerateCommand.cs
   │       │   ├── CatalogCommand.cs
   │       │   └── CheckCommand.cs
   │       ├── Assessment/
   │       │   └── (empty, phase 2)
   │       ├── Generation/
   │       │   └── (empty, phase 3)
   │       └── Models/
   │           ├── ProjectAssessment.cs
   │           ├── SkillDefinition.cs
   │           ├── SkillCatalog.cs
   │           └── CheckResult.cs
   └── tests/
       └── SkillToCs.Tests/
           └── SkillToCs.Tests.csproj
   ```

2. **NuGet packages**
   - `System.CommandLine` — CLI parsing (supports subcommands, `--json`, `--verbose`)
   - `Spectre.Console` — Rich terminal output (tables, trees, progress)
   - `System.Text.Json` — Serialization (source-generated for AOT compat)

3. **Command structure with shared options**
   ```
   skill-to-cs init [--path <dir>] [--force]
   skill-to-cs assess [--path <dir>] [--json] [--verbose]
   skill-to-cs generate [--path <dir>] [--category <cat>] [--dry-run] [--json]
   skill-to-cs catalog [--path <dir>] [--json]
   skill-to-cs check [--path <dir>] [--json] [--fail-fast]
   ```

4. **Global options** on all commands
   - `--json` — structured JSON output (for agent consumption)
   - `--verbose` — detailed human output
   - `--path <dir>` — target directory (defaults to cwd)
   - `--no-color` — disable ANSI colors

5. **Exit code convention**
   - 0 = success
   - 1 = violations/issues found (expected failures)
   - 2 = tool error (unexpected failures)

6. **Pack as dotnet tool**
   ```xml
   <PackAsTool>true</PackAsTool>
   <ToolCommandName>skill-to-cs</ToolCommandName>
   ```

7. **Tests:** Verify command routing, help output, exit codes.

---

### Phase 2: Assessment Engine

**Deliverable:** `skill-to-cs assess` scans a project and reports what it found — project type, available tools, scriptable opportunities.

#### Architecture

```
AssessmentEngine
├── IDetector (interface)
│   ├── DotNetDetector          Finds .csproj/.sln, reads properties
│   ├── EditorConfigDetector    Parses .editorconfig rules
│   ├── GitDetector             Detects repo, hooks, ignore patterns
│   ├── CIDetector              Finds GitHub Actions, Azure Pipelines
│   ├── TestDetector            Finds test projects, frameworks
│   ├── DockerDetector          Finds Dockerfile, compose files
│   ├── NodeDetector            Finds package.json, tsconfig
│   ├── ToolManifestDetector    Reads .config/dotnet-tools.json
│   └── AgentConfigDetector     Finds CLAUDE.md, AGENTS.md, .cursor/
├── DetectorRunner              Runs all detectors, aggregates results
└── OpportunityMapper           Maps detections → scriptable opportunities
```

#### Tasks

1. **Define `IDetector` interface**
   ```csharp
   public interface IDetector
   {
       string Name { get; }
       int Priority { get; }  // Run order (lower = earlier)
       Task<Detection?> DetectAsync(string rootPath, CancellationToken ct);
   }

   public record Detection(
       string DetectorName,
       string Category,        // "build", "standards", "workflow", "agent"
       Dictionary<string, object> Properties,
       List<ScriptOpportunity> Opportunities
   );

   public record ScriptOpportunity(
       string Name,             // "build-check"
       string Description,      // "Verify project builds with analyzers"
       string Category,         // "build"
       string[] SourceFiles,    // Config files this is derived from
       ScriptCapability Capabilities  // Check, Fix, or both
   );
   ```

2. **Implement detectors (v1 scope — .NET focused)**

   **DotNetDetector:**
   - Find all `*.csproj` and `*.sln` files
   - Read `TargetFramework`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`
   - Detect analyzer packages (StyleCop, Roslynator, etc.)
   - Detect `EnforceCodeStyleInBuild` setting
   - Opportunities: `build-check`, `analyzer-check`

   **EditorConfigDetector:**
   - Parse `.editorconfig` files at each directory level
   - Extract naming rules, formatting rules, severity levels
   - Opportunities: `format-check`, `naming-check`

   **TestDetector:**
   - Find test projects (naming convention: `*.Tests.csproj`, `*.Test.csproj`)
   - Detect test framework (xUnit, NUnit, MSTest) from package refs
   - Check for coverage tools (Coverlet, etc.)
   - Opportunities: `test-runner`, `coverage-check`

   **GitDetector:**
   - Confirm `.git/` exists
   - Check for existing hooks (`.husky/`, `.git/hooks/`)
   - Read `.gitignore` for patterns
   - Opportunities: `pre-commit-check`

   **CIDetector:**
   - Find `.github/workflows/*.yml`, `azure-pipelines.yml`
   - Extract step names and commands (these are existing checks we can mirror locally)
   - Opportunities: `ci-local` (run CI checks locally)

   **ToolManifestDetector:**
   - Read `.config/dotnet-tools.json`
   - List installed tools and versions
   - Opportunities: `tools-check` (verify tools are restored)

   **AgentConfigDetector:**
   - Find `CLAUDE.md`, `AGENTS.md`, `.cursor/rules/`, `.claude/skills/`
   - Note existing agent configuration
   - Opportunities: `agent-catalog` (generate agent-consumable index)

3. **DetectorRunner** — runs all registered detectors, handles errors gracefully (one detector failing doesn't stop others), merges results.

4. **OpportunityMapper** — deduplicates opportunities, assigns priorities, resolves conflicts.

5. **Output formatters**
   - Human: Spectre.Console table with detected features and opportunities
   - JSON: Full `ProjectAssessment` object

6. **Tests:**
   - Unit tests per detector with fixture directories
   - Integration test: assess a known sample project, verify expected detections

#### Sample Output (Human)

```
Assessment: C:\Projects\MyApi

  Project Type    .NET 8.0 (Web API)
  Test Framework  xUnit + Coverlet
  CI Pipeline     GitHub Actions (3 workflows)
  Agent Config    CLAUDE.md found

  Scriptable Opportunities (7 found)

  ┌──────────────────┬────────────────────────────────────────┬──────────┬───────┐
  │ Name             │ Description                            │ Category │ Modes │
  ├──────────────────┼────────────────────────────────────────┼──────────┼───────┤
  │ build-check      │ Build with all analyzers enabled       │ build    │ check │
  │ format-check     │ Verify formatting via dotnet format    │ standards│ both  │
  │ naming-check     │ Enforce .editorconfig naming rules     │ standards│ both  │
  │ test-runner      │ Run xUnit tests, parse results         │ build    │ check │
  │ coverage-check   │ Verify test coverage meets threshold   │ build    │ check │
  │ tools-check      │ Verify dotnet tools are restored       │ workflow │ both  │
  │ ci-local         │ Mirror GitHub Actions checks locally   │ workflow │ check │
  └──────────────────┴────────────────────────────────────────┴──────────┴───────┘

  Run: skill-to-cs generate  to create scripts
```

---

### Phase 3: Script Generation Engine

**Deliverable:** `skill-to-cs generate` creates actual `.cs` files from assessment results.

#### Architecture

```
GenerationEngine
├── IScriptTemplate (interface)
│   ├── BuildCheckTemplate
│   ├── FormatCheckTemplate
│   ├── NamingCheckTemplate
│   ├── TestRunnerTemplate
│   ├── CoverageCheckTemplate
│   ├── ToolsCheckTemplate
│   └── CILocalTemplate
├── TemplateRenderer          Fills templates with project-specific values
├── ScriptWriter              Writes .cs files with headers and hashes
└── CatalogWriter             Generates/updates catalog.json
```

#### Tasks

1. **Define `IScriptTemplate` interface**
   ```csharp
   public interface IScriptTemplate
   {
       string Name { get; }                    // "build-check"
       string[] HandlesOpportunities { get; }  // Which opportunities this template covers
       string Render(ScriptContext context);    // Returns full .cs file content
   }

   public record ScriptContext(
       ScriptOpportunity Opportunity,
       ProjectAssessment Assessment,
       SkillTocsConfig Config
   );
   ```

2. **Implement v1 templates (7 scripts)**

   Each template generates a complete, runnable `.cs` file with:
   - Shebang line (`#!/usr/bin/env dotnet`)
   - Source comment header (tool version, source hash, generation timestamp)
   - `#:package` directives for dependencies
   - `System.CommandLine` for `--check`/`--fix`/`--json` flags
   - Idempotent execution logic
   - Structured JSON output schema
   - Human-readable Spectre.Console output

   **build-check.cs:**
   - Runs `dotnet build --no-restore -warnaserror` (or respects project's existing severity)
   - Parses MSBuild output for errors/warnings
   - Reports pass/fail with violation list

   **format-check.cs:**
   - `--check` mode: `dotnet format --verify-no-changes --severity error`
   - `--fix` mode: `dotnet format --severity error`
   - Parses format output for changed files

   **naming-check.cs:**
   - Reads `.editorconfig` naming rules
   - Runs `dotnet format analyzers --verify-no-changes` filtered to naming diagnostics
   - Reports violations with file:line references

   **test-runner.cs:**
   - Runs `dotnet test --no-build --logger "trx"` (or `--logger "json"` if available)
   - Parses test results for pass/fail/skip counts
   - Reports failures with test names and messages

   **coverage-check.cs:**
   - Runs `dotnet test --collect:"XPlat Code Coverage"`
   - Parses Coverlet output for coverage percentage
   - Configurable threshold (default: 80%, overridable in config)

   **tools-check.cs:**
   - `--check` mode: compare installed tools against manifest
   - `--fix` mode: run `dotnet tool restore`
   - Reports missing/outdated tools

   **ci-local.cs:**
   - Reads CI config (GitHub Actions YAML)
   - Extracts `run:` steps that are dotnet commands
   - Executes them in order, reports results

3. **TemplateRenderer** — fills project-specific values (paths, framework version, test project names, coverage thresholds) into templates.

4. **ScriptWriter**
   - Writes `.cs` files to `.skill-to-cs/skills/`
   - Computes SHA256 hash of source config files
   - Embeds hash in script header for staleness detection
   - Skips writing if file exists and hash matches (idempotent)

5. **CatalogWriter** — generates/updates `.skill-to-cs/catalog.json`

6. **Tests:**
   - Snapshot tests: verify generated script content matches expected output
   - Round-trip tests: generate script → run it → verify it produces valid JSON output

#### Generated File Layout

```
.skill-to-cs/
├── catalog.json
├── config.json              # User overrides (coverage threshold, etc.)
└── skills/
    ├── build-check.cs
    ├── format-check.cs
    ├── naming-check.cs
    ├── test-runner.cs
    ├── coverage-check.cs
    ├── tools-check.cs
    └── ci-local.cs
```

---

### Phase 4: Init, Catalog & Check Commands

**Deliverable:** Remaining commands that complete the v1 workflow.

#### Tasks

1. **`init` command**
   - Creates `.skill-to-cs/` directory structure
   - Generates default `config.json` with sensible defaults
   - Adds `.skill-to-cs/bin/` to `.gitignore` (AOT binaries not committed)
   - Optionally runs `assess` + `generate` in one shot (`init --full`)

2. **`catalog` command**
   - Reads `.skill-to-cs/skills/` directory
   - Parses script headers for metadata (name, description, hash, modes)
   - Outputs `catalog.json`
   - `--json` mode: outputs catalog to stdout (for piping to agents)
   - Detects stale scripts (source hash mismatch) and flags them

3. **`check` command**
   - Reads catalog
   - Runs each script in `--check --json` mode
   - Aggregates results into a single report
   - `--fail-fast`: stop on first failure
   - `--category <cat>`: run only scripts in a category
   - `--skill <name>`: run a single named skill
   - Exit 0 if all pass, exit 1 if any fail

4. **Config file format**
   ```json
   // .skill-to-cs/config.json
   {
     "version": "1.0.0",
     "settings": {
       "coverageThreshold": 80,
       "treatWarningsAsErrors": true,
       "excludePaths": ["**/Generated/**", "**/Migrations/**"]
     },
     "skills": {
       "disabled": ["ci-local"],
       "overrides": {
         "test-runner": {
           "args": "--filter Category!=Integration"
         }
       }
     }
   }
   ```

5. **Tests:** Full workflow integration test: `init` → `assess` → `generate` → `check`.

---

### Phase 5: User-Level Layering

**Deliverable:** User-level defaults that apply across all projects.

#### Tasks

1. **User config directory**
   ```
   ~/.skill-to-cs/
   ├── config.json           # User defaults (coverage threshold, preferred categories)
   ├── skills/               # User-level skills (available everywhere)
   │   └── git-hygiene.cs    # Example: check branch naming, commit messages
   └── templates/            # Custom templates (v2)
   ```

2. **Config merge logic**
   - Load user config first
   - Deep-merge project config over user config
   - `"clear": true` at project level resets a section to ignore user defaults
   - Component-level (subfolder) configs merge over project

3. **User skill discovery**
   - User skills appear in catalog alongside project skills
   - Marked with `"layer": "user"` in catalog.json
   - Can be disabled per-project in project config

4. **`init --global`** — bootstrap `~/.skill-to-cs/` with defaults.

5. **Tests:** Verify merge precedence (user < project < component), verify `clear` directive.

---

### Phase 6: V1 Polish & Documentation

**Deliverable:** Ready for real use.

#### Tasks

1. **README.md** — installation, quick start, command reference
2. **Error messages** — helpful, actionable errors for common problems (no .NET project found, SDK version too old, etc.)
3. **`--verbose` mode** — detailed output showing what each detector found and why
4. **`--dry-run` on generate** — show what would be created without writing files
5. **Staleness warnings** — when running `check`, warn if scripts are stale (source config changed since generation)
6. **Sample project** — a small .NET project with `.skill-to-cs/` already set up, for demos and testing
7. **NuGet package** — publish as `dotnet tool install -g skill-to-cs`

---

## V2: Full Integration

**Goal:** Deep integration with the agent ecosystem, richer assessment, and cross-platform output.

### Phase 7: SKILL.md Wrapper Generation

- For each generated `.cs` script, optionally produce a SKILL.md wrapper
- Places scripts in `.claude/skills/` format for native Claude Code integration
- SKILL.md frontmatter includes: name, description, allowed-tools, argument-hint
- The skill's instruction body tells the agent when and how to invoke the script
- Supports `${CLAUDE_SKILL_DIR}` substitution for portable paths

### Phase 8: AGENTS.md Section Generation

- Auto-generate a "## Available Verification Scripts" section
- Table of scripts with descriptions, modes, and invocation commands
- Can be included in AGENTS.md via a comment marker: `<!-- skill-to-cs:start -->...<!-- skill-to-cs:end -->`
- `skill-to-cs catalog --agents-md` outputs the section for manual inclusion
- `skill-to-cs catalog --agents-md --update` updates it in-place between markers

### Phase 9: Richer Assessment

- **ArchitectureDetector** — detect layered architecture (Controllers/Services/Repositories), module boundaries
- **SecurityDetector** — find auth middleware, CORS config, secrets patterns
- **APIDetector** — find OpenAPI specs, controller conventions, endpoint patterns
- **DatabaseDetector** — find EF Core migrations, connection strings, DbContext
- **Parse CI deeply** — extract environment variables, service containers, test matrix
- **Cross-language awareness** — detect JS/TS frontend alongside .NET backend (monorepo support)

### Phase 10: Fix Mode for All Scripts

- Upgrade all templates to support `--fix` where applicable
- `build-check --fix` → run `dotnet build` and report (can't auto-fix code, but can restore packages)
- `format-check --fix` → `dotnet format`
- `naming-check --fix` → `dotnet format analyzers`
- `tools-check --fix` → `dotnet tool restore`
- `coverage-check --fix` → report which files lack coverage (can't auto-fix, but can guide)

### Phase 11: AOT Publish Pipeline

- `skill-to-cs publish` command
- Compiles all scripts to native binaries using `dotnet publish -r <rid>`
- Places binaries in `.skill-to-cs/bin/`
- Updates catalog.json with binary paths and `preferBinary` flag
- Supports multi-RID (`win-x64`, `linux-x64`, `osx-arm64`)
- CI integration: publish once, distribute binaries as artifacts

### Phase 12: Custom Templates

- Users can add custom templates in `~/.skill-to-cs/templates/` or `.skill-to-cs/templates/`
- Template format: a `.cs` file with `{{placeholder}}` markers
- Templates are registered in config and matched to assessment opportunities
- Example: company-specific API convention checker, internal service health checks

---

## V3: Ecosystem & Intelligence

**Goal:** Advanced features, community ecosystem, and self-improving capabilities.

### Phase 13: Pre-Commit & CI Generation

- `skill-to-cs generate --pre-commit` → outputs `.pre-commit-config.yaml` entries
- `skill-to-cs generate --github-actions` → outputs workflow step YAML
- `skill-to-cs generate --azure-pipelines` → outputs pipeline task YAML
- Each format uses the generated scripts as the implementation

### Phase 14: Watch Mode & Staleness Daemon

- `skill-to-cs watch` — monitors source configs, regenerates scripts on change
- File hash comparison to detect when `.editorconfig`, `.csproj`, etc. change
- Integrates with IDE file watchers (VS Code tasks, Rider external tools)

### Phase 15: MCP Server (Optional Bridge)

- For agents that prefer MCP over CLI, expose the catalog as an MCP server
- `skill-to-cs serve` — starts an MCP server exposing:
  - `list_skills` → returns catalog
  - `run_skill` → executes a named script and returns JSON result
  - `assess_project` → runs assessment
- Uses dynamic toolset pattern (Speakeasy-inspired) — only the 3 meta-tools are registered; actual skills are discovered lazily

### Phase 16: Skill Sharing & Registry

- `skill-to-cs share <name>` — publishes a skill to a NuGet-based registry
- `skill-to-cs install <package>` — installs community skills
- Skills are NuGet packages containing `.cs` templates
- Version pinning via `.skill-to-cs/skills.lock.json`

---

## Dependency Summary

### V1 NuGet Packages (the tool itself)

| Package | Purpose |
|---------|---------|
| `System.CommandLine` | CLI parsing, subcommands, help generation |
| `Spectre.Console` | Rich terminal output (tables, trees, progress bars) |
| `System.Text.Json` | JSON serialization (source-generated for AOT compat) |
| `Microsoft.Extensions.FileSystemGlobbing` | Glob pattern matching for file discovery |
| `YamlDotNet` | Parse CI configs (GitHub Actions, Azure Pipelines) |
| `IniParser` or custom | Parse `.editorconfig` files |

### Generated Script Dependencies

| Package | Purpose | Used By |
|---------|---------|---------|
| `System.CommandLine` | `--check`/`--fix`/`--json` flags | All scripts |
| `Spectre.Console` | Human-readable output | All scripts |

---

## Testing Strategy

### Unit Tests
- Each detector: fixture directories with known configs, verify expected detections
- Each template: snapshot tests comparing generated output to expected `.cs` content
- Config merge: verify layering precedence and `clear` directive

### Integration Tests
- Full workflow on sample projects: `init` → `assess` → `generate` → `check`
- Generated scripts actually run and produce valid JSON
- Cross-platform: test on Windows + Linux (GitHub Actions matrix)

### Sample Projects (Test Fixtures)
- `samples/minimal-api/` — single .NET 8 web API project
- `samples/multi-project/` — solution with API + library + tests
- `samples/monorepo/` — .NET backend + Node frontend
- `samples/empty/` — bare directory, verify graceful handling

---

## Milestones

| Milestone | Phases | Outcome |
|-----------|--------|---------|
| **v1.0-alpha** | 1-3 | CLI scaffolding + assessment + script generation |
| **v1.0-beta** | 4-5 | Full init/catalog/check workflow + user layering |
| **v1.0** | 6 | Polished, documented, published to NuGet |
| **v2.0** | 7-12 | Agent integration, richer assessment, AOT, templates |
| **v3.0** | 13-16 | CI generation, MCP bridge, community registry |
