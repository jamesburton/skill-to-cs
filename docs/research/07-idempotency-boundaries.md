# Idempotency Boundaries: Where Scripts End and Judgment Begins

> The hard problem — decomposing tasks until each leaf is deterministic

---

## The Core Test

**If the same input triggers a consistent output, it should be a script.**

Not "similar" output. Not "usually the same" output. *Consistent.* If there's any ambiguity in what should happen given a specific input, it's not yet decomposed enough to be a script — or it genuinely requires judgment.

---

## The Decomposition Tree

A skill request arrives. It may or may not be fully scriptable. The job is to push determinism as deep as possible:

```
User/Agent Request
  │
  ├─ Can the entire request be resolved deterministically?
  │   YES → Single script, done
  │   NO  → Decompose...
  │
  ├─ Which parts are deterministic?
  │   → Script those parts
  │
  ├─ Which parts require a decision?
  │   ├─ Is the decision a lookup/classification? (finite options, clear criteria)
  │   │   YES → Script as a decision node (switch/match)
  │   │   NO  → Bubble up to judgment layer
  │   │
  │   └─ Can we narrow the decision to a constrained choice?
  │       YES → Present options to a small model / skill / user
  │       NO  → This is genuinely creative work, not scriptable
  │
  └─ Recurse on each sub-part until every leaf is:
      (a) A deterministic add/update/delete, OR
      (b) A constrained question for a judgment layer
```

---

## Five Layers of Determinism

| Layer | Determinism | Executor | Example |
|-------|------------|----------|---------|
| **L1: Mechanical** | 100% — pure function of inputs | Script | "Insert `MapGet("/users/{id}")` at line 24 of UsersEndpoints.cs" |
| **L2: Decision tree** | 100% — deterministic given the code state | Script calling sub-scripts | "Given rootPath `/api/users` and method `GET`, find the file, find the group, pick the insertion point" |
| **L3: Classification** | 95%+ — finite categories, clear heuristics | Script with fallback | "Is this a query or a command?" → heuristic: GET/DELETE = query, POST/PUT/PATCH = command. Edge cases exist. |
| **L4: Constrained judgment** | Variable — needs context but from a small option space | Small model / skill | "What should the response model properties be?" → model reads the entity, suggests properties, but the choice space is bounded |
| **L5: Creative** | Low — genuinely open-ended | Full model / human | "Design the API for this domain" |

**The goal of skill-to-cs is to push as much work as possible from L4/L5 down to L1/L2/L3.** And to clearly mark the boundaries.

---

## What Makes Something L1/L2 (Fully Scriptable)

A task is scriptable when ALL of:

1. **Inputs fully specify the output** — no ambiguity given the parameters
2. **Location is deterministic** — the target file and insertion point can be computed from inputs + project state
3. **Format is prescribed** — the exact code to generate follows a template with variable substitution
4. **Existence is checkable** — we can detect "already done" by pattern matching
5. **The operation is atomic** — it's a single add, update, or delete

### Examples of L1/L2 Operations

| Operation | Why It's Deterministic |
|-----------|----------------------|
| Add endpoint mapping line | Method + path + handler → exact code, exact location |
| Add DI registration | Interface + Implementation + Lifetime → exact line in exact file |
| Add NuGet package reference | Package + Version → exact XML in .csproj |
| Create DTO record | Name + Properties (with types) → exact file, exact content |
| Add route parameter binding | Parameter name + type → exact code in handler signature |
| Add authorization attribute | Policy name → exact `.RequireAuthorization()` chain |
| Register middleware | Type + Order → exact line in pipeline |

### Examples of L3 (Decision Tree, Usually Scriptable)

| Decision | Heuristic | Edge Case |
|----------|-----------|-----------|
| Which file does this endpoint go in? | rootPath prefix → filename convention | New area with no existing file → need to decide directory |
| Query or Command? | HTTP method mapping | POST that's actually a query (search endpoint) |
| Which test framework? | Package references in test project | Multiple test frameworks in same solution |
| Where to put the model? | Existing models directory convention | No convention established yet |
| What error codes apply? | Method + operation type → standard set | Domain-specific errors beyond the standard set |

At L3, the script can handle the common case and **signal** when it hits an edge case rather than guessing.

### Examples of L4 (Needs Judgment)

| Decision | Why It Needs Judgment |
|----------|----------------------|
| What properties should this DTO have? | Requires understanding the domain entity |
| What validation rules apply to this input? | Requires business logic knowledge |
| How should this be mapped? | May require understanding data shapes |
| What should the error messages say? | Requires context about the user experience |
| Should this be async? | Depends on whether the implementation will do I/O |

### Examples of L5 (Not Scriptable)

| Task | Why |
|------|-----|
| "Design the user management API" | Open-ended, many valid designs |
| "Refactor this service for better testability" | Requires understanding intent and trade-offs |
| "Fix this bug" | Requires diagnosis, which is inherently exploratory |

---

## The Sub-Script Architecture

Scripts call sub-scripts down the decision tree. Each level handles one category of decision:

```
add-endpoint.cs (L3: orchestrator — resolves which file, which group)
  │
  ├── resolve-endpoint-file.cs (L2: finds or creates the right file)
  │     ├── find-by-convention.cs (L2: match rootPath to existing file)
  │     └── scaffold-endpoint-file.cs (L1: create new file from template)
  │
  ├── resolve-insertion-point.cs (L2: finds where in the file)
  │     ├── find-method-group.cs (L2: locate MapGet/MapPost group)
  │     └── find-ordered-position.cs (L1: sort by method, find slot)
  │
  ├── generate-endpoint-code.cs (L1: template substitution)
  │     ├── generate-handler-signature.cs (L1: params from schema)
  │     ├── generate-result-type.cs (L1: union from error codes)
  │     └── generate-auth-chain.cs (L1: from roles list)
  │
  ├── ensure-request-model.cs (L2: check if exists, scaffold if not)
  │     └── scaffold-record.cs (L1: create record with properties)
  │           └── ??? what properties? → BUBBLE TO L4
  │
  └── ensure-response-model.cs (L2: check if exists, scaffold if not)
        └── scaffold-record.cs (L1)
              └── ??? what properties? → BUBBLE TO L4
```

The tree is deterministic down to the point where we need to know DTO properties. At that leaf, we have three options:

1. **User provided them** — parameters included `requestModel: "CreateUserRequest { string Name, string Email }"` → fully deterministic, stay in script
2. **Infer from existing code** — scan the entity, derive properties → L3 heuristic, sometimes right
3. **Can't determine** — bubble back up, ask for input

---

## Bubble-Back Mechanism

When a script hits a decision it can't make, it needs to signal this clearly — not guess, not hallucinate, not pick a default that might be wrong.

### Option A: Return a Question

The script returns structured output indicating what it needs:

```json
{
  "status": "needs_input",
  "completed": [
    { "step": "resolve-file", "result": "src/Api/Endpoints/UsersEndpoints.cs" },
    { "step": "resolve-insertion", "result": "line 42, after MapGet" }
  ],
  "blocked_on": {
    "step": "ensure-request-model",
    "question": "What properties should CreateUserRequest have?",
    "context": {
      "entity": "User",
      "entity_properties": ["Id: int", "Name: string", "Email: string", "CreatedAt: DateTime"],
      "existing_dtos": ["UserDto { int Id, string Name, string Email }"],
      "suggestion": "Based on entity User, likely: string Name, string Email"
    },
    "options": [
      { "label": "Derive from entity (exclude Id, CreatedAt)", "value": "string Name, string Email" },
      { "label": "Mirror UserDto", "value": "int Id, string Name, string Email" },
      { "label": "Custom", "value": null }
    ]
  },
  "resume_with": "skill-to-cs generate api-endpoint --continue <session-id> --requestModelProperties 'string Name, string Email'"
}
```

This is the **constrained choice** pattern. The script did all the deterministic work, narrowed the judgment to a specific bounded question, and provided options with context.

### Option B: Delegate to a Small Model

For L4 decisions that follow a pattern, a small/fast model (Haiku-class) can often resolve them:

```
Script → "Given entity User with properties [Id, Name, Email, CreatedAt, PasswordHash],
          what properties should a CreateUserRequest have?
          Rules: exclude auto-generated (Id), exclude audit (CreatedAt),
          exclude sensitive (PasswordHash), include all others."

Haiku → "string Name, string Email"
```

This is cheap (~50 tokens), fast, and usually right. The script provides the constraint frame; the model provides the judgment within that frame.

### Option C: Bubble to a Full Skill

Some decisions are too complex for a small model but too specific for a human to want to answer manually. A skill (prompt-based, running a capable model) can handle these:

```
"Design the request and response models for a user creation endpoint.
 Context: [entity schema, existing DTOs, project conventions].
 Output: JSON with property definitions."
```

### When to Use Which

| Bubble Target | When |
|---------------|------|
| **Return question (to user/agent)** | Few options, user knows best, one-time decision |
| **Small model (Haiku)** | Pattern-based judgment, high confidence, cheap to retry if wrong |
| **Full skill** | Multiple interdependent decisions, needs broader context understanding |
| **Full model (human-in-loop)** | Genuinely novel, no pattern to follow, high stakes |

---

## Categorising the Boundaries

To build this, we need to map every rule's decision tree and categorise each node. Here's a framework:

### Per-Rule Boundary Map

For each rule, document every decision point and its layer:

```
Rule: api-endpoint

Decision Tree:
├── [L2] Which file? → convention lookup (rootPath → filename)
│     └── [L3] File doesn't exist → create? (usually yes, but check config)
├── [L2] Which group? → parse file structure
├── [L1] What code? → template substitution from params
├── [L2] Request model exists? → type lookup
│     ├── [L1] Yes → reference it
│     └── [L3/L4] No → scaffold it
│           ├── [L1] Properties provided in params → use them
│           ├── [L3] Entity exists → derive (heuristic: exclude auto-gen fields)
│           └── [L4] Neither → BUBBLE (ask or infer)
├── [L2] Response model exists? → type lookup
│     ├── [L1] Yes → reference it
│     └── [L3/L4] No → same as request model
├── [L1] Auth chain → roles list → RequireAuthorization calls
├── [L1] Error types → errorCodes → Results<> union
└── [L2] DI registration needed? → check if file is new
      └── [L1] Yes → insert registration line
```

When the tree is mapped like this, we can see:
- **How much of the rule is L1/L2** (fully scriptable) — this is the ROI
- **Where the L3/L4 boundaries are** — this is the complexity budget
- **What information would push L3/L4 down to L2/L1** — this tells us what parameters to add

---

## The Parameter Completeness Spectrum

The same rule can operate at different layers depending on how much the caller provides:

```
Full params (all L1):
  skill-to-cs generate api-endpoint \
    --rootPath /api/users \
    --method POST \
    --path / \
    --requestModel "CreateUserRequest { string Name, string Email }" \
    --responseModel "UserDto { int Id, string Name, string Email }" \
    --roles admin \
    --errorCodes 400,409

Partial params (some L3/L4):
  skill-to-cs generate api-endpoint \
    --rootPath /api/users \
    --method POST \
    --path /
    # No models specified → needs to figure them out

Minimal params (mostly L4):
  skill-to-cs generate api-endpoint \
    --rootPath /api/users
    # Only the root path → needs to figure out method, route, models, everything
```

**Key insight:** The tool should work at ALL completeness levels. When fully specified, it's pure L1 scripting. When partially specified, it fills in what it can deterministically and asks about the rest. When minimally specified, it either returns a rich set of questions or delegates to a model.

---

## The Progressive Narrowing Pattern

Rather than trying to handle everything in one pass, the tool can narrow progressively:

```
Pass 1 (script): Resolve everything deterministic
  → "I've determined: file=UsersEndpoints.cs, insertion=line 42,
     auth=RequireAuthorization("admin"). Still need: request model properties."

Pass 2 (small model or user): Answer the outstanding questions
  → "Request model: string Name, string Email"

Pass 3 (script): Now everything is L1, generate the code
  → Deterministic output, idempotent
```

This means the engine needs:
1. **Session state** — track what's been resolved and what's pending
2. **Resume capability** — pick up where we left off with new information
3. **Partial execution** — do the deterministic parts, pause at judgment points

---

## Implications for Architecture

### The Script Isn't Always a Script

The execution unit at each node might be:

| Node Type | Implemented As | Invoked How |
|-----------|---------------|-------------|
| **L1 leaf** | Pure function in a .cs script | Direct call, no I/O beyond file write |
| **L2 lookup** | Script that reads project state | Roslyn parse, file glob, regex match |
| **L3 heuristic** | Script with confidence score | Returns result + confidence; low confidence → escalate |
| **L4 judgment** | Prompt template for a model | Small model call via CLI/API, or return question |
| **L5 creative** | Full skill/agent | Bubble all the way up to the calling agent |

### What This Means for V1

V1 should focus on mapping the boundaries, not pretending everything is L1/L2.

**V1 revised approach:**
1. Build the decision tree framework (nodes, edges, layer classification)
2. Implement the L1/L2 paths for 3 rules (the deterministic parts)
3. Implement the bubble-back mechanism (return questions as structured JSON)
4. Handle the "fully specified" case completely (all params provided → pure L1)
5. Handle the "partially specified" case by returning structured questions
6. Defer L4 model integration to V2

**V1 does NOT:**
- Call models itself
- Guess when uncertain
- Pretend L3/L4 decisions are L1

**V1 DOES:**
- Clearly signal what it can and can't resolve
- Do all deterministic work
- Return actionable questions when stuck
- Accept answers and resume

This makes V1 useful immediately: an agent calls `generate`, gets back structured questions for the parts it can't determine, answers them, calls `generate --continue` with the answers, and gets deterministic output.

---

## Open Questions

1. **Confidence thresholds for L3** — When a heuristic is "90% sure," should it just do it and flag uncertainty, or always ask? Configurable per-rule?

2. **Session persistence** — How long does a partial execution session live? In-memory for the current invocation? Written to disk for multi-turn?

3. **Model integration shape (V2)** — Should skill-to-cs call models directly (embed an API client), or should it always bubble to the calling agent/skill? The latter is simpler and decoupled.

4. **Sub-script granularity** — How small should leaf scripts be? One per file operation? One per decision node? Too granular = overhead; too coarse = can't reuse.

5. **Learning from resolutions** — When a user answers an L4 question, should that answer become a heuristic for next time? (e.g., "for this project, CreateXxxRequest always mirrors the entity minus Id and audit fields")
