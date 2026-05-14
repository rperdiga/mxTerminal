# Concord skills + rules — split by Studio Pro version

> Status: design · 2026-05-14 · target release: v5.0.0

## Context

Concord installs two kinds of always-loaded content into the user's Mendix project so the AI agent has both the doctrine and the operational knowledge it needs:

- **Rules** — `rules/concord-*.md` files copied into `.claude/rules/` (and the Codex + Copilot equivalents), `@`-imported into the per-CLI top-level instructions file (CLAUDE.md / AGENTS.md / .github/copilot-instructions.md).
- **Skills** — 7 skill packs copied into `.claude/skills/` (and the per-CLI equivalents) as on-demand reference.

Today both hosts resolve the bundled content identically:

```
Concord.Host10x.TerminalPaneExtension : ResolvePath("skills") + ResolvePath("rules")
Concord.Host11x.TerminalPaneExtension : ResolvePath("skills") + ResolvePath("rules")
```

Both point at the same `<ext>/skills/` and `<ext>/rules/` folders inside the .mxmodule. There is **no version-aware switch** in this layer. The only version-aware code is `Studio11xAllowlist.cs`, which gates which concord-mcp tools get *registered* on 11.x (the "45 tools" allowlist) so they don't clobber the built-in `mendix-studio-pro` MCP. On 10.x the full ~88-tool concord-mcp catalog is registered because that MCP doesn't exist to coexist with.

This produces a real correctness problem on 10.x:

- The shipped `rules/concord-build-rules.md` instructs the agent to call `mcp__mendix-studio-pro__ped_*` as the primary tool family. That MCP does not exist on Studio Pro 10.x–11.9. Skills like `skills/mendix-microflow-update/SKILL.md` reference `mcp__mendix-studio-pro__ped_update_document` by name. On 10.x the agent gets pointed at tools that aren't there.
- 10.x has no Maia. The 11.x doctrine ("Maia owns pages", `maia__*` recovery ladder, `pg_write_page` second-opinion path) is dead weight at best, misdirection at worst.
- On 10.x the right path is concord-mcp's full SPMCP handler catalog plus direct filesystem manipulation for styling/jsactions. The current shipped content does not teach this.

## Goal

Two distinct sets of skills + rules, one per host. Both sets are getting a refresh — 10.x is new content; 11.x is an in-place doctrine update to reflect the current 45-tool concord-mcp catalog and the new 4-tier sequencing.

| Host | Studio Pro versions | Bundled content folder | Doctrine focus |
|---|---|---|---|
| `Concord.Host11x` | 11.10.0+ | `skills/`, `rules/` (existing — **refreshed**) | 4-tier hierarchy: `mendix-studio-pro` MCP → `concord-mcp` (45 tools) → Maia delegate → direct FS |
| `Concord.Host10x` | 10.24.13 – 11.9.x | `skills-10x/`, `rules-10x/` (new) | 2-tier hierarchy: `concord-mcp` (~88 tools) → direct FS. No Maia. No `mendix-studio-pro` MCP references. |

The user's directive verbatim:

> 11.10: Skills focused on tools available; use `mendix-studio-pro` MCP first, then `concord-mcp` (45 tools), then Maia delegate, then direct FS for styling / things outside Mendix.
>
> 10.24.13–11.9.x: Skills focused on tools available; use `concord-mcp` (88 tools), then direct FS for styling / things outside Mendix. No Maia (it does not do anything well on 10.x).

### Why the 11.x content also needs work

The current 11.x rules predate the concord-mcp tool expansion that produced the 45-tool 11.x allowlist. Two doctrine gaps result:

1. **Concord-mcp is under-represented as a distinct tier.** `concord-build-rules.md` §1 lists concord-mcp tools but mostly limited to UI actions + Maia bridge. Domain-model gap-fillers (`rename_entity`, `rename_attribute`, `rename_association`, `rename_document`, `rename_module`, `rename_enumeration_value`, `delete_model_element`, `set_documentation`, `arrange_domain_model`), navigation (`manage_navigation`), security audit tools (`read_security_info`, `read_entity_access_rules`, `read_microflow_security`, `audit_security`), runtime/configuration tools (`read_runtime_settings`, `set_runtime_settings`, `read_configurations`, `set_configuration`), and diagnostics (`check_model`, `check_project_errors`, `get_studio_pro_logs`, `get_last_error`, `analyze_project_patterns`) are part of the 45-tool catalog but mostly absent from the doctrine. The agent will not call tools the rules don't mention.

2. **Maia is conflated with concord-mcp.** Because `maia__*` tools are physically routed through the concord-mcp server, the current rules list them under "Concord MCP server" in §1. That blurs the new 4-tier sequencing: in the user's model concord-mcp (the 45 model-side tools) comes before Maia delegate. Splitting Maia into its own tier in the rules makes the priority ordering explicit, even if the transport remains the same.

3. **"Direct FS for styling" is permitted but not promoted.** Today the rules steer file-domain work through `mcp__mendix-studio-pro__write_file` (the studio-pro MCP file domain, registered for `/themes` and `/jsactions`). The user wants direct FS explicitly listed as a tier for styling and things-outside-Mendix. The studio-pro MCP file domain remains the preferred path for `/themes` / `/jsactions` (governed; respects model state); direct FS becomes the explicit fallback and the path for any file path outside the registered domains.

## Approach — two complete bundle directories

We add `skills-10x/` and `rules-10x/` to the repo root as full, independent content sets. `Concord.Host10x` resolves these instead of `skills/` / `rules/`. `Concord.Host11x` is unchanged. The skill/rules **installation code** (`SkillInstaller`, `RulesInstaller`, `SettingsApplyHelper`) does not need version awareness — it operates on whatever `bundledSkillsRoot` / `bundledRulesRoot` the host hands it.

Why two complete directories, not an overlay:

- Content divergence is doctrinal, not cosmetic. The 11.10 hierarchy is 4-tier; the 10.x hierarchy is 2-tier. The 11.10 microflow-update skill is built around `mcp__mendix-studio-pro__ped_*`; the 10.x equivalent is built around `mcp__concord-mcp__*`. Overlaying would leak 11.x prose onto 10.x.
- Reuses an existing pattern at one layer up: `Concord.Core.csproj` already includes both `skills/` and `skills-mac/` as separate content trees. Adding `skills-10x/` and `rules-10x/` is symmetric.
- Each set can evolve independently; no implicit cross-coupling between 10.x and 11.x doctrine.

Cost: shared content (where it exists) is duplicated. Acceptable because the shared content is small — most of the rules text is version-specific, and the skills have substantially different surface areas per version.

## Scope of changes

### 1a. New bundled content for 10.x (the bulk of the work)

```
rules-10x/
  concord-build-rules.md          (rewritten — 10.x doctrine)
  concord-model-discipline.md     (rewritten — concord-mcp tool names; PED-equivalents via SPMCP handlers)
  concord-pages-and-themes.md     (rewritten — no Maia delegate; concord-mcp page tools + direct FS for themes)

skills-10x/
  mendix-microflow-common/SKILL.md   (rewritten)
  mendix-microflow-syntax/SKILL.md   (mostly portable — expression/XPath syntax is version-agnostic; tool-name references swapped)
  mendix-microflow-update/SKILL.md   (rewritten — mutation tool names swapped to concord-mcp)
  mendix-page-gen/SKILL.md           (rewritten — no Maia; concord-mcp page tools only)
  mendix-view-entities/SKILL.md      (rewritten — concord-mcp OQL-equivalent tool names)
  mendix-workflow-common/SKILL.md    (rewritten)
  mendix-workflow-update/SKILL.md    (rewritten — mutation tool names swapped)
```

For each 10.x file, the changes are roughly:
- Top-level "Tools in this environment" header lists `mcp__concord-mcp__*` tools.
- Tool-name references in the body swapped from `mcp__mendix-studio-pro__ped_*` to the concord-mcp equivalents.
- Maia ladders, second-opinion paths, `maia__*` introspection — removed.
- `pg_write_page` references (Maia-fronted page write) — replaced with concord-mcp page-generation tools.
- Verification cadence (§12 in build-rules) simplified: error checks stay; the studio-pro MCP `ped_check_errors` is replaced with the concord-mcp equivalent. `save_all` / `refresh_project` / `run_app` cycle stays — those are concord-mcp tools, same names on both versions.

The exact concord-mcp tool names for the 10.x rewrite are read directly from the registered handler set (`MendixDomainModelTools`, `MendixAdditionalTools`, `UiActionsBootstrap`) at content-write time, not invented.

### 1b. In-place refresh of 11.x bundled content

```
rules/
  concord-build-rules.md          (refreshed — §1 tool hierarchy restructured; concord-mcp tier broken out from Maia)
  concord-model-discipline.md     (refreshed — concord-mcp domain-model gap-fillers referenced)
  concord-pages-and-themes.md     (refreshed — Maia delegate framed as tier 3, not tier 1; concord-mcp page tools tier 2)

skills/
  mendix-microflow-common/SKILL.md   (refreshed — concord-mcp microflow gap-fillers referenced)
  mendix-microflow-syntax/SKILL.md   (verify — likely no change; expression/XPath syntax is doctrine-stable)
  mendix-microflow-update/SKILL.md   (refreshed — concord-mcp `modify_microflow_activity`, `insert_before_activity` referenced as fallback for studio-pro MCP gaps)
  mendix-page-gen/SKILL.md           (refreshed — sequencing: studio-pro MCP PED page → concord-mcp `generate_overview_pages` / `delete_document` → Maia delegate)
  mendix-view-entities/SKILL.md      (verify — view-entity authoring is studio-pro MCP territory; minimal change expected)
  mendix-workflow-common/SKILL.md    (refreshed — concord-mcp diagnostics + rename tools referenced)
  mendix-workflow-update/SKILL.md    (refreshed — same)
```

For each 11.x file, the changes are:

1. **Tool hierarchy section** rewritten to surface the 4-tier ordering explicitly:
   - **Tier 1 — `mcp__mendix-studio-pro__*`**: PED, OQL, knowledge base, file domain (`/themes`, `/jsactions`).
   - **Tier 2 — `mcp__concord-mcp__*` (45 tools)**: UI actions (`run_app`, `stop_app`, `save_all`, `refresh_project`, `get_app_status`, `get_active_run_configuration`), domain-model gap-fillers (`delete_model_element`, all `rename_*`, `set_documentation`, `arrange_domain_model`), microflow gap-fillers (`exclude_document`, `set_microflow_url`, `modify_microflow_activity`, `insert_before_activity`), pages (`generate_overview_pages`, `delete_document`), navigation (`manage_navigation`), security audit (`read_security_info`, `read_entity_access_rules`, `read_microflow_security`, `audit_security`), runtime/configuration (`read_runtime_settings`, `set_runtime_settings`, `read_configurations`, `set_configuration`), diagnostics (`check_model`, `check_project_errors`, `get_studio_pro_logs`, `get_last_error`, `analyze_project_patterns`).
   - **Tier 3 — Maia delegate via `mcp__concord-mcp__maia__*`**: `maia__ask`, `maia__send`, `maia__status`, `maia__wait`, `maia__reset`, plus introspection (`maia__busy`, `maia__ping`, `maia__health`, `maia__new_chat`). Windows only. Used when Tiers 1+2 don't cover the operation (e.g. authoring a page with rich content beyond `generate_overview_pages`).
   - **Tier 4 — Direct filesystem**: explicit fallback for styling files, custom JS actions outside the registered domains, and any path the studio-pro MCP file domain doesn't cover. Preferred path remains `mcp__mendix-studio-pro__write_file` *inside* the registered roots (`/themes`, `/jsactions`); direct FS is for everything outside.
2. **Maia ladder kept** in `concord-pages-and-themes.md`, but reframed as tier 3, not tier 1. Page authoring sequence: try studio-pro MCP PED page write first, then concord-mcp `generate_overview_pages` for list/detail scaffolding, then Maia delegate for richer authoring, then direct FS for theme/CSS.
3. **Newly-referenced concord-mcp tools get a one-line "use this when" callout** so the agent knows the trigger — e.g. `audit_security` → "before shipping, surface anonymous-role / open-grant findings"; `arrange_domain_model` → "after batch entity creates, before screenshotting the domain model".
4. **`Studio11xAllowlist.cs` is the source of truth** for the concord-mcp tool list referenced in 11.x content — read it at content-write time. If the allowlist changes, this is the file that says "rules need to be refreshed too".

### 2. Build infrastructure

`src/Concord.Core/Concord.Core.csproj` — add two `<Content Include>` blocks symmetric to the existing `skills-mac/` block:

```xml
<Content Include="..\..\skills-10x\**\*">
  <Link>skills-10x\%(RecursiveDir)%(Filename)%(Extension)</Link>
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
  <Visible>false</Visible>
</Content>
<Content Include="..\..\rules-10x\**\*">
  <Link>rules-10x\%(RecursiveDir)%(Filename)%(Extension)</Link>
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
  <Visible>false</Visible>
</Content>
```

This packages the new content into the deployed extension layout. Same machinery that ships `skills/` and `rules/` today.

### 3. Host10x resolve switch

`src/Concord.Host10x/Pane/TerminalPaneExtension.cs` — change four call-site pairs that currently do:

```csharp
var bundledSkillsRoot = extensionFileService.ResolvePath("skills");
var bundledRulesRoot  = extensionFileService.ResolvePath("rules");
```

to:

```csharp
var bundledSkillsRoot = extensionFileService.ResolvePath("skills-10x");
var bundledRulesRoot  = extensionFileService.ResolvePath("rules-10x");
```

Call sites are at approximately lines 123/124 (Open), 450/451, 569/570, 633/634 — verify exact lines at edit time.

`Concord.Host11x.TerminalPaneExtension.cs` — **unchanged**. Still resolves `"skills"` and `"rules"`.

No changes to `SkillInstaller`, `RulesInstaller`, `SettingsApplyHelper`, `BundledSkillReader`, or anything in `Concord.Core`. Those operate on whatever bundle root the host hands them — they're already correctly version-blind at that layer.

### 4. Tests

- Existing `SettingsApplyHelperTests` uses a synthetic temp-dir bundle root → continues to pass unchanged.
- Existing `RulesInstaller` / `SkillInstaller` tests likewise pass unchanged — they don't depend on bundle name.
- **Bundle-resolution smoke tests** (one per host): assert Host10x resolves a bundle root ending in `skills-10x` / `rules-10x`; assert Host11x resolves `skills` / `rules`. Trivial path-string checks; the paths are logged at the resolve call sites already.
- **Allowlist-doctrine sync test** (new, in-scope) — see §4a below.
- **No content-shape tests** for the markdown beyond the allowlist-doctrine sync — prose review is manual.

### 4a. Allowlist-doctrine sync test (in-scope)

The test guards against the drift risk called out earlier: a tool added to or renamed in `Studio11xAllowlist` (or in the 10.x bootstrap path) that the doctrine never gets updated to reference. It lives in `Concord.Core.Tests` since it touches `Studio11xAllowlist` and reads shipped bundle content.

**Shape — 11.x side (assertion target: `Studio11xAllowlist.All`):**

```
For each tool_name in Studio11xAllowlist.All except SkipList:
  assert tool_name appears as a literal string in at least one .md file under rules/ ∪ skills/
SkipList = { "maia__force_tier" }   # explicitly debug-only per existing rules
Failure message includes: which tool is unreferenced, and which file the doctrine probably wants it in (heuristic: family ↔ skill mapping table inline in the test).
```

**Shape — 10.x side (assertion target: a 10.x catalog enumerated at test setup):**

```
Build a ToolCatalog, run Host10x's bootstrap path against it (UiActionsBootstrap + the SPMCP handler set used on 10.x — NO MaiaToolsBootstrap).
For each registered tool_name except SkipList_10x:
  assert tool_name appears as a literal string in at least one .md file under rules-10x/ ∪ skills-10x/
Plus negative assertions on the 10.x bundle:
  assert no .md file under rules-10x/ ∪ skills-10x/ contains the substring "mcp__mendix-studio-pro__"
  assert no .md file contains the substring "maia__" or "pg_write_page"
```

**Failure mode is informative, not opaque.** The assertion message names the tool, the bundle (10.x or 11.x), and (heuristically) the file the test would expect the reference to live in based on tool family. So when this test fails 8 months from now, a reader who hasn't seen this spec can fix it.

**Implementation notes:**

- The test runs as a normal xUnit test in `Concord.Core.Tests`. It reads `.md` content **from the repo source directories** (`<repo>/skills/`, `<repo>/rules/`, `<repo>/skills-10x/`, `<repo>/rules-10x/`), not from a build output copy. The repo root is located by walking up from the test assembly's location until a sentinel file (`README.md` at the repo root) is found. This avoids the trap that `Concord.Core`'s `<Content Include>` blocks copy bundles only to `Concord.Core`'s output, not to `Concord.Core.Tests`'s output.
- The Host10x bootstrap path is *enumerated*, not *executed* against real Studio Pro — there's no Studio Pro running under unit tests. A small refactor may be needed to make 10.x's tool-registration set inspectable without side effects (likely a `Studio10xToolNames` accessor or a `ToolCatalog`-builder that doesn't require a live `IModelApp`). Scope-included; trivial.
- Substring matching is deliberately permissive — we want `mcp__concord-mcp__rename_entity` and bare `rename_entity` and `\`rename_entity\`` to all count. The test is doctrinal-coverage, not exact-prose enforcement.
- Skip-list is explicit and reviewed at test time; not a comment-driven mechanism. `maia__force_tier` is in the skip-list because the existing 11.x rules explicitly say "do not use unless the user asks for transport-tier diagnostics" — that's a deliberate exclusion, not a doctrine gap.

### 5. .mxmodule rebuild

Studio Pro re-bakes the `.mxmodule` from disk at export time. Adding `skills-10x/` and `rules-10x/` directories at the repo root means Neo's manual UI export step in Studio Pro **must run** to produce a `.mxmodule` that contains the new content. Add to the release checklist for this version. Same gotcha already documented in `CLAUDE.md` ("Things that bit us before") — applies identically here.

## Non-goals

- **No version-aware logic in `Concord.Core`.** Resolution stays where it belongs — at the host boundary. Core stays version-blind.
- **No runtime tool-name translation.** Each bundle speaks its own version's tool names directly; we don't ship a translation layer.
- **No changes to the settings modal UI.** Per-CLI skill/rules toggles work identically on both hosts. The user picks which CLIs get the bundle; the host picks which bundle to ship.
- **No changes to `Studio11xAllowlist`.** That gates *tool registration* in the concord-mcp server, a separate concern from *skill/rules content*. It's already correct. The 11.x content refresh *reads from* the allowlist; it does not modify it.
- **No invention of new tools.** The 11.x refresh only references tools that already exist in the registered handlers. The 10.x rewrite likewise references only what `Concord.Host10x` registers. If a tool the doctrine wants doesn't exist, it gets a deferred-follow-up note, not a hallucinated tool name.
- **No restructuring of the rules file split** (build-rules vs model-discipline vs pages-and-themes). The current three-file decomposition stays; only the content inside each changes.

## Risks and open questions

1. **Content drift between the two sets.** Two bundles means two places to update when a shared truth changes (e.g. layout math in `mendix-microflow-update`). Mitigation: at file creation time, copy the existing skill verbatim, then prune/swap. The structural skeleton stays parallel so future diffs are easy to spot.
2. **Allowlist-doctrine staying in sync over time.** The 11.x content now references the concord-mcp 45-tool catalog explicitly. If `Studio11xAllowlist` changes (a tool added, removed, or renamed) and the rules aren't refreshed, the agent will reference a tool that doesn't exist or miss one that does. **Mitigated in-scope** by the allowlist-doctrine sync test (§4a) — that test runs in CI and fails on drift, surfacing the gap on the same PR that introduced it. Secondary mitigation: a comment block atop `Studio11xAllowlist.cs` pointing at `rules/concord-build-rules.md` as the doctrine that co-evolves; the reverse comment atop the rules file. The cross-comments are belt-and-suspenders for the failing-test message.
3. **Naming of the 10.x bundle root.** Chosen: `skills-10x` / `rules-10x`. Alternative considered: `skills-legacy` (rejected — 10.x is not legacy; it's a supported version) or `skills-pre1110` (rejected — too verbose). The `-10x` suffix mirrors `Concord.Host10x` naming.
4. **What if 11.10's `mendix-studio-pro` MCP gains tools 10.x's concord-mcp doesn't expose?** Already handled by `Studio11xAllowlist` at the tool registration layer; this design doesn't touch that. Content evolves independently per bundle.
5. **Smoke-test reach.** A path-string check on `bundledSkillsRoot` confirms the wiring is correct but doesn't verify the actual deployed bundle has the right content. That's an integration concern (does the .mxmodule contain `skills-10x/`?) — verified manually at marketplace upload time, not in CI.
6. **Maia ladder narrative survives in 11.x rules.** The 4-tier sequencing demotes Maia from "primary path for pages" to "tier 3 fallback", but the existing Maia ladder, second-opinion tiebreaker, and 3-consecutive-failure stop rule (§2, §3) are still correct *operationally* when Maia IS used. We keep that content intact and reframe only the entry conditions — when to reach for Maia in the first place. Risk: writers reading only the §1 hierarchy and missing §2's Maia operational rules. Mitigation: cross-reference §2 from §1 explicitly in the refresh.

## Out of scope for this design (deferred follow-ups)

- **Auto-generating skill/rules tool-name lists** from the concord-mcp tool catalog at build time. The §4a allowlist-doctrine sync test closes the *drift detection* gap; auto-generation would close the *manual-fix* gap. Worth considering after the manual split lands and we see how often the test catches real drift.
- A "diagnostics dashboard" that shows the agent which bundle was installed (helps users when they switch Studio Pro versions across the same project). Today a user can inspect `.claude/rules/concord-build-rules.md` directly to tell.

## Acceptance criteria

1. **10.x bundle exists.** Repo contains `skills-10x/` (7 packs) and `rules-10x/` (3 files) with content that speaks only the tool surface available on Studio Pro 10.24.13–11.9.x. No `mcp__mendix-studio-pro__*` references. No `maia__*` references. No `pg_write_page` references.
2. **11.x bundle refreshed.** Existing `rules/concord-build-rules.md` §1 (tool hierarchy) restructured to the 4-tier ordering (studio-pro MCP → concord-mcp → Maia delegate → direct FS), with Maia split out from the concord-mcp tier. Skills updated to reference the previously-absent concord-mcp tools where the operation calls for them (renames, security audit, navigation, configurations, diagnostics).
3. **Host wiring**: `Concord.Host10x.TerminalPaneExtension` resolves `"skills-10x"` and `"rules-10x"` at all four call sites. Host11x untouched.
4. **Build packaging**: `Concord.Core.csproj` packages both new directories into the deployed extension layout alongside `skills/`, `skills-mac/`, `rules/`.
5. **Tests**: existing 324 tests stay green. New tests: (a) Host10x and Host11x path-string smoke tests for bundle name resolution; (b) allowlist-doctrine sync test that fails when a `Studio11xAllowlist` tool is missing from the 11.x bundle, when a Host10x-registered tool is missing from the 10.x bundle, or when a forbidden reference (`mcp__mendix-studio-pro__*`, `maia__*`, `pg_write_page`) appears in the 10.x bundle.
6. **.mxmodule**: building the `.mxmodule` produces a module that contains both `skills/` + `rules/` (refreshed) and `skills-10x/` + `rules-10x/` (new) directories.
7. **Cross-version manual verification**: opening a Mendix project with Concord installed, on 10.24.13 and on 11.10.0, drops different rule content into the project's `.claude/rules/concord-build-rules.md` — version-appropriate doctrine, no dead tool references on either side.
8. **Allowlist-doctrine sync**: every tool in `Studio11xAllowlist` appears at least once in the 11.x rules or skills with a "use this when" callout. Every tool in `Concord.Host10x`'s registered set appears at least once in the 10.x content. The "agent will not call tools the rules don't mention" gap is closed for both versions.
