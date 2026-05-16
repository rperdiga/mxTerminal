---
name: concord-shim-mac-loadcontext-fix
description: "Mac smoke validation of the Concord.Shim load-context fix (drop AssemblyDependencyResolver; add LoadUnmanagedDll override). Both Studio Pro 10.24.13 and 11.10 on macOS now load the merged-shim Concord extension cleanly — same .mxmodule layout that was crashing with `Hostpolicy must be initialized` errors before the fix. Windows regression smokes still TODO."
metadata:
  node_type: memory
  type: project
  originSessionId: 2026-05-15-concord-shim-mac-loadcontext-fix
  status: MAC-COMPLETE-WINDOWS-PENDING
---

# Concord runtime-shim — Mac load-context fix (handoff)

> **Status:** Mac validation COMPLETE. Windows regression smokes + canonical `.mxmodule` rebuild pending.

## TL;DR

- **Bug:** `Concord.Shim/5.1.0-alpha.1`'s `ConcordHostLoadContext` constructed an `AssemblyDependencyResolver` eagerly in its ctor. The resolver's native `corehost_resolve_component_dependencies` call has a precondition (`hostpolicy.fxr_path` set via `corehost_main`) that Studio Pro on macOS doesn't satisfy. Result: `InvalidArgFailure -2147450750` thrown from `TerminalWebServerShim`'s cctor → MEF composition exception → extension fails to load. Phase 5's smoke was Windows-only and didn't surface this.
- **Fix:** Drop `AssemblyDependencyResolver` entirely from `ConcordHostLoadContext` (it was only priority-4 fallback for `runtimes/<rid>/lib/<tfm>/` RID-specific managed DLLs — a layout Concord doesn't use). Add `LoadUnmanagedDll(string)` override that probes `runtimes/<rid>/native/` with RID fallback (`osx-arm64` → `osx` → `unix` → `any`, mirror chains on other platforms) for native binaries like SQLite's `libe_sqlite3.dylib`. ~140 lines net change to `src/Concord.Shim/ConcordHostLoadContext.cs`; 4 new tests in `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`. All existing 27 shim tests still pass.
- **Mac SP 11.10:** PASS. Pane opens, terminal renders, tool call round-trips. Shim log: `HostKickstart.InstantiateEntry took 17ms`, `PaneShim.CreateInner took 1ms`, multiple `LoadUnmanagedDll fired for libSystem.dylib` (override exercised, defers cleanly to OS default).
- **Mac SP 10.24.13:** PASS. Pane opens, terminal renders. (Required wrapper `.app` at `/Applications/Studio Pro 10.24.13 (Ext Dev).app` to launch SP with `--enable-extension-development` since the Mac variant lacks the Preferences toggle for this flag.)
- **Windows SP 11.10 + 10.24.13:** TODO. Same code; regression smokes to confirm no Windows breakage from the resolver removal.
- **`.mxmodule` packaging:** TODO. Mac SP 10.24.13 lacks an export-module UI, so the canonical `.mxmodule` build needs to happen from the `ConcordPublisher` wrapper on Windows. Then re-import on Mac (both SP versions) to validate the marketplace-install path end-to-end.

## Root cause (excerpt — full chain in spec)

```text
TerminalWebServerShim..cctor()              // MEF activates first shim export
  → HostKickstart.EnsureLoaded()
    → new ConcordHostLoadContext(hostFolder)
      → new AssemblyDependencyResolver(likelyHostDll)  // throws here
        → corehost_resolve_component_dependencies()    // returns InvalidArgFailure -2147450750
        → "Hostpolicy must be initialized and corehost_main must have been called"
```

Windows works because Studio Pro's launcher is `apphost`-style — goes through `corehost_main`, sets `fxr_path`. Studio Pro on Mac launches via an embedded hosting path that doesn't satisfy the precondition.

## What was changed

| File | Change |
|---|---|
| `src/Concord.Shim/ConcordHostLoadContext.cs` | Remove `_resolver` field + its construction (was lines 39, 45–56). Remove priority-4 fallback in `Resolve()` (was lines 134–141). Add `LoadUnmanagedDll(string)` override + `TryResolveNativePath`, `NativeProbePaths`, `RidFallbackChain` helpers (`internal` for testability). Added trailing-comment paragraph explaining the absence as load-bearing documentation. |
| `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs` | +4 tests: `ConcordHostLoadContext_HasNoAssemblyDependencyResolverField` (regression prevention via reflection), `TryResolveNativePath_FindsFileInMatchingRidNativeFolder`, `TryResolveNativePath_ReturnsFalse_WhenNoNativeFolderExists`, `TryResolveNativePath_FlatHostFolderFallback_WhenNoRuntimesFolder`. |

Nothing else touched. `RuntimeHostLocator` was already correctly using `Assembly.Location` (verified during pre-execution audit). The build pipeline (`MergeHostsForShim.targets`, `DeployMergedToMendix.targets`) and host projects (`Concord.Host10x`, `Concord.Host11x`) are unchanged.

## Side issue discovered (not a code change — operational)

While smoke-testing on Mac, the GraphViewer test project surfaced a **dual-load conflict**: the project had Concord imported as a Mendix module (`/Users/.../GraphViewer/modules/Concord.mxmodule` — a stale 4.2.2-era export). Studio Pro extracts that `.mxmodule`'s bundled `extensions/Concord/` into `.mendix-cache/modules/concord.mxmodule/extensions/Concord/` and treats it as a separate MEF extension alongside the dev-deployed `extensions/Concord/`. The old `.mxmodule`'s shim still had `AssemblyDependencyResolver`; the dev shim didn't.

Result: every project open created two cache directories — one with the new shim (load succeeded) and one with the old (load failed). The composition exception you saw was always from the old `.mxmodule`'s shim, NOT the dev deploy. Resolution: delete the imported Concord module from the project (right-click → Delete Module in Studio Pro's project explorer). The fresh `.mxmodule` (once rebuilt from Windows wrapper) won't have this conflict because its shim will be the fixed version.

This is worth a one-line note in `DEPLOYING.md` for upgraders coming from a marketplace install — "delete the prior Concord module before installing v5.1.x, then re-import the new `.mxmodule`" — but is otherwise an artifact of installing two versions on the same project.

## Mac smoke evidence

- Shim log clean run (SP 11.10, GraphViewer, 2026-05-16T02:53:02): cache `94769e7e-9dc7-4fde-86f9-f017ed6d7248`, host instantiate 17ms, pane create 1ms, no errors. Log lives at `$TMPDIR/Concord/shim.log` (Mac equivalent of Windows's `%TEMP%\Concord\shim.log`).
- SP 10.24.13 (Test_10_24_13): clean install — empty project, no pre-existing Concord, no conflict pattern. Tool calls round-tripped.
- Test suite: 31/31 in `Concord.Shim.Tests`; 56/56 in `Concord.Core.Tests`; 274/277 in `Terminal.Tests` (3 skipped — Maia live tests, expected). Total 361 passing on Mac.

## Open items for Windows pickup

1. **Regression smokes:** pull `fix/concord-shim-mac-loadcontext`, build, deploy via `MendixDeployTarget10x`/`MendixDeployTarget11x` (Windows uses the per-host layout for dev iteration), confirm SP 10.24.13 + 11.10 still work. The resolver removal is the only meaningful behavior change; if anything broke on Windows it'll surface here.
2. **`.mxmodule` rebuild:** run the `ConcordPublisher` wrapper-module export per `reference_concord_mxmodule_build.md`. Decide release version (the spec said `5.0.3` but the current shim is `5.1.0-alpha.1` — likely target is `5.1.0` final or `5.1.0-alpha.2`, Joe's call).
3. **Round-trip:** install the new `.mxmodule` on a fresh Mac project (both SP versions). Confirm the marketplace-install path is clean — this is the path that originally surfaced the bug, so it's the strongest end-to-end validation.
4. **`DEPLOYING.md` upgrader note:** see "side issue" section above. One-line addition.
5. **PR description:** spec at `docs/superpowers/specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md`, plan at `docs/superpowers/plans/2026-05-15-concord-shim-mac-loadcontext-fix.md`. Plan was revised post-source-audit; the revision commit (`0095ac3`) documents what changed and why.

## See also

- Spec: [docs/superpowers/specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md](../specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md)
- Plan: [docs/superpowers/plans/2026-05-15-concord-shim-mac-loadcontext-fix.md](../plans/2026-05-15-concord-shim-mac-loadcontext-fix.md)
- Phase 0 spike findings (Q1/Q2/Q3 POSITIVE — Windows only): [docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md](2026-05-15-concord-shim-spike-findings.md)
- Phase 5 smoke results (Windows, pre-Mac-fix): [docs/superpowers/handoffs/2026-05-15-concord-shim-smoke-results.md](2026-05-15-concord-shim-smoke-results.md)
- Maia-on-Mac feasibility (related Mac context, out-of-scope for this fix): [docs/MAIA_MAC_FEASIBILITY.md](../../MAIA_MAC_FEASIBILITY.md)
