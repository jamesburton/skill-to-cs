# Prior Art Catalog — Tools, Standards & Patterns

> Reference index of everything discovered during research

---

## Standards & Specifications

| Name | What It Is | Relevance | Link |
|------|-----------|-----------|------|
| **Agent Skills (SKILL.md)** | Cross-platform skill format adopted by 26+ tools | Our scripts could emit SKILL.md wrappers for portability | agentskills.io |
| **AGENTS.md** | Always-loaded project context for AI agents (Linux Foundation AAIF) | Our catalog section could auto-generate into AGENTS.md | agents.md |
| **EditorConfig** | Cascading code style config with glob patterns | Model for our hierarchical layering (user > project > component) | editorconfig.org |
| **Cloud Native Buildpacks** | Detect/build project types via `bin/detect` scripts | Model for our assessment pipeline (detect phase) | buildpacks.io |
| **MCP (Model Context Protocol)** | JSON-RPC protocol for agent tool discovery | What our scripts can replace for many use cases | modelcontextprotocol.io |
| **llms.txt** | AI-friendly content index for websites | Analogous to our catalog.json for agent discovery | llmstxt.org |
| **AI-Rule-Spec** | YAML+Markdown format for agent rules with triggers/globs | Could inform our skill metadata format | aicodingrules.org |

---

## Tools & Frameworks

### Assessment & Detection

| Tool | What It Does | Relevance |
|------|-------------|-----------|
| **GitHub Linguist** | Detects project languages via heuristics + classifier | Model for our marker file detection |
| **Enry** | Go port of Linguist detection (used by Gitea, Sourcegraph) | Potential library for detection logic |
| **Tokei** | Fast language statistics via extension mapping | Simpler alternative for detection |
| **Rulens** | Extracts ESLint/Biome config into agent-consumable markdown | Direct inspiration — we do this but output scripts |
| **CLI-Anything** | Auto-generates CLI interfaces from existing software | Similar goal: make capabilities agent-accessible |

### Build & Task Systems

| Tool | Pattern We Can Learn From |
|------|--------------------------|
| **Nx** | Project graph plugins for auto-detecting project types and targets |
| **Turborepo** | Content-aware hashing — skip tasks when inputs unchanged |
| **Bazel** | Hermetic, cacheable rules with explicit inputs/outputs |
| **Taskfile.dev** | YAML task definitions with `--list` for self-documentation |
| **just** | Simple command runner, good UX patterns |

### .NET-Specific

| Tool | Relevance |
|------|-----------|
| **Roslyn Analyzers** | Encode naming, style, and architecture rules as build-time checks |
| **dotnet format** | `--verify-no-changes` for idempotent format checking |
| **.NET tool manifests** | `.config/dotnet-tools.json` — hierarchical local tool management |
| **StyleCop.Analyzers** | Additional .NET style enforcement |
| **NDepend** | Architecture rules as executable code queries |

### Agent Skill Ecosystems

| Framework | Stars | Key Pattern |
|-----------|-------|-------------|
| **Superpowers** (obra) | 57K | Enforced methodology: brainstorm > spec > plan > TDD > review |
| **BMAD Method** | 37K | 12+ specialized personas, high customization |
| **GitHub Spec Kit** | 71K | 4-phase workflow with approval gates |
| **oh-my-claudecode** | — | 32 agent types, model routing, delegation categories |

---

## Patterns & Practices

### Harness Engineering (OpenAI / Martin Fowler)

**Key insight:** Constraining the solution space makes agents more productive. OpenAI's Codex team:
- Built custom linters whose error messages are designed as agent remediation instructions
- Structural tests enforce architecture boundaries mechanically
- Entropy management agents find inconsistencies periodically
- Result: 1M+ lines of production code, zero human-written

**Our application:** Generated scripts serve as the harness. The linter output *is* the agent instruction.

Sources:
- martinfowler.com/articles/exploring-gen-ai/harness-engineering.html
- openai.com/index/harness-engineering/

### Factory.ai's Lint-as-Direction

Seven categories of lint rules that direct AI agents: grep-ability, glob-ability, architectural boundaries, security, testability, observability, documentation signals.

**Our application:** Assessment phase maps detected rules to these categories.

Source: factory.ai/news/using-linters-to-direct-agents

### Speakeasy's Dynamic Toolsets (100x Token Reduction)

Three-function pattern: `search_tools` > `describe_tools` > `execute_tool`. Lazy schema loading. Token usage dropped from ~150K to ~2K per session.

**Our application:** Our catalog.json acts as the discovery layer; full scripts are loaded only when needed.

Source: speakeasy.com/blog/how-we-reduced-token-usage-by-100x-dynamic-toolsets-v2

### Agent Decision Records (AgDR)

Extension of ADRs with agent-specific fields: `agent`, `model`, `trigger`, `timestamp`. Uses Y-Statement format. Operational states (`accepted`, `deprecated`, `superseded`) become directives.

**Our application:** Each generated script could record its decisions in AgDR format.

Source: github.com/me2resh/agent-decision-record

---

## Configuration Cascade Models

### Models to Draw From

| System | Cascade | Override Mechanism |
|--------|---------|-------------------|
| **EditorConfig** | Walk up to `root = true` | Glob sections, nearest wins |
| **Git config** | system > global > local > worktree | Later overrides earlier |
| **NuGet.Config** | Walk up directories | `<clear/>` to reset |
| **ESLint flat config** | Explicit array ordering | Index position determines priority |
| **.NET tool manifests** | Walk up to `isRoot: true` | Nearest manifest wins |

### Recommended Approach for skill-to-cs

**Hybrid:** EditorConfig-style walk-up for discovery, NuGet-style `clear` for explicit override.

```
1. Walk up from current directory to project root
2. At each level, if .skill-to-cs/config.json exists, merge it
3. "clear": true in any layer resets inherited skills for that category
4. Continue up to user level (~/.skill-to-cs/)
5. Final merged config determines available skills
```

---

## Token Efficiency Strategies

| Strategy | Mechanism | Reduction |
|----------|-----------|-----------|
| **Progressive disclosure** | Load name+description only, full script on demand | 90-97% |
| **Structured output** | `--json` flag produces minimal parseable output | 60-80% vs prose |
| **Linter-as-verification** | Run script, read exit code + errors vs. reading full files | 95%+ |
| **Convention over configuration** | Predictable layouts eliminate explanation | Indirect but significant |
| **Binary caching** | AOT publish once, invoke native binary thereafter | Startup: 170ms → 5-30ms |
| **Content-addressed skipping** | Hash inputs, skip if unchanged (Turborepo pattern) | 100% on cache hit |

---

## Security Considerations

- Snyk found prompt injection in 36% of audited skills
- Generated scripts run with full user permissions
- Mitigation: scripts are reviewed in PRs like any code
- Mitigation: scripts are deterministic (no LLM calls), reducing injection surface
- Mitigation: hash verification ensures scripts match their declared source config
- Tool: `parry` — prompt injection scanner for Claude Code hooks
