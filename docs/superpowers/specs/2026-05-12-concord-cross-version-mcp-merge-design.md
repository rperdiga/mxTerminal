# Concord 5.x — Cross-version support + MCPExtension merge (umbrella design)

**Status:** Approved umbrella design. Sub-specs and implementation plans follow per workstream.
**Date:** 2026-05-12
**Author:** Joe + Claude (brainstorming session)
**Supersedes:** none — extends current 4.1 architecture.

---

## Background

Concord 4.1 today targets Studio Pro 11.10+. Two constraints shape the next release:

- Most Mendix customers run **10.24.13 through 10.24.x** and won't migrate for 6-12 months. Mendix 11 isn't LTS until 11.12. Concord is unreachable for those customers.
- The built-in studio-pro MCP server (11.10+) is **weak in the areas customers most need help with**: security, page create/delete, layouts, navigation. The current escape hatch — Maia delegate — is slow and unreliable.

A sibling project, [`MCPExtension`](file:///c:/Extensions/MCPExtension) (a.k.a. SPMCP), already solves both problems independently: it's a standalone Studio Pro extension that ships ~84 MCP tools over its own HTTP/SSE server, and it works on 10.24.13. It has a `backport-10x/` tree handling the API drift between 10.x and 11.x. But it's a separate install with its own pane, its own port, its own Mendix module — and it duplicates a lot of plumbing Concord already has.

This design merges MCPExtension into Concord, makes the merged extension load on both 10.x and 11.x with a single binary family, and defines how its tool catalog and skill packs adapt per version.

## Goals

1. **Single Concord extension family** that loads on Studio Pro **10.24.13 through 11.x**.
2. **MCPExtension source-merged** into Concord. SPMCP standalone is retired.
3. **Hybrid orchestration on 11.x.** Studio-pro MCP and Concord MCP coexist with non-overlapping tool catalogs; the model picks tier via skill instructions; Maia and manual-steps escalation live inside individual Concord tool failure paths.
4. **Version-conditioned tool catalog.** 10.x advertises the full ~84-tool surface (no studio-pro MCP exists there). 11.x advertises a curated allowlist of ~15-25 tools — only the ones studio-pro MCP can't do or does badly.
5. **Family-level user toggles** for the merged tool surface, plus version-aware bundled skill packs that mirror the catalog.

## Non-goals

- Continued standalone publishing of SPMCP (`MCPExtension` repo becomes a read-only archive; customers consume the merged Concord).
- Per-tool toggle granularity (family-level is enough for v5; per-tool is a future iteration if asked).
- Proxying studio-pro MCP through Concord — catalogs don't overlap by name, no proxy needed.
- Automatic Mendix module imports (the sample-data `.mpk` import is user-driven via a Settings button).
- Concord 5.x preview alongside 4.1 — assume clean cutover at the next major release (open question, can be revisited).

## Architecture overview

Concord 5.x ships as **one shared `Concord.Core` library plus two host DLLs** (`Concord.Host10x.dll`, `Concord.Host11x.dll`). Each host binds its version-specific `Mendix.StudioPro.ExtensionsAPI` and registers tools via the shared Core's `ToolCatalog`. Studio Pro's MEF loader picks the matching host; the other host's manifest fails to resolve types and is silently skipped.

The MCP topology stays as today on the wire (`concord-mcp` on port 7783 with free-port fallback). What changes is the *contents* of `tools/list` per version, and the breadth of source code feeding that catalog (current Concord MCP code + the merged SPMCP tool surface).

### Four workstreams

- **W1 — Cross-version targeting.** Two host DLLs, shared core, version-aware deploy.
- **W2 — Source merge of MCPExtension.** `git subtree add` brings MCPExtension into `src/Spmcp/`. SPMCP's standalone HTTP server (`:3001`) and dockable pane are deleted. Tools re-register into Concord's in-process MCP host.
- **W3 — Hybrid fallback orchestration.** On 11.x the catalogs don't overlap; the model picks studio-pro vs. concord top-level; Maia + manual escalation live inside per-tool failure paths.
- **W4 — Version-aware skills + family-level toggles.** Seven pattern packs stay version-agnostic. New `mendix-tool-map` pack ships in two flavors (`10x`, `11x`). Settings → Concord MCP grows family-level toggles and a "Import sample-data module" button.

Each workstream gets its own sub-spec and implementation plan after this umbrella is approved.

## W1 — Repo, build, deploy

### Repo layout

```
Terminal.sln
├── src/
│   ├── Concord.Core/                  # NEW project — version-agnostic
│   │   ├── Terminal/                  # PTY, sessions, web server, settings
│   │   ├── Mcp/                       # In-process MCP host (refactored from StudioProActionServer.cs)
│   │   │   ├── McpServer.cs           # HTTP/SSE transport, unchanged on the wire
│   │   │   ├── ToolCatalog.cs         # NEW — registers tools, reads TargetMode + toggles
│   │   │   └── Tools/                 # UI actions + Maia tools (relocated)
│   │   ├── Maia/                      # CDP transports + Maia bridge
│   │   ├── Spmcp/                     # NEW — from MCPExtension via `git subtree add`
│   │   │   ├── Tools/                 # MendixAdditionalTools.cs, MendixDomainModelTools.cs
│   │   │   ├── Handlers/              # AssociationDiagnosticHandler, DeleteModelHandler, …
│   │   │   ├── Utils/                 # Shared helpers
│   │   │   └── Studio11xAllowlist.cs  # NEW — curated tool names for 11.x mode
│   │   └── Skills/                    # SkillInstaller + bundled-skill reader
│   ├── Concord.Host10x/               # NEW — binds 10.24.13 ExtensionsAPI
│   │   ├── ConcordMenuExtension.cs    # MEF entry — sets TargetMode=Studio10x
│   │   ├── ModuleImportWrapper.cs     # 10.x-specific Mendix module import
│   │   └── Concord.Host10x.csproj
│   └── Concord.Host11x/               # Equivalent for 11.x
├── skills/
│   ├── mendix-microflow-common/       # unchanged (version-agnostic)
│   ├── mendix-microflow-syntax/
│   ├── mendix-microflow-update/
│   ├── mendix-page-gen/
│   ├── mendix-view-entities/
│   ├── mendix-workflow-common/
│   ├── mendix-workflow-update/
│   └── mendix-tool-map/               # NEW — two flavors on disk
│       ├── 10x/SKILL.md
│       └── 11x/SKILL.md
├── resources/
│   └── Concord.SampleData.mpk         # renamed from SPMCP.mpk
├── tests/
└── Directory.Build.props
```

### Project structure

- **`Concord.Core`** is a regular .NET class library with **no Studio Pro reference**. All Studio Pro modeling calls go through interfaces (`IDomainModelService`, `IPageService`, etc.) that the host projects implement against their version-pinned typings. The existing `backport-10x/` tree from MCPExtension becomes the 10.x implementations; the modern code path becomes the 11.x implementations.
- **`Concord.Host10x`** and **`Concord.Host11x`** are thin MEF-entry projects. Each references Concord.Core plus its version-pinned `Mendix.StudioPro.ExtensionsAPI`. Each implements the Core interfaces and sets `TargetMode` on Core at startup.
- The current `Terminal.csproj` (assembly name = Concord) becomes `Concord.Host11x.csproj`; the 10.x host is new.

### Build flow

`dotnet build Terminal.sln` produces three DLLs: `Concord.Core.dll`, `Concord.Host10x.dll`, `Concord.Host11x.dll`. The existing `BuildUi` target (npm + esbuild) is unchanged. `DeployToMendix` xcopies all three DLLs, `terminal.bundle.js`, `Concord.SampleData.mpk`, and the skills tree into each target's `extensions/Concord/`.

**Manifest layout — working assumption (open question).** Two subfolders, one manifest each:

```
extensions/Concord/
├── Core/                   # Concord.Core.dll, terminal.bundle.js, skills/, resources/
├── 10x/
│   ├── manifest.json       # points at ../Core + Host10x.dll
│   └── Concord.Host10x.dll
└── 11x/
    ├── manifest.json       # points at ../Core + Host11x.dll
    └── Concord.Host11x.dll
```

The plan should verify Studio Pro's manifest schema supports either this layout or a version-conditional DLL pointer in a single manifest. If a single manifest with conditional pointers works, the layout collapses to a flat folder.

### CI matrix

`dotnet test` runs against both host projects. GitHub Actions adds a matrix dimension `{ studiopro_version: [10.24.13, 11.x-latest] }` for end-to-end smoke tests. If the 10.x runner image is heavy to set up, ship unit + integration tests first and add 10.x e2e as a follow-up; do not block the W1 release on the 10.x e2e runner.

### Backward compatibility

`terminal-state.json` and the settings JSON shape stay backward-compatible. New keys (family toggles under `ConcordMcp.Catalog.*`) default-to-on when absent, matching v4.1's `Defaults() = all-on (Codex excluded)` principle. v4.1 users upgrading to 5.x keep their session state.

## W2 — Source merge of MCPExtension

### Mechanics

Use `git subtree add --prefix=src/Concord.Core/Spmcp <MCPExtension-remote> main --squash` to bring MCPExtension's `Tools/`, `Handlers/`, `Mcp/`, `Utils/`, `backport-10x/` into Concord. `--squash` keeps the initial commit clean; the full MCPExtension repo stays available as a read-only archive for anyone who needs the original history.

After the subtree add, the following MCPExtension surfaces are **deleted** (they duplicate Concord plumbing):

- `Mcp/McpServer.cs`, `Mcp/MendixMcpServer.cs` — replaced by Concord's existing in-process MCP host.
- `MenuExtension.cs`, `AIAPIEngine.cs`, `AIAPIEngineViewModel.cs` — MCPExtension's pane host, not needed.
- `.mcp.json`, `start-studiopro.bat`, `test-transports.sh` — dev tooling for the standalone repo.
- `SPMCP/` (the Mendix module project tree) — moves to `resources/Concord.SampleData/` and rebuilds as `Concord.SampleData.mpk`. Module is renamed `SPMCP → Concord.SampleData` for brand consistency.

What stays:

- `Tools/MendixAdditionalTools.cs` (~9700 lines), `Tools/MendixDomainModelTools.cs` (~4800 lines) — the 84-tool implementations.
- `Handlers/*` — supporting handlers.
- `Utils/*` — helpers.
- `backport-10x/*` — becomes the basis for `Concord.Host10x`'s implementation of Core interfaces.

### MCP host refactor

Concord's existing `StudioProActionServer.cs` registers a small static catalog (UI actions + Maia). The merged catalog is dynamic: registration depends on `TargetMode` + family toggles. The host refactors into:

- `Concord.Core/Mcp/McpServer.cs` — HTTP/SSE transport, request dispatch, unchanged on the wire.
- `Concord.Core/Mcp/ToolCatalog.cs` — given `TargetMode + Settings → List<ITool>`. Holds the curated 11.x allowlist as a static `HashSet<string>`.
- `Concord.Core/Mcp/Tools/*` — one file per family, each implementing `ITool` and depending on Core interfaces only. Existing UI action and Maia tools relocate here unchanged.
- `Concord.Core/Spmcp/Tools/*` — the merged 84-tool implementations, also implementing `ITool` but adapted to depend on Core interfaces instead of Studio Pro types directly.

The plan should scope the SPMCP-tool-adapter refactor explicitly; it's the biggest single chunk of work in W2.

### Tool naming on the wire

All Concord MCP tools advertise under one server name (`concord-mcp`) without prefix. On 11.x they don't collide with studio-pro MCP tools by name (because the curated allowlist excludes anything studio-pro MCP advertises). On 10.x there's no studio-pro MCP, so no collision possible.

## W3 — Hybrid fallback orchestration (11.x)

### The chain

1. **Tier 1 — studio-pro MCP** (11.x only). Handles read paths, basic CRUD, anything studio-pro MCP does well. Model selects via skill instructions in `mendix-tool-map-11x/SKILL.md`.
2. **Tier 2 — Concord MCP.** Handles the curated allowlist: pages, navigation, security, hard deletes, renames, sample data, diagnostics. Model selects via the same skill instructions.
3. **Tier 3 — Maia delegate.** When a Concord MCP tool fails with a known Maia-eligible recovery, the model invokes `maia__ask` with the suggested prompt. *The server never calls Maia itself* — Maia output is freeform chat and impersonating it as a tool result is misleading.
4. **Tier 4 — Manual steps to user.** When a Concord MCP tool fails with no Maia recovery, the model relays the `manual_steps` array verbatim to the terminal.

On 10.x, Tier 1 is absent (no studio-pro MCP). Tiers 2-4 operate identically.

### Error contract for Concord MCP tools

Tools with a known recovery path return errors in this shape:

```json
{
  "ok": false,
  "error": "Permission rule could not be written via Extensions API",
  "escalation": "maia-eligible",
  "maia_prompt": "In the open project, set the Read rule for entity Customer in module Sales to allow role User. Save when done.",
  "manual_steps": [
    "Open Security → Module security → Sales → Access rules",
    "Select Customer → Read → check User role",
    "Save"
  ]
}
```

`escalation` values:
- `none` — surface error directly, no recovery attempted.
- `maia-eligible` — try `maia__ask` with `maia_prompt`. If Maia succeeds, treat as success. If Maia fails, fall to `manual_steps`.
- `manual` — go straight to `manual_steps`, no Maia attempt.

Tools without a known recovery omit `escalation` (model surfaces error directly).

### The 11.x curated allowlist (first cut)

Source of truth: `Concord.Core/Spmcp/Studio11xAllowlist.cs`. Initial contents:

| Family | Tools kept on 11.x | Reasoning |
|---|---|---|
| Pages | `generate_overview_pages`, `delete_document` | studio-pro MCP has read-only page tools |
| Navigation | `manage_navigation` | not in studio-pro MCP write surface |
| Security | `read_security_info`, `read_entity_access_rules`, `read_microflow_security`, `audit_security` | studio-pro MCP security coverage is weak |
| Domain Model | `delete_model_element`, `rename_*` family (`rename_entity`, `rename_attribute`, `rename_association`, `rename_document`, `rename_module`, `rename_enumeration_value`), `set_documentation`, `arrange_domain_model` | hard deletes + reference-safe renames + layout |
| Microflows | `exclude_document`, `set_microflow_url`, `modify_microflow_activity`, `insert_before_activity` | gaps in studio-pro MCP edit surface |
| Project | `read_runtime_settings`, `set_runtime_settings`, `read_configurations`, `set_configuration` | runtime/config write surface |
| Data & Sample | `save_data`, `generate_sample_data`, `read_sample_data`, `setup_data_import` | not in studio-pro MCP at all (requires Concord.SampleData module imported) |
| Diagnostics | `check_model`, `check_project_errors`, `get_studio_pro_logs`, `get_last_error`, `analyze_project_patterns` | studio-pro MCP introspection gaps |

The list is reviewed each Studio Pro release. If studio-pro MCP improves at, say, security, those tools move out of the allowlist in the next Concord release.

The mendix-tool-map skill pack mirrors this allowlist verbatim. A build-time check parses both and fails CI if they disagree.

**Open question for the plan:** the actual allowlist needs reconciliation against a live `mendix-studio-pro__tools/list` snapshot from a current Studio Pro 11.x build. The table above is a starting point, not a final commitment.

### Why no proxy

Concord MCP does not call studio-pro MCP. The catalogs don't overlap by name, so the model picks the right server top-level via skill instructions. This keeps Concord's process model simple: one HTTP server, no inbound proxy, no schema-tracking against a moving target.

## W4 — Skills, settings, UX

### Bundled skill packs

The seven existing pattern packs stay version-agnostic (no change). A new pack ships in two flavors on disk:

```
skills/mendix-tool-map/
├── 10x/SKILL.md      # 10.x tool roster + "concord is sole Mendix tool source"
└── 11x/SKILL.md      # 11.x tool roster + tier-routing rules (mirrors allowlist verbatim)
```

`SkillInstaller` picks the flavor matching `TargetMode` and writes it to `.claude/skills/mendix-tool-map/SKILL.md` (or `.github/skills/` / `.codex/skills/` per CLI). User-facing pack name is always `mendix-tool-map`; only the content differs.

Each Save in Settings → Skills re-renders the right flavor — same refresh model as today's bundled packs. The mendix-tool-map content mirrors `Studio11xAllowlist.cs` so the registry and the skill text never drift.

### Settings → Concord MCP — new shape

```
Settings → Concord MCP
├── ☑ Enable Concord MCP server                          (master, existing)
│
├── Studio Pro UI actions
│   └── ☑ Enable                                          (existing)
│
├── Maia integration (Windows only)
│   └── ☑ Enable                                          (existing, disabled on Mac)
│
└── Mendix tool families                                  (NEW)
    ├── ☑ Domain Model                ☑ Microflows
    ├── ☑ Pages                       ☑ Navigation
    ├── ☑ Security                    ☑ Workflows
    ├── ☑ Constants & Enumerations    ☑ Data & Sample
    ├── ☑ Diagnostics                 ☑ Project & Settings
    │
    └── [ Import sample-data module ]   (button — visible when Data & Sample is on
                                         and Concord.SampleData not yet in project)
```

Family toggles default to ON. Per-tool granularity is out of scope for v5.

### Settings → Studio Pro MCP

**Hidden when `TargetMode == Studio10x`** — the section's only function is writing `.mcp.json` / `~/.codex/config.toml` entries pointing at Studio Pro's built-in MCP, which doesn't exist on 10.x.

### Settings → Skills

Unchanged in layout. The `mendix-tool-map` pack is silently included in the install list (no separate user toggle); it's a dependency of the other packs in spirit. The installed flavor follows detected `TargetMode`.

### Settings → About

Adds one line: `Studio Pro detected: <version> (host: Concord.Host{10,11}x.dll)`. Confirms at a glance which host loaded.

### First-run

v4.1's `TryFirstRunApply` semantics extend cleanly: on a fresh project, all family toggles default ON, the appropriate skill flavor installs, and the Studio Pro MCP section is visible only on 11.x. The first-run flush mechanism added in commit `7d13106` handles JS-side late binding — no new flush logic needed.

### Sample-data import flow

"Import sample-data module" invokes the version-appropriate Studio Pro module-import API against `extensions/Concord/Core/resources/Concord.SampleData.mpk`. On success, the button replaces itself with "Sample-data module imported ✓". On failure (most likely cause: project already has a conflicting version), Concord surfaces Studio Pro's error verbatim — no clever conflict resolution.

The module-import API differs meaningfully between 10.x and 11.x; the wrapper is host-specific (`Concord.Host10x/ModuleImportWrapper.cs`, `Concord.Host11x/ModuleImportWrapper.cs`).

## Migration

### From Concord 4.1 on 11.x → Concord 5.x

In-place upgrade. `DeployToMendix` removes the old `Concord.dll` and writes the new structure. Settings JSON shape is additive — new family-toggle keys default-to-on when absent. Users see the new Settings sections after Studio Pro restart.

### From SPMCP on 10.x → Concord 5.x

Install Concord into `extensions/Concord/`, remove `extensions/MCPExtension/` manually. In the project, optionally remove the old `SPMCP` module (`App → Modules → SPMCP → Delete`) and re-import as `Concord.SampleData` via the new Settings button. We ship a one-page migration note in `DEPLOYING.md`. We do **not** auto-migrate.

### From Concord 4.1 + MCPExtension both installed on 11.x → Concord 5.x

Same as above. If the user forgets to remove `extensions/MCPExtension/`, two MCP servers run side-by-side (Concord on 7783 + SPMCP on 3001). Tools surface as duplicates and the model may misroute. Concord logs a startup warning when it detects an `extensions/MCPExtension/` folder alongside itself.

## Testing strategy

- **Unit tests in `Concord.Core.Tests`.** ToolCatalog registration logic (TargetMode + toggle state → expected tool set). Allowlist-vs-tool-map drift check (parses both, asserts equality). Settings JSON shape backward compatibility (loads v4.1 files without loss).
- **Per-host unit tests.** Module-import wrapper, version probe, manifest-load smoke. Same test shape, two project files.
- **Integration tests (existing xunit suite).** MCP `tools/list` returns expected catalog under each TargetMode. Family toggles filter correctly. Allowlist tools dispatch against a mock modeling service.
- **End-to-end smoke (CI matrix).** `{ studiopro_version: [10.24.13, 11.x-latest] }` — installs the version, deploys Concord, opens pane, hits `concord-mcp/health`, calls one tool per family. Windows + Mac split kept.
- **Skill-pack render test.** Asserts rendered `mendix-tool-map/SKILL.md` matches the expected fixture per TargetMode.
- **Manual smoke checklist** in the plan: open on 10.24.13, 11.10, 11.x-latest; verify panel layout; round-trip one tool per family; import sample-data module; run/stop/save_all UI actions.

## Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Studio Pro 10.x and 11.x MEF loaders both try to load the wrong-version DLL | Med | Pane fails to open | Two-subfolder manifest layout; verify in plan-time spike |
| The 11.x curated allowlist mis-cherrypicks | Med | Model misroutes or duplicates tools | Plan-time `tools/list` reconciliation against current studio-pro MCP; allowlist is data, easy to amend |
| `git subtree add` creates a huge initial commit obscuring blame | Low | Annoying but recoverable | Use `--squash`; preserve full MCPExtension repo as archive |
| 10.x users hit Extensions API surfaces that differ from `backport-10x/` expectations | Med | Tool calls fail at runtime, not load | Per-host integration tests against real 10.24.13; clear errors with `escalation: manual` |
| Module-import API behavior differs between 10.x and 11.x | Med | "Import sample-data" works on one version, not the other | Host-specific wrapper; integration test on both |
| Maia escalation prompts drift from Maia's actual capabilities | Med | Confused Maia responses | Treat `maia_prompt` as part of tool contract; review during authoring; capture failures in telemetry |
| Mixed fleets (some 10.x projects, some 11.x) | Low | None — both DLLs ship in every deploy, only matching one loads | Document in DEPLOYING.md |
| Concord MCP + studio-pro MCP both registered on 11.x — model picks wrong | Med | Misrouted calls | mendix-tool-map skill explicit; family toggles let users silence Concord; telemetry tracks `tools/list` source per call |

## Open questions for the implementation plan

1. **Manifest schema.** Does Studio Pro's manifest support a version-conditional DLL pointer, or do we need two subfolders? Plan-time spike.
2. **Exact 11.x allowlist.** Reconciliation against a live `mendix-studio-pro__tools/list` snapshot from a current 11.x build.
3. **SPMCP-tool-adapter refactor.** The 84 tools currently depend on Studio Pro types directly. Plan needs to scope adapting them to depend on Core interfaces — biggest single chunk of W2 work.
4. **Module-import API spike.** Concrete differences between 10.x and 11.x module-import surfaces.
5. **Preview release.** Whether to ship Concord 5.x as a preview alongside 4.1 during a transition window, or do a clean cutover at the major version. Default assumption: clean cutover.

## Sub-spec / sub-plan map

After this umbrella is approved and a single implementation plan is drafted, the plan decomposes into per-workstream phases:

- **Phase W1** — Repo split into Core + Host10x + Host11x; manifest spike; CI matrix.
- **Phase W2** — `git subtree add` of MCPExtension; tool-adapter refactor; ToolCatalog implementation.
- **Phase W3** — Allowlist reconciliation; escalation contract for tools that need Maia/manual recovery.
- **Phase W4** — Settings panel additions; mendix-tool-map skill pack authoring; SkillInstaller TargetMode-aware flavor selection.

Each phase has its own integration test gates and ships as part of one Concord 5.x release.
