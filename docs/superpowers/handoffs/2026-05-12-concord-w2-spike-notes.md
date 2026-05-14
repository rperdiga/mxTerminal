# W2 Discovery Spike — Findings

Date: 2026-05-12
Branch: feat/v5.0.0-w2-mcpx-merge
W1 anchor: v5.0.0-alpha.1 @ 6dbfbf7

## Studio Pro 11.x tools/list snapshot
- Captured: no — deferred (requires running Studio Pro 11.10 with MCP attached)
- STUDIO_PRO_11X_TOOLS_LIST = TBD
- Implications: Phase 5 Task 18 uses spec lines 198-211 as working assumption.
  Allowlist reconciliation is a follow-up before W2 ships.

## MCPExtension subtree source
- Approach: local git init (option a)
- Source ref: local tag `concord-w2-import` at C:\Extensions\MCPExtension
- Commit: 329180c — "import: MCPExtension snapshot for Concord W2 subtree merge"
- Directories confirmed present: Tools/, Handlers/, Utils/, backport-10x/
- (If a remote URL becomes available later, the plan's option b can replace this.)

## Studio Pro service inventory (SPMCP)

Captured 2026-05-12 from `src/Concord.Core/Spmcp/Tools/` + `Handlers/`.

### Namespaces referenced
- `Mendix.StudioPro.ExtensionsAPI.Model` — core `IModel`, `IModule`, `IDocument` interfaces
- `Mendix.StudioPro.ExtensionsAPI.Model.Projects` — `IProject`, `IModule`, `IDocument`, `IFolderBase`
- `Mendix.StudioPro.ExtensionsAPI.Model.DomainModels` — `IEntity`, `IAttribute`, `IAssociation`, `IGeneralization`, `AssociationDirection`, `AssociationType`
- `Mendix.StudioPro.ExtensionsAPI.Model.Microflows` — `IMicroflow`, `MicroflowReturnValue`, `AttributeSorting`
- `Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions` — `CommitEnum`, `AggregateFunctionEnum`, `ChangeListActionOperation`, `ChangeActionItemType`
- `Mendix.StudioPro.ExtensionsAPI.Model.MicroflowExpressions` — `IMicroflowExpression`
- `Mendix.StudioPro.ExtensionsAPI.Model.JavaActions` — Java action types (imported in MendixAdditionalTools; not surfaced via constructor injection)
- `Mendix.StudioPro.ExtensionsAPI.Model.Settings` — `IProjectSettings`, `IConfigurationSettings`
- `Mendix.StudioPro.ExtensionsAPI.Model.Constants` — `IConstant`
- `Mendix.StudioPro.ExtensionsAPI.Model.DataTypes` — `DataType` (value type, not interface)
- `Mendix.StudioPro.ExtensionsAPI.Model.UntypedModel` — accessed via `IUntypedModelAccessService` (resolved from `IServiceProvider`)
- `Mendix.StudioPro.ExtensionsAPI.Model.Enumerations` — `IEnumeration`
- `Mendix.StudioPro.ExtensionsAPI.Model.Pages` — `IPage`
- `Mendix.StudioPro.ExtensionsAPI.Model.Texts` — text/translation types (used in WriteModelHandler, ReadMicroflowActivitiesHandler, MendixDomainModelTools)
- `Mendix.StudioPro.ExtensionsAPI.Services` — `IPageGenerationService`, `INavigationManagerService`, `IVersionControlService`, `IUntypedModelAccessService`, `IMicroflowService`, `INameValidationService`
- `Mendix.StudioPro.ExtensionsAPI.UI.Services` — `INavigationManagerService` (UI-tier service, imported alongside Services)

### Constructor signatures

**Tools:**
- `MendixAdditionalTools(IModel model, ILogger<MendixAdditionalTools> logger, IPageGenerationService pageGenerationService, INavigationManagerService navigationManagerService, IServiceProvider serviceProvider, string? projectDirectory = null)` — also resolves `IVersionControlService` and `IUntypedModelAccessService` from `IServiceProvider` in the constructor body
- `MendixDomainModelTools(IModel model, ILogger<MendixDomainModelTools> logger, INameValidationService? nameValidationService = null)`

**Handlers** (all extend `BaseApiHandler`; base takes `IModel currentApp`):
- `AssociationDiagnosticHandler(IModel currentApp)` — base only
- `DebugHandler(IModel currentApp)` — base only
- `DeleteModelHandler(IModel currentApp)` — base only
- `GenerateOverviewHandler(IModel currentApp, IPageGenerationService pageGenerationService, INavigationManagerService navigationManagerService)` — `[ImportingConstructor]`
- `ListMicroflowsHandler(IModel currentApp)` — base only
- `ReadMicroflowActivitiesHandler(IModel currentApp, IMicroflowService microflowService)`
- `ReadModelHandler(IModel currentApp)` — base only
- `SaveDataHandler(IModel currentApp)` — base only
- `WriteModelHandler(IModel currentApp)` — base only

### Type → Core interface mapping

| Studio Pro type / namespace | Target Core interface |
|---|---|
| `IModel`, `IProject`, `IModule`, `IDocument` | `IModelHost` |
| `IFolderBase` | `IModelHost` (structural navigation within module tree) |
| `IEntity`, `IAttribute`, `IAssociation`, `IGeneralization`, `AssociationDirection`, `AssociationType` | `IDomainModelHost` |
| `IPageGenerationService` | `IPageGenerationHost` |
| `INavigationManagerService` (both `Services` + `UI.Services`) | `INavigationHost` |
| `IVersionControlService` | `IVersionControlHost` |
| `IUntypedModelAccessService` | `IUntypedModelHost` |
| `IMicroflowService`, `IMicroflow`, `MicroflowReturnValue`, `AttributeSorting`, `Microflows.Actions.*`, `IMicroflowExpression` | `IMicroflowAuthoringHost` |
| `IEnumeration`, `IConstant`, `IProjectSettings`, `IConfigurationSettings` | `IModelHost` (read) + `IDomainModelHost` (write) |
| `DataType` (value type) | `IDomainModelHost` (parameter/return-type authoring) |
| `INameValidationService` | `IDomainModelHost` (name-safety validation before writes) |
| `IPage` | `IPageGenerationHost` (page lookup + generation context) |
| `IProjectSettings`, `IConfigurationSettings` | `IModelHost` (project-level read-only settings) |

### Out-of-scope leakage

- **`Mendix.StudioPro.ExtensionsAPI.Model.Texts`** — imported in `WriteModelHandler`, `ReadMicroflowActivitiesHandler`, and `MendixDomainModelTools`. Used for translatable text / caption objects attached to domain model elements and microflow activities. Does not map cleanly to any of the seven planned interfaces; the closest home is `IDomainModelHost` since it's always used alongside entity/attribute/microflow mutations. **Proposal: extend `IDomainModelHost` with a `ITranslationContext` accessor (or expose the relevant text-factory methods directly), leaving a `ITextHost` interface as a follow-up if the surface grows).**
- **`Mendix.StudioPro.ExtensionsAPI.Model.JavaActions`** — imported (but not constructor-injected) in `MendixAdditionalTools`. Used for reflection/type-checking of Java action activity nodes inside microflows. Could live under `IMicroflowAuthoringHost`, but `JavaActions` is a distinct document type. **Proposal: treat as part of `IMicroflowAuthoringHost` for now; flag for a dedicated `IJavaActionHost` if the surface expands in Phase 4.**
- **`Mendix.StudioPro.ExtensionsAPI.UI.Services` (`INavigationManagerService`)** — `INavigationManagerService` is defined in both `Services` and `UI.Services`. The `GenerateOverviewHandler` imports from `UI.Services` while `MendixAdditionalTools` imports from `Services`. This is the same interface surfaced under two namespaces. The `INavigationHost` Interop interface must resolve to whichever namespace is correct for the non-UI Core tier; this needs verification against the SDK docs before Tasks 5-7.
- **`IServiceProvider`** — `MendixAdditionalTools` takes `IServiceProvider` directly to late-resolve `IVersionControlService` and `IUntypedModelAccessService`. The Core Interop layer should not expose `IServiceProvider`; those two services should be promoted to explicit constructor parameters on the Interop interface or injected at the host boundary. Flag for Phase 4 refactor.

## Open compile-time blockers expected during Phase 2
- (record findings here as Phase 2 progresses)

## Phase 3 API-drift findings

Date: 2026-05-12 (Task 11 implementation)

### Service namespace verification (XML grep results)

Checked both `mendix.studiopro.extensionsapi/10.21.1` and `11.6.2` XML docs.

| Service | 10.21.1 namespace | 11.6.2 namespace | Notes |
|---|---|---|---|
| `IPageGenerationService` | `Services` | `Services` | Same on both; no drift |
| `INavigationManagerService` | `Services` | `Services` | Same on both. Spike note in "Out-of-scope leakage" flagged `UI.Services` but only `Services` appears in the type index (`T:` entries); `UI.Services` mention was in the method-level docs only. RESOLUTION: use `Mendix.StudioPro.ExtensionsAPI.Services.INavigationManagerService` in both hosts. |
| `IMicroflowService` | `Services` | `Services` | Same on both; no drift |
| `IUntypedModelAccessService` | `Services` | `Services` | Present on BOTH 10.21.1 and 11.6.2. IsAvailable=true on both if service is injected. |
| `IVersionControlService` | `UI.Services` | `UI.Services` | ONLY in `UI.Services` on BOTH versions — NOT in `Services`. The T: type index shows `Mendix.StudioPro.ExtensionsAPI.UI.Services.IVersionControlService` in both XML files. |

### IsAvailable conclusions

- **VersionControlHost10x.IsAvailable** — `true` when `IVersionControlService` is injected. The service IS present in 10.21.1 (`UI.Services` namespace). Both versions implemented normally; no stub needed.
- **UntypedModelHost10x.IsAvailable** — `true` when `IUntypedModelAccessService` is injected. The service IS present in 10.21.1 (`Services` namespace). Both versions implemented normally; no stub needed.

### API gaps found in 11.6.2 typed surface (affects both host versions)

- `IMicroflow.Documentation` — NOT exposed in 11.6.2 ExtensionsAPI. `MicroflowSummary.Documentation` is always null from these hosts. (Documentation IS accessible via the untyped model if needed.)
- `IActionActivity.Delete()` — NOT exposed. `DeleteActivity()` throws `NotSupportedException` on both host versions.
- `IMicroflowCallAction.Microflow` — property does not exist. Correct pattern is `IMicroflowCallAction.MicroflowCall.Microflow` (using the intermediate `IMicroflowCall` object).
- `IPageGenerationService.GenerateOverviewPages` — returns `IEnumerable<IPage>` (not `IReadOnlyList`); `.ToList()` required.
- Navigation read-back (`ListProfiles`, `ReadProfile`) — `INavigationManagerService` exposes only `PopulateWebNavigationWith`. No read API for profiles or existing items. `ListProfiles()` returns empty; `ReadProfile()` returns null. `RemoveItem`/`SetItemIcon` are no-ops. `SetItemTarget` re-adds via PopulateWebNavigationWith (appends, does not replace).
- `IMicroflow.SetAccessLevel` / `AllowedModuleRoles` — not accessible via typed API. `SetAccessLevel()` is a no-op; security lives in a separate `IModuleSecurity` untyped model unit.

## W2 smoke results

Date: 2026-05-13 (Task 35; pre-tag state at HEAD `f1858c0`).

### Step 1 + 2 — Clean build + tests (controller-executed)

| Step | Command | Result |
|---|---|---|
| Clean build | `git clean -fdx -- src/*/bin src/*/obj tests/*/bin tests/*/obj` + `dotnet build Terminal.sln` | 0 errors, 20 warnings (all pre-existing CS0414 / CS8602 / CS8604; no new ones from W2 work). Build time ~6 s. |
| Full tests | `dotnet test Terminal.sln --no-build` | **272 total** passing: 242 Terminal.Tests + 27 Concord.Core.Tests, 3 Maia-live skipped (no real Studio Pro on this build agent), 0 failed. Up from 244 in 5.0.0-alpha.1. |

### Step 3 + 4 — Studio Pro UI smoke (NEO REQUIRED)

Compile-time + unit-test green proves API-shape parity. The runtime parity check (pane opens; WebView renders; `concord-mcp` responds; one tool per family invokes successfully on a real Studio Pro install) requires Neo's hands.

**Suggested matrix:**

| Version | Pane opens | `/health` 200 | `tools/list` count | `save_all` | Concord-specific tool | Notes |
|---|---|---|---|---|---|---|
| 11.10.x | ☐ | ☐ | ~37 (curated allowlist) | ☐ | `delete_model_element` | |
| 10.24.13 | ☐ | ☐ | ~87 (full SPMCP) | ☐ | `create_entity` | |
| 11.10.x + Claude Code | ☐ | n/a | n/a | ☐ | `claude mcp call concord-mcp save_all` | |
| 11.10.x + Codex | ☐ | n/a | n/a | ☐ | UI tool via Codex | |
| 11.10.x + Copilot CLI | ☐ | n/a | n/a | ☐ | UI tool via Copilot | |

**Setup:**
- Set `MendixDeployTarget11x` to a 11.10 testbed and `MendixDeployTarget10x` to a 10.24.13 testbed in `Directory.Build.props`.
- `dotnet build Terminal.sln` populates `<project>/extensions/Concord11x/` and `<project>/extensions/Concord10x/` respectively.
- Open Studio Pro → Extensions → Concord → Open Pane.
- The 10.24.13 install is the bigger unknown: this is the FIRST runtime test of the Host10x UI port after Phase 6's compile-time-only validation.

**If anything diverges,** capture the divergence in a fresh handoff (`docs/superpowers/handoffs/2026-05-13-after-w2-smoke.md`) and roll the finding into the alpha.3 backlog. Don't tag until smoke is clean.

### Step 5 — Capture findings (this section)

Findings recorded as the smoke runs. If the matrix above lights up green on both versions, add a one-line "no divergences observed" note here and proceed to Step 6.

### Step 6 — Tag (NEO'S CALL)

```bash
git tag -a v5.0.0-alpha.2 -m "Concord 5.0.0-alpha.2 — W2 SPMCP merge + Host10x UI port"
# Pushing is a separate decision:
# git push origin v5.0.0-alpha.2
```

**Do not tag until Step 4 (10.24.13 smoke) is green.** Compile-time parity is necessary but not sufficient for the "Studio Pro 10.x feature parity" claim made in the CHANGELOG entry. If 10.24.13 surfaces a runtime divergence, fix in alpha.2 before the tag OR roll the claim to alpha.3 and tag a narrower scope.

### Step 7 — Commit smoke notes

Append this file (already present) on completion. No new commit required for the empty placeholders above; only commit if the smoke results fill in actual rows.
