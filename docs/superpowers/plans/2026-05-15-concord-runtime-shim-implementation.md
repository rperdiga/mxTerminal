# Concord runtime-shim implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Atomic commit per phase; one PR at the end. Match the v5.0.0 cycle's "atomic-commit-per-phase + minimal-ceremony" pattern.

**Goal:** Implement [`docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md`](../specs/2026-05-15-concord-runtime-shim-design.md) — a single `Concord.Shim.dll` that loads, isolates, and forwards to a version-appropriate host (`Concord.Host10x` or `Concord.Host11x`) from inside a per-extension `AssemblyLoadContext`, so a single `.mxmodule` works on both Studio Pro 10.24.13 and 11.10+.

**Architecture:** A new project `src/Concord.Shim/` builds against ExtensionsAPI 10.21.1 (the lower-version baseline; forward-compatible to 11.x per the [Phase 0 findings](../handoffs/2026-05-15-concord-shim-spike-findings.md)). The shim publishes the **three** MEF exports Studio Pro consumes (`DockablePaneExtension`, `MenuExtension`, `WebServerExtension`), each with an `[ImportingConstructor]` that mirrors the inner host's signature 1:1. On first activation the shim creates one process-wide `ConcordHostLoadContext` pointed at `extensions/Concord/bin-{10,11}x/`, reflectively instantiates `Host{N}xEntry` (firing the host's static bootstrap) and the chosen host export, and delegates every override to the inner instance.

**Tech stack:** C# 12 / .NET 8 / `System.Runtime.Loader.AssemblyLoadContext` / `System.ComponentModel.Composition` (MEF) / xUnit + FluentAssertions / `Microsoft.CodeAnalysis.CSharp` (Roslyn) for compiling test-fake assemblies / MSBuild custom targets / Studio Pro 10.24.13 + 11.10 for manual smoke.

**Source spec:** [`docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md`](../specs/2026-05-15-concord-runtime-shim-design.md)
**Phase 0 findings (supersede the spec on Q3 and add two implementation constraints):** [`docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md`](../handoffs/2026-05-15-concord-shim-spike-findings.md)
**Spike plan (style reference only):** [`docs/superpowers/plans/2026-05-15-concord-runtime-shim-spike.md`](./2026-05-15-concord-runtime-shim-spike.md)

---

## Resolution of the 5 open questions in the findings doc

The spike's "Open questions left unanswered" must be settled by this plan, not punted to implementation time. Each resolution is binding on the phases below.

### OQ1 — Performance benchmark

**Resolution:** Phase 5 (manual Studio Pro smoke) instruments the shim's hot path with `ShimLog.Timed` wrappers around the 5 measurable steps (locate host folder, build load context, load host assembly, instantiate `Host{N}xEntry`, instantiate primary export). The smoke matrix records pane-open wall-clock latency on both Studio Pro versions and compares against the v5.0.0 baseline captured in the v5.0.0 cycle's handoffs.

**Pass gate:** ≤500 ms regression in pane-open latency (per spec §"Open questions for the implementation plan" item 4). >500 ms is a Phase 5 STOP and forces a perf-tuning sub-phase before merge.

### OQ2 — Studio Pro 12.x forward-compat

**Resolution:** Explicitly OUT OF SCOPE for this plan. The shim's baseline binding is ExtensionsAPI 10.21.1, which Phase 0 proved forward-compatible to 11.10.0.0 across a major-version jump. There is no Studio Pro 12.x preview SDK as of 2026-05-15.

**Tracking:** After merge, Phase 7 adds a one-line follow-up to `_HANDOFF.md`'s "deferred" section: *re-verify shim against ExtensionsAPI 12.x ContentMatrix when Mendix ships a 12.x preview*.

### OQ3 — Mac variant

**Resolution:** OUT OF SCOPE for first ship. The shim's mechanism (`AssemblyLoadContext`, `Resolving` event, reflection) is OS-agnostic .NET 8. The folder-layout deploy logic in Phase 4's MSBuild targets must remain cross-platform (already true of the current `xcopy`/`cp -R` split). Mac smoke is Ricardo's fast-follow after Windows ships.

**Tracking:** Phase 7 backlog entry: *run a Mac smoke pass against `extensions/Concord/` on SP for Mac as soon as a build is on the device; fast-follow patch if any platform-specific glitch surfaces*.

### OQ4 — Host MEF imports (the load-bearing one)

**Resolution: Option A — shim relays its own MEF imports.** Each shim forwarder declares the same `[ImportingConstructor]` shape as its inner host counterpart. Studio Pro's MEF satisfies the shim's imports (it sees the shim's DLL via `manifest.json`). The shim captures the service references and passes them positionally to the inner host type's ctor via `Activator.CreateInstance(hostType, ctorArgs)`. Type identity holds across the load-context boundary because every service interface lives in `Mendix.StudioPro.ExtensionsAPI`, which the `Resolving` event redirects to the default context (proven empirically by Phase 0 Q2 — including across the 10.21.1 → 11.10.0.0 major-version jump).

**Why not Option B (re-import via inner-context MEF container):** Studio Pro's MEF catalog is private; we can't enumerate it. Standing up an independent `CompositionContainer` inside the load context would require re-implementing service discovery — fragile, untestable, and adds a second wiring path that diverges from Studio Pro's truth at zero benefit. Option A is one direction of data flow, type-identity-safe, and trivially unit-testable with a FakeHost.

**Implications for the shim code (binding on Phase 3):**

1. **`TerminalPaneExtensionShim`** mirrors `TerminalPaneExtension`'s 9-arg `[ImportingConstructor]` exactly (5 required + 4 `[Import(AllowDefault = true)]` optional). It stores the references and passes them positionally to `Activator.CreateInstance` after resolving the host type.
2. **`ConcordMenuExtensionShim`** mirrors `TerminalMenuExtension`'s 1-arg ctor (`IDockingWindowService`).
3. **`TerminalWebServerShim`** mirrors `TerminalWebServer`'s 1-arg ctor (`IExtensionFileService`).
4. **`Host{N}xEntry`** is NOT a Studio Pro MEF type — it's a private host-internal chain-trigger. Under the shim, the host's `[Import(typeof(Host{N}xEntry))]` field on pane/web-server is never satisfied (no inner MEF container runs). The shim must explicitly instantiate `Host{N}xEntry` ONCE during load-context bootstrap to drive its static side effects (`HostContext.Initialize`, `HostServices.Register`, `ToolCatalog` population). The field on the host's pane/web-server stays `null`; per the existing `#pragma warning disable CS0414` comment it's never read.
5. **The host's own `[Export]` / `[ImportingConstructor]` attributes remain in place but become vestigial under the shim.** Harmless dead metadata. A 1-line `// [shim-vestigial]` comment near each [Export] documents this for future readers; no removal — the attributes don't cost anything at runtime and removing them would compound the diff.

### OQ5 — `Concord.Core.dll` divergence

**Resolution:** Phase 4's `MergeHostsForShim` MSBuild target SHA-256 hashes both copies of `Concord.Core.dll` (the top-level one and each `bin-{Nx}/` one) and fails the build if any pair diverges. The hash check uses MSBuild's built-in `<GetFileHash Algorithm="SHA256" />` task (available since .NET 6 SDK) — no custom tooling required. The mismatch error is loud and unambiguous: `BUILD FAILED — Concord.Core.dll hash mismatch between extensions/Concord/Concord.Core.dll and bin-{Nx}/Concord.Core.dll. This breaks HostServices static state.`

**Why this matters:** `HostServices` and `HostContext` carry static state (the registered `IApp` / `IRunConfigurationsService` shims, the `ToolCatalog`). Two copies of `Concord.Core.dll` that diverge produce two disjoint static states — the shim writes to copy A's, the host reads from copy B's — silent runtime corruption with no exception. The hash gate prevents this at build time.

---

## Branch and commit strategy — minimal ceremony

Per Ricardo's stated preference (matches the v5.0.0 cycle):

- **One branch:** `feat/v5.1.0-runtime-shim` off `main` at the post-spike commit (`531cc2b`).
- **One commit per phase.** Seven phases below; seven commits. Each commit message: `phase N: <one-line summary>` followed by a short paragraph and a `Refs:` line linking to this plan.
- **One PR at the end.** Open after Phase 6 lands; Phase 7 is the PR + adversarial review + merge + tag dance.
- **No PR until smoke passes.** Phase 5 is the gate. If smoke regresses Studio Pro startup or pane open by >500 ms, stop and add a tuning sub-phase before opening the PR.

---

## File structure map

### Files to create

| Path | Purpose | Phase |
|---|---|---|
| `src/Concord.Shim/Concord.Shim.csproj` | .NET 8 class-library targeting ExtensionsAPI 10.21.1; produces `Concord.Shim.dll` | 1 |
| `src/Concord.Shim/manifest.json` | `{"mx_extensions":["Concord.Shim.dll"]}` | 1 |
| `src/Concord.Shim/ShimLog.cs` | Pre-`HostContext` logger (`%TEMP%\Concord\shim.log`, rolling 1 MB) + `Timed` helper | 1 |
| `src/Concord.Shim/RuntimeHostLocator.cs` | Studio Pro version probe + `bin-{Nx}/` path resolution off `Assembly.Location` | 2 |
| `src/Concord.Shim/ConcordHostLoadContext.cs` | `AssemblyLoadContext` subclass; `Resolving` event forwards API + system to default context | 2 |
| `src/Concord.Shim/HostKickstart.cs` | Idempotent inner-context bootstrap (resolve folder, build context, load `Concord.Host{N}x.dll`, instantiate `Host{N}xEntry` once) | 3 |
| `src/Concord.Shim/Pane/TerminalPaneExtensionShim.cs` | `[Export(typeof(DockablePaneExtension))]` forwarder | 3 |
| `src/Concord.Shim/Menu/ConcordMenuExtensionShim.cs` | `[Export(typeof(MenuExtension))]` forwarder | 3 |
| `src/Concord.Shim/WebServer/TerminalWebServerShim.cs` | `[Export(typeof(WebServerExtension))]` forwarder | 3 |
| `tests/Concord.Shim.Tests/Concord.Shim.Tests.csproj` | xUnit + FluentAssertions + Roslyn for test-fake DLLs | 2 |
| `tests/Concord.Shim.Tests/RuntimeHostLocatorTests.cs` | Boundary cases for version → `bin-{Nx}/` selection | 2 |
| `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs` | Resolving event redirects, type-identity across boundary, exactly-one-API-copy assertion | 2 |
| `tests/Concord.Shim.Tests/HostKickstartTests.cs` | Idempotency, `Host{N}xEntry` invocation, missing-folder error path | 3 |
| `tests/Concord.Shim.Tests/ShimForwarderTests.cs` | Each forwarder captures services, delegates to fake host, returns the right values | 3 |
| `tests/Concord.Shim.Tests/Fakes/FakeHostBuilder.cs` | Roslyn-emit a fake `Concord.Host{N}x.dll` with the same MEF shapes as production | 2/3 |
| `src/Concord.Shim/build/MergeHostsForShim.targets` | MSBuild target that produces `bin/x64/$(Configuration)/net8.0-merged/` | 4 |
| `docs/superpowers/handoffs/2026-05-XX-concord-shim-smoke-results.md` | Phase 5 smoke output | 5 |

### Files to modify

| Path | Change | Phase |
|---|---|---|
| `Terminal.sln` | Add `Concord.Shim` and `Concord.Shim.Tests` project references | 1 |
| `Directory.Build.props` | Add `<ExtensionsApiShimBaselineVersion>` property (= 10.21.1) so the shim and Host10x share the baseline | 1 |
| `src/Concord.Host10x/manifest.json` | Stays for now (Phase 4 removes the per-host deploy; old manifest only removed from the deploy target, not the source tree, so dev iteration can still hit a per-host deploy if `MendixDeployTarget10x` is set) | — |
| `src/Concord.Host11x/manifest.json` | Same | — |
| `src/Concord.Host10x/Concord.Host10x.csproj` | Phase 4: leave per-host `DeployToMendix` in place but gate it on `$(MendixDeployTarget10x) != ''`; remove the stale "MEF skips" comment (lines 31–34) opportunistically | 4 |
| `src/Concord.Host11x/Concord.Host11x.csproj` | Phase 4: same gating; **move `BuildUi` target out** to a new `build/BuildUi.targets` that the Shim project imports (so wwwroot bundling fires under the shim build path) | 4 |
| `src/Concord.Host10x/Pane/TerminalPaneExtension.cs` | Phase 3: add `// [shim-vestigial]` comment near the `[Export]` and `[ImportingConstructor]` (1-line each, sentinel comment only — no code change) | 3 |
| `src/Concord.Host10x/MenuExtensions/TerminalMenuExtension.cs` | Same | 3 |
| `src/Concord.Host10x/Ui/TerminalWebServer.cs` | Same | 3 |
| (mirror three above for `Concord.Host11x`) | Same | 3 |
| `Directory.Build.props` | Phase 4: add `MendixDeployTargetMerged` property; default empty | 4 |
| `DEPLOYING.md` | Phase 6: replace "which folder do I need?" matrix with single drop-in instruction | 6 |
| `README.md` | Phase 6: drop the version-fork install paragraph | 6 |
| `CHANGELOG.md` | Phase 6: v5.1.0 entry with empirical perf baseline | 6 |
| `CLAUDE.md` | Phase 6: update key-paths and architecture cheat sheet | 6 |
| `marketing/marketplace-overview.md` | Phase 6: drop version-fork messaging | 6 |
| `marketing/marketplace-overview.html` | Phase 6: same (keep MD + HTML in sync — burned us before) | 6 |
| `marketing/release-announcement.md` | Phase 6: same | 6 |
| `marketing/release-announcement.html` | Phase 6: same | 6 |
| `_HANDOFF.md` (memory) | Phase 7: replace with post-merge state | 7 |
| `MEMORY.md` (memory) | Phase 7: index the post-merge handoff + smoke-results handoff | 7 |

---

## Phase 1: Concord.Shim project skeleton + ShimLog

**Goal:** A buildable, empty-of-logic `Concord.Shim.dll` integrated into the solution. Output produces the right artifact in the right output dir; doesn't load on Studio Pro yet (manifest will list a still-empty DLL).

### Task 1.1: Branch and baseline

- [ ] **Step 1: Create the working branch**

  ```powershell
  git checkout main
  git pull --ff-only
  git checkout -b feat/v5.1.0-runtime-shim
  ```

- [ ] **Step 2: Verify clean baseline**

  ```powershell
  dotnet build Terminal.sln -p:Platform=x64
  dotnet test tests/Concord.Core.Tests/Concord.Core.Tests.csproj
  dotnet test tests/Terminal.Tests/Terminal.Tests.csproj
  ```

  Expected: build green; Concord.Core.Tests 56/56 PASS; Terminal.Tests 273 PASS + 3 environmental skips. If anything red, stop and investigate.

### Task 1.2: Create the project

**Files:** `src/Concord.Shim/Concord.Shim.csproj`, `src/Concord.Shim/manifest.json`, `Directory.Build.props`.

- [ ] **Step 1: Add `ExtensionsApiShimBaselineVersion` property**

  Edit [`Directory.Build.props`](../../../Directory.Build.props) — locate the existing `<PropertyGroup>` block that contains `<ExtensionsApi10xVersion>` and add a sibling:

  ```xml
  <ExtensionsApiShimBaselineVersion>10.21.1</ExtensionsApiShimBaselineVersion>
  ```

  Rationale: the shim's API version IS the same as Host10x's lower bound, but encoding it as its own property documents the design intent (this is the *shim baseline*, not coincidentally the 10x version). Phase 0 verified the shim's 10.21.1 binding forward-compatibly resolves to 10.24.13.0 on SP10 and 11.10.0.0 on SP11.

- [ ] **Step 2: Create `src/Concord.Shim/Concord.Shim.csproj`**

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net8.0</TargetFramework>
      <ImplicitUsings>enable</ImplicitUsings>
      <Nullable>enable</Nullable>
      <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
      <AssemblyName>Concord.Shim</AssemblyName>
      <RootNamespace>Concord.Shim</RootNamespace>
      <LangVersion>preview</LangVersion>
      <IsPackable>false</IsPackable>
      <Version>5.1.0-alpha.1</Version>
    </PropertyGroup>

    <ItemGroup>
      <InternalsVisibleTo Include="Concord.Shim.Tests" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\Concord.Core\Concord.Core.csproj" />
      <PackageReference Include="Mendix.StudioPro.ExtensionsAPI" Version="$(ExtensionsApiShimBaselineVersion)" />
    </ItemGroup>

    <ItemGroup>
      <Content Include="manifest.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>
    </ItemGroup>
  </Project>
  ```

  Note: **no** `DeployToMendix` target on the shim yet — Phase 4 introduces the unified merged deploy.

- [ ] **Step 3: Create `src/Concord.Shim/manifest.json`**

  ```json
  { "mx_extensions": ["Concord.Shim.dll"] }
  ```

- [ ] **Step 4: Add the project to the solution**

  ```powershell
  dotnet sln Terminal.sln add src\Concord.Shim\Concord.Shim.csproj
  ```

- [ ] **Step 5: Verify it builds**

  ```powershell
  dotnet build src\Concord.Shim\Concord.Shim.csproj -p:Platform=x64
  ```

  Expected: builds clean. Output at `src\Concord.Shim\bin\Debug\net8.0\Concord.Shim.dll` (+ `Concord.Core.dll` + `manifest.json` + the ExtensionsAPI DLLs copied locally per `CopyLocalLockFileAssemblies=true`).

### Task 1.3: Create the shim logger

**Files:** `src/Concord.Shim/ShimLog.cs`.

`ShimLog` is the pre-`HostContext` logger. The host's `Logger` (in `Concord.Core`) needs `HostContext.Initialize` to have run, but the shim runs BEFORE the host is even loaded. We need a logger that works from the static cctor of the shim's first activated [Export], with no setup.

- [ ] **Step 1: Write `ShimLog.cs`**

  ```csharp
  using System;
  using System.IO;

  namespace Concord.Shim;

  /// <summary>
  /// Pre-HostContext logger. Writes to %TEMP%\Concord\shim.log with rolling
  /// truncation at 1 MB. Safe to call before any Concord.Core type has been
  /// touched. After HostContext.Initialize the host's Logger takes over;
  /// ShimLog is only used during load-context bootstrap and for errors that
  /// happen before the host has been instantiated.
  /// </summary>
  internal static class ShimLog
  {
      private static readonly object _gate = new();
      private static readonly string _path =
          Path.Combine(Path.GetTempPath(), "Concord", "shim.log");
      private const long MaxBytes = 1_000_000;

      static ShimLog()
      {
          try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); }
          catch { /* logger must never throw */ }
      }

      public static void Info(string message) => Write("INFO ", message);
      public static void Warn(string message) => Write("WARN ", message);
      public static void Error(string message, Exception? ex = null)
      {
          Write("ERROR", message + (ex is null ? "" : $"\n{ex}"));
      }

      /// <summary>
      /// Times the given action; logs "<label> took Xms" at INFO. Returns the
      /// action's result. Used by HostKickstart to feed Phase 5's perf matrix.
      /// </summary>
      public static T Timed<T>(string label, Func<T> action)
      {
          var sw = System.Diagnostics.Stopwatch.StartNew();
          var result = action();
          sw.Stop();
          Info($"{label} took {sw.ElapsedMilliseconds}ms");
          return result;
      }

      private static void Write(string level, string message)
      {
          try
          {
              lock (_gate)
              {
                  RollIfTooLarge();
                  using var sw = new StreamWriter(_path, append: true);
                  sw.WriteLine($"[{DateTime.UtcNow:O}] {level} {message}");
              }
          }
          catch { /* logger must never throw */ }
      }

      private static void RollIfTooLarge()
      {
          try
          {
              if (!File.Exists(_path)) return;
              var len = new FileInfo(_path).Length;
              if (len < MaxBytes) return;
              var backup = _path + ".1";
              if (File.Exists(backup)) File.Delete(backup);
              File.Move(_path, backup);
          }
          catch { /* logger must never throw */ }
      }
  }
  ```

- [ ] **Step 2: Build, confirm green**

  ```powershell
  dotnet build src\Concord.Shim\Concord.Shim.csproj -p:Platform=x64
  ```

  Expected: builds clean. No warnings.

### Task 1.4: Commit Phase 1

- [ ] **Step 1: Stage and commit**

  ```powershell
  git add Directory.Build.props src/Concord.Shim/ Terminal.sln
  git commit -m @'
  phase 1: Concord.Shim project skeleton + ShimLog

  Adds a buildable Concord.Shim project (net8.0, ExtensionsAPI 10.21.1
  baseline, refs Concord.Core) with a manifest declaring only Concord.Shim.dll
  and a pre-HostContext logger writing to %TEMP%\Concord\shim.log with rolling
  truncation. No exports yet — empty scaffolding that Phases 2–3 fill in.

  Refs: docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
  '@
  ```

---

## Phase 2: Isolation primitives (RuntimeHostLocator + ConcordHostLoadContext)

**Goal:** Pure-logic, fully unit-tested isolation primitives. No MEF, no Studio Pro, no reflection-against-real-host. By the end of this phase, the two foundational classes the shim's forwarders will compose are working and tested.

### Task 2.1: Create the test project

**Files:** `tests/Concord.Shim.Tests/Concord.Shim.Tests.csproj`, `tests/Concord.Shim.Tests/Fakes/FakeHostBuilder.cs`.

- [ ] **Step 1: Create the test csproj**

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net8.0</TargetFramework>
      <ImplicitUsings>enable</ImplicitUsings>
      <Nullable>enable</Nullable>
      <IsPackable>false</IsPackable>
      <LangVersion>preview</LangVersion>
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
      <PackageReference Include="xunit" Version="2.9.0" />
      <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
      <PackageReference Include="FluentAssertions" Version="6.12.0" />
      <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.10.0" />
      <PackageReference Include="Mendix.StudioPro.ExtensionsAPI" Version="$(ExtensionsApiShimBaselineVersion)" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\..\src\Concord.Shim\Concord.Shim.csproj" />
    </ItemGroup>
  </Project>
  ```

  Roslyn (`Microsoft.CodeAnalysis.CSharp`) is used by `FakeHostBuilder` to emit fake host DLLs in-test so the load-context tests don't depend on the real production hosts.

- [ ] **Step 2: Add to solution**

  ```powershell
  dotnet sln Terminal.sln add tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj
  ```

- [ ] **Step 3: Write `FakeHostBuilder` skeleton**

  This util compiles a tiny "fake host" assembly at test time. The fake host has:
  - A type that subclasses `Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneExtension`.
  - A parameterless ctor (the simplest case — Task 2.3 uses this).
  - Returns a known string from a public `string Marker => "fake-host-marker"` property so tests can assert the right type was instantiated.

  ```csharp
  // tests/Concord.Shim.Tests/Fakes/FakeHostBuilder.cs
  using System.Reflection;
  using Microsoft.CodeAnalysis;
  using Microsoft.CodeAnalysis.CSharp;

  namespace Concord.Shim.Tests.Fakes;

  internal static class FakeHostBuilder
  {
      /// <summary>
      /// Emits FakeHost.dll to <paramref name="outputDir"/> with a single type
      /// FakeHost.FakeDockablePane : DockablePaneExtension and a public string
      /// property Marker = "fake-host-marker".
      /// </summary>
      public static string EmitFakeHost(string outputDir, string assemblyName = "FakeHost")
      {
          const string source = """
              using System;
              using System.Collections.Generic;
              using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;

              namespace FakeHost
              {
                  public class FakeDockablePane : DockablePaneExtension
                  {
                      public override string Id => "FakeHost";
                      public string Marker => "fake-host-marker";
                      public override DockablePaneViewModelBase Open() => null!;
                  }
              }
              """;

          var refs = new List<MetadataReference>
          {
              MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
              MetadataReference.CreateFromFile(typeof(DockablePaneExtensionRef).Assembly.Location),
              // Add runtime-required refs:
              MetadataReference.CreateFromFile(
                  Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
              MetadataReference.CreateFromFile(
                  Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "netstandard.dll")),
          };

          var compilation = CSharpCompilation.Create(
              assemblyName,
              new[] { CSharpSyntaxTree.ParseText(source) },
              refs,
              new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

          Directory.CreateDirectory(outputDir);
          var dllPath = Path.Combine(outputDir, assemblyName + ".dll");
          var result = compilation.Emit(dllPath);
          if (!result.Success)
              throw new InvalidOperationException(
                  "Fake host compile failed:\n" + string.Join("\n", result.Diagnostics));
          return dllPath;
      }

      // Sentinel type used purely to locate the ExtensionsAPI assembly for refs.
      private sealed class DockablePaneExtensionRef
          : Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneExtension
      {
          public override string Id => "ref";
          public override Mendix.StudioPro.ExtensionsAPI.UI.DockablePane.DockablePaneViewModelBase Open() => null!;
      }
  }
  ```

- [ ] **Step 4: Smoke-test the builder with a one-shot test**

  Add `tests/Concord.Shim.Tests/Fakes/FakeHostBuilderTests.cs`:

  ```csharp
  using FluentAssertions;
  using Xunit;

  namespace Concord.Shim.Tests.Fakes;

  public class FakeHostBuilderTests
  {
      [Fact]
      public void EmitFakeHost_ProducesLoadableDll()
      {
          var temp = Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));
          try
          {
              var dllPath = FakeHostBuilder.EmitFakeHost(temp);
              File.Exists(dllPath).Should().BeTrue();
              new FileInfo(dllPath).Length.Should().BeGreaterThan(1024);
          }
          finally
          {
              try { Directory.Delete(temp, true); } catch { }
          }
      }
  }
  ```

- [ ] **Step 5: Run the test, confirm green**

  ```powershell
  dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --filter "FullyQualifiedName~FakeHostBuilderTests"
  ```

  Expected: 1/1 PASS. (No commit yet — wait for end of phase.)

### Task 2.2: RuntimeHostLocator — TDD

**Files:** `src/Concord.Shim/RuntimeHostLocator.cs`, `tests/Concord.Shim.Tests/RuntimeHostLocatorTests.cs`.

The locator is the version-mapping table + path resolver. Two responsibilities:

1. **Version → bin folder name.** Given a Studio Pro version string (e.g., `10.24.13`, `11.10.0`, `11.9.5`), return `"bin-10x"` or `"bin-11x"`. Boundary: 11.0.0+ → 11x; anything below → 10x; unknown → 11x with WARN log (since 11.x is the forward-active branch).
2. **Resolve `bin-{Nx}/` absolute path.** Compute it as `Path.Combine(Path.GetDirectoryName(typeof(<shim-export-class>).Assembly.Location)!, binFolderName)`. Per [Phase 0 finding §"Probe bugs"](../handoffs/2026-05-15-concord-shim-spike-findings.md), **must NOT** use `AppDomain.CurrentDomain.BaseDirectory`.

- [ ] **Step 1: Write failing tests first**

  ```csharp
  // tests/Concord.Shim.Tests/RuntimeHostLocatorTests.cs
  using FluentAssertions;
  using Xunit;

  namespace Concord.Shim.Tests;

  public class RuntimeHostLocatorTests
  {
      [Theory]
      [InlineData("10.24.13", "bin-10x")]
      [InlineData("10.21.1", "bin-10x")]
      [InlineData("10.0.0", "bin-10x")]
      [InlineData("11.0.0", "bin-11x")]
      [InlineData("11.6.2", "bin-11x")]
      [InlineData("11.10.0", "bin-11x")]
      [InlineData("11.9.5", "bin-11x")]
      [InlineData("12.0.0-preview", "bin-11x")] // forward-active branch wins; SP12 may invalidate
      public void BinFolderName_ForVersion_ReturnsExpected(string version, string expected)
      {
          RuntimeHostLocator.BinFolderName(version).Should().Be(expected);
      }

      [Fact]
      public void BinFolderName_ForUnknownInput_DefaultsTo11x()
      {
          RuntimeHostLocator.BinFolderName(null).Should().Be("bin-11x");
          RuntimeHostLocator.BinFolderName("").Should().Be("bin-11x");
          RuntimeHostLocator.BinFolderName("garbage").Should().Be("bin-11x");
      }

      [Fact]
      public void ResolveBinDirectory_AnchorsToAssemblyLocation_NotAppDomainBase()
      {
          // The locator must resolve relative to the shim's own assembly path,
          // not AppDomain.BaseDirectory — Phase 0 found the latter returns
          // Studio Pro's install dir under the cache-snapshot deployment model.
          var anchorAssemblyDir = Path.GetDirectoryName(typeof(RuntimeHostLocator).Assembly.Location)!;
          var got = RuntimeHostLocator.ResolveBinDirectoryFromAnchor(anchorAssemblyDir, "bin-11x");
          got.Should().Be(Path.Combine(anchorAssemblyDir, "bin-11x"));
      }
  }
  ```

- [ ] **Step 2: Run, confirm RED**

  ```powershell
  dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --filter "FullyQualifiedName~RuntimeHostLocatorTests"
  ```

  Expected: compile errors (class doesn't exist).

- [ ] **Step 3: Implement `RuntimeHostLocator.cs`**

  ```csharp
  using System.IO;
  using System.Reflection;
  using Terminal.Interop; // StudioProThemeProbe lives here

  namespace Concord.Shim;

  /// <summary>
  /// Maps Studio Pro versions to the shim's bin-{Nx}/ subdirectory and
  /// resolves the absolute path to that subdirectory anchored on the shim
  /// assembly's actual file location.
  ///
  /// CRITICAL: anchors on Assembly.Location, NOT AppDomain.BaseDirectory.
  /// Studio Pro deploys extensions to <project>/.mendix-cache/extensions-cache/
  /// <guid>/, but AppDomain.BaseDirectory returns Studio Pro's install dir.
  /// This was empirically verified during the Phase 0 spike (see
  /// docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md
  /// §"Probe bugs discovered" item B).
  /// </summary>
  internal static class RuntimeHostLocator
  {
      public static string BinFolderName(string? studioProVersion)
      {
          if (string.IsNullOrWhiteSpace(studioProVersion)) return "bin-11x";
          if (!System.Version.TryParse(SplitOffPrerelease(studioProVersion), out var v))
              return "bin-11x"; // unknown / garbage
          return v.Major >= 11 ? "bin-11x" : "bin-10x";
      }

      public static (string binDir, string version) ResolveBinDirectory()
      {
          var anchorAssemblyDir = AssemblyLocationDir(typeof(RuntimeHostLocator));
          var version = StudioProThemeProbe.StudioProVersionFromExePath();
          var binName = BinFolderName(version);
          return (ResolveBinDirectoryFromAnchor(anchorAssemblyDir, binName), version ?? "<unknown>");
      }

      public static string ResolveBinDirectoryFromAnchor(string anchorAssemblyDir, string binFolderName)
          => Path.Combine(anchorAssemblyDir, binFolderName);

      private static string AssemblyLocationDir(System.Type anchor)
          => Path.GetDirectoryName(anchor.Assembly.Location)
             ?? throw new System.InvalidOperationException(
                $"Could not resolve directory of {anchor.FullName} assembly.");

      private static string SplitOffPrerelease(string v)
      {
          var dashIndex = v.IndexOf('-');
          return dashIndex >= 0 ? v.Substring(0, dashIndex) : v;
      }
  }
  ```

  **Note on `StudioProThemeProbe.StudioProVersionFromExePath`:** This already exists in `Concord.Core` (see [src/Concord.Host11x/Host11xEntry.cs:46](../../../src/Concord.Host11x/Host11xEntry.cs#L46) which uses it for Maia detection). It reads the running Studio Pro's `.exe` version. Phase 5 smoke verifies it works under both 10.24.13 and 11.10.

- [ ] **Step 4: Run, confirm GREEN**

  ```powershell
  dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --filter "FullyQualifiedName~RuntimeHostLocatorTests"
  ```

  Expected: 11/11 PASS.

### Task 2.3: ConcordHostLoadContext — TDD

**Files:** `src/Concord.Shim/ConcordHostLoadContext.cs`, `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`.

The load context is the heart of the isolation. Its `Resolving` event must:

1. For `Mendix.StudioPro.ExtensionsAPI` → return `null` (falls back to default context, where Studio Pro's already-loaded copy lives).
2. For `System.*`, `Microsoft.*`, `netstandard`, `mscorlib` → return `null` (default-context fallback, preserves system type identity).
3. For everything else → load from the configured `bin-{Nx}/` folder if a matching DLL exists; otherwise return `null` (let default context try; failure surfaces as `FileNotFoundException` at the call site).

Phase 0 Q2 proved this strategy correct on both Studio Pro versions, including the cross-major-version case.

- [ ] **Step 1: Write failing tests**

  ```csharp
  // tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs
  using System.Reflection;
  using Concord.Shim.Tests.Fakes;
  using FluentAssertions;
  using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
  using Xunit;

  namespace Concord.Shim.Tests;

  public class ConcordHostLoadContextTests : IDisposable
  {
      private readonly string _tempDir =
          Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));

      public void Dispose()
      {
          try { Directory.Delete(_tempDir, true); } catch { }
      }

      [Fact]
      public void Load_FakeHostDll_AssemblyLoadsIntoCustomContext()
      {
          var dllPath = FakeHostBuilder.EmitFakeHost(_tempDir);
          using var ctx = new ConcordHostLoadContext(_tempDir);

          var asm = ctx.LoadFromAssemblyPath(dllPath);

          asm.Should().NotBeNull();
          asm.GetName().Name.Should().Be("FakeHost");
      }

      [Fact]
      public void LoadedHostType_DockablePaneExtensionBaseType_IsReferenceEqualToDefaultContextType()
      {
          // This is the load-bearing invariant. The fake host's
          // FakeDockablePane subclasses DockablePaneExtension. If the
          // Resolving event correctly redirects Mendix.StudioPro.ExtensionsAPI
          // to the default context, then the loaded type's BaseType MUST
          // be ReferenceEqual to typeof(DockablePaneExtension) from this
          // test assembly's perspective.
          var dllPath = FakeHostBuilder.EmitFakeHost(_tempDir);
          using var ctx = new ConcordHostLoadContext(_tempDir);
          var asm = ctx.LoadFromAssemblyPath(dllPath);

          var fakeType = asm.GetType("FakeHost.FakeDockablePane")!;

          fakeType.BaseType.Should().BeSameAs(typeof(DockablePaneExtension));
      }

      [Fact]
      public void LoadedInstance_CastsToDefaultContextType_AndVirtualDispatchWorks()
      {
          var dllPath = FakeHostBuilder.EmitFakeHost(_tempDir);
          using var ctx = new ConcordHostLoadContext(_tempDir);
          var asm = ctx.LoadFromAssemblyPath(dllPath);
          var fakeType = asm.GetType("FakeHost.FakeDockablePane")!;

          var instance = Activator.CreateInstance(fakeType)!;
          var castInstance = instance as DockablePaneExtension;

          castInstance.Should().NotBeNull();
          castInstance!.Id.Should().Be("FakeHost"); // virtual property dispatch
      }

      [Fact]
      public void ExactlyOne_ExtensionsApi_LoadedInAppDomain_AfterCustomContextLoads()
      {
          var dllPath = FakeHostBuilder.EmitFakeHost(_tempDir);
          using var ctx = new ConcordHostLoadContext(_tempDir);
          ctx.LoadFromAssemblyPath(dllPath);

          var apiCopies = AppDomain.CurrentDomain.GetAssemblies()
              .Where(a => a.GetName().Name == "Mendix.StudioPro.ExtensionsAPI")
              .ToList();

          apiCopies.Should().HaveCount(1,
              "Resolving event must redirect ExtensionsAPI requests from the custom " +
              "context to the default context — never load a second copy.");
      }
  }
  ```

- [ ] **Step 2: Run, confirm RED**

  ```powershell
  dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --filter "FullyQualifiedName~ConcordHostLoadContextTests"
  ```

  Expected: compile errors.

- [ ] **Step 3: Implement `ConcordHostLoadContext.cs`**

  ```csharp
  using System.IO;
  using System.Reflection;
  using System.Runtime.Loader;

  namespace Concord.Shim;

  /// <summary>
  /// AssemblyLoadContext for the version-specific host. Resolves
  /// Mendix.StudioPro.ExtensionsAPI and System.* / Microsoft.* through the
  /// default context (so Studio Pro's already-loaded copy is reused and CLR
  /// type identity is preserved across the boundary). Resolves everything
  /// else from the configured bin-{Nx}/ folder.
  ///
  /// One instance per process is sufficient (and intended) — the shim binds
  /// to a host on first load and stays bound for the process lifetime, per
  /// spec §"Non-goals". Multiple instances would double-load the host
  /// assembly and double-state HostServices/HostContext.
  /// </summary>
  internal sealed class ConcordHostLoadContext : AssemblyLoadContext, IDisposable
  {
      private readonly string _hostFolder;
      private readonly AssemblyDependencyResolver? _resolver;

      public ConcordHostLoadContext(string hostFolder)
          : base(name: $"ConcordHost@{hostFolder}", isCollectible: false)
      {
          _hostFolder = hostFolder;
          // AssemblyDependencyResolver reads the .deps.json of a primary
          // assembly to resolve its dependency graph. We seed it with the
          // expected host DLL path; the resolver tolerates the file being
          // absent at construction time as long as the .deps.json appears
          // before any LoadFromAssemblyPath call.
          var likelyHostDll = Path.Combine(hostFolder, "Concord.Host10x.dll");
          if (!File.Exists(likelyHostDll))
              likelyHostDll = Path.Combine(hostFolder, "Concord.Host11x.dll");
          if (File.Exists(likelyHostDll))
              _resolver = new AssemblyDependencyResolver(likelyHostDll);

          Resolving += OnResolving;
      }

      protected override Assembly? Load(AssemblyName assemblyName) =>
          // The Load override fires for the host assembly itself when something
          // inside the context asks for it. Default to falling through to
          // OnResolving so the shared-types rules apply uniformly.
          null;

      private Assembly? OnResolving(AssemblyLoadContext _, AssemblyName name)
      {
          // Shared types — defer to default context. This preserves CLR
          // type identity for any type Studio Pro hands across the boundary.
          if (IsSharedAssembly(name.Name))
              return null;

          // Try the host folder.
          var candidate = Path.Combine(_hostFolder, name.Name + ".dll");
          if (File.Exists(candidate))
          {
              ShimLog.Info($"Resolved {name.Name} from {candidate} into {Name}");
              return LoadFromAssemblyPath(candidate);
          }

          // Try via the dependency resolver as a secondary lookup (handles
          // runtime/<rid>/ subfolders for native interop etc.).
          var resolved = _resolver?.ResolveAssemblyToPath(name);
          if (resolved is not null && File.Exists(resolved))
              return LoadFromAssemblyPath(resolved);

          ShimLog.Warn($"Could not resolve {name.Name} in {Name}; deferring to default context.");
          return null;
      }

      private static bool IsSharedAssembly(string? name)
      {
          if (name is null) return false;
          return name.StartsWith("Mendix.StudioPro.ExtensionsAPI", StringComparison.Ordinal)
              || name.StartsWith("System.", StringComparison.Ordinal)
              || name.StartsWith("Microsoft.", StringComparison.Ordinal)
              || name == "System"
              || name == "netstandard"
              || name == "mscorlib";
      }

      public void Dispose()
      {
          // AssemblyLoadContext is non-collectible (we set isCollectible: false
          // because the .NET CoreCLR + native interop layer in Studio Pro is
          // not designed to support collectible contexts safely under all the
          // unmanaged code paths Studio Pro's loader takes). Dispose just
          // detaches the Resolving handler so any further attempts fail-fast.
          Resolving -= OnResolving;
      }
  }
  ```

- [ ] **Step 4: Run, confirm GREEN**

  ```powershell
  dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --filter "FullyQualifiedName~ConcordHostLoadContextTests"
  ```

  Expected: 4/4 PASS. If any fail, the `Resolving` event logic is the culprit — re-check `IsSharedAssembly` patterns first.

### Task 2.4: Commit Phase 2

- [ ] **Step 1: Run the full test suite to confirm no regressions**

  ```powershell
  dotnet test Terminal.sln
  ```

  Expected: 56 + 273 + new Concord.Shim.Tests count, all PASS (modulo the known `Apply_MultipleManagedBlocksFromCorruptState_CollapsesToOne` flake — re-run in isolation if it trips).

- [ ] **Step 2: Stage and commit**

  ```powershell
  git add src/Concord.Shim/ tests/Concord.Shim.Tests/ Terminal.sln
  git commit -m @'
  phase 2: isolation primitives — RuntimeHostLocator + ConcordHostLoadContext

  Adds the two pure-logic foundations Phase 3 composes:

  - RuntimeHostLocator: Studio Pro version -> bin-{Nx}/ folder name; resolves
    the bin folder path anchored on the shim assembly's actual file location
    (Path.GetDirectoryName(typeof(RuntimeHostLocator).Assembly.Location)),
    NOT AppDomain.BaseDirectory — per Phase 0 finding §"Probe bugs" item B.

  - ConcordHostLoadContext: AssemblyLoadContext subclass whose Resolving event
    forwards Mendix.StudioPro.ExtensionsAPI + System.*/Microsoft.* to the
    default context (preserving type identity, proven by Phase 0 Q2), and
    resolves everything else from the configured bin-{Nx}/ folder.

  Tests use Roslyn (Microsoft.CodeAnalysis.CSharp) to emit a minimal FakeHost
  with a single DockablePaneExtension subclass; the load-context tests assert
  type-identity, cast, virtual dispatch, and the "exactly-one-ExtensionsAPI-
  in-AppDomain" invariant. Same shape as the Phase 0 LocalRunner that
  validated the mechanism end-to-end.

  Refs: docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
  '@
  ```

---

## Phase 3: Shim MEF forwarders + Host{N}xEntry bootstrap chain

**Goal:** The three production MEF exports + the `HostKickstart` helper that drives `Host{N}xEntry` static init. End of phase: locally deploying `extensions/Concord/` with this build into a Studio Pro test project will start the extension and open the pane on either version. (Manual verification deferred to Phase 5 smoke matrix.)

This phase implements **Option A** from OQ4 above: shim mirrors host `[ImportingConstructor]` shapes exactly and relays the services via positional `Activator.CreateInstance`.

### Task 3.1: HostKickstart — TDD

**Files:** `src/Concord.Shim/HostKickstart.cs`, `tests/Concord.Shim.Tests/HostKickstartTests.cs`.

`HostKickstart` is the idempotent process-wide bootstrap. First call: resolve host folder, build `ConcordHostLoadContext`, load `Concord.Host{N}x.dll`, instantiate `Host{N}xEntry` once (fires `HostContext.Initialize`, `HostServices.Register`, `ToolCatalog` population). Subsequent calls: return the cached context.

- [ ] **Step 1: Extend `FakeHostBuilder` to emit a `Host{N}xEntry`-like type**

  Add a second method on `FakeHostBuilder`:

  ```csharp
  /// <summary>
  /// Emits FakeHost.dll containing both FakeDockablePane (Task 2.1) AND
  /// a FakeHostEntry type with a parameterless ctor that, on first
  /// instantiation, writes "fake-entry-ran" to the side-channel file.
  /// Used by HostKickstart tests to assert the bootstrap chain fires
  /// exactly once.
  /// </summary>
  public static string EmitFakeHostWithEntry(string outputDir, string sideChannelPath, string assemblyName = "FakeHost")
  {
      // sideChannelPath is inlined as a verbatim string literal — escape backslashes.
      var escapedPath = sideChannelPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
      var source = $$"""
          using System;
          using System.Collections.Generic;
          using System.IO;
          using System.Threading;
          using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;

          namespace FakeHost
          {
              public class FakeDockablePane : DockablePaneExtension
              {
                  public override string Id => "FakeHost";
                  public string Marker => "fake-host-marker";
                  public override DockablePaneViewModelBase Open() => null!;
              }

              public class FakeHostEntry
              {
                  private static int _ran;
                  public FakeHostEntry()
                  {
                      if (Interlocked.Exchange(ref _ran, 1) != 0) return;
                      File.AppendAllText("{{escapedPath}}", "fake-entry-ran\n");
                  }
              }
          }
          """;

      var refs = new List<MetadataReference>
      {
          MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
          MetadataReference.CreateFromFile(typeof(DockablePaneExtensionRef).Assembly.Location),
          MetadataReference.CreateFromFile(
              Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
          MetadataReference.CreateFromFile(
              Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "netstandard.dll")),
          MetadataReference.CreateFromFile(
              Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.IO.FileSystem.dll")),
          MetadataReference.CreateFromFile(
              Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Threading.dll")),
      };

      var compilation = CSharpCompilation.Create(
          assemblyName,
          new[] { CSharpSyntaxTree.ParseText(source) },
          refs,
          new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

      Directory.CreateDirectory(outputDir);
      var dllPath = Path.Combine(outputDir, assemblyName + ".dll");
      var result = compilation.Emit(dllPath);
      if (!result.Success)
          throw new InvalidOperationException(
              "Fake host (with entry) compile failed:\n" + string.Join("\n", result.Diagnostics));
      return dllPath;
  }
  ```

  This same pattern (single Roslyn `CSharpCompilation` with one source unit per fake-host shape) extends to the next 3 variants used by Tasks 3.2–3.4:

  - **`EmitFakeHostWithPaneAndEntry(outputDir, sideChannelPath)`** — adds `FakeHost.FakePane : DockablePaneExtension` with a 2-arg ctor `(ILocalRunConfigurationsService, IExtensionFileService)` storing both args; exposes `LocalRunConfigsMarker => localRunConfigs?.GetType().Name` and `FileServiceMarker => fileService?.GetType().Name`. Add a MetadataReference for the test project's `Mendix.StudioPro.ExtensionsAPI.dll` (already in the test csproj's package refs).
  - **`EmitFakeHostWithMenuAndEntry(outputDir, sideChannelPath)`** — adds `FakeHost.FakeMenu : MenuExtension` with 1-arg ctor `(IDockingWindowService)`; `GetMenus()` yields one `new MenuViewModel("fake-caption", () => { })`.
  - **`EmitFakeHostWithWebServerAndEntry(outputDir, sideChannelPath)`** — adds `FakeHost.FakeWebServer : WebServerExtension` with 1-arg ctor `(IExtensionFileService)`; `InitializeWebServer(webServer)` calls `webServer.AddRoute("fake", (req, resp, ct) => Task.CompletedTask)`.

- [ ] **Step 2: Write failing tests for `HostKickstart`**

  ```csharp
  // tests/Concord.Shim.Tests/HostKickstartTests.cs
  using Concord.Shim.Tests.Fakes;
  using FluentAssertions;
  using Xunit;

  namespace Concord.Shim.Tests;

  // Sequential, NOT parallel — HostKickstart has process-wide static state.
  [CollectionDefinition("HostKickstart", DisableParallelization = true)]
  public class HostKickstartCollection { }

  [Collection("HostKickstart")]
  public class HostKickstartTests : IDisposable
  {
      private readonly string _tempDir =
          Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));
      private readonly string _sideChannel;

      public HostKickstartTests()
      {
          Directory.CreateDirectory(_tempDir);
          _sideChannel = Path.Combine(_tempDir, "fake-entry.log");
          // CRITICAL: tests must NOT clobber each other's static state. Use a
          // per-test reset hook (Task 3.1.5 below adds HostKickstart.ResetForTesting).
          HostKickstart.ResetForTesting();
      }

      public void Dispose()
      {
          HostKickstart.ResetForTesting();
          try { Directory.Delete(_tempDir, true); } catch { }
      }

      [Fact]
      public void EnsureLoaded_FiresFakeHostEntryExactlyOnce_OnMultipleCalls()
      {
          var dllPath = FakeHostBuilder.EmitFakeHostWithEntry(_tempDir, _sideChannel);

          // Override the host folder + host-DLL name + entry-type-name via the
          // testing seam (Task 3.1.6).
          HostKickstart.OverrideForTesting(
              hostFolder: _tempDir,
              hostAssemblyName: "FakeHost",
              entryTypeName: "FakeHost.FakeHostEntry");

          HostKickstart.EnsureLoaded();
          HostKickstart.EnsureLoaded();
          HostKickstart.EnsureLoaded();

          File.ReadAllText(_sideChannel).Should().Be("fake-entry-ran\n");
      }

      [Fact]
      public void EnsureLoaded_MissingHostFolder_ThrowsClearMessage()
      {
          HostKickstart.OverrideForTesting(
              hostFolder: Path.Combine(_tempDir, "nope"),
              hostAssemblyName: "FakeHost",
              entryTypeName: "FakeHost.FakeHostEntry");

          var act = () => HostKickstart.EnsureLoaded();

          act.Should().Throw<DirectoryNotFoundException>()
             .WithMessage("*nope*");
      }

      [Fact]
      public void ResolveHostType_ReturnsTypeFromCustomLoadContext_NotDefaultContext()
      {
          var dllPath = FakeHostBuilder.EmitFakeHostWithEntry(_tempDir, _sideChannel);
          HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");
          HostKickstart.EnsureLoaded();

          var paneType = HostKickstart.ResolveHostType("FakeHost.FakeDockablePane");

          paneType.Should().NotBeNull();
          paneType!.Assembly.GetName().Name.Should().Be("FakeHost");
          // Different load contexts mean different (logical) assemblies even
          // if the file is the same.
          System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(paneType.Assembly)
              .Should().NotBeSameAs(System.Runtime.Loader.AssemblyLoadContext.Default);
      }
  }
  ```

- [ ] **Step 3: Run, confirm RED**

  ```powershell
  dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --filter "FullyQualifiedName~HostKickstartTests"
  ```

  Expected: compile errors.

- [ ] **Step 4: Implement `HostKickstart.cs`**

  ```csharp
  using System;
  using System.IO;
  using System.Reflection;

  namespace Concord.Shim;

  /// <summary>
  /// Process-wide idempotent host bootstrap. First call from any shim
  /// [Export] activates the AssemblyLoadContext, loads
  /// Concord.Host{N}x.dll, and instantiates Host{N}xEntry once (which
  /// fires HostContext.Initialize, HostServices.Register, and
  /// ToolCatalog population — all via the host's static cctor + ctor).
  ///
  /// Subsequent calls are O(1) — return immediately.
  ///
  /// Thread safety: a process-wide lock guards EnsureLoaded. The shim's
  /// three [Export]s all call EnsureLoaded; only one will win the race;
  /// the other two block briefly and return.
  /// </summary>
  internal static class HostKickstart
  {
      private static readonly object _gate = new();
      private static volatile bool _loaded;
      private static ConcordHostLoadContext? _context;
      private static Assembly? _hostAssembly;

      // Testing seams. NEVER used in production — production resolves these
      // values from RuntimeHostLocator + the convention below.
      private static string? _testHostFolder;
      private static string? _testHostAssemblyName;
      private static string? _testEntryTypeName;

      public static void EnsureLoaded()
      {
          if (_loaded) return;
          lock (_gate)
          {
              if (_loaded) return;

              var (hostFolder, version) = ResolveHostFolder();
              if (!Directory.Exists(hostFolder))
                  throw new DirectoryNotFoundException(
                      $"Concord host folder not found: {hostFolder}. " +
                      $"Studio Pro reports version '{version}'. " +
                      $"The .mxmodule may be corrupted or partially installed.");

              ShimLog.Info($"HostKickstart: SP version='{version}', hostFolder={hostFolder}");

              var hostAssemblyName = ResolveHostAssemblyName(hostFolder);
              var hostDll = Path.Combine(hostFolder, hostAssemblyName + ".dll");

              _context = ShimLog.Timed("HostKickstart.BuildLoadContext",
                  () => new ConcordHostLoadContext(hostFolder));

              _hostAssembly = ShimLog.Timed("HostKickstart.LoadHostAssembly",
                  () => _context.LoadFromAssemblyPath(hostDll));

              // Resolve and instantiate Host{N}xEntry. Parameterless ctor —
              // the entry type's ImportingConstructor takes no MEF imports
              // (verified by reading both Host10xEntry.cs and Host11xEntry.cs).
              var entryTypeName = ResolveEntryTypeName(hostAssemblyName);
              var entryType = _hostAssembly.GetType(entryTypeName, throwOnError: true)!;

              ShimLog.Timed("HostKickstart.InstantiateEntry", () =>
              {
                  Activator.CreateInstance(entryType);
                  return 0;
              });

              _loaded = true;
          }
      }

      public static Type? ResolveHostType(string fullyQualifiedTypeName)
      {
          EnsureLoaded();
          return _hostAssembly!.GetType(fullyQualifiedTypeName);
      }

      /// <summary>
      /// Instantiates the named host type using positional args (typically
      /// the services captured by a shim [Export]'s [ImportingConstructor]).
      /// </summary>
      public static object CreateHostInstance(string fullyQualifiedTypeName, params object?[] ctorArgs)
      {
          var type = ResolveHostType(fullyQualifiedTypeName)
              ?? throw new InvalidOperationException(
                  $"Host type '{fullyQualifiedTypeName}' not found in {_hostAssembly?.GetName().Name}.");
          try
          {
              return Activator.CreateInstance(type, ctorArgs)!;
          }
          catch (Exception ex)
          {
              ShimLog.Error($"Failed to instantiate host type '{fullyQualifiedTypeName}'", ex);
              throw;
          }
      }

      private static (string hostFolder, string version) ResolveHostFolder()
      {
          if (_testHostFolder is not null)
              return (_testHostFolder, "<test>");
          return RuntimeHostLocator.ResolveBinDirectory();
      }

      private static string ResolveHostAssemblyName(string hostFolder)
      {
          if (_testHostAssemblyName is not null) return _testHostAssemblyName;
          // Convention: bin-10x/ contains Concord.Host10x.dll; bin-11x/ contains
          // Concord.Host11x.dll. Trust the folder name.
          var name = Path.GetFileName(hostFolder);
          return name switch
          {
              "bin-10x" => "Concord.Host10x",
              "bin-11x" => "Concord.Host11x",
              _ => throw new InvalidOperationException(
                  $"Unrecognized host folder name '{name}'; expected bin-10x or bin-11x.")
          };
      }

      private static string ResolveEntryTypeName(string hostAssemblyName)
      {
          if (_testEntryTypeName is not null) return _testEntryTypeName;
          return hostAssemblyName switch
          {
              "Concord.Host10x" => "Concord.Host10x.Host10xEntry",
              "Concord.Host11x" => "Concord.Host11x.Host11xEntry",
              _ => throw new InvalidOperationException($"No entry type mapping for {hostAssemblyName}.")
          };
      }

      // === Testing seams. Production callers never touch these. ===

      internal static void OverrideForTesting(string hostFolder, string hostAssemblyName, string entryTypeName)
      {
          _testHostFolder = hostFolder;
          _testHostAssemblyName = hostAssemblyName;
          _testEntryTypeName = entryTypeName;
      }

      internal static void ResetForTesting()
      {
          _loaded = false;
          _context?.Dispose();
          _context = null;
          _hostAssembly = null;
          _testHostFolder = null;
          _testHostAssemblyName = null;
          _testEntryTypeName = null;
      }
  }
  ```

- [ ] **Step 5: Run, confirm GREEN**

  ```powershell
  dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --filter "FullyQualifiedName~HostKickstartTests"
  ```

  Expected: 3/3 PASS.

### Task 3.2: TerminalPaneExtensionShim — TDD

**Files:** `src/Concord.Shim/Pane/TerminalPaneExtensionShim.cs`, `tests/Concord.Shim.Tests/ShimForwarderTests.cs` (partial — pane section), extension of `FakeHostBuilder` for a fake pane-style type with the right ctor shape.

The shim mirrors `TerminalPaneExtension`'s 9-arg ctor exactly. It captures the services in its own ctor, calls `HostKickstart.EnsureLoaded()`, then on each override forwards to the inner instance (lazily constructed on first override that needs it — typically `Open()`).

- [ ] **Step 1: Extend `FakeHostBuilder` for a 2-arg fake pane type**

  Add `EmitFakeHostWithPane` that emits a `FakeHost.FakePane : DockablePaneExtension` whose ctor takes 2 args: an `ILocalRunConfigurationsService` and an `IExtensionFileService` (smallest viable subset of the real 9-arg ctor — enough to validate the positional-passing pattern). Stores the args in fields exposed as `LocalRunConfigsMarker` (returns `localRunConfigs?.GetType().Name`) and `FileServiceMarker` properties.

- [ ] **Step 2: Write failing tests for the pane shim**

  Inside `tests/Concord.Shim.Tests/ShimForwarderTests.cs`:

  ```csharp
  using Concord.Shim.Pane;
  using Concord.Shim.Tests.Fakes;
  using FluentAssertions;
  using Mendix.StudioPro.ExtensionsAPI.Services;
  using Mendix.StudioPro.ExtensionsAPI.UI.Services;
  using NSubstitute;
  using Xunit;

  namespace Concord.Shim.Tests;

  [Collection("HostKickstart")]
  public class TerminalPaneExtensionShimTests : IDisposable
  {
      private readonly string _tempDir =
          Path.Combine(Path.GetTempPath(), "Concord.Shim.Tests", Guid.NewGuid().ToString("N"));

      public TerminalPaneExtensionShimTests()
      {
          HostKickstart.ResetForTesting();
          Directory.CreateDirectory(_tempDir);
      }

      public void Dispose()
      {
          HostKickstart.ResetForTesting();
          try { Directory.Delete(_tempDir, true); } catch { }
      }

      [Fact]
      public void Ctor_CapturesServices_PassesPositionallyToHostInstance()
      {
          var fakeRunConfigs = Substitute.For<ILocalRunConfigurationsService>();
          var fakeFileService = Substitute.For<IExtensionFileService>();
          var sideChannel = Path.Combine(_tempDir, "entry.log");

          var dll = FakeHostBuilder.EmitFakeHostWithPaneAndEntry(_tempDir, sideChannel);

          HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");

          var shim = new TerminalPaneExtensionShim(
              localRunConfigs: fakeRunConfigs,
              extensionFileService: fakeFileService,
              pageGenerationService: Substitute.For<IPageGenerationService>(),
              navigationManagerService: Substitute.For<INavigationManagerService>(),
              microflowService: Substitute.For<IMicroflowService>());

          // Override the host type name the shim looks up (production: "Concord.Host{N}x.Pane.TerminalPaneExtension").
          shim.TestOverrideInnerTypeName("FakeHost.FakePane");

          var inner = shim.EnsureInnerInstance();

          // The fake pane stores the type names of its ctor args; assert they
          // match the proxies we passed through.
          var markerProp = inner.GetType().GetProperty("LocalRunConfigsMarker")!;
          markerProp.GetValue(inner).Should().Be(fakeRunConfigs.GetType().Name);
      }
  }
  ```

  Note: introduce a single `NSubstitute` dep in the test csproj for service mocking. Add to `tests/Concord.Shim.Tests/Concord.Shim.Tests.csproj`:

  ```xml
  <PackageReference Include="NSubstitute" Version="5.1.0" />
  ```

- [ ] **Step 3: Run, confirm RED**

  Expected: compile errors.

- [ ] **Step 4: Implement `TerminalPaneExtensionShim.cs`**

  ```csharp
  using System.ComponentModel.Composition;
  using Mendix.StudioPro.ExtensionsAPI.Services;
  using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
  using Mendix.StudioPro.ExtensionsAPI.UI.Services;
  using MxVcsService = Mendix.StudioPro.ExtensionsAPI.UI.Services.IVersionControlService;

  namespace Concord.Shim.Pane;

  /// <summary>
  /// Shim forwarder for the host's TerminalPaneExtension. Mirrors the inner
  /// type's [ImportingConstructor] shape exactly so Studio Pro's MEF binds
  /// our imports the same way it would have bound the host's. Captures the
  /// services and passes them positionally to the inner host type via
  /// HostKickstart.CreateHostInstance.
  ///
  /// Inner instantiation is lazy — defers until the first override that
  /// needs it (Open) — so the load-context boundary cross is not paid at
  /// MEF activation time.
  ///
  /// Per OQ4 in the implementation plan: the inner host type's own
  /// [Export]/[ImportingConstructor] attributes remain in place but are
  /// vestigial under the shim. The shim drives instantiation; MEF inside
  /// the inner load context never runs.
  /// </summary>
  [Export(typeof(DockablePaneExtension))]
  public sealed class TerminalPaneExtensionShim : DockablePaneExtension
  {
      // ID must match the inner pane's ID — Studio Pro binds the dockable
      // pane registration to this string. Both production hosts use "Concord".
      public override string Id => "Concord";

      private readonly object?[] _capturedServices;
      private string _innerTypeNameOverride = ""; // testing seam

      [ImportingConstructor]
      public TerminalPaneExtensionShim(
          ILocalRunConfigurationsService localRunConfigs,
          IExtensionFileService extensionFileService,
          IPageGenerationService pageGenerationService,
          INavigationManagerService navigationManagerService,
          IMicroflowService microflowService,
          [Import(AllowDefault = true)] INameValidationService? nameValidationService = null,
          [Import(AllowDefault = true)] IUntypedModelAccessService? untypedModelAccessService = null,
          [Import(AllowDefault = true)] IMicroflowExpressionService? microflowExpressionService = null,
          [Import(AllowDefault = true)] MxVcsService? versionControlService = null)
      {
          // Static cctor in this same class ensures HostKickstart fires before
          // any instance member is touched. (Per Phase 0 finding §"Probe bugs"
          // item A — DO NOT use [ModuleInitializer]; unreliable on .NET 10.)
          _capturedServices = new object?[]
          {
              localRunConfigs,
              extensionFileService,
              pageGenerationService,
              navigationManagerService,
              microflowService,
              nameValidationService,
              untypedModelAccessService,
              microflowExpressionService,
              versionControlService,
          };
      }

      static TerminalPaneExtensionShim()
      {
          // Triggers load-context creation + Host{N}xEntry static init on
          // first activation. Idempotent thereafter.
          try { HostKickstart.EnsureLoaded(); }
          catch (Exception ex)
          {
              ShimLog.Error("HostKickstart.EnsureLoaded threw during pane shim cctor", ex);
              throw;
          }
      }

      private DockablePaneExtension? _inner;

      internal DockablePaneExtension EnsureInnerInstance()
      {
          if (_inner is not null) return _inner;
          var typeName = string.IsNullOrEmpty(_innerTypeNameOverride)
              ? ResolveInnerTypeName()
              : _innerTypeNameOverride;
          var instance = HostKickstart.CreateHostInstance(typeName, _capturedServices);
          _inner = (DockablePaneExtension)instance;
          return _inner;
      }

      internal void TestOverrideInnerTypeName(string name) => _innerTypeNameOverride = name;

      private static string ResolveInnerTypeName()
      {
          // Match the convention from HostKickstart.ResolveEntryTypeName:
          // production hosts deploy as Concord.Host{10,11}x with pane class
          // namespaced under Concord.Host{N}x.Pane.TerminalPaneExtension.
          // Detect which from the loaded host assembly's name.
          var entryAssemblyName = HostKickstart.ResolveHostType("Concord.Host10x.Host10xEntry") is not null
              ? "Concord.Host10x"
              : "Concord.Host11x";
          return $"{entryAssemblyName}.Pane.TerminalPaneExtension";
      }

      public override DockablePaneViewModelBase Open() => EnsureInnerInstance().Open();
  }
  ```

- [ ] **Step 5: Run, confirm GREEN**

  ```powershell
  dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --filter "FullyQualifiedName~TerminalPaneExtensionShimTests"
  ```

  Expected: 1/1 PASS.

### Task 3.3: ConcordMenuExtensionShim — TDD

**Files:** `src/Concord.Shim/Menu/ConcordMenuExtensionShim.cs`, `tests/Concord.Shim.Tests/ShimForwarderTests.cs` (menu section appended).

Same pattern as Task 3.2 but for the menu extension. 1-arg ctor (`IDockingWindowService`).

- [ ] **Step 1: Write failing test**

  Add to `ShimForwarderTests.cs`:

  ```csharp
  [Collection("HostKickstart")]
  public class ConcordMenuExtensionShimTests : IDisposable
  {
      // Same setup/teardown pattern as TerminalPaneExtensionShimTests.

      [Fact]
      public void Ctor_CapturesDocking_GetMenusDelegatesToInner()
      {
          var fakeDocking = Substitute.For<IDockingWindowService>();
          var sideChannel = Path.Combine(_tempDir, "entry.log");

          FakeHostBuilder.EmitFakeHostWithMenuAndEntry(_tempDir, sideChannel);
          HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");

          var shim = new ConcordMenuExtensionShim(fakeDocking);
          shim.TestOverrideInnerTypeName("FakeHost.FakeMenu");

          var menus = shim.GetMenus().ToList();

          // FakeMenu yields one MenuViewModel with caption "fake-caption".
          menus.Should().HaveCount(1);
          menus[0].Caption.Should().Be("fake-caption");
      }
  }
  ```

  Extend `FakeHostBuilder` with `EmitFakeHostWithMenuAndEntry` — adds a `FakeMenu : MenuExtension` whose ctor takes `IDockingWindowService` and whose `GetMenus()` yields one `MenuViewModel("fake-caption", () => { })`.

- [ ] **Step 2: Run, confirm RED**

- [ ] **Step 3: Implement `ConcordMenuExtensionShim.cs`**

  ```csharp
  using System.Collections.Generic;
  using System.ComponentModel.Composition;
  using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
  using Mendix.StudioPro.ExtensionsAPI.UI.Services;

  namespace Concord.Shim.Menu;

  [Export(typeof(MenuExtension))]
  public sealed class ConcordMenuExtensionShim : MenuExtension
  {
      private readonly IDockingWindowService _docking;
      private string _innerTypeNameOverride = "";
      private MenuExtension? _inner;

      [ImportingConstructor]
      public ConcordMenuExtensionShim(IDockingWindowService docking)
          => _docking = docking;

      static ConcordMenuExtensionShim()
      {
          try { HostKickstart.EnsureLoaded(); }
          catch (Exception ex)
          {
              ShimLog.Error("HostKickstart.EnsureLoaded threw during menu shim cctor", ex);
              throw;
          }
      }

      internal void TestOverrideInnerTypeName(string name) => _innerTypeNameOverride = name;

      private MenuExtension EnsureInner()
      {
          if (_inner is not null) return _inner;
          var typeName = string.IsNullOrEmpty(_innerTypeNameOverride)
              ? ResolveInnerTypeName()
              : _innerTypeNameOverride;
          _inner = (MenuExtension)HostKickstart.CreateHostInstance(typeName, _docking);
          return _inner;
      }

      private static string ResolveInnerTypeName()
      {
          var asmName = HostKickstart.ResolveHostType("Concord.Host10x.Host10xEntry") is not null
              ? "Concord.Host10x" : "Concord.Host11x";
          return $"{asmName}.MenuExtensions.TerminalMenuExtension";
      }

      public override IEnumerable<MenuViewModel> GetMenus() => EnsureInner().GetMenus();
  }
  ```

- [ ] **Step 4: Run, confirm GREEN**

### Task 3.4: TerminalWebServerShim — TDD

**Files:** `src/Concord.Shim/WebServer/TerminalWebServerShim.cs`, `tests/Concord.Shim.Tests/ShimForwarderTests.cs` (web-server section).

Same pattern. 1-arg ctor (`IExtensionFileService`).

- [ ] **Step 1: Write failing test**

  ```csharp
  [Collection("HostKickstart")]
  public class TerminalWebServerShimTests : IDisposable
  {
      [Fact]
      public void InitializeWebServer_DelegatesToInner()
      {
          var fakeFileService = Substitute.For<IExtensionFileService>();
          var fakeWebServer = Substitute.For<IWebServer>();
          var sideChannel = Path.Combine(_tempDir, "entry.log");

          FakeHostBuilder.EmitFakeHostWithWebServerAndEntry(_tempDir, sideChannel);
          HostKickstart.OverrideForTesting(_tempDir, "FakeHost", "FakeHost.FakeHostEntry");

          var shim = new TerminalWebServerShim(fakeFileService);
          shim.TestOverrideInnerTypeName("FakeHost.FakeWebServer");

          shim.InitializeWebServer(fakeWebServer);

          // FakeWebServer's InitializeWebServer calls webServer.AddRoute("fake", ...) once.
          fakeWebServer.Received(1).AddRoute("fake", Arg.Any<RouteAsyncDelegate>());
      }
  }
  ```

  Extend `FakeHostBuilder` with `EmitFakeHostWithWebServerAndEntry`.

- [ ] **Step 2: Run, confirm RED**

- [ ] **Step 3: Implement `TerminalWebServerShim.cs`**

  ```csharp
  using System.ComponentModel.Composition;
  using Mendix.StudioPro.ExtensionsAPI.Services;
  using Mendix.StudioPro.ExtensionsAPI.UI.WebServer;

  namespace Concord.Shim.WebServer;

  [Export(typeof(WebServerExtension))]
  public sealed class TerminalWebServerShim : WebServerExtension
  {
      private readonly IExtensionFileService _fileService;
      private string _innerTypeNameOverride = "";
      private WebServerExtension? _inner;

      [ImportingConstructor]
      public TerminalWebServerShim(IExtensionFileService fileService)
          => _fileService = fileService;

      static TerminalWebServerShim()
      {
          try { HostKickstart.EnsureLoaded(); }
          catch (Exception ex)
          {
              ShimLog.Error("HostKickstart.EnsureLoaded threw during webserver shim cctor", ex);
              throw;
          }
      }

      internal void TestOverrideInnerTypeName(string name) => _innerTypeNameOverride = name;

      private WebServerExtension EnsureInner()
      {
          if (_inner is not null) return _inner;
          var typeName = string.IsNullOrEmpty(_innerTypeNameOverride)
              ? ResolveInnerTypeName()
              : _innerTypeNameOverride;
          _inner = (WebServerExtension)HostKickstart.CreateHostInstance(typeName, _fileService);
          return _inner;
      }

      private static string ResolveInnerTypeName()
      {
          var asmName = HostKickstart.ResolveHostType("Concord.Host10x.Host10xEntry") is not null
              ? "Concord.Host10x" : "Concord.Host11x";
          return $"{asmName}.Ui.TerminalWebServer";
      }

      public override void InitializeWebServer(IWebServer webServer) => EnsureInner().InitializeWebServer(webServer);
  }
  ```

- [ ] **Step 4: Run, confirm GREEN**

### Task 3.5: Add `[shim-vestigial]` sentinel comments on the host MEF attributes

**Files:** 6 host source files. Comment-only — no code change. Pure documentation aid for future maintainers reading the host source and wondering why MEF attributes are present if Studio Pro only sees the shim.

For EACH of the following 6 files, find the `[Export]` line above the public class and the `[ImportingConstructor]` line above the public ctor; immediately above each, add the SAME ONE-LINE COMMENT:

```csharp
// [shim-vestigial] Studio Pro's MEF sees only Concord.Shim.dll under the
// runtime-shim architecture (Phase 0 spike — 2026-05-15). The attributes
// below remain so the host can still be built and tested in isolation, but
// at production runtime Concord.Shim's *Shim forwarders drive instantiation
// via reflection — these attributes are dead metadata. See
// docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
// §OQ4.
```

Files:

- `src/Concord.Host10x/Pane/TerminalPaneExtension.cs`
- `src/Concord.Host10x/MenuExtensions/TerminalMenuExtension.cs`
- `src/Concord.Host10x/Ui/TerminalWebServer.cs`
- `src/Concord.Host11x/Pane/TerminalPaneExtension.cs`
- `src/Concord.Host11x/MenuExtensions/TerminalMenuExtension.cs`
- `src/Concord.Host11x/Ui/TerminalWebServer.cs`

- [ ] **Step 1: Apply comment to all 6 files**

  Apply the comment block above EACH `[Export]` and EACH `[ImportingConstructor]` line in the 6 files above. (12 insertion points total.)

- [ ] **Step 2: Verify build is still green**

  ```powershell
  dotnet build Terminal.sln -p:Platform=x64
  ```

  Expected: clean. Comments are inert.

### Task 3.6: Commit Phase 3

- [ ] **Step 1: Run the full test suite**

  ```powershell
  dotnet test Terminal.sln
  ```

  Expected: Concord.Core.Tests 56/56, Terminal.Tests 273+/-flake, Concord.Shim.Tests ~12/12 PASS.

- [ ] **Step 2: Stage and commit**

  ```powershell
  git add src/Concord.Shim/Pane/ src/Concord.Shim/Menu/ src/Concord.Shim/WebServer/ `
          src/Concord.Shim/HostKickstart.cs `
          tests/Concord.Shim.Tests/ `
          src/Concord.Host10x/Pane/TerminalPaneExtension.cs `
          src/Concord.Host10x/MenuExtensions/TerminalMenuExtension.cs `
          src/Concord.Host10x/Ui/TerminalWebServer.cs `
          src/Concord.Host11x/Pane/TerminalPaneExtension.cs `
          src/Concord.Host11x/MenuExtensions/TerminalMenuExtension.cs `
          src/Concord.Host11x/Ui/TerminalWebServer.cs
  git commit -m @'
  phase 3: shim MEF forwarders + Host{N}xEntry bootstrap chain

  Implements the three production MEF exports plus HostKickstart, the
  idempotent inner-context bootstrap. Each *Shim forwarder declares the
  same [ImportingConstructor] shape as its inner host counterpart, captures
  the services in its ctor, and on first override forwards to the inner
  instance via Activator.CreateInstance with positional args.

  This is Option A from the plan's §OQ4 resolution: shim relays its own
  MEF imports across the load-context boundary, rather than standing up a
  second MEF container inside the inner context. Type identity holds across
  the boundary because every service interface lives in
  Mendix.StudioPro.ExtensionsAPI, which ConcordHostLoadContext.Resolving
  forwards to the default context (proven by Phase 0 Q2).

  Bootstrap chain: TerminalPaneExtensionShim's static cctor calls
  HostKickstart.EnsureLoaded, which builds the load context, loads
  Concord.Host{N}x.dll, and instantiates Host{N}xEntry once to fire
  HostContext.Initialize / HostServices.Register / ToolCatalog population.
  The host's own [Import(typeof(Host{N}xEntry))] sentinel fields on
  pane/web-server stay null (no inner MEF container runs) — per existing
  CS0414 sentinel comment, they're never read by host code.

  Adds [shim-vestigial] sentinel comments above the 6 host [Export] sites
  + 6 [ImportingConstructor] sites to document that MEF attributes there
  are dead metadata under the shim architecture.

  Tests use Roslyn-emitted FakeHost variants (with parameterless,
  pane-shaped, menu-shaped, and web-server-shaped ctors) plus NSubstitute
  mocks for the Studio Pro service interfaces.

  Refs: docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
  '@
  ```

---

## Phase 4: MSBuild merge target + Core hash gate + unified deploy

**Goal:** Build pipeline produces the merged `extensions/Concord/` layout in `bin/x64/$(Configuration)/net8.0-merged/`. SHA-256 hash gate prevents `Concord.Core.dll` drift between top-level and `bin-{Nx}/` copies. New unified deploy target reads `$(MendixDeployTargetMerged)` from `Directory.Build.props` and deploys the merged layout to a Mendix project's `extensions/Concord/`. The two per-host deploy targets stay (gated on their respective env props) for component-level iteration.

### Task 4.1: Externalize BuildUi into a shared targets file

**Files:** `src/build/BuildUi.targets` (new), `src/Concord.Host11x/Concord.Host11x.csproj` (modify).

Currently `BuildUi` lives in `Concord.Host11x.csproj` and fires `BeforeBuild` to bundle the UI via esbuild. Under the shim architecture, the merged output's `wwwroot/` lives at the top level (not under either `bin-{Nx}/`). To make sure the wwwroot bundle exists when the merge step runs, move `BuildUi` out to a shared targets file that `Concord.Shim.csproj` imports — that way the shim build pulls in the bundle regardless of which host built last.

- [ ] **Step 1: Create `src/build/BuildUi.targets`**

  ```xml
  <Project>
    <Target Name="BuildUi" BeforeTargets="BeforeBuild"
            Inputs="$(MSBuildThisFileDirectory)..\..\ui\src\**\*;$(MSBuildThisFileDirectory)..\..\ui\package.json;$(MSBuildThisFileDirectory)..\..\ui\esbuild.mjs;$(MSBuildThisFileDirectory)..\..\ui\index.html"
            Outputs="$(MSBuildThisFileDirectory)..\..\wwwroot\terminal.bundle.js">
      <Message Importance="high" Text="Installing UI dependencies (first build only)…" Condition="!Exists('$(MSBuildThisFileDirectory)..\..\ui\node_modules')" />
      <Exec Command="npm install --prefix &quot;$(MSBuildThisFileDirectory)..\..\ui&quot; --silent" Condition="!Exists('$(MSBuildThisFileDirectory)..\..\ui\node_modules')" />
      <Message Importance="high" Text="Bundling UI…" />
      <Exec Command="node &quot;$(MSBuildThisFileDirectory)..\..\ui\esbuild.mjs&quot;" />
    </Target>
  </Project>
  ```

- [ ] **Step 2: Remove `BuildUi` from `Concord.Host11x.csproj`**

  Delete lines 31–38 of [src/Concord.Host11x/Concord.Host11x.csproj](../../../src/Concord.Host11x/Concord.Host11x.csproj#L31-L38).

- [ ] **Step 3: Import the shared target from `Concord.Shim.csproj`**

  Add to `src/Concord.Shim/Concord.Shim.csproj` (after the `<ItemGroup>` blocks):

  ```xml
  <Import Project="..\build\BuildUi.targets" />
  ```

- [ ] **Step 4: Verify build still produces `wwwroot/terminal.bundle.js`**

  ```powershell
  Remove-Item -Recurse -Force wwwroot\terminal.bundle.js -ErrorAction Ignore
  dotnet build src\Concord.Shim\Concord.Shim.csproj -p:Platform=x64
  Test-Path wwwroot\terminal.bundle.js
  ```

  Expected: True.

### Task 4.2: `MergeHostsForShim` target — produces merged layout + hash gate

**Files:** `src/build/MergeHostsForShim.targets`, `src/Concord.Shim/Concord.Shim.csproj` (import).

The merge target:

1. Runs `AfterTargets="Build"` on the shim project (so both hosts must have built first — enforced via `ProjectReference`).
2. Outputs to `$(MSBuildThisFileDirectory)..\..\bin\x64\$(Configuration)\net8.0-merged\`.
3. Copies `Concord.Shim.dll` + dependencies + `manifest.json` to the root of `net8.0-merged/`.
4. Creates `net8.0-merged/bin-10x/` and copies `Concord.Host10x/bin/.../*` into it.
5. Creates `net8.0-merged/bin-11x/` and copies `Concord.Host11x/bin/.../*` into it.
6. Hoists shared content (`wwwroot/`, `skills/`, `skills-10x/`, `skills-mac/`, `rules/`, `rules-10x/`) to the top level of `net8.0-merged/`.
7. Deletes hoisted duplicates from inside each `bin-{Nx}/` (since they're now at root).
8. SHA-256 hashes `Concord.Core.dll` at root + in each `bin-{Nx}/`. Three hashes; all three must match. Build failure otherwise.

- [ ] **Step 1: Add `ProjectReference`s to both hosts in the shim csproj**

  Edit `src/Concord.Shim/Concord.Shim.csproj` — add inside the existing `<ItemGroup>` that has the Concord.Core reference:

  ```xml
  <ProjectReference Include="..\Concord.Host10x\Concord.Host10x.csproj">
    <Private>false</Private>
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
  </ProjectReference>
  <ProjectReference Include="..\Concord.Host11x\Concord.Host11x.csproj">
    <Private>false</Private>
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
  </ProjectReference>
  ```

  `<Private>false</Private>` and `<ReferenceOutputAssembly>false</ReferenceOutputAssembly>` together mean: build the host projects first (for ordering), but do NOT pull their outputs into the shim's own `bin/`. The merge target handles the copy explicitly.

- [ ] **Step 2: Create `src/build/MergeHostsForShim.targets`**

  ```xml
  <Project>
    <PropertyGroup>
      <_MergedRoot>$(MSBuildThisFileDirectory)..\..\bin\x64\$(Configuration)\net8.0-merged</_MergedRoot>
      <_Host10xBin>$(MSBuildThisFileDirectory)..\Concord.Host10x\bin\$(Configuration)\net8.0</_Host10xBin>
      <_Host11xBin>$(MSBuildThisFileDirectory)..\Concord.Host11x\bin\$(Configuration)\net8.0</_Host11xBin>
      <_ShimBin>$(MSBuildThisFileDirectory)..\Concord.Shim\bin\$(Configuration)\net8.0</_ShimBin>
    </PropertyGroup>

    <Target Name="MergeHostsForShim" AfterTargets="Build">
      <Message Importance="high" Text="MergeHostsForShim: producing $(_MergedRoot)" />

      <!-- 1. Clean output. -->
      <RemoveDir Directories="$(_MergedRoot)" />
      <MakeDir Directories="$(_MergedRoot)" />

      <!-- 2. Copy shim output to root of merged folder. -->
      <ItemGroup>
        <_ShimFiles Include="$(_ShimBin)\**\*" />
      </ItemGroup>
      <Copy SourceFiles="@(_ShimFiles)"
            DestinationFiles="@(_ShimFiles->'$(_MergedRoot)\%(RecursiveDir)%(Filename)%(Extension)')"
            SkipUnchangedFiles="true" />

      <!-- 3a. Copy Host10x to bin-10x/. -->
      <MakeDir Directories="$(_MergedRoot)\bin-10x" />
      <ItemGroup>
        <_Host10xFiles Include="$(_Host10xBin)\**\*" />
      </ItemGroup>
      <Copy SourceFiles="@(_Host10xFiles)"
            DestinationFiles="@(_Host10xFiles->'$(_MergedRoot)\bin-10x\%(RecursiveDir)%(Filename)%(Extension)')"
            SkipUnchangedFiles="true" />

      <!-- 3b. Copy Host11x to bin-11x/. -->
      <MakeDir Directories="$(_MergedRoot)\bin-11x" />
      <ItemGroup>
        <_Host11xFiles Include="$(_Host11xBin)\**\*" />
      </ItemGroup>
      <Copy SourceFiles="@(_Host11xFiles)"
            DestinationFiles="@(_Host11xFiles->'$(_MergedRoot)\bin-11x\%(RecursiveDir)%(Filename)%(Extension)')"
            SkipUnchangedFiles="true" />

      <!-- 4. Hoist shared content (wwwroot, skills*, rules*) and delete dupes from bin-{Nx}/. -->
      <!-- Each host's bin currently ships these by copying from repo root via Content links;
           after copy, delete the per-host copies so we have a single canonical top-level copy. -->
      <ItemGroup>
        <_HoistableDirs Include="wwwroot;skills;skills-10x;skills-mac;rules;rules-10x" />
      </ItemGroup>
      <RemoveDir Directories="@(_HoistableDirs->'$(_MergedRoot)\bin-10x\%(Identity)')" Condition="Exists('$(_MergedRoot)\bin-10x\%(_HoistableDirs.Identity)')" />
      <RemoveDir Directories="@(_HoistableDirs->'$(_MergedRoot)\bin-11x\%(Identity)')" Condition="Exists('$(_MergedRoot)\bin-11x\%(_HoistableDirs.Identity)')" />

      <!-- The shim's own bin already has wwwroot/skills/rules (they're shared
           at repo root and linked into all 3 csprojs as Content). Verify
           each hoisted dir is present at $(_MergedRoot)\<dir>\. -->
      <Error Condition="!Exists('$(_MergedRoot)\wwwroot')" Text="MergeHostsForShim: wwwroot not present at $(_MergedRoot)\wwwroot — UI bundle missing." />
      <Error Condition="!Exists('$(_MergedRoot)\skills')" Text="MergeHostsForShim: skills/ not present at top level." />
      <Error Condition="!Exists('$(_MergedRoot)\rules')" Text="MergeHostsForShim: rules/ not present at top level." />

      <!-- 5. Hash gate: Concord.Core.dll must be byte-identical across all 3 locations. -->
      <GetFileHash Files="$(_MergedRoot)\Concord.Core.dll" Algorithm="SHA256">
        <Output TaskParameter="Hash" PropertyName="_RootCoreHash" />
      </GetFileHash>
      <GetFileHash Files="$(_MergedRoot)\bin-10x\Concord.Core.dll" Algorithm="SHA256">
        <Output TaskParameter="Hash" PropertyName="_Bin10xCoreHash" />
      </GetFileHash>
      <GetFileHash Files="$(_MergedRoot)\bin-11x\Concord.Core.dll" Algorithm="SHA256">
        <Output TaskParameter="Hash" PropertyName="_Bin11xCoreHash" />
      </GetFileHash>

      <Error Condition="'$(_RootCoreHash)' != '$(_Bin10xCoreHash)'"
             Text="BUILD FAILED — Concord.Core.dll hash mismatch: root=$(_RootCoreHash) vs bin-10x=$(_Bin10xCoreHash). This would break HostServices static state. Verify both hosts build against the same Concord.Core source." />
      <Error Condition="'$(_RootCoreHash)' != '$(_Bin11xCoreHash)'"
             Text="BUILD FAILED — Concord.Core.dll hash mismatch: root=$(_RootCoreHash) vs bin-11x=$(_Bin11xCoreHash). This would break HostServices static state. Verify both hosts build against the same Concord.Core source." />

      <Message Importance="high" Text="MergeHostsForShim: Concord.Core.dll SHA256=$(_RootCoreHash) verified across all 3 locations." />
    </Target>
  </Project>
  ```

- [ ] **Step 3: Import the merge target from the shim csproj**

  Add to `src/Concord.Shim/Concord.Shim.csproj`:

  ```xml
  <Import Project="..\build\MergeHostsForShim.targets" />
  ```

- [ ] **Step 4: Verify the merge runs**

  ```powershell
  dotnet build src\Concord.Shim\Concord.Shim.csproj -p:Platform=x64 -v:m
  ```

  Expected: build completes; `bin\x64\Debug\net8.0-merged\` exists; contains `Concord.Shim.dll`, `Concord.Core.dll`, `manifest.json`, `wwwroot\`, `skills\`, `rules\`, `bin-10x\Concord.Host10x.dll`, `bin-11x\Concord.Host11x.dll`; "Concord.Core.dll SHA256=... verified across all 3 locations" message.

- [ ] **Step 5: Smoke the hash gate by introducing a deliberate divergence and confirming build fails**

  ```powershell
  # Sanity check the gate fires when divergence is present.
  Copy-Item bin\x64\Debug\net8.0-merged\bin-10x\Concord.Core.dll bin\x64\Debug\net8.0-merged\Concord.Core.dll.bak
  [System.IO.File]::WriteAllBytes("bin\x64\Debug\net8.0-merged\bin-10x\Concord.Core.dll", [byte[]](1,2,3,4))
  # Don't trigger a rebuild — call the target directly:
  dotnet msbuild src\Concord.Shim\Concord.Shim.csproj -t:MergeHostsForShim -p:Platform=x64
  # Restore:
  Move-Item -Force bin\x64\Debug\net8.0-merged\Concord.Core.dll.bak bin\x64\Debug\net8.0-merged\bin-10x\Concord.Core.dll
  ```

  Expected: msbuild call fails with the "BUILD FAILED — Concord.Core.dll hash mismatch" error. If it does NOT fail, the gate is misconfigured — fix and retry.

### Task 4.3: Unified deploy target

**Files:** `src/build/DeployMergedToMendix.targets` (new), `src/Concord.Shim/Concord.Shim.csproj` (import), `Directory.Build.props` (add property).

The new deploy target reads `$(MendixDeployTargetMerged)` (per-developer override), falls back to `$(MendixDeployTarget)`, and deploys `bin/x64/.../net8.0-merged/` to `<target>/extensions/Concord/`. Refreshes any existing cache snapshot.

Per-host deploys stay in place (Phase 4 does NOT delete them) — they remain useful for component-level iteration. Gating on their own per-host env vars means setting only `$(MendixDeployTargetMerged)` deploys the merged shim; setting only `$(MendixDeployTarget10x)` deploys just the 10x host (for dev iteration on host code without the shim cycle).

- [ ] **Step 1: Add property to `Directory.Build.props`**

  In the same `<PropertyGroup>` that has `<ExtensionsApiShimBaselineVersion>`:

  ```xml
  <!-- Per-developer; set this to the path of your Mendix project root (e.g.
       C:\Workspace\MendixApps\TestOSApp3) to enable the shim deploy. Falls
       back to $(MendixDeployTarget) if unset. -->
  <MendixDeployTargetMerged Condition="'$(MendixDeployTargetMerged)' == ''">$(MendixDeployTargetMerged)</MendixDeployTargetMerged>
  ```

- [ ] **Step 2: Create `src/build/DeployMergedToMendix.targets`**

  ```xml
  <Project>
    <PropertyGroup>
      <_MergedDeployTarget Condition="'$(MendixDeployTargetMerged)' != ''">$(MendixDeployTargetMerged)</_MergedDeployTarget>
      <_MergedDeployTarget Condition="'$(_MergedDeployTarget)' == ''">$(MendixDeployTarget)</_MergedDeployTarget>
    </PropertyGroup>

    <Target Name="DeployMergedToMendix" AfterTargets="MergeHostsForShim" Condition="'$(_MergedDeployTarget)' != ''">
      <ItemGroup>
        <_DeployTargets Include="$(_MergedDeployTarget)" />
      </ItemGroup>
      <Message Importance="high" Text="Deploying merged shim to %(_DeployTargets.Identity)/extensions/Concord" />

      <!-- Windows: xcopy. POSIX: cp -R. -->
      <Exec Condition="'$(OS)' == 'Windows_NT'"
            Command="if exist &quot;%(_DeployTargets.Identity)\extensions\Concord&quot; rd /s /q &quot;%(_DeployTargets.Identity)\extensions\Concord&quot;" />
      <Exec Condition="'$(OS)' == 'Windows_NT'"
            Command="xcopy /y /s /i /q &quot;$(_MergedRoot)\*&quot; &quot;%(_DeployTargets.Identity)\extensions\Concord&quot;" />
      <Exec Condition="'$(OS)' != 'Windows_NT'"
            Command="rm -rf &quot;%(_DeployTargets.Identity)/extensions/Concord&quot; &amp;&amp; mkdir -p &quot;%(_DeployTargets.Identity)/extensions/Concord&quot; &amp;&amp; cp -R &quot;$(_MergedRoot)/.&quot; &quot;%(_DeployTargets.Identity)/extensions/Concord/&quot;" />

      <!-- Refresh the Studio Pro per-project cache snapshot if any exists.
           Snapshot subdirs are named with a GUID; identify ours by the
           presence of Concord.Shim.dll. -->
      <Exec Condition="'$(OS)' == 'Windows_NT'"
            IgnoreExitCode="true"
            Command="for /d %%d in (&quot;%(_DeployTargets.Identity)\.mendix-cache\extensions-cache\*&quot;) do if exist &quot;%%d\Concord.Shim.dll&quot; xcopy /y /s /i /q &quot;$(_MergedRoot)\*&quot; &quot;%%d&quot;" />
      <Exec Condition="'$(OS)' != 'Windows_NT'"
            IgnoreExitCode="true"
            Command="for d in &quot;%(_DeployTargets.Identity)/.mendix-cache/extensions-cache/&quot;*/ ; do if [ -f &quot;$d/Concord.Shim.dll&quot; ]; then cp -R &quot;$(_MergedRoot)/.&quot; &quot;$d&quot;; echo &quot;Refreshed cache: $d&quot;; fi; done" />
    </Target>
  </Project>
  ```

- [ ] **Step 3: Import the deploy target from the shim csproj**

  ```xml
  <Import Project="..\build\DeployMergedToMendix.targets" />
  ```

- [ ] **Step 4: Add the stale-comment fix (per Phase 0 deferred item)**

  Edit [src/Concord.Host10x/Concord.Host10x.csproj](../../../src/Concord.Host10x/Concord.Host10x.csproj#L32-L34) lines 31–34 — replace the "MEF skips" paragraph with:

  ```xml
  <!-- Two-extension layout: Host10x deploys to extensions/Concord10x/; Host11x to
       extensions/Concord11x/. Each is a separate Studio Pro extension with its own
       single-DLL manifest. Required because Studio Pro 10.24.13's extension loader
       rejects multi-DLL manifests (calls .Single() during hashing — System.Linq
       InvalidOperationException). The two-folder layout duplicates Concord.Core.dll
       and the shared assets but is the only structure that loads on both versions
       *when deployed independently*.

       NOTE: Under the runtime-shim architecture introduced in v5.1.0 (see
       src/Concord.Shim/), production deploys ship a single extensions/Concord/
       folder via DeployMergedToMendix. These per-host DeployToMendix targets
       remain for component-level dev iteration ONLY.

       On Studio Pro 10.x: deploying Concord11x/ alongside Concord10x/ crashes
       Studio Pro (type-resolution failure inside the loader during MEF discovery;
       verified by spike 2026-05-12, commit a0ce567). -->
  ```

- [ ] **Step 5: Verify a no-op build (no deploy target set) works**

  ```powershell
  dotnet build Terminal.sln -p:Platform=x64
  ```

  Expected: clean. Merge target runs; no deploy fires because `$(MendixDeployTargetMerged)` is unset in CI.

### Task 4.4: Commit Phase 4

- [ ] **Step 1: Run all tests**

  ```powershell
  dotnet test Terminal.sln
  ```

  Expected: all green.

- [ ] **Step 2: Stage and commit**

  ```powershell
  git add src/build/ src/Concord.Shim/Concord.Shim.csproj `
          src/Concord.Host10x/Concord.Host10x.csproj `
          src/Concord.Host11x/Concord.Host11x.csproj `
          Directory.Build.props
  git commit -m @'
  phase 4: MergeHostsForShim + Concord.Core hash gate + unified deploy

  Adds the build pipeline that produces bin/x64/$(Configuration)/net8.0-merged/
  — the layout that goes into ConcordPublisher's bundled resources for
  .mxmodule export. Layout:

      net8.0-merged/
        Concord.Shim.dll, Concord.Core.dll, manifest.json, wwwroot/, skills/, rules/
        bin-10x/Concord.Host10x.dll + Concord.Core.dll + ExtensionsAPI 10.21.1 …
        bin-11x/Concord.Host11x.dll + Concord.Core.dll + ExtensionsAPI 11.6.2 …

  SHA-256 hash gate verifies Concord.Core.dll is byte-identical across the
  3 copies — drift would split HostServices/HostContext static state.

  Externalizes BuildUi out of Concord.Host11x.csproj into a shared
  src/build/BuildUi.targets that Concord.Shim.csproj imports, so the
  wwwroot bundle exists at merge time regardless of build order.

  New deploy target DeployMergedToMendix reads $(MendixDeployTargetMerged)
  from Directory.Build.props (per-dev) and deploys net8.0-merged/ to
  <target>/extensions/Concord/, refreshing any existing cache snapshot
  identified by the presence of Concord.Shim.dll. The per-host deploy
  targets stay in place (gated on $(MendixDeployTarget{10,11}x)) for
  component-level iteration.

  Fixes the stale "MEF skips" comment on Concord.Host10x.csproj per the
  Phase 0 findings deferred item.

  Refs: docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
  '@
  ```

---

## Phase 5: Manual Studio Pro smoke matrix

**Goal:** Validate the unified `.mxmodule` on both Studio Pro versions end-to-end. Capture pane-open latency for the perf gate (OQ1). Produce a smoke-results handoff doc that the PR review can cite.

This phase is **gated on Ricardo's manual Studio Pro UI step** (the .mxmodule export click-through). The agent's part: produce the merged layout, instrument timing, document smoke procedure, and process results into the handoff doc.

### Task 5.1: Verify perf instrumentation captures all 5 measurable steps

**Files:** None — this is a verification task against `ShimLog.Timed` already added in Phase 3.

- [ ] **Step 1: Grep for `Timed` usage in the shim**

  ```powershell
  Select-String -Path src\Concord.Shim\*.cs -Pattern "ShimLog\.Timed"
  ```

  Expected: 3 hits in `HostKickstart.cs` — `BuildLoadContext`, `LoadHostAssembly`, `InstantiateEntry`. If any are missing, add them now.

- [ ] **Step 2: Add timing around the two remaining steps (host-folder resolution + first-export instantiation)**

  In `HostKickstart.EnsureLoaded`, wrap `ResolveHostFolder()`:

  ```csharp
  var (hostFolder, version) = ShimLog.Timed("HostKickstart.ResolveHostFolder",
      () => ResolveHostFolder());
  ```

  In `TerminalPaneExtensionShim.EnsureInnerInstance()`, wrap the `CreateHostInstance` call:

  ```csharp
  var instance = ShimLog.Timed("PaneShim.CreateInner",
      () => HostKickstart.CreateHostInstance(typeName, _capturedServices));
  ```

- [ ] **Step 3: Rebuild, confirm no regressions**

  ```powershell
  dotnet build src\Concord.Shim\Concord.Shim.csproj -p:Platform=x64
  dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj
  ```

  Expected: green.

### Task 5.2: Build the merged layout for smoke

- [ ] **Step 1: Clean build**

  ```powershell
  dotnet clean Terminal.sln -p:Platform=x64
  dotnet build Terminal.sln -p:Platform=x64
  ```

- [ ] **Step 2: Verify merged layout**

  ```powershell
  Get-ChildItem bin\x64\Debug\net8.0-merged -Recurse | Select-Object FullName
  ```

  Expected: top-level files `Concord.Shim.dll`, `Concord.Core.dll`, `manifest.json`; top-level dirs `wwwroot`, `skills`, `rules`; `bin-10x/` and `bin-11x/` each with their host DLL + bin contents.

### Task 5.3: Ricardo's manual .mxmodule export step

**Ricardo's role:** Open `C:\Workspace\MendixApps\ConcordPublisher` in Studio Pro 11.x. Use the Studio Pro UI step documented in [reference_concord_mxmodule_build.md](../../../../../.claude/projects/c--Extensions-Terminal/memory/reference_concord_mxmodule_build.md) (memory) to export the `.mxmodule` with `bin/x64/Debug/net8.0-merged/` as the bundled resources source.

- [ ] **Step 1: Hand off to Ricardo**

  Comment in `_HANDOFF.md` or via session: "Phase 5 ready — run the .mxmodule export step against `bin/x64/Debug/net8.0-merged/`. Then return the .mxmodule path for the smoke install."

- [ ] **Step 2: After Ricardo returns the .mxmodule** — agent picks back up at Task 5.4.

### Task 5.4: Smoke matrix — Studio Pro 10.24.13

**Test project:** `C:\Projects\Test_10_24_13` (per `_HANDOFF.md` setup).

- [ ] **Step 0: Capture v5.0.0 baseline pane-open latency (one-time)**

  Before installing the new shim build, deploy the CURRENT (v5.0.0 two-folder) Concord into the test project and time the pane open. This is the reference for the perf gate. Skip this step if a baseline is already recorded in [`_HANDOFF.md`](../../../../.claude/projects/c--Extensions-Terminal/memory/_HANDOFF.md) or a prior smoke handoff.

  ```powershell
  $TEST = "C:\Projects\Test_10_24_13"
  # Deploy current v5.0.0 two-folder layout from the last known-good build.
  # (If main is already at the shim branch, check out the previous main commit
  # before this branch, build, deploy, measure, then return.)
  git stash
  git checkout 09a2e41   # the merge commit of feat/v5.0.0-w2-mcpx-merge
  dotnet build src\Concord.Host10x\Concord.Host10x.csproj -p:Platform=x64 -p:MendixDeployTarget10x=$TEST
  # Reset cache; launch Studio Pro 10.24.13 against $TEST; open the pane;
  # time from manifest load (first log line in Concord's logger) to first
  # WebView render. Record as v5.0.0-SP10 baseline.
  git checkout feat/v5.1.0-runtime-shim
  git stash pop
  ```

  Record the baseline in the smoke-results handoff (Task 5.6) under "v5.0.0 reference baseline".

- [ ] **Step 1: Reset the test project's extension state**

  ```powershell
  $TEST = "C:\Projects\Test_10_24_13"
  Remove-Item -Recurse -Force "$TEST\extensions\Concord", "$TEST\extensions\Concord10x", "$TEST\extensions\Concord11x", "$TEST\.mendix-cache\extensions-cache" -ErrorAction Ignore
  Remove-Item -Force "$env:TEMP\Concord\shim.log" -ErrorAction Ignore
  ```

- [ ] **Step 2: Install the .mxmodule via Studio Pro 10.x UI**

  Ricardo's manual step: open Studio Pro 10.24.13 against `$TEST`, install the .mxmodule. (Per Studio Pro 10.x requirement, launch with `--enable-extension-development`.)

- [ ] **Step 3: Wait for Studio Pro to fully load (project tree visible, no progress indicators), then collect evidence**

  ```powershell
  $TEST = "C:\Projects\Test_10_24_13"
  # Cache snapshot folder count + contents:
  Get-ChildItem "$TEST\.mendix-cache\extensions-cache" | Select-Object Name, LastWriteTime
  # Shim log:
  Get-Content "$env:TEMP\Concord\shim.log"
  ```

  Expected:
  - Exactly ONE cache snapshot folder (corresponding to `extensions/Concord`), no separate Concord10x/Concord11x snapshots.
  - Shim log contains `HostKickstart: SP version='10.24.13...'`, `BinFolderName=bin-10x`, then the 5 `Timed` lines.
  - No `ERROR` entries.

- [ ] **Step 4: Open the Concord pane (Ricardo: Extensions menu → Concord → Open Pane)**

  Confirm pane opens, terminal initializes (xterm.js renders, prompt visible), the about button shows v5.1.0.

  Capture pane-open latency: subtract the `ResolveHostFolder` timestamp from the `PaneShim.CreateInner` timestamp.

- [ ] **Step 5: Run one tool-call round-trip (smoke MCP server)**

  In the terminal: `curl http://127.0.0.1:7783/save_all` (or whatever the concord-mcp endpoint is on SP10). Expected: 200 OK; project saves; no shim-log errors.

### Task 5.5: Smoke matrix — Studio Pro 11.10

Mirror of Task 5.4, against `C:\Projects\Test_11_10`. No `--enable-extension-development` flag needed (per `_HANDOFF.md`). Same evidence to capture.

### Task 5.6: Compose the smoke-results handoff

**Files:** `docs/superpowers/handoffs/2026-05-XX-concord-shim-smoke-results.md` (date = actual completion date).

- [ ] **Step 1: Write the handoff**

  Structure mirrors the Phase 0 findings doc:

  ```markdown
  ---
  name: concord-shim-smoke-results
  description: Phase 5 manual smoke validation of the runtime-shim implementation on Studio Pro 10.24.13 + 11.10. Captures pane-open latency vs v5.0.0 baseline, asserts no regressions, and feeds into the v5.1.0 PR.
  metadata:
    node_type: memory
    type: project
    originSessionId: <fill in>
  ---

  # Concord runtime-shim smoke results (Phase 5)

  ## TL;DR

  - SP 10.24.13: <PASS/FAIL>, pane open <Xms>, delta vs v5.0.0 baseline <±Yms>
  - SP 11.10: <PASS/FAIL>, pane open <Xms>, delta vs v5.0.0 baseline <±Yms>
  - Perf gate (OQ1 — ≤500ms regression): <PASS/FAIL>
  - Recommendation: <PROCEED to PR / STOP and tune perf / regression investigation>

  ## SP 10.24.13 evidence

  ### Shim log
  ``` (paste relevant log section) ```

  ### Pane-open latency
  - Total: Xms (from manifest load to first WebView render)
  - HostKickstart.ResolveHostFolder: Xms
  - HostKickstart.BuildLoadContext: Xms
  - HostKickstart.LoadHostAssembly: Xms
  - HostKickstart.InstantiateEntry: Xms
  - PaneShim.CreateInner: Xms

  ### Tool round-trip
  - Endpoint: <e.g. save_all>
  - Status: 200 OK / FAIL
  - Latency: Xms

  ## SP 11.10 evidence
  (same structure)

  ## Outcomes against the 5 open questions

  - **OQ1 Performance:** <PASS/FAIL>
  - **OQ2 SP 12.x compat:** Deferred, tracking entry added to _HANDOFF.md backlog.
  - **OQ3 Mac variant:** Deferred, Ricardo's fast-follow post-merge.
  - **OQ4 MEF imports:** Empirically confirmed — the pane opened, the 5 required + 4 optional services were forwarded successfully, no NullReferenceException at the service call sites.
  - **OQ5 Concord.Core hash gate:** Built green; gate fired in Task 4.2 step 5 when deliberately diverged.

  ## Issues found / deferred follow-ups
  <fill in based on actual smoke>
  ```

- [ ] **Step 2: Commit**

  ```powershell
  git add docs/superpowers/handoffs/2026-05-XX-concord-shim-smoke-results.md `
          src/Concord.Shim/HostKickstart.cs `
          src/Concord.Shim/Pane/TerminalPaneExtensionShim.cs
  git commit -m @'
  phase 5: manual Studio Pro smoke matrix — both versions PASS

  Validates the merged .mxmodule end-to-end on Studio Pro 10.24.13 + 11.10.
  Pane opens, action server starts, tool round-trip succeeds, no errors in
  shim.log. Pane-open latency: SP10 = <X>ms (delta +<Y>ms vs v5.0.0), SP11 =
  <X>ms (delta +<Y>ms). Within the ≤500ms perf gate.

  Adds Timed wrappers around the two remaining measurable bootstrap steps
  (ResolveHostFolder + PaneShim.CreateInner) so the perf matrix in the
  smoke-results handoff is complete.

  Refs: docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
  Findings: docs/superpowers/handoffs/2026-05-XX-concord-shim-smoke-results.md
  '@
  ```

### Task 5.7: Perf-gate decision

- [ ] **If pane-open regression ≤500ms on both versions:** PROCEED to Phase 6.

- [ ] **If pane-open regression >500ms on either version:** STOP. Add a sub-phase 5b before Phase 6:
  - Identify the slowest `Timed` step from the smoke log.
  - Optimize (likely candidates: cache the version probe at process scope; pre-warm the load context on a thread; eliminate redundant reflection lookups).
  - Re-run Tasks 5.4–5.5.

---

## Phase 6: Retire two-extension consumer surface (docs + marketing)

**Goal:** All consumer-facing documentation says "single `extensions/Concord/` drop-in" — no version fork. Marketing surfaces (4 — MD + HTML × 2) stay in sync. CLAUDE.md key paths reflect the new layout. CHANGELOG.md v5.1.0 entry is authored.

**Important:** per CLAUDE.md, the four marketing surfaces are coupled. Updating one without the others is a documented past-incident bug (v4.2.1 cycle missed `marketplace-overview.html`).

### Task 6.1: DEPLOYING.md

- [ ] **Step 1: Replace the "Which folder do I need?" matrix section with a single drop-in instruction**

  Open [`DEPLOYING.md`](../../../DEPLOYING.md). Find the section that contains the version-fork matrix and the "Never copy both folders into the same Mendix project. Studio Pro will crash..." warning. Replace with:

  ```markdown
  ## Installing Concord

  Concord ships as a single `.mxmodule` that auto-detects your Studio Pro
  version at runtime and loads the version-appropriate host. Both Studio Pro
  10.24.13 and 11.10+ are supported by the same artifact.

  1. Download the latest `.mxmodule` from the Marketplace listing.
  2. In Studio Pro, **App → Modules → Import Module Package** (or drag-and-drop).
  3. The extension installs to `<project>/extensions/Concord/`.
  4. Restart Studio Pro.
  5. Open the pane: **Extensions → Concord → Open Pane**.

  ## How the version routing works

  Concord ships a small shim (`Concord.Shim.dll`) that Studio Pro's extension
  loader sees. On first activation, the shim:

  1. Probes the running Studio Pro version.
  2. Loads the matching internal host (`bin-10x/Concord.Host10x.dll` for
     Studio Pro 10.x; `bin-11x/Concord.Host11x.dll` for 11.x+) into an
     isolated AssemblyLoadContext.
  3. Forwards Studio Pro's MEF activations to the loaded host.

  You don't need to do anything to pick a version. The shim handles it.
  ```

### Task 6.2: CHANGELOG.md

- [ ] **Step 1: Add v5.1.0 entry**

  At the top of [`CHANGELOG.md`](../../../CHANGELOG.md):

  ```markdown
  ## v5.1.0 — runtime-shim unified .mxmodule (2026-05-XX)

  Single .mxmodule now installs on both Studio Pro 10.24.13 and 11.10+. The
  v5.0.0 two-folder layout (extensions/Concord10x/ + extensions/Concord11x/)
  is retired for consumers; both internal hosts are now bundled into one
  extensions/Concord/ folder and a runtime shim routes to the right one
  based on the running Studio Pro version.

  ### Empirical baseline
  - SP 10.24.13 pane-open latency: <X>ms (delta vs v5.0.0: +<Y>ms — within 500ms gate)
  - SP 11.10 pane-open latency: <X>ms (delta vs v5.0.0: +<Y>ms)
  - Test suite: 56 + 273 + <N> = <total> tests PASS.

  ### Architecture
  - New project `src/Concord.Shim/` builds against ExtensionsAPI 10.21.1
    (forward-compatible to 11.x per Phase 0 spike, 2026-05-15).
  - AssemblyLoadContext + Resolving event forwards shared types
    (Mendix.StudioPro.ExtensionsAPI, System.*) to default context;
    everything else loads from bin-{10,11}x/.
  - SHA-256 hash gate enforces Concord.Core.dll byte-equality across all
    three deployed copies (top-level + each bin-{Nx}/).

  ### Breaking changes for installers from v5.0.0
  - The old extensions/Concord10x/ and extensions/Concord11x/ folders should
    be removed manually. The new .mxmodule installs to extensions/Concord/.

  ### See also
  - Spec: docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md
  - Spike findings: docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md
  - Smoke results: docs/superpowers/handoffs/2026-05-XX-concord-shim-smoke-results.md
  ```

### Task 6.3: README.md

- [ ] **Step 1: Find and remove the version-fork install paragraph**

  Open [`README.md`](../../../README.md). Locate the section that says "On Studio Pro 10.x, copy extensions/Concord10x/..." (or similar). Replace with a single sentence pointing at `DEPLOYING.md`:

  ```markdown
  See [DEPLOYING.md](DEPLOYING.md) for install instructions. A single
  `.mxmodule` works on both Studio Pro 10.24.13 and 11.10+.
  ```

### Task 6.4: CLAUDE.md key paths and architecture

- [ ] **Step 1: Update the "Key paths" table**

  In `CLAUDE.md`, the "Key paths (this machine)" table currently lists ConcordPublisher's bundled resources point — adjust so it references `bin/x64/Debug/net8.0-merged/` as the source of truth for the `.mxmodule` packaging step.

- [ ] **Step 2: Update the "Architecture cheat sheet" bullets**

  Add a new bullet at the top:

  ```markdown
  - `src/Concord.Shim/` — runtime shim. Single MEF-discovered DLL; probes
    Studio Pro version on first activation and loads the matching host
    (Concord.Host10x or Concord.Host11x) into an isolated AssemblyLoadContext.
    All MEF [Export]s Studio Pro sees live here as forwarders.
  ```

- [ ] **Step 3: Update "Things that bit us before — don't repeat"**

  Add:

  ```markdown
  - **Don't use AppDomain.CurrentDomain.BaseDirectory** to locate the
    extension folder under the cache-snapshot deployment model — it returns
    Studio Pro's install dir. Use `Path.GetDirectoryName(typeof(<your-type>)
    .Assembly.Location)` instead. (Phase 0 spike found this; the shim's
    RuntimeHostLocator honors it.)
  - **Don't use `[ModuleInitializer]`** for shim bootstrap — fires
    unreliably on .NET 10 SDK 10.0.203. Use a static cctor on the [Export]
    class. (Phase 0 finding.)
  - **Concord.Core.dll exists in 3 places** under the shim layout (root +
    bin-10x/ + bin-11x/). They MUST be byte-identical. MergeHostsForShim's
    SHA-256 gate enforces this; drift = silent runtime corruption.
  ```

### Task 6.5: Marketing surfaces — all four together

**Files:**
- `marketing/marketplace-overview.md`
- `marketing/marketplace-overview.html`
- `marketing/release-announcement.md`
- `marketing/release-announcement.html`

Per [CLAUDE.md](../../../CLAUDE.md) "Things that bit us before": these four are coupled — the v4.2.1 cycle shipped MD updates without HTML and Ricardo caught it post-hoc. Update all four in the same commit.

- [ ] **Step 1: Diff hunt — find the version-fork messaging across the 4 surfaces**

  ```powershell
  Select-String -Path marketing\*.md, marketing\*.html -Pattern "Concord10x|Concord11x|two-folder|version fork|version-specific" -SimpleMatch:$false
  ```

  Note each hit; intend to remove or rephrase each.

- [ ] **Step 2: Replace the messaging consistently across all 4 surfaces**

  Old (paraphrased): "Concord ships in two flavors — one for Studio Pro 10.24.13, one for 11.10+. Pick the matching folder."

  New: "A single Concord install supports Studio Pro 10.24.13 and 11.10+. The extension detects your Studio Pro version on first launch and loads the right host automatically — no manual selection needed."

  Apply the same prose to both `marketplace-overview.md` and `marketplace-overview.html`; same for both announcement variants. Keep HTML escaping consistent.

- [ ] **Step 3: Cross-verify MD ↔ HTML parity**

  ```powershell
  # If you have pandoc or a markdown renderer, render the .md and visual-diff against .html. Otherwise, eyeball:
  Get-Content marketing\marketplace-overview.md | Select-String -Pattern "single" -Context 0,2
  Get-Content marketing\marketplace-overview.html | Select-String -Pattern "single" -Context 0,2
  ```

  Confirm both reference the single-mxmodule story; no orphan "two flavors" copy in either.

### Task 6.6: Commit Phase 6

- [ ] **Step 1: Stage docs + marketing surfaces**

  ```powershell
  git add DEPLOYING.md README.md CHANGELOG.md CLAUDE.md marketing/
  git commit -m @'
  phase 6: retire two-extension consumer surface (docs + marketing)

  Updates DEPLOYING.md, README.md, CHANGELOG.md (v5.1.0 entry with empirical
  perf baseline), CLAUDE.md (key paths + architecture cheat sheet + new
  "don't repeat" entries), and all four marketing surfaces (two MD + two HTML,
  kept in sync per the v4.2.1 lesson) to drop the two-folder/version-fork
  messaging. Single .mxmodule supports both Studio Pro versions; consumers
  don't pick a flavor.

  Old extensions/Concord10x/ and extensions/Concord11x/ source-tree projects
  REMAIN — they're still where the version-specific code lives. Only the
  consumer-facing artifact unifies.

  Refs: docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
  '@
  ```

---

## Phase 7: PR + adversarial review + ship

**Goal:** Open one PR with the 6 prior commits, run the adversarial-review checkpoint per the v5.0.0 cycle pattern, address NITs in-branch, merge, tag v5.1.0, push the tag, draft the GitHub release.

### Task 7.1: Pre-PR housekeeping

- [ ] **Step 1: Verify clean working tree**

  ```powershell
  git status
  git log main..feat/v5.1.0-runtime-shim --oneline
  ```

  Expected: working tree clean; 6 commits ahead of main.

- [ ] **Step 2: Final all-test run on the branch tip**

  ```powershell
  dotnet test Terminal.sln
  ```

  Expected: all green. If a flake hits, re-run the specific test in isolation.

- [ ] **Step 3: Push the branch**

  ```powershell
  git push -u origin feat/v5.1.0-runtime-shim
  ```

### Task 7.2: Open the PR

- [ ] **Step 1: Compose PR body and create**

  ```powershell
  $body = @'
  ## Summary

  - Single .mxmodule that works on both Studio Pro 10.24.13 and 11.10+.
  - New `src/Concord.Shim/` project: AssemblyLoadContext-isolated runtime
    router that probes the Studio Pro version on first activation and
    loads the matching host (`bin-10x/` or `bin-11x/`).
  - SHA-256 build-time hash gate prevents Concord.Core.dll divergence
    between the 3 deployed copies.
  - Consumer-facing surface unified — old `extensions/Concord10x/` and
    `extensions/Concord11x/` folders retired (source-tree hosts remain
    for version-specific code).

  ## Phase outline (6 atomic commits)

  1. Concord.Shim project skeleton + ShimLog
  2. Isolation primitives — RuntimeHostLocator + ConcordHostLoadContext
  3. Shim MEF forwarders + Host{N}xEntry bootstrap chain
  4. MergeHostsForShim MSBuild + hash gate + unified deploy
  5. Manual Studio Pro smoke matrix (results: docs/superpowers/handoffs/2026-05-XX-concord-shim-smoke-results.md)
  6. Retire two-extension consumer surface (docs + marketing in sync)

  ## Empirical baseline (from Phase 5 smoke)

  - SP 10.24.13: pane-open <Xms> (delta vs v5.0.0: +<Y>ms)
  - SP 11.10: pane-open <Xms> (delta vs v5.0.0: +<Y>ms)
  - Perf gate (≤500ms regression): PASS

  ## Test plan

  - [x] `dotnet test Terminal.sln` — Concord.Core 56/56, Terminal 273+, Concord.Shim ~12, all PASS
  - [x] Hash gate fires on deliberate divergence (Phase 4 Task 4.2 step 5)
  - [x] SP 10.24.13 smoke — pane open, action server, tool round-trip
  - [x] SP 11.10 smoke — same
  - [ ] Mac smoke — Ricardo's fast-follow post-merge

  ## See also

  - Spec: [docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md](docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md)
  - Phase 0 findings: [docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md](docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md)
  - Implementation plan: [docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md](docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md)

  🤖 Generated with [Claude Code](https://claude.com/claude-code)
  '@
  $body | Out-File -Encoding utf8 -FilePath pr-body.tmp.md
  gh pr create --title "feat: v5.1.0 — runtime-shim unified .mxmodule" --body-file pr-body.tmp.md --base main --head feat/v5.1.0-runtime-shim
  Remove-Item pr-body.tmp.md
  ```

### Task 7.3: Adversarial review checkpoint

Per the v5.0.0 cycle's pattern (per [CLAUDE.md](../../../CLAUDE.md) "Working style"): one fresh-reviewer pass on the full diff before merge.

- [ ] **Step 1: Run adversarial review**

  Either via `/ultrareview <PR#>` (if Ricardo wants the multi-agent review — that's user-triggered) OR by dispatching a code-reviewer agent against the branch.

  Capture review output. NITs get addressed in-branch (new commits OK; squash at merge time per Ricardo's preference). FLAGs (deeper issues that don't block) get logged as deferred-followup memory entries.

- [ ] **Step 2: Address NITs**

  For each NIT, edit + commit `nit: <one-line description>`. Re-push.

### Task 7.4: Merge + tag + release

- [ ] **Step 1: Merge (squash if Ricardo prefers; otherwise plain merge — confirm before action)**

  ```powershell
  # ASK RICARDO first: squash, merge commit, or rebase.
  # Likely answer: squash (matches v5.0.0 cycle).
  gh pr merge <PR#> --squash --delete-branch
  ```

- [ ] **Step 2: Pull main, tag, push tag**

  ```powershell
  git checkout main
  git pull --ff-only
  git tag v5.1.0
  git push origin v5.1.0
  ```

- [ ] **Step 3: Draft the GitHub release**

  Per CLAUDE.md gotcha: `gh release create` mishandles heredocs with backticks. Write notes to a temp file, use `--notes-file`.

  ```powershell
  $notes = @'
  # v5.1.0 — single .mxmodule for both Studio Pro versions

  Concord now ships as one .mxmodule that auto-detects your Studio Pro
  version at runtime and loads the appropriate host. No more separate
  Concord10x/Concord11x folders.

  ## Highlights

  - Single .mxmodule supports Studio Pro 10.24.13 and 11.10+.
  - Runtime shim (`Concord.Shim.dll`) uses AssemblyLoadContext isolation;
    type identity preserved across the boundary via `Resolving` event
    forwarding `Mendix.StudioPro.ExtensionsAPI` to the default context.
  - Build-time SHA-256 hash gate prevents `Concord.Core.dll` divergence
    between the 3 deployed copies.

  ## Empirical baseline

  - SP 10.24.13 pane-open latency: <X>ms (delta vs v5.0.0: +<Y>ms)
  - SP 11.10 pane-open latency: <X>ms (delta vs v5.0.0: +<Y>ms)
  - Test suite: <total> tests PASS.

  ## Migration from v5.0.0

  Delete `extensions/Concord10x/` and `extensions/Concord11x/` from your
  Mendix project. Install the new .mxmodule — it deploys to
  `extensions/Concord/`.

  ## See also

  - [Implementation plan](docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md)
  - [Phase 0 spike findings](docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md)
  - [Phase 5 smoke results](docs/superpowers/handoffs/2026-05-XX-concord-shim-smoke-results.md)
  '@
  $notes | Out-File -Encoding utf8 -FilePath release-notes.tmp.md
  gh release create v5.1.0 --title "v5.1.0 — runtime-shim unified .mxmodule" --notes-file release-notes.tmp.md
  Remove-Item release-notes.tmp.md
  ```

- [ ] **Step 4: Marketplace upload**

  This is Ricardo's manual step per `reference_concord_release_playbook.md`. Hand off the .mxmodule file path; let him handle the upload at his discretion.

### Task 7.5: Memory housekeeping

- [ ] **Step 1: Update `_HANDOFF.md`**

  Replace body with post-merge state:
  - v5.1.0 shipped via PR <#>, tag `v5.1.0`, GitHub release live.
  - Marketplace upload: Ricardo's discretion.
  - Deferred backlog:
    - SP 12.x compat re-verification (OQ2)
    - Mac smoke pass (OQ3)
    - SP12 ExtensionsAPI ContentMatrix audit when available
    - Auto-generation of skill/rules tool-name lists from concord-mcp catalog (carried over from v5.0.0 backlog)
    - `run_app` UI automation (still Task-15 stub, carried over)
    - any FLAGs from Phase 7 adversarial review

- [ ] **Step 2: Update `MEMORY.md`**

  Add index entries for:
  - Phase 5 smoke-results handoff
  - This implementation plan
  - Updated `_HANDOFF.md`

  Remove or update any stale entries that pointed at the two-folder layout (Phase 0 findings doc stays — it's the empirical foundation).

- [ ] **Step 3: Commit memory updates**

  ```powershell
  cd C:\Users\rc1yok\.claude\projects\c--Extensions-Terminal\memory
  # Memory dir is its own (gitignored from main repo) — Ricardo's call on whether to version it
  ```

  Memory dir is outside the repo; no git commit needed for the codebase. Just save the files.

---

## Definition of done

- [ ] All 7 phases committed (6 codebase commits + post-merge memory housekeeping)
- [ ] PR #<N> merged to main with adversarial review passed
- [ ] Tag `v5.1.0` pushed; GitHub release drafted
- [ ] Smoke matrix on SP 10.24.13 and SP 11.10 both PASS; perf gate ≤500ms regression
- [ ] Marketplace upload handed to Ricardo
- [ ] `_HANDOFF.md` and `MEMORY.md` reflect post-merge state
- [ ] `Concord.Core.dll` hash gate verified working (Task 4.2 step 5 evidence)
- [ ] All 5 open questions from the spike findings resolved per the "Resolution" section at the top of this plan

If any phase produces a STOP signal (most notably Phase 5's perf regression), pause, address the root cause in a sub-phase, and resume — do not work around. The whole point of the gate is to keep v5.1.0 from shipping with hidden perf debt.
