# Concord runtime shim — single .mxmodule, cross-version dispatch

> Status: design · 2026-05-15 · target release: v5.x architectural follow-up (post-v5.0.0)

## Context

Concord v5.0.0 (PR #20, merged 2026-05-14) introduced a two-extension architecture: `Concord.Host10x` deploys to `extensions/Concord10x/` and `Concord.Host11x` deploys to `extensions/Concord11x/`. Each host is built against a different `Mendix.StudioPro.ExtensionsAPI` version (10.21.1 vs 11.6.2). Studio Pro 10.x crashes if it sees `extensions/Concord11x/` in its project (type-resolution failure inside the loader's MEF discovery path — empirically verified by spike, commit `a0ce567`, 2026-05-12). Studio Pro 11.x has the mirror failure mode against `Concord10x/`. [`DEPLOYING.md:43`](../../../DEPLOYING.md#L43) documents this constraint to consumers verbatim: *"Never copy both folders into the same Mendix project. Studio Pro will crash trying to load the wrong-version DLL."*

A single Studio Pro add-on module package (`.mxmodule`) deploys its bundled resources into one and only one `extensions/<module-name>/` folder on install. So the current architecture cannot ship a single `.mxmodule` that works on both Studio Pro 10.24.13 and 11.10+. Joe's report (2026-05-15) confirms this in practice: a `.mxmodule` exported from Studio Pro after dropping `Concord.Host10x`'s bin output into the wrapper module's bundled resources only carries the 10.x DLLs. Installing that `.mxmodule` on Studio Pro 11.10 produces a project with the 10.x host present — and the 10.x host fails type resolution against 11.x's ExtensionsAPI.

## Goal

A single `.mxmodule` that, when installed on **either** Studio Pro 10.24.13 or 11.10+, produces a working Concord extension. The packaging is version-uniform; the dispatch to the version-appropriate host happens **at runtime**, inside the extension itself, after Studio Pro has loaded it.

Joe's directive verbatim:

> The ideal scenario would be for the Extension to have full functionality for the 2 versions in its packaging. But the package version that would be used would be decided by the Studio Version where the Extension is hosted.

### Why not "two .mxmodule files" instead

The two-mxmodule path (one per Studio Pro major version) is the lowest-effort answer and is the fallback if this design fails its spike. It is **not** the goal because:

1. **Marketplace UX cost.** Mendix Marketplace listings expose one primary download per version of a listing. Two parallel artifacts means either two listings (which fragments installed-user upgrade paths — and the marketplace `Component Type` is immutable post-publish per [`CLAUDE.md`](../../../CLAUDE.md)) or a single listing where users have to read the description to pick the right download. Both are friction Joe specifically wants to avoid.
2. **Future-proofing.** Studio Pro 12.x is coming. A runtime-shim architecture absorbs that transition behind the same `.mxmodule`; a per-major-version `.mxmodule` strategy multiplies artifacts each release.
3. **Single source-of-truth for the consumer.** [`DEPLOYING.md`](../../../DEPLOYING.md) currently has a "which folder do I need?" matrix that a single-artifact shim retires entirely.

## Non-goals

- **No changes to `Concord.Core`'s public API surface.** Core stays version-blind; the shim is the version-aware layer.
- **No changes to the host implementations' business logic.** `Concord.Host10x` and `Concord.Host11x` keep doing what they do today; only their packaging location and load mechanism change.
- **No support for hot-swapping hosts at runtime.** The shim binds a host on first load and stays bound for the process lifetime.
- **No support for running both hosts simultaneously.** Studio Pro is single-version per process — there's no need.

## Approach — Concord.Shim with AssemblyLoadContext isolation

A new project `src/Concord.Shim/Concord.Shim.csproj` produces `Concord.Shim.dll`. The deployed `extensions/Concord/` folder layout becomes:

```
extensions/Concord/
   manifest.json              { "mx_extensions": ["Concord.Shim.dll"] }
   Concord.Shim.dll           ← only DLL Studio Pro MEF-discovers
   wwwroot/                   ← shared web assets (HTML/JS/CSS)
   skills/    skills-10x/     skills-mac/
   rules/     rules-10x/
   bin-10x/
      Concord.Host10x.dll
      Concord.Core.dll
      Mendix.StudioPro.ExtensionsAPI.dll (10.21.1)
      … 10.x-specific dependencies
   bin-11x/
      Concord.Host11x.dll
      Concord.Core.dll
      Mendix.StudioPro.ExtensionsAPI.dll (11.6.2)
      … 11.x-specific dependencies
```

### How the shim works at runtime

1. Studio Pro's extension loader reads `manifest.json` and MEF-discovers exports in `Concord.Shim.dll` only. The shim is built against ExtensionsAPI **10.21.1** (the lower of the two versions) so its export contracts are satisfiable by the higher version too (forward-compatible — 11.x's ExtensionsAPI extends 10.x without breaking the types Concord uses).
2. The shim's `[Export(typeof(DockablePaneExtension))]` and analogous menu/web-server exports each:
   1. On first instantiation, call `RuntimeHostLocator.Resolve()` to get the matching host folder (`bin-10x/` or `bin-11x/`), using `StudioProThemeProbe.StudioProVersionFromExePath()` (already in [`src/Concord.Core/`](../../../src/Concord.Core/)) for the version probe.
   2. Build a single process-wide `ConcordHostLoadContext` (a subclass of `AssemblyLoadContext`) pointed at the chosen folder. The context's `Resolving` event redirects `Mendix.StudioPro.ExtensionsAPI` and all `System.*` to the **default** load context (so the host binds against Studio Pro's already-loaded API copy); everything else (`Concord.Host{Nx}`, `Concord.Core`, `Eto.Forms`, SQLite, etc.) resolves from the chosen `bin-{Nx}/` folder.
   3. Reflectively instantiate the host's `TerminalPaneExtension` (or `ConcordMenuExtension` etc.) by name, cast to the shared base type (`DockablePaneExtension`), and forward all calls to it.
3. Subsequent shim exports reuse the same `ConcordHostLoadContext` — so all MEF entry points share state through the same loaded host.

### Why AssemblyLoadContext rather than AppDomain

.NET 8 does not support multiple AppDomains. `AssemblyLoadContext` is the supported isolation primitive — it allows the host's `Concord.Core.dll` (which carries the host's bindings to its ExtensionsAPI version) to be loaded in a context separate from any DLLs already loaded in Studio Pro's default context. Shared dependencies (Studio Pro's ExtensionsAPI, `System.*`) are explicitly resolved up to the default context via the `Resolving` event hook, so type identity is preserved across the boundary for the API types Studio Pro hands to our exports.

### Folder layout invariants the shim must preserve

- The shim never copies host DLLs out of `bin-{Nx}/`. Studio Pro's cache-snapshot mechanism (per [`docs/superpowers/handoffs/2026-05-12-after-w1.md`](../handoffs/2026-05-12-after-w1.md), §"Build cache + Studio Pro cache") still snapshots `extensions/Concord/` into `<project>/.mendix-cache/extensions-cache/<guid>/` on first load; the shim resolves paths off `AppDomain.CurrentDomain.BaseDirectory` so it works against the snapshot.
- `Concord.Core.dll` exists in **two** places: once at the top level of `extensions/Concord/` (loaded into the default context, used by the shim itself) and once inside each `bin-{Nx}/` folder (loaded into the `ConcordHostLoadContext` alongside the host). The two copies must be the **same build** of `Concord.Core` — built once, copied to both locations during the deploy step. Drift between them would cause `HostContext.Initialize` / `HostServices.Register` to operate on two disjoint static states.

## Empirical questions that must be answered before implementation

The design rests on three load-bearing assumptions. Phase 0 (the spike, separate plan document) answers each empirically against real Studio Pro installations before any production code is written.

| # | Question | Why it matters | Fallback if wrong |
|---|---|---|---|
| **Q1** | Does Studio Pro's MEF discovery scan only DLLs listed in `manifest.json`, or does it recursively scan subdirectories of `extensions/Concord/`? | If recursive: `bin-{Nx}/` is in scope; the wrong-version host's MEF discovery against the mismatched ExtensionsAPI will crash Studio Pro, same failure mode as the 2026-05-12 spike with sibling top-level folders. | Shadow-copy host DLLs to `%TEMP%\Concord\<version>\<hash>\` at first run; load from there. First-run extract step, cache invalidation on upgrade, signature verification on the extracted bits. Adds ~150 lines of shim code and one new failure mode. |
| **Q2** | Will `AssemblyLoadContext.Resolving` correctly redirect `Mendix.StudioPro.ExtensionsAPI` to Studio Pro's already-loaded copy (default context), so type identity is preserved across the boundary? | If type identity diverges: the host's `DockablePaneExtension` subclass is a different CLR type from the one Studio Pro expects — MEF discovery fails on the shim side, or the cast fails when forwarding calls. | (a) `Assembly.LoadFile()` into the default context (no isolation) — works for the shared APIs but lets the host's `Concord.Core.dll` overwrite the shim's. (b) AppDomain isolation via .NET Framework compat shim — heavy. (c) Abandon shim, fall back to two-mxmodule path. |
| **Q3** | Is the `MenuExtension` (10.x: abstract class) vs `IMenuExtension` (11.x: interface) drift addressable from a single shim DLL, or does the menu entry stay version-specific? | If unaddressable: shim is incomplete; either ship a version-specific shim per host (defeating the unification), or accept the menu entry is broken on one version. | The menu surface is small (one entry — "Open Concord"). Worst case: ship the shim's `[Export]` matching the 10.x type only; on 11.x, the menu entry is registered via fallback code path in the loaded host. Document the asymmetry. |

These questions are not theoretical — the 2026-05-12 spike's surprise finding (Studio Pro 10.x crashes on sibling wrong-version folders, contradicting the earlier hope that "MEF skips" type-resolution failures gracefully) means we owe ourselves empirical evidence before designing on top of any assumption.

## Scope of changes (assuming spike confirms feasibility)

### 1. New project — `src/Concord.Shim/`

```
src/Concord.Shim/
   Concord.Shim.csproj         (targets net8.0, PackageReference Mendix.StudioPro.ExtensionsAPI 10.21.1)
   manifest.json               (Content Include — { "mx_extensions": ["Concord.Shim.dll"] })
   ShimEntry.cs                (MEF activation sentinel)
   RuntimeHostLocator.cs       (version probe + path resolution to bin-10x/ or bin-11x/)
   ConcordHostLoadContext.cs   (AssemblyLoadContext subclass with Resolving event)
   Pane/TerminalPaneExtensionShim.cs        ([Export(typeof(DockablePaneExtension))] forwarder)
   Menu/ConcordMenuExtensionShim.cs         ([Export(typeof(MenuExtension))] forwarder — pending Q3)
   WebServer/TerminalWebServerExtensionShim.cs  ([Export(typeof(IWebServerExtension))] forwarder)
```

Only API surface the shim references must be present and signature-stable across 10.21.1 and 11.6.2. Spike output includes a compat matrix per used type.

### 2. New MSBuild output target — `bin/x64/Debug/net8.0-merged/`

A new MSBuild target `MergeHostsForShim` runs after both `Concord.Host10x` and `Concord.Host11x` build:

1. Creates `bin/x64/Debug/net8.0-merged/`
2. Copies `Concord.Shim/bin/.../*` to the top of `net8.0-merged/`
3. Creates `net8.0-merged/bin-10x/` and copies `Concord.Host10x/bin/.../*` into it
4. Creates `net8.0-merged/bin-11x/` and copies `Concord.Host11x/bin/.../*` into it
5. Hoists shared content (`wwwroot/`, `skills/`, `skills-10x/`, `skills-mac/`, `rules/`, `rules-10x/`) to the top level of `net8.0-merged/` and deletes the duplicates inside `bin-{10,11}x/`

This merged folder is what gets dropped into the `ConcordPublisher` wrapper module's bundled resources for `.mxmodule` export.

### 3. Updated dev-deploy workflow

`MendixDeployTarget` (per-host vs merged) gets a new option:

- `MendixDeployTargetShim` → deploys the merged layout to `<target>/extensions/Concord/`
- Per-host targets (`MendixDeployTarget10x`, `MendixDeployTarget11x`) remain for component-level iteration — building only the shim or only one host.

### 4. Retire the two-extension folder layout (gradually)

After the shim ships and validates:

- `DEPLOYING.md` "Which folder do I need?" matrix replaced by a single "drop `extensions/Concord/` into your project".
- `marketing/` (4 surfaces — MD + HTML) updated to drop the version-fork messaging.
- Old `extensions/Concord10x/` and `extensions/Concord11x/` folders on consumer projects get migration guidance: delete the old folder, install the new `.mxmodule`.

The two host projects stay in the source tree — they're still where the version-specific code lives. Only the deployed surface unifies.

## Tests

### Unit (shim-level)

- **`RuntimeHostLocatorTests`**: given a synthetic Studio Pro version string, asserts the correct `bin-{Nx}/` is selected. Covers boundary cases (`11.10.0` → 11x, `11.9.x` → 10x, `10.24.13` → 10x, unknown → 11x with warning).
- **`ConcordHostLoadContextTests`**: given a temp folder layout with a fake host DLL, asserts the context's `Resolving` event redirects `Mendix.StudioPro.ExtensionsAPI` to the default context and resolves all other deps from the chosen folder. Uses a fake ExtensionsAPI-shaped assembly with a known marker type.

### Integration (cross-version, manual)

- **Smoke matrix**: build merged `.mxmodule`. Install on (a) Studio Pro 10.24.13 clean project, (b) Studio Pro 11.10 clean project. For each: assert pane opens, action server starts, one tool call (e.g., `save_all`) round-trips successfully.
- **Cache-refresh**: confirm Studio Pro's `.mendix-cache/extensions-cache/<guid>/` snapshot of the merged layout works the same as the current single-host snapshot.

### Negative

- **`extensions/Concord/` with the wrong-version `bin-{Nx}/` removed**: shim falls back to a clear error message (logged + status pill in pane stub), does not crash Studio Pro. Tested by deleting `bin-11x/` from a 11.x install and confirming graceful failure.

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Spike Q1 turns out: MEF discovery scans subdirectories recursively | Med | Forces shadow-copy fallback; +1–2 days, more complex shim | Phase 0 spike answers this before any code; if positive, falls back to documented secondary design |
| Spike Q2 turns out: AssemblyLoadContext can't cleanly share `Mendix.StudioPro.ExtensionsAPI` | Med | No isolation possible; host DLLs would conflict | Falls back to two-mxmodule path — same path as if we'd never tried |
| `Concord.Core.dll` divergence between top-level and `bin-{Nx}/` copies | Low | HostServices state split between two contexts; hard-to-debug runtime confusion | Build script asserts file hashes match before deploy; CI gate |
| Type drift in ExtensionsAPI 10.21.1 → 11.6.2 for a type Concord uses that wasn't enumerated in spike | Low (after spike) | Cast fails at runtime when forwarding to host | Spike's output includes complete compat matrix; CI test that loads both ExtensionsAPI versions and asserts the matrix is exhaustive |
| Studio Pro 12 introduces an ExtensionsAPI 12.x with a breaking change to a used type | Med (mid-term) | Shim's lower-version baseline (10.21.1) no longer forward-compatible to 12.x | Rev the shim's baseline when 12.x ships; treat as a normal API-rev cycle, not a re-architecture |
| Mendix changes the `.mxmodule` packaging mechanism to support version-conditional resources natively | Low | This whole design becomes unnecessary | Watch [Mendix release notes](https://docs.mendix.com/releasenotes/) for `IExtensionAPI` changes; the shim still works in that scenario, just becomes redundant |

## Open questions for the implementation plan

1. **Logging location for the shim.** The host has a `Logger` wired through `HostContext`; the shim runs before `HostContext.Initialize`. First-run shim errors need a known log path. Proposal: `%TEMP%\Concord\shim.log` with rolling truncation at 1 MB.
2. **Probe accuracy.** `StudioProThemeProbe.StudioProVersionFromExePath()` reads the running Studio Pro's `.exe` version. Verify it works under both Studio Pro 10.x and 11.x without modification; if not, generalize.
3. **`Eto.Forms` version coexistence.** Both hosts reference `Eto.Forms 2.9.*`. If 2.9.x is binary-compatible across the host versions, drop it from `bin-{Nx}/` and ship one copy at top level. Verify in spike.
4. **Performance.** Reflective instantiation + load-context boundary cross adds startup latency. Benchmark before/after pane open during spike; flag if it exceeds 500 ms regression.

## Sub-spec / sub-plan map

- **Phase 0** — Spike that answers Q1–Q3. Spec: this document. Plan: [`docs/superpowers/plans/2026-05-15-concord-runtime-shim-spike.md`](../plans/2026-05-15-concord-runtime-shim-spike.md). Output: a `2026-05-XX-concord-shim-spike-findings.md` handoff that either confirms or invalidates the design above.
- **Phase 1+** — Implementation plan, written **after** Phase 0 completes. Each phase atomic, one commit per phase, single PR at the end per Joe's minimal-ceremony preference (consistent with the v5.0.0 skills-rules split pattern).
