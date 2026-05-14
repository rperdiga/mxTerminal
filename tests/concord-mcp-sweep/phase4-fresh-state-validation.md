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
