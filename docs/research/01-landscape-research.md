# Research: Landscape Analysis — Scripts as Agent Skills

> Synthesized from 4 parallel research streams, March 2026

---

## 1. The Problem Space

AI coding agents (Claude Code, Codex, Gemini CLI, Cursor, etc.) consume engineering knowledge through two channels:

1. **Prose instructions** — CLAUDE.md, AGENTS.md, cursor rules. Loaded into context every turn, consuming tokens even when irrelevant.
2. **Tool definitions** — MCP servers, built-in tools. Each tool's JSON Schema burns 200-500 tokens of context per definition, per request.

Both channels are **expensive** (tokens), **fragile** (instructions drift from reality), and **non-verifiable** (the agent may or may not follow prose guidance).

The alternative: **executable scripts** that encode knowledge as deterministic, idempotent, versionable code. The agent runs the script and reads structured output, rather than interpreting prose instructions about what to do.

---

## 2. MCP vs CLI Scripts — When Each Wins

### Where MCP Adds Value

| Scenario | Why MCP |
|----------|---------|
| Multi-client tool discovery | Standardized `tools/list` across agents |
| Stateful sessions | DB connections, authenticated browser, long-lived resources |
| Security boundaries | Mediated access with fine-grained permissions |
| Cross-agent platform | Multiple agents share the same capability server |

### Where CLI Scripts Win

| Scenario | Why Scripts |
|----------|-------------|
| Agent already has shell access | No wrapper tax — just execute |
| Core dev workflows | Build, test, lint, format — existing CLIs are better |
| Versionability | Scripts tracked in git alongside the code they serve |
| Token efficiency | No tool definition overhead; lean structured output |
| Debuggability | Run manually, pipe, inspect with standard tools |
| Idempotency | Logic is explicit and inspectable |
| CI reuse | Same script used by pre-commit, CI, and agent |

### Token Cost Comparison

| Component | MCP Server (10 tools) | CLI Equivalent |
|-----------|----------------------|----------------|
| Tool definitions per request | 2,000-5,000 tokens | 0 (bash tool already defined) |
| Per-call protocol overhead | 100-300 tokens | 20-50 tokens |
| Response overhead | 50-200 tokens | Near zero |
| **10 calls in a session** | **3,000-8,000 extra tokens** | **Baseline** |

Speakeasy demonstrated a **96-97% token reduction** by switching from static MCP tool loading to dynamic/lazy tool discovery.

### The Emerging Consensus

**Two-tier architecture:**
- **Scripts** for core, frequently-used, project-specific operations
- **MCP** for standardized discovery, stateful integrations, and cross-platform sharing

Claude Code itself validates this: its core capabilities (Read, Write, Edit, Grep, Glob, Bash) are direct tools, not MCP. MCP is reserved for extensibility.

---

## 3. The Agent Skills Ecosystem

### The Standard: SKILL.md

The Agent Skills open standard (agentskills.io) is now adopted by 26+ platforms including Claude Code, Codex, Gemini CLI, Cursor, GitHub Copilot, and Roo Code.

A skill is a directory:

```
skill-name/
  SKILL.md          # Metadata + instructions (YAML frontmatter + markdown)
  scripts/          # Executable code
  references/       # Documentation
  assets/           # Templates, resources
```

**Progressive disclosure** manages token budgets:
1. Name + description (~100 tokens) loaded at startup for all skills
2. Full SKILL.md (<5000 tokens recommended) loaded when skill activates
3. Scripts/references/assets loaded only when needed

### Layering Across Platforms

Every major agent supports hierarchical configuration:

| Level | Claude Code | Cursor | Copilot | Gemini CLI |
|-------|-------------|--------|---------|------------|
| Organization | Enterprise settings | Business rules | Org config | Workspace admin |
| User | `~/.claude/skills/` | User settings | User instructions | `~/.gemini/GEMINI.md` |
| Project | `.claude/skills/` | `.cursor/rules/` | `.github/copilot-instructions.md` | Root `GEMINI.md` |
| Component | Nested skills | Per-file rules | `.instructions.md` + globs | Subdirectory `GEMINI.md` |

### Complementary Standards

| Standard | Purpose | When Loaded |
|----------|---------|-------------|
| AGENTS.md | Always-on project context | Every request |
| SKILL.md | On-demand capabilities | When triggered |
| MCP | Runtime tool discovery | When connected |

---

## 4. Engineering Guidelines as Code

### The Harness Engineering Paradigm

OpenAI's Codex team built a 1M+ line production app with zero human-written code. The key insight: **constraining the solution space makes agents more productive**. Their harness included:

- Deterministic custom linters with error messages designed as agent remediation instructions
- Structural tests enforcing architectural boundaries
- Entropy management agents running periodically

**Core principle:** Don't explain rules to the agent in prose when you can enforce them mechanically. The linter error message *is* the agent guidance. The build failure *is* the instruction. The convention *is* the documentation.

### Guideline Encoding Hierarchy

From most to least effective:

| Level | Mechanism | Token Cost | Reliability |
|-------|-----------|------------|-------------|
| **Enforced** | Build errors, linter failures, type checker | 0 (agent reads output) | 100% — cannot be bypassed |
| **Automated** | Pre-commit hooks, formatters, fixers | 0 (runs automatically) | 99% — can be skipped with --no-verify |
| **Scripted** | Verification scripts (check + fix) | Minimal (exit code + errors) | 95% — agent must choose to run |
| **Documented** | AGENTS.md, CLAUDE.md | High (loaded every turn) | 70% — agent may ignore or misinterpret |
| **Implicit** | Convention, team knowledge | 0 (but unreliable) | 30% — agent cannot know what isn't written |

### The Verification Script Pattern

The strongest pattern: scripts that both **check** (exit non-zero on failure) and **fix** (auto-correct):

```
verify.cs / verify.sh
  - dotnet format --verify-no-changes
  - dotnet build (with EnforceCodeStyleInBuild=true)
  - dotnet test
  - custom structural checks

Used identically by: [Pre-commit] [CI/CD] [AI Agent]
```

When an agent runs `dotnet run verify.cs` and gets structured pass/fail output, that's cheaper and more reliable than reading a 200-line CLAUDE.md section about coding standards.

### Factory.ai's Lint-as-Agent-Direction Categories

1. **Grep-ability** — Named exports, consistent error types make code searchable
2. **Glob-ability** — Predictable file layout (`enums.ts`, `types.ts`, colocated `.test.ts`)
3. **Architectural boundaries** — Module restrictions prevent cross-layer violations
4. **Security & privacy** — Block secrets, enforce input validation
5. **Testability** — Colocated tests, no network calls in unit tests
6. **Observability** — Structured logging with metadata
7. **Documentation signals** — Module docstrings and ADR references

---

## 5. Project Capability Detection

### Detection Pipeline

```
1. MARKER FILES      → Identify project type(s)
   package.json, *.csproj, Cargo.toml, go.mod, pyproject.toml

2. MANIFEST PARSING  → Read available scripts/tools
   npm scripts, dotnet tools, Makefile targets, Taskfile

3. CONVENTION SCAN   → Check standard directories
   scripts/, tools/, .github/, .claude/skills/

4. CONTENT ANALYSIS  → Parse configs for frameworks/capabilities
   Read tsconfig.json, .editorconfig, NuGet.config

5. AGGREGATION       → Merge into a unified catalog
```

### Hierarchical Layering Models

The dominant pattern is **nearest-ancestor wins with inheritance**, following EditorConfig/Git config:

```
~/.config/tool/config       (user defaults)
  /project/.toolrc          (project overrides)
    /project/src/.toolrc    (directory overrides)
```

Merge strategies vary:

| Strategy | Example | Behavior |
|----------|---------|----------|
| Last wins | Git config | Later value replaces |
| Deep merge | webpack-merge | Objects merged recursively |
| Array concat | Babel presets | Lists concatenated |
| Explicit replace | NuGet `<clear/>` | Reset inherited values |

---

## 6. Idempotency Patterns

### Core Patterns for Scripts

| Pattern | Example |
|---------|---------|
| **Check-before-act** | `mkdir -p`, `CREATE TABLE IF NOT EXISTS` |
| **Exit code conventions** | 0 = success (including already-in-state), 1 = failure |
| **Declarative state** | Script describes desired state, converges idempotently |
| **Content-addressed guards** | Hash inputs, skip if unchanged |
| **Atomic operations** | Write to temp, rename on success |

### Idempotency in Scripts vs MCP

MCP tools handle idempotency internally — the server author must implement it, and there's no protocol-level guarantee. CLI scripts make idempotency **explicit and inspectable** — you can read the script to verify the pattern.

---

## 7. Versioning & Reproducibility

### Scripts Beat MCP for Version Control

| Aspect | Scripts in Repo | MCP Servers |
|--------|----------------|-------------|
| Version tracking | Full git history | External process, may drift |
| Code review | Standard PR process | Changes may bypass review |
| Reproducibility | Checkout any commit | Runtime version may differ |
| Environment parity | Same in dev/CI/prod | Config varies per environment |
| Rollback | `git revert` | Must redeploy server |
| Testing | CI runs them directly | Requires running the server |

### Tool Version Pinning Patterns

| Ecosystem | Lock Mechanism |
|-----------|---------------|
| npm | `package-lock.json` |
| .NET | `packages.lock.json` (opt-in), `.config/dotnet-tools.json` |
| Cargo | `Cargo.lock` |
| Go | `go.sum` |
| Nix | `flake.lock` (most rigorous — pins everything to exact Git SHA) |

---

## 8. The .NET 10 Single-File Opportunity

### Why .NET 10 File-Based Apps Fit This Space

| Advantage | Detail |
|-----------|--------|
| Type safety | Compile-time checks catch errors before execution |
| NuGet inline | `#:package Roslyn@*` — pull in code analysis, CLI parsing, rich output |
| Native AOT | 5-30ms startup, 3-10MB binaries — fast as shell scripts |
| Cross-platform | Windows, Linux, macOS from the same `.cs` file |
| Versionable | Single file in git, clean diffs in PRs |
| Shebang | `#!/usr/bin/env dotnet` — directly executable on Unix |
| Self-contained | Publish to standalone binary, no SDK needed at runtime |
| Upgradeable | `dotnet project convert app.cs` when complexity grows |

### Comparison with Alternatives

| Feature | .NET 10 | Python | Bash | PowerShell |
|---------|---------|--------|------|------------|
| Type safety | Compile-time | Runtime | None | Limited |
| Package management | Inline `#:package` | pip/venv | Manual | PSGallery |
| Native binary | AOT (3-10MB, 5-30ms) | No | N/A | No |
| Startup (interpreted) | ~170ms | ~50ms | ~5ms | ~200ms |
| Startup (AOT) | 5-30ms | N/A | N/A | N/A |
| Cross-platform | Yes | Yes | Unix only | Yes |
| IDE support | Full | Good | Limited | Good |
| Multi-file | .NET 11 | Yes | Yes | Yes |

---

## Key Sources

| Topic | Source |
|-------|--------|
| Harness Engineering | Martin Fowler, OpenAI |
| Agent Skills Spec | agentskills.io |
| Lint-as-Agent-Direction | Factory.ai |
| Rulens (lint-to-docs) | mh4gf.dev |
| Token Reduction (100x) | Speakeasy |
| AGENTS.md Standard | agents.md / Linux Foundation AAIF |
| CLI-Anything | HKUDS (GitHub) |
| Architecture Decision Records | joelparkerhenderson/architecture-decision-record |
| .NET 10 File-Based Apps | Microsoft Learn |
| Cloud Native Buildpacks | buildpacks.io |
| EditorConfig Cascade | editorconfig.org |
