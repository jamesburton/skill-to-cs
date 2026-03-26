# skill-to-cs

A .NET CLI tool that assesses project structure, detects conventions, and generates idempotent verification and generation scripts. It scans your codebase for patterns (API endpoints, services, test classes) and produces scripts that can check compliance, generate boilerplate, or enforce standards -- all designed to be re-runnable without side effects.

## Installation

```bash
dotnet tool install -g skill-to-cs
```

## Quick Start

```bash
skill-to-cs init           # Create .skill-to-cs/ config directory
skill-to-cs assess         # Detect project structure and applicable rules
skill-to-cs scan api-endpoint  # Find all API endpoint instances
```

## Command Reference

| Command | Description |
|---------|-------------|
| `init` | Initialize a `.skill-to-cs/` configuration directory |
| `assess` | Assess project structure and detect applicable rules |
| `describe <rule>` | Describe a rule and its parameters |
| `scan <rule>` | Scan project for rule instances |
| `generate <rule>` | Generate code from a rule |
| `verify <rule>` | Verify rule compliance |
| `catalog` | Build a catalog of available rules |
| `check` | Run all checks against the project |
| `feedback` | Record feedback for a rule instance |

All commands accept `--path` (project root), `--json` (machine-readable output), and `--verbose` flags.

## Example Workflow

```bash
# 1. Assess: discover what rules apply to this project
skill-to-cs assess

# 2. Scan: find existing instances of a pattern
skill-to-cs scan api-endpoint

# 3. Generate: produce code from a rule template
skill-to-cs generate api-endpoint --param controller=OrdersController --param route=/api/orders

# 4. Verify: check that generated code meets the rule
skill-to-cs verify build-check

# 5. Check: run all applicable verification rules at once
skill-to-cs check
```

## Built-in Rules

### Generation Rules

| Rule | Description |
|------|-------------|
| `api-endpoint` | Detect and generate ASP.NET API controller endpoints |
| `service` | Detect and generate service class registrations |
| `test-class` | Detect and generate test class scaffolding |

### Verification Rules

| Rule | Description |
|------|-------------|
| `build-check` | Verify the project builds with zero warnings/errors |
| `format-check` | Verify code formatting matches .editorconfig rules |
| `test-runner` | Run the test suite and report results |
| `tools-check` | Verify .NET local tools are restored and functional |

## Assessment Detectors

The `assess` command runs detectors that scan for project characteristics:

| Detector | Priority | Looks For |
|----------|----------|-----------|
| `dotnet` | 10 | `.csproj` files, target framework, analyzers |
| `editorconfig` | 20 | `.editorconfig` rules, naming/formatting conventions |
| `test` | 30 | Test projects, framework (xunit/NUnit/MSTest), coverage tools |
| `git` | 40 | `.git/` directory, hooks, `.gitignore` |
| `tool-manifest` | 50 | `.config/dotnet-tools.json` and listed tools |
| `agent-config` | 60 | `CLAUDE.md`, `AGENTS.md`, `.cursor/rules/`, `.claude/skills/` |

## JSON Output for Agents

Every command supports `--json` for structured output suitable for AI agent consumption:

```bash
skill-to-cs assess --json
skill-to-cs scan api-endpoint --json
skill-to-cs check --json
```

This makes skill-to-cs a building block for automated pipelines and agent-driven workflows.

## Architecture

See `docs/research/` for detailed design documents:

- [Landscape Research](docs/research/01-landscape-research.md)
- [Design Guidance](docs/research/02-design-guidance.md)
- [Prior Art Catalog](docs/research/03-prior-art-catalog.md)
- [Implementation Plan](docs/research/04-implementation-plan.md)
- [Rule Engine Design](docs/research/05-rule-engine-design.md)
- [Revised Implementation Plan](docs/research/06-revised-implementation-plan.md)
- [Idempotency Boundaries](docs/research/07-idempotency-boundaries.md)
- [Decisions and Refinements](docs/research/08-decisions-and-refinements.md)

## Requirements

- .NET 10 SDK

## License

See [LICENSE](LICENSE) for details.
