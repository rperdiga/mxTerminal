# Handoff: alpha.3 backlog prep on feat/v5.0.0-w2-mcpx-merge (Tasks 31, 30, 32) — 2026-05-13

> **For the next session:** Framing B from the prior session's handoff (Tasks 31 → 30 → 32) is complete. The alpha.2 tag is still pending Neo's runtime smoke matrix on Studio Pro 11.10 and 10.24.13. The two new commits are alpha.3 *prep*: cosmetic warning cleanup and a duplicate-type consolidation. They sit on top of `f1858c0` (the alpha.2 version bump). **The branch is NOT yet pushed past `96cb54d`** — Neo decides whether to push the alpha.3 prep ahead of or after the alpha.2 tag.

---

## Quick orientation

- **Branch:** `feat/v5.0.0-w2-mcpx-merge`
- **HEAD:** `96737ce` (2 commits ahead of `origin/feat/v5.0.0-w2-mcpx-merge`)
- **Tests:** **272 passing** (242 Terminal.Tests + 27 Concord.Core.Tests + 3 skipped Maia-live), 0 failed. Same as alpha.2 baseline.
- **Build:** **0 errors, 14 warnings** (down from 20 after Task 31; the 6 cleared were all the CS0414 false-suppressed fields).
- **Version (csproj):** Still `5.0.0-alpha.2` — neither task touched the version. The alpha.3 commits are *prep* sitting on the alpha.2 branch.

---

## What landed this session

| Commit | Task | What |
|---|---|---|
| `4dd3611` | 31 | `chore(hosts): fix CS0649 pragma codes to CS0414 (W2 Task 31)` — The `_entry` fields are *assigned* (by MEF) and never *read*, which is the precise semantic of CS0414 ("field is assigned but its value is never used"). CS0649 covers fields that are never *assigned at all* (defaulting to null) — wrong code. The 6 `#pragma warning disable CS0649` / `restore CS0649` pairs in Host10x + Host11x (MenuExtensions, Pane, Ui per host) flipped to `CS0414`. Net: 6 files, 12 lines, 12 insertions + 12 deletions. Warning count dropped 20 → 14. |
| n/a    | 30 | Audit only — **no code changes.** `Func<RunConfigurationSnapshot>`, `Func<(string? path, string? name)>`: 0 hits in `src/Concord.Core/`. `Mendix.StudioPro.ExtensionsAPI`: 4 hits, all in doc comments of Interop interfaces (correct — those interfaces are the version-agnostic wrapper). `StudioProActions` reads `HostServices.RunStateProbe` / `.UiAutomation`; `StudioProActionServer` ctor is `(int port, Logger? log)`; `TerminalSessionManager.StartActionServer` is `(int port, Logger?, CdpClient?)`. Task 29's HostServices migration was thorough. The dormant 11-arg `HostServices.Register` overload is **deliberately retained** for the next cycle (Phase 7 trap explicitly preserves it); not in scope for Task 30. |
| `96737ce` | 32 | `refactor(core): collapse RunConfigurationSnapshot into RunConfigurationInfo (W2 Task 32)` — Two records with identical wire shape (same property names: `Id`, `Name`, `ApplicationRootUrl`) but different nullability annotations and different namespaces. Both call sites in `StudioProActions` constructed Snapshot *from* a non-null RunConfigurationInfo, so Snapshot's `string? Id, string? Name` was laziness, not load-bearing. The outer nullable on `AppStatusInfo.ActiveRunConfiguration` IS load-bearing (null = no active config) — preserved as `RunConfigurationInfo?`. Delete the Snapshot record; switch the field type; simplify the two construction sites to direct assignment. Net: 2 files, 6 insertions + 9 deletions. |

Cumulative session diff vs `96cb54d`: 8 files, +18 / −21.

---

## Critical patterns this session reinforced

### Edit tool > PowerShell for cross-encoding text changes (line-ending + BOM trap)

First Task 31 attempt used PowerShell 5.1's `(Get-Content -Raw)` + `Set-Content -Encoding utf8 -NoNewline`. Two failures:

1. **`Get-Content` defaulted to ANSI codepage**, so files containing em-dashes (UTF-8 `E2 80 94`) read as 3 ANSI characters (`â € "`). Set-Content's UTF-8 write then double-encoded them into mojibake (`â€"`).
2. **`Set-Content -Encoding utf8`** wrote a UTF-8 BOM (`EF BB BF`) at the head of each file. None of the source files originally had a BOM.

Reverted (`git checkout --`), then redid with the Edit tool's `replace_all: true` across all 6 files in parallel. Edit preserved CRLF endings and UTF-8 (no BOM) — clean 12-line diff.

**Rule:** for substring-replace on existing source files with non-ASCII characters or specific line-ending conventions, use the Edit tool's `replace_all`, not raw PowerShell `Get-Content`/`Set-Content`. The PowerShell one-liner in the plan template *only* works if the corpus is pure ASCII and the source line endings match what Set-Content emits (Set-Content on PS 5.1 emits CRLF by default, but `-NoNewline` strips trailing newlines and `-Encoding utf8` adds BOM — both surprises).

### Edit tool's CRLF preservation has a corner case (file rewrites)

The Task 32 edit of `ActionResult.cs` replaced 28 of 29 lines (the whole file minus the closing brace), and the rewrite came out LF-encoded — the tool did NOT preserve the file's prior CRLF endings on a near-full-file edit. Small in-place edits (Task 31's per-pragma replacements, StudioProActions's two function-body edits in Task 32) preserved CRLF correctly.

**Fix:** after a near-full-file Edit, check with `git diff --stat` for a CRLF warning. If present, normalize with:

```powershell
$p = '<absolute path>'; $content = [System.IO.File]::ReadAllText($p); $content = $content -replace "(?<!`r)`n", "`r`n"; [System.IO.File]::WriteAllText($p, $content, [System.Text.UTF8Encoding]::new($false))
```

The `[System.Text.UTF8Encoding]::new($false)` arg suppresses BOM. The negative-lookbehind regex only adds `\r` before bare `\n` (won't double-`\r` an existing CRLF).

### Task 30-style audits sometimes have no commit

The plan template said "Audit + remove dead pre-W1 injection paths." After Task 29 was thorough, the audit had nothing to remove. The deliverable is the audit conclusion itself, not a commit. Don't fabricate a commit for the sake of having one — the next session reads the handoff, not the git log, for "is Task 30 done."

---

## What's still open for Neo (unchanged from the prior handoff)

1. **Studio Pro 11.10.x runtime smoke** — `docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md` "W2 smoke results" section, expected `tools/list` count ~37 (curated allowlist).
2. **Studio Pro 10.24.13 runtime smoke** — same section, expected count ~87 (full SPMCP). First runtime test of the Host10x UI port.
3. **`git tag -a v5.0.0-alpha.2`** — AFTER both smokes pass. Open question: should the tag include the alpha.3 prep commits (`4dd3611`, `96737ce`)? They don't bump the version and don't change observable behavior, but they're refactors not in alpha.2's intended scope. Two reasonable options:
   - **Tag at `f1858c0`** (the version bump) — clean "alpha.2 = the W2 merge work, period" boundary. The alpha.3 prep is staged on the branch for the next cycle.
   - **Tag at `96737ce`** (HEAD) — includes the alpha.3 prep refactors in alpha.2 because they're improvements, not new features. Bumps the alpha.3 surface slightly.

   Suggested: tag at `f1858c0` for clarity. The alpha.3 prep stays on the branch for the alpha.3 cycle to absorb.

4. **`git push origin v5.0.0-alpha.2`** (after tag) — separate decision.
5. **`git push origin feat/v5.0.0-w2-mcpx-merge`** — currently 2 commits ahead of origin. If pushing, choose timing relative to the tag: push BEFORE tagging so origin reflects the alpha.3 prep; push AFTER tagging to land both in one go.
6. **PR + merge to `main`** — after tag is pushed.

---

## Remaining v5.0.0-alpha.3 backlog

Only the per-tool descriptions task remains from the W2 deferred list:

- **Per-tool descriptions + input schemas on `ITool`** — Add nullable `Description` and `InputSchema` properties to `ITool`. SPMCP bootstrap leaves them null (catalog keeps today's generic placeholder fallback); UI/Maia bootstraps populate the rich strings the deleted `HandleToolsList` block in Task 21 used to emit. Restores the alpha.1 description quality for MCP clients that surface descriptions to users. Sized ~2-4h: define properties, update both bootstraps (UiActionsBootstrap and MaiaToolsBootstrap have ~16 tools to annotate), update tools/list rendering, add tests.

Phase 7 trap items still open (NOT urgent, NOT in scope for alpha.3):

- 11-arg `HostServices.Register` overload is defined but not called. The 7 extended Interop services (Model, DomainModel, PageGeneration, Navigation, VersionControl, UntypedModel, MicroflowAuthoring) throw `NotInitialized` at runtime. A future cycle wires them through Host\*Entry.

---

## Things NOT to do (carry-over + additions)

- **Don't tag before 10.24.13 smoke.** Unchanged from prior handoff.
- **Don't push the alpha.3 prep commits without intent.** The branch is currently 2 commits ahead of origin. If you push, the alpha.3 prep becomes visible to anyone watching the branch *before* the alpha.2 tag exists. Not harmful, but conveys "alpha.2 + extras" rather than a clean alpha.2 boundary.
- **Don't roll the alpha.3 prep into the alpha.2 CHANGELOG.** The CHANGELOG entry at `f3c3f79` is alpha.2-scoped. If the alpha.3 prep stays on the branch through tagging, add a "since alpha.2" or alpha.3 section when alpha.3 ships.
- **Don't use raw PowerShell `Get-Content -Raw` + `Set-Content -NoNewline -Encoding utf8` to bulk-edit .cs files.** The Edit tool's `replace_all` is the safe path — see "Critical patterns" above.

---

## TL;DR for the new session

1. **The alpha.3 backlog prep is queued on the branch.** Two commits: pragma code cleanup (`4dd3611`), Snapshot/Info consolidation (`96737ce`). Build clean, 272 tests passing.
2. **The alpha.2 tag is still gated on Neo's smoke matrix.** Same gate as 6 hours ago.
3. **Only the per-tool descriptions task remains** from the W2 deferred list.
4. **Tag-point decision:** prefer `f1858c0` over HEAD so alpha.2 = "the W2 merge work" without the alpha.3 prep refactors.
5. **No protocol changed**, no API surface widened, no migration needed — the alpha.3 prep is purely internal.
