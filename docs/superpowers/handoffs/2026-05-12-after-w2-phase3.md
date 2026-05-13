# Handoff: after W2 Phase 3 (Concord 5.x, host Interop implementations landed) — 2026-05-12

> **For the next session:** Phase 3 of Plan W2 ([`docs/superpowers/plans/2026-05-12-concord-w2-mcpx-merge.md`](../plans/2026-05-12-concord-w2-mcpx-merge.md)) executed via subagent-driven development. This doc orients the next session for Phase 4. The Phase 2 handoff at [`2026-05-12-after-w2-phase2.md`](2026-05-12-after-w2-phase2.md) remains valid for everything before Phase 3.

---

## Quick orientation

- **Branch:** `feat/v5.0.0-w2-mcpx-merge` (pushed to origin)
- **HEAD:** `3dc2668` ("feat(host): implement remaining 5 interop hosts on both targets (W2)")
- **Tests:** 249 passing (241 Terminal.Tests + 8 Concord.Core.Tests), 3 skipped, 0 failed
- **Build:** clean modulo pre-existing CS0414/CS8604 warnings inherited from W1
- **Working tree:** clean

**What Phase 3 produced:**

| Task | Commit | Files | Result |
|---|---|---|---|
| Task 9 | `ff754d2` | `ModelHost11x.cs` + `ModelHost10x.cs` (384/387 lines) | 16-method `IModelHost` implemented on both hosts |
| Task 10 | `6653453` | `DomainModelHost11x.cs` + `DomainModelHost10x.cs` (1528 lines each) | 34-method `IDomainModelHost` implemented |
| Task 10b (hotfix) | `8e08a23` | `DomainModelHost10x.cs` encoding fix | Normalized UTF-8 LF, no BOM (had shipped with BOM+CRLF+mojibake) |
| Task 11 | `3dc2668` | `PageGenerationHost{10x,11x}`, `NavigationHost{10x,11x}`, `VersionControlHost{10x,11x}`, `UntypedModelHost{10x,11x}`, `MicroflowAuthoringHost{10x,11x}` (10 files, 2944 lines total) + spike-notes update | 5 remaining Interop interfaces implemented on both hosts |

**What's NOT done:** Phase 4 (refactor SPMCP tools to depend on Core Interop) — Tasks 12-17. Then Phases 5-8. **Phase 4 is the largest single chunk of W2** — see "Recommended dispatch order" in the Phase 2 handoff.

---

## Critical patterns established in Phase 3 (Phase 4 should preserve)

1. **`IAbstractUnit.Id` is `string`, not `Guid`.** The Phase 2 Core records (`ModuleId`, `DocumentId`, `FolderId`, `EntityRef`, `AttributeRef`, etc.) use `Guid Value`. Phase 3 introduced a `ParseId(string id)` helper (Guid.TryParse with MD5 deterministic fallback) in each host file. For domain-model items (`IEntity`, `IAttribute`, `IAssociation`) which don't expose `.Id` at all, `GuidFromString(qualifiedName)` derives a deterministic Guid from the qualified name. This is a workaround — a clean fix would change the Core records to `string Id`, but that's deferred (touches every interface).

2. **`_model.StartTransaction("...")` + explicit `tx.Commit()`** for every mutation. Disposal alone does NOT commit. There is no `IModel.Save()` API — persistence is per-transaction. `IModelHost.SaveAsync` returns `Task.CompletedTask` for interface symmetry; the comment explains.

3. **10.x and 11.x ExtensionsAPI are 99% compatible.** Every 10x host file in Phase 3 is byte-identical to its 11x sibling except: namespace (`Concord.Host10x.Interop` vs `Concord.Host11x.Interop`), classname, ctor name, and 1-4 version-number comments. **No genuine drift was found across all 14 host method bodies.** The plan's mentions of `backport-10x/reference/` are misleading — that directory does not exist on this branch. The SPMCP source itself (still in `src/Concord.Core/Spmcp/`, compile-excluded) is the body source for both versions.

4. **Encoding discipline (load-bearing).** Task 10's first commit shipped `DomainModelHost10x.cs` with UTF-8 BOM + CRLF and mojibake'd box-drawing characters because the implementer used PowerShell `Set-Content` (defaults to UTF-16 BOM on Windows). Hotfix landed in `8e08a23`. **For Phase 4 — when creating 10x mirror files from 11x originals — use `sed` via the Bash tool**, not PowerShell:

   ```bash
   sed -e 's/Concord\.Host11x/Concord.Host10x/g' \
       -e 's/SomeClass11x/SomeClass10x/g' \
       -e 's/11\.6\.2/10.21.1/g' \
       src/Concord.Host11x/Interop/SomeClass11x.cs \
     > src/Concord.Host10x/Interop/SomeClass10x.cs
   ```

   Verify with `file <path>`: must say `ASCII text` or `Unicode text, UTF-8 text` (never "UTF-8 (with BOM)" or "CRLF").

---

## Phase 3 findings that bear on Phase 4

Captured in [`docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md`](2026-05-12-concord-w2-spike-notes.md) under "## Phase 3 API-drift findings". Highlights for Phase 4 tool-author awareness:

### Service namespace resolutions

- **`INavigationManagerService` is in `Mendix.StudioPro.ExtensionsAPI.Services`** on both versions. The spike note flagging `UI.Services` ambiguity was a false alarm — only `Services` appears in the type index (`T:` entries); `UI.Services` mentions were in method-level docs only.
- **`IVersionControlService` is in `Mendix.StudioPro.ExtensionsAPI.UI.Services`** on both versions (NOT in `Services`). Both hosts inject it from `UI.Services`. The Phase 4 SPMCP refactor of `MendixAdditionalTools.ReadVersionControl` needs to update its using statements to match.
- **`IUntypedModelAccessService`** and **`IPageGenerationService`** both live in `Services` on both versions.

### Capability gaps in the typed ExtensionsAPI surface

The following operations are NOT exposed in the typed ExtensionsAPI; the Phase 4 tool wrappers must treat them as `escalation: manual` or accept the documented no-op/null-return behavior:

- `IMicroflow.Documentation` — not in the typed API. `MicroflowSummary.Documentation` is always null. (Documentation IS accessible via the untyped model — `IUntypedModelHost` route.)
- `IActionActivity.Delete()` — not exposed. `MicroflowAuthoringHost.DeleteActivity()` throws `NotSupportedException`.
- `IMicroflowCallAction.Microflow` — the property doesn't exist. The correct path is `IMicroflowCallAction.MicroflowCall.Microflow`. Phase 4 tools that call into the microflow host must use the host method, not work around the host.
- `INavigationManagerService` read-back (`ListProfiles`, `ReadProfile`, `RemoveItem`, `SetItemIcon`) — only `PopulateWebNavigationWith` is exposed. The host returns empty/null/no-op for the read operations. `SetItemTarget` appends via `PopulateWebNavigationWith` (does not replace). Phase 4 tools must surface this as a partial-functionality escalation.
- `IMicroflow.SetAccessLevel` / `AllowedModuleRoles` — not exposed. `MicroflowAuthoringHost.SetAccessLevel()` is a no-op. Security lives in `IModuleSecurity` in the untyped model — Phase 4's `IUntypedModelHost` is the route if Phase 4 needs to surface this.

### Project-level setting gaps from Task 9

These were captured in `ff754d2`'s commit message:

- `IProject` does NOT expose `ServerVersion` or `AppId` (neither version). `ProjectInfo.MendixVersion` and `ProjectInfo.AppId` always return null.
- `IConfiguration.IsActive`, `DatabaseType`, `DatabaseConnectionString` — not exposed on the typed API. `ConfigurationSetting` returns those fields as `false`/`null`. The active-configuration concept lives in `ILocalRunConfigurationsService` (UI-tier), which Core does not surface yet.
- `IModelHost.SetActiveConfiguration` returns `false` (not supported in Core). Phase 4 may want to route this through the UI service via `HostServices` if the upstream tool depended on it (or surface as `escalation: manual`).

---

## Things NOT to do (additions for Phase 4)

- **Don't use PowerShell `Set-Content` to create 10x mirror files.** Use `sed` via Bash. The encoding hotfix in Phase 3 was avoidable.
- **Don't change the Core records (`Guid Value` → `string Id`) as a side effect of Phase 4.** That's a meaningful Phase 2 retroactive change and should be its own commit (or a new task) if you decide it's worth doing. Phase 3's `ParseId` workaround is fine for now.
- **Don't rediscover the `IVersionControlService` namespace question.** The answer is `UI.Services`. Update the spike notes if you find a more authoritative source.
- **Don't expect Phase 4 to be one-and-done per slice.** The handoff prediction was ~25 slice dispatches across Tasks 13-15. That estimate stands.

---

## Recommended dispatch order for the new session

The Phase 2 handoff's recommendation still stands:

1. **Phase 4** is the next phase. Strongly consider a dedicated session — controller context budget got tight by the end of Phase 3 (3 subagent dispatches totaling ~500K tokens; Phase 4's 25 slice dispatches will be heavier).

2. **Start Phase 4 with Task 12** — namespace rename of SPMCP from `MCPExtension.*` to `Terminal.Spmcp.*`, add `Microsoft.Extensions.Logging.Abstractions` package, remove `Compile Remove="Spmcp/**/*.cs"` from `Concord.Core.csproj`. **The build will break at the end of Task 12 by design** (CS0246 from `Mendix.*` usings that Core can't see). Tasks 13-16 resolve them slice by slice.

3. **Within Task 13/14/15, dispatch one subagent per slice.** Each slice = one tool method or small related group + a verification build. The plan body lists the slices explicitly.

4. **Verification cadence:** after each slice subagent, run `dotnet build src/Concord.Core/...` + `git log --oneline -1`. The controller does NOT need to dispatch a separate spec-reviewer for mechanical slice work — the build is the spec. For Task 16 (BaseApiHandler refactor decision) and Task 17 (fake-host smoke tests), a fresh reviewer subagent is worth the cost.

5. **Resolve the open question on `BaseApiHandler(IModel)` in Task 16** (Option A: rework base to read `HostServices.Model`; Option B: remove base, each handler reads directly). Phase 2's default recommendation was Option A.

6. **Resolve the constants/enumerations CRUD interface gap** that Phase 2 flagged. Default: extend `IModelHost` (Phase 4 will surface the gap when refactoring `CreateConstant`/`ListConstants` and can extend in a side-commit before continuing).

---

## Phase 4 success criterion (from plan)

`dotnet list src/Concord.Core/Concord.Core.csproj package` shows **zero `Mendix.StudioPro.ExtensionsAPI` references**. Core builds clean against the refactored SPMCP code. All ~80 SPMCP tool entries instantiate using `HostServices` accessors instead of constructor `IModel` / `IPageGenerationService` / etc.

---

## Quick commit reference (W2 commits in Phase 3)

| Commit | What |
|---|---|
| `ff754d2` | `IModelHost` on both hosts (Task 9) |
| `6653453` | `IDomainModelHost` on both hosts (Task 10) |
| `8e08a23` | Encoding hotfix for `DomainModelHost10x.cs` |
| `3dc2668` | 5 remaining hosts + Phase 3 API-drift spike notes (Task 11) — HEAD |

---

## TL;DR for the new session

1. **Read this handoff + the Phase 2 handoff at [`2026-05-12-after-w2-phase2.md`](2026-05-12-after-w2-phase2.md)** — the latter still describes Phases 4-8 in detail; this one only updates the "what just shipped" frame.
2. **Skim Phase 3 API-drift findings** in [`2026-05-12-concord-w2-spike-notes.md`](2026-05-12-concord-w2-spike-notes.md) — substantive constraints for Phase 4 tool wrappers.
3. **Resume on branch `feat/v5.0.0-w2-mcpx-merge` at `3dc2668`** — clean, pushed, 249 tests green.
4. **Suggested first action:** dispatch Task 12 (namespace rename + package refs + remove `Compile Remove`).
