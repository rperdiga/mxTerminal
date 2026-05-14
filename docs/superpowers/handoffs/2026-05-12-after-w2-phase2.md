# Handoff: after W2 Phase 2 (Concord 5.x, Interop contracts landed) — 2026-05-12

> **For the next session:** Plan W2 ([`docs/superpowers/plans/2026-05-12-concord-w2-mcpx-merge.md`](../plans/2026-05-12-concord-w2-mcpx-merge.md)) was drafted and Phases 0-2 (Tasks 1-8 of 35) were executed via subagent-driven development. This doc orients the next session for Phase 3 onward.

---

## Quick orientation

- **Branch:** `feat/v5.0.0-w2-mcpx-merge` (pushed to origin, branch + remote in sync at the time of writing)
- **HEAD:** `5ac92e6` ("feat(core): extend HostServices with 7 new accessors (W2 Task 8)")
- **W1 anchor:** `v5.0.0-alpha.1` at `6dbfbf7`. The W2 branch was cut from this tag.
- **Tests:** 249 passing (245 W1 baseline + 4 new from Task 8), 3 skipped (pre-existing Maia live).
- **Build:** `dotnet build Terminal.sln` is clean modulo the pre-existing CS0414/CS8604 warnings inherited from W1. Task 31 fixes CS0414 in Phase 8.
- **Working tree:** clean.

**What's been done in W2 so far:**

| Phase | Tasks | Result |
|---|---|---|
| Phase 0 | 1 | Branch created off `v5.0.0-alpha.1`. MCPExtension local-git-init'd at `C:\Extensions\MCPExtension` with tag `concord-w2-import`. Spike notes file seeded. |
| Phase 1 | 2, 3 | `git subtree add` pulled MCPExtension into `src/Concord.Core/Spmcp/`. Pruned duplicate MCP server, pane host, dev scripts, SPMCP module project, residual marketplace artifacts. `MendixAdditionalTools.cs` (10,211 lines) + `MendixDomainModelTools.cs` (4,871 lines) + 9 handlers in tree, **un-compiled** (csproj `Compile Remove="Spmcp/**/*.cs"`). |
| Phase 2 | 4, 5, 6, 7, 8 | SPMCP service inventory captured. Seven Core `Interop` interfaces defined (`IModelHost` 16 methods, `IDomainModelHost` 34 methods, `IPageGenerationHost` 2, `INavigationHost` 7, `IVersionControlHost` 1 prop + 1, `IUntypedModelHost` 1 prop + 3, `IMicroflowAuthoringHost` 1 prop + 14). `HostServices` extended with 7 new accessors + 11-arg `Register` overload (legacy 4-arg preserved). 4 new TDD tests + 11 fake host classes under `tests/Concord.Core.Tests/Fakes/`. |

**What's NOT done:** Phases 3-8 (Tasks 9-35) — see "Remaining work" below.

---

## Key documents

| Doc | Why |
|---|---|
| [`docs/superpowers/plans/2026-05-12-concord-w2-mcpx-merge.md`](../plans/2026-05-12-concord-w2-mcpx-merge.md) | The 35-task plan. Each task has file paths + concrete code blocks + commit message templates. Read the task you're about to dispatch in full before crafting the implementer prompt. |
| [`docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md`](2026-05-12-concord-w2-spike-notes.md) | Live findings: SPMCP service inventory, type → interface mapping, four out-of-scope-leakage items (Texts, JavaActions, namespace ambiguity, IServiceProvider absorption). |
| [`docs/superpowers/handoffs/2026-05-12-after-w1.md`](2026-05-12-after-w1.md) | W1 architecture context: two-extension layout, ExtensionsAPI version drift (10.21.1 vs 11.6.2), MEF activation order, naming smells (`RunConfigurationInfo` duplicate, dual injection mechanisms). |
| [`docs/superpowers/handoffs/2026-05-12-before-w2.md`](2026-05-12-before-w2.md) | The handoff that drove this W2 plan. Branch-strategy guidance, the three W2 strands (B1/B2/B3), things not to do. |
| [`docs/superpowers/specs/2026-05-12-concord-cross-version-mcp-merge-design.md`](../specs/2026-05-12-concord-cross-version-mcp-merge-design.md) | Umbrella design. W3 + W4 sections (lines 162-285) are the source of truth for the family-toggle UI, allowlist contents, escalation contract, etc. — out of W2 scope but informative. |

---

## Open questions / decisions captured during Phase 2

These came out of the inventory (Task 4) and the Phase 2 interface-design tasks. Phase 3-4 work needs to address them — flag in the dispatch when relevant.

### 1. `BaseApiHandler(IModel)` — refactor strategy

All 9 SPMCP handlers in `src/Concord.Core/Spmcp/Handlers/*.cs` extend a `BaseApiHandler` whose constructor takes `IModel currentApp`. Phase 4 Task 16 must decide:

- **Option A:** Rework `BaseApiHandler` to take no parameters and read `HostServices.Model` from inside its methods. Each handler stays a subclass.
- **Option B:** Remove the base class entirely; each handler reads from `HostServices.Model` directly.

Option A is the smaller change (one base-class edit + zero subclass edits). Option B is cleaner but touches every handler. Default recommendation: **Option A** for the W2 milestone, with Option B as a follow-up if the base class is found to do nothing else useful.

### 2. Constants / enumerations CRUD interface gap

The SPMCP methods `CreateConstant`, `ListConstants`, `UpdateConstant`, `CreateEnumeration`, `ListEnumerations`, `UpdateEnumeration` (all in `MendixDomainModelTools.cs`) didn't land on `IModelHost` (Task 5) or `IDomainModelHost` (Task 6). Both subagents explicitly excluded them — IDomainModelHost says they're "module-level documents closer to IModelHost," and IModelHost focused on document **traversal** rather than CRUD on individual document types.

Resolution options for Phase 4:
- **Option A:** Extend `IModelHost` with constant + enumeration CRUD methods (a small addition; matches the inventory's "read on IModelHost, write on IDomainModelHost" intent — collapse both sides onto IModelHost). Cleanest.
- **Option B:** Add a tiny new `IConstantsHost` interface.

Default: **Option A**. The Phase 4 subagent refactoring `CreateConstant`/`ListConstants` will surface the gap and can extend `IModelHost` in a side-commit before continuing the slice.

### 3. Studio Pro 11.x `tools/list` snapshot — still TBD

Task 1 Step 3 deferred capturing a live `mendix-studio-pro__tools/list` snapshot from a running Studio Pro 11.10 with its MCP server attached. The W2 plan's Phase 5 Task 18 uses spec lines 198-211 (the umbrella spec's curated allowlist starting point) as the working assumption.

Before W2 ships, run the snapshot reconciliation:

1. From a Claude Code CLI in a running Studio Pro 11.10 Concord pane, capture: `claude mcp list-tools mendix-studio-pro --format=json > docs/superpowers/handoffs/2026-05-12-studio-pro-11x-tools.json`.
2. Compare the studio-pro tool names to `Studio11xAllowlist.cs` (added in Task 18).
3. Remove any tool from the allowlist that studio-pro now covers; add any tool whose studio-pro coverage looks weak.
4. Update CHANGELOG to note the reconciliation.

This work is small (1-2 hours) and gateable on Phase 5 completion. **Do not wait on it before starting Phase 3.**

### 4. `INavigationManagerService` lives in two namespaces

The inventory flagged that `INavigationManagerService` appears under both `Mendix.StudioPro.ExtensionsAPI.Services` (used by `MendixAdditionalTools`) and `Mendix.StudioPro.ExtensionsAPI.UI.Services` (used by `GenerateOverviewHandler`). Same interface, two namespace imports. The Phase 3 `NavigationHost11x` implementation will resolve which is the canonical one for the Core tier — most likely `Services` (non-UI) since Core isn't UI-tier. Capture the finding in spike notes when Task 11 is executed.

### 5. `MendixAdditionalTools.SaveAsync` API verification

The Phase 2 `IModelHost.SaveAsync` is defined but no Host11x body verified yet. Task 9 must verify the precise 11.6.2 `IModel` save method (`Save()` vs `SaveAll()` vs `CommitTransaction()` — the plan's Task 9 prompt explicitly calls this out). Spike notes get updated with the finding.

---

## Remaining work — 27 tasks across Phases 3-8

Each phase has its own milestone shape. Use the table to plan dispatch order.

### Phase 3 — Host implementations of the 7 Interop interfaces (Tasks 9, 10, 11)

**Largest mechanical phase**. Each task = "implement interface on Host11x" + "mirror to Host10x" + "build both hosts".

- **Task 9** (`IModelHost`): 16 methods × 2 hosts = 32 method bodies. Host11x bodies lift from `MendixAdditionalTools.cs` (`SaveData`, `ReadVersionControl`, `ManageFolders`); Host10x mirrors with `src/Concord.Core/Spmcp/backport-10x/reference/` as drift guide.
- **Task 10** (`IDomainModelHost`): 34 methods × 2 hosts = 68 method bodies. The biggest single dispatch in the plan. **Strongly recommend this as a standalone subagent dispatch** with a clear "stop and report if you hit 5+ unresolved 10.x API drifts" escape hatch.
- **Task 11** (5 remaining interfaces): 26+ methods × 2 hosts = ~52 method bodies. `IVersionControlHost` and `IUntypedModelHost` on Host10x likely return `IsAvailable=false` if 10.21.1 doesn't expose the underlying services — verify with `Select-String -Path src/Concord.Core/Spmcp/backport-10x/reference -Pattern "IVersionControlService"`.

**Tip:** Phase 3 is when API drift between 10.21.1 and 11.6.2 actually bites. If a method's 10.x equivalent doesn't exist (e.g., a service that was added in 11.x), have Host10x throw `NotSupportedException` from that method — the corresponding SPMCP tool will return a structured `escalation: manual` response in W3.

**Phase 3 commit cadence:** one commit per task (3 total). Each task is a unit of work even though it touches ~14 files.

### Phase 4 — Refactor SPMCP tools to depend on Core Interop (Tasks 12-17)

**Biggest single chunk of W2 by far**. Plan-time estimate: 14+ slice commits across `MendixAdditionalTools.cs` (10,211 lines) + `MendixDomainModelTools.cs` (4,871 lines) + 9 handlers.

- **Task 12** — namespace rename + add Microsoft.Extensions.Logging.Abstractions package + remove `Compile Remove="Spmcp/**/*.cs"`. **The build will break at the end of Task 12** by design (CS0246 errors from Mendix.* using statements). Tasks 13-16 resolve them slice-by-slice.
- **Task 13** — `MendixAdditionalTools` read paths, slices 1-6 (constructor refactor, sample data, microflow read, diagnostics).
- **Task 14** — `MendixAdditionalTools` write paths, slices 7-14 (page gen, microflow author, settings/config, security). One commit per slice (8 commits).
- **Task 15** — `MendixDomainModelTools` — 5 slice commits (read, create, update, rename, delete + arrange + diagnostics).
- **Task 16** — 9 Handlers refactored.
- **Task 17** — fake-host SPMCP smoke tests. Confirms each tool instantiates + dispatches without referencing Studio Pro types.

**Phase 4 success criterion:** `dotnet list src/Concord.Core/Concord.Core.csproj package` shows zero `Mendix.StudioPro.ExtensionsAPI` references. Core builds clean.

**Dispatch advice:** Phase 4 tasks are large enough that **each slice within Task 13/14/15 should be its own subagent dispatch** rather than one big "implement Task 14" dispatch. The plan body lists the slices explicitly. The controller-side cost: ~30K tokens per slice × ~25 slices = ~750K tokens for Phase 4 alone. Consider running Phase 4 in a dedicated session.

### Phase 5 — ToolCatalog + ITool + version-aware registration (Tasks 18-21)

After Phase 4, Core compiles without Studio Pro types. Phase 5 wires the catalog.

- **Task 18** — `ITool`, `ToolFamily`, `ToolCatalog`, `Studio11xAllowlist`. 4 new files in `src/Concord.Core/Mcp/`. TDD tests for registration + mode filter + family disable + dispatch. **This is where the open question #3 (live tools/list reconciliation) might inform the Studio11xAllowlist contents** — but the spec's allowlist works as a default.
- **Task 19** — `SpmcpToolBootstrap{10x,11x}.cs` files instantiate the refactored tool classes from each host and register ~80 tool entries into the catalog.
- **Task 20** — refactor `StudioProActionServer.cs` to dispatch through `ToolCatalog.InvokeAsync` instead of the hand-rolled switch. Introduces `ToolCatalogRegistry.Active` global.
- **Task 21** — migrate UI-action tools (`run_app`, `stop_app`, `save_all`, `refresh_project`) and Maia tools into catalog entries. `UiActionsBootstrap.cs`, `MaiaToolsBootstrap.cs`.

### Phase 6 — Host10x UI port (Tasks 22-28)

Replaces the "10.x preview" placeholder with the real pane + terminal + MCP + Maia.

- **Task 22** — package refs (Eto.Forms + any others Host11x has).
- **Task 23** — port `TerminalPaneViewModel`. Likely a near-verbatim copy (UI framework code, no Studio Pro coupling).
- **Task 24** — port `TerminalPaneExtension`. **API drift hot spot** — 10.x's `DockablePaneExtension` is an abstract base class vs 11.x's `IDockablePaneExtension` interface. Verify against `src/Concord.Core/Spmcp/backport-10x/reference/Mendix.StudioPro.ExtensionsAPI.xml` (the included XML doc file).
- **Task 25** — port `TerminalWebServer` against 10.x's `IWebServerExtension` (same drift pattern likely).
- **Task 26** — port `RunStateProbe` + `StudioProUiAutomation` (the UI-automation class is largely Win32 — verbatim copy with namespace adjust).
- **Task 27** — replace `ConcordMenuExtension.cs` placeholder with the real `TerminalMenuExtension.cs` (mirrors Host11x). Smoke against Studio Pro 10.24.13 to verify the pane opens.
- **Task 28** — delete `src/Concord.Core/Spmcp/backport-10x/` reference. By this point, every drift is encoded in Host10x's `Interop/` files.

### Phase 7 — HostServices consolidation (Tasks 29-30)

The W1 handoff flagged the dual injection mechanism. Phase 7 collapses it.

- **Task 29** — Migrate `StudioProActions` to read `IRunStateProbe` + `IStudioProUiAutomation` + `IRunConfigurationsHost` + `IStudioProAppHost` from `HostServices` instead of constructor `Func<>` callbacks. Extend `HostServices` with `RunStateProbe` + `UiAutomation` accessors. Update `Terminal.Tests` that constructed `StudioProActions` with fakes.
- **Task 30** — audit + delete leftover legacy paths. Confirm Core has zero `Mendix.*` references and no `Func<>` callbacks for host services.

### Phase 8 — Polish + smoke matrix + version bump (Tasks 31-35)

- **Task 31** — fix `#pragma warning disable CS0649` → `CS0414` in Host10x/Host11x sentinel sites. Eliminates the 5 pre-existing warnings.
- **Task 32** — consolidate `Terminal.Interop.RunConfigurationInfo` and `Terminal.RunConfigurationSnapshot` (the duplicate flagged in W1 handoff).
- **Task 33** — update `DEPLOYING.md` migration section + CHANGELOG.md + README.md project-layout note.
- **Task 34** — bump version to `5.0.0-alpha.2` in all three csproj files.
- **Task 35** — full-stack smoke matrix. **User-blocking** — Joe deploys to `C:\Projects\Test_10_24_13` and `C:\Projects\Test_11_10`, opens Concord on both Studio Pro versions, exercises one tool per family. Tag `v5.0.0-alpha.2` if smoke passes.

---

## Recommended dispatch order for the new session

If you're starting fresh, dispatch tasks in this sequence:

1. **Phase 3 batch:** Tasks 9 → 10 → 11. Each task is one large subagent dispatch (Task 10 is the biggest — IDomainModelHost on both hosts is 68 method bodies). Aim for one commit per task.

2. **Phase 4 — handle with care.** Each slice in Tasks 13/14/15 should be its own subagent dispatch. ~25 slice dispatches total. Strongly recommend a dedicated session for Phase 4 alone — controller context will be tight by the end.

3. **Phase 5 batch:** Tasks 18 → 19 → 20 → 21. Smaller, code-heavy but well-defined.

4. **Phase 6 batch:** Tasks 22-28. Host10x port. Task 24 (`TerminalPaneExtension` against 10.x) is the API-drift hot spot — budget extra context for it.

5. **Phase 7 batch:** Tasks 29-30. Refactor + audit.

6. **Phase 8 batch:** Tasks 31-34 mechanically, then Task 35 needs Joe (smoke matrix on real Studio Pro installs).

**Estimated total wall time at the current per-task cadence (~3-5 min per dispatch + verification):** 4-8 hours of focused execution, divided across 2-3 sessions to keep context manageable.

---

## Things NOT to do (additions to W1's list)

- **Don't merge Phase 4 slices into one big "refactor the whole tools file" commit.** The slice-by-slice cadence (one commit per ~10 tool methods) is the load-bearing pattern. Big-bang refactors of 10,000-line files go badly.
- **Don't add new Interop interfaces without updating the spike notes.** Phase 2's spike notes captured the inventory; Phase 3-4 will discover gaps. Each gap → spike-notes amendment → interface extension commit (split from the main task's commit).
- **Don't bypass `HostServices` once Phase 7 lands.** Even one-off direct service injection regrows the dual mechanism that Phase 7 worked to remove.
- **Don't ship W2 without the tools/list reconciliation (open question #3).** It's small but it's the difference between "the allowlist is the spec's best guess" and "the allowlist matches what 11.x actually advertises today."
- **Don't squash the W2 branch into main without the smoke matrix passing.** Task 35 is the gate. Joe smoke-tests manually; a passing test suite isn't proof the extension loads.

---

## Per-CLI dispatch pattern that worked in Phase 2

For continuity if you're using subagent-driven development:

1. **Dispatch shape:** one general-purpose subagent (model: sonnet) per task. Token budget per dispatch: ~30K (implementer report) + ~5K (your verification step). Phase 4 slices may push higher.

2. **Implementer prompt structure** (lift from `C:\Users\rc1yok\.claude\plugins\cache\claude-plugins-official\superpowers\5.1.0\skills\subagent-driven-development\implementer-prompt.md`):
   - Frame the task with file paths + branch + HEAD SHA.
   - Paste the relevant section of the W2 plan verbatim (don't make the subagent read the plan file — it'll waste context).
   - Provide inventory findings from `2026-05-12-concord-w2-spike-notes.md` when relevant.
   - Self-review checklist at the end.
   - Explicit "do not push to origin" unless it's a milestone push.

3. **Verification step in the controller:** after each subagent reports DONE/DONE_WITH_CONCERNS, run a quick `git log --oneline ...` + `dotnet build` to confirm. Don't dispatch a separate spec-reviewer for purely mechanical tasks — for design-sensitive tasks (Phase 3 host implementations, Phase 5 ToolCatalog design) a fresh reviewer subagent is worth the cost.

4. **Milestone push cadence:** push after each phase completes. Phase 1 + 2 was pushed at HEAD `5ac92e6` after Task 8 finished.

---

## Quick commit reference (W2 commits so far)

| Commit | What |
|---|---|
| `ae13fda` | W2 spike notes seeded (Task 1) |
| `ae56df6` | Squashed MCPExtension subtree content (Task 2) |
| `053813b` | Merge commit for the subtree add (Task 2) |
| `f31d7a3` | Build-exclude `Spmcp/**/*.cs` until Phase 4 (Task 2 fallback) |
| `f561249` | Prune duplicated SPMCP plumbing (Task 3) |
| `6e3d834` | Remove residual marketplace + dotfile noise (Task 3 cleanup) |
| `6d7a2ab` | SPMCP service inventory (Task 4) |
| `554b54f` | `IModelHost` (Task 5) |
| `9a20ffe` | `IDomainModelHost` (Task 6) |
| `e8fe00f` | 5 remaining Interop interfaces (Task 7) |
| `5ac92e6` | Extend `HostServices` + tests (Task 8) — HEAD |

---

## TL;DR for the new session

1. **Read this handoff, then [`docs/superpowers/plans/2026-05-12-concord-w2-mcpx-merge.md`](../plans/2026-05-12-concord-w2-mcpx-merge.md)** — specifically the Phase 3 section (Tasks 9-11).
2. **Skim the spike notes** at [`2026-05-12-concord-w2-spike-notes.md`](2026-05-12-concord-w2-spike-notes.md) — five out-of-scope-leakage items affect Phase 3-4 decisions.
3. **Use superpowers:subagent-driven-development** to dispatch Phase 3 tasks. The pattern is documented above.
4. **Resume on branch `feat/v5.0.0-w2-mcpx-merge` at `5ac92e6`** — clean, pushed, 249 tests green.
5. **Suggested first action:** dispatch Task 9 (`IModelHost` implementations on both hosts).
