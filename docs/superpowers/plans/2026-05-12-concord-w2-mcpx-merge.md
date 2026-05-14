# Concord W2 — SPMCP Source-Merge + Host10x UI Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge MCPExtension's ~84-tool catalog into Concord, expose them through a single `concord-mcp` server with version-aware `ToolCatalog`, port Host11x's UI surface into Host10x so 10.24.13 gets the full Concord experience, and consolidate the dual host-injection mechanisms onto `HostServices`.

**Architecture:** MCPExtension's `Tools/`, `Handlers/`, `Utils/` source-merge into `src/Concord.Core/Spmcp/`. Studio-Pro-typed surfaces (`IModel`, `IPageGenerationService`, etc.) get wrapped behind new Core `Interop` interfaces (`IModelHost`, `IPageGenerationHost`, `INavigationHost`, `IVersionControlHost`, `IUntypedModelHost`); each host project supplies a version-pinned implementation. A new `Concord.Core/Mcp/ToolCatalog.cs` registers tools per `TargetMode` + family-toggle settings — 10.x gets the full surface; 11.x gets the curated allowlist from the spec. Host10x grows the matching UI/pane/menu/web-server classes against ExtensionsAPI 10.21.1. Once the catalog and 10.x UI are landed, `HostServices` becomes the only injection path and the legacy `Func<>` callback chain in `StudioProActions` retires.

**Tech Stack:** .NET 8, MEF (`System.ComponentModel.Composition`), `Mendix.StudioPro.ExtensionsAPI` 10.21.1 + 11.6.2, xunit + FluentAssertions, Eto.Forms (UI), `Microsoft.Extensions.Logging` / `Microsoft.Extensions.DependencyInjection` (transitively required by SPMCP code), esbuild (UI bundle, unchanged).

---

## Branch strategy (decided before Task 0)

Default per the W2 handoff: cut a fresh branch off the W1 alpha tag.

```bash
git fetch origin
git checkout -b feat/v5.0.0-w2-mcpx-merge v5.0.0-alpha.1
```

If the user explicitly opts for option (B) — staying on `feat/v5.0.0-w1-foundation` — skip the checkout and continue on that branch. **Do not merge to `main`** until the full Phase 8 smoke matrix passes on both Studio Pro 10.24.13 and 11.10.

---

## Working assumptions to verify in Phase 0

- The MCPExtension repo at `C:\Extensions\MCPExtension` is not a git repository (`git remote -v` returns "fatal: not a git repository"). `git subtree add` requires a remote-or-local **git** source. Phase 0 includes initialising MCPExtension as a one-shot local git repo (or cloning the upstream GitHub repo when the URL is known) before the subtree pull.
- The live `mendix-studio-pro__tools/list` snapshot on Studio Pro 11.10 has not been captured in the repo. The 11.x curated allowlist in the spec (lines 198-211) is a starting point, not a final list. Phase 0 captures the snapshot and reconciles.
- The 10.x ExtensionsAPI surface for `IDockablePaneExtension`, `IWebServerExtension`, `IModel`, `IPageGenerationService`, and `INavigationManagerService` may differ from 11.x in ways not yet documented in the W1 handoff. Each Host10x port task verifies against `C:\Extensions\MCPExtension\backport-10x\reference\` and adjusts as needed.

If any of these assumptions fail (e.g., the MCPExtension upstream URL is wrong, or 10.x's `IWebServerExtension` doesn't exist), the affected task pauses and the plan needs an in-flight amendment — flag explicitly in the task's commit message and escalate to the user.

---

## File Structure

**Created files (Concord.Core):**

```
src/Concord.Core/
├── Interop/
│   ├── IModelHost.cs                    # NEW — wraps Mendix IModel + project access
│   ├── IDomainModelHost.cs              # NEW — wraps entity/attribute/association ops
│   ├── IPageGenerationHost.cs           # NEW — wraps IPageGenerationService
│   ├── INavigationHost.cs               # NEW — wraps INavigationManagerService
│   ├── IVersionControlHost.cs           # NEW — wraps IVersionControlService
│   ├── IUntypedModelHost.cs             # NEW — wraps IUntypedModelAccessService
│   ├── IMicroflowAuthoringHost.cs       # NEW — wraps microflow create/edit/inspect
│   └── HostServices.cs                  # MODIFY — add the new accessors
├── Mcp/
│   ├── ITool.cs                         # NEW — { string Name; Func<JsonObject,Task<object>> Invoke }
│   ├── ToolCatalog.cs                   # NEW — registry: TargetMode + ToggleState → tools
│   ├── ToolFamily.cs                    # NEW — enum (Pages, Navigation, Security, …)
│   ├── Studio11xAllowlist.cs            # NEW — HashSet<string> curated for 11.x
│   ├── McpServer.cs                     # NEW (or refactored from StudioProActionServer.cs)
│   ├── StudioProActionServer.cs         # MODIFY — delegate dispatch to ToolCatalog
│   └── StudioProActions.cs              # MODIFY — read from HostServices instead of Func<>
└── Spmcp/                               # NEW (created by subtree add in Phase 1)
    ├── Tools/
    │   ├── MendixAdditionalTools.cs     # imported then refactored
    │   └── MendixDomainModelTools.cs    # imported then refactored
    ├── Handlers/
    │   └── (9 files imported)
    └── Utils/
        └── Utils.cs                     # imported (no refactor needed)
```

**Created files (host projects):**

```
src/Concord.Host10x/
├── Interop/
│   ├── ModelHost10x.cs                  # NEW — implements IModelHost against 10.21.1
│   ├── DomainModelHost10x.cs            # NEW
│   ├── PageGenerationHost10x.cs         # NEW
│   ├── NavigationHost10x.cs             # NEW
│   ├── VersionControlHost10x.cs         # NEW
│   ├── UntypedModelHost10x.cs           # NEW
│   ├── MicroflowAuthoringHost10x.cs     # NEW
│   ├── RunStateProbe.cs                 # NEW — mirror of Host11x's
│   └── StudioProUiAutomation.cs         # NEW — mirror of Host11x's (Ctrl+S / F5 / F8)
├── MenuExtensions/
│   └── TerminalMenuExtension.cs         # MODIFY (renamed from ConcordMenuExtension.cs)
├── Pane/
│   ├── TerminalPaneExtension.cs         # NEW — 10.x IDockablePaneExtension equivalent
│   └── TerminalPaneViewModel.cs         # NEW
└── Ui/
    └── TerminalWebServer.cs             # NEW — 10.x IWebServerExtension equivalent

src/Concord.Host11x/
├── Interop/
│   ├── ModelHost11x.cs                  # NEW — implements IModelHost against 11.6.2
│   ├── DomainModelHost11x.cs            # NEW
│   ├── PageGenerationHost11x.cs         # NEW
│   ├── NavigationHost11x.cs             # NEW
│   ├── VersionControlHost11x.cs         # NEW
│   ├── UntypedModelHost11x.cs           # NEW
│   └── MicroflowAuthoringHost11x.cs     # NEW
├── Spmcp/
│   └── SpmcpToolBootstrap11x.cs         # NEW — instantiates SPMCP tool classes per host
└── (existing Pane/, Ui/, MenuExtensions/, Interop/RunStateProbe.cs etc unchanged structurally)

src/Concord.Host10x/Spmcp/
└── SpmcpToolBootstrap10x.cs             # NEW — same pattern, 10.x services
```

**Modified files:**

```
src/Concord.Core/Concord.Core.csproj    # add System.Text.Json package (already in tree), Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions
src/Concord.Core/Interop/HostServices.cs # add 7 new properties + Register overload
src/Concord.Core/Mcp/StudioProActionServer.cs # delegate to ToolCatalog
src/Concord.Core/Mcp/StudioProActions.cs # consume HostServices directly
src/Concord.Host11x/Host11xEntry.cs     # register the new IModelHost, IDomainModelHost, … impls
src/Concord.Host10x/Host10xEntry.cs     # same on 10.x side
src/Concord.Host11x/Concord.Host11x.csproj # remove old direct service usage if any
src/Concord.Host10x/Concord.Host10x.csproj # add Eto + UI deps, mirror Host11x project layout
tests/Concord.Core.Tests/                # add ToolCatalog, Studio11xAllowlist, fake-host tests
tests/Terminal.Tests/                    # update tests that exercised the Func<> callback paths
.gitignore                               # add MCPExtension/ if Phase 0 clones it into a sibling working folder
CHANGELOG.md                             # 5.0.0-alpha.2 entry
docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md  # NEW — Phase 0 findings
```

**Deleted files (from the SPMCP subtree import):**

```
src/Concord.Core/Spmcp/Mcp/McpServer.cs          # Concord has its own; delete
src/Concord.Core/Spmcp/Mcp/MendixMcpServer.cs    # duplicate; delete
src/Concord.Core/Spmcp/Mcp/ToolCallEventArgs.cs  # absorbed into Concord.Core/Mcp; delete or relocate
src/Concord.Core/Spmcp/MenuExtension.cs          # duplicate of Concord's; delete
src/Concord.Core/Spmcp/AIAPIEngine.cs            # MCPExtension's pane host; delete
src/Concord.Core/Spmcp/AIAPIEngineViewModel.cs   # delete
src/Concord.Core/Spmcp/start-studiopro.bat       # dev tooling for standalone repo; delete
src/Concord.Core/Spmcp/test-transports.sh        # delete
src/Concord.Core/Spmcp/manifest.json             # delete
src/Concord.Core/Spmcp/SPMCP/                    # the Mendix module project; moves to resources/Concord.SampleData/ in W4
src/Concord.Core/Spmcp/backport-10x/             # kept temporarily as reference for Host10x port; deleted at end of Phase 6
src/Concord.Core/Spmcp/.mcp.json                 # standalone-repo config; delete
src/Concord.Core/Spmcp/README.md                 # delete (Concord's README is the source of truth)
src/Concord.Core/Spmcp/project.mpr               # delete
src/Concord.Core/Spmcp/MCPExtension.csproj       # delete (Core compiles the .cs directly)
src/Concord.Core/Spmcp/MCPExtension.sln          # delete
src/Concord.Core/Spmcp/Core/                     # the SPMCP modeling sub-project; delete (Concord.Core is the only Core now)
```

`backport-10x/` is exceptional — it stays in tree through Phase 6 because the Host10x port references it for API-drift guidance, then gets deleted in Task 28.

---

## Phase 0 — Discovery and branch setup

### Task 1: Branch off the W1 alpha and capture the spike

**Files:**
- Create: `docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md`

- [ ] **Step 1: Verify the branch state**

Run: `git status && git log --oneline -5 && git tag --list 'v5.0.0*'`

Expected: clean working tree, HEAD at `a0d56ac` or later, `v5.0.0-alpha.1` tag present locally.

- [ ] **Step 2: Cut the W2 branch (default option A)**

```bash
git checkout -b feat/v5.0.0-w2-mcpx-merge v5.0.0-alpha.1
git push -u origin feat/v5.0.0-w2-mcpx-merge
```

If option (B) was chosen, skip — continue on `feat/v5.0.0-w1-foundation`.

- [ ] **Step 3: Capture a live `mendix-studio-pro__tools/list` snapshot from Studio Pro 11.10**

From a Claude Code session attached to a running Studio Pro 11.10 with the built-in MCP server enabled, run `claude mcp list-tools mendix-studio-pro --format=json > docs/superpowers/handoffs/2026-05-12-studio-pro-11x-tools.json`.

Expected: a JSON document listing the studio-pro MCP tool catalog. This is the ground truth for what to **exclude** from Concord's 11.x allowlist. If the user can't do this step yet, mark the spike notes as `STUDIO_PRO_11X_TOOLS_LIST = TBD` and treat the spec's allowlist (lines 198-211) as the working assumption — flag the reconciliation as a follow-up in CHANGELOG.

- [ ] **Step 4: Verify the MCPExtension source is reachable**

Run: `ls "C:\Extensions\MCPExtension\Tools" "C:\Extensions\MCPExtension\Handlers" "C:\Extensions\MCPExtension\Utils" "C:\Extensions\MCPExtension\backport-10x"`

Expected: each directory enumerates files. Then check git status:

```powershell
cd "C:\Extensions\MCPExtension"
git status
```

If the response is "fatal: not a git repository", the directory is a working copy without `.git/`. Two options:

  (a) **Initialise locally + tag a synthetic root** (preferred when the upstream URL isn't ready to hand):

  ```powershell
  cd "C:\Extensions\MCPExtension"
  git init
  git add -A
  git commit -m "import: MCPExtension snapshot for Concord W2 subtree merge"
  git tag concord-w2-import
  ```

  Then in Concord, `git subtree add --prefix=src/Concord.Core/Spmcp file:///C:/Extensions/MCPExtension concord-w2-import --squash`.

  (b) **Clone the upstream MCPExtension GitHub repo** (the user will provide the URL — do not guess).

Record the chosen approach in the spike notes file.

- [ ] **Step 5: Write the spike notes file**

Create `docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md`:

```markdown
# W2 Discovery Spike — Findings

Date: <today>
Branch: feat/v5.0.0-w2-mcpx-merge (or feat/v5.0.0-w1-foundation if option B)
W1 anchor: v5.0.0-alpha.1 @ <SHA>

## Studio Pro 11.x tools/list snapshot
- Captured: <yes | no — deferred>
- Path: `docs/superpowers/handoffs/2026-05-12-studio-pro-11x-tools.json`
- Tools advertised: <count>
- Implications for 11.x allowlist:
  - Tools removed from spec's allowlist because studio-pro now covers them: <list>
  - Tools added to allowlist because of newly-discovered gaps: <list>

## MCPExtension subtree source
- Approach: <local git init | upstream clone>
- Source ref: <local tag `concord-w2-import` | upstream commit SHA>

## Open compile-time blockers expected during Phase 2
- (record findings here as Phase 2 progresses)
```

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md \
        docs/superpowers/handoffs/2026-05-12-studio-pro-11x-tools.json
git commit -m "docs(w2): capture spike notes + 11.x studio-pro tools/list snapshot

Anchors the W2 plan against the live 11.x studio-pro MCP surface so
the allowlist isn't a guess. Records the subtree-import approach
chosen for MCPExtension."
```

If the tools/list snapshot couldn't be captured, commit just the spike notes file with the TBD marker.

---

## Phase 1 — Subtree-import MCPExtension and prune duplicates

### Task 2: `git subtree add` the MCPExtension source

**Files:**
- Create: `src/Concord.Core/Spmcp/` (entire tree from the subtree pull)

- [ ] **Step 1: Run the subtree add**

Depending on the Step-4 decision in Task 1:

  - Local-init approach:
    ```bash
    git subtree add --prefix=src/Concord.Core/Spmcp \
                    file:///C:/Extensions/MCPExtension concord-w2-import --squash
    ```
  - Upstream-clone approach (substitute the real URL):
    ```bash
    git remote add mcpx <upstream-url>
    git fetch mcpx
    git subtree add --prefix=src/Concord.Core/Spmcp mcpx/main --squash
    ```

Expected: one new merge commit `"Add 'src/Concord.Core/Spmcp/' from commit '<sha>'"` and one squash commit `"Squashed 'src/Concord.Core/Spmcp/' content from commit '<sha>'"`. The new folder contains `Tools/`, `Handlers/`, `Mcp/`, `Utils/`, `backport-10x/`, plus the cruft we'll prune in Task 3.

- [ ] **Step 2: Verify the import**

Run: `ls src/Concord.Core/Spmcp/ && git log --oneline -5`

Expected: subtree contents visible, two new commits on the W2 branch.

- [ ] **Step 3: Verify the build still succeeds (it should — the imported .cs aren't compiled yet)**

Run: `dotnet build Terminal.sln`

Expected: `Build succeeded.` `Concord.Core.csproj` doesn't reference the new files yet. If the build fails, MSBuild may be auto-globbing — check `Concord.Core.csproj` for any `<Compile Include="**/*.cs">` glob, and add `<Compile Remove="Spmcp/**/*.cs" />` to suppress until Phase 2.

- [ ] **Step 4: Commit the import (already done by subtree add)**

`git subtree add` creates the commits automatically. Confirm with `git log --oneline -5`. No additional commit needed for this step.

### Task 3: Prune the duplicated MCPExtension plumbing

**Files:**
- Delete: `src/Concord.Core/Spmcp/Mcp/McpServer.cs`
- Delete: `src/Concord.Core/Spmcp/Mcp/MendixMcpServer.cs`
- Delete: `src/Concord.Core/Spmcp/MenuExtension.cs`
- Delete: `src/Concord.Core/Spmcp/AIAPIEngine.cs`
- Delete: `src/Concord.Core/Spmcp/AIAPIEngineViewModel.cs`
- Delete: `src/Concord.Core/Spmcp/start-studiopro.bat`
- Delete: `src/Concord.Core/Spmcp/test-transports.sh`
- Delete: `src/Concord.Core/Spmcp/manifest.json`
- Delete: `src/Concord.Core/Spmcp/.mcp.json`
- Delete: `src/Concord.Core/Spmcp/README.md`
- Delete: `src/Concord.Core/Spmcp/project.mpr`
- Delete: `src/Concord.Core/Spmcp/MCPExtension.csproj`
- Delete: `src/Concord.Core/Spmcp/MCPExtension.sln`
- Delete: `src/Concord.Core/Spmcp/bin/`, `src/Concord.Core/Spmcp/obj/`, `src/Concord.Core/Spmcp/dist/`
- Delete: `src/Concord.Core/Spmcp/Core/` (the SPMCP Core sub-project — Concord.Core is the only Core now)
- Delete: `src/Concord.Core/Spmcp/SPMCP/` and `src/Concord.Core/Spmcp/SPMCP.mpk` (this is the Mendix module project; in W4 it relocates to `resources/Concord.SampleData/` and gets repackaged as `Concord.SampleData.mpk`. Out of scope for W2 — delete here and re-import in W4.)
- Relocate: `src/Concord.Core/Spmcp/Mcp/ToolCallEventArgs.cs` → `src/Concord.Core/Mcp/ToolCallEventArgs.cs` (Concord's MCP server will reuse this DTO)
- Keep: `src/Concord.Core/Spmcp/Tools/`, `src/Concord.Core/Spmcp/Handlers/`, `src/Concord.Core/Spmcp/Utils/`, `src/Concord.Core/Spmcp/backport-10x/`

- [ ] **Step 1: Delete duplicate MCP transport and pane host**

```powershell
git rm -r src/Concord.Core/Spmcp/Mcp/McpServer.cs `
          src/Concord.Core/Spmcp/Mcp/MendixMcpServer.cs `
          src/Concord.Core/Spmcp/MenuExtension.cs `
          src/Concord.Core/Spmcp/AIAPIEngine.cs `
          src/Concord.Core/Spmcp/AIAPIEngineViewModel.cs
```

- [ ] **Step 2: Delete standalone-repo dev tooling**

```powershell
git rm src/Concord.Core/Spmcp/start-studiopro.bat `
       src/Concord.Core/Spmcp/test-transports.sh `
       src/Concord.Core/Spmcp/manifest.json `
       src/Concord.Core/Spmcp/.mcp.json `
       src/Concord.Core/Spmcp/README.md `
       src/Concord.Core/Spmcp/project.mpr `
       src/Concord.Core/Spmcp/MCPExtension.csproj `
       src/Concord.Core/Spmcp/MCPExtension.sln
```

If any path doesn't exist (older snapshot), `git rm` errors out — drop that line and continue. List the remaining files in the folder afterwards with `Get-ChildItem src/Concord.Core/Spmcp` to confirm only `Tools/`, `Handlers/`, `Mcp/`, `Utils/`, `backport-10x/` (and possibly `bin`/`obj`/`Core`/`SPMCP`) remain.

- [ ] **Step 3: Delete build artifacts and the embedded Mendix module project**

```powershell
git rm -r src/Concord.Core/Spmcp/bin `
          src/Concord.Core/Spmcp/obj `
          src/Concord.Core/Spmcp/dist `
          src/Concord.Core/Spmcp/Core `
          src/Concord.Core/Spmcp/SPMCP `
          src/Concord.Core/Spmcp/SPMCP.mpk
```

Same fallback — drop missing entries.

- [ ] **Step 4: Relocate ToolCallEventArgs to Concord.Core/Mcp**

```powershell
git mv src/Concord.Core/Spmcp/Mcp/ToolCallEventArgs.cs `
       src/Concord.Core/Mcp/ToolCallEventArgs.cs
```

Edit the file to change the namespace from `MCPExtension.MCP` to `Terminal.Mcp` so it lines up with Concord's convention. Concrete diff:

```csharp
// Before:
namespace MCPExtension.MCP;

// After:
namespace Terminal.Mcp;
```

- [ ] **Step 5: Delete the now-empty Spmcp/Mcp folder**

If `src/Concord.Core/Spmcp/Mcp/` is empty after the deletes/move, remove the directory:

```powershell
Remove-Item -Recurse src/Concord.Core/Spmcp/Mcp -ErrorAction SilentlyContinue
```

- [ ] **Step 6: Verify nothing in the kept tree references the deleted files**

Run: `Select-String -Path src/Concord.Core/Spmcp -Pattern "AIAPIEngine|MCPExtension\.MCP\.McpServer|MendixMcpServer" -Recurse`

Expected: no matches in `Tools/`, `Handlers/`, or `Utils/`. If hits exist, capture them; they're the surfaces the upcoming Phase 4 refactor must address.

- [ ] **Step 7: Build to confirm the prune didn't break anything**

Run: `dotnet build Terminal.sln`

Expected: `Build succeeded.` Core still doesn't compile `Spmcp/**/*.cs` yet, so the build is identical to Task 2's.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "chore(spmcp): prune duplicated plumbing from MCPExtension subtree

Deletes the MCPExtension McpServer/MendixMcpServer (Concord has its
own), its standalone pane host (AIAPIEngine + MenuExtension), dev
scripts (start-studiopro.bat, test-transports.sh, .mcp.json), the
Mendix module project (SPMCP/ — relocates to resources/ in W4), and
its csproj/sln/manifest. Keeps Tools/, Handlers/, Utils/, and
backport-10x/. ToolCallEventArgs relocates to Concord.Core/Mcp/."
```

---

## Phase 2 — Define Mendix-service Interop interfaces in Core

The seven new interfaces wrap the Studio Pro service types that SPMCP's tools use directly today (`IModel`, `IPageGenerationService`, `INavigationManagerService`, `IVersionControlService`, `IUntypedModelAccessService`, plus domain-model and microflow authoring helpers). Each is defined in Core with no Studio Pro reference; each host project supplies a concrete impl in Phase 3.

Interface design principle: **wrap the surface SPMCP actually uses, not the full Studio Pro service**. If `MendixAdditionalTools.SaveData` calls only `_model.Root` and `_model.GetModuleDocuments<>`, the interface exposes only those. We can grow the surface incrementally in later phases when a tool needs more.

### Task 4: Inventory SPMCP's Studio Pro service usage

**Files:**
- Modify: `docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md` (append the inventory)

- [ ] **Step 1: List every Studio Pro type referenced by SPMCP**

Run:

```powershell
Select-String -Path src/Concord.Core/Spmcp/Tools/*.cs, `
                    src/Concord.Core/Spmcp/Handlers/*.cs, `
                    src/Concord.Core/Spmcp/Utils/*.cs `
              -Pattern "Mendix\.StudioPro\.ExtensionsAPI\.[A-Za-z\.]+" `
              -AllMatches | ForEach-Object { $_.Matches.Value } | Sort-Object -Unique
```

Expected: a deduped list of Studio Pro namespaces and types — typically including `IModel`, `IModule`, `IProject`, `IEntity`, `IAttribute`, `IAssociation`, `IPageGenerationService`, `INavigationManagerService`, `IVersionControlService`, `IUntypedModelAccessService`, `IMicroflowService`, `IMicroflow`, plus model-element types under `Model.DomainModels`, `Model.Microflows`, etc.

- [ ] **Step 2: Map each Studio Pro service to a target Interop interface**

Append to spike notes:

```markdown
## Studio Pro service inventory (SPMCP)

| Studio Pro type | Target Core interface |
|---|---|
| IModel, IProject, IModule | IModelHost |
| IEntity, IAttribute, IAssociation, IGeneralization | IDomainModelHost |
| IPageGenerationService | IPageGenerationHost |
| INavigationManagerService | INavigationHost |
| IVersionControlService | IVersionControlHost |
| IUntypedModelAccessService | IUntypedModelHost |
| IMicroflowService + Microflow model types | IMicroflowAuthoringHost |
| IEnumeration, IConstant, ISettings, etc. | IModelHost (read paths) + IDomainModelHost (write paths) |

Out-of-scope leakage to escalate:
- <any type that doesn't fit cleanly — escalate to user before defining a new interface>
```

- [ ] **Step 3: Commit the inventory**

```bash
git add docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md
git commit -m "docs(w2): inventory SPMCP's Studio Pro service surface

Maps each ExtensionsAPI type used by the imported tools to a target
Core Interop interface. Drives the Phase 2 interface definitions."
```

### Task 5: Define IModelHost in Core

**Files:**
- Create: `src/Concord.Core/Interop/IModelHost.cs`

Core defines a **thin** abstraction over `IModel` — the minimum SPMCP needs without forcing Core to know about Studio Pro types. Where SPMCP currently passes `IModule`, `IDocument`, `IEntity` instances around, the Interop interface uses **identifier records** (e.g., `ModuleId`, `DocumentId`) and exposes methods like `GetModuleByName`, `GetDocument`, `ListModules`. The Studio Pro instance lives inside the host's implementation; Core never sees it.

- [ ] **Step 1: Write the interface**

`src/Concord.Core/Interop/IModelHost.cs`:

```csharp
namespace Terminal.Interop;

/// <summary>
/// Host-side identifier for a Mendix module. The Guid maps to the host's
/// internal IModule instance; Core never resolves it directly.
/// </summary>
public readonly record struct ModuleId(Guid Value, string Name);

public readonly record struct DocumentId(Guid Value, string QualifiedName);

public readonly record struct ProjectInfo(
    string Name,
    string DirectoryPath,
    string? MendixVersion,
    string? AppId);

/// <summary>
/// Wraps Mendix.StudioPro.ExtensionsAPI.Model.IModel for SPMCP tools that
/// only need project/module/document traversal. Heavier modeling
/// operations (entity create, microflow author, etc.) belong on the more
/// specific host interfaces.
/// </summary>
public interface IModelHost
{
    ProjectInfo GetProjectInfo();
    IReadOnlyList<ModuleId> ListModules();
    ModuleId? GetModuleByName(string moduleName);
    IReadOnlyList<DocumentId> ListModuleDocuments(ModuleId moduleId, string? documentTypeFilter = null);
    DocumentId? GetDocumentByQualifiedName(string qualifiedName);
    Task SaveAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Build Core to confirm the interface compiles**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj`

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/Concord.Core/Interop/IModelHost.cs
git commit -m "feat(core): add IModelHost interop interface (W2)

Wraps IModel + module/document traversal so SPMCP tools can target
Core without referencing Mendix.StudioPro.ExtensionsAPI. Heavier
modeling surfaces split into IDomainModelHost, IMicroflowAuthoringHost,
etc."
```

### Task 6: Define IDomainModelHost in Core

**Files:**
- Create: `src/Concord.Core/Interop/IDomainModelHost.cs`

- [ ] **Step 1: Write the interface**

`src/Concord.Core/Interop/IDomainModelHost.cs`:

```csharp
namespace Terminal.Interop;

public enum AttributeKind { String, Integer, LongType, Decimal, Boolean, DateTime, Enumeration, AutoNumber, HashString, Binary, Object }

public readonly record struct EntityRef(Guid Id, string QualifiedName);
public readonly record struct AttributeRef(Guid Id, string Name, AttributeKind Kind);
public readonly record struct AssociationRef(Guid Id, string Name, string ParentEntity, string ChildEntity, string AssociationType);

public record EntityShape(
    string Name,
    string ModuleName,
    IReadOnlyList<AttributeRef> Attributes,
    string? GeneralizationOf,
    IReadOnlyList<string> EventHandlers);

public record CreateEntityRequest(
    string ModuleName,
    string EntityName,
    string? Generalization,
    IReadOnlyList<(string Name, AttributeKind Kind, string? EnumerationQualifiedName)> Attributes,
    double X,
    double Y);

public record CreateAssociationRequest(
    string ModuleName,
    string Name,
    string ParentEntityQualifiedName,
    string ChildEntityQualifiedName,
    string Type,
    string ParentDeleteBehavior,
    string ChildDeleteBehavior);

public interface IDomainModelHost
{
    IReadOnlyList<EntityRef> ListEntities(ModuleId moduleId);
    EntityShape ReadEntity(EntityRef entity);
    EntityRef CreateEntity(CreateEntityRequest request);
    void DeleteEntity(EntityRef entity);
    void RenameEntity(EntityRef entity, string newName);
    void AddAttribute(EntityRef entity, string name, AttributeKind kind, string? enumerationQualifiedName);
    void RenameAttribute(AttributeRef attribute, string newName);
    void UpdateAttributeKind(AttributeRef attribute, AttributeKind newKind, string? enumerationQualifiedName);
    AssociationRef CreateAssociation(CreateAssociationRequest request);
    void DeleteAssociation(AssociationRef association);
    void RenameAssociation(AssociationRef association, string newName);
    void SetGeneralization(EntityRef entity, EntityRef parent);
    void RemoveGeneralization(EntityRef entity);
    void SetDocumentation(EntityRef entity, string documentation);
    void ArrangeDomainModel(ModuleId moduleId, string strategy);
}
```

The exact methods come from `MendixDomainModelTools.cs`'s public API (see Task 4's inventory). If a method discovered during refactor doesn't fit any of the seven interfaces, **add it here** (or to the most appropriate one) and document the addition in the spike notes.

- [ ] **Step 2: Build Core**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/Concord.Core/Interop/IDomainModelHost.cs
git commit -m "feat(core): add IDomainModelHost interop interface (W2)

Wraps entity, attribute, association, generalization, and arrange
operations from MendixDomainModelTools. Implementations land in each
host project in Phase 3."
```

### Task 7: Define the remaining five host interfaces

**Files:**
- Create: `src/Concord.Core/Interop/IPageGenerationHost.cs`
- Create: `src/Concord.Core/Interop/INavigationHost.cs`
- Create: `src/Concord.Core/Interop/IVersionControlHost.cs`
- Create: `src/Concord.Core/Interop/IUntypedModelHost.cs`
- Create: `src/Concord.Core/Interop/IMicroflowAuthoringHost.cs`

Each interface follows the same pattern as Tasks 5–6: wrap only what SPMCP uses, model parameters and return values as Core records (`PageGenerationRequest`, `NavigationProfileInfo`, etc.) so Studio Pro types never leak into Core.

- [ ] **Step 1: Define IPageGenerationHost**

`src/Concord.Core/Interop/IPageGenerationHost.cs`:

```csharp
namespace Terminal.Interop;

public record PageGenerationRequest(
    string ModuleName,
    string EntityQualifiedName,
    string? LayoutQualifiedName,
    string? NavigationCategory,
    bool GenerateOverview,
    bool GenerateEdit);

public record PageGenerationResult(bool Success, IReadOnlyList<string> CreatedPages, string? Error);

public interface IPageGenerationHost
{
    PageGenerationResult GenerateOverviewPages(PageGenerationRequest request);
    bool DeleteDocument(DocumentId document);
}
```

- [ ] **Step 2: Define INavigationHost**

`src/Concord.Core/Interop/INavigationHost.cs`:

```csharp
namespace Terminal.Interop;

public record NavigationItem(string Caption, string DocumentQualifiedName, string? IconQualifiedName);

public interface INavigationHost
{
    IReadOnlyList<NavigationItem> ListNavigation(string profileName);
    void AddNavigationItem(string profileName, NavigationItem item);
    void RemoveNavigationItem(string profileName, string caption);
    void SetNavigationItemUrl(string profileName, string caption, string microflowOrPageQualifiedName);
}
```

- [ ] **Step 3: Define IVersionControlHost**

`src/Concord.Core/Interop/IVersionControlHost.cs`:

```csharp
namespace Terminal.Interop;

public record VersionControlInfo(
    string? BranchName,
    string? CommitId,
    bool HasLocalChanges,
    IReadOnlyList<string> PendingChanges);

public interface IVersionControlHost
{
    VersionControlInfo Read();
    bool IsAvailable { get; }
}
```

- [ ] **Step 4: Define IUntypedModelHost**

`src/Concord.Core/Interop/IUntypedModelHost.cs`:

```csharp
namespace Terminal.Interop;

public interface IUntypedModelHost
{
    bool IsAvailable { get; }
    string ReadAsJson(DocumentId document);
    void WriteFromJson(DocumentId document, string json);
}
```

- [ ] **Step 5: Define IMicroflowAuthoringHost**

`src/Concord.Core/Interop/IMicroflowAuthoringHost.cs`:

```csharp
namespace Terminal.Interop;

public enum MicroflowAccessLevel { CheckPerOperation, AllowAll, ModuleSpecific }

public record MicroflowSummary(
    string QualifiedName,
    string Module,
    string Name,
    string? Documentation,
    MicroflowAccessLevel AccessLevel,
    IReadOnlyList<string> Parameters);

public record MicroflowActivitySummary(
    string ActivityType,
    string? Caption,
    string? TargetEntity,
    string? TargetMicroflow);

public record CreateMicroflowRequest(
    string ModuleName,
    string Name,
    IReadOnlyList<(string ParameterName, string TypeQualifiedName, bool IsList)> Parameters,
    string? ReturnTypeQualifiedName,
    MicroflowAccessLevel AccessLevel);

public interface IMicroflowAuthoringHost
{
    bool IsAvailable { get; }
    IReadOnlyList<MicroflowSummary> ListMicroflows(ModuleId? moduleFilter);
    MicroflowSummary? Read(string qualifiedName);
    IReadOnlyList<MicroflowActivitySummary> ReadActivities(string microflowQualifiedName);
    DocumentId Create(CreateMicroflowRequest request);
    void Delete(string microflowQualifiedName);
    void InsertActivityBefore(string microflowQualifiedName, string activityRef, MicroflowActivitySummary newActivity);
    void ModifyActivity(string microflowQualifiedName, string activityRef, IReadOnlyDictionary<string, string> changes);
    void SetUrl(string microflowQualifiedName, string url);
}
```

- [ ] **Step 6: Build Core**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add src/Concord.Core/Interop/IPageGenerationHost.cs \
        src/Concord.Core/Interop/INavigationHost.cs \
        src/Concord.Core/Interop/IVersionControlHost.cs \
        src/Concord.Core/Interop/IUntypedModelHost.cs \
        src/Concord.Core/Interop/IMicroflowAuthoringHost.cs
git commit -m "feat(core): add five remaining interop interfaces (W2)

IPageGenerationHost, INavigationHost, IVersionControlHost,
IUntypedModelHost, IMicroflowAuthoringHost. Together with IModelHost
and IDomainModelHost, these cover the Studio Pro service surface that
SPMCP tools call. Implementations follow in Phase 3."
```

### Task 8: Extend HostServices registry with the seven new accessors

**Files:**
- Modify: `src/Concord.Core/Interop/HostServices.cs`

- [ ] **Step 1: Write the failing test**

Add to `tests/Concord.Core.Tests/HostContextTests.cs` (extending the existing class):

```csharp
[Fact]
public void HostServices_ResolvesModelHost_AfterRegisterCalled()
{
    HostContext.Reset();
    HostServices.Reset();

    var fakeApp = new FakeAppHost();
    var fakeModel = new FakeModelHost();
    HostServices.Register(
        app: fakeApp,
        runConfigs: new FakeRunConfigsHost(),
        runState: new FakeRunStateHost(),
        moduleImport: new FakeModuleImportHost(),
        model: fakeModel,
        domainModel: new FakeDomainModelHost(),
        pageGeneration: new FakePageGenerationHost(),
        navigation: new FakeNavigationHost(),
        versionControl: new FakeVersionControlHost(),
        untypedModel: new FakeUntypedModelHost(),
        microflowAuthoring: new FakeMicroflowAuthoringHost());

    Assert.Same(fakeModel, HostServices.Model);
}

[Fact]
public void HostServices_Model_ThrowsBeforeRegister()
{
    HostServices.Reset();
    Assert.Throws<InvalidOperationException>(() => _ = HostServices.Model);
}
```

Add stub `Fake*` host classes under `tests/Concord.Core.Tests/Fakes/`, each implementing its interface with `throw new NotImplementedException()`.

- [ ] **Step 2: Run the test to see it fail to compile**

Run: `dotnet test tests/Concord.Core.Tests/Concord.Core.Tests.csproj`
Expected: build error — `HostServices.Register` doesn't have the new overload yet; `HostServices.Model` doesn't exist.

- [ ] **Step 3: Extend HostServices**

Edit `src/Concord.Core/Interop/HostServices.cs`. Add private fields, public accessors, and a new `Register` overload covering all 11 services. The existing 4-service `Register` overload stays (test compatibility); the new overload delegates to it for the first 4 and additionally captures the new 7.

```csharp
namespace Terminal.Interop;

public static class HostServices
{
    private static IStudioProAppHost? _app;
    private static IRunConfigurationsHost? _runConfigs;
    private static IRunStateHost? _runState;
    private static IModuleImportHost? _moduleImport;
    private static IModelHost? _model;
    private static IDomainModelHost? _domainModel;
    private static IPageGenerationHost? _pageGeneration;
    private static INavigationHost? _navigation;
    private static IVersionControlHost? _versionControl;
    private static IUntypedModelHost? _untypedModel;
    private static IMicroflowAuthoringHost? _microflowAuthoring;
    private static readonly object _gate = new();

    public static IStudioProAppHost App => _app ?? throw NotInitialized(nameof(IStudioProAppHost));
    public static IRunConfigurationsHost RunConfigurations => _runConfigs ?? throw NotInitialized(nameof(IRunConfigurationsHost));
    public static IRunStateHost RunState => _runState ?? throw NotInitialized(nameof(IRunStateHost));
    public static IModuleImportHost ModuleImport => _moduleImport ?? throw NotInitialized(nameof(IModuleImportHost));
    public static IModelHost Model => _model ?? throw NotInitialized(nameof(IModelHost));
    public static IDomainModelHost DomainModel => _domainModel ?? throw NotInitialized(nameof(IDomainModelHost));
    public static IPageGenerationHost PageGeneration => _pageGeneration ?? throw NotInitialized(nameof(IPageGenerationHost));
    public static INavigationHost Navigation => _navigation ?? throw NotInitialized(nameof(INavigationHost));
    public static IVersionControlHost VersionControl => _versionControl ?? throw NotInitialized(nameof(IVersionControlHost));
    public static IUntypedModelHost UntypedModel => _untypedModel ?? throw NotInitialized(nameof(IUntypedModelHost));
    public static IMicroflowAuthoringHost MicroflowAuthoring => _microflowAuthoring ?? throw NotInitialized(nameof(IMicroflowAuthoringHost));

    public static void Register(
        IStudioProAppHost app,
        IRunConfigurationsHost runConfigs,
        IRunStateHost runState,
        IModuleImportHost moduleImport)
    {
        lock (_gate)
        {
            _app = app; _runConfigs = runConfigs; _runState = runState; _moduleImport = moduleImport;
        }
    }

    public static void Register(
        IStudioProAppHost app,
        IRunConfigurationsHost runConfigs,
        IRunStateHost runState,
        IModuleImportHost moduleImport,
        IModelHost model,
        IDomainModelHost domainModel,
        IPageGenerationHost pageGeneration,
        INavigationHost navigation,
        IVersionControlHost versionControl,
        IUntypedModelHost untypedModel,
        IMicroflowAuthoringHost microflowAuthoring)
    {
        lock (_gate)
        {
            _app = app; _runConfigs = runConfigs; _runState = runState; _moduleImport = moduleImport;
            _model = model; _domainModel = domainModel;
            _pageGeneration = pageGeneration; _navigation = navigation;
            _versionControl = versionControl; _untypedModel = untypedModel;
            _microflowAuthoring = microflowAuthoring;
        }
    }

    internal static void Reset()
    {
        lock (_gate)
        {
            _app = null; _runConfigs = null; _runState = null; _moduleImport = null;
            _model = null; _domainModel = null;
            _pageGeneration = null; _navigation = null;
            _versionControl = null; _untypedModel = null; _microflowAuthoring = null;
        }
    }

    private static InvalidOperationException NotInitialized(string serviceName)
        => new($"HostServices.{serviceName} was accessed before HostServices.Register was called. " +
               "Each host DLL must call Register from its MEF activation.");
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Concord.Core.Tests/Concord.Core.Tests.csproj`
Expected: green.

- [ ] **Step 5: Run the full solution test**

Run: `dotnet test Terminal.sln`
Expected: `Terminal.Tests` still green (existing 4-arg `Register` overload preserved).

- [ ] **Step 6: Commit**

```bash
git add src/Concord.Core/Interop/HostServices.cs \
        tests/Concord.Core.Tests/
git commit -m "feat(core): extend HostServices with 7 new accessors (W2)

Adds Model, DomainModel, PageGeneration, Navigation, VersionControl,
UntypedModel, MicroflowAuthoring. Existing 4-arg Register overload
stays so Host10x/Host11x can adopt the 11-arg overload incrementally.
Tests cover the new accessors and the pre-Register throw behavior."
```

---

## Phase 3 — Host implementations of the new Interop interfaces

Each host project (Host10x + Host11x) gets 7 new files under `Interop/`, one per interface. The implementations differ between hosts because the underlying ExtensionsAPI surface drifts. The pattern for each: take Studio Pro services via constructor (eventually wired through MEF inside `HostXEntry`), and translate between Studio Pro types and the Core records defined in Phase 2.

### Task 9: Implement IModelHost on both hosts

**Files:**
- Create: `src/Concord.Host11x/Interop/ModelHost11x.cs`
- Create: `src/Concord.Host10x/Interop/ModelHost10x.cs`

- [ ] **Step 1: Implement ModelHost11x**

`src/Concord.Host11x/Interop/ModelHost11x.cs`:

```csharp
namespace Concord.Host11x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Terminal.Interop;

public sealed class ModelHost11x : IModelHost
{
    private readonly IModel _model;

    public ModelHost11x(IModel model) => _model = model;

    public ProjectInfo GetProjectInfo()
    {
        var project = _model.Root as IProject ?? throw new InvalidOperationException("No project open");
        return new ProjectInfo(
            Name: System.IO.Path.GetFileNameWithoutExtension(project.DirectoryPath),
            DirectoryPath: project.DirectoryPath,
            MendixVersion: project.ServerVersion?.ToString(),
            AppId: project.AppId);
    }

    public IReadOnlyList<ModuleId> ListModules()
        => _model.Root.GetModules()
                      .Select(m => new ModuleId(m.Id, m.Name))
                      .ToList();

    public ModuleId? GetModuleByName(string moduleName)
    {
        var module = _model.Root.GetModules().FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        return module is null ? null : new ModuleId(module.Id, module.Name);
    }

    public IReadOnlyList<DocumentId> ListModuleDocuments(ModuleId moduleId, string? documentTypeFilter = null)
    {
        var module = ResolveModule(moduleId);
        var docs = module.GetDocuments();
        if (!string.IsNullOrEmpty(documentTypeFilter))
            docs = docs.Where(d => d.GetType().Name.Contains(documentTypeFilter, StringComparison.OrdinalIgnoreCase));
        return docs.Select(d => new DocumentId(d.Id, d.QualifiedName.FullName)).ToList();
    }

    public DocumentId? GetDocumentByQualifiedName(string qualifiedName)
    {
        var doc = _model.Root.GetAllDocuments().FirstOrDefault(d => string.Equals(d.QualifiedName.FullName, qualifiedName, StringComparison.OrdinalIgnoreCase));
        return doc is null ? null : new DocumentId(doc.Id, doc.QualifiedName.FullName);
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        // Studio Pro 11.x exposes saving via the IModel transaction surface;
        // confirm the exact method during implementation (Save / SaveAll /
        // CommitTransaction). Wrap with try/catch and translate any failure
        // into an InvalidOperationException with the underlying message.
        await Task.Yield();
        // TODO replace with the verified call:
        // _model.Save();
        throw new NotImplementedException("Verify the 11.6.2 IModel save method during implementation");
    }

    private IModule ResolveModule(ModuleId moduleId)
        => _model.Root.GetModules().FirstOrDefault(m => m.Id == moduleId.Value)
           ?? throw new InvalidOperationException($"Module '{moduleId.Name}' (id={moduleId.Value}) not found");
}
```

The `SaveAsync` body is left as `NotImplementedException` because the precise 11.6.2 API for save needs verification at implementation time. If a test exercises it, the implementer fills it in then; no need to ship blocking unknowns.

- [ ] **Step 2: Implement ModelHost10x**

Copy ModelHost11x into `src/Concord.Host10x/Interop/ModelHost10x.cs` and adjust for any 10.21.1 API drift discovered. The MCPExtension `backport-10x/` reference at `src/Concord.Core/Spmcp/backport-10x/reference/` is the primary source for drift mapping — read it whenever a method call fails to compile against 10.21.1.

The class shell:

```csharp
namespace Concord.Host10x.Interop;

using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Terminal.Interop;

public sealed class ModelHost10x : IModelHost
{
    private readonly IModel _model;

    public ModelHost10x(IModel model) => _model = model;

    // (Body mirrors ModelHost11x; if any call doesn't resolve on 10.21.1, consult
    //  src/Concord.Core/Spmcp/backport-10x/reference/Tools/MendixAdditionalTools.cs
    //  for the equivalent call pattern.)

    public ProjectInfo GetProjectInfo() { /* same shape as 11x */ throw new NotImplementedException(); }
    public IReadOnlyList<ModuleId> ListModules() => throw new NotImplementedException();
    public ModuleId? GetModuleByName(string moduleName) => throw new NotImplementedException();
    public IReadOnlyList<DocumentId> ListModuleDocuments(ModuleId moduleId, string? documentTypeFilter = null) => throw new NotImplementedException();
    public DocumentId? GetDocumentByQualifiedName(string qualifiedName) => throw new NotImplementedException();
    public Task SaveAsync(CancellationToken ct = default) => throw new NotImplementedException();
}
```

Fill bodies in iteratively: copy from 11x → build → fix any compile error using `backport-10x/reference/` as a guide → loop until 10x project builds.

- [ ] **Step 3: Build both hosts**

Run:
```bash
dotnet build src/Concord.Host11x/Concord.Host11x.csproj
dotnet build src/Concord.Host10x/Concord.Host10x.csproj
```

Expected: both succeed. If `Host11x` already wires services via MEF in `Host11xEntry`, the new `ModelHost11x` doesn't get auto-registered yet — that wiring is Task 16. Build-only is enough for now.

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Host11x/Interop/ModelHost11x.cs \
        src/Concord.Host10x/Interop/ModelHost10x.cs
git commit -m "feat(host): implement IModelHost on both hosts (W2)

ModelHost11x targets 11.6.2; ModelHost10x mirrors the shape against
10.21.1 with backport-10x/reference/ as the API-drift guide. SaveAsync
left as NotImplementedException pending verification at MEF wire-up."
```

### Task 10: Implement IDomainModelHost on both hosts

**Files:**
- Create: `src/Concord.Host11x/Interop/DomainModelHost11x.cs`
- Create: `src/Concord.Host10x/Interop/DomainModelHost10x.cs`

The pattern mirrors Task 9: 11x is the primary implementation; 10x copies and adjusts. Methods correspond 1:1 with `IDomainModelHost` from Task 6.

- [ ] **Step 1: Implement DomainModelHost11x**

The bodies for `ListEntities`, `ReadEntity`, `CreateEntity`, `DeleteEntity`, `RenameEntity`, attribute and association ops come directly from `src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs` — find the matching tool method and lift its body into the host implementation. Adapt the body to:
  - Accept the Core record parameter type instead of `JsonObject parameters`.
  - Return the Core record / void instead of `Task<string>` JSON envelopes.
  - Throw `InvalidOperationException` on failure instead of returning a JSON error envelope (the tool layer in Phase 4 wraps these back into JSON responses).

Concrete example for `CreateEntity`:

```csharp
public EntityRef CreateEntity(CreateEntityRequest request)
{
    var module = _model.Root.GetModules().First(m => m.Name == request.ModuleName);
    var domainModel = module.DomainModel
        ?? throw new InvalidOperationException($"Module '{request.ModuleName}' has no domain model");
    var entity = domainModel.AddEntity();
    entity.Name = request.EntityName;
    entity.Location = new System.Drawing.PointF((float)request.X, (float)request.Y);

    if (!string.IsNullOrEmpty(request.Generalization))
    {
        var parent = _model.Root.GetAllDocuments()
            .OfType<Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.IEntity>()
            .First(e => string.Equals(e.QualifiedName.FullName, request.Generalization, StringComparison.OrdinalIgnoreCase));
        entity.Generalization = new Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.Generalization(parent);
    }

    foreach (var (name, kind, enumQn) in request.Attributes)
        AddAttribute(new EntityRef(entity.Id, entity.QualifiedName.FullName), name, kind, enumQn);

    return new EntityRef(entity.Id, entity.QualifiedName.FullName);
}
```

The other methods follow the same translation pattern. Where the source tool method has 200+ lines of JSON arg parsing, **drop that** — the Phase 4 tool wrapper handles parsing; the host only needs to do the modeling work.

- [ ] **Step 2: Mirror to DomainModelHost10x**

Copy DomainModelHost11x → DomainModelHost10x → fix compile errors using `backport-10x/reference/Tools/MendixDomainModelTools.cs` as the drift reference. Common drift hits: model traversal methods may be `IModel.AllDomainModels()` on 10.x vs `IModel.Root.GetAllDocuments().OfType<>` on 11.x.

- [ ] **Step 3: Build both hosts**

Run:
```bash
dotnet build src/Concord.Host11x/Concord.Host11x.csproj
dotnet build src/Concord.Host10x/Concord.Host10x.csproj
```

Expected: both `Build succeeded.` Compile errors here are *load-bearing* — they're the actual API-drift findings. Resolve each before committing.

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Host11x/Interop/DomainModelHost11x.cs \
        src/Concord.Host10x/Interop/DomainModelHost10x.cs
git commit -m "feat(host): implement IDomainModelHost on both hosts (W2)

Entity/attribute/association/generalization/arrange ops, ported from
MendixDomainModelTools.cs (11.x) and backport-10x/reference (10.x).
Bodies focus on modeling logic; JSON parsing happens in Phase 4 tool
wrappers."
```

### Task 11: Implement the five remaining interfaces on both hosts

**Files:**
- Create: `src/Concord.Host11x/Interop/PageGenerationHost11x.cs` and `…Host10x.cs`
- Create: `src/Concord.Host11x/Interop/NavigationHost11x.cs` and `…Host10x.cs`
- Create: `src/Concord.Host11x/Interop/VersionControlHost11x.cs` and `…Host10x.cs`
- Create: `src/Concord.Host11x/Interop/UntypedModelHost11x.cs` and `…Host10x.cs`
- Create: `src/Concord.Host11x/Interop/MicroflowAuthoringHost11x.cs` and `…Host10x.cs`

Same pattern as Tasks 9–10. For each: implement 11.x first by lifting bodies from SPMCP source; mirror to 10.x using `backport-10x/reference/` as drift guide; build both hosts after each pair.

`IVersionControlHost` and `IUntypedModelHost` need special handling for 10.x: those services may not exist on 10.21.1 (the MCPExtension's `backport-10x/reference/Tools/MendixAdditionalTools.cs` constructor doesn't take them). If a service isn't available on 10.x:

```csharp
// VersionControlHost10x.cs
public sealed class VersionControlHost10x : IVersionControlHost
{
    public bool IsAvailable => false;
    public VersionControlInfo Read() => throw new NotSupportedException(
        "Version control inspection is not exposed via the 10.21.1 ExtensionsAPI surface. " +
        "Use the Mendix UI Team menu directly.");
}
```

This lets tools that depend on the service return a structured `{ "escalation": "manual", "manual_steps": [...] }` response on 10.x without crashing.

- [ ] **Step 1: Implement PageGenerationHost (both hosts)**

11x: lift body from `MendixAdditionalTools.GenerateOverviewPages` and the page-deletion handler. 10x: mirror; the `IPageGenerationService` was available on 10.21.1 per the backport reference.

- [ ] **Step 2: Implement NavigationHost (both hosts)**

11x: lift body from `MendixAdditionalTools.ManageNavigation` (the action dispatcher). Split into the four `INavigationHost` methods. 10x: mirror.

- [ ] **Step 3: Implement VersionControlHost (both hosts)**

11x: lift body from `MendixAdditionalTools.ReadVersionControl`. 10x: stub with `IsAvailable=false` if the 10.21.1 ExtensionsAPI doesn't expose `IVersionControlService` (verify via `Select-String -Path src/Concord.Core/Spmcp/backport-10x/reference -Pattern "IVersionControlService"`).

- [ ] **Step 4: Implement UntypedModelHost (both hosts)**

11x: lift body from anywhere `IUntypedModelAccessService` is used in SPMCP. 10x: stub with `IsAvailable=false` if not available on 10.21.1.

- [ ] **Step 5: Implement MicroflowAuthoringHost (both hosts)**

This is the largest single host implementation — covers `ListMicroflows`, `CreateMicroflow`, `CreateMicroflowActivity`, `ReadMicroflowDetails`, `ModifyMicroflowActivity`, `InsertBeforeActivity`, `SetMicroflowUrl`, `ReadNanoflowDetails`, `ListNanoflows`, etc. Bodies come from `MendixAdditionalTools.cs` lines 554-867 and 1691-7016 (per Task 4's inventory). 10x mirrors using `backport-10x/reference/`. Some methods (the activity-modification family) may genuinely differ between 10.x and 11.x — capture the drift in spike notes.

- [ ] **Step 6: Build both hosts**

Run:
```bash
dotnet build src/Concord.Host11x/Concord.Host11x.csproj
dotnet build src/Concord.Host10x/Concord.Host10x.csproj
```

Expected: both succeed.

- [ ] **Step 7: Commit**

```bash
git add src/Concord.Host11x/Interop/ src/Concord.Host10x/Interop/
git commit -m "feat(host): implement remaining 5 interop hosts on both targets (W2)

PageGeneration, Navigation, VersionControl, UntypedModel,
MicroflowAuthoring. Bodies lifted from SPMCP Tools/ for 11.x and
mirrored via backport-10x/reference/ for 10.x. VersionControl and
UntypedModel report IsAvailable=false on 10.x if the underlying
ExtensionsAPI service isn't exposed on 10.21.1; corresponding tools
return escalation=manual on that host."
```

---

## Phase 4 — Refactor SPMCP tools to depend on Core Interop

SPMCP tools currently take Studio Pro services in their constructors (`IModel`, `IPageGenerationService`, etc.) and ship `Task<object>` / `Task<string>` JSON envelopes. The refactor:
  1. Replaces the constructor parameters with `HostServices` reads (or a single `IServiceProvider` injected for any legacy escape hatch).
  2. Replaces direct Studio Pro type usage with the Core Interop interfaces from Phase 2.
  3. Keeps the JSON-in/JSON-out signatures so the tool-registration shape is unchanged.

Because SPMCP's tool classes are huge (`MendixAdditionalTools` 10,211 lines; `MendixDomainModelTools` 4,871 lines), the refactor happens in slices. Each slice = one tool method (or a small related group) + a verification build. Each slice gets its own commit so blame stays useful and rollback is easy.

**Subagent-driven approach is strongly recommended for Phase 4** — each slice is independent enough to delegate, and Phase 4 is where the conversation context will balloon otherwise.

### Task 12: Make Core compile the SPMCP source (move from "imported but unused" to "imported and compiled")

**Files:**
- Modify: `src/Concord.Core/Concord.Core.csproj` (add Logging/DI package refs; remove the `Compile Remove="Spmcp/**"` line)
- Modify: SPMCP `.cs` namespaces to bring them under `Terminal.Spmcp` (vs. the imported `MCPExtension.*`)

- [ ] **Step 1: Add the package references SPMCP code needs**

Edit `src/Concord.Core/Concord.Core.csproj`. Add to the existing `<ItemGroup>` containing package references:

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.*" />
```

These are interface-only packages — they don't add a Studio Pro reference.

- [ ] **Step 2: Remove the `Compile Remove` (if any) that excluded Spmcp**

If Task 2 added `<Compile Remove="Spmcp/**/*.cs" />`, delete that line now. Confirm with:

```bash
dotnet build src/Concord.Core/Concord.Core.csproj 2>&1 | head -50
```

Expected at this point: **compile errors** — SPMCP files reference `Mendix.StudioPro.ExtensionsAPI.*` namespaces that Core doesn't have. That's by design; Phase 4 fixes them.

- [ ] **Step 3: Rename the SPMCP namespaces from `MCPExtension.*` to `Terminal.Spmcp.*`**

Run a one-shot rename across the kept SPMCP source. In PowerShell:

```powershell
Get-ChildItem -Path src/Concord.Core/Spmcp -Filter *.cs -Recurse | ForEach-Object {
  (Get-Content $_.FullName -Raw) `
    -replace 'namespace MCPExtension\.Tools', 'namespace Terminal.Spmcp.Tools' `
    -replace 'namespace MCPExtension\.Handlers', 'namespace Terminal.Spmcp.Handlers' `
    -replace 'namespace MCPExtension\.Utils', 'namespace Terminal.Spmcp.Utils' `
    -replace 'using MCPExtension\.Tools', 'using Terminal.Spmcp.Tools' `
    -replace 'using MCPExtension\.Handlers', 'using Terminal.Spmcp.Handlers' `
    -replace 'using MCPExtension\.Utils', 'using Terminal.Spmcp.Utils' `
    | Set-Content $_.FullName -NoNewline
}
```

This is mechanical — no semantic change. Verify with `Select-String -Path src/Concord.Core/Spmcp -Pattern "MCPExtension\." -Recurse` and confirm only documentation/comments remain (if any).

- [ ] **Step 4: Don't try to build yet — Phase 4 expects compile errors at this point**

The Studio Pro `using` statements (`using Mendix.StudioPro.ExtensionsAPI.Model;` etc.) still produce CS0246 errors. That's expected — the next tasks resolve them by substituting Interop interface calls. Build will succeed at the end of Task 17, not here.

- [ ] **Step 5: Commit the namespace rename**

```bash
git add src/Concord.Core/Concord.Core.csproj src/Concord.Core/Spmcp/
git commit -m "build(spmcp): bring SPMCP source under Terminal.Spmcp namespace (W2)

Mechanical namespace rename (MCPExtension.* → Terminal.Spmcp.*). Adds
Microsoft.Extensions.Logging.Abstractions + DependencyInjection.Abstractions
package refs that the SPMCP code uses. Core does not yet build —
Phase 4 substitutes Studio Pro type usage with Core Interop calls.
This commit isolates the namespace move so the next tasks' diffs
focus on the semantic refactor."
```

### Task 13: Refactor MendixAdditionalTools — read paths first (slices 1–6)

**Files:**
- Modify: `src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs`

Read paths are easier than write paths and exercise the Interop surface fully. Slices in this task:
  1. Constructor + DI plumbing → take `IServiceProvider` only as a passthrough; drop direct Studio Pro service params.
  2. `SaveData`, `ReadSampleData` — touch IModel only.
  3. `GenerateSampleData` — touches IModel + microflow authoring.
  4. `ListMicroflows`, `ReadMicroflowDetails`, `ListNanoflows`, `ReadNanoflowDetails` — read paths over microflow authoring.
  5. `GetStudioProLogs`, `CheckProjectErrors`, `GetLastError`, `DebugInfo` — read-only diagnostics.
  6. `ListAvailableTools` — pure introspection, no service deps.

- [ ] **Step 1: Refactor the constructor**

Replace the original constructor:

```csharp
public MendixAdditionalTools(
    IModel model,
    ILogger<MendixAdditionalTools> logger,
    IPageGenerationService pageGenerationService,
    INavigationManagerService navigationManagerService,
    IServiceProvider serviceProvider,
    string? projectDirectory = null)
{
    _model = model;
    _logger = logger;
    _pageGenerationService = pageGenerationService;
    _navigationManagerService = navigationManagerService;
    _serviceProvider = serviceProvider;
    _projectDirectory = projectDirectory;
    _versionControlService = serviceProvider.GetService<IVersionControlService>();
    _untypedModelService = serviceProvider.GetService<IUntypedModelAccessService>();
}
```

With:

```csharp
public MendixAdditionalTools(ILogger<MendixAdditionalTools> logger, string? projectDirectory = null)
{
    _logger = logger;
    _projectDirectory = projectDirectory;
}
```

Delete the private fields that held Studio Pro service references (`_model`, `_pageGenerationService`, `_navigationManagerService`, `_versionControlService`, `_untypedModelService`, `_serviceProvider`). Methods will read from `HostServices.*` instead.

Update the `using` block at the top of the file. Remove Mendix imports; add `using Terminal.Interop;`.

- [ ] **Step 2: Refactor SaveData and ReadSampleData**

The original `SaveData` reads `_model.Root` to identify modules and the project directory. Replace with `HostServices.Model.GetProjectInfo()` and `HostServices.Model.ListModules()`.

Example refactor pattern (apply throughout):

```csharp
// Before:
var project = _model.Root as IProject;
var projectDir = project?.DirectoryPath;

// After:
var info = HostServices.Model.GetProjectInfo();
var projectDir = info.DirectoryPath;
```

Where the original calls `_model.GetModuleDocuments<IConstant>(module)`, replace with:

```csharp
var docs = HostServices.Model.ListModuleDocuments(moduleId, documentTypeFilter: "Constant");
```

(The `IModelHost.ListModuleDocuments` filter param matches the type name suffix — implementations match case-insensitively.)

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj 2>&1 | Select-Object -First 60`

Expected: fewer CS0246 errors than before; the touched methods compile. Outstanding errors in other tool methods are normal — they'll resolve in subsequent slices.

- [ ] **Step 4: Refactor GenerateSampleData**

This touches microflow authoring. Replace `IMicroflowService microflowService` (originally received via `serviceProvider.GetService<>()`) with `HostServices.MicroflowAuthoring`. Tool's response JSON shape is unchanged.

- [ ] **Step 5: Refactor microflow read paths (ListMicroflows, ReadMicroflowDetails, ListNanoflows, ReadNanoflowDetails)**

These methods originally walk `_model.Root.GetAllDocuments().OfType<IMicroflow>()`. Replace with `HostServices.MicroflowAuthoring.ListMicroflows(moduleFilter: …)` and `HostServices.MicroflowAuthoring.Read(qualifiedName)` + `ReadActivities`. The bulk of the method becomes a JSON serializer over the returned `MicroflowSummary` records — much shorter than the original.

- [ ] **Step 6: Refactor diagnostic methods (GetStudioProLogs, CheckProjectErrors, GetLastError, DebugInfo, ListAvailableTools)**

`GetStudioProLogs` reads files from `<project-dir>/log/` — replace `_model.Root.DirectoryPath` with `HostServices.Model.GetProjectInfo().DirectoryPath`. `CheckProjectErrors` calls into the model's consistency-check service — wire to a new `IModelHost.CheckConsistency()` method (extend the interface in a sub-commit, implement in both hosts) and have the tool route through that. If the underlying 10.21.1 ExtensionsAPI doesn't expose consistency-check, `ModelHost10x.CheckConsistency()` returns a structured `{ "escalation": "manual" }` response. `GetLastError`/`SetLastError` are static fields, no Studio Pro deps — already compile after the using block is fixed. `DebugInfo` enumerates services; rewrite to enumerate `HostServices.*` accessors. `ListAvailableTools` is pure reflection — no host deps after the namespace move.

- [ ] **Step 7: Build to verify slices 1–6 compile**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj 2>&1 | Select-Object -First 80`

Expected: all errors should be in the **write-path** methods (Phase 4 Task 14 fixes those). If a read-path method still errors, fix before committing.

- [ ] **Step 8: Commit slices 1–6**

```bash
git add src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs
git commit -m "refactor(spmcp): route read-path tools through HostServices (W2)

MendixAdditionalTools slices 1-6 (constructor + sample data read +
microflow read + diagnostics). Studio Pro IModel,
IPageGenerationService, INavigationManagerService, etc. usages
replaced with HostServices.Model / .MicroflowAuthoring / .Navigation
reads. Write paths still TODO; tracked in Task 14."
```

### Task 14: Refactor MendixAdditionalTools — write paths (slices 7–14)

**Files:**
- Modify: `src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs` (continued)

Each slice ports one method group. Same pattern as Task 13 — replace direct Studio Pro service calls with `HostServices.*`. Commits per slice (or batched 2-3 at a time when slices touch related areas).

Slice list:
  7. `GenerateOverviewPages`, `DeleteDocument` (page generation) → `HostServices.PageGeneration`.
  8. `CreateMicroflow`, `CreateMicroflowWithService` → `HostServices.MicroflowAuthoring.Create`.
  9. `CreateMicroflowActivity`, `CreateMicroflowActivitiesSequence` → `HostServices.MicroflowAuthoring.InsertActivityBefore` (+ a new `AppendActivity` method if needed).
  10. `SetupDataImport` → composite; uses `MicroflowAuthoring` + `DomainModel`.
  11. `ListJavaActions`, `ReadRuntimeSettings`, `SetRuntimeSettings`, `ReadConfigurations`, `SetConfiguration` → settings/config; either fits in `IModelHost` or extend it with a `ReadSettings`/`WriteSettings` pair (add to interface in Core, implement in both hosts; commit the interface extension separately).
  12. `ReadVersionControl` → `HostServices.VersionControl`.
  13. `SetMicroflowUrl`, `ExcludeDocument` → `HostServices.MicroflowAuthoring.SetUrl` + `HostServices.Model.SetExcluded` (extend interface if needed).
  14. `ListRules`, `ReadSecurityInfo`, `ReadEntityAccessRules`, `ReadMicroflowSecurity`, `AuditSecurity` → security; extend `IModelHost` or define a new `ISecurityHost` if the surface is wider than 2-3 methods (defer the new-interface decision to implementation time).

- [ ] **Step 1: Refactor slices 7–8 (page gen + microflow create) and commit**

For each slice: edit the method body to call `HostServices.*`. Verify with `dotnet build src/Concord.Core/Concord.Core.csproj` (expect remaining errors only in untouched slices).

Commit:
```bash
git add -A
git commit -m "refactor(spmcp): route page-gen + microflow-create tools through HostServices (W2)"
```

- [ ] **Step 2: Refactor slices 9–10 (microflow activities + sample-import composite) and commit**

```bash
git add -A
git commit -m "refactor(spmcp): route microflow-activity + sample-import tools through HostServices (W2)"
```

- [ ] **Step 3: Refactor slices 11–12 (settings/config + version control) and commit**

If slice 11 requires extending `IModelHost` (e.g., `ReadRuntimeSettings`, `WriteRuntimeSettings`), split the interface change into its own micro-commit:

```bash
git add src/Concord.Core/Interop/IModelHost.cs \
        src/Concord.Host11x/Interop/ModelHost11x.cs \
        src/Concord.Host10x/Interop/ModelHost10x.cs
git commit -m "feat(core): extend IModelHost with runtime-settings + configurations surface (W2)"
git add -A
git commit -m "refactor(spmcp): route settings/config + version-control tools through HostServices (W2)"
```

- [ ] **Step 4: Refactor slices 13–14 (URL + security) and commit**

Per Step 1 pattern. If `ISecurityHost` is introduced, do the same split-commit pattern as Step 3.

- [ ] **Step 5: Refactor the remaining "miscellaneous" methods (lines 7374-10211 of original file)**

`ManageNavigation`, `CheckVariableName`, `ModifyMicroflowActivity`, `InsertBeforeActivity`, `QueryModelElements`, etc. — same pattern, fewer surprises. Commit one slice at a time.

- [ ] **Step 6: Final build for MendixAdditionalTools**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj 2>&1 | Select-Object -First 30`

Expected: only errors remaining are in `MendixDomainModelTools.cs` (Task 15) and any handler files (Task 16).

### Task 15: Refactor MendixDomainModelTools

**Files:**
- Modify: `src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs`

Apply the same slice-by-slice refactor as Task 14. Slices map to the public method list captured in the inventory:

  - Constructor → `(ILogger<MendixDomainModelTools> logger)` only.
  - Read paths: `ListModules`, `ListConstants`, `ListEnumerations`, `ReadProjectInfo`, `ReadDomainModel`, `GetAvailableEntityTypes`, `GetEntityTypeInfo`, `QueryAssociations`.
  - Create paths: `CreateModule`, `CreateEntity`, `CreateMultipleEntities`, `CreateAssociation`, `CreateMultipleAssociations`, `CreateDomainModelFromSchema`, `CreateConstant`, `CreateEnumeration`.
  - Update paths: `AddAttribute`, `SetEntityGeneralization`, `RemoveEntityGeneralization`, `AddEventHandler`, `SetCalculatedAttribute`, `UpdateAttribute`, `UpdateAssociation`, `UpdateConstant`, `UpdateEnumeration`, `SetDocumentation`, `ConfigureSystemAttributes`, `ManageFolders`, `CopyModelElement`.
  - Rename paths: `RenameEntity`, `RenameAttribute`, `RenameAssociation`, `RenameDocument`, `RenameModule`, `RenameEnumerationValue`.
  - Delete + arrange: `DeleteModelElement`, `ArrangeDomainModel`.
  - Diagnostics: `CheckModel`, `DiagnoseAssociations`, `ValidateName`, `GetLastError`, `ListAvailableTools`.

Commit pattern matches Task 14 — one slice per commit, with interface-extension commits split out when an extension is needed.

- [ ] **Step 1: Refactor constructor + read paths, commit**

- [ ] **Step 2: Refactor create paths, commit**

- [ ] **Step 3: Refactor update paths, commit**

- [ ] **Step 4: Refactor rename paths, commit (the rename family is reference-safe and worth grouping)**

- [ ] **Step 5: Refactor delete + arrange + diagnostics, commit**

- [ ] **Step 6: Final build**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj`

Expected: only errors remaining are in `Handlers/*.cs` (Task 16).

### Task 16: Refactor SPMCP Handlers

**Files:**
- Modify: `src/Concord.Core/Spmcp/Handlers/AssociationDiagnosticHandler.cs`
- Modify: `src/Concord.Core/Spmcp/Handlers/DebugHandler.cs`
- Modify: `src/Concord.Core/Spmcp/Handlers/DeleteModelHandler.cs`
- Modify: `src/Concord.Core/Spmcp/Handlers/GenerateOverviewHandler.cs`
- Modify: `src/Concord.Core/Spmcp/Handlers/ListMicroflowsHandler.cs`
- Modify: `src/Concord.Core/Spmcp/Handlers/ReadMicroflowActivitiesHandler.cs`
- Modify: `src/Concord.Core/Spmcp/Handlers/ReadModelHandler.cs`
- Modify: `src/Concord.Core/Spmcp/Handlers/SaveDataHandler.cs`
- Modify: `src/Concord.Core/Spmcp/Handlers/WriteModelHandler.cs`

Handlers are lower-level than tools — they're the inner workers the tools delegate to. They have the same Studio Pro coupling.

- [ ] **Step 1: Inspect each handler's Studio Pro surface and refactor**

For each `Handlers/*.cs`, follow the Task 13 pattern: drop Studio Pro params from the constructor, replace direct service calls with `HostServices.*`. Most handlers can route through one or two host interfaces.

- [ ] **Step 2: Build Core**

Run: `dotnet build src/Concord.Core/Concord.Core.csproj`

Expected: **`Build succeeded.`** This is the milestone for Phase 4 — Core no longer references `Mendix.StudioPro.ExtensionsAPI.*`.

- [ ] **Step 3: Confirm no Mendix package leak**

Run: `dotnet list src/Concord.Core/Concord.Core.csproj package`

Expected: the listed packages contain `Eto.Forms`, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `System.Text.Json` — and **no** `Mendix.StudioPro.ExtensionsAPI`.

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Core/Spmcp/Handlers/
git commit -m "refactor(spmcp): route handlers through HostServices (W2)

All 9 handler classes (AssociationDiagnostic, Debug, DeleteModel,
GenerateOverview, ListMicroflows, ReadMicroflowActivities, ReadModel,
SaveData, WriteModel) now consume HostServices.Model /.DomainModel /
.MicroflowAuthoring / etc. instead of Studio Pro services directly.
Concord.Core no longer references Mendix.StudioPro.ExtensionsAPI."
```

### Task 17: Run the full test suite against the refactored Core

**Files:**
- Modify: `tests/Concord.Core.Tests/` (add fakes + tool exercises if needed)

Existing tests in `Terminal.Tests` won't exercise SPMCP. But Core tests should at least confirm the refactored tools instantiate and dispatch correctly with fake hosts.

- [ ] **Step 1: Write a smoke test that instantiates each tool class and calls one read method**

`tests/Concord.Core.Tests/SpmcpSmokeTests.cs`:

```csharp
namespace Concord.Core.Tests;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using Terminal.Interop;
using Terminal.Spmcp.Tools;
using Xunit;

public class SpmcpSmokeTests
{
    public SpmcpSmokeTests()
    {
        HostServices.Reset();
        HostServices.Register(
            app: new Fakes.FakeAppHost(),
            runConfigs: new Fakes.FakeRunConfigsHost(),
            runState: new Fakes.FakeRunStateHost(),
            moduleImport: new Fakes.FakeModuleImportHost(),
            model: new Fakes.FakeModelHost(),
            domainModel: new Fakes.FakeDomainModelHost(),
            pageGeneration: new Fakes.FakePageGenerationHost(),
            navigation: new Fakes.FakeNavigationHost(),
            versionControl: new Fakes.FakeVersionControlHost(),
            untypedModel: new Fakes.FakeUntypedModelHost(),
            microflowAuthoring: new Fakes.FakeMicroflowAuthoringHost());
    }

    [Fact]
    public async Task MendixAdditionalTools_GetLastError_ReturnsJson()
    {
        var tools = new MendixAdditionalTools(NullLogger<MendixAdditionalTools>.Instance);
        var result = await tools.GetLastError(new JsonObject());
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MendixDomainModelTools_ListModules_HitsModelHost()
    {
        var tools = new MendixDomainModelTools(NullLogger<MendixDomainModelTools>.Instance);
        var result = await tools.ListModules(new JsonObject());
        result.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Implement the fakes**

Each `Fake*Host` returns canned data sufficient for the smoke tests (e.g., `FakeModelHost.ListModules()` returns one synthetic `ModuleId(Guid.Empty, "TestModule")`).

- [ ] **Step 3: Run tests**

Run: `dotnet test Terminal.sln`
Expected: previous 245 still green; new SPMCP smoke tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/Concord.Core.Tests/SpmcpSmokeTests.cs tests/Concord.Core.Tests/Fakes/
git commit -m "test(core): SPMCP tools instantiate + dispatch through fake hosts (W2)

Smoke covers MendixAdditionalTools + MendixDomainModelTools using
FakeModelHost / FakeDomainModelHost / etc. Confirms the Phase 4
refactor's wiring works end-to-end before Phase 5 wraps tools in
ToolCatalog registrations."
```

---

## Phase 5 — ToolCatalog, ITool, and version-aware registration

With Core compiling and SPMCP tools refactored, the next move is wiring them through a registry that selects the right surface per `TargetMode` and family-toggle setting.

### Task 18: Define ITool, ToolFamily, and ToolCatalog

**Files:**
- Create: `src/Concord.Core/Mcp/ITool.cs`
- Create: `src/Concord.Core/Mcp/ToolFamily.cs`
- Create: `src/Concord.Core/Mcp/ToolCatalog.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Concord.Core.Tests/ToolCatalogTests.cs`:

```csharp
namespace Concord.Core.Tests;

using FluentAssertions;
using System.Text.Json.Nodes;
using Terminal;
using Terminal.Mcp;
using Xunit;

public class ToolCatalogTests
{
    private static ITool MakeTool(string name, ToolFamily family) =>
        new SimpleTool(name, family, _ => Task.FromResult<object>("{}"));

    [Fact]
    public void RegisteredTools_Visible_When_TargetIs10x()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        catalog.Register(MakeTool("create_entity", ToolFamily.DomainModel));
        catalog.Register(MakeTool("save_all", ToolFamily.UiActions));
        catalog.ListVisibleNames().Should().BeEquivalentTo("create_entity", "save_all");
    }

    [Fact]
    public void OnStudio11x_OnlyAllowlistedTools_Visible()
    {
        var catalog = new ToolCatalog(TargetMode.Studio11x);
        catalog.Register(MakeTool("create_entity", ToolFamily.DomainModel));        // not on allowlist
        catalog.Register(MakeTool("delete_model_element", ToolFamily.DomainModel)); // on allowlist
        catalog.ListVisibleNames().Should().BeEquivalentTo("delete_model_element");
    }

    [Fact]
    public void FamilyDisabled_RemovesTools_OnBothModes()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        catalog.Register(MakeTool("create_entity", ToolFamily.DomainModel));
        catalog.Register(MakeTool("save_all", ToolFamily.UiActions));
        catalog.SetFamilyEnabled(ToolFamily.DomainModel, enabled: false);
        catalog.ListVisibleNames().Should().BeEquivalentTo("save_all");
    }

    [Fact]
    public async Task Invoke_DispatchesToRegisteredHandler()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        catalog.Register(new SimpleTool("echo", ToolFamily.Diagnostics,
            args => Task.FromResult<object>(args.ToJsonString())));
        var result = await catalog.InvokeAsync("echo", new JsonObject { ["msg"] = "hi" });
        ((string)result).Should().Contain("hi");
    }

    private sealed record SimpleTool(string Name, ToolFamily Family, Func<JsonObject, Task<object>> Invoke) : ITool;
}
```

- [ ] **Step 2: Run the tests — they should fail to build**

Run: `dotnet test tests/Concord.Core.Tests/Concord.Core.Tests.csproj`
Expected: build error — types don't exist yet.

- [ ] **Step 3: Implement ITool and ToolFamily**

`src/Concord.Core/Mcp/ITool.cs`:

```csharp
namespace Terminal.Mcp;

using System.Text.Json.Nodes;

public interface ITool
{
    string Name { get; }
    ToolFamily Family { get; }
    Func<JsonObject, Task<object>> Invoke { get; }
}
```

`src/Concord.Core/Mcp/ToolFamily.cs`:

```csharp
namespace Terminal.Mcp;

public enum ToolFamily
{
    UiActions,
    Maia,
    DomainModel,
    Microflows,
    Pages,
    Navigation,
    Security,
    Workflows,
    ConstantsEnums,
    DataSample,
    Diagnostics,
    ProjectSettings,
}
```

- [ ] **Step 4: Implement ToolCatalog**

`src/Concord.Core/Mcp/ToolCatalog.cs`:

```csharp
namespace Terminal.Mcp;

using System.Text.Json.Nodes;
using Terminal;

public sealed class ToolCatalog
{
    private readonly TargetMode _mode;
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ToolFamily> _disabledFamilies = new();
    private readonly object _gate = new();

    public ToolCatalog(TargetMode mode) => _mode = mode;

    public void Register(ITool tool)
    {
        lock (_gate) _tools[tool.Name] = tool;
    }

    public void SetFamilyEnabled(ToolFamily family, bool enabled)
    {
        lock (_gate)
        {
            if (enabled) _disabledFamilies.Remove(family);
            else _disabledFamilies.Add(family);
        }
    }

    public IReadOnlyList<string> ListVisibleNames()
    {
        lock (_gate)
            return _tools.Values
                         .Where(IsVisible)
                         .Select(t => t.Name)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                         .ToList();
    }

    public IReadOnlyList<ITool> ListVisibleTools()
    {
        lock (_gate) return _tools.Values.Where(IsVisible).ToList();
    }

    public Task<object> InvokeAsync(string name, JsonObject arguments)
    {
        ITool? tool;
        lock (_gate)
        {
            if (!_tools.TryGetValue(name, out tool) || !IsVisible(tool))
                throw new InvalidOperationException(
                    $"Tool '{name}' is not registered or is filtered out for TargetMode={_mode}.");
        }
        return tool.Invoke(arguments);
    }

    private bool IsVisible(ITool tool)
    {
        if (_disabledFamilies.Contains(tool.Family)) return false;
        if (_mode == TargetMode.Studio11x && !Studio11xAllowlist.Contains(tool.Name)) return false;
        return true;
    }
}
```

- [ ] **Step 5: Create the Studio11xAllowlist scaffold**

`src/Concord.Core/Mcp/Studio11xAllowlist.cs`:

```csharp
namespace Terminal.Mcp;

/// <summary>
/// Tools that ship on Studio Pro 11.x. Studio Pro's built-in MCP covers
/// the rest; including those would create model-side ambiguity. Contents
/// from spec lines 198-211, reconciled with the live tools/list snapshot
/// in Task 1.
/// </summary>
public static class Studio11xAllowlist
{
    private static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        // UI actions + Maia (always on 11.x — Concord-native)
        "run_app", "stop_app", "save_all", "refresh_project",
        "maia__ask", "maia__busy", "maia__ping", "maia__health", "maia__new_chat",

        // Pages
        "generate_overview_pages", "delete_document",
        // Navigation
        "manage_navigation",
        // Security
        "read_security_info", "read_entity_access_rules", "read_microflow_security", "audit_security",
        // Domain Model — hard deletes + reference-safe renames + layout
        "delete_model_element",
        "rename_entity", "rename_attribute", "rename_association",
        "rename_document", "rename_module", "rename_enumeration_value",
        "set_documentation", "arrange_domain_model",
        // Microflows — gaps in studio-pro MCP edit surface
        "exclude_document", "set_microflow_url", "modify_microflow_activity", "insert_before_activity",
        // Project / Settings
        "read_runtime_settings", "set_runtime_settings", "read_configurations", "set_configuration",
        // Data & Sample
        "save_data", "generate_sample_data", "read_sample_data", "setup_data_import",
        // Diagnostics
        "check_model", "check_project_errors", "get_studio_pro_logs", "get_last_error", "analyze_project_patterns",
    };

    public static bool Contains(string toolName) => _names.Contains(toolName);
    public static IReadOnlyCollection<string> All => _names;
}
```

Reconcile this set against the live `tools/list` snapshot captured in Task 1 Step 3. Add to / remove from the set based on what studio-pro 11.x actually advertises. Commit the reconciliation as part of the same Phase 5 commit.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Concord.Core.Tests/Concord.Core.Tests.csproj`
Expected: green (4 new ToolCatalogTests pass).

- [ ] **Step 7: Commit**

```bash
git add src/Concord.Core/Mcp/ITool.cs \
        src/Concord.Core/Mcp/ToolFamily.cs \
        src/Concord.Core/Mcp/ToolCatalog.cs \
        src/Concord.Core/Mcp/Studio11xAllowlist.cs \
        tests/Concord.Core.Tests/ToolCatalogTests.cs
git commit -m "feat(core): add ToolCatalog + ToolFamily + 11.x allowlist (W2)

ITool is the registration contract; ToolCatalog filters by TargetMode
+ family toggles. Studio11xAllowlist holds the curated 11.x surface
(spec lines 198-211, reconciled against the live tools/list snapshot
captured in W2 spike Task 1). Unit tests cover registration, mode
filtering, family disable, and dispatch."
```

### Task 19: Wire SPMCP tools through ToolCatalog from each host

**Files:**
- Create: `src/Concord.Host11x/Spmcp/SpmcpToolBootstrap11x.cs`
- Create: `src/Concord.Host10x/Spmcp/SpmcpToolBootstrap10x.cs`
- Modify: `src/Concord.Host11x/Host11xEntry.cs`
- Modify: `src/Concord.Host10x/Host10xEntry.cs`

Bootstrap classes instantiate the SPMCP tool classes and register one `ITool` per public method into the `ToolCatalog`. The hosts call this from `HostXEntry`'s constructor after `HostServices.Register`.

- [ ] **Step 1: Implement SpmcpToolBootstrap11x**

```csharp
namespace Concord.Host11x.Spmcp;

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Terminal.Mcp;
using Terminal.Spmcp.Tools;

public static class SpmcpToolBootstrap11x
{
    public static void Register(ToolCatalog catalog, string? projectDirectory)
    {
        var additional = new MendixAdditionalTools(NullLogger<MendixAdditionalTools>.Instance, projectDirectory);
        var domain = new MendixDomainModelTools(NullLogger<MendixDomainModelTools>.Instance);

        Register(catalog, "save_data", ToolFamily.DataSample, additional.SaveData);
        Register(catalog, "generate_sample_data", ToolFamily.DataSample, additional.GenerateSampleData);
        Register(catalog, "read_sample_data", ToolFamily.DataSample, additional.ReadSampleData);
        Register(catalog, "generate_overview_pages", ToolFamily.Pages, additional.GenerateOverviewPages);
        Register(catalog, "list_microflows", ToolFamily.Microflows, additional.ListMicroflows);
        Register(catalog, "read_microflow_details", ToolFamily.Microflows, additional.ReadMicroflowDetails);
        // … register every public tool method here. About 80 calls.
        Register(catalog, "list_modules", ToolFamily.DomainModel, domain.ListModules);
        Register(catalog, "create_entity", ToolFamily.DomainModel, domain.CreateEntity);
        Register(catalog, "delete_model_element", ToolFamily.DomainModel, domain.DeleteModelElement);
        // …
    }

    private static void Register(ToolCatalog catalog, string name, ToolFamily family, Func<JsonObject, Task<object>> invoke)
        => catalog.Register(new RegisteredTool(name, family, invoke));

    private static void Register(ToolCatalog catalog, string name, ToolFamily family, Func<JsonObject, Task<string>> invoke)
        => catalog.Register(new RegisteredTool(name, family, async args => (object)await invoke(args)));

    private sealed record RegisteredTool(string Name, ToolFamily Family, Func<JsonObject, Task<object>> Invoke) : ITool;
}
```

The tool-name → method mapping should be **the complete public surface** of `MendixAdditionalTools` + `MendixDomainModelTools`. Use Task 4's inventory to make sure none are missed.

- [ ] **Step 2: Mirror SpmcpToolBootstrap10x**

Same shape. On 10.x, *every* tool registers (no allowlist filter), and any tool whose underlying host service reports `IsAvailable=false` will still register but throw `NotSupportedException` at invocation — ToolCatalog can wrap that into the structured error response in W3.

- [ ] **Step 3: Wire bootstrap calls from Host*Entry**

Edit `src/Concord.Host11x/Host11xEntry.cs`:

```csharp
[ImportingConstructor]
public Host11xEntry(/* MEF imports */ IApp app, IModel model, /* … */)
{
    if (System.Threading.Interlocked.Exchange(ref _initialized, 1) != 0) return;

    HostContext.Initialize(TargetMode.Studio11x);
    HostServices.Register(
        app: new Interop.StudioProAppHost11x(app),
        runConfigs: new Interop.RunConfigurationsHost11x(/* … */),
        runState: new Interop.RunStateHost11x(/* … */),
        moduleImport: new Interop.ModuleImportHost11x(app),
        model: new Interop.ModelHost11x(model),
        domainModel: new Interop.DomainModelHost11x(model),
        pageGeneration: new Interop.PageGenerationHost11x(/* IPageGenerationService */),
        navigation: new Interop.NavigationHost11x(/* INavigationManagerService */),
        versionControl: new Interop.VersionControlHost11x(/* IVersionControlService */),
        untypedModel: new Interop.UntypedModelHost11x(/* IUntypedModelAccessService */),
        microflowAuthoring: new Interop.MicroflowAuthoringHost11x(model /*, IMicroflowService */));

    Catalog = new ToolCatalog(TargetMode.Studio11x);
    SpmcpToolBootstrap11x.Register(Catalog, projectDirectory: app.Root?.DirectoryPath);
    Spmcp.UiActionsBootstrap.Register(Catalog);     // Phase 7 splits UI-action tools into this
}

public static ToolCatalog? Catalog { get; private set; }
```

The `Catalog` static is the dispatch root for `StudioProActionServer` (Phase 6 wires it).

- [ ] **Step 4: Mirror in Host10xEntry**

- [ ] **Step 5: Build both hosts**

Run: `dotnet build Terminal.sln`
Expected: build succeeds. Runtime is still broken until Phase 6 routes HTTP requests through the catalog; build green is sufficient.

- [ ] **Step 6: Commit**

```bash
git add src/Concord.Host11x/Spmcp/ src/Concord.Host10x/Spmcp/ \
        src/Concord.Host11x/Host11xEntry.cs src/Concord.Host10x/Host10xEntry.cs
git commit -m "feat(host): wire SPMCP tools into ToolCatalog at MEF activation (W2)

Each host instantiates the SPMCP tool classes against its
HostServices implementations and registers ~80 tools by family.
Host11xEntry.Catalog (static) is the dispatch root used by
StudioProActionServer in the next task."
```

### Task 20: Route StudioProActionServer dispatch through ToolCatalog

**Files:**
- Modify: `src/Concord.Core/Mcp/StudioProActionServer.cs`

`StudioProActionServer` currently hand-dispatches a small set of UI-action tools. Refactor it to read the active `ToolCatalog` and call `InvokeAsync(name, args)` for every request, with the UI-action tools registered in the catalog by the host bootstrap (no longer hardcoded in the server).

- [ ] **Step 1: Refactor the dispatch loop**

In `StudioProActionServer.cs`, find the section that handles incoming HTTP / SSE `tools/call` requests. Replace the hand-rolled switch with:

```csharp
var catalog = Concord.Host11x.Host11xEntry.Catalog ?? Concord.Host10x.Host10xEntry.Catalog
    ?? throw new InvalidOperationException("No ToolCatalog registered — host startup failed.");
var result = await catalog.InvokeAsync(toolName, arguments);
```

But Core can't reference either Host project. Instead, the active catalog must be exposed via Core. Add a static `Terminal.Mcp.ToolCatalogRegistry.Active` property:

```csharp
// src/Concord.Core/Mcp/ToolCatalog.cs (extend)
public static class ToolCatalogRegistry
{
    public static ToolCatalog? Active { get; set; }
}
```

The host bootstrap (Task 19) sets `ToolCatalogRegistry.Active = Catalog;` after registering all tools.

- [ ] **Step 2: Update bootstrap to set the global**

In `SpmcpToolBootstrap11x.Register` (and `10x`), after all `catalog.Register(...)` calls:

```csharp
ToolCatalogRegistry.Active = catalog;
```

In Host*Entry, also set `ToolCatalogRegistry.Active = Catalog;` after the bootstrap completes — belt-and-braces.

- [ ] **Step 3: Update `tools/list` response**

`StudioProActionServer` advertises a tools array on `tools/list`. Have it iterate `ToolCatalogRegistry.Active.ListVisibleTools()` and produce one entry per tool. Each entry needs name + description + JSON schema for arguments. Schemas come from the existing SPMCP catalog (look in MCPExtension's `Mcp/MendixMcpServer.cs` — though we deleted that file, the schemas may live elsewhere in `Tools/`. If they were inlined into `MendixMcpServer`, regenerate per-tool from the docstrings using a small reflection helper).

If schema reconstruction is non-trivial, deliver an MVP that returns name + a generic schema `{ "type": "object" }`, and capture full schemas as a follow-up task in CHANGELOG. The client tooling — Claude Code, Codex — accepts open schemas.

- [ ] **Step 4: Build + test**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: green. Existing UI-action tests still pass because the catalog now contains them.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(core): route MCP server dispatch through ToolCatalog (W2)

StudioProActionServer now resolves and invokes tools via
ToolCatalogRegistry.Active. tools/list iterates the catalog. The
hardcoded UI-action switch is replaced. 11.x users see the curated
allowlist; 10.x sees the full SPMCP surface."
```

### Task 21: Migrate UI-action + Maia tools into ToolCatalog registrations

**Files:**
- Modify: `src/Concord.Core/Mcp/StudioProActions.cs` and surrounding wiring
- Create: `src/Concord.Core/Mcp/UiActionsBootstrap.cs`
- Create: `src/Concord.Core/Mcp/MaiaToolsBootstrap.cs`

UI actions (`run_app`, `stop_app`, `save_all`, `refresh_project`) and Maia tools (`maia__ask`, etc.) used to be wired directly in `StudioProActionServer`. Phase 5 puts them in the catalog so 11.x family-toggles can disable them just like SPMCP tools.

- [ ] **Step 1: Implement UiActionsBootstrap**

```csharp
namespace Terminal.Mcp;

using System.Text.Json.Nodes;
using Terminal.Interop;

public static class UiActionsBootstrap
{
    public static void Register(ToolCatalog catalog)
    {
        var actions = new StudioProActions(/* construct from HostServices, see Task 22 */);

        catalog.Register(new Tool("run_app",         ToolFamily.UiActions, args => RunAsync(actions, args)));
        catalog.Register(new Tool("stop_app",        ToolFamily.UiActions, args => StopAsync(actions, args)));
        catalog.Register(new Tool("save_all",        ToolFamily.UiActions, args => SaveAsync(actions, args)));
        catalog.Register(new Tool("refresh_project", ToolFamily.UiActions, args => RefreshAsync(actions, args)));
    }

    private static async Task<object> RunAsync(StudioProActions actions, JsonObject args)
        => await actions.RunAppAsync();

    private static async Task<object> StopAsync(StudioProActions actions, JsonObject args)
        => await actions.StopAppAsync();

    private static async Task<object> SaveAsync(StudioProActions actions, JsonObject args)
        => await actions.SaveAllAsync();

    private static async Task<object> RefreshAsync(StudioProActions actions, JsonObject args)
        => await actions.RefreshProjectAsync();

    private sealed record Tool(string Name, ToolFamily Family, Func<JsonObject, Task<object>> Invoke) : ITool;
}
```

- [ ] **Step 2: Implement MaiaToolsBootstrap**

Same shape — register `maia__busy`, `maia__ping`, `maia__health`, `maia__new_chat`, `maia__ask`, each delegating to the existing Maia bridge.

- [ ] **Step 3: Wire bootstrap calls into Host*Entry after SPMCP register**

In each Host*Entry, after `SpmcpToolBootstrap{N}x.Register(...)`:

```csharp
UiActionsBootstrap.Register(Catalog);
MaiaToolsBootstrap.Register(Catalog);
```

- [ ] **Step 4: Build + run all tests + smoke check that tools/list returns the expected surface**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): register UI-action and Maia tools through ToolCatalog (W2)

run_app, stop_app, save_all, refresh_project, and the maia__* tools
are now first-class ToolCatalog entries with family=UiActions /
Maia. Family toggles can disable them; allowlist still includes
them on 11.x."
```

---

## Phase 6 — Host10x UI port (B2)

Bring Host10x to feature parity with Host11x's UI surface: terminal pane, web server, run/stop/save/refresh UI automation, and the real menu (replacing the v5.0.0-alpha.1 placeholder dialog).

### Task 22: Add Host10x project references for UI tier

**Files:**
- Modify: `src/Concord.Host10x/Concord.Host10x.csproj`

Host11x.csproj already references Eto.Forms (for `MessageBox`, `Form`) and any WebView2 / native-pty packages needed. Host10x.csproj likely doesn't yet.

- [ ] **Step 1: Inspect Host11x.csproj's package list**

Run: `Select-String -Path src/Concord.Host11x/Concord.Host11x.csproj -Pattern "PackageReference"`

Expected: a list of NuGet refs. Identify any that Host10x needs to mirror (Eto.Forms is the likely one).

- [ ] **Step 2: Add the matching references to Host10x.csproj**

Edit `src/Concord.Host10x/Concord.Host10x.csproj`. Add to the existing `<ItemGroup>`:

```xml
<PackageReference Include="Eto.Forms" Version="2.9.*" />
<!-- plus any other UI-tier refs found in Host11x -->
```

- [ ] **Step 3: Build Host10x**

Run: `dotnet build src/Concord.Host10x/Concord.Host10x.csproj`
Expected: `Build succeeded.` No new code yet — just the package adds.

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Host10x/Concord.Host10x.csproj
git commit -m "build(host10x): add Eto.Forms (+ UI tier deps) for upcoming pane port (W2)"
```

### Task 23: Port TerminalPaneViewModel to Host10x

**Files:**
- Create: `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs`

Host11x's `TerminalPaneViewModel` is the WebView ↔ session-manager bridge. The class is largely UI-framework-only (Eto + WebView2) and may not reference Studio Pro types at all. If it doesn't, the 10.x port is a near-verbatim copy.

- [ ] **Step 1: Inspect Host11x's TerminalPaneViewModel for Studio Pro coupling**

Run: `Select-String -Path src/Concord.Host11x/Pane/TerminalPaneViewModel.cs -Pattern "Mendix\.StudioPro\.ExtensionsAPI"`

If no matches, the file can be copied verbatim to Host10x (with just namespace + using adjustments).
If there are matches, each one needs to be replaced with `HostServices.*` or a host parameter.

- [ ] **Step 2: Create Host10x's TerminalPaneViewModel**

Most-likely path: copy from `src/Concord.Host11x/Pane/TerminalPaneViewModel.cs` to `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs`. Change:
  - `namespace Concord.Host11x.Pane;` → `namespace Concord.Host10x.Pane;`
  - `using Concord.Host11x;` → `using Concord.Host10x;` (only if any usings reference host classes)
  - `Concord.Host11x.Host11xEntry` reference (if any, e.g., for the catalog static) → `Concord.Host10x.Host10xEntry`

- [ ] **Step 3: Build Host10x**

Run: `dotnet build src/Concord.Host10x/Concord.Host10x.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Host10x/Pane/TerminalPaneViewModel.cs
git commit -m "feat(host10x): port TerminalPaneViewModel against 10.21.1 (W2)

WebView ↔ session-manager bridge. Largely UI-framework code, mirrors
Host11x with namespace adjustments. No Studio Pro coupling expected;
deviations recorded inline if found."
```

### Task 24: Port TerminalPaneExtension to Host10x

**Files:**
- Create: `src/Concord.Host10x/Pane/TerminalPaneExtension.cs`

This is the file that drove the W1 alpha's "10.x preview" placeholder. The 11.x version implements `IDockablePaneExtension` (interface); the 10.x equivalent likely **inherits** from a `DockablePaneExtension` base class — same API-drift pattern as `MenuExtension`.

- [ ] **Step 1: Identify the 10.x dockable-pane API shape**

Run: `Select-String -Path src/Concord.Core/Spmcp/backport-10x/reference -Pattern "DockablePane|IDockablePaneExtension" -Recurse`

This catalogs whether MCPExtension's 10.x reference subclassed a base or implemented an interface — and what type names to reference.

If `backport-10x/reference/` doesn't include a pane extension (the original SPMCP used `AIAPIEngine` instead), inspect `Mendix.StudioPro.ExtensionsAPI` 10.21.1 directly:

```powershell
$pkg = Get-ChildItem "$env:USERPROFILE\.nuget\packages\mendix.studiopro.extensionsapi\10.21.1" -Recurse -Filter Mendix.StudioPro.ExtensionsAPI.dll | Select-Object -First 1
dotnet tool run ildasm $pkg.FullName /text /item:Mendix.StudioPro.ExtensionsAPI.UI.DockablePane | Select-String "class"
```

(Alternatively read the included `Mendix.StudioPro.ExtensionsAPI.xml` doc file at `src/Concord.Core/Spmcp/backport-10x/reference/Mendix.StudioPro.ExtensionsAPI.xml`.)

Record the finding in spike notes: `HOST10X_DOCKABLE_PANE_KIND = abstract-base | interface`.

- [ ] **Step 2: Copy Host11x's TerminalPaneExtension and adapt**

`src/Concord.Host10x/Pane/TerminalPaneExtension.cs`:

```csharp
namespace Concord.Host10x.Pane;

using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;  // VERIFY: namespace may differ on 10.21.1
using Terminal;
using Terminal.Interop;

[Export(typeof(DockablePaneExtension))]  // if abstract-base on 10.x
public sealed class TerminalPaneExtension : DockablePaneExtension  // → adjust to interface impl if needed
{
    public const string ID = "Concord.Terminal";

    [Import(typeof(Host10xEntry))]
#pragma warning disable CS0414  // Sentinel: field is read by MEF activation, never used by host code
    private Host10xEntry? _entry = null;
#pragma warning restore CS0414

    public override string GetTitle() => "Concord";
    public override DockablePaneViewModelBase Open() => new TerminalPaneViewModel(/* construct as in Host11x */);

    // Adjust signatures based on the 10.x abstract class / interface shape.
}
```

The key methods to override (or implement) are `GetTitle`, `Open`, and any lifecycle hooks (`OnClose`, `OnActivated`). Match against the 10.21.1 surface.

- [ ] **Step 3: Build Host10x**

Run: `dotnet build src/Concord.Host10x/Concord.Host10x.csproj`
Expected: `Build succeeded.` Compile errors here are the actual API drift — resolve them using the IDoc/XML reference, then build.

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Host10x/Pane/TerminalPaneExtension.cs
git commit -m "feat(host10x): port TerminalPaneExtension against 10.21.1 (W2)

Subclasses the 10.x DockablePaneExtension base (cf. interface on
11.x). Open() instantiates the ported TerminalPaneViewModel. ID
'Concord.Terminal' matches Host11x for cross-version consistency."
```

### Task 25: Port TerminalWebServer to Host10x

**Files:**
- Create: `src/Concord.Host10x/Ui/TerminalWebServer.cs`

Host11x's `TerminalWebServer` is a MEF-exported `IWebServerExtension` that registers the HTTP route for the WebView pane (serving the React bundle + WebSocket for the terminal). The 10.x port mirrors but binds against 10.21.1's web-server abstraction.

- [ ] **Step 1: Identify the 10.x web-server API shape**

Run: `Select-String -Path src/Concord.Core/Spmcp/backport-10x/reference -Pattern "WebServer|IWebServerExtension" -Recurse`

Record the shape in spike notes.

- [ ] **Step 2: Copy from Host11x and adapt**

Mirror `src/Concord.Host11x/Ui/TerminalWebServer.cs` to `src/Concord.Host10x/Ui/TerminalWebServer.cs`. Namespace + base class / interface adjustments only — route handlers should be reusable verbatim if they go through Core (the HTTP-serving plumbing in TerminalWebServer is Studio Pro's, but the *handlers* are in Core).

- [ ] **Step 3: Build Host10x**

Run: `dotnet build src/Concord.Host10x/Concord.Host10x.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Host10x/Ui/TerminalWebServer.cs
git commit -m "feat(host10x): port TerminalWebServer against 10.21.1 (W2)

10.x web-server extension surface mirrors Host11x; handler logic
delegates to Core. Routes are identical (/, /terminal/ws, /maia/*)."
```

### Task 26: Port RunStateProbe + StudioProUiAutomation to Host10x

**Files:**
- Create: `src/Concord.Host10x/Interop/RunStateProbe.cs`
- Create: `src/Concord.Host10x/Interop/StudioProUiAutomation.cs`

These are the implementations behind `IRunStateHost` (already stubbed in W1) and the UI-automation surface (`StudioProActions`'s ui callbacks). Host11x has both; Host10x doesn't.

- [ ] **Step 1: Copy from Host11x and adapt**

`src/Concord.Host10x/Interop/RunStateProbe.cs` mirrors `src/Concord.Host11x/Interop/RunStateProbe.cs`. The probe reads Studio Pro's run-state surface — on 10.21.1 this may live under a slightly different namespace (`Mendix.StudioPro.ExtensionsAPI.Services` vs. `Mendix.StudioPro.ExtensionsAPI.UI.Services`). Verify each `using` line.

`src/Concord.Host10x/Interop/StudioProUiAutomation.cs` is the Win32-level Send-Input class that fires Ctrl+S, F5, F8 to Studio Pro's main window. Mostly Win32 — no Studio Pro coupling. Verbatim copy with namespace adjustment.

- [ ] **Step 2: Build Host10x**

Run: `dotnet build src/Concord.Host10x/Concord.Host10x.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/Concord.Host10x/Interop/RunStateProbe.cs \
        src/Concord.Host10x/Interop/StudioProUiAutomation.cs
git commit -m "feat(host10x): port RunStateProbe + StudioProUiAutomation against 10.21.1 (W2)

Run-state probe verifies on 10.21.1's run-state surface; UI
automation is Win32-only and mirrors Host11x verbatim with
namespace adjustments."
```

### Task 27: Replace ConcordMenuExtension placeholder with the real TerminalMenuExtension

**Files:**
- Delete: `src/Concord.Host10x/MenuExtensions/ConcordMenuExtension.cs`
- Create: `src/Concord.Host10x/MenuExtensions/TerminalMenuExtension.cs`

The W1 alpha's placeholder showed an Eto MessageBox saying "preview". Now that the pane works, the menu opens it.

- [ ] **Step 1: Create TerminalMenuExtension for Host10x**

`src/Concord.Host10x/MenuExtensions/TerminalMenuExtension.cs`:

```csharp
namespace Concord.Host10x.MenuExtensions;

using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Concord.Host10x.Pane;

[Export(typeof(MenuExtension))]
public sealed class TerminalMenuExtension : MenuExtension
{
    [Import(typeof(Host10xEntry))]
#pragma warning disable CS0414  // Sentinel: field is read by MEF activation, never used by host code
    private Host10xEntry? _entry = null;
#pragma warning restore CS0414

    private readonly IDockingWindowService docking;

    [ImportingConstructor]
    public TerminalMenuExtension(IDockingWindowService docking) => this.docking = docking;

    public override IEnumerable<MenuViewModel> GetMenus()
    {
        yield return new MenuViewModel(
            caption: "Open Pane",
            action: () => docking.OpenPane(TerminalPaneExtension.ID));
    }
}
```

- [ ] **Step 2: Delete the placeholder**

```bash
git rm src/Concord.Host10x/MenuExtensions/ConcordMenuExtension.cs
```

- [ ] **Step 3: Build + test**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: green.

- [ ] **Step 4: Smoke test on Studio Pro 10.24.13**

Deploy to a real 10.24.13 project. Launch Studio Pro. Click Extensions → Concord → Open Pane. Expected: the terminal pane opens, the WebView renders, `concord-mcp` HTTP server responds on `http://localhost:7783/health`, one tool per family invokes successfully (run `claude mcp list` then `claude mcp call concord-mcp list_modules` or similar).

If any check fails, fix the underlying Host10x port issue before moving on. Capture findings in spike notes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(host10x): wire the real terminal menu, retire 'preview' placeholder (W2)

Replaces ConcordMenuExtension's MessageBox with TerminalMenuExtension
that opens the ported TerminalPaneExtension. Studio Pro 10.24.13 now
gets the full pane + MCP + Maia bridge surface."
```

### Task 28: Retire the SPMCP backport-10x reference

**Files:**
- Delete: `src/Concord.Core/Spmcp/backport-10x/`

By this point, every drift Host10x needed is encoded in its `Interop/*Host10x.cs` files. The MCPExtension reference can be archived externally.

- [ ] **Step 1: Confirm no remaining references**

Run: `Select-String -Path src,tests -Pattern "Spmcp/backport-10x|backport-10x/reference" -Recurse`
Expected: no matches.

- [ ] **Step 2: Delete**

```bash
git rm -r src/Concord.Core/Spmcp/backport-10x
```

- [ ] **Step 3: Build + test**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: green.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(spmcp): retire imported backport-10x/ reference (W2)

The 10.x API drift it documented now lives in
src/Concord.Host10x/Interop/*Host10x.cs. The original MCPExtension
repo remains as a read-only archive for anyone who needs the source."
```

---

## Phase 7 — HostServices consolidation (B3)

Migrate the legacy `Func<>` callback chain in `StudioProActions` (and any other callers) to read directly from `HostServices`. Remove the dual injection mechanisms.

### Task 29: Migrate StudioProActions to read run-state + UI automation from HostServices

**Files:**
- Modify: `src/Concord.Core/Mcp/StudioProActions.cs`
- Modify: `src/Concord.Host11x/Pane/TerminalPaneViewModel.cs` (drop the Func<> constructor params)
- Modify: `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs`
- Modify: `src/Concord.Host11x/Host11xEntry.cs` (register UI-automation + run-state hosts)
- Modify: `src/Concord.Host10x/Host10xEntry.cs`

Today `StudioProActions` takes `IRunStateProbe probe` and `IStudioProUiAutomation ui` via constructor, plus two `Func<>` callbacks for `getActiveRunConfig` and `getProjectInfo`. Migrate everything to `HostServices.*`.

- [ ] **Step 1: Add accessors on HostServices**

The two existing run/UI surfaces are not yet in `HostServices`. Add:

```csharp
// HostServices.cs (extend)
public static IRunStateProbe RunStateProbe => _runStateProbe ?? throw NotInitialized(nameof(IRunStateProbe));
public static IStudioProUiAutomation UiAutomation => _uiAutomation ?? throw NotInitialized(nameof(IStudioProUiAutomation));
```

Add corresponding private fields and a third `Register` overload that includes them. (Or extend the existing 11-arg overload to 13 args — pick one for consistency.)

Where do `IRunStateProbe` and `IStudioProUiAutomation` live? Today they're in `Terminal.Mcp` (Core). That's fine — they're already Core-accessible.

- [ ] **Step 2: Refactor StudioProActions constructor**

Replace:

```csharp
public StudioProActions(
    IRunStateProbe probe,
    IStudioProUiAutomation ui,
    TimeSpan? runTimeout = null,
    TimeSpan? stopTimeout = null,
    TimeSpan? pollInterval = null,
    Func<RunConfigurationSnapshot?>? getActiveRunConfig = null,
    Func<(string? path, string? name)>? getProjectInfo = null)
```

With:

```csharp
public StudioProActions(
    TimeSpan? runTimeout = null,
    TimeSpan? stopTimeout = null,
    TimeSpan? pollInterval = null)
```

In the body, replace `this.probe` with `HostServices.RunStateProbe`, `this.ui` with `HostServices.UiAutomation`, `getActiveRunConfig()` with `HostServices.RunConfigurations.GetActive()` (translating the `RunConfigurationInfo` record into `RunConfigurationSnapshot` if the shapes still differ — see naming-smells note in W1 handoff line 95-98 for the duplicate types; Task 33 consolidates), and `getProjectInfo()` with `(HostServices.App.ProjectPath, HostServices.App.ProjectName)`.

- [ ] **Step 3: Update tests that constructed StudioProActions with Fakes**

Existing tests in `Terminal.Tests` instantiate `StudioProActions` with fake probes / UI mocks. Migrate them to:

```csharp
HostServices.Reset();
HostServices.Register(/* fakes for all services */);
var actions = new StudioProActions();
```

The W1 plan's Task 9 already migrated some — verify each test compiles, fix the breakages.

- [ ] **Step 4: Wire UiAutomation + RunStateProbe in Host*Entry**

In `Host11xEntry`:

```csharp
HostServices.Register(
    // existing args …,
    runStateProbe: new Interop.RunStateProbe(/* ... */),
    uiAutomation: new Interop.StudioProUiAutomation(/* ... */));
```

Same in `Host10xEntry`.

- [ ] **Step 5: Drop the Func<> wiring from TerminalPaneViewModel**

In both Host10x and Host11x `TerminalPaneViewModel`, the constructor likely passes `getActiveRunConfig` and `getProjectInfo` callbacks to `StudioProActions`. Remove those param chains entirely; the viewmodel no longer needs them since actions read from HostServices.

- [ ] **Step 6: Build + test**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: green.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(core): route StudioProActions through HostServices (W2)

StudioProActions reads RunStateProbe, UiAutomation, RunConfigurations,
and App via HostServices instead of the Func<> callback chain and
direct service injection. TerminalPaneViewModel drops the legacy
parameter forwarding. Tests migrate to register fakes via
HostServices.Register."
```

### Task 30: Audit and remove now-dead pre-W1 injection paths

**Files:**
- Modify or delete: any class still constructed via the old `Func<>` chain
- Modify: `src/Concord.Core/Mcp/StudioProActionServer.cs` (if it still passes callbacks)

- [ ] **Step 1: Grep for leftover Func<> callback wiring**

Run:

```powershell
Select-String -Path src,tests -Pattern "Func<RunConfigurationSnapshot|Func<\(string\? path, string\? name\)" -Recurse
```

Expected: minimal or no matches after Task 29. Anything that remains is dead — delete or rewire.

- [ ] **Step 2: Grep for direct ExtensionsAPI service constructor injection still in Core**

Run:

```powershell
Select-String -Path src/Concord.Core -Pattern "Mendix\.StudioPro\.ExtensionsAPI" -Recurse
```

Expected: **zero matches** (everything goes through HostServices). If any remain, they're either Phase-4-missed methods or legacy paths — fix them.

- [ ] **Step 3: Build + test**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: green, no warnings.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(core): purge legacy Func<> callback chains and direct ExtensionsAPI refs (W2)

Verifies that Concord.Core has zero Mendix.StudioPro.ExtensionsAPI
references and no Func<> callback injection. HostServices is the
single host-services entry point. Any code surfaced by the audit
that still bypassed HostServices is rewired in this commit."
```

---

## Phase 8 — Polish, smoke matrix, version bump

### Task 31: Fix the CS0414 pragma codes flagged in the W1 handoff

**Files:**
- Modify: `src/Concord.Host11x/**/*.cs` (search for `CS0649`)
- Modify: `src/Concord.Host10x/**/*.cs` (search for `CS0649`)

Per the W1 handoff: the `#pragma warning disable CS0649` lines should be `CS0414`. Harmless but worth fixing during W2's polish pass.

- [ ] **Step 1: Find all occurrences**

Run: `Select-String -Path src/Concord.Host11x,src/Concord.Host10x -Pattern "CS0649" -Recurse`

- [ ] **Step 2: Replace each `CS0649` with `CS0414` (both enable and disable lines)**

```powershell
Get-ChildItem -Path src/Concord.Host11x, src/Concord.Host10x -Filter *.cs -Recurse | ForEach-Object {
  (Get-Content $_.FullName -Raw) -replace 'CS0649', 'CS0414' | Set-Content $_.FullName -NoNewline
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Terminal.sln`
Expected: 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: correct sentinel pragmas (CS0649 → CS0414) (W2 polish)

The MEF sentinel fields are assigned-but-never-used (CS0414), not
unused-uninitialized (CS0649). Wrong code in the suppression meant
the actual warning wasn't being silenced. Now it is."
```

### Task 32: Consolidate the two RunConfigurationInfo types

**Files:**
- Modify: `src/Concord.Core/Interop/IRunConfigurationsHost.cs`
- Modify: `src/Concord.Core/Mcp/StudioProActions.cs` (and any callers of `RunConfigurationSnapshot`)

Per the W1 handoff line 95-98: `Terminal.Interop.RunConfigurationInfo` (record, non-nullable) and `Terminal.RunConfigurationSnapshot` (DTO, nullable) overlap. Pick one — keep the Interop record; replace the Snapshot.

- [ ] **Step 1: Replace `RunConfigurationSnapshot` usages with `RunConfigurationInfo`**

Run: `Select-String -Path src,tests -Pattern "RunConfigurationSnapshot" -Recurse`

For each match, edit the file to use `Terminal.Interop.RunConfigurationInfo`. Where the old DTO had nullable fields and the new record has non-nullable, decide per call site:
  - If null was load-bearing (e.g., "no active run config"), return `RunConfigurationInfo?` instead of switching the field nullability.
  - If null was just laziness, fill it (e.g., with `string.Empty`).

- [ ] **Step 2: Delete the old `RunConfigurationSnapshot` class**

```bash
git grep -l "class RunConfigurationSnapshot\|record RunConfigurationSnapshot"
# For each file found, remove the type definition.
```

- [ ] **Step 3: Build + test**

Run: `dotnet build Terminal.sln && dotnet test Terminal.sln`
Expected: green.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(core): collapse RunConfigurationSnapshot into RunConfigurationInfo (W2)

The two types overlapped in scope (run-config description) and lived
in different namespaces, confusing readers. RunConfigurationInfo
(Terminal.Interop) becomes the single type; nullable returns
preserved where null was load-bearing."
```

### Task 33: Update DEPLOYING.md and CHANGELOG

**Files:**
- Modify: `DEPLOYING.md`
- Modify: `CHANGELOG.md`
- Modify: `README.md` (project-layout section if it lists files)

- [ ] **Step 1: Update DEPLOYING.md migration section**

Add a "From SPMCP standalone → Concord 5.0.0-alpha.2" section:

```markdown
## Migrating from MCPExtension / SPMCP

Concord 5.x source-merges MCPExtension's tool catalog. To migrate:

1. Remove `extensions/MCPExtension/` from your Mendix project — Concord now exposes the same ~84 tools through `concord-mcp` on port 7783.
2. Deploy the matching Concord host (`Concord10x/` for Studio Pro 10.24.13, `Concord11x/` for 11.x) — full guide above.
3. (Optional) Remove the legacy `SPMCP` module from your project: `App → Modules → SPMCP → Delete`. A renamed `Concord.SampleData` module ships in a follow-up W4 release.

On Studio Pro 11.x, Concord exposes only the curated allowlist (the studio-pro built-in MCP server handles the rest). On 10.24.13, Concord exposes the full surface.
```

- [ ] **Step 2: Update CHANGELOG.md**

Prepend:

```markdown
## 5.0.0-alpha.2 — W2 SPMCP merge

**Feature merge.** MCPExtension's ~84-tool catalog is now part of Concord. The standalone SPMCP install retires.

- Single `concord-mcp` HTTP server (port 7783) advertises tools matching the running Studio Pro version:
  - 10.24.13 → full SPMCP surface (no built-in studio-pro MCP exists there).
  - 11.x → curated allowlist (`Studio11xAllowlist.cs`) covering only the gaps in studio-pro MCP.
- Concord.Core no longer references `Mendix.StudioPro.ExtensionsAPI`. All Studio Pro modeling goes through `HostServices.{Model,DomainModel,PageGeneration,Navigation,VersionControl,UntypedModel,MicroflowAuthoring}` interfaces.
- Studio Pro 10.24.13 gains the full pane / terminal / MCP / Maia surface (replaces the v5.0.0-alpha.1 "preview" placeholder).
- `Tools/list` filter is family-aware: family toggles in Settings (W4) silence whole tool groups without restarting Studio Pro.
- HostServices is now the single host-services entry point; the legacy `Func<>` callback chain in `StudioProActions` retires.

**Known follow-ups (W3/W4):**
- 11.x allowlist reconciliation against newer Studio Pro builds remains an ongoing review (see spec line 213).
- Structured error contract (`escalation: maia-eligible | manual | none`) lands in W3.
- Family-toggle UI + `mendix-tool-map` skill pack + sample-data module import land in W4.
```

- [ ] **Step 3: Update README**

If `README.md` has a "Project layout" section that listed files, append:

```markdown
- `src/Concord.Core/Spmcp/` — source-merged from MCPExtension, refactored to depend on Core's `HostServices` (no Studio Pro types).
- `src/Concord.Core/Mcp/{ToolCatalog,Studio11xAllowlist,ITool,ToolFamily}.cs` — version-aware tool registry.
```

- [ ] **Step 4: Commit**

```bash
git add DEPLOYING.md CHANGELOG.md README.md
git commit -m "docs: W2 SPMCP-merge migration notes + changelog (W2)"
```

### Task 34: Bump version to 5.0.0-alpha.2

**Files:**
- Modify: `src/Concord.Core/Concord.Core.csproj` (Version)
- Modify: `src/Concord.Host10x/Concord.Host10x.csproj` (Version)
- Modify: `src/Concord.Host11x/Concord.Host11x.csproj` (Version)

- [ ] **Step 1: Bump each csproj**

Each project's `<Version>5.0.0-alpha.1</Version>` becomes `<Version>5.0.0-alpha.2</Version>`. If any csproj also sets `InformationalVersion`, update to `5.0.0-alpha.2+w2-mcpx-merge`.

- [ ] **Step 2: Build**

Run: `dotnet build Terminal.sln`
Expected: 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/Concord.Core/Concord.Core.csproj \
        src/Concord.Host10x/Concord.Host10x.csproj \
        src/Concord.Host11x/Concord.Host11x.csproj
git commit -m "chore: bump to 5.0.0-alpha.2 (W2 SPMCP merge)"
```

### Task 35: Full-stack smoke matrix

This is the gate before declaring W2 done. Run the full smoke on both Studio Pro versions.

- [ ] **Step 1: Clean build from scratch**

```bash
git clean -fdx -- bin obj
dotnet build Terminal.sln
```

Expected: clean build, 0 warnings.

- [ ] **Step 2: All tests pass**

```bash
dotnet test Terminal.sln --verbosity normal
```

Expected: 245+ existing + new ToolCatalog + SPMCP smoke tests all green; 3 pre-existing Maia-live skips remain.

- [ ] **Step 3: Smoke on Studio Pro 11.10**

In `Directory.Build.props`, set `MendixDeployTarget11x` to `C:\Projects\Test_11_10` (Joe's pinned 11.10 testbed). Run `dotnet build Terminal.sln` and verify `<project>/extensions/Concord11x/` populates.

Open Studio Pro 11.10. Click Extensions → Concord → Open Pane.

Check:
  - Concord pane opens. No errors in `terminal.log`.
  - `curl http://localhost:7783/health` → 200 OK.
  - From a Claude Code CLI in the terminal: `claude mcp list` shows `concord-mcp` connected.
  - `claude mcp call concord-mcp save_all` succeeds — Studio Pro saves.
  - `claude mcp call concord-mcp tools/list` returns the curated 11.x allowlist (no `create_entity`, no `add_attribute`, etc.).
  - `claude mcp call concord-mcp delete_model_element --args '{...}'` succeeds against a test entity.

- [ ] **Step 4: Smoke on Studio Pro 10.24.13**

Set `MendixDeployTarget10x` to `C:\Projects\Test_10_24_13`. Rebuild.

Launch Studio Pro 10.24.13 with `--enable-extension-development`. Click Extensions → Concord → Open Pane.

Check:
  - Same pane / terminal / MCP basics work.
  - `tools/list` returns the full SPMCP surface (~80 tools).
  - `create_entity` works against a test module.
  - `read_runtime_settings` succeeds.
  - VersionControl-dependent tools (e.g., `read_version_control`) return a structured `escalation=manual` response since 10.21.1 doesn't expose `IVersionControlService` (verify from the impl in Task 11 Step 3).

- [ ] **Step 5: Capture findings in spike notes**

Append to `docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md`:

```markdown
## W2 smoke results

| Version | Pane | MCP /health | tools/list count | save_all | create_entity | Notes |
|---|---|---|---|---|---|---|
| 11.10 | ✓ | ✓ | <N>  | ✓ | filtered out | <observations> |
| 10.24.13 | ✓ | ✓ | <N>  | ✓ | ✓ | <observations> |

Failures (if any) and follow-ups:
- …
```

- [ ] **Step 6: Tag W2 release if smoke passes**

```bash
git tag -a v5.0.0-alpha.2 -m "Concord 5.0.0-alpha.2 — W2 SPMCP merge + Host10x UI port"
```

Pushing the tag is a user decision — confirm with the user before `git push origin v5.0.0-alpha.2`.

- [ ] **Step 7: Commit the smoke notes**

```bash
git add docs/superpowers/handoffs/2026-05-12-concord-w2-spike-notes.md
git commit -m "docs(w2): record full-stack smoke results (W2 polish)"
```

---

## Self-review notes

**Spec coverage:**

- Umbrella spec W2 section ("Source merge of MCPExtension") — covered by Phases 1 (subtree import, prune) + 4 (tool refactor through Core Interop).
- Umbrella spec "MCP host refactor" — covered by Phase 5 (ToolCatalog + ITool + dispatch through registry).
- Umbrella spec "tool naming on the wire" (single `concord-mcp` server, no prefix) — covered by Phase 5 Task 20 (`tools/list` iterates the catalog under one server).
- W1 handoff Section B1 (source merge + tool-adapter refactor + ToolCatalog) — covered by Phases 1–5.
- W1 handoff Section B2 (Host10x UI port) — covered by Phase 6 (Tasks 22–28).
- W1 handoff Section B3 (HostServices consolidation) — covered by Phase 7 (Tasks 29–30).
- W2-before handoff "new gotchas" (Host10x defaulted ExtensionsApi10xVersion, Windows MSBuild npm fragility, GitHub Actions Node 20 deprecation) — none require plan-side changes; they're already addressed by W1 commits `57c38d0` and `a0d56ac`. CI Node 20 deprecation is informational; left for a separate trivial bump.
- W1 handoff "naming smells" (`RunConfigurationInfo` duplicate + dual injection mechanisms) — covered by Phase 7 Task 29 and Phase 8 Task 32.
- W1 handoff polish items (CS0414 pragma code, CS8604 nullable warning) — Task 31 covers CS0414. CS8604 was already fixed in W1 commit `7340f67` per the W2-before handoff; nothing remaining.

**Spec items deferred to W3-W4:**

- Structured error contract (`escalation: maia-eligible | manual | none`) — W3.
- Allowlist reconciliation procedure (build-time drift check between `Studio11xAllowlist.cs` and `mendix-tool-map/11x/SKILL.md`) — W3+W4.
- Family-level toggle UI in Settings — W4.
- `mendix-tool-map` skill pack (10x + 11x flavors) — W4.
- "Import sample-data module" button + `Concord.SampleData.mpk` shipping — W4. The SPMCP/ module project is deleted in Task 3 with the explicit understanding that W4 re-introduces it under `resources/Concord.SampleData/`.
- E2E smoke CI matrix against real Studio Pro installs — out-of-scope for W2 build-only CI (W1 ships the build matrix; e2e is a follow-up).

**Type consistency check:**

- `IModelHost`, `IDomainModelHost`, `IPageGenerationHost`, `INavigationHost`, `IVersionControlHost`, `IUntypedModelHost`, `IMicroflowAuthoringHost` — named consistently across Tasks 5–11.
- `ModuleId`, `DocumentId`, `EntityRef`, `AttributeRef`, `AssociationRef`, `ProjectInfo`, `EntityShape`, `CreateEntityRequest`, `CreateAssociationRequest`, `PageGenerationRequest`, `PageGenerationResult`, `NavigationItem`, `VersionControlInfo`, `MicroflowSummary`, `MicroflowActivitySummary`, `CreateMicroflowRequest` — used consistently across record definitions and host implementations.
- `ToolCatalog`, `ToolCatalogRegistry`, `ITool`, `ToolFamily`, `Studio11xAllowlist` — named consistently across Tasks 18–21.
- `SpmcpToolBootstrap11x`, `SpmcpToolBootstrap10x`, `UiActionsBootstrap`, `MaiaToolsBootstrap` — naming pattern consistent (`<Surface>Bootstrap` static class).
- `HostServices.Register` has two overloads in Task 8 (4-arg legacy + 11-arg new); Task 29 extends to 13-arg or refactors to a single overload. Either is fine — pick at execution time and stay consistent.
- `RunConfigurationInfo` (kept) vs `RunConfigurationSnapshot` (deleted) — Task 32.

**Test coverage:**

- `HostContextTests` extended in Task 8 to cover all 11 HostServices accessors via fakes.
- `ToolCatalogTests` added in Task 18 (registration, mode filter, family disable, dispatch).
- `SpmcpSmokeTests` added in Task 17 (each refactored tool instantiates + dispatches via fakes).
- `Terminal.Tests` migrated in Task 29 from Func<> callback construction to `HostServices.Register` setup.
- Manual smoke matrix in Task 35 covers both Studio Pro versions, the full feature surface, and the allowlist filter behavior.

**Assumption-failure escape hatches:**

- Task 1's MCPExtension subtree source unreachable → spike notes Step 4 captures the alternative.
- Task 1's tools/list snapshot unavailable → spec's allowlist used as working assumption; reconciliation deferred.
- Task 24's 10.x DockablePane API doesn't match the assumed shape → spike-notes finding + adjusted task body.
- Task 11 Step 3-4 — `IVersionControlService` / `IUntypedModelAccessService` absent on 10.21.1 → host impls report `IsAvailable=false` and corresponding tools return `escalation=manual`.

**Anti-patterns the plan deliberately avoids:**

- No "TODO" or "implement later" tokens in any task body — every step shows the change.
- Every interface body in Phase 2 shows the full method set, not just signatures-to-be-filled-in.
- Every cross-task reference uses consistent type names — no `clearLayers()` / `clearFullLayers()` drift.
- Phase 4 acknowledges its scope explicitly (largest single chunk; recommends subagent-driven execution).

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-12-concord-w2-mcpx-merge.md`. Two execution options:

1. **Subagent-Driven (recommended)** — Dispatch a fresh subagent per task; review between tasks; fast iteration. Phase 4's slice-by-slice refactor in particular benefits because each slice is independent.
2. **Inline Execution** — Run tasks in this session via `superpowers:executing-plans` with batched checkpoints. Slower context build-up but tighter feedback loop on the early Interop interface definitions.

Recommended: **Option 1 (Subagent-Driven)** for Phases 1, 3, 4, 6 (mechanical or repetitive); **Option 2 (Inline)** for Phases 0, 2, 5, 7, 8 (design-sensitive, fewer tasks, benefit from staying in-conversation).

