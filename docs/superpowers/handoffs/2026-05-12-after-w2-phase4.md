# Handoff: after W2 Phase 4 (Concord 5.x, SPMCP fully Core-Interop) — 2026-05-12

> **For the next session:** Phase 4 of Plan W2 ([`docs/superpowers/plans/2026-05-12-concord-w2-mcpx-merge.md`](../plans/2026-05-12-concord-w2-mcpx-merge.md)) is complete. Concord.Core builds with **zero `Mendix.StudioPro.ExtensionsAPI`** references. The Phase 2/3 handoffs at [`2026-05-12-after-w2-phase2.md`](2026-05-12-after-w2-phase2.md) and [`2026-05-12-after-w2-phase3.md`](2026-05-12-after-w2-phase3.md) remain valid for everything before Phase 4.

---

## Quick orientation

- **Branch:** `feat/v5.0.0-w2-mcpx-merge` (pushed to origin)
- **HEAD:** `d658d00` ("test(spmcp): smoke tests for refactored tool classes (W2)")
- **Tests:** 253 passing (241 Terminal.Tests + 12 Concord.Core.Tests), 3 skipped, 0 failed
- **Build:** **CLEAN** — `dotnet build src/Concord.Core/Concord.Core.csproj` succeeds with 0 errors (12 nullable warnings, all pre-existing)
- **Working tree:** clean

**Phase 4 success criterion confirmed:**

```
$ dotnet list src/Concord.Core/Concord.Core.csproj package
  > Eto.Forms                                            2.9.*
  > Microsoft.AspNetCore.Http.Abstractions               2.2.*
  > Microsoft.Data.Sqlite                                8.0.*
  > Microsoft.Extensions.DependencyInjection.Abstractions 8.0.*
  > Microsoft.Extensions.Logging.Abstractions            8.0.*
  > System.Text.Json                                     8.0.*
```

**No `Mendix.StudioPro.ExtensionsAPI` package.** SPMCP tools and handlers dispatch entirely through `Terminal.Interop.HostServices`.

---

## Phase 4 commit chain (16 commits)

| Commit | What |
|---|---|
| `15f0873` | Task 12 — namespace rename `MCPExtension.*` → `Terminal.Spmcp.*`, package refs added (Logging.Abstractions, DependencyInjection.Abstractions), `Compile Remove` lifted. Build intentionally broken (294 CS0246 errors). |
| `358e7bc` | Task 13 slices 1-2 — ctor + `SaveData` + `ReadSampleData` via HostServices. |
| `91c586a` | Task 13 slice 3 — `GenerateSampleData` + helpers; auto-setup block deferred. |
| `9f52dbb` | Task 13 slice 4 — microflow/nanoflow read paths. |
| `812206e` | Task 13 slices 5-6 — diagnostics + `ListAvailableTools`. |
| `e0c9aaf` | Task 14 slices 7-8 — page-gen + microflow-create. |
| `0649cad` | Task 14 slices 9-10 — microflow-activity + `SetupDataImport` (re-enabled auto-setup). |
| `ce0ea56` | Task 14 slices 11-12 — settings/config + VC + java actions. |
| `be0daa3` | Task 14 slices 13-14 — URL + security (5 security methods → `escalation:manual`). |
| `6e0c3c6` | Task 14 misc — `ManageNavigation`, `CheckVariableName`, `ModifyMicroflowActivity`, `InsertBeforeActivity`, `UpdateMicroflow`. |
| `cbc19fd` | Task 14 cleanup — **deleted 2,118 lines** of dead `Create*Activity` helpers + `CreateActivityByType` dispatcher + `GetOrderedMicroflowActivities`. |
| `6ae8b06` | Task 14 final reads — `ListScheduledEvents`, `ListRestServices`, `QueryModelElements`, `ListPages`, `ReadPageDetails`, `ListWorkflows`, `ReadWorkflowDetails` via `HostServices.UntypedModel`. |
| `a15c671` | Task 14 final writes — `SyncFilesystem`, `ReadAttributeDetails`, `ConfigureConstantValues`, `AnalyzeProjectPatterns`; dead helpers `GetProjectSettings`, `GetSettingsPart<T>`, `NormalizeReduceExpression` deleted. **MendixAdditionalTools.cs Mendix-free.** |
| `9fa3091` | Task 15 batch 1 — `MendixDomainModelTools` ctor + 8 read paths. |
| `cfb4e31` | Task 15 batch 2 — 8 create paths. |
| `dac6ecb` | Task 15 batch 3 — 13 update paths (`UpdateConstant` → escalation:manual; `UpdateEnumeration`, `UpdateAssociation`, `ConfigureSystemAttributes` had richer surface than plan anticipated and got real implementations). |
| `d94d35e` | Task 15 final — 6 renames + delete + arrange + 5 diagnostics + **28 dead helpers deleted**, 1,821 lines net. **MendixDomainModelTools.cs Mendix-free.** |
| `24e2ee6` | Task 16 base infra — `Microsoft.AspNetCore.Http.Abstractions` package added; `IApiHandler` interface + `BaseApiHandler` abstract class recreated under `Terminal.Spmcp.Core` (no `IModel` ctor param; reads `HostServices.Model` on demand). |
| `17088e7` | Task 16 handlers — all 9 handlers (`AssociationDiagnostic`, `Debug`, `DeleteModel`, `GenerateOverview`, `ListMicroflows`, `ReadMicroflowActivities`, `ReadModel`, `SaveData`, `WriteModel`) refactored. **All Handlers/ Mendix-free.** |
| `363c8f1` | Utils.cs cleanup — `Utils` shrunk from 191 to 21 lines (only pure `GetParam` survived). |
| `d658d00` | Task 17 smoke tests — 4 new tests in `tests/Concord.Core.Tests/SpmcpSmokeTests.cs`; FakeModelHost + FakeDomainModelHost updated to return canned data. |

---

## Critical patterns established in Phase 4 (preserve them)

1. **Build IS the spec for mechanical work.** The skipped spec-reviewer subagent pattern worked — each dispatch verified by `dotnet build` + targeted error grep on the file/method being refactored. No regressions caught post-merge.

2. **Aggressive dead-helper deletion.** When a refactored public method stops calling a private helper, the helper is dead — verify with `grep` and delete in the SAME slice. Saved ~5,000 lines across `MendixAdditionalTools.cs` + `MendixDomainModelTools.cs`. Don't leave private dead code sitting around — it accumulates CS errors that mask real issues.

3. **MSBuild error cascading hides real errors.** When a method's body has a CS0246 on a using-statement type, the compiler may not separately surface every `_model.X` line inside that method. **Don't trust the error count to mean "all errors found"** — also grep the file for remaining `Mendix.*` / `_model.` references after each slice. Several slices reported clean compile but had remaining live-code Mendix refs that surfaced only when a downstream slice removed the cascading error.

4. **Slice-internal helper refactor** vs **deferred helper refactor.** When a public method `Foo()` calls private `BarHelper(IEntity)`, three patterns work:
   - (a) Refactor `BarHelper`'s signature in the same slice (preferred when caller count is 1)
   - (b) Refactor `BarHelper` separately in a later slice (preferred when caller count is > 1 — Phase 4 sometimes batched callers per parent helper)
   - (c) Surface as `escalation: manual` if the helper's logic requires a typed-API surface that Interop doesn't expose

5. **Interface gaps surface as `escalation: manual` JSON.** Phase 4 introduced this pattern systematically. ~15 methods total returned `{success: false, escalation: "manual", message: "..."}` for surfaces not on the typed Interop:
   - **`ConfigureConstantValues`, `CreateConstant`, `UpdateConstant`** — typed `IConstantValue`/`ISharedValue` write not on Interop. Deferred decision: extend `IModelHost` with constant CRUD.
   - **`SyncFilesystem`** — `IAppService.SynchronizeWithFileSystem` not on `IStudioProAppHost`. Could be extended.
   - **`CheckProjectErrors`** — no `IConsistencyChecker` host. Could be extended.
   - **5 security methods** (`ReadSecurityInfo`, `ReadEntityAccessRules`, `ReadMicroflowSecurity`, `AuditSecurity`, `ListRules`) — `IUntypedModelHost` only exposes flat `GetUnitsOfType`, not sub-element traversal. Future extension.
   - **`UpdateMicroflow`'s `return_type` / `return_variable_name` branches** — `IMicroflow.ReturnType` mutation not on `IMicroflowAuthoringHost`. Workaround: delete + recreate microflow.
   - **`DeleteModelElement`'s module/constant/enumeration branches** — typed deletes not on Interop. Entity/attribute/association delete branches DO work.
   - **`ManageNavigation`'s remove/list/set-icon/set-target paths** — `INavigationHost` only exposes append-only `PopulateWebNavigationWith`.

6. **Untyped-model writes are NOT possible.** `IUntypedModelHost` is read-only by design. If a tool needs to mutate something only available in the untyped tree (e.g., module security), there's no path — must extend a typed host interface OR escalate.

---

## API gaps cataloged for next session (interface-extension backlog)

Phase 4 surfaced these gaps that future tasks should consider extending Core interfaces to fix:

### `IModelHost`
- `CreateConstant(moduleName, name, dataType, value)` — needed by `CreateConstant` tool
- `UpdateConstant(documentId, newValue)` — needed by `UpdateConstant` tool
- `DeleteModule(moduleId)` — needed by `DeleteModelElement` module branch
- `RuntimeSetting`-specific After-Startup write — `SetupDataImportInternal` Step 5 hit this

### `IDomainModelHost`
- `DeleteConstant`, `DeleteEnumeration` — needed by `DeleteModelElement` branches
- `UpdateEnumeration` (extend `AddValues`/`RemoveValues`/`RenameValues` if not already there — batch 3 says it IS there; verify)

### `AttributeRef` record
- `MaxLength` (string-attribute length constraint)
- `EnumerationQualifiedName` (which enum a Kind=Enumeration attribute references)
- `DefaultValue` (for `ReadAttributeDetails` output)

### `ModuleId` record
- `FromAppStore` (to filter out App Store modules — used to live on `IModule`)

### `IStudioProAppHost`
- `SynchronizeWithFileSystem()` — for `SyncFilesystem` tool

### `IUntypedModelHost` (untyped traversal)
- `GetElementsOfType(unitQualifiedName, elementType)` — for sub-element traversal (security, REST service resources, workflow activities)
- Would unblock 5 security methods + `ListRestServices` sub-element counts + `ListWorkflows` activity counts

### Potential new interface: `IConsistencyHost`
- For `CheckProjectErrors` (now `escalation: manual`)

### Potential new interface: `IConstantsHost`
- Alternative to extending `IModelHost` — if constants CRUD grows beyond 4-5 methods

---

## What's next (Phases 5-8)

Per the plan and Phase 2 handoff:

- **Phase 5** — Test harness integration (verify Terminal.Tests and Concord.Core.Tests don't drift; possibly add more SPMCP smoke tests against the registered tool surface — the 4 in Phase 4 cover instantiation + basic dispatch but not the full ~80 tool entries).
- **Phase 6** — Wire SPMCP tool surface into the existing `StudioProActionServer.cs` MCP server (concord-mcp on port 7783). Currently the tools instantiate but the HTTP server doesn't route to them. This is the **functional integration** step.
- **Phase 7** — Update marketing docs + CHANGELOG entry for v5.0.0-alpha.2 covering the SPMCP merge.
- **Phase 8** — Tag v5.0.0-alpha.2, open PR to main, run `/ultrareview` as per the release playbook.

---

## Recommended dispatch order for the new session

1. **Read this handoff + the Phase 2/3 handoffs** for the full picture.
2. **Don't try to add to Phase 4 retroactively** — the interface extensions cataloged above are deferred to a future cycle by design; pursue them only if/when a downstream task needs them.
3. **Phase 5 first** — start by running `dotnet test` to confirm the 253 tests still pass, then consider whether more SPMCP smoke tests are needed (the existing 4 cover ~5% of the tool surface; full coverage could be 80+ tests).
4. **Phase 6 is the next functionally meaningful chunk.** It requires reading `src/StudioProActionServer.cs` + understanding how MCP tool dispatch currently works, then wiring the SPMCP `Mendix*Tools` classes and `Handlers/` classes into the route table.

---

## Things NOT to do (additions for Phase 5+)

- **Don't revive any deleted helper from Phase 4 without checking if it has a new caller in code added since Phase 4.** Phase 4 deleted ~5,000 lines of dead code; reintroducing any of them by mistake would be a regression.
- **Don't add `using Mendix.StudioPro.ExtensionsAPI.*` to any file under `src/Concord.Core/`.** Phase 4's core success criterion is "zero Mendix.* in Core." Any new SPMCP code that needs Studio Pro types must go through `HostServices.*`.
- **Don't refactor the Fakes back to throwing `NotImplementedException` for the canned-data methods.** The 4 smoke tests in Phase 4 rely on `FakeModelHost.ListModules` returning `[TestModule]` etc.
- **Don't merge to main yet** — Phase 4 is one milestone of an 8-phase plan. Wait for Phase 8 to tag and merge.

---

## Quick commit reference

| Commit | Phase | Headline |
|---|---|---|
| `15f0873` | 4.1 | Namespace rename + package refs (build broken by design) |
| `812206e` | 4.2 | Task 13 complete (all read paths) |
| `cbc19fd` | 4.3 | Dead-code cleanup (-2,118 lines from MendixAdditionalTools.cs) |
| `a15c671` | 4.4 | MendixAdditionalTools.cs Mendix-free |
| `d94d35e` | 4.5 | MendixDomainModelTools.cs Mendix-free |
| `17088e7` | 4.6 | All 9 handlers Mendix-free |
| `363c8f1` | 4.7 | Utils.cs Mendix-free (Core builds clean — Phase 4 success criterion met) |
| `d658d00` | 4.8 | Phase 4 smoke tests landed — HEAD |

---

## TL;DR for the new session

1. **Read this handoff + the Phase 2/3 handoffs.** Phase 4's diff is 19 commits but the patterns are documented above.
2. **Resume on branch `feat/v5.0.0-w2-mcpx-merge` at `d658d00`** — clean, pushed, 253 tests green, Core builds clean with zero Mendix.* refs.
3. **Suggested first action:** read `src/StudioProActionServer.cs` to understand how MCP tool routing currently works — this is the bridge Phase 6 needs to extend.
