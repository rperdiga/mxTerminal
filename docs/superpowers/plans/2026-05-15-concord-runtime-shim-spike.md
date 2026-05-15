# Concord runtime shim — Phase 0 spike plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to work through this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Spike output is empirical evidence, not production code — throwaway prototype DLLs go to `c:\Extensions\Terminal\spikes\runtime-shim\` (gitignored), findings go to a handoff doc.

**Goal:** Answer the three load-bearing empirical questions in [`docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md`](../specs/2026-05-15-concord-runtime-shim-design.md) §"Empirical questions" with concrete evidence from real Studio Pro 10.24.13 and 11.10 installations, so the implementation plan can be written against confirmed assumptions rather than hopes.

**Out of scope for this spike:** Any production code in `src/Concord.Shim/`. Any change to existing host DLLs, manifests, deploy targets, or marketing/deploy docs. Any commit to a long-lived branch.

**Tech stack:** C# / .NET 8 / Studio Pro 10.24.13 + 11.10 (Joe's local installs) / hand-rolled minimal `Concord.Shim.dll` prototype, freshly built per question.

**Source spec:** [`docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md`](../specs/2026-05-15-concord-runtime-shim-design.md)

---

## Branch and commit strategy — minimal ceremony

Per Joe's stated preference (and matching the v5.0.0 skills-rules-split pattern):

- **One branch:** `spike/v5.x-runtime-shim-feasibility` off current `main`. Throwaway — branch is **deleted** after the spike handoff is merged. No PR.
- **One commit** at the end, only if anything in the repo (e.g. `.gitignore`, spike findings handoff) needs to persist. The prototype DLL build artifacts under `spikes/runtime-shim/` are gitignored from the start — they never enter the index.
- **The spike's deliverable is the handoff document**, not source code. The handoff is committed to `docs/superpowers/handoffs/<date>-concord-shim-spike-findings.md` and merged to `main` directly. The implementation plan (Phase 1+) is written **against** that handoff in a follow-up branch.
- **No `.mxmodule` rebuild required during spike.** Each question is testable by manually deploying spike DLLs to `<testproject>/extensions/Concord/` and starting Studio Pro.

---

## File structure (spike-time, mostly throwaway)

### Files to create

| Path | Purpose | Persistence |
|---|---|---|
| `spikes/runtime-shim/Q1-mef-discovery/` | Prototype DLL + manifest to probe whether Studio Pro MEF-scans subdirectories | Throwaway; gitignored |
| `spikes/runtime-shim/Q2-load-context/` | Prototype shim that creates an `AssemblyLoadContext` and loads a fake host DLL | Throwaway; gitignored |
| `spikes/runtime-shim/Q3-menu-drift/` | Two prototype menu-extension DLLs (one class-based, one interface-based) to probe MEF activation on each Studio Pro version | Throwaway; gitignored |
| `docs/superpowers/handoffs/2026-05-XX-concord-shim-spike-findings.md` | The spike's empirical output: per-question evidence + recommended path forward | Persisted, committed to main |
| `.gitignore` (modified) | Add `spikes/` directory pattern | Persisted, committed to main |

### Files to modify

| Path | Change |
|---|---|
| `.gitignore` | Add line `spikes/` so prototype artifacts under `spikes/runtime-shim/` never get accidentally indexed |

---

## Phase 0: Spike — one phase, three questions, one commit

Goal of this phase: produce a single empirical handoff document that either greenlights the implementation spec or redirects to a fallback (shadow-copy, accept menu asymmetry, or abandon the shim approach entirely in favor of two `.mxmodule` files).

### Task 1: Setup — branch, gitignore, scratch space

**Files:** `.gitignore`, `spikes/runtime-shim/README.md` (briefly explains the scratch space; one paragraph)

- [ ] **Step 1: Create spike branch**

  ```
  git checkout main
  git pull --ff-only
  git checkout -b spike/v5.x-runtime-shim-feasibility
  ```

- [ ] **Step 2: Add `spikes/` to `.gitignore`**

  Open [`.gitignore`](../../../.gitignore), append:
  ```
  # Spike scratch space — prototype DLLs, throwaway test extensions, raw logs
  spikes/
  ```

- [ ] **Step 3: Create scratch space**

  ```
  mkdir -p spikes/runtime-shim/Q1-mef-discovery
  mkdir -p spikes/runtime-shim/Q2-load-context
  mkdir -p spikes/runtime-shim/Q3-menu-drift
  ```

  Write a one-paragraph `spikes/runtime-shim/README.md` noting the directory's purpose and gitignored status. (Even though gitignored, the README documents intent for any future developer who stumbles in.)

- [ ] **Step 4: Verify baseline tests still pass**

  Sanity check that the spike branch starts from a clean baseline:
  ```
  dotnet build Terminal.sln -p:Platform=x64
  dotnet test tests/Concord.Core.Tests/Concord.Core.Tests.csproj
  dotnet test tests/Terminal.Tests/Terminal.Tests.csproj
  ```

  Both should pass — 330 tests total per the latest handoff. **If they don't, stop and investigate before proceeding.** The spike must start from a known-good baseline so that any Studio Pro misbehavior during spike testing can be confidently attributed to the prototype DLLs, not a regression in the main codebase.

### Task 2: Q1 — does Studio Pro MEF-discover subdirectory DLLs?

**Files:** `spikes/runtime-shim/Q1-mef-discovery/Q1Shim.csproj`, `spikes/runtime-shim/Q1-mef-discovery/Q1Probe.cs`, `spikes/runtime-shim/Q1-mef-discovery/manifest.json`, and a subdirectory `bin-fake-wrong-version/` containing a deliberately broken DLL.

**The experiment**: build a minimal "shim" DLL that does nothing but log "I loaded on Studio Pro X.Y" to `%TEMP%\concord-spike-q1.log`. Drop it into `extensions/Concord/` of a test Mendix project. Inside the same folder, create a subdirectory `bin-fake-wrong-version/` containing a deliberately broken DLL (e.g. `Concord.Host11x.dll` taken from the 11x build, deployed alongside a Studio Pro 10.x install). Start Studio Pro 10.24.13. Observe:

- (a) Studio Pro starts cleanly and the shim logs successfully → Studio Pro does NOT scan subdirectories. **Q1 result: positive.** Spike design proceeds.
- (b) Studio Pro crashes or refuses to load the extension, same failure mode as the 2026-05-12 sibling-folder spike → Studio Pro DOES scan subdirectories. **Q1 result: negative.** Spike falls back to shadow-copy mitigation; expand Q2 to cover shadow-copy semantics.

- [ ] **Step 1: Build the Q1 probe shim**

  In `spikes/runtime-shim/Q1-mef-discovery/`:

  - `Q1Shim.csproj`: targets net8.0, references `Mendix.StudioPro.ExtensionsAPI 10.21.1`, `System.ComponentModel.Composition` (MEF). Output: `Q1Shim.dll`. No bundled content; no UI.
  - `Q1Probe.cs`: a single `[Export(typeof(DockablePaneExtension))]` class with a no-op `Open` override and an `[ImportingConstructor]` that calls `File.AppendAllText(Path.Combine(Path.GetTempPath(), "concord-spike-q1.log"), $"[{DateTime.UtcNow:O}] Q1Shim loaded\n")`. (Equivalently: log Studio Pro version, log working directory contents, log MEF catalog enumeration if accessible.) Pane ID: `Q1Spike`.
  - `manifest.json`: `{ "mx_extensions": ["Q1Shim.dll"] }`.

  Build:
  ```
  dotnet build spikes/runtime-shim/Q1-mef-discovery/Q1Shim.csproj
  ```

- [ ] **Step 2: Set up the test project for Studio Pro 10.x**

  Pick a Mendix project that has Studio Pro 10.24.13 installed locally and Extension Development enabled (per [`DEPLOYING.md`](../../../DEPLOYING.md) §"Studio Pro setup"). For Joe, this is the existing testbed for Host10x dev iteration (e.g. `TestOSApp3-10x` or equivalent — check `Directory.Build.props`).

  Manually deploy the Q1 probe + a deliberately-wrong-version DLL to the test project:
  ```
  # Pretend the test project root is c:\Workspace\MendixApps\TestOSApp3-10x
  set TEST=c:\Workspace\MendixApps\TestOSApp3-10x
  rd /s /q %TEST%\extensions\Concord 2>nul
  mkdir %TEST%\extensions\Concord
  xcopy /y /s /q spikes\runtime-shim\Q1-mef-discovery\bin\Debug\net8.0\* %TEST%\extensions\Concord\
  mkdir %TEST%\extensions\Concord\bin-fake-wrong-version
  copy /y src\Concord.Host11x\bin\Debug\net8.0\Concord.Host11x.dll %TEST%\extensions\Concord\bin-fake-wrong-version\
  copy /y src\Concord.Host11x\bin\Debug\net8.0\Mendix.StudioPro.ExtensionsAPI.dll %TEST%\extensions\Concord\bin-fake-wrong-version\
  ```

- [ ] **Step 3: Launch Studio Pro 10.24.13 and observe**

  - Delete `%TEST%\.mendix-cache\extensions-cache\*` before launch to ensure no stale snapshot.
  - Launch Studio Pro 10.24.13 against `%TEST%`.
  - Observe: does Studio Pro start cleanly? Does the `Q1Spike` extension appear in the Extensions menu? Does `%TEMP%\concord-spike-q1.log` contain the expected log line?
  - **Capture screenshots and any crash dump.** Save under `spikes/runtime-shim/Q1-mef-discovery/evidence/` (gitignored; just for the spike's own records).

- [ ] **Step 4: Mirror on Studio Pro 11.10**

  Same probe setup, but with the wrong-version DLL being `Concord.Host10x.dll` against a Studio Pro 11.10 project. Mirror the procedure exactly; capture the result.

- [ ] **Step 5: Document Q1 finding**

  Write a Q1 section in the in-progress findings handoff (Task 5 below): pass/fail per Studio Pro version, captured evidence, recommended implication for the design.

### Task 3: Q2 — does `AssemblyLoadContext` cleanly share `Mendix.StudioPro.ExtensionsAPI`?

**Files:** `spikes/runtime-shim/Q2-load-context/Q2Shim.csproj`, `Q2Shim.cs`, `Q2LoadContext.cs`, `manifest.json`, and a `bin-10x/` subfolder containing the existing Concord.Host10x build output verbatim (or `bin-11x/` for the 11x test).

**The experiment**: build a Q2Shim DLL that, on MEF activation, creates an `AssemblyLoadContext` pointed at `bin-{Nx}/` and reflectively instantiates `Concord.Host10x.Pane.TerminalPaneExtension` (or `Concord.Host11x.Pane.TerminalPaneExtension`). Forward `Open()` to it. Observe:

- (a) Pane opens, terminal initializes, action server starts — `AssemblyLoadContext` correctly shared the API types. **Q2 result: positive.**
- (b) Cast failure (`DockablePaneExtension` from default context vs. `DockablePaneExtension` from sub-context) — type identity broken. **Q2 result: negative.** Fallback options outlined in spec §"Empirical questions" Q2 row.

This experiment can be done **without** answering Q1 first — even if Q1 turns out negative (subdirectory scanning), the AssemblyLoadContext mechanics are the same, just with the host DLLs shadow-copied to `%TEMP%` instead of `bin-{Nx}/`. The findings inform the same design decision.

- [ ] **Step 1: Build the Q2 shim**

  `Q2Shim.csproj`: targets net8.0, references `Mendix.StudioPro.ExtensionsAPI 10.21.1` (lower of the two — this is the design baseline).

  `Q2LoadContext.cs`: a small `AssemblyLoadContext` subclass whose `Resolving` event:
  - Returns `null` for `Mendix.StudioPro.ExtensionsAPI` (forces fallback to default context — which is what we want)
  - Returns `null` for `System.*` (same — fallback to default)
  - For everything else: loads from the configured `bin-{Nx}/` folder

  `Q2Shim.cs`: a single `[Export(typeof(DockablePaneExtension))]` that, on first call to any override, instantiates the load context, loads `Concord.Host{Nx}.dll`, finds the host's `TerminalPaneExtension` type by reflection, instantiates it (passing dependencies as needed — note this is non-trivial since the host's MEF-imported services aren't available; for the spike, instantiate with `Activator.CreateInstance(type, ...)` passing fake/no-op service implementations), and forwards method calls.

  Build, deploy to `<testproject>/extensions/Concord/` (the test project from Task 2), and include `bin-10x/` (copy of `src/Concord.Host10x/bin/Debug/net8.0/*`) inside.

  **Note:** if Q1 turns out negative (Step 2 of Task 2), this step needs adjustment — move `bin-10x/` to `%TEMP%\Concord\10x\` and point the load context there instead of `extensions/Concord/bin-10x/`. The mechanics being tested are the same.

- [ ] **Step 2: Launch Studio Pro 10.24.13 and observe pane open**

  Delete the extension cache snapshot. Launch Studio Pro. Open the pane.

  - If the pane opens and a basic UI element renders: Q2 positive. Save evidence (screenshot, log, optionally a brief screen capture).
  - If pane open throws — capture the exception, stack trace, and the loaded-assemblies list at crash time. The cast site (`DockablePaneExtension` → host's subclass) is where the diagnostic value is highest.

- [ ] **Step 3: Mirror on Studio Pro 11.10 with `bin-11x/`**

  Same test, different host. Same observations.

- [ ] **Step 4: If both pass — micro-test type-identity for non-trivial APIs**

  Add a one-shot test to Q2Shim: from inside the host's instantiated `TerminalPaneExtension`, query Studio Pro for an `IPageGenerationService` (or similar service the host already imports), call one method on it, and observe whether the call succeeds. If yes, full forward-compat. If no — service binding fails despite the cast succeeding — capture the failure mode for the spec.

- [ ] **Step 5: Document Q2 finding**

  Add Q2 section to the findings handoff with: pass/fail per Studio Pro version, captured exception stacks if any, the loaded-assemblies dump (to confirm exactly one copy of `Mendix.StudioPro.ExtensionsAPI` is in memory), recommended implication for the design.

### Task 4: Q3 — `MenuExtension` vs `IMenuExtension` drift

**Files:** `spikes/runtime-shim/Q3-menu-drift/Q3-10x-class/`, `spikes/runtime-shim/Q3-menu-drift/Q3-11x-interface/`, `spikes/runtime-shim/Q3-menu-drift/Q3-shim-both/`.

**The experiment**: build three minimal extension DLLs and observe which combination loads where.

- **Q3-10x-class**: subclasses the 10.x `MenuExtension` abstract class. Built against ExtensionsAPI 10.21.1. Manifest lists this DLL.
- **Q3-11x-interface**: implements the 11.x `IMenuExtension` interface. Built against ExtensionsAPI 11.6.2. Manifest lists this DLL.
- **Q3-shim-both**: a single DLL (built against 10.21.1) that contains both a class-based and an interface-based menu extension. Manifest lists this DLL.

Deploy each to the appropriate Studio Pro version and observe which one(s) get loaded by MEF.

- [ ] **Step 1: Build all three Q3 variants**

  Each is a one-class DLL with a stub `OnClick` that logs to `%TEMP%\concord-spike-q3-{variant}.log`. Build commands per variant in the same style as Tasks 2 and 3.

- [ ] **Step 2: Deploy Q3-10x-class to Studio Pro 10.x project, observe**

  Confirms baseline: 10.x recognizes class-based menu extension.

- [ ] **Step 3: Deploy Q3-11x-interface to Studio Pro 11.10 project, observe**

  Confirms baseline: 11.x recognizes interface-based menu extension.

- [ ] **Step 4: Deploy Q3-shim-both to both Studio Pro versions, observe**

  Critical observation: does Studio Pro 10.x see the class-based export and ignore the interface-based one (graceful)? Does 11.x see the interface-based one and ignore the class-based? Or does one or both crash because the metadata for the "wrong" export can't be resolved against the runtime's ExtensionsAPI?

  - If both load gracefully → **Q3 result: positive.** Shim ships both exports; only one is activated per Studio Pro version. No asymmetry in the menu surface.
  - If one or both crash → **Q3 result: negative.** Document the failure mode. Fallback per spec: ship class-based only; on 11.x, register the menu via fallback code path inside the loaded host (or accept asymmetry, since the menu entry is a single "Open Concord" item that 11.x users can reach via the Extensions menu submenu Studio Pro autopopulates).

- [ ] **Step 5: Document Q3 finding**

  Add Q3 section to the findings handoff.

### Task 5: Write the findings handoff and commit

**Files:** `docs/superpowers/handoffs/2026-05-XX-concord-shim-spike-findings.md` (date filled in based on actual completion date).

The handoff is the spike's only persistent deliverable. It's the input to the Phase 1+ implementation plan.

- [ ] **Step 1: Compose handoff with structure:**

  - YAML frontmatter (`name`, `description`, `metadata.type: project`, `metadata.node_type: memory`, `originSessionId`)
  - **§ Q1 — MEF discovery scope.** Per-version result, evidence, design implication. One paragraph each per Studio Pro version, plus an "implication" paragraph.
  - **§ Q2 — AssemblyLoadContext sharing.** Same structure.
  - **§ Q3 — Menu drift.** Same structure.
  - **§ Recommended path forward.** Concrete: green-light spec as-is, green-light with caveats (list them), or fall back to two-mxmodule path. Cite the evidence.
  - **§ Followups for the implementation plan.** Specific design adjustments the spec needs based on what was observed (e.g., "Q1 negative — spec §2 'Folder layout' needs to be revised to use shadow-copy at `%TEMP%\Concord\<version>\<hash>\` instead of `bin-{Nx}/`").
  - **§ Open questions left unanswered.** What the spike didn't cover (e.g., performance benchmarks at scale; Studio Pro Mac variant if not tested).
  - **§ Evidence inventory.** List of screenshot/log files captured under `spikes/runtime-shim/.../evidence/` for posterity — paths only, since the files themselves are gitignored.

- [ ] **Step 2: Commit findings handoff**

  ```
  git add .gitignore docs/superpowers/handoffs/2026-05-XX-concord-shim-spike-findings.md
  git commit -m "spike: runtime-shim feasibility findings — Q1/Q2/Q3 empirical results"
  ```

  Single commit. No PR — the spike branch's only purpose was to host the prototype; the persistent output is the handoff committed straight to `main` via a fast-forward push (or trivial PR if Joe wants the doctrine of "everything PR-reviewed"; defer to his minimal-ceremony preference).

- [ ] **Step 3: Push and clean up**

  ```
  git push -u origin spike/v5.x-runtime-shim-feasibility
  ```

  Either:
  - Open a tiny PR for the handoff + .gitignore change (if Joe wants the doctrine), squash-merge, delete branch.
  - Or fast-forward `main` and delete the branch locally + remote.

  Either way: spike branch deleted; `spikes/` directory remains on local disk (gitignored) for any future re-validation.

### Task 6: Update `_HANDOFF.md` to point at the findings

**Files:** `C:\Users\rc1yok\.claude\projects\c--Extensions-Terminal\memory\_HANDOFF.md`, `C:\Users\rc1yok\.claude\projects\c--Extensions-Terminal\memory\MEMORY.md`.

The spike findings become the next session's starting context for writing the Phase 1+ implementation plan.

- [ ] **Step 1: Replace `_HANDOFF.md` body with new content** that summarizes the spike outcome and points at the findings handoff doc, the spec doc, and the (not-yet-written) Phase 1+ plan. Note next plausible action: "write Phase 1+ implementation plan based on `2026-05-XX-concord-shim-spike-findings.md`".

- [ ] **Step 2: Append a new entry to `MEMORY.md`** index for the spike findings, with one-line description.

---

## Definition of done

- [ ] Spike findings handoff committed to `main` (one commit, no churn).
- [ ] `.gitignore` updated so `spikes/` directory is excluded.
- [ ] `_HANDOFF.md` reflects spike outcome; `MEMORY.md` indexes the findings.
- [ ] Spike branch deleted (local + remote) after the handoff merges.
- [ ] Joe has explicit answers to Q1, Q2, Q3 with evidence cited, and a clear thumbs-up or thumbs-down on proceeding to Phase 1+ implementation.

If Q1 + Q2 are both negative, the spec's runtime-shim design is not viable in its current form. Joe gets a clear "fall back to two-mxmodule path" recommendation in the findings handoff. No code lost — the spec stays in the repo as documented-and-rejected design history, useful context for any future revisit when Mendix releases a packaging mechanism that supports version-conditional resources natively.
