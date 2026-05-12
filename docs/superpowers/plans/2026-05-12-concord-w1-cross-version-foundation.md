# Concord W1 — Cross-Version Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the monolithic `Terminal.csproj` into a `Concord.Core` class library plus two thin host DLLs (`Concord.Host10x`, `Concord.Host11x`) that bind to their version-specific `Mendix.StudioPro.ExtensionsAPI`. Deploy both host DLLs to `extensions/Concord/`; Studio Pro 10.24.13 and 11.x each load the matching host. No functional changes to the user-visible feature surface vs. v4.1 — this is structural only, the foundation for W2-W4.

**Architecture:** Concord.Core is a regular .NET class library with no `Mendix.StudioPro.ExtensionsAPI` reference. All Studio Pro service access goes through interfaces defined in Core; the host DLLs provide version-specific implementations and own the MEF entry classes. The C# namespace stays `Terminal` in Core (matching today's `RootNamespace=Terminal`) to avoid churn across 23+ source files; only the assembly split is new. Host-specific code uses the host's own namespace (`Concord.Host10x`, `Concord.Host11x`).

**Tech Stack:** .NET 8, MEF (Mendix extensibility), `Mendix.StudioPro.ExtensionsAPI` (11.6.2 + the 10.24.13-compatible version), xunit, esbuild (UI bundle, unchanged).

---

## Working assumptions to verify in Task 1

- The exact `Mendix.StudioPro.ExtensionsAPI` NuGet version compatible with Studio Pro 10.24.13 is unknown at plan-write time. Task 1 includes a discovery step.
- The manifest layout (one manifest with two DLLs in `mx_extensions[]`, vs. two manifests in subfolders) is unverified. Task 1 includes a spike.
- Whether Studio Pro 10.x supports MEF the same way as 11.x is unknown. Task 1 verifies a minimal Hello-World extension loads on 10.24.13.

If any of these assumptions fail, downstream tasks adapt — the plan flags the dependent decisions explicitly.

---

## File Structure

**Created files:**

```
src/Concord.Core/
├── Concord.Core.csproj                # class library, no ExtensionsAPI ref
├── TargetMode.cs                       # enum { Studio10x, Studio11x }
├── HostContext.cs                      # static container: TargetMode + host interop
├── Interop/                            # interfaces — Core abstractions for host services
│   ├── IStudioProAppHost.cs            # access to Studio Pro's main IApp / project / etc.
│   ├── IRunConfigurationsHost.cs       # wraps ILocalRunConfigurationsService
│   ├── IModuleImportHost.cs            # one-button .mpk import (W4 will use, stub now)
│   └── IRunStateHost.cs                # wraps RunStateProbe's Studio Pro service usage
└── Spmcp/                              # placeholder, empty in W1 — W2 fills this

src/Concord.Host11x/
├── Concord.Host11x.csproj              # references Core + ExtensionsAPI 11.6.2
├── Host11xEntry.cs                     # MEF entry, sets HostContext.TargetMode = Studio11x
├── manifest.json                       # { "mx_extensions": ["Concord.Host11x.dll"] }
└── Interop/                            # 11.x implementations of Core interfaces
    ├── StudioProAppHost11x.cs
    ├── RunConfigurationsHost11x.cs
    ├── ModuleImportHost11x.cs          # stub returning NotSupported for now
    └── RunStateHost11x.cs

src/Concord.Host10x/
├── Concord.Host10x.csproj              # references Core + ExtensionsAPI (10.24.13-compat version)
├── Host10xEntry.cs                     # MEF entry, sets HostContext.TargetMode = Studio10x
├── manifest.json                       # { "mx_extensions": ["Concord.Host10x.dll"] }
└── Interop/
    ├── StudioProAppHost10x.cs
    ├── RunConfigurationsHost10x.cs
    ├── ModuleImportHost10x.cs          # stub
    └── RunStateHost10x.cs

tests/Concord.Core.Tests/
├── Concord.Core.Tests.csproj
└── HostContextTests.cs                 # unit test for TargetMode plumbing
```

**Modified files:**

```
Terminal.csproj                         # split out: becomes Concord.Host11x.csproj equivalent
                                        # In W1 we KEEP Terminal.csproj as is, just move classes;
                                        # the rename of Terminal.csproj→Host11x is the LAST task
Terminal.sln                            # add 3 new projects + 1 test project
tests/Terminal.Tests.csproj             # add reference to Concord.Core for new tests
manifest.json                           # decided by Task 1 spike (single or per-subfolder)
Directory.Build.props.example           # add docs comment about new structure
DEPLOYING.md                            # update build + deploy walkthrough
```

**Moved files (no logic changes, just relocation):**

Files that today live in `src/` move under `src/Concord.Core/Terminal/` (folder organization only — preserves Git history via `git mv`):

```
src/Concord.Core/Terminal/
├── Logging.cs
├── RingBuffer.cs
├── PtySession.cs
├── UnixPtySession.cs
├── IPtyFactory.cs
├── ShellDetector.cs
├── SessionInfo.cs
├── TerminalSessionManager.cs
├── TerminalState.cs
├── TerminalSettings.cs
├── SettingsApplyHelper.cs
├── McpJsonConfigurator.cs
├── McpTomlConfigurator.cs
├── McpProbe.cs
├── StudioProThemeProbe.cs
├── BundledSkillReader.cs
└── SkillInstaller.cs

src/Concord.Core/Mcp/
├── StudioProActionServer.cs            # moves, refactored to depend on Interop interfaces
└── StudioProActions.cs                 # moves, same refactor

src/Concord.Core/Maia/                  # moves wholesale
└── (existing Maia/* files)

src/Concord.Core/Ui/                    # moves
├── TerminalWebServer.cs
└── Messages/
```

Files that today live in `src/` and depend on `Mendix.StudioPro.ExtensionsAPI` types **stay** in the host projects (split into 10x and 11x copies):

```
# 11.x host (extracted from current code)
src/Concord.Host11x/MenuExtensions/TerminalMenuExtension.cs
src/Concord.Host11x/Pane/TerminalPaneExtension.cs
src/Concord.Host11x/Pane/TerminalPaneViewModel.cs
src/Concord.Host11x/Interop/RunStateProbe.cs       # the Studio Pro service call site

# 10.x host (new, mirrors structure)
src/Concord.Host10x/MenuExtensions/TerminalMenuExtension.cs
src/Concord.Host10x/Pane/TerminalPaneExtension.cs
src/Concord.Host10x/Pane/TerminalPaneViewModel.cs
src/Concord.Host10x/Interop/RunStateProbe.cs
```

The host-specific files differ only in their ExtensionsAPI type references; everything they do delegates to Core.

---

## Phase 0 — Discovery spike

### Task 1: Verify Studio Pro 10.x + manifest layout assumptions

**Files:**
- Spike workspace: `spikes/2026-05-12-w1-discovery/` (gitignored, throw-away)
- Notes output: `docs/superpowers/plans/2026-05-12-concord-w1-spike-notes.md`

This task answers three questions before the plan commits to a layout. Each answer is recorded in the spike notes file; downstream tasks reference those answers.

- [ ] **Step 1: Find the right ExtensionsAPI version for 10.24.13**

Run: `dotnet nuget list source` then `dotnet package search Mendix.StudioPro.ExtensionsAPI --source <feed-url> --prerelease`.
Expected: a list of available `Mendix.StudioPro.ExtensionsAPI` package versions. Identify the highest version whose changelog/readme indicates Studio Pro 10.24.x compatibility (likely around `10.21.x` or `10.24.x`).

Record the version in spike notes as `EXTENSIONSAPI_10X_VERSION = <x.y.z>`.

- [ ] **Step 2: Try a single-manifest, two-DLL layout**

Create `spikes/2026-05-12-w1-discovery/single-manifest/manifest.json`:

```json
{ "mx_extensions": ["Concord.Host10x.dll", "Concord.Host11x.dll"] }
```

Drop two trivial Hello-World DLLs (one built against each ExtensionsAPI version) alongside it into a Mendix 10.24.13 project's `extensions/Hello/` folder. Open Studio Pro 10.24.13 with `--enable-extension-development`.

Expected: Studio Pro either (A) loads the matching DLL and silently ignores the wrong-version one, or (B) errors with a "type load failed" message that breaks the extension entirely.

Record outcome in spike notes as `SINGLE_MANIFEST_OUTCOME = works | breaks`. If (A), the plan uses one flat folder. If (B), the plan uses two subfolders.

- [ ] **Step 3: Try the two-subfolder fallback layout** (only if Step 2 said `breaks`)

Create `spikes/2026-05-12-w1-discovery/two-subfolder/`:

```
extensions/Hello/
├── 10x/manifest.json   { "mx_extensions": ["Hello.Host10x.dll"] }
├── 11x/manifest.json   { "mx_extensions": ["Hello.Host11x.dll"] }
├── Hello.Host10x.dll
└── Hello.Host11x.dll
```

Verify Studio Pro 10.24.13 loads the 10x manifest and Studio Pro 11.x loads the 11x manifest.

Expected: Both extensions load on the matching Studio Pro version. Record outcome in spike notes as `TWO_SUBFOLDER_OUTCOME = works | breaks`.

If both layouts fail, escalate to user — the plan needs revisiting.

- [ ] **Step 4: Verify MEF works the same on 10.24.13**

In the spike DLL for 10.x, expose a `[Export(typeof(IMenuExtension))] HelloMenuExtension` class. Verify Studio Pro 10.24.13 picks it up and adds a menu item.

Expected: menu item appears. Record outcome as `MEF_10X_OUTCOME = works | broken`. If broken, capture the exact MEF surface that differs and escalate.

- [ ] **Step 5: Write spike notes file**

Create `docs/superpowers/plans/2026-05-12-concord-w1-spike-notes.md`:

```markdown
# W1 Discovery Spike — Findings

Date: <today>
Studio Pro versions tested: 10.24.13, 11.10.0

## ExtensionsAPI versions
- 10.x compatible: `EXTENSIONSAPI_10X_VERSION = <version>`
- 11.x compatible: `Mendix.StudioPro.ExtensionsAPI 11.6.2` (current)

## Manifest layout decision
- Single manifest, two DLLs: <works | breaks>
- Two subfolders: <works | breaks>
- **Decision: <single | two-subfolders>**

## MEF on 10.x
- Status: <works | broken>
- Notes: <…>

## Implications for the plan
- <list any task adjustments needed>
```

- [ ] **Step 6: Commit spike notes**

```bash
git add docs/superpowers/plans/2026-05-12-concord-w1-spike-notes.md
git commit -m "docs: W1 cross-version discovery spike findings

Records the ExtensionsAPI version for 10.24.13, the chosen manifest
layout, and any MEF surface differences observed. Downstream tasks
reference the recorded values."
```

The `spikes/` working folder is gitignored and discarded after this task.

---

## Phase 1 — Add Concord.Core as a class library, no functional changes

### Task 2: Create Concord.Core.csproj scaffolding

**Files:**
- Create: `src/Concord.Core/Concord.Core.csproj`
- Modify: `Terminal.sln`

- [ ] **Step 1: Create the project file**

`src/Concord.Core/Concord.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <AssemblyName>Concord.Core</AssemblyName>
    <RootNamespace>Terminal</RootNamespace>
    <LangVersion>preview</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <IsPackable>false</IsPackable>
    <Version>4.1.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Terminal.Tests" />
    <InternalsVisibleTo Include="Concord.Core.Tests" />
    <InternalsVisibleTo Include="Concord.Host10x" />
    <InternalsVisibleTo Include="Concord.Host11x" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Eto.Forms" Version="2.9.*" />
    <PackageReference Include="System.Text.Json" Version="8.0.*" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.*" />
  </ItemGroup>
</Project>
```

Note: `RootNamespace=Terminal` matches the current convention so moved files don't need their `namespace Terminal;` declarations changed.

- [ ] **Step 2: Add the project to the solution**

Run: `dotnet sln Terminal.sln add src/Concord.Core/Concord.Core.csproj`

Expected output: `Project 'src/Concord.Core/Concord.Core.csproj' added to the solution.`

- [ ] **Step 3: Build to confirm it compiles (empty project)**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Core/Concord.Core.csproj Terminal.sln
git commit -m "build: add Concord.Core class library scaffolding (W1)

Empty class library that will hold Studio-Pro-agnostic code shared by
Concord.Host10x and Concord.Host11x. No code moved yet."
```

### Task 3: Add TargetMode enum and HostContext

**Files:**
- Create: `src/Concord.Core/TargetMode.cs`
- Create: `src/Concord.Core/HostContext.cs`
- Create: `tests/Concord.Core.Tests/Concord.Core.Tests.csproj`
- Create: `tests/Concord.Core.Tests/HostContextTests.cs`
- Modify: `Terminal.sln`

- [ ] **Step 1: Write the failing test**

`tests/Concord.Core.Tests/HostContextTests.cs`:

```csharp
namespace Concord.Core.Tests;

using Xunit;
using Terminal;

public class HostContextTests
{
    [Fact]
    public void TargetMode_DefaultsToUninitialized_WhenHostHasNotSetIt()
    {
        HostContext.Reset();
        Assert.Equal(TargetMode.Uninitialized, HostContext.TargetMode);
    }

    [Fact]
    public void TargetMode_ReturnsValueSetByHost()
    {
        HostContext.Reset();
        HostContext.Initialize(TargetMode.Studio11x);
        Assert.Equal(TargetMode.Studio11x, HostContext.TargetMode);
    }

    [Fact]
    public void Initialize_Throws_WhenCalledTwice()
    {
        HostContext.Reset();
        HostContext.Initialize(TargetMode.Studio11x);
        Assert.Throws<InvalidOperationException>(() => HostContext.Initialize(TargetMode.Studio10x));
    }
}
```

- [ ] **Step 2: Create the test project**

`tests/Concord.Core.Tests/Concord.Core.Tests.csproj`:

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
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Concord.Core\Concord.Core.csproj" />
  </ItemGroup>
</Project>
```

Verify the versions match what `tests/Terminal.Tests.csproj` already uses — copy the exact package version strings from there to avoid drift.

- [ ] **Step 3: Add the test project to the solution**

Run: `dotnet sln Terminal.sln add tests/Concord.Core.Tests/Concord.Core.Tests.csproj`

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/Concord.Core.Tests/Concord.Core.Tests.csproj`
Expected: build error — `TargetMode` and `HostContext` don't exist yet.

- [ ] **Step 5: Implement TargetMode enum**

`src/Concord.Core/TargetMode.cs`:

```csharp
namespace Terminal;

public enum TargetMode
{
    Uninitialized = 0,
    Studio10x = 1,
    Studio11x = 2,
}
```

- [ ] **Step 6: Implement HostContext**

`src/Concord.Core/HostContext.cs`:

```csharp
namespace Terminal;

public static class HostContext
{
    private static TargetMode _targetMode = TargetMode.Uninitialized;
    private static bool _initialized;
    private static readonly object _gate = new();

    public static TargetMode TargetMode
    {
        get { lock (_gate) return _targetMode; }
    }

    public static void Initialize(TargetMode mode)
    {
        lock (_gate)
        {
            if (_initialized)
                throw new InvalidOperationException(
                    "HostContext.Initialize was called twice. Each host DLL must call it exactly once at MEF activation.");
            if (mode == TargetMode.Uninitialized)
                throw new ArgumentException("TargetMode.Uninitialized is not a valid initialization value.", nameof(mode));
            _targetMode = mode;
            _initialized = true;
        }
    }

    // Test-only; gated by InternalsVisibleTo.
    internal static void Reset()
    {
        lock (_gate)
        {
            _targetMode = TargetMode.Uninitialized;
            _initialized = false;
        }
    }
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/Concord.Core.Tests/Concord.Core.Tests.csproj`
Expected: `Passed!  - Failed: 0, Passed: 3`

- [ ] **Step 8: Commit**

```bash
git add src/Concord.Core/TargetMode.cs src/Concord.Core/HostContext.cs \
        tests/Concord.Core.Tests/ Terminal.sln
git commit -m "feat(core): add TargetMode + HostContext for cross-version dispatch (W1)

Host DLLs call HostContext.Initialize(Studio10x|Studio11x) at MEF
activation. Core reads HostContext.TargetMode to dispatch version-aware
behavior. Double-initialize is an error to catch deploy mistakes early."
```

### Task 4: Add Interop interfaces in Core

**Files:**
- Create: `src/Concord.Core/Interop/IStudioProAppHost.cs`
- Create: `src/Concord.Core/Interop/IRunConfigurationsHost.cs`
- Create: `src/Concord.Core/Interop/IRunStateHost.cs`
- Create: `src/Concord.Core/Interop/IModuleImportHost.cs`
- Create: `src/Concord.Core/Interop/HostServices.cs` (registry)

These interfaces wrap the Studio Pro service calls that Core needs but cannot make directly (because Core has no ExtensionsAPI ref). Hosts register their implementations at startup.

- [ ] **Step 1: Define IStudioProAppHost**

`src/Concord.Core/Interop/IStudioProAppHost.cs`:

```csharp
namespace Terminal.Interop;

/// <summary>
/// Minimal abstraction over Studio Pro's IApp. Hosts provide a concrete
/// implementation that wraps the version-specific Mendix.StudioPro.ExtensionsAPI
/// types. Core uses only these methods.
/// </summary>
public interface IStudioProAppHost
{
    /// <summary>Absolute path to the open Mendix project directory.</summary>
    string ProjectPath { get; }

    /// <summary>The project's display name (matches the .mpr filename without extension).</summary>
    string ProjectName { get; }

    /// <summary>True if a project is currently open.</summary>
    bool HasOpenProject { get; }
}
```

- [ ] **Step 2: Define IRunConfigurationsHost**

`src/Concord.Core/Interop/IRunConfigurationsHost.cs`:

```csharp
namespace Terminal.Interop;

public record RunConfigurationInfo(string Id, string Name, string? ApplicationRootUrl);

public interface IRunConfigurationsHost
{
    RunConfigurationInfo? GetActive();
    IReadOnlyList<RunConfigurationInfo> ListAll();
}
```

- [ ] **Step 3: Define IRunStateHost**

`src/Concord.Core/Interop/IRunStateHost.cs`:

```csharp
namespace Terminal.Interop;

public enum AppRunState { Stopped, Starting, Running, Stopping, Unknown }

public interface IRunStateHost
{
    AppRunState GetCurrentState();
}
```

- [ ] **Step 4: Define IModuleImportHost**

`src/Concord.Core/Interop/IModuleImportHost.cs`:

```csharp
namespace Terminal.Interop;

public record ModuleImportResult(bool Success, string? Error);

public interface IModuleImportHost
{
    bool IsModuleImported(string moduleName);
    ModuleImportResult ImportFromMpk(string mpkAbsolutePath);
}
```

- [ ] **Step 5: Define the HostServices registry**

`src/Concord.Core/Interop/HostServices.cs`:

```csharp
namespace Terminal.Interop;

/// <summary>
/// Registry of Studio-Pro-typed implementations supplied by the active host DLL.
/// Each host's MEF entry calls Register at startup, then Core resolves via the
/// public getters.
/// </summary>
public static class HostServices
{
    private static IStudioProAppHost? _app;
    private static IRunConfigurationsHost? _runConfigs;
    private static IRunStateHost? _runState;
    private static IModuleImportHost? _moduleImport;
    private static readonly object _gate = new();

    public static IStudioProAppHost App
        => _app ?? throw NotInitialized(nameof(IStudioProAppHost));
    public static IRunConfigurationsHost RunConfigurations
        => _runConfigs ?? throw NotInitialized(nameof(IRunConfigurationsHost));
    public static IRunStateHost RunState
        => _runState ?? throw NotInitialized(nameof(IRunStateHost));
    public static IModuleImportHost ModuleImport
        => _moduleImport ?? throw NotInitialized(nameof(IModuleImportHost));

    public static void Register(
        IStudioProAppHost app,
        IRunConfigurationsHost runConfigs,
        IRunStateHost runState,
        IModuleImportHost moduleImport)
    {
        lock (_gate)
        {
            _app = app;
            _runConfigs = runConfigs;
            _runState = runState;
            _moduleImport = moduleImport;
        }
    }

    internal static void Reset()
    {
        lock (_gate)
        {
            _app = null;
            _runConfigs = null;
            _runState = null;
            _moduleImport = null;
        }
    }

    private static InvalidOperationException NotInitialized(string serviceName)
        => new($"HostServices.{serviceName} was accessed before HostServices.Register was called. " +
               "Each host DLL must call Register from its MEF activation.");
}
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add src/Concord.Core/Interop/
git commit -m "feat(core): add Interop interfaces + HostServices registry (W1)

IStudioProAppHost, IRunConfigurationsHost, IRunStateHost,
IModuleImportHost. Each host DLL implements them against its version's
Mendix.StudioPro.ExtensionsAPI typings and registers at MEF activation.
Core resolves through HostServices."
```

---

## Phase 2 — Migrate Studio-Pro-agnostic files into Core

Each task below moves one logical group from `src/` into `src/Concord.Core/` and verifies the build still passes. **Use `git mv`** for each move so blame history follows.

**File-classification rule for subagents.** The lists below were drafted against an earlier snapshot of `src/`. The current tree may include additional files. For any file in `src/` that a Phase 2 task touches but doesn't explicitly name, classify it with:

```powershell
Select-String -Path src/<file>.cs -Pattern "Mendix\.StudioPro"
```

- **No match** → file is Studio-Pro-agnostic. Move to `Concord.Core` under the matching subfolder (use `Terminal/` for managers/probes, `Ui/` for HTTP/UI plumbing, `Mcp/` for MCP host code, `Maia/` for the Maia bridge). Add it to the task's git mv list.
- **Match** → file uses `Mendix.StudioPro.ExtensionsAPI` types. It must live in a host project, not Core. Skip it in Phase 2 tasks; it'll be picked up in Task 11 (Host11x move) and Task 13 (Host10x mirror).

If a file's classification is ambiguous (e.g., it uses an interface that *could* be host-implemented), report it as `DONE_WITH_CONCERNS` rather than guessing.

**Known Studio-Pro-typed files in v4.2.2** (confirmed via grep at plan-write time):
- `src/TerminalMenuExtension.cs`
- `src/TerminalPaneExtension.cs`
- `src/TerminalPaneViewModel.cs`
- `src/TerminalWebServer.cs`

All four belong in Host11x via Task 11 (and mirrored in Host10x via Task 13). Everything else in `src/` is currently Core-bound.

### Task 5: Move terminal session + PTY files

**Files moved (git mv):**

```
src/Logging.cs                 → src/Concord.Core/Terminal/Logging.cs
src/RingBuffer.cs              → src/Concord.Core/Terminal/RingBuffer.cs
src/IPtyFactory.cs             → src/Concord.Core/Terminal/IPtyFactory.cs
src/PtySession.cs              → src/Concord.Core/Terminal/PtySession.cs
src/UnixPtySession.cs          → src/Concord.Core/Terminal/UnixPtySession.cs
src/ShellDetector.cs           → src/Concord.Core/Terminal/ShellDetector.cs
src/SessionInfo.cs             → src/Concord.Core/Terminal/SessionInfo.cs
src/TerminalSessionManager.cs  → src/Concord.Core/Terminal/TerminalSessionManager.cs
src/TerminalState.cs           → src/Concord.Core/Terminal/TerminalState.cs
```

- [ ] **Step 1: Verify none of these files import Mendix.StudioPro.ExtensionsAPI**

Run: `Select-String -Path src/Logging.cs,src/RingBuffer.cs,src/IPtyFactory.cs,src/PtySession.cs,src/UnixPtySession.cs,src/ShellDetector.cs,src/SessionInfo.cs,src/TerminalSessionManager.cs,src/TerminalState.cs -Pattern "Mendix\.StudioPro"`

Expected: no matches. If a match is found, that file cannot move yet — handle it via the Interop interface or keep it in the host project. Pause and replan.

- [ ] **Step 2: Move files**

```powershell
git mv src/Logging.cs                src/Concord.Core/Terminal/Logging.cs
git mv src/RingBuffer.cs             src/Concord.Core/Terminal/RingBuffer.cs
git mv src/IPtyFactory.cs            src/Concord.Core/Terminal/IPtyFactory.cs
git mv src/PtySession.cs             src/Concord.Core/Terminal/PtySession.cs
git mv src/UnixPtySession.cs         src/Concord.Core/Terminal/UnixPtySession.cs
git mv src/ShellDetector.cs          src/Concord.Core/Terminal/ShellDetector.cs
git mv src/SessionInfo.cs            src/Concord.Core/Terminal/SessionInfo.cs
git mv src/TerminalSessionManager.cs src/Concord.Core/Terminal/TerminalSessionManager.cs
git mv src/TerminalState.cs          src/Concord.Core/Terminal/TerminalState.cs
```

- [ ] **Step 3: Update Terminal.csproj `<Compile Remove>` filter**

The current `Terminal.csproj` has `<Compile Remove="tests/**" />`. Add a remove for the moved tree so the host project doesn't double-compile Core sources:

```xml
<ItemGroup>
  <Compile Remove="tests/**" />
  <Compile Remove="debug/**" />
  <Compile Remove="src/Concord.Core/**" />
</ItemGroup>
```

And add a project reference to Core:

```xml
<ItemGroup>
  <ProjectReference Include="src/Concord.Core/Concord.Core.csproj" />
</ItemGroup>
```

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build Terminal.sln`
Expected: `Build succeeded.`

- [ ] **Step 5: Run all tests**

Run: `dotnet test Terminal.sln`
Expected: `Passed!  - Failed: 0`. Both Terminal.Tests and Concord.Core.Tests should pass. If any test breaks because it referenced `Terminal.PtySession` via the old assembly, fix by updating `using` statements — namespace stays the same (`Terminal`).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(core): move PTY + terminal-session files into Concord.Core (W1)

Files moved via git mv (history preserved). No code changes inside the
files. Terminal.csproj now references Concord.Core and excludes the
moved tree from its own compile."
```

### Task 6: Move settings + MCP config writer + CLI config manager files

**Files moved (git mv):**

```
src/TerminalSettings.cs           → src/Concord.Core/Terminal/TerminalSettings.cs
src/SettingsApplyHelper.cs        → src/Concord.Core/Terminal/SettingsApplyHelper.cs
src/McpJsonConfigurator.cs        → src/Concord.Core/Terminal/McpJsonConfigurator.cs
src/McpTomlConfigurator.cs        → src/Concord.Core/Terminal/McpTomlConfigurator.cs
src/McpProbe.cs                   → src/Concord.Core/Terminal/McpProbe.cs
src/StudioProThemeProbe.cs        → src/Concord.Core/Terminal/StudioProThemeProbe.cs
src/BundledSkillReader.cs         → src/Concord.Core/Terminal/BundledSkillReader.cs
src/SkillInstaller.cs             → src/Concord.Core/Terminal/SkillInstaller.cs
src/RulesInstaller.cs             → src/Concord.Core/Terminal/RulesInstaller.cs
src/AgentsMdManager.cs            → src/Concord.Core/Terminal/AgentsMdManager.cs
src/ClaudeMdManager.cs            → src/Concord.Core/Terminal/ClaudeMdManager.cs
src/CopilotInstructionsManager.cs → src/Concord.Core/Terminal/CopilotInstructionsManager.cs
```

The four CLI-config managers (`AgentsMdManager`, `ClaudeMdManager`, `CopilotInstructionsManager`, `RulesInstaller`) were added in v4.2.x and play the same role as `SkillInstaller` — they write/manage agent-side config files based on Settings, no Studio Pro coupling.

- [ ] **Step 1: Verify none import Mendix.StudioPro.ExtensionsAPI**

Run the same `Select-String` check. Expected: no matches.

- [ ] **Step 2: Move files**

```powershell
git mv src/TerminalSettings.cs           src/Concord.Core/Terminal/TerminalSettings.cs
git mv src/SettingsApplyHelper.cs        src/Concord.Core/Terminal/SettingsApplyHelper.cs
git mv src/McpJsonConfigurator.cs        src/Concord.Core/Terminal/McpJsonConfigurator.cs
git mv src/McpTomlConfigurator.cs        src/Concord.Core/Terminal/McpTomlConfigurator.cs
git mv src/McpProbe.cs                   src/Concord.Core/Terminal/McpProbe.cs
git mv src/StudioProThemeProbe.cs        src/Concord.Core/Terminal/StudioProThemeProbe.cs
git mv src/BundledSkillReader.cs         src/Concord.Core/Terminal/BundledSkillReader.cs
git mv src/SkillInstaller.cs             src/Concord.Core/Terminal/SkillInstaller.cs
```

- [ ] **Step 3: Build solution**

Run: `dotnet build Terminal.sln`
Expected: `Build succeeded.`

- [ ] **Step 4: Run all tests**

Run: `dotnet test Terminal.sln`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(core): move settings + MCP config writers into Concord.Core (W1)

git mv only, no code changes. McpJsonConfigurator, McpTomlConfigurator,
McpProbe, SkillInstaller, BundledSkillReader, TerminalSettings,
SettingsApplyHelper, StudioProThemeProbe now live in Core."
```

### Task 7: Move UI message DTOs into Core

`TerminalWebServer.cs` was originally planned for Core but is Studio-Pro-typed (uses `Mendix.StudioPro.ExtensionsAPI.UI.WebServer.IWebServer`). It stays in the host bucket and moves in Task 11. Task 7 covers only the message-DTO folder.

**Files moved (git mv):**

```
src/Messages/   → src/Concord.Core/Ui/Messages/
```

- [ ] **Step 1: Verify Messages folder doesn't import ExtensionsAPI**

Run: `Select-String -Path src/Messages -Pattern "Mendix\.StudioPro" -Recurse`
Expected: no matches. These are plain DTOs.

- [ ] **Step 2: Move folder**

```powershell
git mv src/Messages src/Concord.Core/Ui/Messages
```

- [ ] **Step 3: Build + test**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: both succeed.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(core): move message DTOs into Concord.Core/Ui (W1)

TerminalWebServer.cs intentionally remains in the host bucket — it
depends on Studio Pro's IWebServer service and will move to Host11x
in Task 11 (mirrored to Host10x in Task 13)."
```

### Task 8: Move Maia bridge into Core

**Files moved:**

```
src/Maia/  → src/Concord.Core/Maia/
```

- [ ] **Step 1: Verify Maia doesn't import ExtensionsAPI**

Run: `Select-String -Path src/Maia -Pattern "Mendix\.StudioPro" -Recurse`
Expected: no matches. Maia uses WebView2 / CDP directly, not Studio Pro modeling types.

If Maia depends on `IStudioProAppHost`-type info (e.g., the project path for log output), keep using `HostServices.App.ProjectPath` from Core — no change.

- [ ] **Step 2: Move folder**

```powershell
git mv src/Maia src/Concord.Core/Maia
```

- [ ] **Step 3: Move embedded resource declaration in Terminal.csproj**

Today, `Terminal.csproj` has:

```xml
<EmbeddedResource Include="src/Maia/maia_agent.js" LogicalName="Terminal.Maia.maia_agent.js" />
```

Cut that line from `Terminal.csproj` and add to `src/Concord.Core/Concord.Core.csproj`, adjusting the path:

```xml
<ItemGroup>
  <EmbeddedResource Include="Maia/maia_agent.js" LogicalName="Terminal.Maia.maia_agent.js" />
</ItemGroup>
```

LogicalName stays the same so `GetManifestResourceStream("Terminal.Maia.maia_agent.js")` callers don't need to change.

- [ ] **Step 4: Build + test**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: pass. If a Maia test reads the resource stream, it should still find it because the LogicalName is preserved.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(core): move Maia bridge + embedded JS resource into Concord.Core (W1)

LogicalName 'Terminal.Maia.maia_agent.js' preserved so consumers don't
need to change. EmbeddedResource declaration moved from Terminal.csproj
to Concord.Core.csproj."
```

### Task 9: Move and refactor StudioProActions + StudioProActionServer

These two files **do** call Studio Pro APIs today. They need a refactor: replace direct Studio Pro service calls with calls through `HostServices.*`.

**Files:**
- Move: `src/StudioProActions.cs` → `src/Concord.Core/Mcp/StudioProActions.cs`
- Move: `src/StudioProActionServer.cs` → `src/Concord.Core/Mcp/StudioProActionServer.cs`
- Modify: both files to replace direct Studio Pro service calls with `HostServices.*`

- [ ] **Step 1: Audit Studio Pro types used in StudioProActions.cs**

Run: `Select-String -Path src/StudioProActions.cs -Pattern "Mendix\."`

Catalog every type used. Typical hits include:
- `ILocalRunConfigurationsService` — wrapped by `IRunConfigurationsHost`.
- `IApp` / project access — wrapped by `IStudioProAppHost`.
- Native `PostMessage` calls — those use Win32, not Mendix types, so they stay in Core.

If a type is used that isn't already covered by an Interop interface, add a method to the interface in Task 4 (and implement it in Host10x + Host11x in Tasks 13-14). Record the surface delta in `docs/superpowers/plans/2026-05-12-concord-w1-spike-notes.md` before continuing.

- [ ] **Step 2: Move the files**

```powershell
git mv src/StudioProActions.cs src/Concord.Core/Mcp/StudioProActions.cs
git mv src/StudioProActionServer.cs src/Concord.Core/Mcp/StudioProActionServer.cs
```

- [ ] **Step 3: Replace direct Studio Pro calls with HostServices.* in both files**

In each occurrence, swap a direct service injection (e.g., constructor parameter `ILocalRunConfigurationsService runConfigs`) for `HostServices.RunConfigurations.GetActive()` or equivalent. Constructor parameters that are now unused get deleted.

The exact edits depend on the current file contents — apply them iteratively, building after each replacement to keep diagnostics manageable.

Expected: after all replacements, Core builds without referencing `Mendix.StudioPro.ExtensionsAPI`.

- [ ] **Step 4: Build Core in isolation to confirm no ExtensionsAPI leak**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj`
Expected: `Build succeeded.` with zero references to Mendix.StudioPro.ExtensionsAPI in the dependency graph. Verify with `dotnet list src/Concord.Core/Concord.Core.csproj package` — no Mendix package should appear.

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build Terminal.sln`
Expected: `Build succeeded.` The Terminal.csproj host still builds because it provides the `HostServices.*` implementations (which we wire up in Tasks 13-14).

If `HostServices.Register` hasn't been called yet, runtime calls will throw `InvalidOperationException`. That's fine — runtime smoke is gated by Task 15. The build needs to succeed.

- [ ] **Step 6: Run all tests**

Run: `dotnet test Terminal.sln`
Expected: tests that don't exercise Studio Pro service calls pass; tests that DO will need a fake `IRunConfigurationsHost` etc. registered via `HostServices.Register(...)` in `[Fact]` setup. Update them as needed.

Each fake is straightforward — e.g., `FakeRunConfigsHost : IRunConfigurationsHost` returning a canned `RunConfigurationInfo`. Add these fakes to `tests/Concord.Core.Tests/Fakes/` as you go.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(core): route StudioProActions + ActionServer through HostServices (W1)

StudioProActions and StudioProActionServer move into
src/Concord.Core/Mcp/. Direct Mendix.StudioPro.ExtensionsAPI service
injection is replaced with calls through HostServices.*, so Core no
longer depends on the ExtensionsAPI NuGet. Hosts register their
service implementations at MEF activation."
```

---

## Phase 3 — Create the two host projects

### Task 10: Create Concord.Host11x.csproj scaffolding

**Files:**
- Create: `src/Concord.Host11x/Concord.Host11x.csproj`
- Modify: `Terminal.sln`

- [ ] **Step 1: Create the project file**

`src/Concord.Host11x/Concord.Host11x.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <AssemblyName>Concord.Host11x</AssemblyName>
    <RootNamespace>Concord.Host11x</RootNamespace>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
    <Version>4.1.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Terminal.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Concord.Core\Concord.Core.csproj" />
    <PackageReference Include="Mendix.StudioPro.ExtensionsAPI" Version="11.6.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution**

Run: `dotnet sln Terminal.sln add src/Concord.Host11x/Concord.Host11x.csproj`

- [ ] **Step 3: Build**

Run: `dotnet build src/Concord.Host11x/Concord.Host11x.csproj`
Expected: `Build succeeded.` (empty project, no entry points yet).

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Host11x/ Terminal.sln
git commit -m "build: add Concord.Host11x project scaffolding (W1)

Binds Mendix.StudioPro.ExtensionsAPI 11.6.2 and references Concord.Core.
MEF entry classes move from Terminal.csproj into here in the next task."
```

### Task 11: Move MEF entry classes from Terminal.csproj to Concord.Host11x

**Files moved + renamed (host-specific namespace):**

```
src/TerminalMenuExtension.cs     → src/Concord.Host11x/MenuExtensions/TerminalMenuExtension.cs
src/TerminalPaneExtension.cs     → src/Concord.Host11x/Pane/TerminalPaneExtension.cs
src/TerminalPaneViewModel.cs     → src/Concord.Host11x/Pane/TerminalPaneViewModel.cs
src/TerminalWebServer.cs         → src/Concord.Host11x/Ui/TerminalWebServer.cs
src/RunStateProbe.cs             → src/Concord.Host11x/Interop/RunStateProbe.cs
src/StudioProUiAutomation.cs     → src/Concord.Host11x/Interop/StudioProUiAutomation.cs
src/IStudioProUiAutomation.cs    → src/Concord.Host11x/Interop/IStudioProUiAutomation.cs
src/IRunStateProbe.cs            → src/Concord.Host11x/Interop/IRunStateProbe.cs
```

These files import `Mendix.StudioPro.ExtensionsAPI` types and must live in a host project, not Core. Confirm with `Select-String -Path <file> -Pattern "Mendix\.StudioPro"` before moving any file not on this list — if the v4.2.2 src/ tree drifts again, an additional Studio-Pro-typed file should land in this task too.

Note on `IRunStateProbe.cs` / `RunStateProbe.cs` and `IStudioProUiAutomation.cs` / `StudioProUiAutomation.cs`: if `Select-String` returns no `Mendix.StudioPro` match for these (i.e., they're pure interfaces or use service injection through abstractions), move them to `Concord.Core/Interop/` instead and document the change in the commit. The plan defaults them to Host11x because the historical pattern of names suggests Studio Pro coupling, but verify each.

The `TerminalWebServer.cs` move duplicates the full file into each host project (mirrored in Task 13). The HTTP route handling code itself is Studio-Pro-agnostic; only Studio Pro's `IWebServer` registration is host-specific. A cleaner refactor that splits the routing logic into Core and keeps only the registration adapter per host is a W2 polish item — not in scope for W1.

- [ ] **Step 1: Move the files**

```powershell
git mv src/TerminalMenuExtension.cs  src/Concord.Host11x/MenuExtensions/TerminalMenuExtension.cs
git mv src/TerminalPaneExtension.cs  src/Concord.Host11x/Pane/TerminalPaneExtension.cs
git mv src/TerminalPaneViewModel.cs  src/Concord.Host11x/Pane/TerminalPaneViewModel.cs
git mv src/RunStateProbe.cs          src/Concord.Host11x/Interop/RunStateProbe.cs
git mv src/StudioProUiAutomation.cs  src/Concord.Host11x/Interop/StudioProUiAutomation.cs
git mv src/IStudioProUiAutomation.cs src/Concord.Host11x/Interop/IStudioProUiAutomation.cs
git mv src/IRunStateProbe.cs         src/Concord.Host11x/Interop/IRunStateProbe.cs
```

- [ ] **Step 2: Change namespace declarations in the moved files**

In each moved file, change `namespace Terminal;` (or `namespace Terminal.{Sub};`) to `namespace Concord.Host11x;` (or `Concord.Host11x.{Sub};`). MEF resolves by attribute and interface, not namespace, so this doesn't affect activation.

Add `using Terminal;` and `using Terminal.Interop;` at the top of each file so references to Core types still resolve.

- [ ] **Step 3: Add a Host11xEntry.cs that initializes HostContext + HostServices**

`src/Concord.Host11x/Host11xEntry.cs`:

```csharp
namespace Concord.Host11x;

using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI;
using Terminal;
using Terminal.Interop;

/// <summary>
/// Single point of MEF activation for the 11.x host. Wires HostContext +
/// HostServices on first use; other MEF exports in this assembly take a
/// dependency on Host11xEntry via [Import] to guarantee it runs first.
/// </summary>
[Export(typeof(Host11xEntry))]
public class Host11xEntry
{
    private static int _initialized;

    [ImportingConstructor]
    public Host11xEntry()
    {
        if (System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0) return;

        HostContext.Initialize(TargetMode.Studio11x);
        HostServices.Register(
            app: new Interop.StudioProAppHost11x(),
            runConfigs: new Interop.RunConfigurationsHost11x(),
            runState: new Interop.RunStateHost11x(),
            moduleImport: new Interop.ModuleImportHost11x());
    }
}
```

Wire `[Import(typeof(Host11xEntry))] Host11xEntry _entry;` into each MEF-exported class in Concord.Host11x (menu extension, pane extension) so MEF instantiates `Host11xEntry` before them.

- [ ] **Step 4: Implement the four Interop stubs**

For now, each implementation can be a thin wrapper calling Studio Pro's services via constructor-injected `IServiceProvider` or via direct `[Import]`. Copy the existing code paths from the pre-move files where applicable.

`src/Concord.Host11x/Interop/StudioProAppHost11x.cs`:

```csharp
namespace Concord.Host11x.Interop;

using Mendix.StudioPro.ExtensionsAPI;
using Terminal.Interop;

public class StudioProAppHost11x : IStudioProAppHost
{
    // The real implementation reads from Studio Pro's IApp service.
    // For W1 we delegate to whatever the existing code did before the move;
    // adapt the body to call the IApp accessor that TerminalPaneExtension
    // already uses.
    public string ProjectPath => throw new NotImplementedException(
        "Wire to IApp.ProjectDirectory or equivalent — adapt from pre-move code.");
    public string ProjectName => throw new NotImplementedException();
    public bool HasOpenProject => throw new NotImplementedException();
}
```

Repeat the stub pattern for `RunConfigurationsHost11x`, `RunStateHost11x`, `ModuleImportHost11x`. Each starts as `throw new NotImplementedException()` and gets filled in as the runtime smoke test in Task 15 hits the calls.

This **deferred wiring** is intentional: the build must succeed first; the runtime behavior comes back online incrementally in Task 15.

- [ ] **Step 5: Move shared Content includes from Terminal.csproj to Concord.Core.csproj**

The current `Terminal.csproj` declares four shared resource trees as Content (these ship in the deploy output and are loaded at runtime by SkillInstaller, RulesInstaller, and the web server):

```xml
<Content Include="wwwroot/**/*">...</Content>
<Content Include="skills/**/*">...</Content>
<Content Include="skills-mac/**/*">...</Content>
<Content Include="rules/**/*">...</Content>
```

Move all four into `src/Concord.Core/Concord.Core.csproj`, adjusting the include paths to be relative to the Core project (`..\..\wwwroot\**\*`, `..\..\skills\**\*`, etc.). Set `<CopyToOutputDirectory>Always</CopyToOutputDirectory>` and `<Visible>false</Visible>` on each. The skills/rules trees stay at the repo root (no folder move needed — they're stable as-is).

- [ ] **Step 6: Convert the old Terminal.csproj to a deprecation shim**

Edit `Terminal.csproj`:
- Change `<AssemblyName>Concord</AssemblyName>` to `<AssemblyName>Concord.Legacy</AssemblyName>` (will be deleted in Task 16, but for now we want a clean build).
- Remove any `<Compile Include="src/**" />` (everything moved).
- Keep `<Compile Remove="tests/**" />`, `<Compile Remove="debug/**" />`.
- Drop the `Mendix.StudioPro.ExtensionsAPI` PackageReference (lives in host projects now).
- Drop the `Maia/maia_agent.js` EmbeddedResource (moved to Core in Task 8).
- Drop the four bulk Content includes (moved to Core in Step 5 above).
- Drop the `manifest.json` Content include (will be replaced in Task 14).

The result is a near-empty `Terminal.csproj` that still builds but produces an unused DLL we delete in Task 16.

- [ ] **Step 6: Build the full solution**

Run: `dotnet build Terminal.sln`
Expected: `Build succeeded.` All assemblies compile. Runtime behavior is broken until Task 15 wires the stubs — that's expected.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(host11x): move MEF entry + Studio-Pro-typed classes into Concord.Host11x (W1)

TerminalMenuExtension, TerminalPaneExtension, TerminalPaneViewModel,
RunStateProbe, StudioProUiAutomation move out of Terminal.csproj into
Concord.Host11x. New Host11xEntry initializes HostContext + HostServices
at MEF activation. Interop wrappers around Studio Pro services start as
stubs; runtime wiring lands in Task 15."
```

### Task 12: Create Concord.Host10x.csproj scaffolding

**Files:**
- Create: `src/Concord.Host10x/Concord.Host10x.csproj`
- Modify: `Terminal.sln`

- [ ] **Step 1: Create the project file**

`src/Concord.Host10x/Concord.Host10x.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <AssemblyName>Concord.Host10x</AssemblyName>
    <RootNamespace>Concord.Host10x</RootNamespace>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
    <Version>4.1.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Concord.Core\Concord.Core.csproj" />
    <PackageReference Include="Mendix.StudioPro.ExtensionsAPI" Version="$(ExtensionsApi10xVersion)" />
  </ItemGroup>
</Project>
```

`$(ExtensionsApi10xVersion)` is read from `Directory.Build.props`. Add to `Directory.Build.props.example`:

```xml
<Project>
  <PropertyGroup>
    <MendixDeployTarget></MendixDeployTarget>
    <!-- Set this to the Mendix.StudioPro.ExtensionsAPI version compatible with Studio Pro 10.24.13.
         Discovered in spike Task 1; record in 2026-05-12-concord-w1-spike-notes.md. -->
    <ExtensionsApi10xVersion>10.21.0</ExtensionsApi10xVersion>
  </PropertyGroup>
</Project>
```

The exact version comes from spike Step 1. If different, update both the comment and the default value.

- [ ] **Step 2: Add to solution**

Run: `dotnet sln Terminal.sln add src/Concord.Host10x/Concord.Host10x.csproj`

- [ ] **Step 3: Build**

Run: `dotnet build src/Concord.Host10x/Concord.Host10x.csproj`
Expected: `Build succeeded.` (empty project).

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Host10x/ Terminal.sln Directory.Build.props.example
git commit -m "build: add Concord.Host10x project scaffolding (W1)

References Concord.Core + Mendix.StudioPro.ExtensionsAPI at the 10.24.13-
compatible version (configured via \$(ExtensionsApi10xVersion))."
```

### Task 13: Implement Host10x MEF entry + Interop stubs

**Files:**
- Create: `src/Concord.Host10x/Host10xEntry.cs`
- Create: `src/Concord.Host10x/MenuExtensions/TerminalMenuExtension.cs`
- Create: `src/Concord.Host10x/Pane/TerminalPaneExtension.cs`
- Create: `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs`
- Create: `src/Concord.Host10x/Ui/TerminalWebServer.cs`
- Create: `src/Concord.Host10x/Interop/StudioProAppHost10x.cs`
- Create: `src/Concord.Host10x/Interop/RunConfigurationsHost10x.cs`
- Create: `src/Concord.Host10x/Interop/RunStateHost10x.cs`
- Create: `src/Concord.Host10x/Interop/ModuleImportHost10x.cs`
- Create: `src/Concord.Host10x/Interop/RunStateProbe.cs`
- Create: `src/Concord.Host10x/Interop/StudioProUiAutomation.cs`

Each file mirrors its 11.x sibling but uses 10.24.13-compatible ExtensionsAPI types. Where the 10.x API surface differs, the spike notes file (from Task 1) records the deltas; apply those deltas here.

- [ ] **Step 1: Create Host10xEntry**

`src/Concord.Host10x/Host10xEntry.cs`:

```csharp
namespace Concord.Host10x;

using System.ComponentModel.Composition;
using Terminal;
using Terminal.Interop;

[Export(typeof(Host10xEntry))]
public class Host10xEntry
{
    private static int _initialized;

    [ImportingConstructor]
    public Host10xEntry()
    {
        if (System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0) return;

        HostContext.Initialize(TargetMode.Studio10x);
        HostServices.Register(
            app: new Interop.StudioProAppHost10x(),
            runConfigs: new Interop.RunConfigurationsHost10x(),
            runState: new Interop.RunStateHost10x(),
            moduleImport: new Interop.ModuleImportHost10x());
    }
}
```

- [ ] **Step 2: Copy host classes from Host11x, adapt for 10.x API differences**

For each file (`TerminalMenuExtension`, `TerminalPaneExtension`, `TerminalPaneViewModel`, the Interop wrappers), start by copying the Host11x version verbatim, then adjust for any API changes recorded in the spike notes.

Typical adjustments based on Mendix Extensions API drift (verify in spike):
- Some types in 11.x were renamed from 10.x (e.g., service interface names).
- Some methods that exist on 11.x's `IApp` aren't on 10.x's — those calls need a `MCPExtension/backport-10x/` reference or a different code path.
- The `[Export(typeof(IMenuExtension))]` MEF contract may differ. Check the spike notes.

If an API genuinely doesn't exist on 10.x (e.g., a Maia panel finder), the 10.x implementation returns `not-supported` and the corresponding tool path returns `escalation: manual` in W2/W3.

- [ ] **Step 3: Reference MCPExtension's backport-10x as a guide**

The `c:\Extensions\MCPExtension\backport-10x\` tree has already solved many 10.x API-drift problems. **Do not copy code from there in W1** (that's W2's source-merge job). But do read it as a reference for which APIs to use on 10.24.13.

- [ ] **Step 4: Build the 10x host**

Run: `dotnet build src/Concord.Host10x/Concord.Host10x.csproj`
Expected: `Build succeeded.` Any 10.x-only compile error means the API spec from spike notes needs another pass.

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build Terminal.sln`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/Concord.Host10x/
git commit -m "feat(host10x): implement MEF entry + Studio Pro 10.24.13 interop (W1)

Mirrors Host11x structure with 10.x-compatible API calls. Interop wrappers
implement IStudioProAppHost, IRunConfigurationsHost, IRunStateHost,
IModuleImportHost against ExtensionsAPI 10.24.13. MEF activation
initializes HostContext.Studio10x + HostServices."
```

### Task 14: Manifest layout + DeployToMendix update

Apply the result of the Task 1 spike. Two branches below — execute the one matching the spike outcome.

**Files (depending on layout):**

If **single-manifest** layout works:
- Modify: `manifest.json` to list both DLLs
- Modify: Host11x.csproj + Host10x.csproj to `<Content Include="manifest.json">` (shared)
- Modify: `DeployToMendix` in Host11x.csproj + Host10x.csproj

If **two-subfolder** layout is required:
- Create: `src/Concord.Host11x/manifest.json`
- Create: `src/Concord.Host10x/manifest.json`
- Delete: top-level `manifest.json`
- Modify: `DeployToMendix` to write to subfolders

#### Branch A — single-manifest

- [ ] **Step 1: Update `manifest.json`**

Replace contents:

```json
{ "mx_extensions": ["Concord.Host10x.dll", "Concord.Host11x.dll"] }
```

- [ ] **Step 2: Add manifest as Content in both host projects**

In both `Concord.Host10x.csproj` and `Concord.Host11x.csproj`, add:

```xml
<ItemGroup>
  <Content Include="..\..\manifest.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>
</ItemGroup>
```

The Content include uses a relative path so both projects copy the same file.

- [ ] **Step 3: Move BuildUi + DeployToMendix targets into Host11x.csproj**

Move the `<Target Name="BuildUi">` and `<Target Name="DeployToMendix">` blocks from the old `Terminal.csproj` into `Concord.Host11x.csproj`. Add the equivalent `DeployToMendix` target to `Concord.Host10x.csproj` — but with two changes:
- It runs `AfterTargets="PostBuildEvent"` independently.
- It copies its own `$(TargetDir)` (which contains `Concord.Host10x.dll` + `Concord.Core.dll` if `CopyLocalLockFileAssemblies` is on).

To avoid copying Concord.Core.dll twice into `extensions/Concord/`, set `<CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>` in `Concord.Host10x.csproj` AND ensure the Host11x DeployToMendix copies Core first, then both hosts xcopy on top. xcopy `/y` overwrite is safe.

#### Branch B — two-subfolder

- [ ] **Step 1: Create per-host manifests**

`src/Concord.Host11x/manifest.json`:

```json
{ "mx_extensions": ["Concord.Host11x.dll"] }
```

`src/Concord.Host10x/manifest.json`:

```json
{ "mx_extensions": ["Concord.Host10x.dll"] }
```

Add `<Content Include="manifest.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>` to each host project.

- [ ] **Step 2: Delete top-level manifest**

```bash
git rm manifest.json
```

- [ ] **Step 3: Update DeployToMendix targets**

`Concord.Host11x.csproj` DeployToMendix target writes to `<target>\extensions\Concord\11x\`:

```xml
<Target Name="DeployToMendix" AfterTargets="PostBuildEvent" Condition="'$(MendixDeployTarget)' != ''">
  <ItemGroup>
    <_MendixDeployTargets Include="$(MendixDeployTarget)" />
  </ItemGroup>
  <Message Importance="high" Text="Deploying to %(_MendixDeployTargets.Identity)/extensions/Concord/11x" />
  <Exec Condition="'$(OS)' == 'Windows_NT'"
        Command="xcopy /y /s /i /q &quot;$(TargetDir)*&quot; &quot;%(_MendixDeployTargets.Identity)\extensions\Concord\11x&quot;" />
  <Exec Condition="'$(OS)' != 'Windows_NT'"
        Command="mkdir -p &quot;%(_MendixDeployTargets.Identity)/extensions/Concord/11x&quot; &amp;&amp; cp -R &quot;$(TargetDir).&quot; &quot;%(_MendixDeployTargets.Identity)/extensions/Concord/11x/&quot;" />
  <!-- Concord.Core, wwwroot, skills live under Core/ -->
  <Exec Condition="'$(OS)' == 'Windows_NT'"
        Command="xcopy /y /s /i /q &quot;$(TargetDir)Concord.Core.dll&quot; &quot;%(_MendixDeployTargets.Identity)\extensions\Concord\Core\&quot; &amp; xcopy /y /s /i /q &quot;$(TargetDir)wwwroot\&quot; &quot;%(_MendixDeployTargets.Identity)\extensions\Concord\Core\wwwroot\&quot; &amp; xcopy /y /s /i /q &quot;$(TargetDir)skills\&quot; &quot;%(_MendixDeployTargets.Identity)\extensions\Concord\Core\skills\&quot;" />
</Target>
```

(macOS equivalent uses `cp -R`. The shared-Core split keeps `wwwroot/` and `skills/` deployed exactly once.)

`Concord.Host10x.csproj` mirrors this but writes to `<target>\extensions\Concord\10x\`.

- [ ] **Step 4: Update the extension-cache refresh logic**

The current `DeployToMendix` includes a step that refreshes `<project>/.mendix-cache/extensions-cache/<guid>/`. Update both per-host targets to also refresh the corresponding subfolder under the cache. The cache path inside Studio Pro mirrors the source layout — so write to `<cache-guid>/Concord/11x/` (or `10x/`).

#### Both branches converge — Step 5+

- [ ] **Step 5: Build + verify deploy output**

Set `MendixDeployTarget` in `Directory.Build.props` to a real Mendix 11.x project. Run:

```bash
dotnet build Terminal.sln
```

Expected: `Build succeeded.` Check the target's `extensions/Concord/` folder contains the right structure (one manifest with two DLLs **or** subfolder structure depending on branch). Inspect manually.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "build: manifest layout + DeployToMendix for two-host structure (W1)

Spike-decided layout (<single | two-subfolders>) is now wired in.
Studio Pro 10.x loads Concord.Host10x.dll; 11.x loads Concord.Host11x.dll.
Concord.Core.dll is shared (deployed once under Core/ in the
two-subfolder case)."
```

---

## Phase 4 — Runtime wiring + smoke test

### Task 15: Wire the Interop stubs to real Studio Pro services

In Tasks 11 and 13 we set the Interop wrappers to `throw NotImplementedException()`. This task fills them in.

**Files:**
- Modify: `src/Concord.Host11x/Interop/StudioProAppHost11x.cs`
- Modify: `src/Concord.Host11x/Interop/RunConfigurationsHost11x.cs`
- Modify: `src/Concord.Host11x/Interop/RunStateHost11x.cs`
- Modify: `src/Concord.Host11x/Interop/ModuleImportHost11x.cs`
- Modify: `src/Concord.Host10x/Interop/StudioProAppHost10x.cs`
- ... (and the three Host10x equivalents)

- [ ] **Step 1: Implement Host11x.StudioProAppHost11x**

The implementation accesses Studio Pro's `IApp` via a MEF [Import]. Since the wrapper instance is constructed manually (not via MEF), it captures the IApp at construction time via a static service-locator pattern, or via the constructor of `Host11xEntry`.

Simplest approach: have `Host11xEntry` receive `IApp` via `[ImportingConstructor]`, pass it into the Interop wrappers when registering:

```csharp
[Export(typeof(Host11xEntry))]
public class Host11xEntry
{
    [ImportingConstructor]
    public Host11xEntry(Mendix.StudioPro.ExtensionsAPI.Services.IApp app, /* other services */ ...)
    {
        if (System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0) return;
        HostContext.Initialize(TargetMode.Studio11x);
        HostServices.Register(
            app: new Interop.StudioProAppHost11x(app),
            runConfigs: new Interop.RunConfigurationsHost11x(/* service */),
            runState: new Interop.RunStateHost11x(/* service */),
            moduleImport: new Interop.ModuleImportHost11x(app));
    }
}
```

The exact service names (`IApp`, `ILocalRunConfigurationsService`, etc.) come from the existing pre-move code in Tasks 11's git history.

`StudioProAppHost11x.cs`:

```csharp
namespace Concord.Host11x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Services;
using Terminal.Interop;

public class StudioProAppHost11x : IStudioProAppHost
{
    private readonly IApp _app;
    public StudioProAppHost11x(IApp app) { _app = app; }

    public string ProjectPath => _app.Root?.DirectoryPath ?? string.Empty;
    public string ProjectName => System.IO.Path.GetFileNameWithoutExtension(_app.Root?.DirectoryPath ?? string.Empty);
    public bool HasOpenProject => _app.Root != null;
}
```

Adjust property names if the actual `IApp` surface differs (verify against the pre-move code).

- [ ] **Step 2: Repeat for the three remaining Host11x wrappers**

`RunConfigurationsHost11x` wraps `ILocalRunConfigurationsService.GetActiveConfiguration()`. `RunStateHost11x` wraps whatever the current `RunStateProbe` does. `ModuleImportHost11x.IsModuleImported` enumerates `_app.Root.Modules`; `ImportFromMpk` is a W4 concern and can stay `throw new NotSupportedException("Module import lands in W4")` for now.

- [ ] **Step 3: Repeat for Host10x wrappers**

The 10.x API names may differ (spike notes capture this). Where the call shape is the same, the code is identical. Where it differs, document why in a comment.

- [ ] **Step 4: Run existing tests**

Run: `dotnet test Terminal.sln`
Expected: all pre-existing tests pass. Some `Terminal.Tests` may break because they relied on direct construction of `RunStateProbe` — update those tests to either register a fake `IRunStateHost` via `HostServices` or skip them as W2 cleanup.

- [ ] **Step 5: Manual smoke test on Studio Pro 11.10**

1. Set `MendixDeployTarget` to a Mendix 11.10 project.
2. `dotnet build Terminal.sln`.
3. Open the project in Studio Pro 11.10.
4. Open the Concord pane.
5. Verify the terminal opens, settings modal loads, MCP server starts (check `<project>/resources/terminal.log` for the "MCP server listening on port 7783" message).
6. Run `curl http://localhost:7783/health`. Expected: 200 OK with `concord-mcp` info.
7. From a Claude Code CLI in the terminal, list MCP tools. Expected: existing tool surface (UI actions + Maia tools) unchanged from v4.1.

If any check fails, fix and re-test. Capture failures in commit history; do not move to Step 6 until smoke passes.

- [ ] **Step 6: Manual smoke test on Studio Pro 10.24.13**

1. Set `MendixDeployTarget` to a Mendix 10.24.13 project.
2. `dotnet build Terminal.sln`.
3. Launch Studio Pro 10.24.13 with `--enable-extension-development`.
4. Open the Concord pane.
5. Same checks as Step 5.
6. Note in spike notes which (if any) tools fail on 10.x — those are W2/W3 cleanup, not W1.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(host): wire Interop wrappers to real Studio Pro services (W1)

Host11xEntry + Host10xEntry now receive Studio Pro services via MEF
[ImportingConstructor] and pass them to the Interop wrappers. Smoke
test passes on Studio Pro 11.10 and Studio Pro 10.24.13: pane opens,
MCP server starts, existing tool surface unchanged."
```

### Task 16: Delete the legacy Terminal.csproj shim

By this point, `Terminal.csproj` is empty (all sources moved). Delete it.

- [ ] **Step 1: Remove the project from the solution**

Run: `dotnet sln Terminal.sln remove Terminal.csproj`

- [ ] **Step 2: Delete the file**

```bash
git rm Terminal.csproj
```

- [ ] **Step 3: Update test project references**

`tests/Terminal.Tests.csproj` currently references `Terminal.csproj`. Replace with references to `Concord.Core` and `Concord.Host11x`:

```xml
<ItemGroup>
  <ProjectReference Include="..\src\Concord.Core\Concord.Core.csproj" />
  <ProjectReference Include="..\src\Concord.Host11x\Concord.Host11x.csproj" />
</ItemGroup>
```

- [ ] **Step 4: Build + test**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "build: delete legacy Terminal.csproj after full split (W1)

All source files now live in Concord.Core, Concord.Host10x, or
Concord.Host11x. The transitional Terminal.csproj is no longer needed."
```

---

## Phase 5 — CI matrix + documentation

### Task 17: Add CI matrix for both Studio Pro versions

**Files:**
- Modify: `.github/workflows/build.yml` (or whichever workflow exists today)

- [ ] **Step 1: Inspect current workflow**

Run: `Get-ChildItem .github/workflows -File`

Read the existing build workflow to understand the current matrix. If no workflow exists, create one based on the standard `dotnet build && dotnet test` pattern.

- [ ] **Step 2: Add matrix dimensions**

The build is the same regardless of Studio Pro version (the test matrix is what differs — e2e smoke isn't in scope for this CI). Add a job that builds for both ExtensionsAPI versions:

```yaml
jobs:
  build-and-test:
    strategy:
      matrix:
        os: [windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore Terminal.sln
      - run: dotnet build Terminal.sln --no-restore
      - run: dotnet test Terminal.sln --no-build --verbosity normal
```

E2E smoke against a real Studio Pro install is a follow-up CI job and is out of scope for W1.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/
git commit -m "ci: build + test on Windows and macOS for both host projects (W1)

E2E smoke against real Studio Pro installs is a follow-up CI job."
```

### Task 18: Update DEPLOYING.md + README

**Files:**
- Modify: `DEPLOYING.md`
- Modify: `README.md`

- [ ] **Step 1: Update DEPLOYING.md**

Add a section explaining the new layout:

```markdown
## Multi-version structure (Concord 5.x)

Concord 5.x ships as three DLLs:

- `Concord.Core.dll` — shared, no Studio Pro reference.
- `Concord.Host11x.dll` — loaded by Studio Pro 11.x.
- `Concord.Host10x.dll` — loaded by Studio Pro 10.24.13+.

The deploy folder layout after `dotnet build` is:

<paste the layout chosen in Task 14 here, single or two-subfolder>

Studio Pro picks the host DLL that matches its version. The wrong-version DLL fails to resolve its `Mendix.StudioPro.ExtensionsAPI` types and is silently skipped by the MEF loader.
```

- [ ] **Step 2: Update README's project-layout section**

Replace the `## Project layout` block in `README.md` to reflect the new tree (Core + Host10x + Host11x).

- [ ] **Step 3: Commit**

```bash
git add README.md DEPLOYING.md
git commit -m "docs: update README + DEPLOYING for two-host layout (W1)"
```

### Task 19: Bump version + CHANGELOG entry

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `src/Concord.Core/Concord.Core.csproj` (Version)
- Modify: `src/Concord.Host10x/Concord.Host10x.csproj` (Version)
- Modify: `src/Concord.Host11x/Concord.Host11x.csproj` (Version)

- [ ] **Step 1: Bump version to 5.0.0-alpha.1 in each csproj**

All three host/core csproj files get `<Version>5.0.0-alpha.1</Version>`. `InformationalVersion` becomes `5.0.0-alpha.1+w1-foundation`.

- [ ] **Step 2: Add CHANGELOG entry**

Prepend to `CHANGELOG.md`:

```markdown
## 5.0.0-alpha.1 — W1 foundation

**Architecture change.** Concord now ships as `Concord.Core.dll` plus two host DLLs (`Concord.Host10x.dll`, `Concord.Host11x.dll`). Studio Pro picks the host matching its API version. No new user-facing features in this alpha — it's the structural foundation for cross-version support and the W2 MCPExtension merge.

- Single Concord build family now loads on Studio Pro 10.24.13 through 11.x.
- All Studio Pro service access routes through `HostServices.*` interfaces in Core.
- Manifest layout: <single-manifest | two-subfolder> (decided in W1 discovery spike).
- No changes to the tool catalog yet — existing UI actions + Maia tools work as in v4.1 on 11.x; on 10.x the same set works subject to API parity (see release notes for any 10.x-specific gaps).
```

- [ ] **Step 3: Commit**

```bash
git add CHANGELOG.md src/Concord.Core/Concord.Core.csproj \
        src/Concord.Host10x/Concord.Host10x.csproj \
        src/Concord.Host11x/Concord.Host11x.csproj
git commit -m "chore: bump to 5.0.0-alpha.1 (W1 foundation)"
```

---

## Final verification

### Task 20: Full-stack smoke check

- [ ] **Step 1: Clean build from scratch**

```bash
git clean -fdx -- bin obj
dotnet build Terminal.sln
```

Expected: clean build, no warnings.

- [ ] **Step 2: All tests pass**

```bash
dotnet test Terminal.sln --verbosity normal
```

Expected: all green. Capture the test count and add to spike notes.

- [ ] **Step 3: Manual smoke checklist**

Deploy to one 10.24.13 project and one 11.10 project. Verify on each:

- Concord pane opens with no errors in `terminal.log`.
- Settings modal renders all sections.
- A new PTY tab opens; typing echoes; resize works.
- `concord-mcp` HTTP server responds on 7783 (or fallback port) with 200 from `/health`.
- From a Claude Code CLI in the terminal, `claude mcp list` shows `concord-mcp` connected.
- One UI action tool (`save_all`) successfully fires Ctrl+S to Studio Pro.

If any check fails on either version, file a bug and decide whether it gates W1 release. UI-action gaps on 10.x are W2 concerns; pane-load failures are W1 gates.

- [ ] **Step 4: Tag the W1 release**

```bash
git tag -a v5.0.0-alpha.1 -m "Concord 5.0.0-alpha.1 — W1 cross-version foundation"
```

Pushing the tag is a user decision — do not push without explicit confirmation.

---

## Self-review notes

This plan covers:

- **Spec coverage:**
  - W1 architecture (repo split, two hosts, shared Core) — Tasks 2-13.
  - Manifest layout decision — Task 1 spike + Task 14 application.
  - DeployToMendix update — Task 14.
  - CI matrix (build-only; e2e deferred) — Task 17.
  - Backward-compatible settings JSON shape — preserved by moving `TerminalSettings.cs` unchanged (Task 6); no schema change.
  - Migration path documentation — DEPLOYING.md update (Task 18).
- **Spec items deferred to W2-W4:**
  - SPMCP source merge (W2).
  - 11.x curated allowlist (W3).
  - Family-level toggles, mendix-tool-map skill pack, sample-data button (W4).
  - End-to-end smoke CI against real Studio Pro installs (post-W1 CI work).

The plan assumes the spike (Task 1) returns workable answers. If it doesn't — e.g., neither manifest layout loads, or MEF on 10.x is fundamentally different — the plan needs revision before proceeding past Task 1.

**Type consistency check:** `IStudioProAppHost`, `IRunConfigurationsHost`, `IRunStateHost`, `IModuleImportHost`, `HostContext`, `TargetMode` use consistent names across all tasks.

**Test coverage:**
- HostContext: unit tests in Task 3.
- HostServices stubs: tested implicitly by the rest of the build + smoke checklist; explicit unit tests are not added in W1 because the registry is a thin pass-through and the runtime smoke catches misuse.
- Interop wrappers: smoke-tested in Task 15. Adding unit tests with mocked Studio Pro services is a follow-up if drift becomes an issue.
