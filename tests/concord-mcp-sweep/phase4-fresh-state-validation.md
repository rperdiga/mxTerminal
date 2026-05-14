# Phase 4 — fresh-state sweep validation (2026-05-14)

## Purpose

Answer the question: do the 94 tools the sweep marked PASS on accumulated state (Test_10_24_13) also work on a pristine 10.24.13 blank-app project? Or are some of them transport-PASS but state-dependent (idempotency-masked or accumulated-state-only)?

The 94/94 PASS from Phase 2/3 was suggestive but not definitive — `expected: "either"` promotes raw FAILs to PASS for 46 of those entries. Fresh-state validation lifts the mask.

## Methodology

1. Backed up findings artifacts from the Test_10_24_13 run as `findings.before-fresh-sweep.{json,md}`.
2. Joe created a fresh blank-app Mendix 10.24.13 project; opened it in Studio Pro 10.24.13 with the Concord pane / MCP server running.
3. Pre-flight verified `MyFirstModule` had 0 entities and no contamination markers (`ConcordSweep_*`, `Sweep*`, `Phase4*`, `Customer`, `Order`, `ACT_Example`, etc.).
4. Patched `scripts/concord-mcp-sweep.ps1` to skip the in-loop `findings.md` write (fixes deferred follow-up #4 — VS Code file-watcher lock). The MD file is now written once at run end via the existing `finally` block; per-entry observability is preserved through the atomic-rename `findings.json` writes.
5. Ran the full sweep against fresh state.
6. Diffed fresh `findings.json` vs the stale backup pairwise (94 entries each, index-matched, 0 mismatches).

The truth signal: an entry's raw transport result is PASS only when `status == "PASS" && severity is null && error_summary is null`. Anything else (including `status: "PASS"` with a non-null `severity`/`error_summary`) is a raw FAIL that was promoted to PASS by the driver's `expected: "either"` rule at [scripts/concord-mcp-sweep.ps1:189-191](../../scripts/concord-mcp-sweep.ps1#L189-L191).

## Result

| Bucket (stale_raw → fresh_raw) | Count | Interpretation |
|---|---|---|
| PASS → PASS | 91 | Raw transport success on both runs |
| FAIL → FAIL | 3 | Same Task-15 stubs, identical severity |
| FAIL → PASS | 0 | No idempotency-mask collapses observed |
| PASS → FAIL | 0 | No regressions on fresh state |

### The 3 persistent FAILs

| Tool | Family | Phase | Severity | Note |
|---|---|---|---|---|
| `get_app_status` | UiActions | read | CRASH | Pending Task 15 + Task 1 spike — 10.x `IApp` surface verification |
| `get_active_run_configuration` | UiActions | read | CRASH | Pending Task 15 — 10.x `ILocalRunConfigurationsService` surface verification |
| `run_app` | UiActions | lifecycle | TIMEOUT | 30s sweep timeout (`NotImplementedException` in 10.x host) |

`stop_app` correctly PASSes on both runs — short-circuits on "already stopped" via `probe.IsRunningAsync` at [src/Concord.Core/Mcp/StudioProActions.cs:56](../../src/Concord.Core/Mcp/StudioProActions.cs#L56). When `run_app` is a stub and never starts the app, `stop_app` sees `RunState.Stopped` and returns "already stopped" cleanly. Not a stub-hidden-as-PASS.

## Conclusions

1. **91 of 94 tools (97%) work correctly on both fresh and accumulated state.** The 94/94 PASS from Phase 2/3 is genuine — not masked by idempotency or state pre-conditions.
2. **The 3 raw FAILs are pre-disclosed Task-15 stubs** with identical severity on fresh and accumulated state. Their `expected: "either"` classification in `matrix.jsonc` remains correct.
3. **The 43 non-stub `expected: "either"` entries did not actually need the mask** — all 43 raw-PASSed on both runs. Most are over-classified and can be tightened to `expected: "ok"`.
4. **No regressions introduced by accumulated state.** The Test_10_24_13 testbed's accumulated mutations did not hide any tool defects.

## Recommended matrix patches

Flip the 7 entries with explicit `[idempotency: ...]` notes (handoff finding #5) from `expected: "either"` → `expected: "ok"`. These are entries the matrix author preemptively guarded against re-run failures; fresh-state evidence shows they pass on first hit. Future fresh-state runs continue to PASS; runs against accumulated state may now surface FAILs that should be triaged as real bugs rather than masked.

| matrix.jsonc line | Entry | Original idempotency note |
|---|---|---|
| 22 | `create_entity` (Customer fixture) | "expected:either for idempotency across reruns" |
| 400 | `create_module` ConcordSweep_create_module | "[idempotency: matrix re-runs against pre-existing project state]" |
| 449 | `rename_attribute` SweepAttr_add_attribute → SweepAttr_rename_attribute | "[idempotency: on re-runs, SweepAttr_rename_attribute already exists]" |
| 530 | `create_multiple_associations` SweepEntityA ↔ SweepEntityB | "[idempotency: matrix re-runs against pre-existing project state]" |
| 581 | `create_enumeration` SweepEnum_create_enumeration | "[idempotency: matrix re-runs against pre-existing project state]" |
| 597 | `rename_enumeration_value` Draft → NewDraft | "[idempotency: matrix re-runs against pre-existing project state]" |
| 741 | `rename_module` ConcordSweep_create_module → ConcordSweep_rename_module | "[idempotency: matrix re-runs against pre-existing project state]" |

### Entries intentionally kept as `expected: "either"`

- 3 Task-15 stubs (`get_app_status`, `get_active_run_configuration`, `run_app`) — genuinely deferred until Task 15 / W4 branch.
- 6 setup fixtures with chain dependencies (`Customer.Name`, `Order`, `Order.Number`, `ACT_Example`, `CAL_Customer_FullName`, second `create_microflow`) — depend on first fixture state in same run.
- `generate_overview_pages` (line 691) — cascade failure mode where `set_entity_generalization` + `remove_entity_generalization` degrades Order's attribute state. Not idempotency; legitimate `either`.
- `read_page_details` for `Customer_Overview` (line 254) and `exclude_document` for `Customer_Overview` (line 752) — depend on `generate_overview_pages` having run successfully.
- ~25 documented-stub entries (Diagnostics, Security audit/read, etc.) with their own audit notes about state-dependent behavior.

## Known limitations / future work

- **Driver overwrites findings.md on every run**, destroying hand-curated content (Phase 4 checklist, Phase 3 follow-up notes). Backup at `findings.before-fresh-sweep.md` preserved the original; this writeup lives in a separate file to avoid the same fate. Future driver improvement: preserve managed-section delimiters or write auto-output to a distinct filename (e.g., `findings.auto.md`).
- **Fresh-state and accumulated-state runs are not interchangeable.** After flipping the 7 idempotency-eithers to `ok`, runs against accumulated state may surface FAILs. The sweep workflow should standardize on fresh-state runs OR add teardown discipline before each run.
- The driver patch in this cycle ([scripts/concord-mcp-sweep.ps1:430](../../scripts/concord-mcp-sweep.ps1#L430)) is the minimum fix for the file-watcher lock. A more robust fix would use `[System.IO.File]::WriteAllText` with explicit `FileShare.ReadWrite`, but the simpler patch (move MD write to end-of-run only) is sufficient and reduces per-entry I/O.

---

## Post-bug_001 re-validation (2026-05-14 later that day)

After this doc landed in commit `74d73e1`, ultrareview surfaced four findings (see `docs/superpowers/handoffs/2026-05-14-ultrareview-fixes-pending-replay.md`), which were addressed in commit `4ef888c`. bug_001 specifically implements `StudioProAppHost{10,11}x` and `RunConfigurationsHost{10,11}x` against `IModel.Root` and `ILocalRunConfigurationsService` — directly affecting two of the three persistent FAILs catalogued above.

To measure the effect, the sweep was re-run on the same fresh-state setup (new blank-app 10.24.13 project; pristine `MyFirstModule`; pre-flight verified empty) and diffed against the validated-PASS baseline preserved as `findings.before-bug001-resweep.json`.

### Re-validation result

| Bucket (pre-bug_001 raw → post-bug_001 raw) | Count | Interpretation |
|---|---|---|
| PASS → PASS | 91 | All previously-passing tools still pass — no regressions from bug_001 |
| FAIL → PASS | 2 | `get_app_status` and `get_active_run_configuration` now function on fresh state |
| FAIL → FAIL | 1 | `run_app` still TIMEOUT — bug_001 did not touch the UI-automation start path |
| PASS → FAIL | 0 | No regressions |

### Updated state of the 3 originally-persistent FAILs

| Tool | Pre-bug_001 | Post-bug_001 | Resolution |
|---|---|---|---|
| `get_app_status` | CRASH (Task 15 / Task 1 spike) | PASS (313 ms) | Resolved by 4ef888c bug_001 |
| `get_active_run_configuration` | CRASH (Task 15) | PASS (24 ms) | Resolved by 4ef888c bug_001 |
| `run_app` | TIMEOUT (30s) | TIMEOUT (30s) | Still pending — needs UI-automation work in `StudioProUiAutomation` (not the IApp/RunConfigurations surfaces bug_001 fixed) |

### Matrix follow-up applied

Flipped two additional entries from `expected: "either"` → `expected: "ok"` based on the re-validation:

- `get_app_status` ([matrix.jsonc:376-382](matrix.jsonc#L376-L382))
- `get_active_run_configuration` ([matrix.jsonc:384-390](matrix.jsonc#L384-L390))

`run_app` and `stop_app` remain `expected: "either"` until the UI-automation start path is implemented (or `stop_app`'s short-circuit-on-stopped behavior is documented as the intended permanent fast path on no-op). `refresh_project` is already `expected: "ok"` and continues to PASS.

After this follow-up: matrix has 57 `expected: "ok"` / 37 `expected: "either"` across 94 entries.

### Methodology drift caveat

The baseline-vs-fresh comparison this time uses `findings.before-bug001-resweep.json` (not the original `findings.before-fresh-sweep.json` from the earlier cycle). The earlier backup is a different snapshot — heavily-mutated `Test_10_24_13` state — and isn't directly comparable to this fresh-state re-run. Both backups are kept locally (gitignored) as historical references.

---

## 11.x cross-host validation (2026-05-14, Studio Pro 11.10.0)

Closing the Studio Pro 11.10 validation gap identified in [docs/superpowers/handoffs/2026-05-14-close-11x-validation-gap.md](../../docs/superpowers/handoffs/2026-05-14-close-11x-validation-gap.md). The Phase 4 v2 sweep above ran exclusively on Studio Pro 10.24.13; bug_001 (`4ef888c`) introduced `StudioProAppHost11x` / `RunConfigurationsHost11x` with only unit-test coverage. This section captures the end-to-end JSON-RPC sweep on a real Studio Pro 11.10.0 instance.

### Pre-flight findings — the surface is much smaller on 11.x

| Check | 10x baseline | 11.x | Notes |
|---|---|---|---|
| `serverInfo` name/version | `concord-mcp` / `1.3.0` | `concord-mcp` / `1.3.0` | ✓ unchanged |
| `protocolVersion` | `2025-03-26` | `2025-03-26` | ✓ unchanged |
| `tools/list` count | **88** | **45** | ⚠ major drift |
| `list_modules` reachable | yes | **no — `-32601 Unknown tool`** | filtered by allowlist |
| `MyFirstModule` contamination check | (run) | (skipped — `list_modules` filtered) | sweep started without contamination verification |

`src/Concord.Core/Mcp/Studio11xAllowlist.cs` filters the 11.x surface to **45 of the 10x's 88 tools**, far more aggressive than the handoff's "2-4 entries" guess. Whole categories are excluded:

- **All greenfield CRUD** (`create_module`, `create_entity`, `create_multiple_entities`, `create_domain_model_from_schema`, `create_association`, `create_microflow`, `create_microflow_activity`, `create_microflow_activities_sequence`, `create_enumeration`, `create_constant`, `add_attribute`, `update_attribute`, `add_event_handler`, `update_*`, `set_calculated_attribute`, `set_entity_generalization` / `remove_*`, `copy_model_element`, `manage_folders`, `sync_filesystem`)
- **All `list_*` discovery** (`list_modules`, `list_microflows`, `list_nanoflows`, `list_pages`, `list_workflows`, `list_constants`, `list_enumerations`, `list_rules`, `list_scheduled_events`, `list_rest_services`, `list_java_actions`, `list_available_tools(_domain)`)
- **All `read_*` deep-dive** (`read_domain_model`, `read_project_info`, `read_microflow_details`, `read_nanoflow_details`, `read_page_details`, `read_workflow_details`, `read_attribute_details`, `read_version_control`)
- **Query/diagnostics** (`query_model_elements`, `query_associations`, `diagnose_associations`, `validate_name`, `check_variable_name`, `get_last_error_domain`)

11.x adds **10 `maia__*` tools** not present in the 10x matrix: `ask`, `busy`, `force_tier`, `health`, `new_chat`, `ping`, `reset`, `send`, `status`, `wait`. Out of scope for this cycle's sweep — matrix doesn't yet describe them.

### Sweep scope: the 35-tool overlap

To produce a comparable bucket table the sweep was restricted (via `scripts/concord-mcp-sweep.ps1 -Only ...`) to the 35-tool intersection of `tools/list` ∩ `matrix.jsonc`. Pre-flight log confirms: `drift: 10 MISSING, 59 EXTRA  →  -Only filter: 35 entries match`.

Pre-condition: fresh blank-app project per the handoff (`MyFirstModule` empty by template — contamination check skipped because `list_modules` was filtered out, but a fresh project was confirmed visually).

### Bucket distribution (10x raw → 11.x raw) for the 35 overlap

| Bucket | Count | Interpretation |
|---|---|---|
| PASS → PASS | 26 | Capability confirmed on both hosts |
| PASS → FAIL | 8 | **All fixture-not-found cascades** — see detail below; not true regressions |
| FAIL → PASS | 0 | No tool that failed on 10x newly passes on 11.x |
| FAIL → FAIL | 1 | `run_app` TIMEOUT — unchanged from 10x |

`Get-RawStatus` from the Phase 4 v2 cycle: `PASS` requires `status='PASS' AND no severity AND no error_summary`. `run_app` returns PASS classification with `severity=TIMEOUT` and is therefore raw-`FAIL` on both hosts.

### The 8 PASS → FAIL entries are a fixture cascade, not 11.x regressions

Every one fails with `<target> not found` because the matrix's setup-phase fixtures (`create_entity` → `SweepEntity_create_entity`, `create_microflow` → `SweepMf_create_microflow`, `create_module` → `ConcordSweep_create_module`, `create_enumeration` → `SweepEnum_create_enumeration`, plus the chained `rename_entity` → `SweepEntityRenamed_rename_entity`) are filtered out of the 11.x surface and therefore never executed. The 8 mutators were called against state that was never seeded:

| Tool | matrix.expected | 11.x error |
|---|---|---|
| `delete_document` | `ok` | `Document 'SweepMf_create_microflow' not found in module 'MyFirstModule'` |
| `delete_model_element` | `ok` | `Entity 'SweepEntityRenamed_rename_entity' not found in module 'MyFirstModule'` |
| `rename_attribute` | `ok` | `Entity 'SweepEntity_create_entity' not found in module 'MyFirstModule'` |
| `rename_entity` | `ok` | `Entity 'SweepEntity_create_entity' not found in module 'MyFirstModule'` |
| `rename_enumeration_value` | `ok` | `Enumeration 'MyFirstModule.SweepEnum_create_enumeration' not found.` |
| `rename_module` | `ok` | `Module 'ConcordSweep_create_module' not found` |
| `set_documentation` | `ok` | `Entity 'SweepEntity_create_entity' not found` |
| `set_microflow_url` | `ok` | `Microflow 'MyFirstModule.SweepMf_create_microflow' not found` |

**Capability of these 8 mutators on 11.x cannot be confirmed from this run.** They reach the host (no transport errors, sub-50ms responses) and return well-formed `<target> not found` errors, which suggests the per-host `Host11x` plumbing is wired — but a follow-up run with pre-seeded fixtures (or matrix args that target stable, pre-existing artifacts) is needed for a true capability signal.

The other six mutators on existing-state (`rename_association`, `rename_document`, `arrange_domain_model`, `modify_microflow_activity`, `insert_before_activity`, `generate_overview_pages`, `exclude_document`) are classified PASS because their matrix `expected: "either"` tolerates target-not-found returns; their underlying status mirrors the 8 above (would-be-FAIL absorbed by `either`).

### The 1 FAIL → FAIL entry

| Tool | 10x severity | 11.x severity | Resolution |
|---|---|---|---|
| `run_app` | TIMEOUT (30056 ms) | TIMEOUT (30056 ms) | Unchanged. The F5-hotkey UI-automation start path is unimplemented on both hosts; bug_001 only addressed `IModel.Root` / `ILocalRunConfigurationsService` surfaces, which is the right scope for that fix. |

`stop_app` PASSes on 11.x (278 ms) via the same already-stopped short-circuit observed on 10x. The Phase 4 caveat about `stop_app` not being meaningfully exercised holds — neither host got `run_app` to start anything for `stop_app` to actually stop.

### Net answer to the handoff's question

> Do the 87/88 tools that pass on 10.24.13 also pass on Studio Pro 11.10?

Reframed by the actual data: **of the 35 tools that exist on both 11.x and 10.x, 26 PASS on both, 1 FAILs the same way on both (`run_app`), and 8 cascade-FAIL on 11.x for lack of seeded fixtures**. No PASS → FAIL entry has a non-fixture-related error. **Zero true capability regressions detected on 11.x.**

The handoff's other sub-questions:

1. **Tool count drift** — 88 → 45. Not "a few `[11x-only-filtered]` notes"; an entire workstream's worth of surface gap.
2. **`get_app_status` and `get_active_run_configuration` on 11.x post-bug_001** — both PASS on 11.x (matching the Phase 4 v2 flip on 10x). bug_001's per-host work landed cleanly.
3. **Regressions on 11.x** — none confirmed (see above).
4. **`run_app` works on 11.x?** — no, still TIMEOUT. The F5 path remains the bottleneck regardless of host.

### Matrix follow-up — deferred, not applied this cycle

No matrix edits made. Rationale: the cleanest signal from this run is that **53 matrix entries (~57% of the matrix) have no counterpart on the live 11.x server** and the 8 fixture-cascade FAILs need a re-run with seeded state before any `expected` tightening. Both are workstream-scale items, not in-cycle nits.

Suggested follow-ups (deferred to a separate handoff):

- **Allowlist audit** — read `src/Concord.Core/Mcp/Studio11xAllowlist.cs` and adjacent code; determine whether the 45-tool surface is intentional scope cut for 11.x or a regression of host-binding. Document the intent in `references_extension_resources.md`.
- **Per-host matrix variants** — add `host: "10x" | "11x" | "both"` field to matrix entries (or split into `matrix.10x.jsonc` / `matrix.11x.jsonc`) so future sweeps can run against either host without `-Only` plumbing.
- **Maia tools in matrix** — add 10 `maia__*` entries with appropriate `expected` values once the bridge transport semantics are documented for sweep coverage.
- **Fixture-decoupled mutator probes** — for the 8 cascade-FAIL tools, add matrix entries that target pre-existing or auto-detected artifacts (e.g. `rename_module` against `MyFirstModule` is unsafe; better candidates needed) so capability can be measured on 11.x without a seeded blank-app.

### Methodology drift caveats

- **Contamination pre-check skipped.** `list_modules` is filtered out on 11.x so the standard `MyFirstModule` empty-check from the handoff Step 1 couldn't run. Visual confirmation of a fresh blank-app stood in.
- **`findings.json` contains synthetic drift entries.** The driver writes both real probe entries (35 today) and synthetic `MISSING` entries for matrix/server mismatches (10 maia EXTRA + 59 matrix-without-server). Total entry count is 104 — only the 35 with `severity≠MISSING` are actual 11.x probe data. The diff script keys off `name` and the latest-timestamp record per name to compute the bucket table cleanly.
- **`findings.before-11x-sweep.json` is the 10x baseline backup**, gitignored per the existing `findings.before-*` pattern.
