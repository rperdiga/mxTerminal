# Handoff: after W2 Phase 5 (Tasks 18-20 — ToolCatalog operational, SPMCP reachable via concord-mcp) — 2026-05-12

> **For the next session:** Phase 5 of Plan W2 ([`docs/superpowers/plans/2026-05-12-concord-w2-mcpx-merge.md`](../plans/2026-05-12-concord-w2-mcpx-merge.md)) is **functionally complete**: the 87 SPMCP tools registered in `ToolCatalog` are now dispatched by `StudioProActionServer` (concord-mcp on port 7783). Task 21 (migrate UI-action + Maia tools into the catalog) is **deferred to Phase 6** — the plan template forward-references `StudioProActions(/* construct from HostServices, see Task 22 */)` but `IRunStateProbe` + `IStudioProUiAutomation` aren't on `HostServices` yet (Task 29 adds them). For now those 16 tools keep dispatching via the unchanged hardcoded switch in `StudioProActionServer`; the catalog-try path falls through cleanly when a name isn't in the catalog. Phase 2/3/4 handoffs remain valid for everything earlier.

---

## Quick orientation

- **Branch:** `feat/v5.0.0-w2-mcpx-merge` (pushed to origin)
- **HEAD (this writing):** `43f0e11` — `refactor(core): route MCP server dispatch through ToolCatalog (W2)`. After the handoff commit, HEAD will be `+1`.
- **Tests:** **257 passing** (241 Terminal.Tests + 16 Concord.Core.Tests), 3 skipped, 0 failed
- **Build:** **CLEAN** — `dotnet build Terminal.sln` succeeds with 0 errors (pre-existing nullable + CS0414 warnings only)
- **Working tree:** clean (after this handoff commit)

**Phase 5 functional success criteria — met:**
1. `ToolCatalog` + `ITool` + `ToolFamily` + `Studio11xAllowlist` exist in `src/Concord.Core/Mcp/`
2. `Host{N}xEntry.Catalog` (static) is populated at MEF activation with 87 SPMCP tools registered by `SpmcpToolBootstrap{N}x.Register(...)`
3. `ToolCatalogRegistry.Active` is set by Host*Entry; `StudioProActionServer` reads it for both `tools/list` and `tools/call`
4. The existing UI-action + Maia hardcoded switch in `StudioProActionServer` is preserved as a fallback (continues to work as before)

---

## Phase 5 commit chain (3 commits)

| Commit | Task | What |
|---|---|---|
| `cc2befd` | 18 | `feat(core): add ToolCatalog + ToolFamily + 11.x allowlist (W2)` — `ITool` interface, `ToolFamily` enum (12 values), `ToolCatalog` (TargetMode + family-toggle filtering), `Studio11xAllowlist` (curated 11.x surface, ~50 names). 4 new unit tests in `ToolCatalogTests.cs`. Added `FluentAssertions 6.*` to `Concord.Core.Tests.csproj` (existing tests use raw `Assert`; new tests use FA per the plan template). |
| `e7cb891` | 19 | `feat(host): wire SPMCP tools into ToolCatalog at MEF activation (W2)` — `SpmcpToolBootstrap11x.cs` + `SpmcpToolBootstrap10x.cs` (identical bodies, 114 lines each), each registering all **87 tools** (47 `MendixAdditionalTools` + 40 `MendixDomainModelTools`) by family. Two methods (`GetLastError`, `ListAvailableTools`) exist on both tool classes — disambiguated as `get_last_error_domain` / `list_available_tools_domain` on the `MendixDomainModelTools` side to avoid catalog name collision. `Host{N}xEntry` gets a `public static ToolCatalog? Catalog` property and calls the bootstrap after `HostServices.Register`. **Note:** `HostServices.Register` stays on the 4-arg overload — see "Things NOT to do" below. |
| `43f0e11` | 20 | `refactor(core): route MCP server dispatch through ToolCatalog (W2)` — `ToolCatalogRegistry.Active` static appended to `ToolCatalog.cs`. Both Host*Entry set it after `Catalog = catalog;`. `StudioProActionServer.HandleToolsCallAsync` tries the catalog FIRST (catches exceptions → `isError: true`), falls through to the existing hardcoded UI-action/Maia switch if the tool isn't in the catalog's visible set. `HandleToolsList` builds a name-keyed dictionary (catalog wins on collision), then unions hardcoded UI-action + Maia entries that don't collide, emits sorted `JsonArray`. Tool definitions from the catalog use a placeholder schema (`{type: "object", additionalProperties: true}`) — full per-tool schema reconstruction is a Phase 7+ follow-up (Claude Code / Codex accept open schemas). |

---

## Critical patterns established in Phase 5 (preserve them)

1. **Catalog-try with fallback is the bridge pattern.** Don't drop the hardcoded UI-action/Maia switch until those tools are actually registered in the catalog (Task 21 + Task 29). The fallback is what keeps existing tests + runtime behavior intact between catalog phases.

2. **Bootstrap classes are pure registration.** `SpmcpToolBootstrap{N}x.Register` instantiates the tool classes (which only need `ILogger`) and registers method delegates. **Construction does NOT hit `HostServices`** — that happens at invocation time. This is why Phase 5 can wire the catalog at MEF activation even though the 7 extended Interop hosts aren't fully wired yet.

3. **`ToolCatalogRegistry.Active` is the Core ↔ Host bridge.** `Concord.Core` can't reference `Concord.Host{N}x`, so the static registry is how `StudioProActionServer` (Core) reads the catalog populated by `Host{N}xEntry` (Host). Tests don't trigger MEF activation, so `Active` stays null in test scenarios — both paths (catalog-present and catalog-absent) are exercised by the test suite.

4. **Two tool classes have method-name collisions.** `MendixAdditionalTools.GetLastError` / `ListAvailableTools` and `MendixDomainModelTools.GetLastError` / `ListAvailableTools` exist on both classes. The catalog keys by snake_case Name with `OrdinalIgnoreCase`, so the second registration would overwrite the first. The bootstrap disambiguates with `_domain` suffix on the domain-model side. **Future tool additions must check for this collision** — pick unique snake_case names, or pick which class wins.

5. **`HostServices.Register` 4-arg vs 11-arg.** The current Host*Entry call is 4-arg (the 4 base hosts: App, RunConfigs, RunState, ModuleImport — all zero-arg stubs that throw `NotImplementedException`). The 7 extended hosts (Model, DomainModel, PageGeneration, Navigation, VersionControl, UntypedModel, MicroflowAuthoring) require real Mendix services in their ctors (`IModel`, `IPageGenerationService`, etc.) which only `TerminalPaneExtension` has MEF access to. Switching to the 11-arg overload from Host*Entry would require either adding MEF imports to Host*Entry (wrong shape — `IModel` is project-scoped, not stable) or refactoring the 7 hosts to defer service resolution. **Both are Phase 6 / B2 concerns** — don't attempt in Phase 5.

6. **Forward-references in the plan are real plan defects.** Task 21's template literally says `/* construct from HostServices, see Task 22 */` where Task 22 starts Phase 6. Future tasks that say "see Task N+1" should be carefully ordering-checked.

---

## Why Task 21 is deferred

Per the plan, Task 21 creates `UiActionsBootstrap` and `MaiaToolsBootstrap` to migrate the 16 UI-action + Maia tools into the catalog. The plan template:

```csharp
public static class UiActionsBootstrap
{
    public static void Register(ToolCatalog catalog)
    {
        var actions = new StudioProActions(/* construct from HostServices, see Task 22 */);
        // ...
    }
}
```

`StudioProActions`'s real constructor is `(IRunStateProbe probe, IStudioProUiAutomation ui, ...)` — those interfaces are NOT in `HostServices`. They're added in Phase 6 Task 29 ("Migrate StudioProActions to read run-state + UI automation from HostServices"). Until then, the only place a working `StudioProActions` exists is inside `TerminalPaneExtension.TryAutoStartActionServer()`, which is on the UI thread of the host process.

**Two ways to satisfy Task 21 in a future session:**
- **Path A (plan-aligned):** Do Phase 6 Task 29 first — extend `HostServices` with `IRunStateProbe` + `IStudioProUiAutomation`. Then Task 21 becomes mechanical. Recommended.
- **Path B (smaller scope):** Have `TerminalPaneExtension.TryAutoStartActionServer()` register the UI-action + Maia tools into the existing catalog right before starting the server. The bootstrap classes still exist in `Concord.Core` but are CALLED with `(catalog, actions, maia)` from the pane extension, not from `Host{N}xEntry`.

Path B is faster but creates two registration sites (Host*Entry for SPMCP, TerminalPaneExtension for UI/Maia). Path A is cleaner architecturally.

---

## What's next

Two valid framings for "what's the next chunk":

### Framing 1 — strict plan order (recommended)

Continue with the plan's **Phase 6** (Tasks 22-27, Host10x UI port). This is the substantive next chunk before v5.0.0-alpha.2 can ship. It includes:
- Task 22: Add Host10x project references for UI tier
- Task 23-26: Port `TerminalPaneViewModel`, `TerminalPaneExtension`, `TerminalWebServer`, `RunStateProbe`, `StudioProUiAutomation` to Host10x
- Task 27: Replace `ConcordMenuExtension` placeholder with the real `TerminalMenuExtension`

After Phase 6, **Task 29** unblocks the deferred Task 21 (UI-action + Maia tools into catalog).

### Framing 2 — handoff doc Phase 7-equivalent (faster path to alpha)

Skip the Host10x UI port (B2 is a separate concern from MCPX merge), and go straight to:
- Update CHANGELOG.md with v5.0.0-alpha.2 entry covering the SPMCP merge + catalog routing
- Update marketing docs if the alpha gets pre-release positioning
- Tag v5.0.0-alpha.2 and open PR to main

This is the user's prompt framing ("Phase 7 — Update marketing docs + CHANGELOG entry for v5.0.0-alpha.2"). It's valid if the alpha is explicitly scoped to "11.x SPMCP integration, 10.x still placeholder."

**Recommendation:** Framing 1 is closer to "ship a coherent alpha." Framing 2 ships sooner but with a known 10.x gap. The user's discretion.

---

## Things NOT to do (additions for Phase 6+)

- **Don't switch `HostServices.Register` from 4-arg to 11-arg in `Host{N}xEntry`** without first refactoring the 7 extended Interop hosts (`ModelHost11x.cs` etc.) to defer their service resolution (e.g., take `Func<IModel>` instead of `IModel`). Otherwise MEF activation will fail because Host*Entry doesn't have valid `IModel` at construction time. This is the trap the Task 19 implementer hit and correctly stopped on.
- **Don't try Task 21 without doing Task 29 first**, OR explicitly take Path B above (register UI/Maia from `TerminalPaneExtension.TryAutoStartActionServer()`). Mixing the approaches creates dead code.
- **Don't remove the hardcoded UI-action/Maia switch in `StudioProActionServer.cs`** until the catalog actually has those 16 tools registered. The Task 20 commit message explicitly notes the switch is the fallback "Task 21 migrates UI-action and Maia tools into the catalog and removes the fallback switch."
- **Don't add new SPMCP tool methods to `MendixAdditionalTools` or `MendixDomainModelTools` without updating the bootstrap registrations** in both `SpmcpToolBootstrap11x.cs` and `SpmcpToolBootstrap10x.cs`. The two files are identical by design; keep them in sync. Refactoring to a shared base class is a Phase 7+ concern.
- **Don't introduce per-tool JSON schemas in `tools/list` without a plan** for keeping them in sync with the C# method bodies. The current placeholder schema (`{type: "object", additionalProperties: true}`) is intentional and ships fine — Claude Code, Codex, Copilot CLI all accept open schemas. Full schemas are a polish/UX task, not a correctness one.
- **Don't merge to main yet** — Phase 5 is one milestone of the W2 plan. Phase 6 (Host10x UI port) and Phase 8 (versioning + smoke matrix) still gate the v5.0.0-alpha.2 tag.

---

## API gaps cataloged (carryover from Phase 4, still valid)

No new gaps surfaced in Phase 5. The Phase 4 catalog is still the authoritative list:
- `IModelHost`: `CreateConstant`, `UpdateConstant`, `DeleteModule`, `RuntimeSetting` After-Startup write
- `IDomainModelHost`: `DeleteConstant`, `DeleteEnumeration` (and verify `UpdateEnumeration` is fully exposed)
- `AttributeRef`: `MaxLength`, `EnumerationQualifiedName`, `DefaultValue`
- `ModuleId`: `FromAppStore`
- `IStudioProAppHost`: `SynchronizeWithFileSystem`
- `IUntypedModelHost`: `GetElementsOfType(unitQualifiedName, elementType)` (would unblock 5 security tools)
- Potential new interface: `IConsistencyHost` for `CheckProjectErrors`

Phase 5 added one **new** gap implied by the deferral of Task 21:
- `HostServices` needs `IRunStateProbe` + `IStudioProUiAutomation` accessors (Task 29 in Phase 6) before Task 21 can register UI-action tools through the catalog.

---

## Quick commit reference

| Commit | Phase | Headline |
|---|---|---|
| `cc2befd` | 5.1 | ToolCatalog + ToolFamily + 11.x allowlist + 4 unit tests |
| `e7cb891` | 5.2 | SpmcpToolBootstrap{11x,10x} + Host*Entry.Catalog static |
| `43f0e11` | 5.3 | StudioProActionServer dispatch via ToolCatalogRegistry.Active (fallback to hardcoded switch) — HEAD before handoff commit |

---

## TL;DR for the new session

1. **Read this handoff + the Phase 4 handoff.** Phase 5 added 3 commits (~660 lines net) wiring 87 SPMCP tools into a catalog that `concord-mcp` now dispatches through.
2. **Resume on branch `feat/v5.0.0-w2-mcpx-merge` at `43f0e11`** (or +1 after the handoff commit) — clean, pushed, 257 tests green.
3. **Phase 5 is functionally complete; Task 21 is deferred** to Phase 6 because of a plan ordering defect (Task 21's template forward-references Task 22+). Don't try to backfill Task 21 in isolation — do Phase 6 Task 29 first, OR take Path B (register UI/Maia from `TerminalPaneExtension.TryAutoStartActionServer()`).
4. **Next substantive chunk:** Phase 6 Tasks 22-27 (Host10x UI port). Or if shipping pre-Host10x, jump to Phase 7+ (CHANGELOG, alpha tag, PR).
