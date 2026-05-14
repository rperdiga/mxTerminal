# Handoff: ultrareview fixes ready to replay — 2026-05-14

> **For the next session:** Four ultrareview findings on `feat/v5.0.0-w2-mcpx-merge` have been investigated, fixed, and verified (build clean, 324 tests passing). The fixes were then reverted from the working tree so Joe's pending W2 WIP could commit cleanly first. The fixes are saved in a `/tmp` backup directory; this handoff is the replay recipe.

---

## Quick orientation

- **Branch:** `feat/v5.0.0-w2-mcpx-merge`
- **HEAD at session start:** `74d73e1` — `test(spmcp-sweep): Phase 4 fresh-state validation; tighten 7 idempotency eithers`
- **Working tree at handoff time:** **pre-session WIP-only state** — the same uncommitted W2 work Joe started the session with. My ultrareview edits have been removed from the tree.
- **Expected next commit by Joe:** the W2 WIP, whatever shape Joe lands it in. After that commit, this replay runs cleanly on top.

---

## What ultrareview found and what I fixed (verified-working at peak)

| # | Sev | Bug | Fix |
|---|---|---|---|
| **001** | normal | `get_app_status` / `get_active_run_configuration` throw on Host11x — `StudioProAppHost11x` and `RunConfigurationsHost11x` were `NotImplementedException` stubs registered as live; reachable on the 11.x allowlist. | (a) Implement both classes against `IModel.Root` (an `IProject`) and `ILocalRunConfigurationsService.GetActiveConfiguration(model)` + `IConfigurationSettings.GetConfigurations()`. (b) Add `SetApp` / `SetRunConfigurations` setters to `HostServices` mirroring the existing `Set*` pattern. (c) Wire from `TryAutoStartActionServer` in both panes. (d) `Host{10,11}xEntry` registers placeholders with `() => null` model closure and `null` service at MEF activation; pane swaps real instances in before the action server starts dispatching. |
| **003** | normal | `update_attribute` corrupts/crashes when only non-type fields are updated — the host always rebuilt `IAttributeType` from the spec, silently resetting `IStringAttributeType.Length` / `IDateTimeAttributeType.LocalizeDate`, and throwing on Enumeration attributes. Reachable on Host10x (no 11.x allowlist filter). | In `DomainModelHost{10,11}x.UpdateAttribute`, only rebuild the type when the caller actually requested a kind change: `typeChangeRequested = newSpec.Kind != currentKind || !string.IsNullOrEmpty(newSpec.EnumerationQualifiedName) || (newSpec.EnumerationValues?.Count > 0)`. `currentKind` comes from the existing `MapAttributeKind(IAttributeType?)` helper. Honors the documented contract on `IDomainModelHost.UpdateAttribute` ("applies only the non-null fields of newSpec"). |
| **004** | normal | "Studio Pro MCP is off" advisory fires on versions without the feature — `BuildAdvisoryNotices` called `ReadMcpServer` without the `IsMcpServerSupported(version)` gate that the rest of the file uses (`ProbeStudioProMcpAvailable` already had it; the advisory path didn't). On 10.x and 11.6-11.9, the `EnableMcpServer` key is absent, so `info.Enabled == null != true` fires the notice — pointing users at a Maia preferences pane that doesn't exist on their version. | Two-line gate change on both hosts: `if (!string.IsNullOrEmpty(version) && StudioProThemeProbe.IsMcpServerSupported(version)) { ... }`. |
| **006** | nit | `HandleSaveSettings` missing `maiaSupported` version gate — the parallel auto-start path applies `IsMaiaSupported(version)` (Host11x:331-332); the save-path computes `maiaEnabled` from Windows + setting only. UI clamp covers the normal flow, so this is a defense-in-depth fix for non-bundled MCP clients posting a payload that omits `MaiaIntegrationEnabled`. | Mirror the auto-start gate in both `TerminalPaneViewModel.HandleSaveSettings`: `bool maiaSupported = StudioProThemeProbe.IsMaiaSupported(StudioProThemeProbe.StudioProVersionFromExePath()); bool maiaEnabled = OperatingSystem.IsWindows() && newMaiaIntegration && maiaSupported;`. |

**Verification at peak (before revert):** `dotnet build` clean; `dotnet test` — **324 passing** (50 Concord.Core.Tests + 274 Terminal.Tests, 3 skipped Maia-live), 0 failed.

---

## Backup location

All 13 modified files saved at:

```
/tmp/concord-ultrareview-backup-1778770557/
```

Path also stored as a single line in `/tmp/concord-ultrareview-backup-latest` so a future session can read it without guessing the timestamp:

```bash
cat /tmp/concord-ultrareview-backup-latest
```

Directory mirrors the repo layout (`src/...`, so each backup file's relative path matches its destination):

```
src/Concord.Core/Interop/HostServices.cs
src/Concord.Host10x/Host10xEntry.cs
src/Concord.Host10x/Interop/DomainModelHost10x.cs
src/Concord.Host10x/Interop/RunConfigurationsHost10x.cs
src/Concord.Host10x/Interop/StudioProAppHost10x.cs
src/Concord.Host10x/Pane/TerminalPaneExtension.cs
src/Concord.Host10x/Pane/TerminalPaneViewModel.cs
src/Concord.Host11x/Host11xEntry.cs
src/Concord.Host11x/Interop/DomainModelHost11x.cs
src/Concord.Host11x/Interop/RunConfigurationsHost11x.cs
src/Concord.Host11x/Interop/StudioProAppHost11x.cs
src/Concord.Host11x/Pane/TerminalPaneExtension.cs
src/Concord.Host11x/Pane/TerminalPaneViewModel.cs
```

---

## Replay sequence

The next session's job is mechanical:

1. **Confirm Joe's WIP commit has landed.** `git log -5` should show a new commit on top of `74d73e1`. If the WIP is not yet committed, stop and surface that to Joe — the replay assumes WIP is in HEAD.
2. **Confirm working tree is clean** (no uncommitted modifications other than handoff doc).
3. **Read the backup path:**
   ```bash
   backup=$(cat /tmp/concord-ultrareview-backup-latest)
   ```
4. **Copy all 13 backup files over the working tree.** From the repo root:
   ```bash
   for f in $(cd "$backup" && find . -type f); do
       mkdir -p "$(dirname "$f")"
       cp "$backup/$f" "$f"
   done
   ```
5. **Sanity-check the diff before building.** `git diff --stat` should show ~13 files changed. The hunks should match the fix table above (advisory gate, maia gate, type-rebuild guard, App/RunConfigurations setters + host impls + pane wiring).
6. **Build:** `dotnet build src/Concord.Host11x/Concord.Host11x.csproj -c Debug --nologo` and same for `Host10x`. Both should succeed with 0 errors (only the pre-existing nullable warnings).
7. **Test:** `dotnet test --nologo --verbosity minimal`. Expected baseline: 324 passing, 3 skipped (Maia-live), 0 failed — same as when peak was verified. If the WIP commit changed the test count, the new baseline replaces 324 — just verify nothing regressed because of the ultrareview fixes.
8. **Commit as a single atomic `fix(ultrareview): …` commit.** Suggested message body:
   ```
   fix(ultrareview): address 4 findings from ultrareview run on feat/v5.0.0-w2-mcpx-merge

   - bug_001: implement StudioProAppHost11x/10x and RunConfigurationsHost11x/10x
     against IModel.Root / ILocalRunConfigurationsService; add SetApp/SetRunConfigurations
     setters on HostServices; pane wires real instances in TryAutoStartActionServer.
     get_app_status and get_active_run_configuration now function on Host11x.
   - bug_003: DomainModelHost{10,11}x.UpdateAttribute only rebuilds IAttributeType
     when the caller requested a kind change. Documentation-only / max_length-only /
     localize_date-only updates no longer reset String.Length / DateTime.LocalizeDate
     to platform defaults; Enumeration attribute updates no longer throw on the
     EnumerationQualifiedName-required guard.
   - bug_004: BuildAdvisoryNotices gates the SP-MCP probe with IsMcpServerSupported.
     The "enable in Maia → MCP Server" notice no longer fires on 10.x or 11.6-11.9
     where the menu path doesn't exist.
   - bug_006: HandleSaveSettings mirrors the IsMaiaSupported gate that
     TryAutoStartActionServer already applies. Dormant in the normal UI flow
     (settings-modal already clamps); defense-in-depth for non-bundled clients.

   Tests: 324 passing (no change from baseline), 0 failed, 3 skipped (Maia-live).
   Build: clean.
   ```
9. **Delete the backup once the commit lands successfully:**
   ```bash
   rm -rf "$(cat /tmp/concord-ultrareview-backup-latest)"
   rm /tmp/concord-ultrareview-backup-latest
   ```

---

## Things NOT to do during the replay

- **Don't re-derive the fixes from the ultrareview findings.** The backup files are the verified-working versions. Trying to re-edit by hand from this handoff alone re-introduces drift and re-burns build/test time. The mechanical copy IS the replay.
- **Don't rebase or reset.** The fixes were verified against `74d73e1` + WIP. If Joe's WIP commit is on top of `74d73e1`, the copy applies cleanly. If anything has been rebased / squashed / amended in a way that changes the WIP content, stop and re-verify line-by-line rather than blindly overwriting.
- **Don't commit if any test fails.** Even if the failure looks unrelated. The ultrareview fixes were green at peak; any new red is a signal that either (a) Joe's WIP commit changed something my fixes depended on, or (b) the copy missed something. Investigate before committing.
- **Don't delete the backup until the commit is in `git log`.** Belt-and-suspenders.

---

## Why this odd sequence

The /ultrareview session opened on a working tree that already had substantial uncommitted W2 work (~17 files of WIP, including new `HostServices.Set*` setters that bug_001's wiring naturally extends). The four ultrareview fixes layered on top of that WIP in 7 of those files. Joe chose "commit WIP first, then ultrareview fixes on top" to keep the W2 boundary clean in history — which required me to step out of the way so Joe's commit could be just-WIP.

The mechanical revert + /tmp backup + replay is the cost of that clean boundary. It's safe because the revert was scoped (only my Edits, file-by-file), tests still pass on the reverted tree, and the backup is byte-for-byte the verified-working version.

---

## TL;DR for the new session

1. Joe has (by the time you read this) committed the W2 WIP. Confirm with `git log -5`.
2. Read `/tmp/concord-ultrareview-backup-latest` to get the backup dir, copy all 13 files over the working tree.
3. Build + test. Expect ~324 passing (or whatever the new baseline is after Joe's WIP). No regressions.
4. Commit as a single `fix(ultrareview): ...` commit using the message body above.
5. Clean up the backup.

Done in five steps. Ten minutes if everything aligns; longer only if the WIP commit shape diverges and the copy needs reconciliation.
