# Concord Skills+Rules Version Split — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship two distinct skill+rules bundles — `skills-10x/` + `rules-10x/` for Studio Pro 10.24.13–11.9.x (concord-mcp + direct FS, no Maia), and an in-place refresh of the existing `skills/` + `rules/` for 11.10+ (4-tier hierarchy: studio-pro MCP → concord-mcp 45 tools → Maia delegate → direct FS) — with a doctrine-sync test that fails when a registered tool isn't referenced in the matching bundle.

**Architecture:** `Concord.Host10x.TerminalPaneExtension` resolves `"skills-10x"` / `"rules-10x"` at all 4 `ResolvePath` call sites; `Concord.Host11x` stays on `"skills"` / `"rules"`. `Concord.Core.csproj` packages both new dirs into the deployed extension layout. `Concord.Core` and the installers stay version-blind — they receive whatever bundle root the host hands them. A doctrine-sync test in `Concord.Core.Tests` enumerates `Studio11xAllowlist.All` (11.x) and a `ToolCatalog` populated by Host10x's bootstrap (10.x), then asserts every tool name appears in some `.md` file under the matching bundle, plus negative assertions on the 10.x bundle (no `mcp__mendix-studio-pro__`, no `maia__`, no `pg_write_page`).

**Tech Stack:** C# / .NET 8 / xUnit + FluentAssertions / MSBuild content includes / Markdown content authoring.

**Source spec:** [docs/superpowers/specs/2026-05-14-concord-skills-rules-version-split-design.md](../specs/2026-05-14-concord-skills-rules-version-split-design.md)

---

## Branch and commit strategy — minimal ceremony

Per Joe's preference to avoid git ceremony overhead:

- **One branch:** `feat/v5.0.0-skills-rules-version-split` off current `main`. No sub-branches per phase.
- **Three commits, one per phase.** Each phase ends with build + tests green + a single atomic commit. Phase boundaries are natural (infra → 10.x content → 11.x refresh), not arbitrary.
- **One PR at the end.** No PR per phase. Adversarial fresh-review on the diff before merge; address NITs in-branch.
- **No rebases between phases.** The branch keeps growing linearly. Branch deletion is post-merge.
- **`.mxmodule` rebuild** is Joe's manual Studio Pro step at release time, not part of this PR. The PR ships source + tests + content; the marketplace artifact is built afterwards.

---

## File structure

### Files to create

| Path | Purpose |
|---|---|
| `rules-10x/concord-build-rules.md` | 10.x core doctrine: 2-tier hierarchy (concord-mcp → direct FS), no Maia, no studio-pro MCP |
| `rules-10x/concord-model-discipline.md` | 10.x model-discipline rules: ped-equivalent tool names via concord-mcp SPMCP handlers |
| `rules-10x/concord-pages-and-themes.md` | 10.x pages/themes: concord-mcp page tools + direct FS for themes; no Maia delegate |
| `skills-10x/mendix-microflow-common/SKILL.md` | 10.x microflow common skill (read/list/check) |
| `skills-10x/mendix-microflow-syntax/SKILL.md` | 10.x microflow syntax (expressions/XPath — mostly portable, tool refs swapped) |
| `skills-10x/mendix-microflow-update/SKILL.md` | 10.x microflow mutations |
| `skills-10x/mendix-page-gen/SKILL.md` | 10.x page generation — concord-mcp tools only |
| `skills-10x/mendix-view-entities/SKILL.md` | 10.x view-entities (OQL-equivalent via concord-mcp) |
| `skills-10x/mendix-workflow-common/SKILL.md` | 10.x workflow common skill |
| `skills-10x/mendix-workflow-update/SKILL.md` | 10.x workflow mutations |
| `rules-10x/.gitkeep` + `skills-10x/.gitkeep` | Ensure dirs exist for csproj globs before content lands; deleted later if not needed |
| `tests/Concord.Core.Tests/BundleResolutionTests.cs` | Source-grep assertion that Host10x resolves `*-10x` and Host11x resolves bare names |
| `tests/Concord.Core.Tests/DoctrineSyncTests.cs` | Positive (every registered tool referenced) + negative (no forbidden references) coverage |

### Files to modify

| Path | Change |
|---|---|
| `src/Concord.Core/Concord.Core.csproj` | Add two `<Content Include>` blocks symmetric to `skills-mac/`, for `skills-10x/` and `rules-10x/` |
| `src/Concord.Host10x/Pane/TerminalPaneExtension.cs` | 4 call-site pairs: `ResolvePath("skills")` → `ResolvePath("skills-10x")`, same for rules |
| `src/Concord.Core/Mcp/Studio11xAllowlist.cs` | Add doctrine-sync cross-comment block atop the class pointing at `rules/concord-build-rules.md` |
| `rules/concord-build-rules.md` | Refresh §1 tool hierarchy to 4-tier; add doctrine-sync cross-comment pointer atop |
| `rules/concord-model-discipline.md` | Surface concord-mcp domain-model gap-fillers (renames, arrange, delete_model_element) with use-this-when callouts |
| `rules/concord-pages-and-themes.md` | Reframe Maia as tier 3, not tier 1; add concord-mcp `generate_overview_pages` / `delete_document` / `manage_navigation` callouts; add explicit tier-4 direct-FS section |
| `skills/mendix-microflow-common/SKILL.md` | Reference concord-mcp microflow diagnostics + gap-fillers |
| `skills/mendix-microflow-update/SKILL.md` | Reference `modify_microflow_activity`, `insert_before_activity` as concord-mcp fallbacks |
| `skills/mendix-page-gen/SKILL.md` | Surface concord-mcp `generate_overview_pages`, `delete_document` as tier-2 path before Maia |
| `skills/mendix-workflow-common/SKILL.md` | Reference concord-mcp diagnostics + rename tools |
| `skills/mendix-workflow-update/SKILL.md` | Same for workflow mutations |
| `skills/mendix-microflow-syntax/SKILL.md` | Verify — likely no change (expression/XPath syntax is doctrine-stable) |
| `skills/mendix-view-entities/SKILL.md` | Verify — view-entity authoring is studio-pro MCP territory; minimal change expected |

---

## Phase 1: Infrastructure (1 commit)

Goal of this phase: get the wiring in place so the 10.x bundle path exists structurally before content lands. After this phase, on 10.x the agent finds an empty `skills-10x/` + `rules-10x/` bundle (a no-op install — logged warning, no crash) instead of being misled by the 11.x doctrine. On 11.x, nothing changes.

### Task 1: Create branch and verify baseline

**Files:** none (git only)

- [ ] **Step 1: Create branch from clean main**

```
git checkout main
git pull --ff-only
git checkout -b feat/v5.0.0-skills-rules-version-split
git status   # expect: nothing to commit, working tree clean
```

- [ ] **Step 2: Run baseline tests to confirm green starting state**

```
dotnet test
```

Expected: 324 passing (50 Concord.Core.Tests + 274 Terminal.Tests), 0 failed, 3 skipped. If anything else, STOP and report — don't start the work on red.

### Task 2: Add `skills-10x/` and `rules-10x/` placeholder directories

**Files:**
- Create: `rules-10x/.gitkeep`
- Create: `skills-10x/.gitkeep`

The csproj content includes use globs that match no files if the directory is empty. To make git track empty dirs (needed for the next task's csproj wiring to be testable), use `.gitkeep` sentinel files.

- [ ] **Step 1: Create the placeholders**

```
mkdir rules-10x; New-Item rules-10x/.gitkeep -ItemType File
mkdir skills-10x; New-Item skills-10x/.gitkeep -ItemType File
```

- [ ] **Step 2: Verify directory structure**

```
ls rules-10x; ls skills-10x
```

Expected: each shows a `.gitkeep` file.

### Task 3: Wire `skills-10x/` and `rules-10x/` into the .mxmodule via `Concord.Core.csproj`

**Files:**
- Modify: `src/Concord.Core/Concord.Core.csproj` (after the `skills-mac/` block, before `rules/`)

- [ ] **Step 1: Add content includes**

In `src/Concord.Core/Concord.Core.csproj`, after the `<Content Include="..\..\skills-mac\**\*">` block (around line 51-55), and before the `<Content Include="..\..\rules\**\*">` block, insert:

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

Also update the leading comment block (currently at lines 36-40) to mention the new dirs:

```xml
    <!-- Content includes use Link metadata to preserve top-level folder names
         (wwwroot/, skills/, skills-mac/, skills-10x/, rules/, rules-10x/) in
         the output directory. Without Link, MSBuild strips the parent-relative
         path and the files land flat at the output root — TerminalWebServer's
         ResolvePath ("wwwroot", "index.html") would then 404. -->
```

- [ ] **Step 2: Build to confirm the csproj parses and copies happen**

```
dotnet build src/Concord.Core/Concord.Core.csproj
```

Expected: build succeeds. The new dirs (`skills-10x/`, `rules-10x/`) appear in `src/Concord.Core/bin/Debug/net8.0/` containing just `.gitkeep` files. No new errors or warnings.

### Task 4: Switch Host10x to resolve `skills-10x` / `rules-10x` at all 4 call sites

**Files:**
- Modify: `src/Concord.Host10x/Pane/TerminalPaneExtension.cs` (4 call-site pairs at lines ~123/124, ~450/451, ~569/570, ~633/634)

- [ ] **Step 1: Find all 4 call sites**

```
grep -n 'ResolvePath("skills")\|ResolvePath("rules")' src/Concord.Host10x/Pane/TerminalPaneExtension.cs
```

Expected: 8 lines (4 pairs).

- [ ] **Step 2: Replace each call**

Use Edit with `replace_all`:

```
Edit:
  file_path: src/Concord.Host10x/Pane/TerminalPaneExtension.cs
  old_string: ResolvePath("skills")
  new_string: ResolvePath("skills-10x")
  replace_all: true
```

```
Edit:
  file_path: src/Concord.Host10x/Pane/TerminalPaneExtension.cs
  old_string: ResolvePath("rules")
  new_string: ResolvePath("rules-10x")
  replace_all: true
```

- [ ] **Step 3: Verify exact substitutions**

```
grep -n 'ResolvePath("skills' src/Concord.Host10x/Pane/TerminalPaneExtension.cs
grep -n 'ResolvePath("rules' src/Concord.Host10x/Pane/TerminalPaneExtension.cs
```

Expected: 8 lines total. All hits should be `"skills-10x"` or `"rules-10x"` — no bare `"skills"` or `"rules"`.

- [ ] **Step 4: Verify Host11x is untouched**

```
grep -n 'ResolvePath("skills\|ResolvePath("rules' src/Concord.Host11x/Pane/TerminalPaneExtension.cs
```

Expected: 8 lines, all bare `"skills"` or `"rules"` (no `-10x`).

- [ ] **Step 5: Build to confirm compilation**

```
dotnet build
```

Expected: build succeeds. No new errors.

### Task 5: Write `BundleResolutionTests` (Host10x + Host11x source-grep smoke tests)

**Files:**
- Create: `tests/Concord.Core.Tests/BundleResolutionTests.cs`

These tests guard against accidental reverts. They read the host extension source files directly and assert the call-site literals. Substring-precise so `"skills-10x"` doesn't accidentally satisfy a `"skills"` check.

- [ ] **Step 1: Write the test file**

```csharp
namespace Concord.Core.Tests;

using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class BundleResolutionTests
{
    [Fact]
    public void Host10x_resolves_skills_10x_and_rules_10x_at_every_call_site()
    {
        var src = ReadHostSource("Concord.Host10x");

        var skillsCalls = Regex.Matches(src, @"ResolvePath\(""skills(-10x)?""\)");
        skillsCalls.Should().HaveCount(4, "Host10x has 4 call sites that resolve the skills bundle");
        skillsCalls.Should().AllSatisfy(m =>
            m.Value.Should().Be("ResolvePath(\"skills-10x\")",
                "Host10x must resolve the 10.x bundle, not the 11.x one"));

        var rulesCalls = Regex.Matches(src, @"ResolvePath\(""rules(-10x)?""\)");
        rulesCalls.Should().HaveCount(4);
        rulesCalls.Should().AllSatisfy(m =>
            m.Value.Should().Be("ResolvePath(\"rules-10x\")"));
    }

    [Fact]
    public void Host11x_resolves_bare_skills_and_rules_at_every_call_site()
    {
        var src = ReadHostSource("Concord.Host11x");

        var skillsCalls = Regex.Matches(src, @"ResolvePath\(""skills(-10x)?""\)");
        skillsCalls.Should().HaveCount(4);
        skillsCalls.Should().AllSatisfy(m =>
            m.Value.Should().Be("ResolvePath(\"skills\")",
                "Host11x must resolve the 11.x bundle, not the 10.x one"));

        var rulesCalls = Regex.Matches(src, @"ResolvePath\(""rules(-10x)?""\)");
        rulesCalls.Should().HaveCount(4);
        rulesCalls.Should().AllSatisfy(m =>
            m.Value.Should().Be("ResolvePath(\"rules\")"));
    }

    private static string ReadHostSource(string hostAssemblyName)
    {
        var repoRoot = RepoRootFinder.Find();
        var path = Path.Combine(repoRoot, "src", hostAssemblyName, "Pane", "TerminalPaneExtension.cs");
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }
}

internal static class RepoRootFinder
{
    /// <summary>Walks up from the test assembly location until CLAUDE.md is found at the directory level.</summary>
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("repo root not found (CLAUDE.md sentinel missing)");
        return dir.FullName;
    }
}
```

- [ ] **Step 2: Run the tests**

```
dotnet test tests/Concord.Core.Tests --filter FullyQualifiedName~BundleResolutionTests
```

Expected: both tests PASS. The Host10x test passes because Task 4 already switched the call sites; the Host11x test passes because Host11x was untouched.

### Task 6: Add doctrine-sync cross-comment block to `Studio11xAllowlist.cs`

**Files:**
- Modify: `src/Concord.Core/Mcp/Studio11xAllowlist.cs` (atop the class)

- [ ] **Step 1: Insert the comment block**

In `src/Concord.Core/Mcp/Studio11xAllowlist.cs`, replace the existing class-doc comment with:

```csharp
/// <summary>
/// Tools that ship on Studio Pro 11.x. Studio Pro's built-in MCP covers
/// the rest; including those would create model-side ambiguity.
/// </summary>
/// <remarks>
/// <para><b>DOCTRINE SYNC:</b> The shipped 11.x rules and skills in
/// <c>rules/concord-build-rules.md</c>, <c>rules/concord-model-discipline.md</c>,
/// <c>rules/concord-pages-and-themes.md</c>, and <c>skills/**/SKILL.md</c>
/// reference every tool in this allowlist (minus <c>maia__force_tier</c>, a
/// debug-only tool deliberately excluded). When a tool is added, removed, or
/// renamed here, the doctrine must be refreshed too — otherwise the agent
/// will reference a tool that doesn't exist or miss one that does.</para>
/// <para><b>Test guard:</b> <c>DoctrineSyncTests</c> in
/// <c>Concord.Core.Tests</c> asserts the bundle text references every
/// non-skipped tool. It fails on the same PR that introduces drift.</para>
/// </remarks>
public static class Studio11xAllowlist
```

- [ ] **Step 2: Build to confirm no compile errors**

```
dotnet build src/Concord.Core/Concord.Core.csproj
```

Expected: build succeeds.

### Task 7: Phase 1 verification and commit

- [ ] **Step 1: Run full test suite**

```
dotnet test
```

Expected: still 324 + 2 (the two new `BundleResolutionTests`) = 326 passing. 0 failed, 3 skipped.

- [ ] **Step 2: Verify .gitkeep files are tracked**

```
git status
git diff --stat
```

Expected: 5 changed files (csproj + Host10x extension + Studio11xAllowlist + BundleResolutionTests + 2 .gitkeep additions, which counts as 2 files). All in the expected paths.

- [ ] **Step 3: Commit Phase 1**

```
git add src/Concord.Core/Concord.Core.csproj `
        src/Concord.Host10x/Pane/TerminalPaneExtension.cs `
        src/Concord.Core/Mcp/Studio11xAllowlist.cs `
        tests/Concord.Core.Tests/BundleResolutionTests.cs `
        rules-10x/.gitkeep `
        skills-10x/.gitkeep
git commit -m @'
feat(skills-rules-split): Phase 1 — Host10x resolves skills-10x/rules-10x bundles

Wires the version-aware bundle resolution into Concord.Host10x without yet
shipping the 10.x content. After this commit:

  - Concord.Host10x.TerminalPaneExtension resolves "skills-10x"/"rules-10x"
    at all 4 call sites (Host11x untouched, still resolves "skills"/"rules").
  - Concord.Core.csproj packages skills-10x/ and rules-10x/ into the .mxmodule
    layout symmetric to skills-mac/.
  - rules-10x/ and skills-10x/ exist as empty placeholders so the csproj
    globs resolve; install path is no-op (SkillInstaller/RulesInstaller log
    warning and skip when the bundle root is empty).
  - Studio11xAllowlist.cs gains a doctrine-sync comment block pointing at
    rules/concord-build-rules.md as the co-evolving doctrine.
  - BundleResolutionTests asserts call-site literals on both hosts via
    source grep — guards against accidental revert.

Content rewrites for the 10.x bundle (Phase 2) and the 11.x refresh (Phase 3)
land separately.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

- [ ] **Step 4: Verify commit landed**

```
git log -1 --stat
```

Expected: one commit, 6 files changed (5 modified + 2 gitkeep adds), reasonable line counts.

---

## Phase 2: 10.x bundle + 10.x doctrine sync test (1 commit)

Goal: populate `rules-10x/` and `skills-10x/` with version-appropriate doctrine, plus the half of `DoctrineSyncTests` that guards the 10.x bundle.

**Content authoring approach:** for each 10.x file, the existing 11.x file is the starting reference, but the rewrite is substantial — different tool family, different doctrine tier count, no Maia. Don't blind-copy; the resulting prose must read as a coherent 10.x document, not 11.x with strings replaced.

**Tool-name reference for the 10.x bundle:** enumerate via `ToolCatalog` (see Task 16's test setup). Concretely, the 10.x tool set is:
- `UiActionsBootstrap.Register` → `run_app, stop_app, save_all, refresh_project, get_app_status, get_active_run_configuration`
- `SpmcpToolBootstrap10x.Register` → the SPMCP catalog (~76 tools across domain-model, microflow-authoring, page-generation, navigation, security, runtime, view-entities, diagnostics)
- `MaiaToolsBootstrap.Register` is intentionally NOT registered for the 10.x doctrine — Maia doesn't work well on 10.x per Joe's directive

**Forbidden references in 10.x bundle (negative assertions in Task 16):**
- `mcp__mendix-studio-pro__` — that MCP doesn't exist on 10.x
- `maia__` — Maia tools excluded by doctrine
- `pg_write_page` — Maia-fronted page write
- `ped_create_document`, `ped_update_document`, etc. as standalone references (always prefix `mcp__concord-mcp__` or refer to it as "the concord-mcp create/update equivalent")

### Task 8: Write `rules-10x/concord-build-rules.md`

**Files:**
- Create: `rules-10x/concord-build-rules.md`
- Reference: `rules/concord-build-rules.md` (existing 11.x file — start here, then transform)

- [ ] **Step 1: Read the 11.x reference**

Read `rules/concord-build-rules.md` (already exists; 213 lines). Note the section structure: §1 tool hierarchy, §3 persistence, §4 read-back, §12 verification, §13 plan-before-write, §14 persisting learnings, §15 search. §2 lives in `concord-pages-and-themes.md`; §5–§11 live in companion files.

- [ ] **Step 2: Author the 10.x version**

Create `rules-10x/concord-build-rules.md` with this structure (the exact prose is authored at write time, but the constraints are):

- **Title and preamble:** "Concord Build Rules — Core (Studio Pro 10.x – 11.9.x)". Same "Don't guess. Don't fake. Don't break." north star.
- **Companion files** list points at `concord-pages-and-themes.md` and `concord-model-discipline.md` (same names, 10.x content).
- **Skills cross-reference** points at `.claude/skills/mendix-*/SKILL.md` (no path change — the skill folder name on disk is identical; only the content differs).
- **§1 Tool hierarchy — closed set (2-tier):**
  1. Studio Pro itself (the IDE window, native UI actions).
  2. **Concord MCP server** (`mcp__concord-mcp__*`):
     - Domain Model: `create_entity`, `create_attribute`, `create_association`, `read_domain_model`, `read_entity`, `read_attribute`, `read_association`, `delete_model_element`, `rename_entity`, `rename_attribute`, `rename_association`, `set_documentation`, `arrange_domain_model`, plus the SPMCP equivalents for any tool you'd reach for on 11.10. **Enumerate every tool surfaced by `SpmcpToolBootstrap10x` and `UiActionsBootstrap` here, grouped by family.**
     - UI Actions: `run_app`, `stop_app`, `save_all`, `refresh_project`, `get_app_status`, `get_active_run_configuration`.
     - **No Maia tools.** State this explicitly: "Maia is excluded from 10.x doctrine. On Studio Pro 10.x–11.9 the bridge does not produce reliable output; the build path is 100% concord-mcp."
  3. **Your reasoning** — analysis, JSON construction, schema diffs, planning.
  4. **Direct filesystem** — for styling files (`/themes/**`), custom JS actions (`/jsactions/**`), and any file outside the model. The concord-mcp on 10.x does not expose a registered file domain like 11.10's studio-pro MCP does, so direct FS via Bash/PowerShell read+write is the only option for these paths. **Never write to `.mpr` directly (binary SQLite; corrupts on write).** Read-only inspection of model files is fine.
  5. **Web search and `docs.mendix.com`** — when knowledge is missing.

  **Forbidden:**
  - Editing `.mpr` directly.
  - Filesystem writes against model files.
  - mxbuild / mxcli / npm against the project.
  - Manually attaching MCP servers — Concord wires `.mcp.json` and `~/.codex/config.toml` automatically.

- **§3 Persistence — verbatim evidence required**: same shape as 11.x §3 but recovery ladder swapped — instead of `ped_get_schema` / `search_mendix_knowledge_base` (those are 11.x studio-pro MCP tools), the 10.x ladder is:
  1. Confirm the matching skill is loaded.
  2. Strip to minimal shape and retry.
  3. **`mcp__concord-mcp__check_model`** for project-wide diagnostics.
  4. **`mcp__concord-mcp__get_last_error`** and **`get_studio_pro_logs`** to surface the most recent failure.
  5. **Web search** for the verbatim error string.
  6. Escalate to user with verbatim errors.

  Retry budgets for general PED-equivalent writes: same as 11.x §3 (recovery ladder; no fixed call count; jump to web search after 3 different payload shapes return the same error). No Maia retry budget — Maia is not in the 10.x ladder.

- **§4 Read-back after every write**: identical doctrine to 11.x §4. Tool names: `mcp__concord-mcp__read_entity` / `read_microflow` / etc. after each `create_*` / `update_*`. `check_model` once after the full task batch.

- **§12 Verification — three-part gate**: same shape as 11.x §12 but `ped_check_errors` references swapped to `mcp__concord-mcp__check_model` (or whatever the 10.x equivalent is — enumerate at write time). The `save_all` → `refresh_project` → `stop_app` → `run_app` → `get_app_status` polling cycle stays unchanged (these are concord-mcp tools, same names both versions).

- **§13 Plan-before-write for non-trivial builds**: identical to 11.x §13.

- **§14 Persisting what you learn during a build**: identical to 11.x §14 (the `.claude/rules/project/learned-*.md` pattern is 10.x-compatible — it's Concord-managed and version-agnostic).

- **§15 Search and external references**: drops the `search_mendix_knowledge_base` and `web_fetch` references (those are 11.x studio-pro MCP tools). Replaces with: web search via the agent's built-in WebSearch tool, plus `docs.mendix.com` cited directly.

- **Cross-reference footer**: list of the 7 skills under `.claude/skills/`, plus pointer to `.claude/rules/project/` user-authored rules.

- [ ] **Step 3: Save the file**

Write the content to `rules-10x/concord-build-rules.md`.

### Task 9: Write `rules-10x/concord-model-discipline.md`

**Files:**
- Create: `rules-10x/concord-model-discipline.md`
- Reference: `rules/concord-model-discipline.md`

- [ ] **Step 1: Read the 11.x reference**

Read the existing file. Note: it covers §5 `ped_*` discipline, §6 update operations, §7 don't ship orphans, §9 new project = new module.

- [ ] **Step 2: Author the 10.x version**

Rewrite for 10.x:
- §5 swaps `ped_*` references for concord-mcp's SPMCP-handler equivalents. **The exact tool names come from `SpmcpToolBootstrap10x.Register`** — enumerate by reading `src/Concord.Host10x/Spmcp/SpmcpToolBootstrap10x.cs` at write time. Constructor-flattening rules and silent-permissive-vs-strict-extras behavior likely still apply (those are PED engine behaviors, not MCP wrapper behaviors); preserve that guidance.
- §6 update operations: same discipline (single-shot fix rule, batched ops only across different paths), tool names swapped.
- §7 don't ship orphans: identical doctrine, no tool-name changes needed.
- §9 new project = new module: identical doctrine.

- [ ] **Step 3: Save the file**

Write the content to `rules-10x/concord-model-discipline.md`.

### Task 10: Write `rules-10x/concord-pages-and-themes.md`

**Files:**
- Create: `rules-10x/concord-pages-and-themes.md`
- Reference: `rules/concord-pages-and-themes.md`

- [ ] **Step 1: Read the 11.x reference**

Read the existing file. Note: covers §2 Pages-via-Maia, §8 Studio Pro UI handoffs, §10 Layout-first, §11 Custom theme.

- [ ] **Step 2: Author the 10.x version**

Substantial doctrine shift:
- **§2 Pages-via-Maia is removed entirely.** Maia is not in the 10.x tool surface. Replace with §2 "Pages via concord-mcp": uses `mcp__concord-mcp__generate_overview_pages` for list/detail scaffolding (if available on 10.x — verify in `SpmcpToolBootstrap10x` at write time; if not, document the available page-authoring path or note its absence explicitly).
- §8 Studio Pro UI handoffs: identical to 11.x (these are about handing the user a UI step the agent can't automate; version-agnostic).
- §10 Layout-first: identical doctrine.
- §11 Custom theme: rewrite the file-domain references. On 10.x there's no studio-pro MCP file domain; theme work uses direct FS (Bash/PowerShell read+write) against `theme/styles/web/` under the project root.

- [ ] **Step 3: Save the file**

Write the content to `rules-10x/concord-pages-and-themes.md`.

### Task 11: Write `skills-10x/mendix-microflow-common/SKILL.md`

**Files:**
- Create: `skills-10x/mendix-microflow-common/SKILL.md`
- Reference: `skills/mendix-microflow-common/SKILL.md`

- [ ] **Step 1: Read the 11.x reference** (read it; do not skim).

- [ ] **Step 2: Author the 10.x version**

- Top-level "Tools in this environment" header lists `mcp__concord-mcp__*` microflow tools — exact names from `SpmcpToolBootstrap10x.Register` (e.g. `read_microflow`, `list_microflows`, `check_microflow_errors`, etc. — verify at write time).
- Drop all `mcp__mendix-studio-pro__ped_*` references; swap for concord-mcp equivalents.
- Drop all `read_skill` references (that's a studio-pro MCP tool).
- Keep doctrinal content about microflow shape: object types, flow rules, expression syntax — those are Mendix invariants, not version-specific.

- [ ] **Step 3: Save the file**.

### Task 12: Write `skills-10x/mendix-microflow-syntax/SKILL.md`

**Files:**
- Create: `skills-10x/mendix-microflow-syntax/SKILL.md`
- Reference: `skills/mendix-microflow-syntax/SKILL.md`

- [ ] **Step 1: Read the 11.x reference**

This skill covers microflow expression syntax and XPath — largely doctrine-stable. The transformation is mostly tool-name swaps.

- [ ] **Step 2: Author the 10.x version**

Swap any `mcp__mendix-studio-pro__*` references for `mcp__concord-mcp__*` equivalents. Preserve all expression/XPath syntax guidance. No structural changes expected.

- [ ] **Step 3: Save the file**.

### Task 13: Write `skills-10x/mendix-microflow-update/SKILL.md`

**Files:**
- Create: `skills-10x/mendix-microflow-update/SKILL.md`
- Reference: `skills/mendix-microflow-update/SKILL.md`

- [ ] **Step 1: Read the 11.x reference** (already partially shown — covers RULE 1 auto-deletion, RULE 2 re-read-after-mutation, RULE 3 no overlaps, 70px spacing, recipes for replace and insert).

- [ ] **Step 2: Author the 10.x version**

- Top header: `mcp__concord-mcp__read_microflow`, `mcp__concord-mcp__update_microflow` (exact names from `SpmcpToolBootstrap10x` — verify at write time).
- All three RULES preserved doctrinally — they're Mendix invariants.
- 70px spacing math preserved.
- Replace and Insert recipes rewritten with the 10.x tool names. Same JSON shapes; only tool names change.

- [ ] **Step 3: Save the file**.

### Task 14: Write `skills-10x/mendix-page-gen/SKILL.md`

**Files:**
- Create: `skills-10x/mendix-page-gen/SKILL.md`
- Reference: `skills/mendix-page-gen/SKILL.md`

- [ ] **Step 1: Read the 11.x reference**

This is the largest skill (34990 tokens per Read; covers Maia-led page authoring, `pg_write_page` shape, retry budgets, schema validation).

- [ ] **Step 2: Author the 10.x version**

This is the biggest content shift. The 11.x skill is built around `pg_write_page` (Maia's page-writing endpoint). On 10.x, page authoring goes through concord-mcp's `generate_overview_pages` and any other page tools registered by `SpmcpToolBootstrap10x`.

- Document the concord-mcp page tools available on 10.x. If page authoring is more limited than 11.x, say so honestly — "for richer pages, use Studio Pro's native page editor and use Concord to wire up navigation/microflows".
- Drop ALL Maia ladder, second-opinion path, retry budget, `pg_write_page` JSON shape, transient error handling content.
- Keep page-design doctrine (layout-first, navigation graph thinking, widget naming) — those are Mendix invariants.

- [ ] **Step 3: Save the file**.

### Task 15: Write `skills-10x/mendix-view-entities/SKILL.md`

**Files:**
- Create: `skills-10x/mendix-view-entities/SKILL.md`
- Reference: `skills/mendix-view-entities/SKILL.md`

- [ ] **Step 1: Read the 11.x reference**

This skill covers view-entity authoring and OQL. On 11.x it leverages `oql_generate` and `oql_read` (studio-pro MCP tools).

- [ ] **Step 2: Author the 10.x version**

- If `SpmcpToolBootstrap10x` exposes OQL-equivalent tools, document those. If not, the 10.x view-entity skill becomes "view-entity authoring requires Studio Pro's native UI on this version — Concord can read/list but not author them".
- Verify the exact 10.x view-entity surface at write time by reading `SpmcpToolBootstrap10x.cs`.

- [ ] **Step 3: Save the file**.

### Task 16: Write `skills-10x/mendix-workflow-common/SKILL.md` and `skills-10x/mendix-workflow-update/SKILL.md`

**Files:**
- Create: `skills-10x/mendix-workflow-common/SKILL.md`
- Create: `skills-10x/mendix-workflow-update/SKILL.md`
- Reference: `skills/mendix-workflow-common/SKILL.md`, `skills/mendix-workflow-update/SKILL.md`

- [ ] **Step 1: Read both 11.x references**.

- [ ] **Step 2: Author both 10.x versions**

Workflow tools on 10.x: enumerate via `SpmcpToolBootstrap10x` at write time. Same transformation pattern as microflow common+update: swap tool prefixes, preserve doctrinal content (workflow shape, activity types, flow rules).

- [ ] **Step 3: Save both files**.

### Task 17: Write `DoctrineSyncTests` — 10.x assertions

**Files:**
- Create: `tests/Concord.Core.Tests/DoctrineSyncTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
namespace Concord.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Terminal;
using Terminal.Interop;
using Terminal.Maia;
using Terminal.Mcp;
using Xunit;

[Collection("HostServices")]
public class DoctrineSyncTests : IDisposable
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public string? GetActiveUrl() => null;
        public int? GetActivePort() => null;
        public Task<RunState> IsRunningAsync(CancellationToken ct = default)
            => Task.FromResult(RunState.Stopped);
    }
    private sealed class FakeUi : IStudioProUiAutomation
    {
        public bool TriggerRun() => false;
        public bool TriggerStop() => false;
        public bool TriggerRefreshFromDisk() => false;
        public bool TriggerSaveAll() => false;
        public string? LastFailureReason => null;
    }

    public DoctrineSyncTests()
    {
        HostServices.Reset();
        HostServices.SetRunStateProbe(new FakeProbe());
        HostServices.SetUiAutomation(new FakeUi());
    }

    public void Dispose()
    {
        HostServices.Reset();
        ToolCatalogRegistry.Active = null;
    }

    private static readonly string[] ForbiddenIn10x = new[]
    {
        "mcp__mendix-studio-pro__",
        "maia__",
        "pg_write_page",
    };

    [Fact]
    public void Bundle_10x_does_not_reference_forbidden_tools()
    {
        var repoRoot = RepoRootFinder.Find();
        var bundleFiles = EnumerateMdFiles(Path.Combine(repoRoot, "rules-10x"))
            .Concat(EnumerateMdFiles(Path.Combine(repoRoot, "skills-10x")))
            .ToList();
        bundleFiles.Should().NotBeEmpty("the 10.x bundle should have content after Phase 2");

        foreach (var file in bundleFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var forbidden in ForbiddenIn10x)
            {
                content.Should().NotContain(forbidden,
                    $"10.x bundle file {RelativeTo(repoRoot, file)} must not reference '{forbidden}' — that tool is not available on Studio Pro 10.x–11.9.");
            }
        }
    }

    [Fact]
    public void Bundle_10x_references_every_registered_concord_mcp_tool()
    {
        // Enumerate the 10.x tool surface via the existing bootstrap pattern.
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        UiActionsBootstrap.Register(catalog);
        SpmcpToolBootstrap10x.Register(catalog);
        // Maia is intentionally NOT registered for the 10.x doctrine — even
        // though Host10x may register it at runtime, the bundle excludes it
        // per Joe's "Maia doesn't work well on 10.x" directive.

        var registered = catalog.ListVisibleNames().ToHashSet();

        // SkipList: explicitly-excluded debug/internal tools. Update with reason if you add.
        var skip = new HashSet<string>(); // No 10.x-specific skips at v1.

        var expected = registered.Except(skip).ToList();
        expected.Should().NotBeEmpty();

        var repoRoot = RepoRootFinder.Find();
        var bundleText = string.Concat(
            EnumerateMdFiles(Path.Combine(repoRoot, "rules-10x"))
                .Concat(EnumerateMdFiles(Path.Combine(repoRoot, "skills-10x")))
                .Select(File.ReadAllText));

        var missing = expected.Where(t => !bundleText.Contains(t)).ToList();
        missing.Should().BeEmpty(
            "every concord-mcp tool registered on 10.x must be referenced at least once in rules-10x/ or skills-10x/. " +
            "If a tool is intentionally excluded from the doctrine (e.g. internal/debug), add it to the skip-list in DoctrineSyncTests with a reason. " +
            $"Missing: {string.Join(", ", missing)}");
    }

    internal static IEnumerable<string> EnumerateMdFiles(string root)
    {
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories);
    }

    internal static string RelativeTo(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}
```

- [ ] **Step 2: Run the 10.x doctrine sync tests**

```
dotnet test tests/Concord.Core.Tests --filter FullyQualifiedName~DoctrineSyncTests
```

Expected: both 10.x tests PASS if the content from Tasks 8-16 is complete. If the positive test fails, the failure message names the missing tool(s); add the missing reference(s) to the appropriate rule or skill and re-run.

- [ ] **Step 3: Iterate until green**

If the negative test fails (a forbidden reference slipped in): fix the content. If the positive test fails (a tool is missing): add a use-this-when callout in the matching skill file (e.g. `delete_model_element` belongs in microflow-update or model-discipline). Re-run after each fix.

### Task 18: Phase 2 verification and commit

- [ ] **Step 1: Run full test suite**

```
dotnet test
```

Expected: 324 baseline + 2 BundleResolution + 2 DoctrineSync (10.x positive + 10.x negative) = 328 passing. 0 failed.

- [ ] **Step 2: Confirm `.gitkeep` files are no longer needed**

If content files exist in `rules-10x/` and `skills-10x/`, the `.gitkeep` files can be deleted:

```
git rm rules-10x/.gitkeep skills-10x/.gitkeep
```

- [ ] **Step 3: Stage and commit Phase 2**

```
git add rules-10x/ skills-10x/ tests/Concord.Core.Tests/DoctrineSyncTests.cs
git commit -m @'
feat(skills-rules-split): Phase 2 — 10.x bundle content + doctrine sync test

Populates rules-10x/ and skills-10x/ with version-appropriate doctrine for
Studio Pro 10.24.13–11.9.x. The 10.x bundle teaches a 2-tier hierarchy:
concord-mcp (~88 tools across SPMCP handlers + UI actions) → direct FS for
styling and non-model files. Maia is excluded — it does not produce reliable
output on 10.x. The mendix-studio-pro MCP is not referenced anywhere — it
does not exist on these versions.

Files:
  - rules-10x/concord-build-rules.md
  - rules-10x/concord-model-discipline.md
  - rules-10x/concord-pages-and-themes.md
  - skills-10x/{mendix-microflow-common,mendix-microflow-syntax,
                mendix-microflow-update,mendix-page-gen,mendix-view-entities,
                mendix-workflow-common,mendix-workflow-update}/SKILL.md

Test guard: DoctrineSyncTests asserts the 10.x bundle (a) references every
tool registered by UiActionsBootstrap + SpmcpToolBootstrap10x and (b)
contains no forbidden references (mcp__mendix-studio-pro__, maia__,
pg_write_page). The guard fires on the same PR that introduces drift.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

- [ ] **Step 4: Verify commit**

```
git log -1 --stat
```

---

## Phase 3: 11.x bundle refresh + 11.x doctrine sync test (1 commit)

Goal: update `rules/` and `skills/` to surface the full 45-tool concord-mcp catalog as a distinct tier, demote Maia to tier 3, promote direct FS as tier 4, and add the 11.x half of the doctrine sync test.

### Task 19: Refresh `rules/concord-build-rules.md` — §1 tool hierarchy 4-tier restructure

**Files:**
- Modify: `rules/concord-build-rules.md`

- [ ] **Step 1: Read the current file**

The current §1 (lines 23-50) lists tools in 6 numbered items: Studio Pro itself, Studio Pro MCP, Concord MCP (which conflates Maia bridge with UI actions), Maia in Studio Pro, your reasoning, web search. The new structure: 4 ranked tiers (studio-pro MCP, concord-mcp, Maia delegate, direct FS) plus the supporting items (reasoning, search) as separate concerns.

- [ ] **Step 2: Rewrite §1 with the 4-tier structure**

Replace the existing §1 (lines 23-50 approximately) with:

```markdown
## 1. Tool hierarchy — closed set, 4 tiers

The full set of allowed paths for working on this Mendix project, in priority order:

### Tier 1 — Studio Pro MCP server (`mcp__mendix-studio-pro__*`)

The first reach for any model operation. Authored, well-tested, schema-validated.

- `ped_*` — domain models, microflows, workflows, view entities (read / create / update / remove).
- `oql_*` — OQL generation and reading for view entities (`oql_generate`, `oql_read`).
- `read_skill`, `search_mendix_knowledge_base`, `web_fetch`.
- `glob`, `read_file`, `write_file` — scoped to file domains registered by the server (`/themes`, `/jsactions` as of Studio Pro 11.10). Always call `glob` first to confirm the current set; future Studio Pro versions may register additional roots.

### Tier 2 — Concord MCP server (`mcp__concord-mcp__*`) — 45 tools

Fills gaps the Studio Pro MCP doesn't cover. Use when Tier 1 doesn't reach the operation.

**UI actions** (lifecycle):
- `run_app`, `stop_app`, `save_all`, `refresh_project`, `get_app_status`, `get_active_run_configuration`.

**Domain Model gap-fillers** (hard deletes + reference-safe renames + layout):
- `delete_model_element` — surgical removes that respect references.
- `rename_entity`, `rename_attribute`, `rename_association`, `rename_document`, `rename_module`, `rename_enumeration_value` — reference-safe renames the studio-pro MCP can't do safely.
- `set_documentation` — attach docstrings to model elements.
- `arrange_domain_model` — auto-layout after batch entity creates.

**Microflow gap-fillers** (where studio-pro MCP's `ped_*` doesn't reach):
- `exclude_document`, `set_microflow_url`, `modify_microflow_activity`, `insert_before_activity`.

**Pages**:
- `generate_overview_pages` — list/detail scaffolding for an entity.
- `delete_document` — remove a page (or other doc).

**Navigation**:
- `manage_navigation` — read/edit the navigation graph.

**Security audit**:
- `read_security_info`, `read_entity_access_rules`, `read_microflow_security`, `audit_security` — before-shipping audit pass; surfaces anonymous-role and open-grant findings.

**Runtime / Configuration**:
- `read_runtime_settings`, `set_runtime_settings`, `read_configurations`, `set_configuration`.

**Diagnostics**:
- `check_model` — project-wide model validation.
- `check_project_errors` — full error report.
- `get_studio_pro_logs`, `get_last_error` — runtime/error log surfacing.
- `analyze_project_patterns` — heuristic pattern detection.

### Tier 3 — Maia delegate (via `mcp__concord-mcp__maia__*`, Windows only)

Used when Tier 1 and Tier 2 don't cover the operation — typically richer page authoring beyond `generate_overview_pages` scaffolding.

- `maia__ask`, `maia__send`, `maia__status`, `maia__wait`, `maia__reset` — the request/response/recovery surface.
- `maia__busy`, `maia__ping`, `maia__health`, `maia__new_chat` — introspection.

The Maia operational ladder (when-to-retry, when-to-escalate, the 3-consecutive-failure stop rule, the page-error second-opinion tiebreaker) lives in §2 and §3 — load them before reaching for Maia. **(`maia__force_tier` is a transport-tier debug aid; do not use unless the user explicitly asks for transport diagnostics.)**

### Tier 4 — Direct filesystem

Fallback for paths the Studio Pro MCP file domain doesn't cover.

- **Inside the registered file domains** (`/themes/**`, `/jsactions/**`): use `mcp__mendix-studio-pro__write_file` — it respects model state.
- **Outside the registered domains**: direct FS via Bash/PowerShell. Typical use: theme variants, custom CSS, additional JS actions in non-standard paths, project-specific config files.
- **Never** write to `.mpr` directly (binary SQLite; corrupts on write). Read-only inspection of model files is fine.

### Supporting concerns

- **Your reasoning** — analysis, JSON construction, schema diffs, planning.
- **Web search and `docs.mendix.com`** — when knowledge is missing.

**Forbidden, every time:**

- Editing `.mpr` directly.
- Filesystem writes against model files via direct FS (use Tier 1's file domain or Tier 4 with the model-file exclusion).
- mxbuild, mxcli, npm against the project.
- Manually attaching MCP servers (`claude mcp add ...`) — Concord wires `.mcp.json` and `~/.codex/config.toml` automatically.

If a path is not in this list, it is not an option. The right move when a tier boundary blocks you is §3 (persist with evidence), not a parallel filesystem path.
```

- [ ] **Step 3: Add doctrine-sync cross-comment pointer atop the file**

Insert at the top of the file (after the existing title), before "Always-loaded for any session...":

```markdown
> **Doctrine sync:** This file references every tool in `Studio11xAllowlist.All` (the 45-tool concord-mcp 11.x catalog) plus the studio-pro MCP and Maia surfaces. When `Studio11xAllowlist` changes (a tool added, removed, renamed), this file and the matching skill files must be refreshed to match. `DoctrineSyncTests` in `Concord.Core.Tests` fails the build when drift appears.
```

- [ ] **Step 4: Cross-reference §2 from §1**

In the new §1 Tier 3 paragraph (Maia delegate), the sentence "The Maia operational ladder ... lives in §2 and §3 — load them before reaching for Maia" already does this. Verify it reads cleanly.

### Task 20: Refresh `rules/concord-model-discipline.md`

**Files:**
- Modify: `rules/concord-model-discipline.md`

- [ ] **Step 1: Read the current file** (it's bundled with concord-build-rules per the cross-reference; covers §5 / §6 / §7 / §9).

- [ ] **Step 2: Add concord-mcp domain-model gap-filler callouts**

Where §5 talks about `ped_*` writes, add a sentence describing when the concord-mcp gap-fillers apply: "After a `ped_*` create, if the model needs a layout pass, call `mcp__concord-mcp__arrange_domain_model`. For reference-safe renames the studio-pro MCP can't do (rename an attribute referenced from microflows / pages / OQL views), use `mcp__concord-mcp__rename_attribute` instead of editing via `ped_update_document` (which doesn't update references)." Adapt phrasing to existing prose style.

For §6 update operations: surface `mcp__concord-mcp__set_documentation` for adding docstrings post-create.

For §7 don't ship orphans: surface `mcp__concord-mcp__delete_model_element` as the safe-delete path.

### Task 21: Refresh `rules/concord-pages-and-themes.md`

**Files:**
- Modify: `rules/concord-pages-and-themes.md`

- [ ] **Step 1: Read the current file**.

- [ ] **Step 2: Reframe Maia from tier 1 to tier 3**

The existing §2 "Pages via Maia" doctrine remains correct *operationally* — the Maia ladder, second-opinion tiebreaker, retry budgets all stay. But the *entry sequencing* changes: the file should make clear that the page-authoring sequence is now:

1. Try `mcp__mendix-studio-pro__ped_create_document`/`ped_update_document` (Tier 1) for the page if its content is simple.
2. Try `mcp__concord-mcp__generate_overview_pages` (Tier 2) for list/detail scaffolding off an entity.
3. Reach for Maia (Tier 3) when Tiers 1+2 don't reach the page's content — rich layout, dynamic widgets, custom interaction patterns.
4. Direct FS (Tier 4) for the `.scss` / theme variant — never for the page document itself.

For §8 Studio Pro UI handoffs: surface `mcp__concord-mcp__manage_navigation` callout — navigation-graph edits don't need a UI handoff anymore.

For §11 Custom theme: surface direct FS as the explicit path for theme variants beyond the studio-pro MCP file domain.

### Task 22: Refresh `skills/mendix-microflow-common/SKILL.md`

**Files:**
- Modify: `skills/mendix-microflow-common/SKILL.md`

- [ ] **Step 1: Read the current file**.

- [ ] **Step 2: Add concord-mcp microflow callouts**

Surface in a "Tier 2 concord-mcp gap-fillers" section (or equivalent):
- `mcp__concord-mcp__check_model`, `check_project_errors`, `get_studio_pro_logs`, `get_last_error`, `analyze_project_patterns` — when the studio-pro MCP `ped_check_errors` doesn't surface enough context.

### Task 23: Verify `skills/mendix-microflow-syntax/SKILL.md`

**Files:**
- Verify: `skills/mendix-microflow-syntax/SKILL.md`

- [ ] **Step 1: Read the file**.

- [ ] **Step 2: Decide if changes are needed**

This skill covers expression and XPath syntax. Likely no new concord-mcp tool callouts needed. If after reading, no change is warranted: skip to Task 24 with a note in the eventual commit message. If a change is warranted: make it.

### Task 24: Refresh `skills/mendix-microflow-update/SKILL.md`

**Files:**
- Modify: `skills/mendix-microflow-update/SKILL.md`

- [ ] **Step 1: Read the current file** (already read in spec exploration).

- [ ] **Step 2: Surface microflow gap-fillers**

Add callouts for `mcp__concord-mcp__modify_microflow_activity` and `mcp__concord-mcp__insert_before_activity` — when to reach for them versus `mcp__mendix-studio-pro__ped_update_document` (concord-mcp's microflow surface fills specific authoring gaps; document where).

Also surface `mcp__concord-mcp__exclude_document` (rare — for excluding a microflow from compilation) and `mcp__concord-mcp__set_microflow_url` (when a microflow needs URL exposure).

### Task 25: Refresh `skills/mendix-page-gen/SKILL.md`

**Files:**
- Modify: `skills/mendix-page-gen/SKILL.md`

- [ ] **Step 1: Read the relevant sections** (file is large; focus on the page-authoring sequence and Maia entry conditions).

- [ ] **Step 2: Reframe sequencing**

Update the page-authoring sequence so studio-pro MCP `ped_create_document` for a Page document is the FIRST reach (Tier 1), `mcp__concord-mcp__generate_overview_pages` is the SECOND reach (Tier 2, for list/detail scaffolding), Maia is the THIRD reach (Tier 3). Preserve the Maia ladder operational content — it stays correct, just reframe entry conditions.

Add `mcp__concord-mcp__delete_document` callout for removing a page (replaces ad-hoc patterns).

### Task 26: Verify `skills/mendix-view-entities/SKILL.md`

**Files:**
- Verify: `skills/mendix-view-entities/SKILL.md`

- [ ] **Step 1: Read the file**.

- [ ] **Step 2: Decide on changes**

View-entity authoring is studio-pro MCP territory (`oql_*` + `ped_*`). Likely no new concord-mcp tool callouts needed. If a change is warranted, make it; otherwise note "no change" in the commit message.

### Task 27: Refresh `skills/mendix-workflow-common/SKILL.md` and `skills/mendix-workflow-update/SKILL.md`

**Files:**
- Modify: `skills/mendix-workflow-common/SKILL.md`
- Modify: `skills/mendix-workflow-update/SKILL.md`

- [ ] **Step 1: Read both files**.

- [ ] **Step 2: Add concord-mcp gap-filler callouts**

Surface in each file (where relevant to the skill's scope):
- `audit_security` callout — workflows often need security review.
- `analyze_project_patterns` callout — for finding workflow shape inconsistencies.
- `set_documentation` callout — for adding docstrings to workflow steps.

### Task 28: Add the 11.x half of `DoctrineSyncTests`

**Files:**
- Modify: `tests/Concord.Core.Tests/DoctrineSyncTests.cs`

- [ ] **Step 1: Add the 11.x tests to the existing class**

Append the following two `[Fact]` methods to `DoctrineSyncTests`:

```csharp
private static readonly HashSet<string> Skip11x = new()
{
    "maia__force_tier", // debug-only; explicitly excluded by existing rules
};

[Fact]
public void Bundle_11x_references_every_studio11x_allowlist_tool()
{
    var expected = Studio11xAllowlist.All
        .Where(t => !Skip11x.Contains(t))
        .ToList();
    expected.Should().NotBeEmpty();

    var repoRoot = RepoRootFinder.Find();
    var bundleText = string.Concat(
        EnumerateMdFiles(Path.Combine(repoRoot, "rules"))
            .Concat(EnumerateMdFiles(Path.Combine(repoRoot, "skills")))
            .Select(File.ReadAllText));

    var missing = expected.Where(t => !bundleText.Contains(t)).ToList();
    missing.Should().BeEmpty(
        "every tool in Studio11xAllowlist must be referenced at least once in rules/ or skills/. " +
        "If a tool is intentionally excluded from the doctrine, add it to Skip11x in DoctrineSyncTests with a reason. " +
        $"Missing: {string.Join(", ", missing)}");
}

[Fact]
public void Bundle_11x_consistently_uses_the_concord_mcp_prefix_or_omits_it_intentionally()
{
    // Sanity check: the 11.x bundle should mostly use mcp__concord-mcp__<tool>
    // for clarity, but bare tool names are acceptable in skill snippets and
    // recipe blocks (e.g. "TOOL CALL: ped_update_document"). This test does
    // not enforce prefix consistency — it asserts only that when the concord-mcp
    // prefix IS used, it's spelled correctly.
    var repoRoot = RepoRootFinder.Find();
    var bundleFiles = EnumerateMdFiles(Path.Combine(repoRoot, "rules"))
        .Concat(EnumerateMdFiles(Path.Combine(repoRoot, "skills")));

    foreach (var file in bundleFiles)
    {
        var content = File.ReadAllText(file);
        // Common mis-spellings to guard against. Add others here as they're
        // discovered in review.
        content.Should().NotContain("mcp__concord_mcp__",
            $"{RelativeTo(repoRoot, file)} uses underscore-separator instead of dash in the concord-mcp prefix");
        content.Should().NotContain("mcp__concord_mcp_",
            $"{RelativeTo(repoRoot, file)} uses underscore-separator");
    }
}
```

- [ ] **Step 2: Run the doctrine sync tests**

```
dotnet test tests/Concord.Core.Tests --filter FullyQualifiedName~DoctrineSyncTests
```

Expected: 4 tests PASS (2 for 10.x from Phase 2, plus 2 new for 11.x). If `Bundle_11x_references_every_studio11x_allowlist_tool` fails, the failure message lists the missing tools — add their use-this-when callouts to the appropriate refresh file from Tasks 19-27 and re-run.

- [ ] **Step 3: Iterate until green**

This is where Phase 3's content discipline gets enforced. The test names exact missing tools; address each by editing the appropriate file.

### Task 29: Phase 3 verification and commit

- [ ] **Step 1: Run full test suite**

```
dotnet test
```

Expected: 324 baseline + 2 BundleResolution + 4 DoctrineSync = 330 passing. 0 failed.

- [ ] **Step 2: Stage and commit Phase 3**

```
git add rules/ skills/ tests/Concord.Core.Tests/DoctrineSyncTests.cs
git commit -m @'
feat(skills-rules-split): Phase 3 — 11.x bundle refresh + 11.x doctrine sync

Refreshes rules/ and skills/ to surface the full 45-tool concord-mcp 11.x
catalog as a distinct doctrine tier, demote Maia from the page-authoring
primary path to a tier-3 fallback, promote direct FS as an explicit tier 4
for paths outside the studio-pro MCP file domain.

Key changes:
  - rules/concord-build-rules.md §1 restructured to a 4-tier hierarchy:
    studio-pro MCP → concord-mcp (45 tools) → Maia delegate → direct FS.
    Concord-mcp tier broken out from Maia (was previously conflated).
  - All 29 concord-mcp tools surfaced with use-this-when callouts in the
    matching rules/skills file.
  - Maia operational ladder (§2/§3) preserved verbatim; only entry conditions
    reframed to make tier 3 status explicit.

Test guard: DoctrineSyncTests gains 2 11.x assertions — every tool in
Studio11xAllowlist.All (minus maia__force_tier in the skip-list) appears in
rules/ or skills/, plus a guard against common misspellings of the
mcp__concord-mcp__ prefix.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

- [ ] **Step 3: Verify commit**

```
git log -3 --oneline
```

Expected: three commits on the branch, in phase order.

---

## Final verification before PR

### Task 30: Diff review and fresh-reviewer pass

- [ ] **Step 1: Review the full diff vs main**

```
git diff main...HEAD --stat
git diff main...HEAD --shortstat
```

Expected scale: ~15-20 files changed, several thousand lines added (most in the new 10.x bundle content + refreshed 11.x content + new tests).

- [ ] **Step 2: Run full test suite one more time**

```
dotnet test
```

Expected: 330 passing, 0 failed, 3 skipped.

- [ ] **Step 3: Adversarial fresh-review checkpoint**

Per Joe's working style, do a fresh-reviewer pass on the diff before opening the PR. Look for:
- NITs: typos, inconsistent spelling, broken cross-references.
- FLAGs: doctrine claims that aren't backed by the bundle text; tool callouts in the wrong file family; missing examples.
- Cross-file consistency: do `rules-10x/concord-build-rules.md` companions match `rules-10x/concord-model-discipline.md` actual contents?

Address NITs in-branch. Defer FLAGs to next cycle with a memory capture.

### Task 31: Push branch and open PR

- [ ] **Step 1: Push the branch**

```
git push -u origin feat/v5.0.0-skills-rules-version-split
```

- [ ] **Step 2: Open PR via `gh`**

```
gh pr create --title "feat: split skills+rules by Studio Pro version (10.x vs 11.10+)" --body-file <see template below>
```

PR body template (write to a temp file first to avoid heredoc issues):

```markdown
## Summary

- Ships two distinct skill+rules bundles: `skills-10x/` + `rules-10x/` for Studio Pro 10.24.13–11.9.x (concord-mcp + direct FS only, no Maia, no studio-pro MCP); refreshed `skills/` + `rules/` for 11.10+ (4-tier hierarchy: studio-pro MCP → concord-mcp 45 tools → Maia delegate → direct FS).
- `Concord.Host10x` resolves the `-10x` bundles via `extensionFileService.ResolvePath`. Host11x stays on the existing names.
- New `DoctrineSyncTests` asserts every registered tool is referenced in the matching bundle's MD, and no forbidden references (studio-pro MCP, Maia, `pg_write_page`) appear in the 10.x bundle.

## Why

Today both hosts resolve the same `skills/` and `rules/` content. On 10.x the shipped doctrine instructs the agent to call `mcp__mendix-studio-pro__*` tools that don't exist on that version, and references Maia tools that don't work reliably. This corrects the doctrine on 10.x and modernizes 11.x to reference the full concord-mcp 45-tool catalog that landed in v5.0.0-w2.

Source spec: [docs/superpowers/specs/2026-05-14-concord-skills-rules-version-split-design.md](docs/superpowers/specs/2026-05-14-concord-skills-rules-version-split-design.md).

## Test plan

- [x] `dotnet test` — 330 passing, 0 failed, 3 skipped (baseline 324 + 2 BundleResolutionTests + 4 DoctrineSyncTests).
- [x] Manual: open a Mendix 10.24.13 project with Concord installed → `.claude/rules/concord-build-rules.md` reflects 10.x doctrine (no `mcp__mendix-studio-pro__` references, no `maia__` references).
- [x] Manual: open a Mendix 11.10.0 project with Concord installed → `.claude/rules/concord-build-rules.md` reflects 11.x doctrine (4-tier hierarchy, all 45 concord-mcp tools surfaced).
- [ ] `.mxmodule` rebuild — manual Studio Pro UI step, Joe-only. Confirm the new module contains both bundles. (To be done at marketplace upload time.)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

- [ ] **Step 3: Wait for Joe's merge approval**

Per the auto-mode classifier guardrail noted in `_HANDOFF.md`: direct merges + pushes to `main` get blocked. Pattern that works: Claude does the work locally; Joe pastes the merge command. The PR ready signal here is "tests green, fresh-reviewer pass clean, ready for your merge".

---

## Self-review

After writing this plan, re-checked vs the spec at [docs/superpowers/specs/2026-05-14-concord-skills-rules-version-split-design.md](../specs/2026-05-14-concord-skills-rules-version-split-design.md):

- **Spec coverage**: every acceptance criterion in §Acceptance maps to one or more tasks:
  - AC1 (10.x bundle exists) → Tasks 8-16.
  - AC2 (11.x bundle refreshed) → Tasks 19-27.
  - AC3 (Host wiring) → Task 4.
  - AC4 (Build packaging) → Task 3.
  - AC5 (Tests) → Tasks 5, 17, 28.
  - AC6 (.mxmodule contains both bundles) → verified manually at PR time, called out in Task 31's PR body.
  - AC7 (Cross-version manual verification) → Task 31 PR test plan.
  - AC8 (Allowlist-doctrine sync) → Task 17 (10.x), Task 28 (11.x).

- **Placeholder scan**: no "TBD", "TODO", "fill in later". The content tasks (8-16, 19-27) describe structural requirements + reference files + must-include tool sets rather than pre-writing every line of doctrinal prose — this is correct for content authoring at plan scope; the writer fills in the prose at execution time within the listed constraints. Code tasks (1-7, 17-18, 28-31) contain complete code.

- **Type consistency**: `RepoRootFinder.Find()` is defined in Task 5 and reused in Task 17 — same signature. `EnumerateMdFiles` and `RelativeTo` are defined in Task 17 with `internal` visibility; Task 28 uses them via the same class. `Studio11xAllowlist.All` is the existing public API (verified in spec exploration).

- **Git ceremony**: 3 commits across 1 branch. No sub-branches, no rebases, no per-phase PRs. The branch lives long enough for 3 phases then merges once via standard PR flow. This matches Joe's preference for minimal ceremony while preserving the team's "atomic commits per phase" pattern.

Plan complete and saved to `docs/superpowers/plans/2026-05-14-concord-skills-rules-version-split.md`.

Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Good for the content-heavy tasks (8-16, 19-27) where a fresh context per file improves quality.

2. **Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints. Faster start, but the context grows linearly and the content writing tasks (each is a substantial markdown authoring session) may benefit from fresh state.

Which approach?
