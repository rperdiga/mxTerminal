# Phase 5 session handoff — Concord runtime-shim v5.1.0

> **Audience:** the next Claude Code session, picking up after Phases 1–4 + 3 fixup commits already landed on `feat/v5.1.0-runtime-shim`. Read this end-to-end before doing anything.
>
> **One-line state:** Branch is 7 commits ahead of `main` at `356b15a`; tests green (56 + 274/+3-skip + 23 = 353 PASS); merged `.mxmodule`-source layout sits ready at `bin/x64/Debug/net8.0-merged/`; awaits Ricardo's manual Studio Pro smoke pass on both versions before Phase 6+7 can resume.

---

## What's done

| Phase | Commit | What |
|---|---|---|
| 1 | `044b3a4` | `Concord.Shim` project skeleton + `ShimLog` (file logger) |
| 2 | `99df1e9` | `RuntimeHostLocator` + `ConcordHostLoadContext` (isolation primitives) |
| fixup | `a64f747` | Phase 1+2 review follow-ups (`Timed<T>` try/finally, dead code, smoke test for `ResolveBinDirectory`, etc.) |
| 3 | `ebc3370` | Three MEF forwarders (`TerminalPaneExtensionShim`, `ConcordMenuExtensionShim`, `TerminalWebServerShim`) + `HostKickstart` bootstrap chain + `[shim-vestigial]` sentinel comments on 6 host files |
| fixup | `978442a` | Phase 3 review follow-ups (sticky-failed state in `HostKickstart`, DRY'd inner-type-name probe via `LoadedHostAssemblyName`, FakeHostBuilder safety doc) |
| 4 | `294b032` | `MergeHostsForShim.targets` + SHA-256 hash gate + `BuildUi.targets` externalization + `DeployMergedToMendix.targets` + stale "MEF skips" comment fix |
| fixup | `356b15a` | Phase 4 review follow-ups (RemoveDir Condition bug fixed, AnyCPU warning, BuildUi standalone-regression doc) |

Each phase had spec-compliance + code-quality review pass before its fixup. Hash gate independently verified firing on deliberate divergence (Phase 4 review). All 5 perf instrumentation points present (`HostKickstart.ResolveHostFolder`, `BuildLoadContext`, `LoadHostAssembly`, `InstantiateEntry`, `PaneShim.CreateInner`).

## What's left

Three remaining work items from the plan ([docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md](../plans/2026-05-15-concord-runtime-shim-implementation.md)):

- **Phase 5** — Manual Studio Pro smoke matrix on SP 10.24.13 + SP 11.10. Mostly Ricardo's hands; agent's role is to coordinate, support troubleshooting if smoke fails, then compose the commit once numbers are in.
- **Phase 6** — Retire two-extension consumer surface in docs + marketing (4 surfaces — keep MD + HTML in sync; the v4.2.1 cycle's "missed marketplace-overview.html" is the cautionary tale per CLAUDE.md).
- **Phase 7** — PR + adversarial review (`/ultrareview <PR#>` is user-triggered) + merge + tag `v5.1.0` + GitHub release. Plus Phase 7 Task 7.5 memory housekeeping.

Phase 5 gates Phase 6+7. If smoke passes, Phase 6+7 are pure code/docs work that can resume in any session.

## Phase 5 — Ricardo's three manual steps

The agent CANNOT do these (`.mxmodule` export and Studio Pro smoke require Ricardo's hands per CLAUDE.md "Things that bit us before"). Sequence:

### 1. (One-time) Capture v5.0.0 baseline pane-open latency

Skip if already recorded in `_HANDOFF.md` or a prior smoke handoff.

```powershell
git stash
git checkout 09a2e41
# Pick a test project for SP 10.24.13:
$TEST = "C:\Projects\Test_10_24_13"
dotnet build src\Concord.Host10x\Concord.Host10x.csproj -p:Platform=x64 -p:MendixDeployTarget10x=$TEST
# Reset cache, launch SP 10.24.13 against $TEST, open pane, time
# manifest-load → first WebView render. Record number.
# Repeat with SP 11.10 deploy + the SP 11.10 test project.
git checkout feat/v5.1.0-runtime-shim
git stash pop
```

Record both numbers in [the smoke-results doc skeleton](2026-05-15-concord-shim-smoke-results.md) under "v5.0.0 reference baseline".

### 2. Export the .mxmodule

Open `C:\Workspace\MendixApps\ConcordPublisher` in Studio Pro 11.x. Point the bundled-resources source at `c:\Extensions\Terminal\bin\x64\Debug\net8.0-merged\` (the v5.1.0 merged layout — already built and waiting). Use the click-by-click in [reference_concord_mxmodule_build.md](file://C:/Users/rc1yok/.claude/projects/c--Extensions-Terminal/memory/reference_concord_mxmodule_build.md).

### 3. Run the smoke matrix on both Studio Pro versions

Procedure for each version (SP 10.24.13 launches with `--enable-extension-development`; SP 11.10 doesn't need the flag):

```powershell
$TEST = "C:\Projects\Test_10_24_13"  # or Test_11_10

# Reset extension state:
Remove-Item -Recurse -Force "$TEST\extensions\Concord", "$TEST\extensions\Concord10x", "$TEST\extensions\Concord11x", "$TEST\.mendix-cache\extensions-cache" -ErrorAction Ignore
Remove-Item -Force "$env:TEMP\Concord\shim.log" -ErrorAction Ignore

# Install the .mxmodule via Studio Pro UI.
# Wait for full load (project tree visible, no progress indicators).

# Collect evidence:
Get-ChildItem "$TEST\.mendix-cache\extensions-cache" | Select-Object Name, LastWriteTime
Get-Content "$env:TEMP\Concord\shim.log"
```

Expected on success:
- Exactly ONE cache snapshot folder (corresponding to `extensions/Concord/`), no separate `Concord10x`/`Concord11x` snapshots.
- shim.log contains `HostKickstart: SP version='<10.24.13|11.10>...'`, then 5 `Timed` lines.
- No `ERROR` entries.

Then open the pane (Extensions → Concord → Open Pane), confirm xterm.js renders + about button shows v5.1.0, run one MCP round-trip (e.g. `curl http://127.0.0.1:7783/save_all`).

### 4. Fill in [2026-05-15-concord-shim-smoke-results.md](2026-05-15-concord-shim-smoke-results.md)

The doc has placeholders for both versions' shim-log excerpts, the 5 `Timed` step latencies, total pane-open, deltas vs v5.0.0 baseline, tool round-trip results, and cache-snapshot count. Fill in. The decision section at the bottom triggers either Phase 6 (if regression ≤500 ms on both) or a perf-tuning sub-phase (if either >500 ms).

## Phase 5 commit

Once the smoke-results doc is complete:

```bash
git add docs/superpowers/handoffs/2026-05-15-concord-shim-smoke-results.md
git commit -m "phase 5: manual Studio Pro smoke matrix — both versions PASS

Validates the merged .mxmodule end-to-end on Studio Pro 10.24.13 + 11.10.
Pane opens, action server starts, tool round-trip succeeds, no errors in
shim.log. Pane-open latency: SP10 = <X>ms (delta +<Y>ms vs v5.0.0), SP11 =
<X>ms (delta +<Y>ms). Within the ≤500ms perf gate.

All 5 perf instrumentation points (ResolveHostFolder, BuildLoadContext,
LoadHostAssembly, InstantiateEntry, PaneShim.CreateInner) added during
Phase 3; this commit only adds the smoke-results handoff doc.

Refs: docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md
Findings: docs/superpowers/handoffs/2026-05-15-concord-shim-smoke-results.md"
```

Substitute the real perf numbers in the message. (No code changes — the Timed wrappers were already added in Phase 3, contrary to what the plan's Task 5.1 step 2 implies.)

## After Phase 5 passes

Continue with Phase 6 (docs/marketing) then Phase 7 (PR + tag + release) as written in the plan. These are pure desk work — no Studio Pro needed. The new session can run them via `superpowers:subagent-driven-development` the same way Phases 1–4 ran.

## If smoke fails

Do NOT proceed to Phase 6. The plan's Task 5.7 says: identify the slowest `Timed` step from the shim.log, optimize (likely candidates: cache the version probe at process scope; pre-warm the load context on a background thread; eliminate redundant reflection lookups), re-run smoke. Add a Phase 5b sub-phase commit. Only then go to Phase 6.

If smoke fails for non-perf reasons (e.g., pane doesn't open, shim.log shows ERROR, MCP round-trip 500s) — that's a real bug. Investigate via the `superpowers:systematic-debugging` skill before patching.

## Branch hygiene

- Working tree has untracked `*.lscache` files — tooling artifacts; leave them alone.
- `Directory.Build.props` is gitignored (per-developer); the tracked template is `Directory.Build.props.example`. The shim's csproj has fallback defaults so missing `Directory.Build.props` doesn't break the build.
- `MendixDeployTargetMerged` env var (in `Directory.Build.props`) controls whether the new unified deploy fires. If unset, the per-host `MendixDeployTarget10x`/`11x` deploys still work for component-level dev iteration.

## Useful files for this work

- Plan (source of truth for everything): [docs/superpowers/plans/2026-05-15-concord-runtime-shim-implementation.md](../plans/2026-05-15-concord-runtime-shim-implementation.md)
- Phase 0 spike findings: [docs/superpowers/handoffs/2026-05-15-concord-shim-spike-findings.md](2026-05-15-concord-shim-spike-findings.md)
- Spec: [docs/superpowers/specs/2026-05-15-concord-runtime-shim-design.md](../specs/2026-05-15-concord-runtime-shim-design.md)
- Smoke-results placeholder Ricardo fills in: [docs/superpowers/handoffs/2026-05-15-concord-shim-smoke-results.md](2026-05-15-concord-shim-smoke-results.md)
- mxmodule build click-by-click: [memory/reference_concord_mxmodule_build.md](file://C:/Users/rc1yok/.claude/projects/c--Extensions-Terminal/memory/reference_concord_mxmodule_build.md)
