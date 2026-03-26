# Guidance: skill-to-cs — Design Principles & Architecture Direction

> For review and refinement before planning

---

## Vision

**Assess what can be scripted in a project, then generate or scaffold .NET 10 single-file C# scripts that encode those capabilities as idempotent, versionable, token-efficient tools for AI coding agents.**

The system operates at two levels:
- **User level** — default skills, preferences, and tooling available regardless of project
- **Project level** (including sub-folders) — project-specific and component-specific scripts that encode engineering knowledge as executable checks

---

## Core Principles

### 1. Enforce, Don't Explain

From harness engineering: a linter error message is more reliable than a CLAUDE.md paragraph. Wherever possible, convert prose instructions into executable verification.

| Instead of... | Generate... |
|---------------|-------------|
| "Use PascalCase for public methods" | A `.editorconfig` rule + Roslyn analyzer |
| "Always run tests before committing" | `verify.cs` that exits non-zero on failure |
| "Use structured logging" | A Roslyn analyzer that flags `Console.WriteLine` |
| "API responses must include correlation ID" | A test script that checks response headers |

### 2. Progressive Disclosure

Match the Agent Skills spec's token management:

1. **Catalog** (~100 tokens per skill) — name + description, always loaded
2. **Script** (<5000 tokens) — full `.cs` source, loaded when invoked
3. **Output** (minimal) — structured JSON/text, only the result

### 3. Idempotent by Default

Every generated script must be safe to run repeatedly:
- Check-before-act pattern
- Exit 0 for "already in desired state"
- Exit non-zero only for genuine failures
- Support `--check` (verify only) and `--fix` (apply corrections) modes

### 4. Layered Configuration

Follow the EditorConfig/NuGet cascade model:

```
~/.skill-to-cs/              (user defaults)
  skills/
    format-check.cs
    naming-conventions.cs
  config.json

/project/.skill-to-cs/       (project level)
  skills/
    api-health-check.cs
    db-migration-check.cs
  config.json                 (can override/extend user config)

/project/src/api/.skill-to-cs/   (component level)
  skills/
    openapi-validation.cs
  config.json
```

Merge strategy: **deep merge with explicit `"clear": true`** to reset inherited skills when a layer needs a clean slate.

### 5. Versionable & Reviewable

- Scripts live in the repo, tracked by git
- Changes go through PR review like any other code
- A lock/hash mechanism detects when scripts need regeneration (source config changed)

---

## Assessment Phase — What to Detect

The tool should scan a project and identify scriptable opportunities across these categories:

### Category 1: Build & Verification

| Signal | Detected From | Potential Script |
|--------|---------------|-----------------|
| .NET project | `*.csproj`, `*.sln` | `build-check.cs` — build + analyzer enforcement |
| TypeScript | `tsconfig.json` | `tsc-check.cs` — type checking |
| Tests present | `*Test*.cs`, `*.test.ts` | `test-runner.cs` — run tests, parse results |
| Linting config | `.editorconfig`, `.eslintrc` | `lint-check.cs` — format + lint verification |
| Docker | `Dockerfile`, `docker-compose.yml` | `container-check.cs` — build/health check |

### Category 2: Code Standards

| Signal | Detected From | Potential Script |
|--------|---------------|-----------------|
| Naming conventions | `.editorconfig` rules | `naming-check.cs` — verify naming patterns |
| Architecture rules | Project structure, ADRs | `architecture-check.cs` — boundary enforcement |
| API conventions | OpenAPI specs, controller patterns | `api-convention-check.cs` |
| Security patterns | Auth middleware, CORS config | `security-check.cs` — OWASP basic checks |

### Category 3: Workflow Automation

| Signal | Detected From | Potential Script |
|--------|---------------|-----------------|
| Git hooks | `.husky/`, `.pre-commit-config.yaml` | `pre-commit.cs` — unified pre-commit |
| CI pipeline | `.github/workflows/`, `azure-pipelines.yml` | `ci-local.cs` — run CI checks locally |
| Database | Connection strings, migration folders | `db-check.cs` — migration status |
| Dependencies | `packages.lock.json`, `package-lock.json` | `deps-check.cs` — outdated/vulnerable |

### Category 4: Agent-Specific

| Signal | Detected From | Potential Script |
|--------|---------------|-----------------|
| CLAUDE.md exists | `.claude/` | `claude-conventions.cs` — extract & verify |
| MCP servers configured | `settings.json` | `mcp-health.cs` — check MCP server status |
| Existing scripts | `scripts/`, `tools/`, Makefile | `tool-catalog.cs` — index available tools |

---

## Script Generation — Output Format

### Generated Script Structure

Each generated `.cs` file follows a consistent pattern:

```csharp
#!/usr/bin/env dotnet
// skill-to-cs: auto-generated verification script
// Source: .editorconfig, MyProject.csproj
// Version: 1.0.0
// Hash: sha256:abc123... (of source config that generated this)

#:package System.CommandLine@*
#:package Spectre.Console@*

using System.CommandLine;
using Spectre.Console;

var rootCommand = new RootCommand("Check naming conventions");
var checkOption = new Option<bool>("--check", "Verify only (default)");
var fixOption = new Option<bool>("--fix", "Auto-fix violations");
var jsonOption = new Option<bool>("--json", "Output JSON for agent consumption");

rootCommand.AddOption(checkOption);
rootCommand.AddOption(fixOption);
rootCommand.AddOption(jsonOption);

rootCommand.SetHandler((check, fix, json) =>
{
    // ... implementation ...

    if (json)
    {
        // Structured output for agent consumption
        Console.WriteLine(JsonSerializer.Serialize(new {
            tool = "naming-check",
            status = "pass", // or "fail"
            violations = new[] { /* ... */ },
            fixable = true
        }));
    }
    else
    {
        // Human-readable output with Spectre.Console
        AnsiConsole.MarkupLine("[green]All naming conventions pass[/]");
    }

    return check && hasViolations ? 1 : 0;
}, checkOption, fixOption, jsonOption);

return rootCommand.Invoke(args);
```

### Key Conventions for Generated Scripts

1. **Always support `--json`** — agents consume JSON; humans read rich text
2. **Always support `--check` and `--fix`** — dual-mode for verification and correction
3. **Include source hash** — detect when regeneration is needed
4. **Use `System.CommandLine`** — self-documenting with `--help`
5. **Exit codes** — 0 = pass, 1 = violations found, 2 = script error
6. **Structured JSON output schema**:

```json
{
  "tool": "script-name",
  "version": "1.0.0",
  "status": "pass|fail|error",
  "summary": "3 violations found, 2 auto-fixable",
  "violations": [
    {
      "file": "src/Api/UserController.cs",
      "line": 42,
      "rule": "naming/async-suffix",
      "message": "Async method 'GetUser' should end with 'Async'",
      "severity": "warning",
      "fixable": true
    }
  ],
  "stats": {
    "filesChecked": 47,
    "passed": 44,
    "failed": 3,
    "duration": "1.2s"
  }
}
```

---

## Skill Catalog — The Index

The system maintains a catalog file that agents can read to discover available skills:

```json
// .skill-to-cs/catalog.json
{
  "version": "1.0.0",
  "generated": "2026-03-26T10:00:00Z",
  "layers": {
    "user": "~/.skill-to-cs/skills/",
    "project": ".skill-to-cs/skills/",
    "components": {
      "src/api": "src/api/.skill-to-cs/skills/"
    }
  },
  "skills": [
    {
      "name": "build-check",
      "description": "Verify project builds with all analyzers enabled",
      "layer": "project",
      "path": ".skill-to-cs/skills/build-check.cs",
      "modes": ["check"],
      "category": "build",
      "sourceHash": "sha256:abc123..."
    },
    {
      "name": "naming-check",
      "description": "Enforce naming conventions from .editorconfig",
      "layer": "project",
      "path": ".skill-to-cs/skills/naming-check.cs",
      "modes": ["check", "fix"],
      "category": "standards",
      "sourceHash": "sha256:def456..."
    }
  ]
}
```

This catalog is:
- **Small** — agents load it cheaply to discover what's available
- **Machine-readable** — JSON, parseable by any agent
- **Regeneratable** — `dotnet run assess.cs` rebuilds it from project state

---

## Integration Points

### With Agent Skills (SKILL.md)

Each generated script can optionally produce a companion SKILL.md for cross-platform agent compatibility:

```
.claude/skills/naming-check/
  SKILL.md               # Agent Skills metadata + instructions
  scripts/
    naming-check.cs      # The actual script (symlink or copy)
```

### With CLAUDE.md / AGENTS.md

The catalog can auto-generate a section for inclusion in AGENTS.md:

```markdown
## Available Verification Scripts

Run `dotnet run .skill-to-cs/skills/<name>.cs --json` for structured output.

| Script | Description | Modes |
|--------|-------------|-------|
| build-check.cs | Build with analyzers | check |
| naming-check.cs | Naming conventions | check, fix |
| api-health.cs | API endpoint health | check |
```

### With CI/CD

Same scripts, same invocation:

```yaml
# .github/workflows/verify.yml
- run: dotnet run .skill-to-cs/skills/build-check.cs --check --json
- run: dotnet run .skill-to-cs/skills/naming-check.cs --check --json
```

### With Pre-Commit

```yaml
# .pre-commit-config.yaml
- repo: local
  hooks:
    - id: naming-check
      name: Naming conventions
      entry: dotnet run .skill-to-cs/skills/naming-check.cs --check
      language: system
      pass_filenames: false
```

---

## AOT Publishing Strategy

For environments without the .NET SDK (CI containers, agent sandboxes):

```bash
# Publish all skills as native binaries
dotnet publish .skill-to-cs/skills/naming-check.cs -r linux-x64 -o .skill-to-cs/bin/
# Result: .skill-to-cs/bin/naming-check (5-10MB, 5-30ms startup)
```

The catalog can reference both forms:

```json
{
  "name": "naming-check",
  "script": ".skill-to-cs/skills/naming-check.cs",
  "binary": ".skill-to-cs/bin/naming-check",
  "preferBinary": true
}
```

---

## Open Questions for Review

1. **Scope of generation** — Should the tool generate full script implementations, or scaffolds that developers fill in? Full generation risks being wrong; scaffolds require human effort.

2. **Assessment depth** — How deep should project scanning go? Marker files only? Parse source code? Read existing CI pipelines and extract the checks they run?

3. **Update strategy** — When project config changes (new `.editorconfig` rule), should scripts auto-regenerate? Or flag staleness and let the developer decide?

4. **Naming convention** — `.skill-to-cs/` directory? Or align with `.claude/skills/` for native integration?

5. **Multi-file limitation** — .NET 10 is single-file only. Complex scripts may need to `#:project` reference a shared library. Is this acceptable, or should we keep everything strictly single-file?

6. **Trust boundary** — Generated scripts run with full user permissions. Should there be a sandboxing story, or is "it's in your repo, you reviewed it" sufficient?

7. **Cross-language support** — The tool assesses any project type but generates .NET scripts. Should it also generate bash/Python alternatives for non-.NET teams?
