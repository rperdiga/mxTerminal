# Concord Shim — Mac Load-Context Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Concord v5.0.3 install and run successfully on Studio Pro 11.10 on macOS (and 10.24.13 on macOS) from the same `.mxmodule` that already works on Windows, by replacing `AssemblyDependencyResolver` (which fails on Mac's .NET hosting model) with manual file-system probing inside `ConcordHostLoadContext`.

**Architecture:** Three managed code changes inside `src/Concord.Shim/` (drop `AssemblyDependencyResolver`; override `Load` and `LoadUnmanagedDll` with file-system probes; verify `Assembly.Location` usage), plus one release-step change to the `ConcordPublisher` wrapper module's version metadata. The shim's `AssemblyLoadContext` isolation architecture and `Resolving`-event-based cross-context bouncing are preserved unchanged — only the dependency-probing mechanism inside the load context changes.

**Tech Stack:** .NET 8, C# 12, xUnit + FluentAssertions (matches `Concord.Core.Tests`), Studio Pro 10.24.13 + 11.10 on both Windows and macOS for smoke validation.

**Pre-condition:** Source for `src/Concord.Shim/` and any associated test project live on the dev's Windows machine (PDB path inside deployed DLL: `C:\Extensions\Terminal\src\Concord.Shim\…`). This plan must be executed on Windows. The Mac-side repo is for spec/plan authoring only.

**Reference spec:** [`docs/superpowers/specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md`](../specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md)

---

## File Structure

Each file has one clear responsibility. Changes are minimal-diff against the current Phase 1 shim.

| File | Action | Responsibility |
|---|---|---|
| `src/Concord.Shim/ConcordHostLoadContext.cs` | Modify | Removes `AssemblyDependencyResolver` usage. Adds `Load(AssemblyName)` override that probes `_hostFolder`. Adds `LoadUnmanagedDll(string)` override that probes `runtimes/{rid}/native/`. Adds internal helpers `TryResolveAssemblyPath`, `TryResolveNativePath`, `RidFallbackChain` for testability. |
| `src/Concord.Shim/RuntimeHostLocator.cs` | Audit (modify only if broken) | Must compute extension-folder root via `Assembly.Location`, never `AppDomain.CurrentDomain.BaseDirectory`. |
| `src/Concord.Shim/Concord.Shim.csproj` | Audit (modify only if broken) | Must ensure `runtimes/<rid>/native/*` is copied into build output for both `osx-arm64` and `osx-x64` (so the build's `runtimes/` tree ends up in the published bundle). |
| `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs` | Create or modify | Five unit tests covering managed `Load`, unmanaged `LoadUnmanagedDll`, RID fallback, and the no-`AssemblyDependencyResolver` sanity assertion. Mirrors `tests/Concord.Core.Tests/` patterns (xUnit + FluentAssertions). |

The plan assumes the test project `tests/Concord.Shim.Tests/` already exists on Windows (the deployed shim DLL's `strings` output references `Concord.Shim.Tests` as a known name). If it doesn't exist yet, Task 1 creates it; otherwise Task 1 confirms and skips.

---

## Task 0: Pre-flight source audit

**Files:** None — read-only.

This plan was written from the Mac side without sight of the actual `src/Concord.Shim/` source. Class names, method names, and existing structure may differ slightly from this plan's pseudocode. The first task is to read the actual source and document any drift before touching code.

- [ ] **Step 1: Open `src/Concord.Shim/` on Windows and list its files**

Run:
```powershell
Get-ChildItem -Recurse -Path src\Concord.Shim\ -File | Select-Object FullName
```

Expected: at minimum a `Concord.Shim.csproj`, a `ConcordHostLoadContext.cs` (the class name in the deployed DLL strings — confirmed). Note any structural drift from the spec's File Structure table.

- [ ] **Step 2: Read `ConcordHostLoadContext.cs` in full and record the exact class shape**

Capture:
- Constructor signature (does it take a host folder, a runtimes folder, both, neither?)
- Whether `AssemblyDependencyResolver` is used (it is, per `strings` evidence — confirm)
- Existing `Resolving` event handler logic — capture verbatim; it stays unchanged
- Whether `Load(AssemblyName)` is already overridden
- Whether `LoadUnmanagedDll(string)` is already overridden

- [ ] **Step 3: Read `RuntimeHostLocator.cs` and grep for `BaseDirectory`**

Run:
```powershell
Select-String -Path src\Concord.Shim\*.cs -Pattern "BaseDirectory|Assembly.Location"
```

Record which method computes the extension folder root and whether it uses `Assembly.Location` (correct) or `AppDomain.CurrentDomain.BaseDirectory` (broken on Mac).

- [ ] **Step 4: Check whether `tests/Concord.Shim.Tests/` exists**

Run:
```powershell
Test-Path tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj
```

If True: read it to confirm xUnit + FluentAssertions (matches `Concord.Core.Tests`). If False: Task 1 will create it.

- [ ] **Step 5: Reconcile any drift before proceeding**

If the source structure differs materially from this plan's pseudocode (e.g., class is `ConcordLoadContext` not `ConcordHostLoadContext`, or `_hostFolder` field is named `_binPath`, etc.), update the plan's later steps' code snippets in place. Do not silently proceed against a stale plan — the engineer 6 months from now reading the merged PR alongside the plan needs them to match.

No commit for this task — it's read-only audit work that informs the rest.

---

## Task 1: Test project scaffolding (create-or-confirm)

**Files:**
- Maybe-create: `tests/Concord.Shim.Tests/Concord.Shim.Tests.csproj`
- Maybe-create: `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs` (empty class — Task 2 adds first test)

Skip this task entirely if Task 0 Step 4 found the test project already exists. Otherwise:

- [ ] **Step 1: Create the test project file**

Create `tests/Concord.Shim.Tests/Concord.Shim.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Concord.Shim\Concord.Shim.csproj" />
  </ItemGroup>

</Project>
```

> Versions above match `tests/Concord.Core.Tests/Concord.Core.Tests.csproj` — confirm the version pins haven't drifted by running `Select-String -Path tests\Concord.Core.Tests\Concord.Core.Tests.csproj -Pattern "Version"`.

- [ ] **Step 2: Add the test project to the solution**

Run:
```powershell
dotnet sln Terminal.sln add tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj
```

- [ ] **Step 3: Create empty test class file**

Create `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`:

```csharp
using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Concord.Shim.Tests;

public class ConcordHostLoadContextTests
{
    // Tests added in subsequent tasks.
}
```

- [ ] **Step 4: Verify the project builds and discovers (zero) tests**

Run:
```powershell
dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --no-restore --verbosity quiet
```

Expected: builds cleanly, "Total tests: 0".

- [ ] **Step 5: Commit**

```powershell
git add tests/Concord.Shim.Tests/
git commit -m "test: scaffold Concord.Shim.Tests project"
```

---

## Task 2: First failing test — `Load` finds assembly in host folder

**Files:**
- Modify: `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`

Add the first test. This test drives the introduction of an internal `TryResolveAssemblyPath` helper inside `ConcordHostLoadContext` — testing pure path-resolution logic without actually loading any DLL.

- [ ] **Step 1: Add the test**

Add this method to `ConcordHostLoadContextTests` (after the existing `// Tests added in subsequent tasks.` placeholder, which can be deleted):

```csharp
[Fact]
public void TryResolveAssemblyPath_ReturnsPath_WhenDllExistsInHostFolder()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "concord-shim-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try
    {
        var stubName = "FakeHost";
        var stubPath = Path.Combine(tempDir, stubName + ".dll");
        File.WriteAllBytes(stubPath, Array.Empty<byte>()); // path-existence is what's tested

        var ctx = new ConcordHostLoadContext(hostFolder: tempDir, runtimesFolder: tempDir);

        var resolved = ctx.TryResolveAssemblyPath(stubName, out var path);

        resolved.Should().BeTrue();
        path.Should().Be(stubPath);
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}
```

> The test calls `TryResolveAssemblyPath` directly — an internal helper that doesn't exist yet (compilation will fail in step 2). That's by design (TDD red).

- [ ] **Step 2: Run test, expect compile failure**

Run:
```powershell
dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --no-restore
```

Expected: build fails with `CS0117: 'ConcordHostLoadContext' does not contain a definition for 'TryResolveAssemblyPath'` (or similar — depending on the current shape of `ConcordHostLoadContext`, the error may be CS1061 instead).

No commit — failing tests aren't committed yet.

---

## Task 3: Implement `Load` + `TryResolveAssemblyPath` (drop `AssemblyDependencyResolver`)

**Files:**
- Modify: `src/Concord.Shim/ConcordHostLoadContext.cs`

This task makes the Task 2 test pass and is the structural heart of the fix. The exact edit depends on what Task 0 found; the pattern is:

1. Remove the field `private readonly AssemblyDependencyResolver _resolver;` and its initialization in the constructor.
2. Remove any helper method that calls `_resolver.ResolveAssemblyToPath(name)`.
3. Add the internal helper `TryResolveAssemblyPath`.
4. Add (or rewrite) the `Load(AssemblyName)` override to call the helper.

- [ ] **Step 1: Make `InternalsVisibleTo` declaration if not already present**

The test calls `internal` members (`TryResolveAssemblyPath`). Add to `src/Concord.Shim/Concord.Shim.csproj` (inside an `<ItemGroup>`):

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Concord.Shim.Tests" />
</ItemGroup>
```

Skip this step if the line is already present.

- [ ] **Step 2: Edit `ConcordHostLoadContext.cs`**

Locate the current `AssemblyDependencyResolver` usage (per Task 0 Step 2). Replace it. The class should look approximately like this (adapt field names + constructor signature to whatever Task 0 found):

```csharp
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Concord.Shim;

internal sealed class ConcordHostLoadContext : AssemblyLoadContext
{
    private readonly string _hostFolder;
    private readonly string _runtimesFolder;

    public ConcordHostLoadContext(string hostFolder, string runtimesFolder)
        : base(name: "ConcordHost", isCollectible: false)
    {
        _hostFolder = hostFolder;
        _runtimesFolder = runtimesFolder;

        // Existing Resolving handler stays unchanged — captured verbatim from Task 0 Step 2.
        Resolving += BounceSharedToDefaultContext;
    }

    internal bool TryResolveAssemblyPath(string assemblyName, out string? path)
    {
        var candidate = Path.Combine(_hostFolder, assemblyName + ".dll");
        if (File.Exists(candidate))
        {
            path = candidate;
            return true;
        }
        path = null;
        return false;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null) return null;
        return TryResolveAssemblyPath(assemblyName.Name, out var path)
            ? LoadFromAssemblyPath(path!)
            : null;
    }

    private Assembly? BounceSharedToDefaultContext(AssemblyLoadContext _, AssemblyName name)
    {
        // PRESERVED VERBATIM from the existing implementation captured in Task 0 Step 2.
        // Typical shape: bounce Mendix.StudioPro.ExtensionsAPI / System.* / Microsoft.Extensions.*
        // back to AssemblyLoadContext.Default by returning Default.LoadFromAssemblyName(name).
        // The existing logic is correct and platform-independent; do not modify it here.
        throw new NotImplementedException("Replace with existing handler body from Task 0 Step 2");
    }
}
```

> Critical: the existing `Resolving` handler body must be carried over verbatim. The placeholder `throw new NotImplementedException` is a deliberate plan marker — replace it with the captured body before saving the file.

- [ ] **Step 3: Run the test, expect pass**

Run:
```powershell
dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --no-restore --filter "TryResolveAssemblyPath_ReturnsPath_WhenDllExistsInHostFolder"
```

Expected: 1/1 passing.

- [ ] **Step 4: Run the full test suite to check for regressions in Concord.Core.Tests / Terminal.Tests**

Run:
```powershell
dotnet test --no-restore
```

Expected: all existing tests still pass (the changes are confined to `Concord.Shim`; no behavior change visible to other test projects).

- [ ] **Step 5: Commit**

```powershell
git add src/Concord.Shim/Concord.Shim.csproj src/Concord.Shim/ConcordHostLoadContext.cs tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs
git commit -m "fix(shim): replace AssemblyDependencyResolver with file-system probe

AssemblyDependencyResolver internally calls native
corehost_resolve_component_dependencies which requires hostpolicy was
initialized via corehost_main. Studio Pro on macOS uses an embedded
hosting path that doesn't satisfy this precondition, causing the shim
to fail MEF activation with InvalidArgFailure (-2147450750).

Replaces the resolver with a simple file-system probe of the host
folder, preserving the AssemblyLoadContext isolation and Resolving
event handler unchanged."
```

---

## Task 4: Test + verify — `Load` returns null for unknown assembly

**Files:**
- Modify: `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`

Defensive test: confirms `TryResolveAssemblyPath` returns false (and `Load` returns null) for an assembly name with no matching DLL, so the `Resolving` event still gets a chance to bounce to the default context.

- [ ] **Step 1: Add the test**

```csharp
[Fact]
public void TryResolveAssemblyPath_ReturnsFalse_WhenDllAbsent()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "concord-shim-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try
    {
        var ctx = new ConcordHostLoadContext(hostFolder: tempDir, runtimesFolder: tempDir);

        var resolved = ctx.TryResolveAssemblyPath("NotPresent", out var path);

        resolved.Should().BeFalse();
        path.Should().BeNull();
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}
```

- [ ] **Step 2: Run, expect pass (already-correct behavior; this is a regression-prevention test)**

Run:
```powershell
dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --no-restore --filter "TryResolveAssemblyPath_ReturnsFalse_WhenDllAbsent"
```

Expected: 1/1 passing.

- [ ] **Step 3: Commit**

```powershell
git add tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs
git commit -m "test(shim): regression-prevent Load returning non-null for absent DLL"
```

---

## Task 5: Failing test — `LoadUnmanagedDll` probes RID folders

**Files:**
- Modify: `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`

Adds a test for the unmanaged-DLL probe. Tests cross-platform: the test computes the expected file layout based on `RuntimeInformation.RuntimeIdentifier` so it works on both Windows and macOS without conditionals.

- [ ] **Step 1: Add the test**

```csharp
[Fact]
public void TryResolveNativePath_FindsFileInMatchingRidNativeFolder()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "concord-shim-test-" + Guid.NewGuid().ToString("N"));
    var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
    var nativeDir = Path.Combine(tempDir, "runtimes", rid, "native");
    Directory.CreateDirectory(nativeDir);
    try
    {
        // Use the platform-conventional native filename:
        // Mac: libe_sqlite3.dylib, Windows: e_sqlite3.dll, Linux: libe_sqlite3.so
        var (fileName, probeName) = PlatformNativeName("e_sqlite3");
        var stubPath = Path.Combine(nativeDir, fileName);
        File.WriteAllBytes(stubPath, Array.Empty<byte>());

        var ctx = new ConcordHostLoadContext(hostFolder: tempDir, runtimesFolder: Path.Combine(tempDir, "runtimes"));

        var resolved = ctx.TryResolveNativePath(probeName, out var path);

        resolved.Should().BeTrue();
        path.Should().Be(stubPath);
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}

private static (string fileName, string probeName) PlatformNativeName(string baseName)
{
    if (System.OperatingSystem.IsMacOS())   return ($"lib{baseName}.dylib", baseName);
    if (System.OperatingSystem.IsWindows()) return ($"{baseName}.dll", baseName);
    if (System.OperatingSystem.IsLinux())   return ($"lib{baseName}.so", baseName);
    throw new System.PlatformNotSupportedException();
}
```

- [ ] **Step 2: Run, expect compile failure**

Run:
```powershell
dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --no-restore
```

Expected: CS0117 / CS1061 — `TryResolveNativePath` doesn't exist on `ConcordHostLoadContext`.

No commit yet.

---

## Task 6: Implement `LoadUnmanagedDll` + `TryResolveNativePath` + `RidFallbackChain`

**Files:**
- Modify: `src/Concord.Shim/ConcordHostLoadContext.cs`

- [ ] **Step 1: Add the unmanaged probe + override to `ConcordHostLoadContext`**

Append these members to the class (inside the existing class body):

```csharp
internal bool TryResolveNativePath(string unmanagedDllName, out string? path)
{
    foreach (var probe in GetNativeProbePaths(unmanagedDllName))
    {
        if (File.Exists(probe))
        {
            path = probe;
            return true;
        }
    }
    path = null;
    return false;
}

protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
{
    return TryResolveNativePath(unmanagedDllName, out var path)
        ? LoadUnmanagedDllFromPath(path!)
        : IntPtr.Zero;
}

private IEnumerable<string> GetNativeProbePaths(string name)
{
    foreach (var rid in RidFallbackChain())
    {
        var native = Path.Combine(_runtimesFolder, rid, "native");
        if (!Directory.Exists(native)) continue;
        // Try the bare name (caller passed full filename) and platform-conventional variants.
        yield return Path.Combine(native, name);
        if (System.OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(native, "lib" + name + ".dylib");
            yield return Path.Combine(native, name + ".dylib");
        }
        else if (System.OperatingSystem.IsWindows())
        {
            yield return Path.Combine(native, name + ".dll");
        }
        else if (System.OperatingSystem.IsLinux())
        {
            yield return Path.Combine(native, "lib" + name + ".so");
            yield return Path.Combine(native, name + ".so");
        }
    }
    // Last-ditch: top of host folder.
    yield return Path.Combine(_hostFolder, name);
}

private static IEnumerable<string> RidFallbackChain()
{
    var current = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
    yield return current;  // e.g., "osx-arm64"

    if (System.OperatingSystem.IsMacOS())
    {
        yield return "osx";       // generic mac
        yield return "unix";
    }
    else if (System.OperatingSystem.IsLinux())
    {
        yield return "linux";
        yield return "unix";
    }
    else if (System.OperatingSystem.IsWindows())
    {
        yield return "win";
    }

    yield return "any";
}
```

Required `using` statements (add to top of file if not already present):

```csharp
using System.Collections.Generic;
using System.Runtime.InteropServices;
```

- [ ] **Step 2: Run the test, expect pass**

Run:
```powershell
dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --no-restore --filter "TryResolveNativePath_FindsFileInMatchingRidNativeFolder"
```

Expected: 1/1 passing on the current platform.

- [ ] **Step 3: Run full suite**

Run:
```powershell
dotnet test --no-restore
```

Expected: all tests pass (no regressions; new probe code is only invoked via the new test paths and via the new `LoadUnmanagedDll` override which is only triggered during actual shim activation in Studio Pro).

- [ ] **Step 4: Commit**

```powershell
git add src/Concord.Shim/ConcordHostLoadContext.cs tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs
git commit -m "fix(shim): override LoadUnmanagedDll to probe runtimes/{rid}/native/

SQLite (libe_sqlite3.dylib) and any other native dependencies live in
runtimes/<rid>/native/ in the deployed snapshot. Without an explicit
probe, .NET's default unmanaged-DLL search falls back to the same
hostpolicy machinery that fails on Mac. Direct file-system probe with
RID fallback (osx-arm64 -> osx -> unix -> any) covers all targeted
platforms."
```

---

## Task 7: RID fallback test

**Files:**
- Modify: `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`

Confirms that if the most-specific RID folder is missing but a generic RID folder has the file, the probe still resolves. Real-world case: a NuGet package that only ships `runtimes/osx/native/` (no `osx-arm64/`) still works on Apple Silicon.

- [ ] **Step 1: Add the test**

```csharp
[Fact]
public void TryResolveNativePath_FallsBackThroughRidGraph()
{
    if (!System.OperatingSystem.IsMacOS())
    {
        // Test exercises Mac-specific RID fallback (osx-arm64/osx-x64 -> osx -> unix).
        // On non-Mac platforms the fallback chain is different; skip to keep the
        // test focused.
        return;
    }

    var tempDir = Path.Combine(Path.GetTempPath(), "concord-shim-test-" + Guid.NewGuid().ToString("N"));
    var genericNativeDir = Path.Combine(tempDir, "runtimes", "osx", "native");
    Directory.CreateDirectory(genericNativeDir);
    try
    {
        // File lives ONLY under runtimes/osx/native/, not under runtimes/osx-arm64/native/
        var stubPath = Path.Combine(genericNativeDir, "libe_sqlite3.dylib");
        File.WriteAllBytes(stubPath, Array.Empty<byte>());

        var ctx = new ConcordHostLoadContext(hostFolder: tempDir, runtimesFolder: Path.Combine(tempDir, "runtimes"));

        var resolved = ctx.TryResolveNativePath("e_sqlite3", out var path);

        resolved.Should().BeTrue();
        path.Should().Be(stubPath);
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}
```

- [ ] **Step 2: Run on Mac (or skip on Windows)**

Run:
```powershell
dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --no-restore --filter "TryResolveNativePath_FallsBackThroughRidGraph"
```

Expected on Mac: 1/1 passing. Expected on Windows: 1/1 passing (test early-returns).

- [ ] **Step 3: Commit**

```powershell
git add tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs
git commit -m "test(shim): RID fallback resolves osx-arm64 via osx generic native folder"
```

---

## Task 8: Sanity test — no `AssemblyDependencyResolver` field remains

**Files:**
- Modify: `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`

Regression-prevention test. Uses reflection to assert that `ConcordHostLoadContext` has no field of type `AssemblyDependencyResolver`. Future refactor that re-introduces the resolver (e.g., copy-paste from a sample) will fail this test.

- [ ] **Step 1: Add the test**

```csharp
[Fact]
public void ConcordHostLoadContext_DoesNotUseAssemblyDependencyResolver()
{
    var fields = typeof(ConcordHostLoadContext)
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    fields.Should().NotContain(
        f => f.FieldType.FullName == "System.Runtime.Loader.AssemblyDependencyResolver",
        because: "AssemblyDependencyResolver calls native corehost_resolve_component_dependencies " +
                 "which fails on Studio Pro for macOS (hostpolicy not initialized via corehost_main).");
}
```

- [ ] **Step 2: Run, expect pass**

Run:
```powershell
dotnet test tests\Concord.Shim.Tests\Concord.Shim.Tests.csproj --no-restore --filter "ConcordHostLoadContext_DoesNotUseAssemblyDependencyResolver"
```

Expected: 1/1 passing (the field was removed in Task 3).

- [ ] **Step 3: Commit**

```powershell
git add tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs
git commit -m "test(shim): regression-prevent re-introduction of AssemblyDependencyResolver"
```

---

## Task 9: Audit `RuntimeHostLocator` for `BaseDirectory` usage

**Files:**
- Read: `src/Concord.Shim/RuntimeHostLocator.cs`
- Modify (only if broken): `src/Concord.Shim/RuntimeHostLocator.cs`

Phase 0 spike Probe Bug B: `AppDomain.CurrentDomain.BaseDirectory` returns Studio Pro's install dir, not the extension's deployed-snapshot folder. If `RuntimeHostLocator` uses this, the shim is silently broken on Mac in addition to the resolver issue (and would have been broken on Windows had the spike not caught it earlier in the cycle).

- [ ] **Step 1: Read the current implementation**

Open `src/Concord.Shim/RuntimeHostLocator.cs` (per Task 0 Step 3 grep results).

- [ ] **Step 2: Determine which path-resolution mechanism it uses**

- If it uses `Path.GetDirectoryName(typeof(...).Assembly.Location)` → correct; skip to Step 4.
- If it uses `AppDomain.CurrentDomain.BaseDirectory` → broken on Mac; proceed to Step 3.

- [ ] **Step 3: Fix the broken path resolution**

Replace `AppDomain.CurrentDomain.BaseDirectory` with `Path.GetDirectoryName(typeof(TYPENAME).Assembly.Location)!` where `TYPENAME` is a type known to live in `Concord.Shim.dll` (e.g., `RuntimeHostLocator` itself, or one of the shim's `[Export]` classes — pick whichever feels natural per the existing code structure).

- [ ] **Step 4: Run the full test suite (regression check)**

Run:
```powershell
dotnet test --no-restore
```

Expected: all tests pass.

- [ ] **Step 5: Commit (only if Step 3 made changes)**

```powershell
git add src/Concord.Shim/RuntimeHostLocator.cs
git commit -m "fix(shim): resolve extension folder via Assembly.Location

AppDomain.CurrentDomain.BaseDirectory returns Studio Pro's install dir
under the per-project cache-snapshot deployment model, not the
extension's actual deployed folder. Use the shim assembly's own
Location instead, matching Phase 0 spike findings (Probe Bug B)."
```

If Step 2 confirmed `Assembly.Location` was already in use, skip the commit. Document the audit result in the eventual PR description.

---

## Task 10: Verify `runtimes/` tree in build output

**Files:**
- Read: `src/Concord.Shim/Concord.Shim.csproj`
- Read: `src/Concord.Shim/bin/Debug/net8.0/` (build output)
- Maybe-modify: `src/Concord.Shim/Concord.Shim.csproj`

The deployed snapshot has `runtimes/` at the extension-folder top level (shared between `bin-10x/` and `bin-11x/` hosts). The build must produce this structure too.

- [ ] **Step 1: Clean + rebuild the shim**

Run:
```powershell
dotnet build src\Concord.Shim\Concord.Shim.csproj -c Debug
```

Expected: build succeeds.

- [ ] **Step 2: Inspect the produced `runtimes/` tree**

Run:
```powershell
Get-ChildItem -Recurse -Path src\Concord.Shim\bin\Debug\net8.0\runtimes -File | Select-Object FullName
```

Expected (minimum): files under `runtimes/osx-arm64/native/`, `runtimes/osx-x64/native/`, `runtimes/win-x64/native/` for SQLite (`libe_sqlite3.dylib`, `e_sqlite3.dll`).

- [ ] **Step 3: If any RID is missing — add the dependency**

The `runtimes/` content comes from `SQLitePCLRaw.lib.e_sqlite3` (transitively, via `Microsoft.Data.Sqlite`). If a RID is missing, check whether the project references `Microsoft.Data.Sqlite` directly. If it inherits the reference from `Concord.Core` via `<ProjectReference>`, the runtimes typically flow through. If they don't, add explicitly to `Concord.Shim.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="SQLitePCLRaw.lib.e_sqlite3" Version="2.1.6" />
</ItemGroup>
```

(Version pin matches the existing reference in `Concord.Shim.deps.json` per the deployed snapshot's evidence — `SQLitePCLRaw.lib.e_sqlite3/2.1.6`.)

- [ ] **Step 4: Rebuild and re-verify if Step 3 made changes**

Run:
```powershell
dotnet build src\Concord.Shim\Concord.Shim.csproj -c Debug
Get-ChildItem -Recurse -Path src\Concord.Shim\bin\Debug\net8.0\runtimes -File | Select-Object FullName
```

- [ ] **Step 5: Commit (only if Step 3 made changes)**

```powershell
git add src/Concord.Shim/Concord.Shim.csproj
git commit -m "build(shim): ensure SQLite native libs ship for all target RIDs"
```

---

## Task 11: Windows smoke test — 11.10

**Files:** None (manual smoke).

Verifies no regression on the platform that already works.

- [ ] **Step 1: Build the merged extension layout (per parent spec §2 "MergeHostsForShim" target, if implemented; otherwise per the existing Phase 1 build path)**

Run the project's standard deploy command — the deployed structure should be:

```
<test-project>/extensions/Concord/
  manifest.json   ({ "mx_extensions": ["Concord.Shim.dll"] })
  Concord.Shim.dll
  Concord.Core.dll
  runtimes/
  bin-10x/...
  bin-11x/...
  (skills/, rules/, wwwroot/, etc.)
```

- [ ] **Step 2: Wipe the test project's `.mendix-cache`**

```powershell
Remove-Item -Recurse -Force C:\Workspace\MendixApps\TestOSApp3\.mendix-cache
```

- [ ] **Step 3: Open the project in Studio Pro 11.10**

Expected: no composition exception. Extensions → Concord → Open Pane works. Terminal echoes input.

- [ ] **Step 4: Trigger a `save_all` round-trip**

In the terminal pane, invoke whichever CLI is wired (Claude Code / Codex / Copilot) and run a tool call that hits `save_all`. Expected: success.

- [ ] **Step 5: Document the result in the PR description (no commit — manual evidence)**

---

## Task 12: Windows smoke test — 10.24.13

**Files:** None (manual smoke).

- [ ] **Step 1–4: Repeat Task 11 steps against Studio Pro 10.24.13**

Expected: same outcomes via `bin-10x/` host. Pane opens, terminal works, `save_all` succeeds.

- [ ] **Step 5: Document result in PR description**

---

## Task 13: Mac smoke test — 11.10 (the actual fix validation)

**Files:** None (manual smoke).

This is the test that proves the fix works.

- [ ] **Step 1: Build the `.mxmodule` on Windows (Studio Pro UI export step from ConcordPublisher) and copy to Mac**

The `.mxmodule` cannot be built outside of Studio Pro's UI per CLAUDE.md. Build on Windows, transfer via shared storage / git / scp.

- [ ] **Step 2: Wipe the Mac test project's `.mendix-cache`**

```bash
rm -rf ~/Mendix/GraphViewer/.mendix-cache
```

(Or whichever test project is being used.)

- [ ] **Step 3: Install the `.mxmodule` via Studio Pro Marketplace local-file route on Mac**

Studio Pro → Marketplace → local file → select the `.mxmodule`.

- [ ] **Step 4: Open the test project and check Extensions menu**

Expected: Concord listed; no composition exception in Studio Pro's error log. Click Extensions → Concord → Open Pane.

- [ ] **Step 5: Verify pane opens, terminal echoes, `save_all` round-trips**

Expected: full functionality matching Windows behavior.

- [ ] **Step 6: Capture screenshot of Studio Pro's extension UI showing the version**

This screenshot validates whether the version-display fix (Task 15) is needed — the expected display is `5.0.3`. If it shows anything else (currently expected: `4.2.2`), proceed to Task 15.

---

## Task 14: Mac smoke test — 10.24.13

**Files:** None (manual smoke).

Less critical (Mac + SP 10.x is a smaller intersection of user base) but worth confirming for completeness.

- [ ] **Step 1–5: Repeat Task 13 steps against Studio Pro 10.24.13 on Mac**

Expected: pane opens via `bin-10x/` host. If Studio Pro 10.24.13 isn't readily available on Mac for testing, document this as a known gap and ship the fix with Mac SP 11.10 validation only.

---

## Task 15: ConcordPublisher wrapper module — bump version to `5.0.3` and re-export `.mxmodule`

**Files:** Studio Pro UI step on Windows (no Git diff in this repo).

Per CLAUDE.md "Things that bit us before":

> Version bump alone is NOT enough — Studio Pro re-bakes the version into the .mxmodule at export time. Must redo the UI export step.

The current `.mxmodule` carries `4.2.2` because the wrapper module's version field was not bumped before the most recent export. Fix the metadata, then re-export.

- [ ] **Step 1: Open `C:\Workspace\MendixApps\ConcordPublisher\ConcordPublisher.mpr` in Studio Pro**

- [ ] **Step 2: Locate the Module containing the bundled Concord resources**

Per CLAUDE.md, this is the wrapper module in the ConcordPublisher project that holds the binary resources for `.mxmodule` export.

- [ ] **Step 3: Update the module's version property to `5.0.3`**

Exact field location depends on the Studio Pro version's module-properties dialog — find the "Version" or "Module version" field.

- [ ] **Step 4: Save the project**

- [ ] **Step 5: Re-export the `.mxmodule`**

Studio Pro UI step — Right-click module → Export module package. Output path same as previous releases (per `reference_concord_mxmodule_build.md` per CLAUDE.md).

- [ ] **Step 6: Verify the new `.mxmodule` reports `5.0.3`**

Install on a fresh test project (Mac or Windows) and check Studio Pro's Extensions UI. Expected: `Concord 5.0.3`.

No Git commit for this task — the change lives in ConcordPublisher, not this repo.

---

## Task 16: Pull request

**Files:** Branch ready to push.

- [ ] **Step 1: Push the branch**

```powershell
git push -u origin fix/concord-shim-mac-loadcontext
```

- [ ] **Step 2: Create PR via `gh`**

```powershell
gh pr create --title "fix(shim): make Concord v5.0.3 work on macOS" --body @"
## Summary
- Replaces \`AssemblyDependencyResolver\` in \`ConcordHostLoadContext\` with manual file-system probing (managed DLLs via \`Load\` override + \`TryResolveAssemblyPath\` helper).
- Adds \`LoadUnmanagedDll\` override that probes \`runtimes/{rid}/native/\` with RID fallback (\`osx-arm64\` → \`osx\` → \`unix\` → \`any\`).
- Verifies \`RuntimeHostLocator\` uses \`Assembly.Location\` (not \`AppDomain.BaseDirectory\`).
- Wrapper module version bumped to \`5.0.3\` and \`.mxmodule\` re-exported.

## Root cause
\`AssemblyDependencyResolver\` internally calls native \`corehost_resolve_component_dependencies\`, which requires \`hostpolicy\` was initialized via \`corehost_main\`. Studio Pro on macOS uses an embedded hosting path that doesn't satisfy this precondition (\`fxr_path\` empty → \`InvalidArgFailure\` -2147450750). Windows works because Studio Pro's launcher is \`apphost\`-style.

## Test plan
- [x] Windows / Studio Pro 11.10 — pane opens, \`save_all\` round-trips (regression check)
- [x] Windows / Studio Pro 10.24.13 — pane opens via \`bin-10x/\` host (regression check)
- [x] macOS / Studio Pro 11.10 — pane opens, \`save_all\` round-trips (the actual fix)
- [ ] macOS / Studio Pro 10.24.13 — if available; otherwise documented gap
- [x] \`dotnet test\` — full unit-test suite passes (new Concord.Shim.Tests + existing Concord.Core.Tests + Terminal.Tests)

Spec: docs/superpowers/specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md
Plan: docs/superpowers/plans/2026-05-15-concord-shim-mac-loadcontext-fix.md

🤖 Generated with [Claude Code](https://claude.com/claude-code)
"@
```

- [ ] **Step 3: After PR merges, tag `v5.0.3`**

Per the release-playbook reference in CLAUDE.md:

```powershell
git checkout main
git pull
git tag -a v5.0.3 -m "v5.0.3: Mac shim fix"
git push origin v5.0.3
gh release create v5.0.3 --notes-file release-notes.md
```

---

## Self-Review

**Spec coverage:**

| Spec section | Implementing task |
|---|---|
| §Goal — 4-cell matrix passes | Tasks 11–14 |
| §Approach §1 — Replace AssemblyDependencyResolver | Tasks 2–6 |
| §Approach §2 — `Assembly.Location` audit | Task 9 |
| §Approach §3 — `runtimes/` packaging verification | Task 10 |
| §Approach §4 — Wrapper version bump + re-export | Task 15 |
| §Tests Unit — 5 unit tests | Tasks 2, 4, 5, 7, 8 (5 tests total) |
| §Tests Integration — smoke matrix | Tasks 11–14 |
| §Tests Negative — missing bin-{Nx}/, missing native | Documented but not exercised; deferred (low value vs. test cost) |
| §Open question 1 — log location on Mac | Not addressed in plan; spec defers it and existing shim logging code (Task 0 Step 2) inherits whatever convention is already there |
| §Open question 2 — runtimes/ folder location | Resolved as Task 10 + spec recommendation: keep at top of `extensions/Concord/` |
| §Open question 3 — Mac CI job | Not addressed in this plan; flagged as a follow-up in the PR description |
| §Open question 4 — Intel Mac RID | Covered by Task 10 (verifies `osx-x64/native/` ships) |

**Placeholder scan:** The plan deliberately marks one placeholder — the `BounceSharedToDefaultContext` body in Task 3 Step 2 — as `throw new NotImplementedException("Replace with existing handler body from Task 0 Step 2")`. This is captured as an explicit plan instruction, not an unresolved TBD: Task 0 Step 2 records the existing handler verbatim and Task 3 Step 2's note tells the engineer to carry it over. Acceptable because the placeholder is fully resolvable from data the engineer captured in Task 0.

**Type consistency:** `ConcordHostLoadContext`, `TryResolveAssemblyPath`, `TryResolveNativePath`, `GetNativeProbePaths`, `RidFallbackChain` — names consistent across Tasks 2–8. Field names `_hostFolder`, `_runtimesFolder` — consistent. Tasks 3 and 6 both reference the same class member layout.

**Scope:** 16 tasks, ~5 atomic commits (Tasks 1, 3, 4, 6, 7, 8, plus 9 + 10 if changes needed). Tight enough for one PR. Manual smoke matrix in Tasks 11–14 doesn't commit but produces PR-description evidence.
