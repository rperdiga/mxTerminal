# Handoff — close the Studio Pro 11.10 validation gap (2026-05-14)

> **For the next session:** Phase 4 validation on `c:\Extensions\Terminal` proved 87/88 Concord MCP tools work on **Studio Pro 10.24.13** via fresh-state sweep (commit `c3eb9e2`, merged to main as `699eea5`). **11.10 was not directly swept** — `StudioProAppHost11x` / `RunConfigurationsHost11x` from `4ef888c` (bug_001) only have unit-test coverage via fakes. This handoff is the procedure to close that gap.

---

## Quick orientation

- **Branch:** `main` (post-merge state — `feat/v5.0.0-w2-mcpx-merge` is shipped + deleted)
- **HEAD at handoff time:** `699eea5` — `Merge feat/v5.0.0-w2-mcpx-merge into main`
- **Working tree:** clean
- **What's empirically validated:** 87/88 tools work on Studio Pro 10.24.13 fresh-state. The 1 exception is `run_app` (TIMEOUT — UI-automation stub, separate from `bug_001`).
- **What's NOT validated:** the same sweep against Studio Pro **11.10 (or later)**. The Host11x code paths exist and unit-test pass via fakes, but no end-to-end JSON-RPC sweep against a real 11.x Studio Pro has run since `bug_001` landed.

---

## The question to answer

**Do the 87/88 tools that pass on 10.24.13 also pass on Studio Pro 11.10?**

Sub-questions to surface in the report:

1. **Tool count drift.** Does `tools/list` on 11.x return the same 88 tools, or a different number? `src/Concord.Core/Mcp/Studio11xAllowlist.cs` exists — implies per-host gating may filter tools. Different count → matrix needs new entries or `[11x-only]` / `[10x-only]` notes.
2. **Same PASS/FAIL pattern?** Specifically, do `get_app_status` and `get_active_run_configuration` work on 11.x post-`bug_001`? They were Host11x stubs pre-`bug_001` (per the ultrareview finding text).
3. **Any tools that worked on 10.x but fail on 11.x?** (regression-class)
4. **Any tools that fail on 10.x but work on 11.x?** (capability-class — maybe `run_app` works on 11.x because the UI-automation surface is more mature there)

---

## Pre-conditions (Joe sets up before the session starts)

1. **Studio Pro 11.10 or later** installed and runnable on this machine.
2. **A fresh blank-app Mendix 11.10 project** — name your choice (`Test_11_10_FreshSweep` is fine).
3. **`MyFirstModule` present and empty** (default for a blank-app template).
4. **Concord pane loaded** in that Studio Pro instance — the extension should auto-deploy via the existing `MendixDeployTarget11x` MSBuild target (`C:\Projects\Test_11_10\extensions\Concord11x\`).
5. **MCP server reachable** at `http://127.0.0.1:7783/mcp` — verify with `Invoke-WebRequest -Uri http://127.0.0.1:7783/mcp -Method POST -ContentType 'application/json' -Body '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'` returning `serverInfo.name = "concord-mcp"`.
6. **Studio Pro 10.24.13 NOT running** simultaneously — only one Concord MCP server can bind port 7783 at a time.

---

## Procedure

### Step 1 — Pre-flight (drift detection is the critical signal)

```powershell
$ep = 'http://127.0.0.1:7783/mcp'

# initialize
$body = @{ jsonrpc='2.0'; id=1; method='initialize'; params=@{} } | ConvertTo-Json -Compress
(Invoke-WebRequest -Uri $ep -Method POST -ContentType 'application/json' -Body $body -UseBasicParsing).Content | ConvertFrom-Json | Select -Expand result | Select -Expand serverInfo

# tools/list — count and capture names for drift check
$body = @{ jsonrpc='2.0'; id=2; method='tools/list'; params=@{} } | ConvertTo-Json -Compress
$tools = (Invoke-WebRequest -Uri $ep -Method POST -ContentType 'application/json' -Body $body -UseBasicParsing).Content | ConvertFrom-Json
"11.x tools/list count: $($tools.result.tools.Count)"   # baseline is 88 on 10.24.13

# list_modules + verify MyFirstModule empty (contamination check)
$body = @{ jsonrpc='2.0'; id=3; method='tools/call'; params=@{ name='list_modules'; arguments=@{} } } | ConvertTo-Json -Compress -Depth 10
$payload = (Invoke-WebRequest -Uri $ep -Method POST -ContentType 'application/json' -Body $body -UseBasicParsing).Content | ConvertFrom-Json
$payload.result.content[0].text | ConvertFrom-Json | Select -Expand modules
```

**Stop and surface to Joe before any sweep if:**
- `serverInfo.version` ≠ what 10.24.13 reported (`concord-mcp v1.3.0` last time)
- `tools/list count` ≠ 88 (drift — needs matrix decisions)
- Any module name matches `ConcordSweep|Sweep|Phase4|RedrawProbe|UndoProbe|CAL_` (project not fresh)
- `MyFirstModule` has any entities

### Step 2 — Back up the 10.x baseline locally

```bash
cp tests/concord-mcp-sweep/findings.json tests/concord-mcp-sweep/findings.10x-baseline.json
cp tests/concord-mcp-sweep/findings.md   tests/concord-mcp-sweep/findings.10x-baseline.md
```

(`findings.before-*.{json,md}` and `*.log` are already gitignored in this directory — but use the explicit name above for clarity. The pattern `findings.before-*` will also match if you'd rather use that convention.)

### Step 3 — Run the sweep (background, ~3-5 min)

```powershell
& "c:\Extensions\Terminal\scripts\concord-mcp-sweep.ps1" `
    -Matrix "c:\Extensions\Terminal\tests\concord-mcp-sweep\matrix.jsonc" `
    -OutDir "c:\Extensions\Terminal\tests\concord-mcp-sweep" 2>&1 `
    | Tee-Object -FilePath "c:\Extensions\Terminal\tests\concord-mcp-sweep\11x-run.log"
```

Driver patch from `4a9fe1e` is already in place — no in-loop MD writes; no file-watcher lock risk.

### Step 4 — Diff 11.x vs 10.x baseline

The pairwise diff pattern used in Phase 4 v2 (commit `c3eb9e2`) — adapt the column names:

```powershell
$baseline_10x = Get-Content tests/concord-mcp-sweep/findings.10x-baseline.json -Raw | ConvertFrom-Json
$current_11x  = Get-Content tests/concord-mcp-sweep/findings.json -Raw | ConvertFrom-Json
function Get-RawStatus($e) { if ($e.status -eq 'PASS' -and -not $e.severity -and -not $e.error_summary) { 'PASS' } else { 'FAIL' } }
# (same pairwise loop as Phase 4 v2 — see git log -p tests/concord-mcp-sweep/phase4-fresh-state-validation.md for the exact incantation)
```

Build the bucket table: `10x_raw → 11x_raw` in {PASS, FAIL} → 4 buckets.

### Step 5 — Decision matrix for the outcomes

| Bucket | Meaning | Action |
|---|---|---|
| PASS → PASS | Tool works on both hosts | No action |
| FAIL → PASS | Tool works on 11.x but not 10.x | Update matrix note for the 10x-side `either` to clarify this is 10x-specific |
| PASS → FAIL | Regression on 11.x | **Real bug.** File as a finding, do NOT tighten matrix, surface to Joe immediately |
| FAIL → FAIL | Tool fails on both | Confirm against the `[pending Task 15]` / stub markers; no surprise if expected |

Plus drift cases:
| Drift case | Meaning | Action |
|---|---|---|
| Server tool MISSING from matrix | 11.x exposes a tool the matrix doesn't cover | Add matrix entry with `expected: "ok"` or `"either"` per first-call result; document in `phase4-fresh-state-validation.md` |
| Matrix tool EXTRA (server doesn't expose) | 11.x allowlist filters this tool out | Add `[11x-only-filtered]` note to matrix entry; do NOT remove the entry (10x still uses it) |

### Step 6 — Write the result

Append a new section to `tests/concord-mcp-sweep/phase4-fresh-state-validation.md`:

```markdown
---

## 11.x cross-host validation (YYYY-MM-DD)

Studio Pro version: <e.g. 11.10.x>
Tool count: <N> (10.24.13 baseline: 88)

| Bucket (10x_raw → 11x_raw) | Count | Interpretation |
|---|---|---|
| PASS → PASS | <N> | |
| FAIL → PASS | <N> | |
| PASS → FAIL | <N> | (regressions, if any) |
| FAIL → FAIL | <N> | (stubs / Task-15) |

Drift: <N> tools missing from matrix, <N> tools filtered by 11x allowlist.

<Specific entry notes here>
```

Plus update `findings.md`'s curated Phase 3 follow-up notes if `run_app` works on 11.x (it'd change item 3's wording).

### Step 7 — Commit + push

Stage only the sweep-harness changes (not any unrelated WIP):

```bash
git add tests/concord-mcp-sweep/findings.json \
        tests/concord-mcp-sweep/findings.md \
        tests/concord-mcp-sweep/matrix.jsonc \
        tests/concord-mcp-sweep/phase4-fresh-state-validation.md
git commit -m "test(spmcp-sweep): 11.x cross-host validation — <one-line outcome summary>"
```

Then push. **Auto-mode classifier will block direct push to main.** Joe must paste:

```cmd
git push origin main
```

If the validation surfaced a regression (PASS → FAIL bucket > 0), do NOT push; surface to Joe so the regression is fixed in a follow-up branch before main moves.

---

## What success looks like

Most likely outcome (educated guess, not evidence):

- **Same 87/88 PASS** on 11.x — bug_001 was the gating fix; Host11x had the same stub issue as Host10x pre-fix.
- **Possibly different tool count** if the 11x allowlist filters anything. Matrix may need 2-4 entries flagged `[11x-only-filtered]`.
- **`run_app` might work on 11.x** if the F5-hotkey UI-automation path is implemented on Host11x but not Host10x. Worth specifically checking.

Surprises to actively look for:

- New crash modes in tools that PASS on 10.x. The 11.x API surface (especially `IModel.Root`, `ILocalRunConfigurationsService`) may behave differently than 10.x at edges the unit-test fakes don't exercise.
- Different `tools/list` counts indicating the allowlist is more aggressive than expected.
- `stop_app` getting actually exercised if `run_app` works — would flip our "stop_app is short-circuiting on already-stopped" caveat.

---

## Things NOT to do

- **Don't run with 10.24.13 Studio Pro still alive.** Port 7783 collision; the sweep would hit whichever instance happens to bind first.
- **Don't push if PASS → FAIL bucket > 0.** A regression on 11.x means main shouldn't advance until the regression is fixed.
- **Don't auto-tighten matrix entries to `ok` if 11.x surfaces new behaviors.** Verify with a second run before changing classification — accumulated-state from the first run will mask idempotency on the re-test.
- **Don't delete `findings.10x-baseline.{json,md}` until the 11.x cycle is committed.** They're the only artifact that proves the 10.x state pre-existed; once findings.json is overwritten by the 11.x run, the 10.x baseline lives only in the gitignored backup.

---

## TL;DR for the next session

1. Joe brings up Studio Pro 11.10+ with a fresh blank-app project; confirms ready.
2. Pre-flight: serverInfo + tools/list count + module list. Surface any drift.
3. Back up `findings.json` → `findings.10x-baseline.json` (gitignored).
4. Run `scripts/concord-mcp-sweep.ps1` in background.
5. Pairwise diff baseline vs new findings.json → 4-bucket table.
6. Append the result to `tests/concord-mcp-sweep/phase4-fresh-state-validation.md`.
7. Commit; Joe pastes the push.

Total budget: ~30 min if no surprises, longer if regressions surface.
