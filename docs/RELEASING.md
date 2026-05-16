# Releasing Concord

Maintainer playbook for cutting a Concord release: bump versions, build the cross-version `.mxmodule`, tag, publish, and (when appropriate) upload to the Mendix Marketplace.

This is a **Windows-only** procedure — Studio Pro's "Export Add-on Module" UI is what re-bakes the public version into the `.mxmodule`, and that UI exists on Windows Studio Pro only (Mac Studio Pro 11.10 has the import side but not the export side; SP 10.24.13 on Mac doesn't have a usable export flow either as of this writing).

See also: [CONTRIBUTING.md](../CONTRIBUTING.md) for general PR + commit conventions, [DEPLOYING.md](../DEPLOYING.md) for end-user install paths.

---

## Quick reference: where each version lives

A release bump touches several places. Keep them in lockstep:

| Where | What | Bumped via |
|---|---|---|
| `src/Concord.Shim/Concord.Shim.csproj` | `<Version>` | manual edit |
| `src/Concord.Core/Concord.Core.csproj` | `<Version>` | manual edit |
| `src/Concord.Host10x/Concord.Host10x.csproj` | `<Version>` | manual edit |
| `src/Concord.Host11x/Concord.Host11x.csproj` | `<Version>` | manual edit |
| Wrapper Mendix project's `Module.mpr` | Module version (marketplace-displayed) | Studio Pro UI before re-export |
| `CHANGELOG.md` | New top entry | manual edit |
| `README.md` | "Current version" headline | manual edit |
| `DEPLOYING.md` | "Migrating to X.Y.Z" section | manual edit (when upgrade path is non-trivial) |
| `marketing/marketplace-*.{md,html}` | "What's new in X.Y.Z" content | manual edit (4 surfaces; keep MD + HTML in sync) |

**CLAUDE.md gotcha:** Studio Pro re-bakes the wrapper module's version into the `.mxmodule` at *export time*. Bumping the assembly versions in the csproj files alone is NOT enough — you must also bump the wrapper module's version in its `Module.mpr` BEFORE the export, otherwise the new `.mxmodule` ships with the assemblies at the new version but the marketplace UI still shows the old version.

---

## Building the cross-version `.mxmodule`

The `.mxmodule` is what end users install. A single `.mxmodule` must work on all four supported targets (Windows + macOS × Studio Pro 10.24.13 + 11.10). This requires the **merged-shim layout** in `extensions/Concord/`, NOT the per-host `extensions/Concord10x/` + `Concord11x/` layout.

> **What goes wrong if you export from a per-host layout:** the resulting `.mxmodule` only contains whichever per-host folder was deployed. Importing it into the *other* Studio Pro version produces a 10x-on-11x (or vice versa) mismatch — Studio Pro loads the wrong host, the menu shows the wrong name (`Concord 10x` on an 11.10 install), and deeper ExtensionsAPI calls crash on version mismatch. Always export from the merged-shim layout.

### Layout that produces a working cross-version `.mxmodule`

```
<source-project>/
└── extensions/
    └── Concord/                           ← single merged-shim folder
        ├── Concord.Shim.dll               ← the MEF entry point
        ├── Concord.Core.dll
        ├── manifest.json                  ← { "mx_extensions": ["Concord.Shim.dll"] }
        ├── bin-10x/
        │   ├── Concord.Host10x.dll        ← bound to ExtensionsAPI 10.21.1
        │   └── Concord.Core.dll           ← SHA256-identical to root copy
        ├── bin-11x/
        │   ├── Concord.Host11x.dll        ← bound to ExtensionsAPI 11.6.2
        │   └── Concord.Core.dll           ← SHA256-identical to root copy
        ├── runtimes/
        │   ├── osx-arm64/native/libe_sqlite3.dylib
        │   ├── osx-x64/native/libe_sqlite3.dylib
        │   ├── win-x64/native/e_sqlite3.dll
        │   └── linux-x64/native/libe_sqlite3.so
        ├── wwwroot/, rules/, rules-10x/, skills/, skills-10x/, skills-mac/
```

There must be **no** `extensions/Concord10x/` or `extensions/Concord11x/` alongside this — they're the dev-iteration layout and would dual-load against the shim.

### Step-by-step (verified procedure for v6.0.0)

This was the working sequence on 2026-05-16 for Concord 6.0.0. Adapt versions as needed.

#### 1. Bump assembly versions in source

Edit the four `<Version>` elements:

```diff
- <Version>5.1.0-alpha.1</Version>   (in src/Concord.Shim/Concord.Shim.csproj)
+ <Version>6.0.0</Version>

- <Version>5.0.0-alpha.2</Version>   (in src/Concord.Core/Concord.Core.csproj)
+ <Version>6.0.0</Version>

- <Version>5.0.0-alpha.2</Version>   (in src/Concord.Host10x/Concord.Host10x.csproj)
+ <Version>6.0.0</Version>

- <Version>5.0.0-alpha.2</Version>   (in src/Concord.Host11x/Concord.Host11x.csproj)
+ <Version>6.0.0</Version>
```

The Shim and the two Hosts can diverge during pre-release iteration; for a shipping release they must all agree.

#### 2. Configure `Directory.Build.props` for merged-shim deploy

`Directory.Build.props` is gitignored (per-developer). For a release build, set `MendixDeployTargetMerged` and comment out (or remove) the per-host targets so the dev-iteration layout doesn't dual-load against the shim:

```xml
<Project>
  <PropertyGroup>
    <!--
      Per-host targets DISABLED while deploying merged-shim for .mxmodule export.
      Re-enable for component-level dev iteration.
    -->
    <!-- <MendixDeployTarget10x>C:\Projects\Test_10_24_13</MendixDeployTarget10x> -->
    <!-- <MendixDeployTarget11x>C:\Projects\Test_11_10</MendixDeployTarget11x> -->
    <MendixDeployTargetMerged>C:\Projects\Test_10_24_13</MendixDeployTargetMerged>
    <ExtensionsApi10xVersion>10.21.1</ExtensionsApi10xVersion>
    <ExtensionsApiShimBaselineVersion>10.21.1</ExtensionsApiShimBaselineVersion>
  </PropertyGroup>
</Project>
```

The target project can be any Mendix project — `Test_10_24_13` is one of the dev test projects on this machine. Either SP 10.24.13 or 11.10 can serve as the export host; SP 10.24.13 is fine even though the project's Mendix runtime version might differ from 11.x. Studio Pro's export-module flow doesn't care about runtime version match; it just packages whatever is in `extensions/Concord/` plus the project metadata.

#### 3. Wipe any prior Concord install from the target project

A stale `Concord10x/` (or `Concord11x/`) alongside the new merged `Concord/` produces dual-load activation; a stale `Concord/` from a pre-fix shim build is also a problem on Mac. From PowerShell:

```powershell
Remove-Item -Recurse -Force C:\Projects\Test_10_24_13\extensions\Concord10x, C:\Projects\Test_10_24_13\extensions\Concord11x, C:\Projects\Test_10_24_13\extensions\Concord, C:\Projects\Test_10_24_13\.mendix-cache -ErrorAction SilentlyContinue
```

#### 4. Build with `-p:Platform=x64`

```sh
dotnet build src/Concord.Shim/Concord.Shim.csproj -c Debug -p:Platform=x64 --no-incremental
```

The `x64` platform matters: `MergeHostsForShim.targets` hardcodes the merged output to `bin/x64/$(Configuration)/net8.0-merged/` because the production `.mxmodule` is x64. Building with `-p:Platform=x64` makes the host bin source paths and the merged-output destination line up. AnyCPU builds warn ("MergeHostsForShim: Platform is 'AnyCPU' …; host bin sources may not match the merged output location. Run with -p:Platform=x64 for a trustworthy merge.") and produce an output that may diverge between the merged tree and per-host source — don't ship from an AnyCPU build.

The build runs:
1. **BuildUi** — `npm install` (first build only, ~30s) + `node esbuild.mjs`; produces `wwwroot/terminal.bundle.js`.
2. **C# compile** for `Concord.Core`, `Concord.Host10x`, `Concord.Host11x`, `Concord.Shim` (in that dependency order).
3. **MergeHostsForShim** — copies shim output to `bin/x64/$(Configuration)/net8.0-merged/`, then copies Host10x to `bin-10x/` and Host11x to `bin-11x/` underneath. Hoists shared content (`wwwroot`, `skills*`, `rules*`) to the top level and removes duplicates from `bin-{10|11}x/`. Verifies `Concord.Core.dll` is SHA256-identical across root + bin-10x + bin-11x (hard error if not). Verifies no `Mendix.StudioPro.ExtensionsAPI.dll` at the merged root (hard error if found — the shim must not ship its own ExtensionsAPI copy).
4. **DeployMergedToMendix** — copies `bin/x64/.../net8.0-merged/` to `$(MendixDeployTargetMerged)/extensions/Concord/`. Refreshes any existing `.mendix-cache/extensions-cache/<guid>/` that contains `Concord.Shim.dll`.

Expected console output near the end:

```
  MergeHostsForShim: Concord.Core.dll SHA256=<hash> verified across all 3 locations.
  Deploying merged shim to C:\Projects\Test_10_24_13/extensions/Concord
  175 File(s) copied
  Build succeeded.
      <N> Warning(s)
      0 Error(s)
```

If you see the AnyCPU warning or the SHA mismatch error, stop and fix before exporting — the resulting `.mxmodule` would be unusable.

#### 5. Verify the deployed layout

```powershell
$root = 'C:\Projects\Test_10_24_13\extensions\Concord'
[Reflection.AssemblyName]::GetAssemblyName("$root\Concord.Shim.dll").Version
[Reflection.AssemblyName]::GetAssemblyName("$root\bin-10x\Concord.Host10x.dll").Version
[Reflection.AssemblyName]::GetAssemblyName("$root\bin-11x\Concord.Host11x.dll").Version
(Select-String -Path "$root\Concord.Shim.dll" -Pattern 'AssemblyDependencyResolver' -SimpleMatch -Encoding utf8 | Measure-Object).Count
```

All three versions should match the bump (e.g. `6.0.0.0`). The `AssemblyDependencyResolver` count must be `0` — the Mac fix removes that type; if it reappears, something has regressed.

#### 6. Bump the wrapper module's version in Studio Pro

> This is the step that's easy to forget. The csproj version is what `Concord.Shim.dll` reports via reflection; the wrapper module's version is what the marketplace UI displays in Extensions → Manage. They are separate fields. Studio Pro re-bakes the wrapper-module version into the `.mxmodule` at export time.

1. Open `C:\Projects\Test_10_24_13` in Studio Pro (10.24.13 or 11.10 — either works).
2. In the project explorer, locate the **Concord** module that wraps the `extensions/` folder.
3. Right-click → **Properties** → bump **Version** to the release version (e.g. `6.0.0`).
4. Save the project.

#### 7. Export the add-on module

1. With the project still open in Studio Pro, right-click the **Concord** module in the project explorer.
2. **Export Add-on Module** → save anywhere (e.g. `C:\Users\<you>\Desktop\Concord.mxmodule`).

The exported file's typical size is ~40 MB (most of it is the bundled .NET runtimes/, SQLite natives, and the wwwroot bundle).

#### 8. Verify the `.mxmodule` is cross-version

A common failure is exporting from a project that has only `extensions/Concord10x/` (or only `Concord11x/`) deployed. Sanity-check the `.mxmodule` contents:

```powershell
$mx = 'C:\Users\<you>\Desktop\Concord.mxmodule'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($mx)
$keys = @('extensions/Concord/Concord.Shim.dll',
          'extensions/Concord/bin-10x/Concord.Host10x.dll',
          'extensions/Concord/bin-11x/Concord.Host11x.dll',
          'extensions/Concord/runtimes/osx-arm64/native/libe_sqlite3.dylib',
          'extensions/Concord/runtimes/win-x64/native/e_sqlite3.dll')
foreach ($k in $keys) {
  $entry = $zip.Entries | Where-Object { $_.FullName -eq $k }
  if ($entry) { Write-Output "OK  $k" } else { Write-Output "MISSING  $k" }
}
$bad = $zip.Entries | Where-Object { $_.FullName -match '^extensions/Concord1[01]x/' }
if ($bad) { Write-Output "BAD: per-host folder present ($($bad.Count) entries) — this .mxmodule is single-version, re-export from the merged-shim layout" }
$zip.Dispose()
```

All five paths should print `OK`. The "per-host folder present" check must come up clean.

#### 9. Round-trip smoke test

The `.mxmodule` only ships if it imports cleanly on all four cells of the support matrix:

| Cell | Steps | Expected |
|---|---|---|
| Windows / SP 11.10 | Import into a fresh project, open it | Menu shows `Concord` (not `Concord 10x`); pane opens; tool call round-trips |
| Windows / SP 10.24.13 | Same | Menu shows `Concord` (not `Concord 11x`); pane opens; tool call round-trips |
| macOS / SP 11.10 | Same | Same; check `$TMPDIR/Concord/shim.log` shows clean load (no `Hostpolicy must be initialized…` errors) |
| macOS / SP 10.24.13 | Same | Same |

The Mac cells are the load-bearing ones — Windows worked even when the `.mxmodule` was single-version (it just showed the wrong menu name on the wrong-version SP). Mac will hard-fail if anything in the shim regresses. The full procedure is captured in [`docs/superpowers/specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md`](./superpowers/specs/2026-05-15-concord-shim-mac-loadcontext-fix-design.md) for the v6.0.0 fix that originally validated this matrix.

---

## Tagging and releasing on GitHub

After the `.mxmodule` smokes on all four cells:

```sh
git checkout main
git pull --ff-only
git tag -a v6.0.0 -m "Concord 6.0.0 — Mac load-context fix + merged-shim deploy"
git push origin v6.0.0
```

Then create the GitHub Release with the `.mxmodule` attached as a downloadable asset:

```sh
gh release create v6.0.0 \
  --title "Concord 6.0.0" \
  --notes-file <release-notes>.md \
  C:\Users\<you>\Desktop\Concord.mxmodule
```

> **CLAUDE.md gotcha:** `gh release create --notes "<inline>"` mishandles heredocs with backticks. Write the release notes to a temp file and use `--notes-file`. Clean up the temp file after.

The release notes should mirror the new CHANGELOG entry. Users will download `Concord.mxmodule` from the Releases page.

---

## Marketplace upload (when shipping to mendix.com)

Out of scope for a code-only release; documented separately in the release playbook memory file (`reference_concord_release_playbook.md`). Headline gotchas restated from CLAUDE.md:

- **Always update the HTML AND MD versions of marketing docs together** (4 files under `marketing/`). v4.2.1 cycle missed `marketplace-overview.html` and the gap was only caught post-hoc.
- **`Component Type` on the marketplace is IMMUTABLE post-publish.** Always `Module`.

---

## Quick post-release sanity checklist

- [ ] All four `Concord.*.dll` assemblies report the new version (reflection check)
- [ ] `Concord.Core.dll` SHA256 identical across root + bin-10x + bin-11x in the deployed merge
- [ ] `AssemblyDependencyResolver` string count is 0 in `Concord.Shim.dll`
- [ ] `.mxmodule` ZIP-inspection: `extensions/Concord/{Shim.dll, bin-10x/Host10x.dll, bin-11x/Host11x.dll, runtimes/osx-arm64/native/libe_sqlite3.dylib, runtimes/win-x64/native/e_sqlite3.dll}` all present
- [ ] `.mxmodule` ZIP-inspection: no `extensions/Concord10x/` or `extensions/Concord11x/` siblings
- [ ] Smoke matrix: 4/4 cells pass (Win + Mac × 10.24.13 + 11.10)
- [ ] Tag pushed, GitHub Release created with `.mxmodule` asset attached
