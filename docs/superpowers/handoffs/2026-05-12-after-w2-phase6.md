# Handoff: after W2 Phase 6 (Tasks 22-28 — Host10x UI port complete) — 2026-05-12

> **For the next session:** Phase 6 of Plan W2 ([`docs/superpowers/plans/2026-05-12-concord-w2-mcpx-merge.md`](../plans/2026-05-12-concord-w2-mcpx-merge.md)) is **complete**: Host10x is now feature-complete with the full UI tier ported from Host11x (terminal pane, web server, run-state probe, UI automation, real menu replacing the v5.0.0-alpha.1 placeholder). The biggest finding: **zero Mendix API drift between 10.21.1 and 11.6.2** across every type Concord touches. The plan template's anticipated drift (interface vs base class on `DockablePaneExtension`, namespace differences on `IExtensionFileService`, signature changes on `ILocalRunConfigurationsService`) did not materialize. Every port was a pure namespace rename. Phase 5 handoff and earlier remain authoritative for prior context.

---

## Quick orientation

- **Branch:** `feat/v5.0.0-w2-mcpx-merge` (pushed to origin)
- **HEAD (this writing):** `16a5145` — `chore(spmcp): retire imported backport-10x/ reference (W2)`. After the handoff commit, HEAD will be `+1`.
- **Tests:** **257 passing** (241 Terminal.Tests + 16 Concord.Core.Tests), 3 skipped, 0 failed — exactly identical to the Phase 5 baseline. The UI port added no new tests (none were specified in the plan; the new files are framework-coupling code that's smoke-tested at runtime, not unit-tested).
- **Build:** **CLEAN** — `dotnet build Terminal.sln` succeeds with 0 errors. Only pre-existing nullable + CS0414 warnings.
- **Working tree:** clean (after this handoff commit)

**Phase 6 functional success criteria — met:**
1. `src/Concord.Host10x/Pane/TerminalPaneExtension.cs` exists and subclasses the same `DockablePaneExtension` base type as Host11x
2. `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs` exists and extends `WebViewDockablePaneViewModel` identically to Host11x
3. `src/Concord.Host10x/Ui/TerminalWebServer.cs` exists and exports the same `WebServerExtension` MEF surface as Host11x
4. `src/Concord.Host10x/Interop/RunStateProbe.cs` + `StudioProUiAutomation.cs` exist with identical bodies to Host11x (modulo namespace)
5. `src/Concord.Host10x/MenuExtensions/TerminalMenuExtension.cs` exports `MenuExtension`, opens the pane via `IDockingWindowService.OpenPane(TerminalPaneExtension.ID)`. The `ConcordMenuExtension.cs` placeholder is deleted.
6. `src/Concord.Core/Spmcp/backport-10x/` is gone (5 files of reference docs, 278 lines)
7. Studio Pro 10.x users now get the full pane + concord-mcp + Maia bridge surface (subject to a real-Studio-Pro smoke test that only Neo can run)

---

## Phase 6 commit chain (7 commits)

| Commit | Task | What |
|---|---|---|
| `705005b` | 22 | `build(host10x): add Eto.Forms UI-tier dep for upcoming pane port (W2)` — Adds explicit `<PackageReference Include="Eto.Forms" Version="2.9.*" />` to Host10x.csproj. Per recon, Eto.Forms was previously transitive via Concord.Core.csproj — this makes it explicit so Host10x's pane VM compiles independently. The plan's premise (Host11x has Eto.Forms as direct ref) was wrong; corrected on the fly. |
| `cb94a23` | 23 | `feat(host10x): port TerminalPaneViewModel against 10.21.1 (W2)` — Near-verbatim copy of Host11x's TerminalPaneViewModel.cs (~615 LOC, the WebView ↔ session-manager bridge). Only changes: namespace `Concord.Host11x.Pane` → `Concord.Host10x.Pane`, using-line renames, inlined a 3-line `StudioProVersionFromExePath` helper that was on Host11x's `TerminalPaneExtension` (not yet ported at that point — forward dependency would have forced a 2-step). All Mendix UI types (`WebViewDockablePaneViewModel`, `IWebView`, `IProject`, `IModel`, `MessageReceivedEventArgs`) resolved identically against 10.21.1 — zero drift. |
| `b459a51` | 23 (fixup) | `refactor(host10x): split PaneInteropHelpers into Interop/RunStateProbe+StudioProUiAutomation (W2)` — Task 23 implementer over-applied a "don't edit files outside Pane/" guardrail and bundled `RunStateProbe` + `StudioProUiAutomation` into `Pane/PaneInteropHelpers.cs` (under namespace `Concord.Host10x.Interop`). This fixup splits them into the proper per-file layout under `Interop/` (matching Host11x and the Task 26 spec) and deletes the bundled file. Effectively completes Task 26 as a side-effect — Task 26 itself becomes a no-op verification. |
| `59c7da3` | 24 | `feat(host10x): port TerminalPaneExtension against 10.21.1 (W2)` — Near-verbatim copy of Host11x's TerminalPaneExtension.cs (~705 LOC, MEF-exported `[Export(typeof(DockablePaneExtension))]`). The plan template speculated 10.x might use an interface (`IDockablePaneExtension`) instead of a base class; the package XML doc confirmed 10.21.1 ALSO uses the abstract base class `Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneExtension`. All consumed APIs (`Subscribe<T>`, `CurrentApp`, `WebServerBaseUrl`, `IExtensionFileService.ResolvePath`, `ILocalRunConfigurationsService.GetActiveConfiguration`, `ExtensionUnloading`, `DockablePaneViewModelBase`) present unchanged. |
| `480f941` | 25 | `feat(host10x): port TerminalWebServer against 10.21.1 (W2)` — Mirror of Host11x's TerminalWebServer.cs (~70 LOC). `WebServerExtension`, `IWebServer.AddRoute`, `IExtensionFileService.ResolvePath` present identically. Created `src/Concord.Host10x/Ui/` directory (didn't exist). Routes (`/`, `/terminal/ws`, `/maia/*`) reuse Core handler bodies verbatim. |
| `8b5cb5e` | 27 | `feat(host10x): wire the real terminal menu, retire 'preview' placeholder (W2)` — Created `MenuExtensions/TerminalMenuExtension.cs` (32 lines, exports `MenuExtension`, calls `docking.OpenPane(TerminalPaneExtension.ID)`). Deleted `MenuExtensions/ConcordMenuExtension.cs` (44 lines, the v5.0.0-alpha.1 MessageBox placeholder). Studio Pro 10.x clicks now open the real terminal pane. The pane ID is `"Concord"` (not `"Concord.Terminal"` as the plan template speculated — the actual constant value matches Host11x). |
| `16a5145` | 28 | `chore(spmcp): retire imported backport-10x/ reference (W2)` — Deletes `src/Concord.Core/Spmcp/backport-10x/` (5 files: `MCPExtension.10x.csproj`, `README.md`, `TOOLS-COMPARISON.md`, `manifest.json`, `start-studiopro-10x.bat` — 278 lines, 14.3 KB). Pure documentation/reference; no .cs source was ever there. The 10.x API drift it documented now lives in `src/Concord.Host10x/Interop/*Host10x.cs` and the ported UI tier. |

Net change vs ee10eb0: +1900 / −322 lines across 13 files. The bulk is in Host10x's three new big files (TerminalPaneExtension 708 LOC, TerminalPaneViewModel 641 LOC, StudioProUiAutomation 394 LOC).

---

## The big finding: zero Mendix API drift across 10.21.1 ↔ 11.6.2

Every Mendix type Concord's UI tier consumes resolves IDENTICALLY against both NuGet versions. Verified types:

| API surface | Host11x (11.6.2) | Host10x (10.21.1) |
|---|---|---|
| `Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneExtension` | abstract base class | abstract base class — identical |
| `Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.WebViewDockablePaneViewModel` | base class | base class — identical |
| `Mendix.StudioPro.ExtensionsAPI.UI.WebView.IWebView` | interface | interface — identical |
| `Mendix.StudioPro.ExtensionsAPI.UI.WebView.MessageReceivedEventArgs` | event args type | event args type — identical |
| `Mendix.StudioPro.ExtensionsAPI.UI.WebServer.WebServerExtension` | abstract base class | abstract base class — identical |
| `Mendix.StudioPro.ExtensionsAPI.UI.WebServer.IWebServer.AddRoute(...)` | extension method | extension method — identical |
| `Mendix.StudioPro.ExtensionsAPI.UI.Menu.MenuExtension` | abstract base class | abstract base class — identical |
| `Mendix.StudioPro.ExtensionsAPI.UI.Services.IDockingWindowService.OpenPane(string)` | service method | service method — identical |
| `Mendix.StudioPro.ExtensionsAPI.Services.IExtensionFileService.ResolvePath(...)` | service method | service method — identical |
| `Mendix.StudioPro.ExtensionsAPI.Services.ILocalRunConfigurationsService.GetActiveConfiguration(model)` | returns `LocalRunConfiguration?` | returns `LocalRunConfiguration?` — identical, with `.ApplicationRootUrl`, `.Id`, `.Name` all present |
| `Mendix.StudioPro.ExtensionsAPI.Model.IModel`, `IProject` | interfaces | interfaces — identical |
| `Mendix.StudioPro.ExtensionsAPI.UI.Events.Subscribe<T>` | event subscription | identical |

**Implication for future work:** The Phase 4 catalog of API gaps (constants CRUD, untyped-element traversal, IConsistencyHost, etc.) is a list of things SPMCP wanted that Mendix doesn't expose on EITHER version — not a list of 10.x-vs-11.x divergences. Don't conflate "API surface we need but Mendix doesn't expose" with "API surface that differs between 10.21.1 and 11.6.2". The latter set is empty for everything Concord currently uses.

**Caveat:** This is API-shape parity, not runtime-behavior parity. Studio Pro 10.24.13 may behave differently at runtime than Studio Pro 11.10+ in ways the compile-time check can't catch (e.g., MEF activation timing, WebView2 version differences, run-state event ordering). Smoke testing on a real 10.24.13 installation is still required before claiming feature parity.

---

## Critical patterns established in Phase 6 (preserve them)

1. **Subagent file-scope guardrails are sharp instruments.** When dispatching an implementer with a "don't edit files outside X/" instruction, expect them to bundle related code into X/ even when it logically belongs elsewhere. Task 23 dispatched with "don't edit files outside `src/Concord.Host10x/Pane/`" → implementer correctly ported `TerminalPaneViewModel.cs` to `Pane/`, then bundled `RunStateProbe` + `StudioProUiAutomation` into `Pane/PaneInteropHelpers.cs` rather than creating files under `Interop/`. The fix was a separate fixup commit (`b459a51`). Better wording for next time: "Edit only files at the prescribed path; if you need a dependency that doesn't exist, BLOCK and report rather than improvising location."

2. **Sequencing pre-empted dependencies pays off.** Task 23 inlined `TerminalPaneExtension.StudioProVersionFromExePath()` (a 3-line static helper) into the VM rather than waiting for Task 24. This avoided a 2-step ordering (port pane VM → port pane extension → re-edit pane VM to delete the inlined helper). For 3-line helpers, inline; for anything bigger, split tasks. (Note: the inlined helper now exists in BOTH Host10x's TerminalPaneViewModel and TerminalPaneExtension. Future cleanup could promote it to a shared static — Phase 7+ polish, not urgent.)

3. **The plan template's "interface vs base class" speculation was wrong.** The plan ([line 2191](../plans/2026-05-12-concord-w2-mcpx-merge.md#L2191)) suggested 10.x might have switched `DockablePaneExtension` from base class to interface. Verification (via the package XML doc) showed both are abstract base classes. Future plans that speculate about API drift between Mendix versions should weight "no drift" as the strong prior — Mendix's public ExtensionsAPI surface is more stable than the SPMCP backport-10x reference's narrow snapshot suggested.

4. **`#pragma warning disable CS0414` on MEF sentinel fields is a project-wide pattern.** Every MEF-imported `_entry` field across both hosts uses this pattern. The CS0649 disable doesn't fully suppress CS0414 ("assigned but value never used"); the latter fires anyway. This is cosmetic, expected, and not worth fixing in isolation. Phase 7 Task 31 (fix CS0414 codes) addresses it.

5. **Mechanical port tasks don't need spec-reviewers.** Phase 6 ran with implementer-only dispatches (no spec-reviewer, no code-quality reviewer) for tasks 22-28 because every task was a near-verbatim namespace rename. The build + test gate is the implicit reviewer. Phases that involve design decisions (e.g., the deferred Task 21 once Task 29 unblocks it — choosing how to register UI/Maia tools into the catalog) should add reviewers back.

---

## Why Task 21 is still deferred (no change since Phase 5)

Task 21 (migrate UI-action + Maia tools into ToolCatalog registrations) remains deferred. Phase 6 did NOT advance Task 21 — it was Phase 6's substantive backlog work (Host10x UI port) that the plan ordering required first. The unblock paths are unchanged from the Phase 5 handoff:

- **Path A (plan-aligned):** Phase 7 Task 29 extends `HostServices` with `IRunStateProbe` + `IStudioProUiAutomation` accessors. Then Task 21 becomes mechanical — `UiActionsBootstrap.Register(catalog)` constructs `StudioProActions` from `HostServices.RunState` and `HostServices.UiAutomation`, registers the 9 UI-action tools; `MaiaToolsBootstrap.Register(catalog, MaiaBridge.Instance)` registers the 7 maia tools.
- **Path B (smaller scope):** Have `TerminalPaneExtension.TryAutoStartActionServer()` register the UI-action + Maia tools into the existing catalog right before starting the server. Bootstrap classes still exist in `Concord.Core` but get called with `(catalog, actions, maia)` from the pane extension, not from `Host{N}xEntry`.

**Recommendation unchanged:** Path A is cleaner architecturally. Phase 7 starts at Task 29 anyway — just do it.

---

## What's next

Per the W2 plan, Phase 7 (HostServices consolidation) and Phase 8 (polish + smoke matrix + version bump) remain. Three valid framings for the next session:

### Framing 1 — strict plan order (Phase 7)

Continue with Tasks 29-32:
- Task 29: Migrate StudioProActions to read run-state + UI automation from HostServices (unblocks Task 21)
- Task 30: Audit and remove now-dead pre-W1 injection paths
- Task 31: Fix the CS0414 pragma codes flagged in the W1 handoff
- Task 32: Consolidate the two RunConfigurationInfo types

After Phase 7, Task 21 (UI/Maia tools into catalog) becomes trivially completable.

### Framing 2 — skip to Phase 8 (CHANGELOG + tag + PR)

Skip Phase 7 (HostServices consolidation is internal cleanup, not a user-visible change). Go straight to:
- Task 33: Update DEPLOYING.md and CHANGELOG
- Task 34: Bump version to 5.0.0-alpha.2
- Task 35: Full-stack smoke matrix (some of which requires Neo's hands)

This ships the alpha sooner. Cost: the catalog still has the dual dispatch (catalog for SPMCP tools + hardcoded fallback for UI/Maia) until Phase 7 cleans it up. Functionally identical for users; cosmetic for code reviewers.

### Framing 3 — interleave (recommended)

Do Task 29 first (smallest Phase 7 task, unblocks Task 21). Then do Task 21. Then jump to Phase 8 (CHANGELOG + tag). Defer Tasks 30-32 + the rest of Phase 7 to the v5.0.0-alpha.3 cycle. This ships an alpha with a fully-clean catalog dispatch (no hardcoded fallback) without paying the full Phase 7 cost.

**My read:** Framing 3 is the sweet spot. Task 29 is small, Task 21 is mechanical once 29 lands, and the alpha tag becomes a clean story ("11.x SPMCP integration + 10.x UI port + unified catalog dispatch"). Tasks 30-32 are housekeeping that doesn't gate any user-facing claim.

---

## Smoke test still required (Neo only)

Phase 6 Task 27 Step 4 was deferred: deploy Host10x to a real Studio Pro 10.24.13 project, click Extensions → Concord → Open Pane, verify the terminal pane opens, the WebView renders, `concord-mcp` HTTP server responds on `http://localhost:7783/health`, one tool per family invokes successfully. This requires Neo's hands. Until this passes, the "Studio Pro 10.x feature parity" claim is compile-time only.

Suggested smoke matrix when the time comes:
- 10.24.13 + Claude Code: pane opens, MCP responds, run/stop/save/refresh UI buttons work
- 10.24.13 + Codex: same
- 10.24.13 + Copilot CLI: same
- 11.10.x + all three CLIs: confirm Phase 6 didn't regress Host11x (compile-time green is necessary but not sufficient)

If any 10.x-specific runtime divergence surfaces during the smoke (MEF activation timing, WebView2 version, run-state event ordering, etc.), capture it as a memory and add it to the Phase 7 backlog.

---

## Things NOT to do (additions for Phase 7+)

- **Don't assume 10.21.1 has different APIs than 11.6.2 without checking.** Phase 6's empirical finding is "zero drift" across every type Concord uses. Future ports/changes should default to "works on both" and only investigate divergence if the build actually fails.
- **Don't promote the inlined `StudioProVersionFromExePath` helper to a shared utility yet.** It's duplicated in TerminalPaneViewModel and TerminalPaneExtension across both hosts (4 call sites total — actually 2 in Host10x + 2 in Host11x). Centralizing it requires deciding where (Concord.Core? Terminal?) and whether to expose `StudioProThemeProbe`-style indirection. Phase 7+ housekeeping; not urgent.
- **Don't switch the tools/list placeholder schema (`{type: "object", additionalProperties: true}`) to per-tool reconstruction without a plan.** Same caveat as Phase 5 — Claude Code, Codex, Copilot CLI all accept open schemas. This is a Phase 9+ polish task.
- **Don't merge to main yet.** v5.0.0-alpha.2 hasn't been tagged. Per Framing 3 above, Task 29 → Task 21 → Phase 8 is the recommended path before tag.
- **Don't claim feature parity with Host11x without the smoke test.** Compile-time green proves the UI-tier types match; runtime parity needs Neo on a real Studio Pro 10.24.13 install.

---

## API gaps cataloged (carryover from Phases 4-5, still valid; no new gaps in Phase 6)

No new gaps surfaced in Phase 6. The Phase 4-5 catalog is still authoritative:
- `IModelHost`: `CreateConstant`, `UpdateConstant`, `DeleteModule`, `RuntimeSetting` After-Startup write
- `IDomainModelHost`: `DeleteConstant`, `DeleteEnumeration` (and verify `UpdateEnumeration` is fully exposed)
- `AttributeRef`: `MaxLength`, `EnumerationQualifiedName`, `DefaultValue`
- `ModuleId`: `FromAppStore`
- `IStudioProAppHost`: `SynchronizeWithFileSystem`
- `IUntypedModelHost`: `GetElementsOfType(unitQualifiedName, elementType)` (would unblock 5 security tools)
- Potential new interface: `IConsistencyHost` for `CheckProjectErrors`
- `HostServices` needs `IRunStateProbe` + `IStudioProUiAutomation` accessors (Task 29 in Phase 7) before Task 21 can register UI-action tools through the catalog

---

## Quick commit reference

| Commit | Phase | Headline |
|---|---|---|
| `705005b` | 6.1 (T22) | Add Eto.Forms UI-tier dep to Host10x.csproj |
| `cb94a23` | 6.2 (T23) | Port TerminalPaneViewModel against 10.21.1 |
| `b459a51` | 6.2 (T23 fixup / T26) | Split PaneInteropHelpers into Interop/ per-file layout |
| `59c7da3` | 6.3 (T24) | Port TerminalPaneExtension against 10.21.1 |
| `480f941` | 6.4 (T25) | Port TerminalWebServer against 10.21.1 |
| `8b5cb5e` | 6.5 (T27) | Wire the real terminal menu, retire 'preview' placeholder |
| `16a5145` | 6.6 (T28) | Retire imported backport-10x/ reference — HEAD before handoff commit |

---

## TL;DR for the new session

1. **Read this handoff + the Phase 5 handoff.** Phase 6 added 7 commits (~1900 lines net, mostly the three big Host10x UI files: TerminalPaneExtension, TerminalPaneViewModel, StudioProUiAutomation).
2. **Resume on branch `feat/v5.0.0-w2-mcpx-merge` at `16a5145`** (or `+1` after the handoff commit) — clean, pushed, 257 tests green.
3. **Phase 6 is complete.** Host10x has feature parity with Host11x at the compile-time level. Runtime smoke test on Studio Pro 10.24.13 still requires Neo's hands.
4. **The big finding: zero Mendix API drift between 10.21.1 and 11.6.2.** Future ports should default to "works on both" — investigate divergence only if build fails.
5. **Recommended next chunk: Framing 3** — Task 29 (small) → Task 21 (mechanical once 29 lands) → Phase 8 (CHANGELOG + tag + PR for v5.0.0-alpha.2). Defer Tasks 30-32 to v5.0.0-alpha.3.
