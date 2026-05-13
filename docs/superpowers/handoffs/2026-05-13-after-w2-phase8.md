# Handoff: after W2 Phase 8 (Tasks 21, 29, 33, 34 — v5.0.0-alpha.2 ready for tag pending smoke) — 2026-05-13

> **For the next session:** W2 Plan execution complete except the user-only steps (Studio Pro 11.10 + 10.24.13 runtime smoke; the `v5.0.0-alpha.2` tag). The branch `feat/v5.0.0-w2-mcpx-merge` is ready to tag, push, and PR-merge once Neo's smoke matrix passes. Phase 6 handoff and earlier remain authoritative for prior context.

---

## Quick orientation

- **Branch:** `feat/v5.0.0-w2-mcpx-merge` (pushed to origin)
- **HEAD (this writing):** `f1858c0` — `chore: bump to 5.0.0-alpha.2 (W2 SPMCP merge + Host10x UI port)`. After the handoff commit, HEAD will be `+1`.
- **Tests:** **272 passing** (242 Terminal.Tests + 27 Concord.Core.Tests + 3 skipped Maia-live), 0 failed. Up from 244 in 5.0.0-alpha.1 (Terminal.Tests count is unchanged after Task 21's migration to catalog-based dispatch; the +27 are net-new Concord.Core.Tests added across W2).
- **Build:** **CLEAN** — `dotnet build Terminal.sln` succeeds with 0 errors and 20 pre-existing warnings (CS0414 sentinel + CS8602/CS8604 nullable; all carry-over from W1/W2 and tracked under deferred Tasks 30-31).
- **Working tree:** clean (after this handoff commit).

**Phase 8 functional success criteria — met:**

1. CHANGELOG.md prepends a 5.0.0-alpha.2 entry covering the full W2 scope (Phases 2-6 + Task 21 + Task 29 + isError fix-up) — `f3c3f79`.
2. DEPLOYING.md adds two new "Migrating from …" sections (alpha.1 drop-in upgrade; MCPExtension / SPMCP standalone supersession) — `f3c3f79`.
3. README.md project-layout updated to mention `Spmcp/` subfolders under Core and both hosts; Host10x's full `Pane/Ui/Interop` expansion — `f3c3f79`.
4. Version bumped 5.0.0-alpha.1 → 5.0.0-alpha.2 in all 3 csproj files — `f1858c0`.
5. Clean rebuild + full test suite verified pre-tag — `0 errors, 272 passing`.
6. Studio Pro runtime smoke matrix Neo-required steps captured in `docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md` (W2 smoke results section). **Two unchecked boxes for Neo: 11.10 smoke + 10.24.13 smoke.**

---

## This session's commit chain (Phase 7 partial + Task 21 + Phase 8)

| Commit | Task | What |
|---|---|---|
| `454d7af` | 29 | `refactor(core): route StudioProActions through HostServices (W2)` — Removes `probe`, `ui`, `getActiveRunConfig`, `getProjectInfo` ctor params from `StudioProActions`. Adds nullable-after-set `RunStateProbe` + `UiAutomation` accessors + `SetRunStateProbe`/`SetUiAutomation` setter methods to `HostServices` (additive — does NOT modify existing 4-arg or 11-arg `Register` overloads, per the Phase 5 trap warning). Pane construction sites in both hosts (TerminalPaneExtension + TerminalPaneViewModel × Host10x + Host11x) now call setters at startup and on settings save; the legacy 11-arg VM ctor drops to 9 args. 6 new `HostContextTests` cover the accessors. Net change: 12 files, +160 / −167. |
| `d4c243d` | 21 | `feat(core): register UI-action and Maia tools through ToolCatalog (W2)` — New `UiActionsBootstrap` (6 tools) and `MaiaToolsBootstrap` (10 tools) in `src/Concord.Core/Mcp/`. Host*Entry wires both after the SPMCP bootstrap. `StudioProActionServer` simplifies to `(int port, Logger? log)`: hardcoded UI/Maia dispatch deleted (179 lines from one file), hardcoded `HandleToolsList` rich descriptions deleted, `actions`/`maia`/two flags deleted. `TerminalSessionManager.StartActionServer` signature drops the same 4 args. Catalog `SetFamilyEnabled(UiActions \| Maia, bool)` from settings drives visibility. New `MaiaActions?` accessor + `SetMaiaActions(MaiaActions?)` setter on HostServices (nullable; null returns `ActionResult.Fail("Maia integration not enabled")` from delegates). 6 new `CatalogBootstrapTests`. Test classes that race on `ToolCatalogRegistry.Active` get `[Collection("ActionServer")]`. Net change: 14 files, +494 / −271. |
| `9f4fc59` | 21 fix-up | `fix(core): preserve isError wire-format invariance after catalog dispatch (W2)` — Spec reviewer caught a regression: the new catalog dispatch path unconditionally set `isError: false` in the JSON-RPC envelope; pre-Task-21 hardcoded path set it from `ActionResult.Error != null`. MCP clients (and the failure-warning log line) lost the signal. Fix: in catalog success branch, detect `resultObj is ActionResult ar`, emit warn log on `ar.Error != null`, set `isError = ar.Error != null`. Non-ActionResult returns keep `isError: false`. New test (`ToolsCall_FailingTool_ReturnsIsErrorTrue`) asserts the invariant. +1 test, 272 total. |
| `f3c3f79` | 33 | `docs: W2 SPMCP-merge migration notes + changelog (W2 Task 33)` — CHANGELOG prepends a 5.0.0-alpha.2 entry. DEPLOYING adds two migration sections. README project-layout reflects SPMCP and Host10x's now-complete folder structure. 3 files, +60 / −4. |
| `f1858c0` | 34 | `chore: bump to 5.0.0-alpha.2 (W2 SPMCP merge + Host10x UI port)` — Three csproj Version bumps. Build verified 0 errors. 3 files, +3 / −3. |

Cumulative session diff vs `6d23b00` (start-of-session): ~30 files, ~720 insertions, ~440 deletions. Most of the deletion volume is from removing the 179-line hardcoded UI/Maia dispatch + tools/list descriptions.

---

## Critical design choices made (preserve them)

### 1. HostServices late-bound setter pattern (Task 29)

The plan template forward-referenced "extend HostServices.Register to include `runStateProbe` and `uiAutomation` args, construct them in Host*Entry." **This was wrong.** `RunStateProbe` ctor takes a `Func<string?>` that captures pane-scoped state (`CurrentApp`, `localRunConfigs.GetActiveConfiguration`); `StudioProUiAutomation` ctor takes hotkeys from user settings + a Logger. Neither exists at MEF activation time.

**Adaptation:** the pane keeps construction; `HostServices` gains `SetRunStateProbe(IRunStateProbe)` and `SetUiAutomation(IStudioProUiAutomation)` setter methods (separate from `Register`). Pane calls the setter when wiring. Settings-save reconstructs and re-sets, allowing hot-swap.

Pattern extended to `SetMaiaActions(MaiaActions?)` in Task 21 (nullable since Maia is opt-in). All three setters are now the standard pane→Core registration shape. The existing 4-arg and 11-arg `HostServices.Register` overloads are untouched; the brief's Phase 5 trap (don't switch Register signature without first refactoring the 7 extended hosts) stands.

### 2. ToolCatalog is the single dispatch path (Task 21)

`StudioProActionServer.HandleToolsCallAsync` has ONE code path now: catalog dispatch. Unknown name → `BuildErrorBody(-32601)`. The hardcoded UI/Maia switch (179 lines) is gone. `HandleToolsList` only enumerates the catalog. `tools/list` shows the generic `"Concord SPMCP tool (Family). Schema TBD."` description for all tools — rich per-tool descriptions are a Phase 9+ polish (add `Description` + `InputSchema` to `ITool`).

`SetFamilyEnabled(family, bool)` is the runtime visibility toggle. Settings-save calls it for `UiActions` and `Maia` families; the catalog enumerates only visible tools.

### 3. JSON-RPC `isError` invariant preserved across migration (Task 21 fix-up)

The catalog dispatch path detects `ActionResult` returns and sets `isError = ar.Error != null` (mirroring the old hardcoded behavior). The failure-warning log line is restored. Non-ActionResult catalog returns (strings, future structured types) keep `isError: false`. Caught by spec review; would have been a silent client-visible regression otherwise.

---

## What's open for Neo (only Neo can do these)

### Smoke matrix (Task 35 Steps 3-4)

`docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md` has a checklist table. Two matrices to run:

1. **Studio Pro 11.10.x** — set `MendixDeployTarget11x` to a 11.10 testbed, build, open Studio Pro, click Extensions → Concord → Open Pane, verify: pane opens; `curl http://localhost:7783/health` returns 200; `claude mcp list` shows `concord-mcp` connected; one tool per family invokes. Expected `tools/list` count: ~37 (the curated allowlist).
2. **Studio Pro 10.24.13** — set `MendixDeployTarget10x` to a 10.24.13 testbed, same procedure. This is the FIRST runtime test of the Host10x UI port after Phase 6's compile-time-only validation. Expected `tools/list` count: ~87 (full SPMCP surface). Watch for: MEF activation timing differences, WebView2 differences, run-state event ordering — these are the runtime-only divergences that the compile-time check couldn't catch.

If anything diverges, capture the finding in `docs/superpowers/handoffs/2026-05-13-after-w2-smoke.md` and roll forward to alpha.3 before tagging.

### Tag (Task 35 Step 6)

```bash
git tag -a v5.0.0-alpha.2 -m "Concord 5.0.0-alpha.2 — W2 SPMCP merge + Host10x UI port"
# Push only if smoke is clean:
git push origin v5.0.0-alpha.2
```

**Do not tag until both smoke matrices are green.** If 10.24.13 surfaces a runtime divergence, either fix in alpha.2 before the tag OR narrow the CHANGELOG's "full Host10x UI port" claim to "compile-time parity; runtime parity in alpha.3" and tag the narrower scope.

### PR / merge

After the tag, the branch is ready for a PR to `main`. The merge is otherwise unblocked.

---

## Deferred to v5.0.0-alpha.3 (W2 backlog)

Tasks the W2 plan templated but Framing 3 chose to defer:

- **Task 30: Audit + remove dead pre-W1 injection paths.** Grep `Func<RunConfigurationSnapshot>` / `Func<(string? path, string? name)>` / `Mendix.StudioPro.ExtensionsAPI` in Core. After Task 29's migration, the search should be empty. If anything remains, it's dead code or a legacy path — delete/rewire.
- **Task 31: Fix CS0414 pragma codes.** All `#pragma warning disable CS0649` lines in Host10x + Host11x should be `CS0414` (the actual warning code firing). Cosmetic. PowerShell one-liner in the plan handles this.
- **Task 32: Consolidate `RunConfigurationSnapshot` → `RunConfigurationInfo`.** Two near-duplicate types coexist (`Terminal.RunConfigurationSnapshot` DTO with nullable fields; `Terminal.Interop.RunConfigurationInfo` record with non-nullable Id/Name). Pick the Interop record as canonical; replace the DTO.
- **Per-tool descriptions on `ITool`.** Add nullable `Description` and `InputSchema` properties to `ITool`. SPMCP bootstrap can leave them null (catalog falls back to today's generic placeholder); UI/Maia bootstraps populate the rich strings the deleted `HandleToolsList` block had. Restores the alpha.1 description quality for MCP clients that surface descriptions to users.

---

## Phase 6 obligation still open

Phase 6 Task 27 Step 4 (Studio Pro 10.24.13 smoke test) was deferred to "next milestone." That next milestone IS Phase 8's Task 35 Step 4 above. **Do not skip it.** Compile-time parity passed Phase 6; runtime parity is the deciding gate.

---

## Things NOT to do (additions for the smoke + post-tag work)

- **Don't tag before 10.24.13 smoke.** Same as above. The CHANGELOG promises feature parity; if 10.24.13 falls over, the tag is making a claim the code can't back up.
- **Don't push the tag without a separate decision.** `git tag -a` is local-only; `git push origin v5.0.0-alpha.2` is Neo's call.
- **Don't merge to main without the tag.** PR + tag should land together so consumers of the marketplace listing get an unambiguous "this is alpha.2" reference.
- **Don't run Studio Pro 10.x and 11.x against the same project root simultaneously.** The two-extension layout requires per-host `MendixDeployTarget10x` and `MendixDeployTarget11x`. Cross-deployment causes a type-resolution crash inside the loader's `.Single()` during MEF discovery. Documented in `Concord.Host10x.csproj` comments.
- **Don't roll the `tools/list` rich-description regression as "blocker" without testing.** Claude Code, Codex, and Copilot CLI all accept open schemas. The brief explicitly says "Phase 9+ polish." If user-visible tool selection degrades materially in the smoke matrix, escalate; otherwise the alpha.3 follow-up is the right home.

---

## TL;DR for the new session

1. **W2 plan execution is complete except the Neo-only runtime gates.** Task 29 → Task 21 → fix-up → docs → version bump all landed in 5 commits (`454d7af` through `f1858c0`).
2. **272 tests passing, build clean.** Up from 244 in alpha.1.
3. **The branch is one tag away from shippable** — pending Neo's smoke matrix on Studio Pro 11.10 and 10.24.13.
4. **Tasks 30-32 + per-tool descriptions deferred to alpha.3.** Backlog noted above; no urgency.
5. **The Phase 7 trap held.** `HostServices.Register` 4-arg / 11-arg overloads were not modified — extension via additive setters only. The 7 extended Interop hosts continue to throw `NotInitialized` until a future cycle wires them through Host*Entry.
