# Concord Shim — Mac Load-Context Fix Implementation Plan (REVISED)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Revision history:** Initial draft was written without sight of `src/Concord.Shim/` (source lived on a Windows-local branch). After PR #21 merged `feat/v5.1.0-runtime-shim` to main, the source became visible and a Task-0 audit revealed the plan's pseudocode was much further from reality than expected. This revision matches the actual code shape. Reasons for each delta from the original plan are recorded inline.

**Goal:** Make Concord install and run successfully on Studio Pro 11.10 on macOS from the same merged `.mxmodule` layout that already works on Windows. The current Phase 1 shim (assembly version `5.1.0-alpha.1`) fails MEF activation on Mac with `Hostpolicy must be initialized…` because `ConcordHostLoadContext` eagerly constructs an `AssemblyDependencyResolver` whose constructor calls native `corehost_resolve_component_dependencies` — a function with a precondition Studio Pro's Mac launcher doesn't satisfy.

**Architecture:** Remove the `AssemblyDependencyResolver` field from `ConcordHostLoadContext` (and its priority-4 fallback in the resolution chain — priorities 1–3 and 5 are unchanged). Add a defensive `LoadUnmanagedDll` override that probes `runtimes/<rid>/native/` for native binaries (e.g., `libe_sqlite3.dylib`) so SQLite still works after the resolver removal. Existing 7-test suite under `tests/Concord.Shim.Tests/` remains as-is; one regression test and one `LoadUnmanagedDll` unit test added.

**Tech Stack:** .NET 8, C# 12, xUnit + FluentAssertions + Microsoft.CodeAnalysis.CSharp (Roslyn-compiled fake hosts via `FakeHostBuilder`), Studio Pro 10.24.13 + 11.10 on macOS arm64 + Windows for smoke validation.

**Reference spec:** [`docs/superpowers/specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md`](../specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md). The spec's §Approach §1–§3 are still correct in intent; §4 (wrapper version bump) is deferred since the current version baseline is `5.1.0-alpha.1`, not `5.0.3` as the spec assumed — the release-step decision is captured as an open question for Joe to resolve after the technical fix lands.

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `src/Concord.Shim/ConcordHostLoadContext.cs` | Modify | Remove `_resolver` field + its constructor block (lines 39, 45-56) + the priority-4 fallback in `Resolve()` (lines 134-141). Add `LoadUnmanagedDll(string)` override + `TryResolveNativePath` internal helper + `RidFallbackChain` private helper. |
| `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs` | Modify | Add two tests: (1) reflection-based regression preventing future re-introduction of `AssemblyDependencyResolver`; (2) `TryResolveNativePath` unit test using existing `FakeHostBuilder` patterns. |

Nothing else changes. Specifically NOT touched:
- `RuntimeHostLocator.cs` — already uses `Assembly.Location` correctly (verified during audit).
- `HostKickstart.cs` — orchestrates the load context but doesn't touch the resolver directly.
- The 7 existing tests — all pass on Mac today because they never plant a real `Concord.Host{N}x.dll` in `_tempDir`, so the `_resolver` constructor's `File.Exists` guard returns false and the failing native call is never made. Removing the resolver is a no-op for them.
- `Concord.Shim.csproj` — no dependency changes needed; SQLite native runtimes already ship via transitive references.

---

## Task 1: Remove `AssemblyDependencyResolver` from `ConcordHostLoadContext`

**Files:**
- Modify: `src/Concord.Shim/ConcordHostLoadContext.cs`

The core fix. Three edits, all deletions.

- [ ] **Step 1: Remove the `_resolver` field**

Edit `src/Concord.Shim/ConcordHostLoadContext.cs` — remove line 39:

```csharp
    private readonly AssemblyDependencyResolver? _resolver;
```

- [ ] **Step 2: Remove the constructor block that builds `_resolver`**

Edit the constructor (lines 41-59) — replace this:

```csharp
    public ConcordHostLoadContext(string hostFolder)
        : base(name: $"ConcordHost@{hostFolder}", isCollectible: false)
    {
        _hostFolder = hostFolder;
        // AssemblyDependencyResolver reads the .deps.json of a primary
        // assembly to resolve its dependency graph. We construct it ONLY
        // when the host DLL is already on disk (the File.Exists guard
        // below) — the resolver constructor immediately reads the adjacent
        // .deps.json and would throw otherwise. Phase 3's HostKickstart
        // must therefore ensure the bin folder is populated before
        // constructing this context.
        var likelyHostDll = Path.Combine(hostFolder, "Concord.Host10x.dll");
        if (!File.Exists(likelyHostDll))
            likelyHostDll = Path.Combine(hostFolder, "Concord.Host11x.dll");
        if (File.Exists(likelyHostDll))
            _resolver = new AssemblyDependencyResolver(likelyHostDll);

        Resolving += OnResolving;
    }
```

with this:

```csharp
    public ConcordHostLoadContext(string hostFolder)
        : base(name: $"ConcordHost@{hostFolder}", isCollectible: false)
    {
        _hostFolder = hostFolder;
        Resolving += OnResolving;
    }
```

- [ ] **Step 3: Remove the priority-4 fallback in `Resolve()`**

Edit `Resolve()` (lines 134-141) — remove this entire block:

```csharp
        // 4. AssemblyDependencyResolver — handles runtimes/<rid>/ subfolders
        //    for native interop, satellite resources, etc.
        var resolved = _resolver?.ResolveAssemblyToPath(name);
        if (resolved is not null && File.Exists(resolved))
        {
            ShimLog.Info($"Resolved {simpleName} from {resolved} via dependency resolver into {Name}");
            return LoadFromAssemblyPath(resolved);
        }
```

And renumber priority-5's comment to priority-4 — replace:

```csharp
        // 5. Fall through to default context. BCL types like System.Runtime
        //    that the runtime provides without a host-folder copy resolve
        //    here through the default ALC's own probing.
        return null;
```

with:

```csharp
        // 4. Fall through to default context. BCL types like System.Runtime
        //    that the runtime provides without a host-folder copy resolve
        //    here through the default ALC's own probing.
        return null;
```

- [ ] **Step 4: Update the class-level XML doc**

The class-level doc (lines 7-35) lists "resolution order" as 3 items but the real code had 4 priorities + fallback (with #4 being the resolver). The doc's 3-item structure doesn't actually need changing — the resolver was an implementation detail that wasn't called out in the doc anyway. No edit needed.

If you want to add a sentence to the class doc justifying the absence, add this paragraph at the end (before `</summary>`):

```csharp
/// Resolution uses ONLY file-system probing and default-ALC dynamic lookup —
/// not <see cref="AssemblyDependencyResolver"/>. The resolver's constructor
/// invokes native <c>corehost_resolve_component_dependencies</c>, which
/// requires hostpolicy to have been initialized via <c>corehost_main</c>.
/// Studio Pro on macOS uses an embedded .NET hosting path that doesn't
/// satisfy that precondition (<c>InvalidArgFailure</c> -2147450750). The
/// flat host folder layout covers all Concord deps; no resolver needed.
```

- [ ] **Step 5: Remove unused `using` if present**

If the `using System.Runtime.Loader;` directive at the top of the file is now only used for `AssemblyLoadContext` (which is the base class), keep it. If it had any usage exclusively for `AssemblyDependencyResolver` that's now gone, no removal needed because `AssemblyLoadContext` lives in the same namespace.

- [ ] **Step 6: Build and verify the change compiles**

Run:
```bash
dotnet build src/Concord.Shim/Concord.Shim.csproj 2>&1 | tail -10
```

Expected: 0 errors, 0 warnings (or only the pre-existing warnings).

- [ ] **Step 7: Run the existing test suite to verify no regression**

Run:
```bash
dotnet test tests/Concord.Shim.Tests/ --no-restore 2>&1 | tail -15
```

Expected: 7+ tests pass (the existing test count; may be slightly higher if other test files in the project add to it).

> Note: Existing tests don't reproduce the Mac failure because they emit fake host DLLs named "FakeHost" — not "Concord.Host10x" or "Concord.Host11x" — so the old `File.Exists` guard skipped resolver construction. After this task, the guard is gone entirely, so the same tests exercise the same code paths but via a different (simpler) flow.

- [ ] **Step 8: Commit**

```bash
git add src/Concord.Shim/ConcordHostLoadContext.cs
git commit -m "fix(shim): remove AssemblyDependencyResolver from ConcordHostLoadContext

The resolver's constructor invokes native corehost_resolve_component_
dependencies, which has a precondition (fxr_path set during corehost_main)
that Studio Pro's macOS launcher doesn't satisfy. Result on Mac:
InvalidArgFailure -2147450750 thrown during MEF activation of
TerminalWebServerShim (whichever shim export MEF activates first),
cascading to a CompositionException that prevents the extension from
loading entirely.

The resolver was used as priority-4 fallback in Resolve() to handle
runtimes/<rid>/lib/<tfm>/ RID-specific managed DLLs. None of Concord's
actual managed dependencies use that layout (they're all flat in the
host folder, hit by priority-3). Native binaries continue to need
handling — addressed in the follow-up commit adding LoadUnmanagedDll.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Regression test — no `AssemblyDependencyResolver` field

**Files:**
- Modify: `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`

Sanity test preventing future re-introduction of the resolver. Reflection-based; cross-platform; fast.

- [ ] **Step 1: Add the test method to the end of the class**

Append to `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs` (before the closing `}` of `ConcordHostLoadContextTests`):

```csharp
    // Regression: AssemblyDependencyResolver was removed in
    // fix(shim) commit "remove AssemblyDependencyResolver from
    // ConcordHostLoadContext". Its constructor invokes native
    // corehost_resolve_component_dependencies which fails on macOS
    // Studio Pro (hostpolicy not initialized via corehost_main).
    // Don't re-introduce — if a future change needs deps.json-driven
    // resolution, find a different mechanism that works on both platforms.
    [Fact]
    public void ConcordHostLoadContext_HasNoAssemblyDependencyResolverField()
    {
        var resolverType = typeof(System.Runtime.Loader.AssemblyDependencyResolver);
        var fields = typeof(ConcordHostLoadContext)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        fields.Should().NotContain(
            f => f.FieldType == resolverType || f.FieldType == typeof(System.Runtime.Loader.AssemblyDependencyResolver?),
            because: "AssemblyDependencyResolver's constructor calls native " +
                     "corehost_resolve_component_dependencies which fails on macOS " +
                     "Studio Pro — see fix commit message for full root cause.");
    }
```

- [ ] **Step 2: Build + run the new test**

Run:
```bash
dotnet test tests/Concord.Shim.Tests/ --no-restore --filter "FullyQualifiedName~ConcordHostLoadContext_HasNoAssemblyDependencyResolverField" 2>&1 | tail -10
```

Expected: 1/1 passing.

- [ ] **Step 3: Commit**

```bash
git add tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs
git commit -m "test(shim): regression-prevent re-introduction of AssemblyDependencyResolver

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Add `LoadUnmanagedDll` override + `TryResolveNativePath` helper

**Files:**
- Modify: `src/Concord.Shim/ConcordHostLoadContext.cs`

Defensive: SQLite's `libe_sqlite3.dylib` (and the Linux/Windows equivalents) live under `runtimes/<rid>/native/` in the deployed snapshot. Without an explicit probe, .NET's default native-search may also fall back to the same hostpolicy-initialised state that just broke on Mac. This override makes native loading deterministic regardless of platform.

- [ ] **Step 1: Add the `LoadUnmanagedDll` override and helpers**

Edit `src/Concord.Shim/ConcordHostLoadContext.cs`. After the `Dispose()` method (around line 172), add these members inside the class:

```csharp
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        ShimLog.Info($"LoadUnmanagedDll fired for {unmanagedDllName}");
        if (TryResolveNativePath(unmanagedDllName, out var path))
        {
            ShimLog.Info($"Resolved native {unmanagedDllName} from {path} into {Name}");
            return LoadUnmanagedDllFromPath(path!);
        }
        // Fall through to default native search (probes the executable's
        // directory, OS-default search paths, etc.). Returning IntPtr.Zero
        // signals "I didn't find it; please try the default mechanism".
        return IntPtr.Zero;
    }

    internal bool TryResolveNativePath(string unmanagedDllName, out string? path)
    {
        var runtimesDir = Path.Combine(_hostFolder, "..", "runtimes");
        if (!Directory.Exists(runtimesDir))
        {
            // Some deployments may also ship runtimes/ alongside the host
            // folder rather than at the extension root; try the host folder
            // as a fallback.
            runtimesDir = Path.Combine(_hostFolder, "runtimes");
        }
        if (Directory.Exists(runtimesDir))
        {
            foreach (var probe in NativeProbePaths(runtimesDir, unmanagedDllName))
            {
                if (File.Exists(probe))
                {
                    path = probe;
                    return true;
                }
            }
        }
        // Last-ditch: flat in host folder (some packages drop natives there).
        var flat = Path.Combine(_hostFolder, unmanagedDllName);
        if (File.Exists(flat))
        {
            path = flat;
            return true;
        }
        path = null;
        return false;
    }

    private static IEnumerable<string> NativeProbePaths(string runtimesDir, string name)
    {
        foreach (var rid in RidFallbackChain())
        {
            var native = Path.Combine(runtimesDir, rid, "native");
            if (!Directory.Exists(native)) continue;
            // Try the bare name first (some callers pass full filename).
            yield return Path.Combine(native, name);
            // Platform-conventional variants.
            if (OperatingSystem.IsMacOS())
            {
                yield return Path.Combine(native, "lib" + name + ".dylib");
                yield return Path.Combine(native, name + ".dylib");
            }
            else if (OperatingSystem.IsWindows())
            {
                yield return Path.Combine(native, name + ".dll");
            }
            else if (OperatingSystem.IsLinux())
            {
                yield return Path.Combine(native, "lib" + name + ".so");
                yield return Path.Combine(native, name + ".so");
            }
        }
    }

    private static IEnumerable<string> RidFallbackChain()
    {
        // RuntimeInformation.RuntimeIdentifier returns the most specific RID
        // (e.g., osx-arm64 on Apple Silicon, win-x64 on x64 Windows).
        var current = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        yield return current;

        if (OperatingSystem.IsMacOS())
        {
            yield return "osx";
            yield return "unix";
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return "linux";
            yield return "unix";
        }
        else if (OperatingSystem.IsWindows())
        {
            yield return "win";
        }

        yield return "any";
    }
```

Add the necessary `using` at the top if not already present:

```csharp
using System.Collections.Generic;
```

(`System.IO` and `System.Runtime.Loader` are already there; `System.Runtime.InteropServices` is qualified inline to avoid touching the using block unnecessarily.)

- [ ] **Step 2: Build and verify**

Run:
```bash
dotnet build src/Concord.Shim/Concord.Shim.csproj 2>&1 | tail -10
```

Expected: 0 errors.

- [ ] **Step 3: Run existing tests to check no regression**

Run:
```bash
dotnet test tests/Concord.Shim.Tests/ --no-restore 2>&1 | tail -15
```

Expected: 8/8 passing (the original 7 + the regression test from Task 2).

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Shim/ConcordHostLoadContext.cs
git commit -m "fix(shim): override LoadUnmanagedDll with runtimes/<rid>/native/ probe

Defensive against the same hostpolicy class of failure that took out
AssemblyDependencyResolver on Mac. SQLite (libe_sqlite3.dylib on Mac,
e_sqlite3.dll on Windows) and any other native interop bits live under
runtimes/<rid>/native/ in the deployed snapshot — explicit probe with
RID fallback (osx-arm64 -> osx -> unix -> any, mirror on other
platforms) makes resolution deterministic regardless of how .NET was
hosted.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Unit test — `TryResolveNativePath` probes RID-specific native folder

**Files:**
- Modify: `tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs`

Tests the new probe logic directly via the internal `TryResolveNativePath` method. Cross-platform: derives expected file layout from `RuntimeInformation.RuntimeIdentifier` at runtime.

- [ ] **Step 1: Add the test**

Append to `ConcordHostLoadContextTests` (before the closing brace):

```csharp
    [Fact]
    public void TryResolveNativePath_FindsNativeFileInMatchingRidFolder()
    {
        // Layout: <_tempDir>/host/ (the load context's hostFolder)
        //         <_tempDir>/runtimes/<rid>/native/<lib-or-dll>
        // Matches the production deployed snapshot: runtimes/ is one level
        // up from the host folder.
        var hostFolder = Path.Combine(_tempDir, "host");
        Directory.CreateDirectory(hostFolder);
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var nativeDir = Path.Combine(_tempDir, "runtimes", rid, "native");
        Directory.CreateDirectory(nativeDir);

        // Platform-conventional native filename.
        string fileName, probeName;
        if (OperatingSystem.IsMacOS())   { fileName = "libe_sqlite3.dylib"; probeName = "e_sqlite3"; }
        else if (OperatingSystem.IsWindows()) { fileName = "e_sqlite3.dll"; probeName = "e_sqlite3"; }
        else if (OperatingSystem.IsLinux())   { fileName = "libe_sqlite3.so"; probeName = "e_sqlite3"; }
        else throw new PlatformNotSupportedException();

        var stubPath = Path.Combine(nativeDir, fileName);
        File.WriteAllBytes(stubPath, Array.Empty<byte>());

        using var ctx = new ConcordHostLoadContext(hostFolder);

        var resolved = ctx.TryResolveNativePath(probeName, out var path);

        resolved.Should().BeTrue();
        path.Should().Be(stubPath);
    }

    [Fact]
    public void TryResolveNativePath_ReturnsFalse_WhenNoNativeFolderExists()
    {
        var hostFolder = Path.Combine(_tempDir, "host");
        Directory.CreateDirectory(hostFolder);

        using var ctx = new ConcordHostLoadContext(hostFolder);

        var resolved = ctx.TryResolveNativePath("nonexistent", out var path);

        resolved.Should().BeFalse();
        path.Should().BeNull();
    }
```

- [ ] **Step 2: Run the new tests**

Run:
```bash
dotnet test tests/Concord.Shim.Tests/ --no-restore --filter "FullyQualifiedName~TryResolveNativePath" 2>&1 | tail -10
```

Expected: 2/2 passing on whatever platform you're running.

- [ ] **Step 3: Run full suite to confirm no regression**

Run:
```bash
dotnet test tests/Concord.Shim.Tests/ --no-restore 2>&1 | tail -15
```

Expected: 10/10 passing (original 7 + Task 2 regression + Task 4's 2 native probe tests).

- [ ] **Step 4: Commit**

```bash
git add tests/Concord.Shim.Tests/ConcordHostLoadContextTests.cs
git commit -m "test(shim): TryResolveNativePath finds RID-specific natives + no-folder fallback

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Full-solution build + test sweep

**Files:** None (verification).

- [ ] **Step 1: Build the whole solution**

Run from repo root:
```bash
dotnet build Terminal.sln 2>&1 | tail -10
```

Expected: 0 errors. Warnings should match the pre-fix baseline (no new warnings introduced).

- [ ] **Step 2: Run the full test suite**

Run:
```bash
dotnet test Terminal.sln 2>&1 | tail -20
```

Expected: all tests pass across `Terminal.Tests`, `Concord.Core.Tests`, `Concord.Shim.Tests`. The Mac CI run will skip any Maia-live tests (already marked Skip) — that's expected.

- [ ] **Step 3: No commit; this is a checkpoint**

If anything failed, stop and investigate before proceeding to deploy.

---

## Task 6: Local Mac deploy + Studio Pro 11.10 smoke test

**Files:** None (manual smoke).

This is the load-bearing test. The fix is correct iff Studio Pro 11.10 on Mac loads the extension without the composition exception.

- [ ] **Step 1: Identify the deploy mechanism**

Read `src/build/DeployMergedToMendix.targets` (referenced from `Concord.Shim.csproj`) to understand how the merged shim deploys. There's probably an MSBuild property like `MendixDeployTargetShim` (per the parent spec §"Scope of changes §3") pointing at a target Mendix project directory.

```bash
cat src/build/DeployMergedToMendix.targets 2>/dev/null | head -50
```

- [ ] **Step 2: Configure the deploy target**

`Directory.Build.props` is gitignored (per-developer settings). If it doesn't already point at a Mac Mendix test project, set `MendixDeployTargetShim` (or the equivalent property) to e.g. `/Users/$(USER)/Mendix/GraphViewer` — wherever your Mac test project lives.

If `Directory.Build.props` doesn't exist on the Mac repo, copy from `Directory.Build.props.example` and edit:

```bash
cp Directory.Build.props.example Directory.Build.props
# edit Directory.Build.props to set MendixDeployTargetShim
```

- [ ] **Step 3: Wipe the existing cache + extension folder on the Mac test project**

```bash
TEST_PROJECT="/Users/$(whoami)/Mendix/GraphViewer"  # adjust to your project
rm -rf "$TEST_PROJECT/.mendix-cache"
rm -rf "$TEST_PROJECT/extensions/Concord"
```

(The cache wipe is critical — Studio Pro snapshots extensions on first load; without a wipe, you'd be testing against the stale snapshot from the broken build.)

- [ ] **Step 4: Build + deploy via MSBuild**

```bash
dotnet build src/Concord.Shim/Concord.Shim.csproj -c Debug 2>&1 | tail -15
```

Verify the deploy ran (the targets should log "Deployed merged Concord to <path>" or similar).

- [ ] **Step 5: Verify deployed layout matches expectations**

```bash
ls "$TEST_PROJECT/extensions/Concord/"
```

Expected: `manifest.json`, `Concord.Shim.dll`, `Concord.Core.dll`, `bin-10x/`, `bin-11x/`, `runtimes/`, `wwwroot/`, `skills/`, etc.

- [ ] **Step 6: Open the test project in Studio Pro 11.10 on Mac**

Launch Studio Pro 11.10. Open the test project.

Expected: no error dialog. Studio Pro starts cleanly.

- [ ] **Step 7: Check the shim log for any errors during load**

```bash
tail -50 /tmp/Concord/shim.log 2>/dev/null || tail -50 "$TMPDIR/Concord/shim.log"
```

Expected: `INFO` lines showing `HostKickstart.ResolveHostFolder`, `HostKickstart.BuildLoadContext`, `HostKickstart.LoadHostAssembly`, `HostKickstart.InstantiateEntry` — all with timing. No `ERROR` lines. No `Hostpolicy must be initialized…` anywhere.

- [ ] **Step 8: Open the Concord pane**

In Studio Pro: Extensions menu → Concord → Open Pane (exact menu path may differ; follow whatever opens the pane).

Expected: pane opens, terminal renders, prompt is interactive.

- [ ] **Step 9: Smoke-test a tool call**

In the terminal pane, run whichever CLI is configured (Claude Code, Codex, or Copilot). Trigger a tool call that hits the Concord MCP server — e.g., ask the CLI to invoke `save_all`.

Expected: tool call round-trips successfully. The save_all action visibly runs in Studio Pro.

- [ ] **Step 10: Document the smoke result**

No commit. Capture in the eventual PR description:
- Pane open: ✅ / ❌
- Terminal works: ✅ / ❌
- `save_all` round-trip: ✅ / ❌
- Any unexpected log lines: list

If anything failed, **stop here** and diagnose before proceeding to Task 7.

---

## Task 7: Mac smoke test — Studio Pro 10.24.13 (if available)

**Files:** None (manual smoke).

Regression check on the 10.x host loaded via the shim's `bin-10x/` branch.

- [ ] **Step 1: Repeat Task 6 steps 3–9 against Studio Pro 10.24.13 on Mac**

If you don't have Studio Pro 10.24.13 installed on Mac, skip this task and document the gap in the PR description. The fix is platform-agnostic and Windows already covers 10.x; missing Mac+10.x coverage is acceptable for a Mac-fix PR but flag it.

---

## Task 8: Windows regression smokes (you, on the Windows side)

**Files:** None (manual smoke).

After the Mac side passes, hand off to Windows for regression validation.

- [ ] **Step 1: Push the branch + open a draft PR**

On Mac:
```bash
git push origin fix/concord-shim-mac-loadcontext
gh pr create --draft --title "fix(shim): make Concord work on macOS" --body "WIP — see plan + spec. Awaiting Windows-side regression smokes (Tasks 8) before un-drafting."
```

- [ ] **Step 2: Pull on Windows + build + smoke SP 11.10 + smoke SP 10.24.13**

On Windows:
```powershell
git fetch origin
git checkout fix/concord-shim-mac-loadcontext
git pull
dotnet build src\Concord.Shim\Concord.Shim.csproj -c Debug
# wipe + open SP 11.10 test project
# wipe + open SP 10.24.13 test project
```

For each Studio Pro version: open pane, terminal works, `save_all` round-trips. Same criteria as Task 6 step 9.

- [ ] **Step 3: Document Windows results in the PR**

If both versions pass on Windows, un-draft the PR and request review.

---

## Task 9: Version bump + tag (deferred — release decision)

**Files:** None in this plan.

Originally Task 15 in the pre-revision plan. **Deferred** because the spec's stated target version (`5.0.3`) doesn't match the actual current version (`5.1.0-alpha.1`). The release-version question is a Joe-call:

- `5.1.0-alpha.2`: the Mac fix is the next alpha cut of the existing 5.1.0 work-in-progress.
- `5.1.0` final: skip the alpha numbering; this fix completes the cross-version + Mac-cross-platform work to a shippable state.
- Something else entirely.

The wrapper-module version (in ConcordPublisher) and the assembly version (in `Concord.Shim.csproj` line 11) both need updating, and the `.mxmodule` re-export step needs running, per CLAUDE.md's "Things that bit us before" note about version baking. Capture as an explicit follow-up commit once the version target is settled.

---

## Self-Review

**Spec coverage:**

| Spec section | Implementing task |
|---|---|
| §Goal — 4-cell matrix passes | Tasks 6 (Mac 11.10), 7 (Mac 10.24.13), 8 (Windows 11.10 + 10.24.13) |
| §Approach §1 — Replace AssemblyDependencyResolver | Task 1 (drop entirely) + Task 2 (regression test) |
| §Approach §2 — `Assembly.Location` audit | Already correct; verified during pre-execution audit |
| §Approach §3 — Native binary handling | Tasks 3 + 4 (`LoadUnmanagedDll` override + tests) |
| §Approach §4 — Wrapper version bump | Task 9 — deferred pending version target |
| §Tests Unit — covered behaviors | Tasks 2 + 4 add 3 new unit tests on top of existing 7 |
| §Tests Integration — smoke matrix | Tasks 6, 7, 8 |
| §Tests Negative — missing bin-{Nx}/, missing native | Not exercised; deferred (low value vs. test cost) |
| §Open Q1 — log location on Mac | Resolved automatically — `ShimLog.cs` uses `Path.GetTempPath()` which is `$TMPDIR` on Mac (`/var/folders/...`), no code change needed |
| §Open Q2 — runtimes/ folder location | Task 3's probe handles both `<extension>/runtimes/` (one level up from host folder) and `<host>/runtimes/` (alongside) |
| §Open Q3 — Mac CI | Not addressed; flag as follow-up in PR description |
| §Open Q4 — Intel Mac RID | RID fallback covers `osx-x64` and `osx-arm64` automatically |

**Placeholder scan:** None. Every step has concrete code or commands. Task 9's deferral is explicit, not a hidden TODO.

**Type consistency:** `ConcordHostLoadContext`, `_hostFolder`, `Resolve`, `OnResolving`, `TryResolveNativePath`, `NativeProbePaths`, `RidFallbackChain` — all match between source-file references and test references. Task 3's helper visibility (`internal bool TryResolveNativePath`) matches Task 4's test usage.

**Scope:** 9 tasks. Tasks 1–5 are local-on-Mac code changes (~5 commits). Tasks 6–8 are manual smokes. Task 9 deferred. Tight enough for one PR.
