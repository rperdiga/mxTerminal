---
name: concord-shim-spike-findings
description: "Phase 0 empirical findings — runtime-shim spec validated on Studio Pro 10.24.13 + 11.10. All three load-bearing questions (Q1 MEF subdir scope, Q2 AssemblyLoadContext type identity, Q3 menu export forward-compat) answered POSITIVE on both versions. Phase 1+ implementation can proceed against the spec."
metadata:
  node_type: memory
  type: project
  originSessionId: 2026-05-15-runtime-shim-spike-execution
---

# Concord runtime-shim spike — findings handoff (2026-05-15)

## TL;DR

All three load-bearing empirical questions in [`docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md`](../specs/2026-05-15-concord-runtime-shim-design.md) answered **POSITIVE on both Studio Pro 10.24.13 and 11.10**. The spec's `Concord.Shim` design — single shim DLL, `AssemblyLoadContext` with `Resolving` event redirecting `Mendix.StudioPro.ExtensionsAPI` to default context, `bin-{Nx}/` subfolders for version-specific hosts — is mechanically validated end-to-end. Phase 1+ implementation can proceed against the spec as written, with three minor revisions documented in §"Followups" below.

**Recommendation: GREEN-LIGHT the spec.** Begin Phase 1+ implementation plan.

| Question | SP 10.24.13 | SP 11.10 | Notes |
|---|---|---|---|
| **Q1** — MEF subdir scope | ✅ Positive | ✅ Positive | Only top-level `extensions/<ext>/manifest.json`-listed DLLs are MEF-discovered. Subdirs (incl. `bin-fake-wrong-version/`) snapshotted by Studio Pro but NOT treated as separate extensions. |
| **Q2** — AssemblyLoadContext type identity | ✅ Positive | ✅ Positive (cross-major-version) | Shim built against ExtensionsAPI 10.21.1; runtime resolved to **10.24.13.0** on SP10 and **11.10.0.0** on SP11. Type identity, cast, and virtual dispatch all hold; exactly ONE ExtensionsAPI copy in AppDomain `[ctx=Default]`. |
| **Q3** — `MenuExtension` forward-compat | ✅ Positive | ✅ Positive | Spec's original "class-vs-interface drift" framing empirically incorrect (corrected pre-spike, see §Q3). The real residual question — does a 10.21.1-built `[Export(typeof(MenuExtension))]` activate + dispatch on SP 11.10 — answered yes, full GetMenus + action-invoked round-trip. |

---

## Q1 — MEF subdirectory discovery scope

### Question

Does Studio Pro's MEF discovery scan only DLLs listed in `manifest.json`, or does it recursively scan subdirectories of `extensions/<extName>/`?

### Probe design

`Q1Shim.dll` — minimal `[Export(typeof(DockablePaneExtension))]` with a static cctor that logs to `%TEMP%\concord-spike-q1.log`. Deployed to `extensions/Concord/`. A subdirectory `bin-fake-wrong-version/` was created alongside, containing the FULL bin output of the OPPOSITE-version host:

- For SP 10.24.13 test: planted `Concord.Host11x` (built against ExtensionsAPI 11.6.2). If MEF scans subdirs, type resolution against SP 10.x's loaded 10.24.13.0 API would fail → Studio Pro crash (same failure mode as 2026-05-12 sibling-folder spike, commit `a0ce567`).
- For SP 11.10 test: planted `Concord.Host10x` (built against ExtensionsAPI 10.21.1). Mirror situation against SP 11.x.

Both planted DLLs carry their own valid `manifest.json` declaring the wrong-version host as a primary extension — so if MEF *did* recursively scan, the subfolder would be MEF-treated as a separate extension and the wrong-version DLL would fail discovery.

### Observed (SP 10.24.13)

- Studio Pro started cleanly, alive + responding throughout the run.
- Probe log written: `Q1Shim module-init fired — proc=studiopro pid=41812 — exe=C:\DevTools\Mendix\10.24.13.86719\modeler\studiopro.exe`.
- `.mendix-cache/extensions-cache/` produced **exactly 2 snapshot folders** — one for `extensions/Concord/` (= Q1Shim) and one for `extensions/Concord10x/` (= Ricardo's prod Concord deploy, present from prior dev work). **`bin-fake-wrong-version/` did NOT get its own snapshot**, although Studio Pro did wholesale-copy its contents inside the Q1Shim snapshot folder.
- Ricardo visually confirmed: Extensions menu shows "Concord10x > Open Pane" (his prod). No "Q1Spike" entry — expected, because Q1Shim has pane-only export, no menu export. No error dialogs.

### Observed (SP 11.10)

- Studio Pro 11.10 started cleanly, alive + responding throughout.
- Probe log written, same shape as SP10.
- Cache: 2 snapshot folders again — one for Concord (Q1Shim), one for Concord11x (Ricardo's prod). Same result.
- Bonus datapoint: `Q1Shim` was built against ExtensionsAPI **10.21.1** but loaded cleanly on SP 11.10's runtime (which has its own 11.10.0.0 API loaded). Useful forward-binding signal for the spec's `10⊆11` assumption.
- Ricardo visually confirmed: Extensions menu shows "Concord11x > Open Pane". No "Q1Spike" entry (same expected reason). No error dialogs.

### Verdict

**POSITIVE on both versions.** Studio Pro's extension discovery is **manifest-bound at the top level of `extensions/<extName>/`** — subdirectories are NOT independently MEF-scanned. The Studio Pro extension loader copies the whole extension folder tree into its per-project cache snapshot, but only the top-level `manifest.json`-listed DLLs go through MEF discovery.

### Design implication

**Spec's `bin-{Nx}/` layout is viable.** No shadow-copy fallback needed. The shim's host DLLs can live inside `extensions/Concord/bin-{10,11}x/` without Studio Pro attempting to MEF-discover them as separate extensions.

> One subtlety: Studio Pro's per-project cache snapshot DOES include the subdirectories. The shim must resolve host DLL paths from `Assembly.GetExecutingAssembly().Location` (NOT `AppDomain.CurrentDomain.BaseDirectory`, which returns Studio Pro's install dir, not the extension folder). This is documented in §"Probe bugs discovered" below — it's a non-trivial gotcha that needs to be in the Phase 1+ implementation, not just a probe-only concern.

---

## Q2 — `AssemblyLoadContext` type identity across the boundary

### Question

Will `AssemblyLoadContext.Resolving` correctly redirect `Mendix.StudioPro.ExtensionsAPI` and `System.*` requests back to Studio Pro's default-context copy, preserving CLR type identity for the API types Studio Pro hands the shim's [Export]s?

### Pre-spike local validation

Before any Studio Pro deploy, a `LocalRunner` console app (`spikes/runtime-shim/Q2-load-context/LocalRunner/`) was built to exercise the same probe code in a vanilla .NET 8 host. **All three checks PASS** when shim and host both bind to ExtensionsAPI 10.21.1:

```
[runner] type-identity check: PASS
[runner] cast check:          PASS
[runner] virtual dispatch:    PASS
[runner] ALL CHECKS PASS — Q2 mechanism is mechanically sound.
```

The local runner provided **strong pre-deploy confidence** that the AssemblyLoadContext + Resolving mechanism is correctly coded. The Studio Pro deploys then exercised the same mechanism against a *different* default-context ExtensionsAPI version, which is the actually-interesting case.

### Observed (SP 10.24.13)

Log excerpt:

```
shim's Assembly.Location: C:\Projects\Test_10_24_13\.mendix-cache\extensions-cache\a418eb27-.../Q2Shim.dll
shim's DockablePaneExtension AQN: Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneExtension, Mendix.StudioPro.ExtensionsAPI, Version=10.24.13.0, ...
shim's ExtensionsAPI location: C:\DevTools\Mendix\10.24.13.86719\modeler\Mendix.StudioPro.ExtensionsAPI.dll
hostDir=C:\Projects\Test_10_24_13\.mendix-cache\extensions-cache\a418eb27-.../bin-fake-host
loaded FakeHost: FakeHost, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
  Context: ConcordHost@C:\Projects\Test_10_24_13\.mendix-cache\extensions-cache\a418eb27-.../bin-fake-host
resolved FakeTerminalPaneExtension type: ConcordSpike.Q2.FakeHost.FakeTerminalPaneExtension, FakeHost, ...
  BaseType: Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneExtension, ... Version=10.24.13.0, ...
TYPE IDENTITY CHECK: ReferenceEquals(fakeType.BaseType, typeof(DockablePaneExtension)) = True
  -> CONVERGED: the same CLR type, shared across contexts.
CAST CHECK: (instance as DockablePaneExtension) = non-null
  -> CAST OK. Id from casted instance: Q2FakeHost
  virtual dispatch Id getter: Q2FakeHost
loaded ExtensionsAPI copies in AppDomain:
  - Mendix.StudioPro.ExtensionsAPI, Version=10.24.13.0, ... @ C:\DevTools\Mendix\10.24.13.86719\modeler\Mendix.StudioPro.ExtensionsAPI.dll [ctx=Default]
```

The shim was built against `Mendix.StudioPro.ExtensionsAPI, Version=10.21.1.0` but the runtime resolved every reference to Studio Pro's loaded `Version=10.24.13.0`. The `ConcordHostLoadContext` correctly forwarded ExtensionsAPI to the default context. FakeHost's `DockablePaneExtension` BaseType and the shim's `typeof(DockablePaneExtension)` are reference-equal — i.e., the SAME CLR type. Cast and virtual dispatch both succeeded. Exactly one ExtensionsAPI copy in the AppDomain.

### Observed (SP 11.10) — the cross-MAJOR-version case

Same probe DLL, deployed to Test_11_10. Log excerpt:

```
shim's DockablePaneExtension AQN: ... Version=11.10.0.0, ...
shim's ExtensionsAPI location: C:\DevTools\Mendix\11.10.0\modeler\Mendix.StudioPro.ExtensionsAPI.dll
...
  BaseType: Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneExtension, ... Version=11.10.0.0, ...
TYPE IDENTITY CHECK: ReferenceEquals(fakeType.BaseType, typeof(DockablePaneExtension)) = True
  -> CONVERGED: the same CLR type, shared across contexts.
CAST CHECK: (instance as DockablePaneExtension) = non-null
  virtual dispatch Id getter: Q2FakeHost
loaded ExtensionsAPI copies in AppDomain:
  - Mendix.StudioPro.ExtensionsAPI, Version=11.10.0.0, ... [ctx=Default]
```

**This is the design-critical result.** A shim DLL built against `Version=10.21.1.0` had its ExtensionsAPI reference resolved to `Version=11.10.0.0` at runtime — a *major-version* forward-bind. The `Resolving` event's "fall back to default context" strategy correctly handled this. Type identity preserved across both the major-version drift AND the AssemblyLoadContext boundary.

### Verdict

**POSITIVE on both versions, including the cross-major-version case.** The spec's central design choice — isolate the host in a custom `AssemblyLoadContext` while sharing ExtensionsAPI via the `Resolving` event — works as designed.

### Design implication

**Spec's `ConcordHostLoadContext` approach proceeds unmodified.** The `Resolving` event redirecting `Mendix.StudioPro.ExtensionsAPI` (and `System.*`, `Microsoft.*`) to default context preserves type identity across the boundary, even when shim and runtime API versions differ by a major version.

---

## Q3 — `MenuExtension` forward-compat

### Pre-spike correction to the spec

The spec originally framed Q3 as "`MenuExtension`-class (10.x) vs `IMenuExtension`-interface (11.x) drift". **This premise is empirically incorrect** for the versions Concord targets:

- ExtensionsAPI 10.21.1 exposes `Mendix.StudioPro.ExtensionsAPI.UI.Menu.MenuExtension` (abstract class) and `ContextMenuExtension<T>` (generic abstract class).
- ExtensionsAPI 11.6.2 exposes the **same two types** with the same signatures. No `IMenuExtension` interface exists in either version.
- `Concord.Host10x` and `Concord.Host11x` use byte-identical `[Export(typeof(MenuExtension))]` implementations today.

(Verified by reflection on the NuGet packages on disk; see [`spikes/runtime-shim/HANDOFF.md`](../../../spikes/runtime-shim/HANDOFF.md) (gitignored, local) for the audit script.)

The Q3 probe was simplified accordingly: one DLL with both `[Export(typeof(DockablePaneExtension))]` and `[Export(typeof(MenuExtension))]`, built against 10.21.1, deployed to both Studio Pro versions. The residual empirical question — *does the 10.21.1-built menu export get correctly enumerated AND its action dispatched on Studio Pro 11.10's runtime?* — is what we actually need to answer.

### Observed (SP 10.24.13 — baseline)

```
=== Q3 probe DLL loaded ===
MenuExtension type AQN: Mendix.StudioPro.ExtensionsAPI.UI.Menu.MenuExtension, ... Version=10.24.13.0, ...
ExtensionsAPI assembly version: 10.24.13.0
Q3MenuProbe.GetMenus called — Studio Pro is enumerating our menu export.
Q3MenuProbe menu action invoked — full activation round-trip OK.
```

- DLL loaded; MenuExtension export activated.
- `GetMenus` invoked at startup (Studio Pro eagerly enumerates menu exports to build the Extensions menu tree).
- Ricardo visually confirmed "Q3Spike > Q3 Probe Click Me" in the Extensions menu.
- Ricardo clicked the menu item → menu action's `Action` lambda invoked → log line written. Full activation + dispatch round-trip working.

### Observed (SP 11.10 — the actual test)

```
=== Q3 probe DLL loaded ===
MenuExtension type AQN: ... Version=11.10.0.0, ...                       ← forward-bound across major-version jump
ExtensionsAPI assembly version: 11.10.0.0
Q3MenuProbe.GetMenus called
Q3MenuProbe menu action invoked — full activation round-trip OK.
```

Same shape, except the runtime-bound API version is now `11.10.0.0`. The shim was built against `Version=10.21.1.0`; SP 11.10 loaded its `Version=11.10.0.0`; the shim's `[Export(typeof(MenuExtension))]` was honored by SP 11.10's MEF; Ricardo's click fired the action correctly.

### Verdict

**POSITIVE on both versions.** A single shim DLL with `[Export(typeof(MenuExtension))]` built against ExtensionsAPI 10.21.1 works on both SP 10.24.13 and SP 11.10. **No menu asymmetry.**

### Design implication

**The shim ships a single class-based `[Export(typeof(MenuExtension))]`** that works on both versions. The spec's fallback ("ship class-based only; on 11.x, register menu via fallback code path inside the loaded host") is **not needed** — the class-based export forward-binds cleanly.

---

## Recommended path forward

**Green-light the spec as written.** Three minor revisions documented below.

Phase 1+ implementation plan (separate document, NOT in scope for this spike per the plan's "Out of scope" clause) should:

1. Use the spec's `bin-{Nx}/` layout directly. No shadow-copy fallback needed.
2. Use a static constructor (cctor) on the shim's [Export] class to bootstrap the load context (NOT `[ModuleInitializer]` — see §"Probe bugs" below).
3. Resolve the shim's deployed-folder root via `typeof(<shim-export-class>).Assembly.Location`, NOT `AppDomain.CurrentDomain.BaseDirectory`. The latter returns Studio Pro's install dir under the per-project cache-snapshot deployment model.

---

## Followups for the Phase 1+ implementation plan

### 1. Use static cctor, NOT `[ModuleInitializer]`, for shim bootstrap

`[ModuleInitializer]` was observed to fire **unreliably** on .NET 10 SDK 10.0.203 during local pre-validation — even after `Activator.CreateInstance`, the initializer did not run. The `Q2LocalRunner` first run produced no log; switching to a static constructor on the [Export] class resolved it.

All three spike probes were refactored to use static cctors. The Phase 1+ shim must do the same. The spec's existing "on first instantiation" wording (§"How the shim works at runtime") already aligns with cctor semantics — just be explicit in the implementation NOT to use `[ModuleInitializer]`.

### 2. Resolve deployed-folder root via `Assembly.Location`

The probe's original use of `AppDomain.CurrentDomain.BaseDirectory` produced wrong paths under Studio Pro — it returns the Studio Pro install dir (e.g., `C:\DevTools\Mendix\10.24.13.86719\modeler\`), not the extension's snapshot folder (e.g., `C:\Projects\<proj>\.mendix-cache\extensions-cache\<guid>\`). The Q2 probe initially aborted with "FakeHost.dll not found" because of this; fixing it to `Path.GetDirectoryName(typeof(Q2Probe).Assembly.Location)` resolved it.

Phase 1+ `RuntimeHostLocator.Resolve()` must compute the host folder relative to the shim assembly's actual location, not the AppDomain base directory. Spec §"Approach" should be lightly updated to call this out as a design constraint (not just an implementation note) — it's load-bearing for the cache-snapshot deployment model.

### 3. Q3 spec section revision

Spec's "Empirical questions" Q3 row and §"Open questions" item 3 (menu-extension drift) should be marked as resolved with the pre-spike correction: no class-vs-interface drift exists in 10.21.1 ↔ 11.6.2; both expose the same `MenuExtension` abstract class. The shim ships one class-based export, no asymmetry, no fallback needed.

### 4. Cache snapshot mechanics — spec is correct, but add a note

Studio Pro snapshots the whole `extensions/<ext>/` tree (including all subdirectories) into `<project>/.mendix-cache/extensions-cache/<guid>/` on first load. Q1's evidence shows: the snapshot includes `bin-fake-wrong-version/` and would include `bin-{Nx}/`, but MEF only scans the top-level manifest-listed DLLs. The spec's existing reference to the cache-snapshot mechanism in §"Folder layout invariants" is correct; the spike just adds empirical confirmation that the snapshot copies subdirs faithfully.

---

## Open questions left unanswered by this spike

- **Performance benchmark.** The spike did not measure pane-open latency or Studio Pro startup time under the shim vs the current direct-host path. Phase 1+ should benchmark before/after and flag if regression exceeds 500ms.
- **Studio Pro 12.x.** Spec assumes `10⊆11` forward-compat for Concord's used types. Once Studio Pro 12 ships, re-verify the same surface against `12.x` ExtensionsAPI before shipping a shim built against an older baseline.
- **Mac variant.** This spike was Windows-only (SP for Mac wasn't available on the test machine). Concord builds are net8.0 cross-platform; the shim mechanism should work the same, but worth a Mac smoke test before shipping.
- **Real-world host instantiation.** The Q2 probe used a minimal `FakeTerminalPaneExtension` with a parameterless constructor. The real `TerminalPaneExtension` has 8+ MEF-imported service dependencies. Q2 confirmed the load-context mechanics; it did NOT verify that the host's MEF imports succeed when loaded via reflection from inside the shim's load context. Phase 1+ MUST work out how the host's `[ImportingConstructor]` services get injected — likely via the shim relaying its own MEF imports to the loaded host instance, OR the host re-importing them itself via a separate MEF container scoped to the load context.
- **`Concord.Core.dll` divergence.** The spec specifies two copies of `Concord.Core.dll` (top-level for shim + inside `bin-{Nx}/` for host). The spike did not exercise this two-copy structure (FakeHost was self-contained). Phase 1+ must implement the build-time hash-check the spec mentions in §"Folder layout invariants" to prevent drift.

---

## Pre-spike API-drift verification (10.21.1 → 11.6.2)

Empirically verified every `Mendix.StudioPro.ExtensionsAPI.*` type referenced in `src/Concord.Host10x/` and `src/Concord.Host11x/` against the XML docs of both NuGet packages (the two host source trees turned out to be line-identical in their API usage, so a single audit covers both):

| Type | 10.21.1 | 11.6.2 | Notes |
|---|---|---|---|
| `DockablePaneExtension`, `DockablePaneViewModelBase` | ✓ | ✓ | Same signatures |
| `MenuExtension`, `MenuViewModel` | ✓ | ✓ | Class-based in both; ctor `(System.String, System.Action)` unchanged |
| `IDockingWindowService`, `ILocalRunConfigurationsService`, `IExtensionFileService` | ✓ | ✓ | |
| `IPageGenerationService`, `INavigationManagerService`, `IMicroflowService` | ✓ | ✓ | |
| `INameValidationService`, `IUntypedModelAccessService`, `IMicroflowExpressionService` | ✓ | ✓ | |
| `IVersionControlService` | ✓ | ✓ | |
| `IWebServerExtension` | (assembly) | (assembly) | Not in XML doc on either; referenced by both hosts and both builds succeed |
| `IEntity`, `IConstant`, `IMicroflow`, `AssociationType`, `AssociationOwner` | ✓ | ✓ | All present, same signatures |

The spec's `10⊆11` assumption is safe for the surface Concord actually touches. A CI test that loads both XMLs and asserts the union of Concord-used types are present is a fit hardening step for Phase 1+ but not blocking.

---

## Probe bugs discovered (corrected in spike artifacts; lessons for Phase 1+)

### A. `[ModuleInitializer]` fires unreliably on .NET 10 SDK 10.0.203

Symptom: Q2LocalRunner's first run reported "log not written" even after `Activator.CreateInstance(typeof(Q2Probe))`. The IL had the `<Module>.cctor` wired correctly (verified by substring scan of the produced DLL), but the runtime did not invoke it during the test.

Fix: refactor all three probes to put the experiment in the [Export] class's static constructor. MEF's instantiation of the export then triggers the cctor deterministically.

Impact on production shim: use static cctor. Don't rely on `[ModuleInitializer]`.

### B. `AppDomain.CurrentDomain.BaseDirectory` returns Studio Pro's install dir under the per-project cache-snapshot deployment model

Symptom: Q2's first SP10 deploy aborted with "FakeHost.dll not found in any candidate location: C:\DevTools\Mendix\10.24.13.86719\modeler\bin-fake-host | ...". The probe was trying to locate FakeHost relative to AppDomain base, which Studio Pro sets to its OWN install dir, not the deployed extension folder.

Fix: use `Path.GetDirectoryName(typeof(<probe-export-class>).Assembly.Location)`. Studio Pro deploys extensions to `<project>/.mendix-cache/extensions-cache/<guid>/<files>`; the assembly's own location is the only reliable anchor.

Impact on production shim: `RuntimeHostLocator.Resolve()` (per spec §"How the shim works at runtime") must use this pattern. This is non-obvious — anyone implementing it from the spec alone would likely reach for `BaseDirectory` first.

---

## Tangential observations captured during the spike

### Pre-existing test flake (not caused by the spike)

`Terminal.Tests.ClaudeMdManagerTests.Apply_MultipleManagedBlocksFromCorruptState_CollapsesToOne` flaked once during the baseline test run (Task 1, Step 4): `UnauthorizedAccessException` at `ClaudeMdManager.AtomicWrite`'s `File.Move(... overwrite: true)` (line 381 in `src/Concord.Core/Terminal/ClaudeMdManager.cs`). Re-running the test in isolation passed. Pre-existing Windows file-system race (file watcher / antivirus / indexer briefly holds a handle on the destination during atomic-move). Worth tracking separately as a future hardening if it recurs.

### Stale comment NOT fixed (deferred per minimal-ceremony preference)

[src/Concord.Host10x/Concord.Host10x.csproj:32-34](../../../src/Concord.Host10x/Concord.Host10x.csproj#L32-L34) says "MEF skips" on a wrong-version sibling folder, which contradicts the 2026-05-12 verified-crash behavior. The previous session's `_HANDOFF.md` already flagged this for opportunistic fix; this spike session deliberately deferred it to keep the spike branch's diff minimal. Should be picked up in the Phase 1+ implementation branch (which touches this csproj naturally) or as a one-line standalone PR.

---

## Evidence inventory

Probe source + DEPLOY guides + raw build outputs all under `spikes/runtime-shim/` (gitignored — see [.gitignore](../../../.gitignore)):

- `spikes/runtime-shim/Q1-mef-discovery/` — Q1Shim probe, deploy script, log artifacts
- `spikes/runtime-shim/Q2-load-context/Q2Shim/` — Q2 main probe DLL
- `spikes/runtime-shim/Q2-load-context/FakeHost/` — minimal stand-in for `Concord.Host{10,11}x` used by Q2
- `spikes/runtime-shim/Q2-load-context/LocalRunner/` — pre-deploy console runner that validated Q2 mechanics outside Studio Pro
- `spikes/runtime-shim/Q3-menu-drift/` — Q3 probe with both pane + menu exports

Probe logs (transient, in `%TEMP%`):
- `concord-spike-q1.log`, `concord-spike-q2.log`, `concord-spike-q3.log` — last-run-wins (probes truncate at deploy time)

UI confirmation: Ricardo's visual observations during the spike runs are captured inline above. No screenshots checked in (per minimal-ceremony preference; the substantive evidence is the probe logs, which are reproducible).

## See also

- Spec: [docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md](../specs/2026-05-15-concord-runtime-shim-design.md)
- Spike plan: [docs/superpowers/plans/2026-05-15-concord-runtime-shim-spike.md](../plans/2026-05-15-concord-runtime-shim-spike.md)
- [[runtime-shim-cross-version]] — broader context for why this design is necessary
- [[mendix-extension-cache]] — Studio Pro's `.mendix-cache/extensions-cache/<guid>/` snapshot mechanism (referenced by Q1 and the "Assembly.Location" probe bug)
