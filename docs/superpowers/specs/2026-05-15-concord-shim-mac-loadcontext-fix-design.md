# Concord shim — Mac load-failure fix (`v5.0.3`)

> Status: design · 2026-05-15 · target release: `v5.0.3` (Mac-functional)

## Context

The Phase 1 implementation of [`docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md`](2026-05-15-concord-runtime-shim-design.md) is built and packaged as `Concord.Shim/5.1.0-alpha.1` (the deployed `.deps.json` confirms this assembly version against `Concord.Core/5.0.0-alpha.2`). On **Windows**, the same `.mxmodule` loads and runs on every Studio Pro version Concord supports — 10.24.13, 11.x through 11.9, and 11.10+. On **macOS** with Studio Pro 11.10, the same `.mxmodule` fails MEF activation:

```
1) Dependency resolution failed for component
   /Users/<user>/Mendix/<proj>/.mendix-cache/extensions-cache/<guid>/bin-11x/Concord.Host11x.dll
   with error code -2147450750. Detailed error:
   Hostpolicy must be initialized and corehost_main must have been called
   before calling corehost_resolve_component_dependencies.

Resulting in: The type initializer for
   'Concord.Shim.WebServer.TerminalWebServerShim' threw an exception.
Resulting in: Cannot activate part
   'Concord.Shim.WebServer.TerminalWebServerShim'.
Element: ... AssemblyCatalog (Assembly="Concord.Shim, Version=5.1.0.0, ...")
```

A separate, mechanical issue: the installed `.mxmodule` shows version **`4.2.2`** in Studio Pro's extension UI when it should show **`5.0.3`**. The wrapper Studio Pro module (`ConcordPublisher`) carries the public version metadata in its `.mpr`; Studio Pro re-bakes that into the `.mxmodule` at export time. The currently-shipped `.mxmodule` was exported from a pre-bumped wrapper.

## Goal

Concord `v5.0.3` installs from a single `.mxmodule` and produces a working extension on **all four** targets:

| Platform | Studio Pro version | Outcome |
|---|---|---|
| Windows | 10.24.13 | Works (unchanged from current behavior) |
| Windows | 11.10 | Works (unchanged from current behavior) |
| macOS | 10.24.13 | Pane opens via `bin-10x/` host (regression-class check) |
| macOS | 11.10 | Pane opens, MCP server starts, `save_all` round-trips |

Plus: the extension reports its version as `5.0.3` in Studio Pro's UI.

## Non-goals

- **No changes to `Concord.Core` or either host (`Concord.Host10x`, `Concord.Host11x`).** The fix is contained to `Concord.Shim`.
- **No revisit of the Phase 0 spike findings.** Q1/Q2/Q3 stand. The shim's high-level architecture (single MEF-discovered DLL + `AssemblyLoadContext` + `Resolving` event for cross-context API sharing) is unchanged; only the *dependency probe mechanism inside the load context* changes.
- **No Mac-specific feature work** (PTY, WebView, code-signing, Gatekeeper). `UnixPtySession` already handles POSIX PTY; Eto.Forms handles WKWebView; signing and Gatekeeper are out of scope for an open-source `.mxmodule`.
- **No performance benchmark.** The Phase 0 spike's open question about pane-open latency stays open in its current home; this spec doesn't introduce a measurable regression and doesn't need its own number.

## Root cause

`Concord.Shim`'s `ConcordHostLoadContext` uses [`AssemblyDependencyResolver`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblydependencyresolver) (confirmed by `strings` on the deployed DLL — `AssemblyDependencyResolver`, `ResolveAssemblyToPath`, `LoadFromAssemblyPath` all present). `AssemblyDependencyResolver` reads `<host>.deps.json` and resolves dependency paths by P/Invoking native `hostpolicy.dll` / `libhostpolicy.dylib`'s `corehost_resolve_component_dependencies`. That export has a hard precondition in [runtime source](https://github.com/dotnet/runtime/blob/main/src/native/corehost/hostpolicy/hostpolicy.cpp):

```cpp
if (g_init.host_info.fxr_path.empty())
{
    trace::error(_X("Hostpolicy must be initialized and corehost_main must "
                    "have been called before calling "
                    "corehost_resolve_component_dependencies."));
    return InvalidArgFailure;  // -2147450750 / 0x80008082
}
```

`g_init.host_info.fxr_path` is populated only when the process was started via the standard `corehost_main` flow — i.e., a `dotnet`-CLI launch or an `apphost`-style EXE that links the standard host. Studio Pro on Windows is `apphost`-style, so the field is set, and `AssemblyDependencyResolver` works. Studio Pro on Mac is a `.app` bundle whose native launcher embeds .NET via `hostfxr_initialize_for_runtime_config` (or equivalent embedded-host APIs) without going through `corehost_main`, so `fxr_path` is empty and every call into `corehost_resolve_component_dependencies` returns `InvalidArgFailure`. This is a documented .NET runtime limitation (e.g., [dotnet/runtime#61573](https://github.com/dotnet/runtime/issues/61573)).

The error chain in the user-visible `CompositionException`:

1. Studio Pro MEF activates `Concord.Shim.WebServer.TerminalWebServerShim` (first export touched).
2. Its static constructor builds the process-wide `ConcordHostLoadContext`.
3. The load context's constructor instantiates `new AssemblyDependencyResolver(hostFolderPath)`.
4. The resolver eagerly calls `corehost_resolve_component_dependencies` to seed itself from `bin-11x/Concord.Host11x.deps.json`.
5. Native call fails with `InvalidArgFailure` → managed `InvalidOperationException` → cctor throws → MEF reports it as a part-activation failure with the full cascade.

The same crash would fire on any of the three shim exports (Pane / Menu / WebServer) — `TerminalWebServerShim` is just whichever MEF tries first in the activation order.

## Approach

Three mechanical changes inside `src/Concord.Shim/`, plus one release-step change to the wrapper module.

### 1. `ConcordHostLoadContext` — replace `AssemblyDependencyResolver` with file-system probing

The Phase 1 shim should look approximately like this (pseudocode pending sight of the actual Windows source — implementation details may shift slightly, but the shape is fixed):

```csharp
internal sealed class ConcordHostLoadContext : AssemblyLoadContext
{
    private readonly string _hostFolder;     // e.g. .../extensions/Concord/bin-11x/
    private readonly string _runtimesFolder; // e.g. .../extensions/Concord/runtimes/

    public ConcordHostLoadContext(string hostFolder, string runtimesFolder)
        : base(name: "ConcordHost", isCollectible: false)
    {
        _hostFolder = hostFolder;
        _runtimesFolder = runtimesFolder;
        Resolving += BounceSharedToDefaultContext;  // existing logic, unchanged
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null) return null;
        var candidate = Path.Combine(_hostFolder, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var probe in NativeProbePaths(unmanagedDllName))
            if (File.Exists(probe))
                return LoadUnmanagedDllFromPath(probe);
        return IntPtr.Zero;
    }

    private IEnumerable<string> NativeProbePaths(string name)
    {
        // RuntimeInformation.RuntimeIdentifier gives the most specific RID
        // (e.g., osx-arm64); fall back through the RID graph.
        foreach (var rid in RidFallbackChain())
        {
            var native = Path.Combine(_runtimesFolder, rid, "native");
            if (!Directory.Exists(native)) continue;
            yield return Path.Combine(native, name);
            yield return Path.Combine(native, "lib" + name + ".dylib");  // mac
            yield return Path.Combine(native, name + ".dll");            // win
            yield return Path.Combine(native, "lib" + name + ".so");     // linux
            yield return Path.Combine(native, name + ".dylib");          // mac alt
        }
        // last-ditch: top of _hostFolder
        yield return Path.Combine(_hostFolder, name);
    }

    private static IEnumerable<string> RidFallbackChain() { /* osx-arm64 → osx → unix → any */ }
}
```

**Key points:**

- **No `AssemblyDependencyResolver` anywhere.** No deps.json parsing. No native hostpolicy call. The entire failure mode disappears.
- **The `Resolving` event handler stays as-is.** It's the cross-context bounce that keeps `Mendix.StudioPro.ExtensionsAPI` (and `System.*`, `Microsoft.Extensions.*`) on Studio Pro's default-context copy. Phase 0 Q2 validated this mechanism end-to-end; it's platform-independent and doesn't go through hostpolicy.
- **`LoadUnmanagedDll` is new.** The current shim likely doesn't override it, which means SQLite's native bits (`libe_sqlite3.dylib`, `libe_sqlite3.so`, `e_sqlite3.dll`) fall back to the default native-DLL search, which uses similar hostpolicy state and is likely broken on Mac for the same reason. Even if it isn't, the lookup would be wrong because the natives live under `runtimes/<rid>/native/` in the deployed snapshot, not at the host-folder top level. Override + explicit probe is required either way for Mac to work past first SQLite operation.
- **RID fallback chain.** `RuntimeInformation.RuntimeIdentifier` returns the most specific RID at runtime (e.g., `osx-arm64`). The .NET RID graph is a tree (`osx-arm64` → `osx` → `unix` → `any`); we walk it in specificity order so a package that only shipped `osx/native/libe_sqlite3.dylib` still resolves on Apple Silicon.

### 2. Folder-root resolution — verify (or fix) `Assembly.Location` usage

Phase 0 spike Probe Bug B: `AppDomain.CurrentDomain.BaseDirectory` returns Studio Pro's install dir on Windows; on Mac it almost certainly returns something inside `/Applications/Studio Pro 11.10.app/Contents/MonoBundle/` or similar. Any shim code computing `bin-{Nx}/` relative to `BaseDirectory` is silently broken on Mac in addition to the resolver issue in §1.

The Windows-side audit (done as part of executing this spec's plan) must confirm `RuntimeHostLocator.Resolve()` — and any other path-computing code in the shim — uses:

```csharp
var shimAssemblyDir = Path.GetDirectoryName(typeof(TerminalPaneExtensionShim).Assembly.Location)!;
```

If the current Phase 1 implementation already uses `Assembly.Location`, this is a no-op. If it uses `BaseDirectory`, swap it. The Phase 0 handoff (§"Probe bugs discovered", item B) is explicit that this is non-obvious and that anyone implementing the spec from scratch would reach for `BaseDirectory` first.

### 3. Native binaries must actually be in the deployed `runtimes/` folder

The deployed snapshot at `/Users/<user>/Mendix/<proj>/.mendix-cache/extensions-cache/<guid>/runtimes/` is the source of truth. Confirm during plan execution that the `.mxmodule` packaging step includes the full `runtimes/` tree:

- `runtimes/osx-arm64/native/libe_sqlite3.dylib` (and `osx-x64/` for Intel Macs)
- `runtimes/win-x64/native/e_sqlite3.dll`
- `runtimes/linux-x64/native/libe_sqlite3.so` (only if Linux is ever a target — not in scope for v5.0.3)

These come from `SQLitePCLRaw.lib.e_sqlite3` package's `runtimes/` content. Most modern .NET build targets copy them automatically into the published output's `runtimes/` folder; verify in the Phase 1 shim's `bin/Debug/net8.0/runtimes/` and in the assembled `.mxmodule` bundled-resources tree.

### 4. Wrapper module version bump + `.mxmodule` re-export

ConcordPublisher (`C:\Workspace\MendixApps\ConcordPublisher` per [CLAUDE.md](../../../CLAUDE.md)) is the Studio Pro module that wraps Concord's binaries for `.mxmodule` export. Its `Module.mpr` carries the public version metadata that Studio Pro displays in Extensions → Manage. Set the field to `5.0.3` and re-run the Studio Pro UI export step. Per CLAUDE.md's "Things that bit us before":

> Version bump alone is NOT enough — Studio Pro re-bakes the version into the .mxmodule at export time. Must redo the UI export step.

No code change in the Concord repo itself. The plan captures this as an explicit release-step task with a checklist so it doesn't get skipped again.

## Tests

### Unit (`tests/Concord.Shim.Tests/`)

A new test project (or new test file in an existing shim-test project — pending Windows-side audit of the current Phase 1 test layout):

- **`ConcordHostLoadContextTests.Load_FindsAssemblyInHostFolder`** — given a temp folder containing a stub `TestHost.dll` (a minimal assembly built at test-setup), assert `Load(new AssemblyName("TestHost"))` returns a non-null Assembly whose `Location` matches the expected path.
- **`ConcordHostLoadContextTests.Load_ReturnsNullForUnknownAssembly`** — passes an assembly name with no matching DLL, asserts `null` return so the `Resolving` event still gets a chance.
- **`ConcordHostLoadContextTests.LoadUnmanagedDll_ProbesRidFolders`** — given a temp `runtimes/osx-arm64/native/libfake.dylib` (touch'd, doesn't need real native code; we're testing path resolution), assert the probe returns a non-zero handle / the expected path on Mac, and the corresponding `.dll` path on Windows under `runtimes/win-x64/native/`.
- **`ConcordHostLoadContextTests.LoadUnmanagedDll_FallsBackThroughRidGraph`** — name `libe_sqlite3.dylib` lives only at `runtimes/osx/native/`, not `runtimes/osx-arm64/native/`; assert probe still resolves on `osx-arm64` via the fallback chain.
- **`ConcordHostLoadContextTests.NoAssemblyDependencyResolver`** — sanity assertion (via Reflection over the type) that `ConcordHostLoadContext` declares no field of type `AssemblyDependencyResolver`. Prevents accidental regression.

These tests run on any CI agent — they don't need Studio Pro or a real Mendix project, just the .NET 8 SDK.

### Integration (manual, cross-platform smoke matrix)

Executed by the dev (Joe) after merging the fix, before tagging `v5.0.3`. Goal is to verify the four-cell matrix in §Goal works end-to-end:

| Cell | Steps | Pass criteria |
|---|---|---|
| macOS arm64 / SP 11.10 | (1) Wipe `~/Mendix/<test-proj>/.mendix-cache/`. (2) Install `.mxmodule` via Studio Pro Marketplace local-file route. (3) Open project, Extensions → Concord → Open Pane. (4) In the terminal, run `claude` (or whichever CLI is configured), trigger `save_all` via a tool call. | Pane opens without composition exception. Terminal echoes input. `save_all` returns success. |
| macOS arm64 / SP 10.24.13 | Same steps. | Pane opens (via `bin-10x/` host). Terminal echoes. Action server starts on port 7783. |
| Windows / SP 11.10 | Same steps. | No regression from current Windows behavior. |
| Windows / SP 10.24.13 | Same steps. | No regression. |

For each cell, capture the Studio Pro Extensions UI showing `Concord v5.0.3` (validates the wrapper-module re-bake in §4). Screenshots in PR description; no checked-in artifacts.

### Negative

- **Missing `bin-{Nx}/` folder.** Manually delete `bin-11x/` from a deployed Mac install and reopen the project. Expected: shim's `ConcordHostLoadContext` constructor throws a clear "host folder not found at <path>" exception, logged to `%TEMP%\Concord\shim.log` (or Mac equivalent — `~/Library/Logs/Concord/shim.log`?), surfaced in Studio Pro's extension UI as a load failure. Does not crash Studio Pro.
- **Missing `runtimes/osx-arm64/native/libe_sqlite3.dylib`.** Manually delete it from a deployed Mac install. Expected: pane opens (no SQLite call yet), but the first feature that touches the session DB (terminal-history save, settings persistence) throws a `DllNotFoundException` with the probed paths in the message. Recoverable by reinstalling. (This is an artifact-completeness regression check, not a code path we add handling for — the deployed file is supposed to be there.)

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Phase 1 shim's actual code structure differs from this spec's pseudocode in ways that change the diff shape | Med | Plan needs revision once Windows source is visible | Plan's first task is "read `src/Concord.Shim/` and align the spec's pseudocode to actual class/method names before touching code"; if structural drift is large, raise it before coding |
| `LoadUnmanagedDll` override misses a probe path used by SQLitePCLRaw at runtime | Low | SQLite native DLL doesn't resolve; pane opens but session-DB calls throw | Tests verify the standard RID graph; if SQLitePCLRaw uses a non-standard layout, the test surface that and we add the probe |
| RID detection on Mac returns something we don't handle (e.g., `osx.11.0-arm64` from older .NET versions) | Low | Fallback chain misses the most-specific RID | `RuntimeInformation.RuntimeIdentifier` on .NET 8 returns clean `osx-arm64` / `osx-x64`; if a user reports otherwise we expand the chain |
| Studio Pro 12.x changes the Mac launcher to use the standard `corehost_main` path | Low | Our manual probe becomes redundant but still works | The fix doesn't depend on Mac-specific hosting state; it works on both Mac launchers. No revert needed. |
| Studio Pro 11.10 on macOS introduces a per-extension hostpolicy init in a future patch | Very low | Same as above — we're a strict superset | n/a — fix works regardless |
| Version-display fix in §4 is forgotten (re-export step skipped again) | Med (CLAUDE.md flags this exact mistake) | Extension installs but UI says wrong version | Plan checklist makes the re-export step an explicit gate; the smoke-matrix screenshots in §Tests catch it |
| `Concord.Core.dll` divergence between top-level copy and `bin-{Nx}/` copies on Mac specifically | Low | Existing risk from parent spec; not Mac-specific | Existing CI gate from parent spec applies; not in scope here |

## Open questions for the implementation plan

1. **Log location on Mac.** The Phase 0 spike's "Logging location for the shim" open question listed `%TEMP%\Concord\shim.log` for Windows. Mac equivalent should probably be `~/Library/Logs/Concord/shim.log` (Apple-conventional) or `/tmp/Concord/shim.log` (matches Windows `%TEMP%` semantics). The plan picks one — recommend Apple-conventional `~/Library/Logs/Concord/`. Same code path on both platforms via `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` or `Path.GetTempPath()` — TBD pending sight of the current shim logging code.
2. **Whether to keep the `runtimes/` folder at the top of `extensions/Concord/` or move it inside each `bin-{Nx}/`.** Current snapshot has it at the top (shared between hosts). That's correct — both hosts use the same SQLite native — so we leave it. But the plan should verify the deployed structure matches and the build target copies it to the right place.
3. **Whether to add a Mac CI job to the existing GitHub Actions matrix.** Commit `2b183d0` ("ci: build + test on Windows and macOS (W1 Task 17)") suggests Mac CI exists but builds only — not extension-loading. The plan flags a follow-up to add a Mac smoke test that at minimum runs the new unit tests; full extension-loading needs a Studio Pro install which CI doesn't have.
4. **RID for Intel Macs.** Concord's Mac user base is almost certainly Apple Silicon, but `.mxmodule` packaging should include `osx-x64/native/` too for completeness. Plan to verify the build pulls both RIDs from the NuGet packages.

## Sub-spec / sub-plan map

- **This spec** — design for the Mac load-failure fix.
- **Plan** — to be written next (via `writing-plans` skill). Atomic tasks: (a) read current Phase 1 `src/Concord.Shim/` on Windows and align with this spec, (b) replace `AssemblyDependencyResolver` with overridden `Load`/`LoadUnmanagedDll`, (c) verify `Assembly.Location` usage in `RuntimeHostLocator`, (d) verify `runtimes/` tree in build output, (e) bump ConcordPublisher wrapper to `5.0.3` and re-export `.mxmodule`, (f) run cross-platform smoke matrix.

## See also

- Parent spec — [docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md](2026-05-15-concord-runtime-shim-design.md)
- Phase 0 spike handoff — [docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md](../handoffs/2026-05-15-concord-shim-spike-findings.md)
- Maia-on-Mac feasibility (out-of-scope but related Mac context) — [docs/MAIA_MAC_FEASIBILITY.md](../../MAIA_MAC_FEASIBILITY.md)
- Release playbook — `~/.claude/projects/.../memory/reference_concord_release_playbook.md` (per [CLAUDE.md](../../../CLAUDE.md))
- [[runtime-shim-cross-version]] — broader context for why the shim architecture is necessary
- [[mendix-extension-cache]] — Studio Pro's `.mendix-cache/extensions-cache/<guid>/` snapshot mechanism
