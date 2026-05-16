---
name: concord-shim-smoke-results
description: "Phase 5 manual smoke validation of the runtime-shim implementation on Studio Pro 10.24.13 + 11.10. Empirical PASS on both versions — single .mxmodule installs on both, pane opens, terminal renders, MCP tools respond. Sum of 5 Timed steps fits comfortably under the 500ms perf gate. Surfaced 6 production-only bugs the Phase 0 spike's FakeHost stub didn't exercise; all fixed in-branch with regression tests."
metadata:
  node_type: memory
  type: project
  originSessionId: 2026-05-15-runtime-shim-impl-execution
  status: COMPLETE
---

# Concord runtime-shim smoke results (Phase 5)

> **Status:** COMPLETE — both SP versions PASS. Six bugs found during smoke; all fixed in branch. Ready for Phase 6 + Phase 7.

## TL;DR

- **SP 10.24.13 (Blank_10_24_13):** PASS. Pane opens, terminal renders, MCP tools work. Pane-open: 350ms cold-install, 55ms warm-reopen.
- **SP 11.10 (Blank_11_10):** PASS. Pane opens, terminal renders, MCP tools work. Pane-open: 77ms cold-install, 122ms warm-reopen.
- **Perf gate (OQ1 — ≤500 ms shim overhead):** PASS on both. Worst case (350ms cold on SP 10.24.13) leaves ~30% headroom.
- **Cross-version single-artifact validated:** one `.mxmodule` (exported from the SP 10.24.13 publisher project — must be the lowest supported SP version) installs cleanly on BOTH SP versions. This is the runtime-shim's core thesis, empirically proved end-to-end.
- **Recommendation:** PROCEED to Phase 6 (docs + marketing updates).

---

## Methodology

**Perf metric**: sum of the 5 `ShimLog.Timed` measurements in `HostKickstart` + `TerminalPaneExtensionShim`, captured to `%TEMP%\Concord\shim.log`. These five steps are the *only* work added by the v5.1.0 shim layer; everything else inside `PaneShim.CreateInner` is the same code that ran in v5.0.0 directly. Therefore **sum(Timed-1..5) ≈ the regression vs v5.0.0**.

This methodology was chosen over capturing a separate v5.0.0 baseline because:
1. The v5.0.0 baseline is not currently captured anywhere in `_HANDOFF.md`, CHANGELOG, or memory — would have required ~30 min of stash/checkout/rebuild/smoke twice.
2. The 5 Timed instrumentation points are precisely the shim's added work; everything downstream is shared with v5.0.0.
3. Empirical confirmation via worst-case (350ms) being under the gate (500ms) — comfortable margin even without a separate baseline.

**Publisher**: `.mxmodule` exported from `C:\Projects\Test_10_24_13` opened in **SP 10.24.13** (must be the lowest supported SP version; Mendix marketplace rejects `.mxmodule` files exported from a NEWER SP project version than the consumer's). One artifact installs on both 10.24.13 and 11.10 consumers.

**Consumer test projects**: `C:\Projects\Blank_10_24_13\Blank_10_24_13.mpr` and `C:\Projects\Blank_11_10\Blank_11_10.mpr` — pristine throwaway projects with no prior Concord state. Each was `Remove-Item -Recurse -Force "extensions"` + `Remove-Item -Recurse -Force ".mendix-cache\extensions-cache"` + `Remove-Item -Force "$env:TEMP\Concord\shim.log"` before install.

---

## SP 11.10 evidence

### Shim log (Blank_11_10 cold pane-open at 22:24:33Z)

```
HostKickstart.ResolveHostFolder took 14ms
HostKickstart: SP version='11.10.0', hostFolder=...\bin-11x
HostKickstart.BuildLoadContext took 6ms
HostKickstart.LoadHostAssembly took 3ms
Resolve fired for Concord.Core
Resolved Concord.Core from ...bin-11x\Concord.Core.dll into ConcordHost@...
Resolve fired for Mendix.StudioPro.ExtensionsAPI (v11.6.2)         # deferred to default ✓
Resolve fired for Microsoft.Extensions.Logging.Abstractions (v8.0)
Resolved Microsoft.Extensions.Logging.Abstractions from ...bin-11x\... # loaded locally ✓
HostKickstart.InstantiateEntry took 52ms
PaneShim.CreateInner took 2ms
Resolve fired for Eto (v2.8.0.0)                                    # deferred to default ✓
```

No `ERROR` entries.

### Pane-open latency table

| Run | ResolveHostFolder | BuildLoadContext | LoadHostAssembly | InstantiateEntry | PaneShim.CreateInner | **Total** |
|---|---|---|---|---|---|---|
| Cold (1st install) | 14ms | 6ms | 3ms | 52ms | 2ms | **77ms** |
| Warm (close → reopen) | 18ms | 11ms | 5ms | 82ms | 6ms | **122ms** |

### Functional confirmation

- Pane opens; xterm.js renders.
- Shells dropdown populated; settings panel populated.
- Terminal accepts input and shows output (Ricardo confirmed manually).
- MCP tool round-trip succeeds (Ricardo confirmed via Claude Code / inside-pane tool invocation; HTTP `curl` to `127.0.0.1:7783` not tested — SP version may expose the endpoint at a different path).

### Cache snapshot count

Exactly **1** snapshot under `Blank_11_10\.mendix-cache\extensions-cache\` — `extensions/Concord/` only, no separate `Concord10x`/`Concord11x` snapshots. The runtime-shim's single-folder deployment is what consumers see. ✓

---

## SP 10.24.13 evidence

### Shim log (Blank_10_24_13 cold pane-open at later timestamp)

```
HostKickstart.ResolveHostFolder took 53ms
HostKickstart: SP version='10.24.13', hostFolder=...\bin-10x          # correctly routed to 10x host ✓
HostKickstart.BuildLoadContext took 23ms
HostKickstart.LoadHostAssembly took 13ms
Resolved Concord.Core from ...bin-10x\Concord.Core.dll
Resolved Microsoft.Extensions.Logging.Abstractions from ...bin-10x\...
HostKickstart.InstantiateEntry took 216ms
PaneShim.CreateInner took 45ms
```

No `ERROR` entries.

### Pane-open latency table

| Run | ResolveHostFolder | BuildLoadContext | LoadHostAssembly | InstantiateEntry | PaneShim.CreateInner | **Total** |
|---|---|---|---|---|---|---|
| Cold (1st install) | 53ms | 23ms | 13ms | 216ms | 45ms | **350ms** |
| Warm (close → reopen) | 11ms | 4ms | 3ms | 33ms | 4ms | **55ms** |

Cold-start is slower on SP 10.24.13 than on 11.10 — likely first-install JIT-compile + cache-snapshot creation overhead. Warm reopens converge to ~55ms (faster than 11.10's warm 122ms; both well under the gate either way).

### Functional confirmation

- Pane opens; xterm.js renders.
- Shells populated; settings populated.
- Terminal accepts input and shows output.
- MCP tools work (Ricardo confirmed).

### Cache snapshot count

Exactly **1** snapshot under `Blank_10_24_13\.mendix-cache\extensions-cache\`. ✓

---

## Outcomes against the 5 open questions

| OQ | Question | Outcome |
|---|---|---|
| **OQ1** | Performance ≤500ms regression | **PASS**. Worst-case 350ms on SP 10.24.13 cold install; 55–122ms on subsequent opens. |
| **OQ2** | SP 12.x compat | **Deferred** — re-verify post-release when SP 12 ships. Tracking item to seed in next-cycle `_HANDOFF.md`. |
| **OQ3** | Mac variant | **Deferred** — Ricardo's fast-follow post-merge. The shim mechanism is OS-agnostic; the Eto v2.8 pin should apply identically on Mac. |
| **OQ4** | Host MEF imports succeed through the shim | **PASS** — but only after 6 production-only fixes (see "Bugs found" below). The Q2 spike's FakeHost stub was too minimal to exercise these surfaces; production-host's real dependencies hit each one. |
| **OQ5** | `Concord.Core.dll` hash gate prevents drift | **PASS** — hash gate fired correctly during Phase 4 deliberate-divergence smoke. SHA-256 verification logged on every build. No drift in the final merged layout (`SHA256=F9CEA25BB9241D3364789697C99AED494EA3E9C169AC5E1DCDAF691CE6C0E16D`). |

---

## Bugs found during smoke

The Phase 0 spike's `FakeHost` was a minimal `DockablePaneExtension` with no production dependencies. The real Concord host references many transitive packages and accesses SP-managed lifecycle state — each surface that differed from FakeHost hit a sharp edge in the shim architecture. Six distinct bugs surfaced during smoke; all fixed in-branch with regression tests.

| # | Symptom | Root cause | Fix |
|---|---|---|---|
| 1 | `CompositionException`: `IExtensionFileService` import not satisfied | Shim's `Mendix.StudioPro.ExtensionsAPI.dll v10.21.1` shipped at `extensions/Concord/` root caused .NET Core `LoadFrom` to load a SECOND ExtensionsAPI assembly alongside SP's loaded v11.6.2 — MEF saw two distinct CLR types for `IExtensionFileService` and refused the match | `<ExcludeAssets>runtime</ExcludeAssets>` on the shim's ExtensionsAPI `PackageReference` + build-time `<Error>` gate in `MergeHostsForShim.targets` to prevent regression |
| 2 | `FileNotFoundException: Microsoft.Extensions.Logging.Abstractions` thrown from inside Host11xEntry's ctor | `ConcordHostLoadContext.IsSharedAssembly` over-shared anything starting with `"Microsoft."` — but SP didn't have `Microsoft.Extensions.Logging.Abstractions` loaded in default ALC, and the deferral threw | Refactored `OnResolving` to use a dynamic `AssemblyLoadContext.Default.Assemblies` check instead of a hardcoded prefix list (catches ExtensionsAPI, Eto, BCL, and any future SP-loaded assembly), with explicit `Concord.*` exemption to preserve the intentional two-copy state-isolation between shim and host |
| 3 | `InvalidOperationException: WebServerBaseUrl is only available after the extension has been fully constructed` | SP populates `UIExtensionBase.WebServerBaseUrl` only on instances it MEF-constructs. The shim's inner `TerminalPaneExtension` is `Activator.CreateInstance`'d, so its `WebServerBaseUrl` and `CurrentApp` are never populated | Added `__ConcordShim_SetUIContext(Func<IModel?>, Func<Uri>)` seam to inner host. The shim shadows `CurrentApp` and `WebServerBaseUrl` with `new` properties backed by these getters, called from `TerminalPaneExtensionShim.Open()` before delegating to `inner.Open()`. All 17 in-class `CurrentApp` / `WebServerBaseUrl` references resolve to the shadows at compile time — zero call-site changes in the inner host |
| 4 | `KeyNotFoundException` from `ExtensionFileService.GetExtensionInfo` when host called `ResolvePath("skills")` | SP's `ExtensionFileService.ResolvePath` uses `Assembly.GetCallingAssembly()` to look up the extension's deploy folder. Only `Concord.Shim` is registered in SP's dictionary; the inner host (`Concord.Host11x`) isn't | New `ShimExtensionFileService` wrapper in the shim assembly. Wraps SP's service and re-dispatches `ResolvePath` from the shim's assembly, so the calling assembly SP observes is the registered one. The wrapper is passed to the inner host's ctor in place of SP's raw service |
| 5 | `NullReferenceException` at `Application.Instance.Invoke(...)` in every `PostMessage` to the WebView — pane rendered but settings + shells UI was empty | Eto loaded into the inner ALC had its own `Application.Instance` static, NOT initialized (Concord never explicitly `new Application(...)` anywhere; relies on SP's already-initialized one) | Same fix as #2 (dynamic Default-ALC defer) — but only fully resolved by #6 below |
| 6 | `FileNotFoundException: Eto v2.9.0.0` AFTER fix #5 was applied | SP 11.10 ships **Eto v2.8.0.0**; the shim was compiled against **Eto.Forms NuGet 2.9.\*** (floating spec auto-upgraded during the v5.1.0 build cycle). When Resolve correctly deferred to default for Eto, CLR's strict version-match fallback couldn't satisfy the v2.9 request with default's v2.8 | (a) Pinned `Eto.Forms` to `2.8.*` in both `Concord.Core.csproj` and `Concord.Host10x.csproj` so compile-time references match SP's shipped version. (b) Changed `Resolve` to return default's *actual loaded `Assembly`* (instead of returning `null` and relying on CLR's strict version fallback) — defense-in-depth against future SP-vs-Concord drift |

### Regression tests added

- `ConcordHostLoadContextTests.OnResolving_AssemblyNotInDefault_AndPresentLocally_LoadsLocally` (fix #2)
- `ConcordHostLoadContextTests.OnResolving_AssemblyLoadedInDefault_DefersToDefault_EvenWhenLocalCopyPresent` (fixes #2, #5, #6 — verifies SP-loaded assemblies are shared regardless of local-copy presence)
- `ShimForwarderTests.Open_ForwardsUIContextToInner_BeforeDelegating` (fix #3)
- `ShimForwarderTests.Ctor_WrapsExtensionFileService_InnerReceivesShimExtensionFileService` (fix #4)
- `FakeHostBuilder.EmitFakeHostWithPaneSeamAndEntry` (new builder for the seam test)
- Build-time `<Error>` gate in `MergeHostsForShim.targets` for fix #1

Full test count after Phase 5b: **357 PASS** (+3 skipped pre-existing). Up from 353 at the start of Phase 5.

---

## Perf-gate decision

- [x] **Both versions ≤500ms regression** → **PROCEED** to Phase 6 (docs + marketing).
- [ ] Either version >500ms regression → STOP, perf-tune.

---

## Notes for Phase 6 + Phase 7

- **Publisher discipline**: the `.mxmodule` MUST be exported from the *lowest* SP version's project (`Test_10_24_13`, SP 10.24.13). A `.mxmodule` exported from `Test_11_10` (SP 11.10) is rejected by SP 10.24.13 at install with "The package could not be imported, because it was created with a newer version of Mendix Studio Pro." Update the release playbook accordingly.
- **Eto version pinning**: future Mendix Studio Pro upgrades may ship a different Eto version. The `Eto.Forms` `PackageReference` in `Concord.Core.csproj` + `Concord.Host10x.csproj` should be re-checked at each Concord release and aligned with the lowest SP version's shipped Eto.dll. The dynamic `Resolve` fix in `ConcordHostLoadContext.cs` is forgiving (returns default's loaded Assembly explicitly), but matching compile-time + runtime versions is still the cleanest contract.
- **Deferred OQ2 (SP 12.x)** and **OQ3 (Mac)**: seed both in `_HANDOFF.md` backlog for the next session.
- **Documentation Phase 6**: the four marketing surfaces (2 MD + 2 HTML) need to reflect that v5.1.0 is now a single-artifact cross-version marketplace listing. Don't miss the HTML versions — that was the v4.2.1 cautionary tale per CLAUDE.md.
