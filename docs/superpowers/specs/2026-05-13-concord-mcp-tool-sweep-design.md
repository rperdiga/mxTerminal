---
title: Concord MCP tool-sweep — design
date: 2026-05-13
status: approved
owner: rperdiga
related_paths:
  - src/Concord.Core/Mcp/StudioProActionServer.cs
  - src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs
  - src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs
---

# Concord MCP tool-sweep — design

## Problem

Concord exposes 87 MCP tools across ten families (DomainModel, Microflows,
Pages, Workflows, Security, Diagnostics, UiActions, ProjectSettings,
ConstantsEnums, Navigation). The server's `tools/list` advertises every tool
with placeholder schemas (`"description": "Concord SPMCP tool (Family). Schema
TBD."`, `inputSchema: {type:"object", additionalProperties:true}`), so neither
clients nor humans have a contract for what each tool expects. Three concrete
bugs are already known before any test run:

1. `read_project_info` throws `KeyNotFoundException` for
   `Mendix.Modeler.ExtensionLoader.ModelProxies.Projects.ModuleProxy` — origin
   inside the per-entity walk at
   [`MendixDomainModelTools.cs:769-775`](../../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs).
2. `get_studio_pro_logs` hardcodes the log directory to Mendix `11.5.0`
   ([`MendixAdditionalTools.cs:658`](../../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)),
   which doesn't exist on the test machine (`10.24.13.86719` and `11.10.0` do).
   The tool returns "no entries" silently.
3. `tools/list` placeholder schemas across every tool, with the downstream
   effect that args-shape errors surface as runtime rejections instead of
   client-side validation. Historical terminal.log shows this in action with
   `generate_overview_pages` (five iterations of error-message tweaks visible
   in the log).

Plus: there is no `clear_logs` tool in the catalog, so a test run has no clean
"start from zero" affordance — we work around with byte-offset baselines on
the existing logs.

The work is to systematically exercise all 87 tools, capture every failure
with a reproducible input, fix what we find, re-test, and finish with an
in-Studio-Pro visual verification pass.

## Constraints

- **Blast radius:** mutations may target any user module *except* Atlas_Core,
  Atlas_Web_Content, NanoflowCommons, WebActions, DataWidgets (system /
  app-store modules). The project under test (`C:\Projects\Test_10_24_13`) is
  treated as a one-shot resource — residue is acceptable.
- **App-lifecycle coverage:** all 87 tools, including `run_app`, `stop_app`,
  `save_all`, `refresh_project`, are exercised in this phase. App-lifecycle
  tools run last; `refresh_project` is dead-last because it invalidates the
  cached model.
- **Output:** a Markdown report (human triage) plus a JSON ledger (machine
  replay), both stored under `tests/concord-mcp-sweep/`.
- **Fix cadence:** test all → triage → fix → re-test only failures → in-Studio
  visual pass at the end.

## Architecture

Three artifacts under `tests/concord-mcp-sweep/`, plus one driver under
`scripts/`:

```
scripts/concord-mcp-sweep.ps1                ← driver (PowerShell, ~200 lines)
tests/concord-mcp-sweep/matrix.jsonc         ← reproducibility contract (87 entries)
tests/concord-mcp-sweep/arg-shapes.md        ← schema-discovery audit trail
tests/concord-mcp-sweep/findings.json        ← machine-replayable ledger (driver output)
tests/concord-mcp-sweep/findings.md          ← human triage report (driver output)
```

### `matrix.jsonc` — the reproducibility contract

JSONC so each entry can carry inline `// notes`. One record per tool. Schema:

```jsonc
{
  "name": "list_modules",          // server-side tool name
  "family": "DomainModel",         // for findings.md grouping
  "phase": "read",                 // "read" | "mutate" | "lifecycle"
  "args": {},                      // exact JSON-RPC params.arguments
  "expected": "ok",                // "ok" | "error" | "either"
  "notes": "No args required; returns all 10 modules."
}
```

- `phase` drives execution order: `read` → `mutate` → `lifecycle`. Original
  matrix order is preserved within a phase.
- `expected`:
  - `"ok"` — payload `success:true` and no `error:` → PASS
  - `"error"` — we *want* a structured failure (e.g. `validate_name` against
    an invalid name) → PASS only if payload has `error:` / `success:false`
  - `"either"` — don't classify by content (e.g. `get_studio_pro_logs`
    returns empty when nothing recent)

Mutation entries name their primary target deterministically with a suffix
matching the tool name:

- `create_entity` → `MyFirstModule.SweepEntity_create_entity`
- `add_attribute` → adds `SweepAttr_add_attribute` to that entity
- `rename_entity` → renames `SweepEntity_rename_entity` →
  `SweepEntityRenamed_rename_entity`

`git grep Sweep` in findings.md and matrix.jsonc cross-references any
mutation's exact repro. A re-run is idempotent because every name is
deterministic.

### `scripts/concord-mcp-sweep.ps1` — the driver

```powershell
./scripts/concord-mcp-sweep.ps1 `
  [-Matrix tests/concord-mcp-sweep/matrix.jsonc] `
  [-OutDir tests/concord-mcp-sweep] `
  [-Endpoint http://127.0.0.1:7783/mcp] `
  [-Only tool1,tool2]   # filter to specific tool names (used for re-test pass)
  [-Phase read,mutate]  # filter by phase
  [-DryRun]             # print the plan, don't POST
```

Behavior:

1. **Pre-flight:** POST `initialize`, then `tools/list`. Cross-check matrix
   names against server names. Drift logged as `MISSING` / `EXTRA` findings;
   does **not** abort the run.
2. **Iterate** entries in `phase` order, preserving original within-phase
   order. For each: POST `tools/call`, capture full JSON-RPC envelope plus
   elapsed ms.
3. **Classify** (see below).
4. **Persist** incrementally: after every entry, atomic-write `findings.json`
   (temp + rename) and re-render `findings.md`. A mid-sweep abort leaves
   accurate partial findings on disk.

### Output artifacts

- **`findings.json`** — array of records, one per attempted tool:
  ```json
  {
    "name": "...", "family": "...", "phase": "...",
    "status": "PASS" | "FAIL" | "SKIP",
    "expected": "ok" | "error" | "either",
    "args": { ... },
    "raw_response": { ... },          // full JSON-RPC envelope
    "error_summary": "string or null",
    "severity": "CRASH | BUG | SCHEMA | STALE | MISSING | SIDE-EFFECT | TRANSPORT | TIMEOUT | null",
    "elapsed_ms": 42,
    "timestamp": "2026-05-13T23:42:00Z"
  }
  ```
- **`findings.md`** — human triage. Sections:
  - **Top:** summary table (`X/87 PASS, Y FAIL, Z SKIP`) plus a "blocking
    findings" banner if >50% of `read` phase failed.
  - **Per family:** failures first, each with severity badge, args sent,
    response snippet, suspected root cause (`file.cs:line`), proposed-fix
    sketch.
  - **Bottom:** PASS list (compact).

## Workflow phases

```
Phase 0 — Pre-flight     initialize + tools/list + matrix cross-check
Phase 1 — Sweep          run matrix, write findings.json/.md
Phase 2 — Triage         read findings.md, classify, prioritize
Phase 3 — Fix            edit C# source; rebuild & redeploy
Phase 4 — Re-test        ./concord-mcp-sweep.ps1 -Only <failing-tools>
Phase 5 — Studio Pro     manual visual verification (after Phase 4 green)
```

Phases 2-4 loop until all targeted fixes are green.

### Phase 1 sweep order (read → mutate → lifecycle)

**`read` phase (~40 tools):** all `list_*`, `read_*`, `query_*`, `validate_*`,
`check_*`, `diagnose_*`, `analyze_*`, `audit_*`, `get_*`, plus
`list_available_tools` and `list_available_tools_domain`. Known pre-sweep
failures (`read_project_info`, `get_studio_pro_logs`) are left in the matrix
so they appear in findings rather than being silently skipped.

**`mutate` phase (~44 tools), dependency-ordered:**

1. Targets first — `add_attribute`, `create_entity`, `create_multiple_entities`,
   `create_domain_model_from_schema` against `MyFirstModule` and `Sales`
   (both empty, blast-radius-tolerant).
2. Modify targets — `update_attribute`, `rename_attribute`,
   `set_calculated_attribute`, `configure_system_attributes`,
   `add_event_handler`, `rename_entity`, `set_entity_generalization`,
   `remove_entity_generalization`, `set_documentation`, `copy_model_element`.
3. Wire associations — `create_association`, `create_multiple_associations`,
   `update_association`, `rename_association`.
4. Constants & enums — full create / update / rename / configure cycle.
5. Microflows — `create_microflow`, `update_microflow`,
   `create_microflow_activity`, `create_microflow_activities_sequence`,
   `modify_microflow_activity`, `insert_before_activity`, `set_microflow_url`.
6. Pages & navigation — `generate_overview_pages`, `manage_navigation`,
   `manage_folders`, `arrange_domain_model`.
7. Project settings — `set_runtime_settings`, `set_configuration`,
   `sync_filesystem`.
8. Deletes last — `delete_model_element`, `delete_document`,
   `exclude_document`, `rename_document`, `rename_module` (only on a
   sweep-created module if `create_module` ran successfully).

**`lifecycle` phase (4 tools, strict order):**
`save_all` → `run_app` → **poll `get_app_status` every 1s until it reports
the app is running, capped at 30s** → `stop_app` → `refresh_project`
(invalidates cached state, so dead-last). The driver handles the poll inline
between matrix entries; the matrix itself only lists the four
tool entries.

### Stop conditions

| Condition | Behavior |
|---|---|
| Server unreachable in Phase 0 | Abort with clear error. No artifacts written. |
| `tools/list` mismatch with matrix | Log MISSING / EXTRA findings; continue. |
| >50% of `read` phase fails | Banner in findings.md (`LIKELY SERVER MISCONFIGURATION`); continue. |
| Server-side CRASH on one tool | Record finding; continue. The whole point is the harvest. |
| Mid-sweep abort (Ctrl-C, timeout) | Incremental findings on disk survive. |

## Schema discovery

I'll grep both [`MendixDomainModelTools.cs`](../../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)
and [`MendixAdditionalTools.cs`](../../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)
for the access pattern every tool uses:

```csharp
public async Task<string> SomeTool(JsonObject parameters)
{
    var moduleName = parameters?["module_name"]?.ToString();
    var entityName = parameters?["entity_name"]?.ToString();
    ...
```

For each tool, extract: required fields (referenced without null-fallback or
with an explicit "X is required" check), optional fields, expected JSON
shape (string / object / array). The derived shape lands in
`tests/concord-mcp-sweep/arg-shapes.md` (one section per tool, citing source
line) which is the audit trail behind matrix entries.

Where source is ambiguous (e.g., a `parameters?["x"]?.ToString()` followed by
branching on both string and array shapes), pick the **most-likely-success
shape** and note the ambiguity. Any tool whose actual rejection reveals a
different shape becomes a `SCHEMA` finding — itself useful evidence that the
absent input-schema is biting.

## Classification logic

```
envelope.error                       → FAIL, severity = TRANSPORT
envelope.result.isError == true      → FAIL, severity = CRASH

payload = parse(envelope.result.content[0].text)

payload.error != null     and expected == "ok"     → FAIL, severity inferred
                          and expected == "error"  → PASS
                          and expected == "either" → PASS
payload.success == false  — same as payload.error

otherwise:
  expected == "ok"    or "either"  → PASS
  expected == "error"              → FAIL (we wanted a rejection)
```

### Severity inference (heuristic, on error-text regex)

| Pattern | Severity |
|---|---|
| `KeyNotFound | NullReference | InvalidOperation` | CRASH |
| `is required | must be | JsonArray | Invalid request format` | SCHEMA |
| `not found.*directory | 11\.5\.0 | 11\.10\.0 | hardcoded | version mismatch` | STALE |
| `not implemented yet` | BUG |
| Tools/list mismatch (matrix vs server) | MISSING |
| Default | BUG |

Severity is heuristic — it speeds your triage. The raw response is always in
`findings.json` as ground truth.

### SIDE-EFFECT detection (limited)

For a small subset of risky mutators (`create_entity`, `rename_entity`,
`delete_model_element`, `create_microflow`, plus ~2 others), the matrix entry
declares a `verify:` follow-up — a `query_model_elements` or `list_modules`
call whose response is compared against an expected snippet. The verifier call
is **recorded as a sub-field of the mutation entry's `findings.json` record**
(under `side_effect_check: { args, raw_response, status }`), **not** as a
separate entry. So `findings.json` keeps exactly one record per tool in the
matrix. If the mutation returned PASS but the verifier shows the model
doesn't reflect it, the entry's top-level severity becomes `SIDE-EFFECT`.
Capped at ~6 verifier pairs.

## Driver error handling

| Failure | Behavior |
|---|---|
| HTTP non-200 | Record FAIL, severity TRANSPORT, keep going. |
| Body isn't valid JSON | Record FAIL, severity TRANSPORT, snippet stored. |
| Connection timeout (default 30s per call) | Record FAIL, severity TIMEOUT, keep going. |
| Driver script throws | Flush findings to disk, surface error, exit nonzero. |
| Ctrl-C mid-sweep | PowerShell trap → flush incremental findings → exit. |

After **every** entry, the driver atomic-writes `findings.json` (temp + rename)
and re-renders `findings.md`. Worst-case data loss is one entry.

## Fix workflow (Phase 3)

1. Edit C# source per the proposed-fix sketch in findings.md.
2. `dotnet build` — MSBuild's `DeployToMendix` target auto-deploys to
   `ConcordPublisher` and `TestOSApp3` per CLAUDE.md. **Open unknown:** does
   it also deploy to `C:\Projects\Test_10_24_13`? Verify at the start of
   Phase 3 (one-line `Get-ChildItem` against the destination's
   `userlib/Concord.dll` timestamp before/after build). If not, add the path
   to the deploy target.
3. Studio Pro must reload the MEF DLL. Realistically this requires a full
   Studio Pro restart between fix iterations (~30s overhead per cycle). Not
   chasing MEF hot-reload.
4. `./scripts/concord-mcp-sweep.ps1 -Only <fixed-tool>`.

## Phase 5 — in-Studio-Pro verification

Only enters after Phase 4 returns all-green for everything targeted. Captures
the things the JSON-RPC sweep can't see. The checklist will be appended to
`findings.md` for filling in:

1. **UI redraw:** does the domain-model designer reflect MCP-driven entity
   changes immediately, or only after `refresh_project`?
2. **Undo stack:** does Ctrl-Z roll back MCP-driven mutations cleanly?
3. **Focus / modal interference:** does `run_app` steal focus from the
   terminal pane? Does `stop_app` leave a stale "running" pill?
4. **Settings modal:** does a `set_runtime_settings` change reflect when the
   Settings modal is reopened?
5. **Concurrent edits:** start an entity rename in the UI, fire
   `rename_attribute` via MCP mid-keystroke. What happens?

Each item has PASS / FAIL boxes plus a notes field. No code in this phase.

## Success criteria

- `findings.json` exists with 87 entries, status assigned to each.
- Every FAIL row in `findings.md` has severity + suspected root cause + one
  of `{fix-implemented, deferred-with-issue, won't-fix-rationale}`.
- All **CRASH** and **STALE** severities are fixed.
- **BUG** and **SCHEMA** severities are fixed where the fix is < ~2 hours
  each; otherwise deferred with rationale captured inline.
- Re-test (`-Only <fixed-tools>`) returns all PASS for everything fixed.
- Phase-5 checklist is filled in.
- Auto-memory `project_concord_mcp_tool_sweep.md` written with: bug patterns
  observed, matrix-file location for future re-runs, anything surprising.

## Out of scope (explicit YAGNI)

- **Not** adding real input schemas to `tools/list`. That's its own design
  (catalog source-of-truth vs. `[ToolSchema]` attribute pattern vs.
  hand-written JSON-Schema per tool — each has trade-offs). Captured as a
  follow-up in findings.md.
- **Not** wiring the matrix into `dotnet test` / CI. The matrix is a one-off
  harness; if it proves valuable, promoting it to a real integration suite
  is a separate project.
- **Not** building a `clear_logs` tool. Feature request, not a bug fix —
  follow-up.
- **Not** redesigning the dispatch path in
  [`StudioProActionServer.cs`](../../../src/Concord.Core/Mcp/StudioProActionServer.cs).
  The catalog-based dispatch works; we're fixing tools that misbehave, not
  refactoring how they're called.
- **Not** testing across Studio Pro 11.x. This sweep targets the 10.24.13
  host. If 11.10 has different bugs, that's a separate sweep with a separate
  matrix (the driver is host-version-agnostic; only the matrix changes).

## Deliverables

- [ ] `tests/concord-mcp-sweep/matrix.jsonc` (~87 entries)
- [ ] `tests/concord-mcp-sweep/arg-shapes.md` (schema-discovery audit trail)
- [ ] `scripts/concord-mcp-sweep.ps1` (driver)
- [ ] `tests/concord-mcp-sweep/findings.json` + `findings.md`
- [ ] C# source fixes for all CRASH/STALE severity; BUG/SCHEMA where cheap
- [ ] Phase-5 manual checklist (in findings.md, filled in)
- [ ] `project_concord_mcp_tool_sweep.md` auto-memory

## Risks and known unknowns

- **`C:\Projects\Test_10_24_13` deploy:** the existing `DeployToMendix`
  target may or may not push to this path. Verified at start of Phase 3.
- **MEF DLL reload:** Studio Pro restart per fix cycle. Acceptable cost.
- **Reverse-engineered args:** some SCHEMA findings will be "matrix had the
  wrong arg shape" rather than "tool is buggy." Classified as a finding
  either way; the resolution annotation distinguishes them.
- **Generated traffic in log:** the sweep itself creates ~87 entries in
  Studio Pro's log and ~174 in Concord's terminal.log. The byte-offset
  baselines already captured (Studio Pro: 34267, terminal.log: 163980) make
  post-sweep filtering straightforward.
