---
title: Concord MCP tool-sweep — Phase 2/3 triage & fixes design
date: 2026-05-13
status: approved
owner: rperdiga
parent_spec: 2026-05-13-concord-mcp-tool-sweep-design.md
related_paths:
  - tests/concord-mcp-sweep/matrix.jsonc
  - tests/concord-mcp-sweep/findings.md
  - scripts/concord-mcp-sweep.ps1
  - src/Concord.Core/Spmcp/Utils/Utils.cs
  - src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs
  - src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs
---

# Concord MCP tool-sweep — Phase 2/3 triage & fixes design

## Context

Phase 1 (parent spec) shipped a sweep harness that exercises 88 MCP tools and
recorded 55 PASS / 33 FAIL against `Test_10_24_13` on Studio Pro 10.24.13.
This document specifies Phase 2 (triage of those 33 FAILs) and Phase 3 (the
fixes that follow), executed on branch `feat/v5.0.0-w2-mcpx-merge`.

## Phase 1 failure taxonomy

The 33 FAILs decompose into six categories:

| Cat | Count | Description | Resolution class |
|---|---|---|---|
| A | 8–10 | Documented `escalation: manual` stubs (Security 5 + Constants 3 + ProjectSettings 1–2) | Matrix reclassify |
| B | 4 | ModuleProxy `KeyNotFoundException` on Studio Pro 10.x — shared root cause | C# refactor |
| C | 2 | Task-15 `NotImplementedException` stubs (`get_app_status`, `get_active_run_configuration`) | Matrix reclassify (deferred to Task 15 branch) |
| D | 8–9 | Matrix references entities (`Customer`, `ACT_Example`, `MyNanoflow`, etc.) that don't exist in the test project | Matrix `setup` phase + a few `expected: "either"` |
| E | 5–7 | Real C# bugs (anon-type field missing, sequencing, etc.) | C# fixes |
| F | 2 | Lifecycle TRANSPORT timeouts on `run_app` / `stop_app` — downstream of C | Resolves with C (matrix reclassify) |

## Approach: matrix-first, then C# fixes

Two-phase ordering:

1. **Phase 2a — Matrix cleanup** (no rebuild). Strip false-positive noise so
   Phase 2b investigates real bugs against a quiet baseline.
2. **Phase 2b — C# source fixes**. Three atomic commits: shared-helper
   refactor, real-bug fix, and an investigation bucket.

Alternative considered: severity-ordered (CRASH first, BUG next, matrix last)
— rejected because real-bug investigation against a noisy baseline makes it
harder to distinguish "broken tool" from "test-design issue."

Alternative considered: one big pass — rejected because it produces a less
reviewable diff and an unbisectable commit if regressions surface.

## Phase decomposition

```
Phase 2a — Matrix cleanup (1 commit, no rebuild)
  • Edit tests/concord-mcp-sweep/matrix.jsonc
  • Edit scripts/concord-mcp-sweep.ps1 (add "setup" to phase order)
  • Re-run sweep → new baseline (~75–80 PASS, ~5–8 FAIL)

Phase 2b — C# source fixes (3 atomic commits, ≥1 rebuild each)
  Commit 1: TryPerModule<T> helper + apply to read sites
  Commit 2: analyze_project_patterns commonDeleteBehavior bug
  Commit 3: Remaining real bugs (TBD pending source dive)

Phase 3 — Re-test
  • dotnet build → Studio Pro restart → sweep -Only <fixed-tools>
  • Loop until all green for everything targeted
  • Final full sweep for end-to-end verification

Phase 4 — Manual Studio Pro verification (inherits parent spec §"Phase 5")
```

## Phase 2a — Matrix cleanup

### 2a.1 Reclassify documented stubs to `expected: "either"`

Ten tools where source explicitly returns
`{ success: false, escalation: "manual", message: "..." }` as a documented
"this surface isn't on the typed Interop yet" signal:

| Tool | Source justification |
|---|---|
| `list_rules` | `MendixAdditionalTools.cs:2975-2989` — explicit deferral |
| `read_security_info` | `:3103-3120` |
| `read_entity_access_rules` | `:3138-3152` |
| `read_microflow_security` | `:3154-3168` |
| `audit_security` | `:3171-3186` |
| `create_constant` | Matrix note: "Always returns escalation: manual" |
| `update_constant` | Same |
| `configure_constant_values` | Same |
| `sync_filesystem` | Same |
| `set_configuration` | Matrix note: "may return escalation: manual" |

Each entry gets a `// stub: escalation:manual until typed Interop expanded`
annotation appended to its `notes` so the audit trail survives.

### 2a.2 Reclassify Task-15 dependents to `expected: "either"`

Four tools — two stubs plus two downstream effects:

| Tool | Reason |
|---|---|
| `get_app_status` | `Concord.Host10x\Interop\StudioProAppHost10x.cs:7` — `NotImplementedException("Pending Task 15...")` |
| `get_active_run_configuration` | `RunConfigurationsHost10x.cs:8` — same |
| `run_app` | Downstream: driver polls `get_app_status`, hits 30s timeout |
| `stop_app` | Same downstream |

Each gets `// pending Task 15 — see Concord.Host10x\Interop\*Host10x.cs` in notes.

These will be implemented in Task 15's owning branch (W4-scope), not on
`feat/v5.0.0-w2-mcpx-merge`.

### 2a.3 Add `setup` phase before `read`

Insert 5 fixture-creation entries before the `read` block. Each call uses an
already-PASSing MCP tool to create a target the read phase needs:

```jsonc
// ---- Setup phase: bootstrap fixtures for read-phase targets ----
{ "name": "create_entity",   "family": "DomainModel", "phase": "setup",
  "args": { "module_name": "MyFirstModule", "entity_name": "Customer" },
  "expected": "either",
  "notes": "Fixture for query_associations, read_attribute_details, copy_model_element. expected:either to stay idempotent across reruns." }
,{ "name": "add_attribute",  "family": "DomainModel", "phase": "setup",
  "args": { "module_name": "MyFirstModule", "entity_name": "Customer",
            "attribute_name": "Name", "attribute_type": "String" },
  "expected": "either",
  "notes": "Fixture for read_attribute_details(Customer.Name)." }
,{ "name": "create_entity",  "family": "DomainModel", "phase": "setup",
  "args": { "module_name": "MyFirstModule", "entity_name": "Order" },
  "expected": "either",
  "notes": "Fixture for generate_overview_pages, create_association." }
,{ "name": "create_microflow","family": "Microflows", "phase": "setup",
  "args": { "name": "ACT_Example", "module_name": "MyFirstModule" },
  "expected": "either",
  "notes": "Fixture for read_microflow_details, check_variable_name, rename_document." }
,{ "name": "create_microflow","family": "Microflows", "phase": "setup",
  "args": { "name": "CAL_Customer_FullName", "module_name": "MyFirstModule" },
  "expected": "either",
  "notes": "Fixture for set_calculated_attribute." }
```

All setup entries use `expected: "either"` for idempotency — `Test_10_24_13`
already carries Phase 1 mutation residue, and reruns must not fail just
because a fixture already exists.

Three reads remain `expected: "either"` without setup support (no `create_*`
MCP tool exists for these surfaces):

- `read_nanoflow_details` (`MyNanoflow`)
- `read_workflow_details` (`MyWorkflow`)
- `read_page_details` (`Customer_Overview`) — depends on `generate_overview_pages`
  succeeding, which is a mutate-phase tool

Each gets a notes annotation explaining why no fixture is created.

### 2a.4 Driver patch

`scripts/concord-mcp-sweep.ps1` iterates phases in a fixed order. Extend its
phase list to `setup, read, mutate, lifecycle`. ~3-line change.

### 2a.5 Fix `rename_enumeration_value` reserved-word arg

```diff
- "new_name": "New"
+ "new_name": "NewDraft"
```

`New` is a Mendix reserved word; the tool correctly rejects it. Pure
matrix bug.

### 2a.6 Phase 2a expected outcome

Full sweep rerun expected to produce ~75–80 PASS / ~5–8 FAIL. The FAIL set
should now contain only category E (real C# bugs).

Phase 2a lands as a single commit:
`fix(spmcp-sweep): Phase 2a matrix cleanup + setup phase`

## Phase 2b — C# source fixes

### Commit 1 — `TryPerModule<T>` helper + ModuleProxy CRASH fixes

**New helper** in `src/Concord.Core/Spmcp/Utils/Utils.cs` (already a modified
file on this branch; extending it keeps the diff coherent):

```csharp
// Wraps a per-module ExtensionsAPI call that may throw KeyNotFoundException
// for ModuleProxy on Studio Pro 10.x system/App-Store modules. Returns the
// call result on success; records the skip and returns default(T) on failure.
public static T? TryPerModule<T>(
    ModuleId moduleId,
    Func<T> call,
    List<object> skipped,
    string operation,
    ILogger logger)
{
    try
    {
        return call();
    }
    catch (Exception ex) when (ex is KeyNotFoundException
                            || ex.Message.Contains("ModuleProxy"))
    {
        logger.LogWarning(ex, "{Operation} failed for module {Module}", operation, moduleId.Name);
        skipped.Add(new {
            module = moduleId.Name,
            operation,
            error = ex.Message,
            note = "Module's index isn't queryable via this Studio Pro version's extension API (often happens for system / App-Store-imported modules on 10.x). Skipped."
        });
        return default;
    }
}
```

**Apply at 3 call sites:**

1. **`read_project_info`** — `MendixDomainModelTools.cs:759-775`. Wrap the
   per-module Select walk's `ListEntities` / `ListEnumerations` /
   `ListModuleDocuments` calls. Result: bad modules land in `skippedModules[]`,
   the rest of the project info returns normally.

2. **`list_java_actions`** — wrap each per-module `ListModuleDocuments(moduleId,
   "JavaAction")` call.

3. **`update_enumeration`** (single-module path) — helper returns `default(T)`
   on failure. Caller checks; on failure, return
   `{ success: false, error: "Module 'X' is not queryable on this Studio Pro version; ModuleProxy not registered. Try a different module or use Studio Pro UI." }`
   so the MCP client gets an actionable error instead of a crash.

**Tests** in `tests/Concord.Core.Tests/SpmcpUtilsTests.cs` (also a
modified-untracked file on this branch):

- Happy path: call returns value, `skipped` stays empty
- KeyNotFound: returns `default`, `skipped` gets one entry with correct fields
- Other exception: re-throws (don't swallow unrelated bugs)

Commit message: `fix(mcp-sweep): TryPerModule helper for 10.x ModuleProxy crashes`

### Commit 2 — `analyze_project_patterns` `commonDeleteBehavior`

**Root cause:** `(string)statistics.commonDeleteBehavior` is read at
`MendixAdditionalTools.cs:4856` and `:4894`, but the anon-type that becomes
`statistics` is assembled upstream without that property. Grep up for the
`var statistics = new { ... }` site, add `commonDeleteBehavior = ...` to the
projection. Data is already computed elsewhere; this is a wiring fix.

Verification: `./scripts/concord-mcp-sweep.ps1 -Only analyze_project_patterns`.

Commit message: `fix(mcp-sweep): wire commonDeleteBehavior into analyze_project_patterns`

### Commit 3 — Remaining real bugs

Three tools requiring source dive before classification:

- **`check_project_errors`** — `success:false` with no error message
- **`set_runtime_settings`** — `success:false`, no message. Matrix author
  believed `com.mendix.core.SessionTimeout` worked; may be a recent regression
  or invalid key on 10.x.
- **`create_microflow_activity` / `modify_microflow_activity` / `insert_before_activity`** —
  `create_microflow` lands an empty microflow; activity inserts then fail
  with `position: 1` invalid. Either tool bug (should append) or test-design
  bug (use `position: 0` or omit for append).

**Strategy:** Investigate each after Commits 1+2 land. Per-bug commits if
genuine bugs. Per-bug matrix patches if test-design issues (rolling into a
Phase 2a follow-up commit, not Commit 3).

**Time-box:** ~2 hours per bug. If exceeded, defer with rationale captured
inline in `findings.md`, matching parent spec success criteria.

## Phase 3 — Re-test workflow

```
Phase 2a complete
  └─ commit matrix.jsonc + driver patch + updated findings.md

Phase 2b commit 1 (TryPerModule helper)
  ├─ dotnet build (auto-deploy per CLAUDE.md DeployToMendix)
  ├─ verify deploy to Test_10_24_13:
  │   Get-ChildItem C:\Projects\Test_10_24_13\userlib\Concord*.dll | Select Name, LastWriteTime
  │   ↳ if stale, add path to DeployToMendix target before rebuild
  ├─ Studio Pro restart (~30s, MEF DLL reload)
  └─ ./scripts/concord-mcp-sweep.ps1 -Only read_project_info,list_java_actions,update_enumeration

Phase 2b commit 2 (commonDeleteBehavior)
  ├─ dotnet build → Studio Pro restart
  └─ ./scripts/concord-mcp-sweep.ps1 -Only analyze_project_patterns

Phase 2b commit 3 (per-bug as needed)
  └─ same pattern — dotnet build → Studio Pro restart → -Only

Phase 3 close-out
  └─ ./scripts/concord-mcp-sweep.ps1   (full sweep, no -Only filter)
```

### Studio Pro restart discipline

One restart per commit. Don't batch unrelated fixes to save restarts —
atomic commits and bisectability are worth more than ~30s. Per parent
spec: MEF DLL reload requires full Studio Pro restart.

### Deploy-path verification

Parent spec flagged the open question of whether `DeployToMendix`
auto-deploys to `Test_10_24_13`. Verified at the start of Phase 2b
commit 1 via timestamp check. If missing, ~2-line MSBuild edit to add
the path.

## Success criteria

Inherits parent spec criteria, plus:

- [ ] `matrix.jsonc` has 4-phase ordering: `setup → read → mutate → lifecycle`
- [ ] All `escalation: manual` stubs (10) carry `expected: "either"` + audit notes
- [ ] All Task-15-dependent tools (4) carry `expected: "either"` + audit notes
- [ ] `TryPerModule<T>` helper exists in `Utils.cs` with ≥3 unit tests
- [ ] All ModuleProxy CRASH severities (3 read sites + 1 mutate site)
      resolved to PASS or structured error
- [ ] `analyze_project_patterns` PASS after `commonDeleteBehavior` wiring
- [ ] `check_project_errors`, `set_runtime_settings`, microflow-activity family:
      fixed-and-PASS or deferred with rationale in `findings.md`
- [ ] Final full-sweep `findings.md` shows ≥80/88 PASS, every remaining FAIL
      has resolution annotation
- [ ] Phase 4 manual Studio Pro checklist filled in (carried from parent)

## Risks and known unknowns

| Risk | Mitigation |
|---|---|
| Commit 3 investigation balloons | 2h time-box per bug; defer with rationale |
| Phase 1 mutation residue (Customer already exists?) collides with setup phase | All setup entries `expected: "either"` for idempotency |
| `DeployToMendix` doesn't reach `Test_10_24_13` | Verified pre-Phase 2b commit 1; ~2-line MSBuild fix if needed |
| MEF DLL hot-reload finally works | Don't optimize for it; restart per cycle |
| New ModuleProxy crash site found post-Phase 2b | Helper exists; apply inline as follow-up commit |
| `set_runtime_settings` key invalid on 10.x rather than tool bug | Discovered during commit 3 source dive; fold into matrix fix if so |

## Out of scope (carryover + reaffirmed YAGNI)

- Real `tools/list` input schemas — own design, deferred
- `clear_logs` tool — feature request, not bug fix
- Studio Pro 11.x re-sweep — separate matrix
- `get_app_status` / `get_active_run_configuration` implementation —
  deferred to Task 15 / W4 branch
- `StudioProActionServer.cs` dispatch path refactor

## Deliverables

- [ ] Updated `tests/concord-mcp-sweep/matrix.jsonc` (4 phases, 14 reclassified
      entries, 5 new setup entries, 1 reserved-word fix)
- [ ] Updated `scripts/concord-mcp-sweep.ps1` (phase-order patch)
- [ ] New `TryPerModule<T>` helper in `src/Concord.Core/Spmcp/Utils/Utils.cs`
- [ ] ≥3 new unit tests in `tests/Concord.Core.Tests/SpmcpUtilsTests.cs`
- [ ] C# source patches in `MendixDomainModelTools.cs` and `MendixAdditionalTools.cs`
- [ ] Updated `tests/concord-mcp-sweep/findings.md` showing ≥80/88 PASS
- [ ] Phase 4 manual checklist filled in (in `findings.md`)
- [ ] Auto-memory `project_concord_mcp_tool_sweep.md` updated with Phase 2/3 outcomes
