# Concord Build Rules — Core

> **Don't guess. Don't fake. Don't break.**

> **Doctrine sync:** This file references every tool in `Studio11xAllowlist.All` (the 45-tool concord-mcp 11.x catalog) plus the studio-pro MCP and Maia surfaces. When `Studio11xAllowlist` changes (a tool added, removed, renamed), this file and the matching skill files must be refreshed to match. `DoctrineSyncTests` in `Concord.Core.Tests` fails the build when drift appears.

Always-loaded for any session driving this Mendix project via Concord. These rules govern *how* you work, not *what* to build.

**Companion files** (also always-loaded; sections numbered globally so cross-references resolve regardless of which file they're cited from):

- `concord-pages-and-themes.md` — §2 Pages-via-Maia · §8 Studio Pro UI handoffs · §10 Layout-first · §11 Custom theme
- `concord-model-discipline.md` — §5 `ped_*` discipline · §6 Update operations · §7 Don't ship orphans · §9 New project = new module

Concord ships matching skill packs alongside these rules. **Read the relevant skill before the matching operation:**

- Microflows → `Read` `.claude/skills/mendix-microflow-common/SKILL.md` (and `mendix-microflow-syntax` for expressions / XPath, `mendix-microflow-update` for mutations).
- Pages → `Read` `.claude/skills/mendix-page-gen/SKILL.md`.
- View entities / OQL → `Read` `.claude/skills/mendix-view-entities/SKILL.md`.
- Workflows → `Read` `.claude/skills/mendix-workflow-common/SKILL.md` (and `mendix-workflow-update` for mutations).

Skills carry mandatory shape constraints — load them before, not after, the operation. Diving into PED calls without the matching skill is the source of most schema failures.

---

## 1. Tool hierarchy — closed set, 4 tiers

The full set of allowed paths for working on this Mendix project. Tiers are ordered: exhaust Tier 1 before reaching for Tier 2; exhaust Tiers 1+2 before reaching for Tier 3; Tier 4 only for filesystem targets outside the MCP tool surface.

### Tier 1 — Studio Pro MCP server (`mcp__mendix-studio-pro__*`)

The built-in Mendix server. Use these first for all model reads and writes.

- `ped_*` — domain models, microflows, workflows, view entities (read / create / update / remove).
- `oql_*` — OQL generation and reading for view entities (`oql_generate`, `oql_read`).
- `read_skill`, `search_mendix_knowledge_base`, `web_fetch` — knowledge and docs.
- `glob`, `read_file`, `write_file` — scoped to file domains registered by the server. As of Studio Pro 11.10 the registered roots are `/themes` and `/jsactions`. Always call `glob` first to confirm the current set; future Studio Pro versions may register additional roots.

### Tier 2 — Concord MCP server (`mcp__concord-mcp__*`)

The 45-tool catalog Concord installs alongside the Studio Pro MCP. Use these when Tier 1 doesn't reach the operation. Grouped by family:

**UI actions** — reach for these to control the app lifecycle and project state:
- `run_app` — start the runtime.
- `stop_app` — stop the runtime.
- `save_all` — flush Studio Pro's in-memory model to disk (`.mpr`). Call after every batch.
- `refresh_project` — reconcile Studio Pro's in-memory model with the on-disk state. Call after every `save_all`.
- `get_app_status` — poll runtime state (starting / running / stopped).
- `get_active_run_configuration` — read the current run configuration name.

**Domain model gap-fillers** — use these when Tier 1's `ped_*` doesn't handle the operation (reference-safe renames, surgical deletes, visual layout):
- `rename_entity` — rename an entity and update all references (microflows, pages, OQL views). Prefer over `ped_update_document` for renames.
- `rename_attribute` — rename an attribute and update all references. Prefer over `ped_update_document`.
- `rename_association` — rename an association and update all references.
- `rename_document` — rename any document (page, microflow, layout, etc.) and update all references.
- `rename_module` — rename a module and update all references.
- `rename_enumeration_value` — rename an enumeration value and update all references.
- `delete_model_element` — safely delete an entity, attribute, association, or other model element. Use this (not `ped_update_document` remove) for hard deletes and orphan cleanup.
- `set_documentation` — write a docstring onto an entity, attribute, microflow, or other model element. Use post-create to add documentation without reopening the full element.
- `arrange_domain_model` — lay out entities visually in the domain model diagram. Call after a batch entity create to avoid overlapping nodes.

**Microflow gap-fillers** — operations missing from the Studio Pro MCP's microflow edit surface:
- `exclude_document` — exclude a document from a module export (mark as excluded).
- `set_microflow_url` — set the REST-publish URL on a published microflow.
- `modify_microflow_activity` — surgically modify a single microflow activity without rewriting the entire flow.
- `insert_before_activity` — insert a new activity immediately before an existing one in a microflow.

**Page lifecycle** — operations beyond what `ped_create_document` handles:
- `generate_overview_pages` — scaffold list + detail pages from an entity. Use this (Tier 2) before reaching for Maia (Tier 3) for simple entity CRUD.
- `delete_document` — delete a page or other document from the project. Use when `ped_*` remove isn't available for the doc type.

**Navigation** — programmatic nav graph edits:
- `manage_navigation` — read and write navigation menu items and role-based home pages. Use this instead of the Studio Pro UI handoff for navigation changes (see §8).

**Security audit** — read-only security introspection:
- `read_security_info` — read module security settings.
- `read_entity_access_rules` — read entity access rules for all roles.
- `read_microflow_security` — read microflow allowed-roles configuration.
- `audit_security` — run a security audit across the project and surface gaps.

**Runtime / Configuration** — read and write app runtime settings and configurations:
- `read_runtime_settings` — read all runtime settings (e.g. scheduled-event toggles, constant values).
- `set_runtime_settings` — write runtime settings.
- `read_configurations` — list all project configurations (Dev / Test / Prod variants).
- `set_configuration` — set the active project configuration.

**Diagnostics** — model and project health checks:
- `check_model` — run a model consistency check (broader than `ped_check_errors`).
- `check_project_errors` — check project-level errors (app settings, deployment, configuration).
- `get_studio_pro_logs` — retrieve recent Studio Pro log output.
- `get_last_error` — fetch the most recent error recorded by Studio Pro.
- `analyze_project_patterns` — analyze structural patterns across the project (unused documents, naming drift, etc.).

### Tier 3 — Maia delegate (`mcp__concord-mcp__maia__*`)

Windows only. Reach for Maia when Tiers 1+2 don't cover the operation — typically rich page authoring beyond `generate_overview_pages` scaffolding, layout creation, or natural-language Studio Pro interactions.

Entry condition check: before calling any `maia__*` tool, confirm Tier 1 `ped_*` and Tier 2 `generate_overview_pages` / `delete_document` don't already cover the operation. The Maia bridge adds latency and has its own failure modes (§2, §3); don't reach for it when a PED or concord-mcp tool suffices.

**Request / response / recovery:**
- `maia__send` — fire a prompt at Maia without waiting for the response (fire-and-forget; poll with `maia__status` or `maia__wait`).
- `maia__status` — poll Maia's current generation state.
- `maia__wait` — block until Maia finishes generating (or timeout).
- `maia__ask` — send a prompt and wait for Maia's response in one call. Preferred over `maia__send` + `maia__wait` for simple request/response pairs.
- `maia__reset` — reinitialize bridge transports. Use to recover FROM observed failure — not prophylactically (see §2 recovery ladder and empirical baseline on `maia__reset` overuse).

**Introspection (v4.2.1+):**
- `maia__busy` — read-only: is Maia currently generating? Returns `{busy, reason, idle_for_ms}`. Do NOT interpret `busy=true` as a failure; it correctly signals Maia is mid-generation.
- `maia__ping` — cheap liveness probe with 5s default timeout. Returns `{alive, latency_ms, response}`. `{alive: false, timed_out: true}` IS a failure; run the recovery ladder.
- `maia__health` — bridge-state snapshot without Maia traffic. Returns transport availability and in-flight handle bindings. If all transports return `available: false`, the WebSocket is dead; jump to recovery ladder step 3.
- `maia__new_chat` — wipe Maia's chat context (see §2 ladder step 3.5 and task-boundary new-chat section). Always call `maia__busy` first; do NOT interrupt mid-generation.

**Debug only:**
- `maia__force_tier` — force a specific Maia transport tier. **Do not use unless the user explicitly asks for transport-tier diagnostics.** This tool is excluded from `DoctrineSyncTests`' enforcement; its presence here is for completeness only.

The full Maia operational ladder (entry, retry budgets, recovery, 3-consecutive-failure stop rule, tiebreakers) lives in §2 of `concord-pages-and-themes.md` and §3 of this file. Load both before reaching for Maia.

### Tier 4 — Direct filesystem

- **Inside `/themes/` or `/jsactions/`** (the registered file domains) → use `mcp__mendix-studio-pro__write_file` (Tier 1). Do not bypass this with direct FS for files inside those roots.
- **Outside the registered domains** → direct FS via Bash/PowerShell is acceptable (e.g. custom scripts, CI config, project-level docs outside the Mendix model).
- **Never write `.mpr` directly.** It is a binary SQLite file; direct writes corrupt it.

### Supporting concerns (not a tier)

- **Your reasoning** — analysis, JSON construction, schema diffs, planning. Not a tool call; always available.
- **Web search and `docs.mendix.com`** — when knowledge is missing. Use `mcp__mendix-studio-pro__search_mendix_knowledge_base` and `mcp__mendix-studio-pro__web_fetch`. These are supporting tools for the §3 recovery ladder, not a separate workflow tier.

### Forbidden, every time

- Editing `.mpr` directly (binary SQLite; corrupts on direct write).
- Filesystem writes against model files outside the Tier 1 file domains. The only filesystem-shaped exceptions are `/themes/**` and `/jsactions/**`, and even there, prefer `mcp__mendix-studio-pro__write_file` — the registered file-domain path (Tier 1).
- mxbuild, mxcli, npm against the project. The model is single-transaction-at-a-time; external CLIs bypass that contract.
- Direct `Bash` / `PowerShell` writes against the project's model directories. Read-only inspection is fine; writes are not.
- Manually attaching MCP servers (`claude mcp add ...`). Concord wires `.mcp.json` and `~/.codex/config.toml` automatically. If `mcp__mendix-studio-pro__*` or `mcp__concord-mcp__*` aren't visible in your tool surface, surface that to the user and stop — don't manually patch around it.

If a path is not in this hierarchy, it is not an option. The right move when an MCP boundary blocks you is §3 (persist with evidence), not a parallel filesystem path.

---

## 3. Persistence — verbatim evidence required, no one-shot bails

You are not allowed to declare an operation "blocked," "unsupported," "outside the tool surface," or "not feasible" without:

1. **At least one actual MCP attempt.**
2. **The verbatim error returned** (HTTP status + message body, or the literal MCP response).
3. **At least one recovery move** before escalation.

**The one-shot bail antipattern.** A common failure: hit one error → declare blocked → silently pivot to other work → leave the original goal unfinished. Forbidden. If you hit an obstacle that prevents the user's primary goal, escalating to the user IS the primary goal until resolved. Don't keep building unrelated stuff hoping the obstacle dissolves on its own.

### Recovery ladder for unexpected MCP errors

Symptoms that look like dead ends are usually payload shape issues. Try in order:

1. **Confirm the matching skill is loaded** (see preamble). Pages → `mendix-page-gen`. Microflows → `mendix-microflow-common`. View entities → `mendix-view-entities`. Workflows → `mendix-workflow-common`. Without the skill you'll fight the schema.
2. **Strip to minimal shape and retry.** PED constructors are flattened (§5). Extras beyond the documented schema are silently dropped on permissive types (Pages$Page) or 500'd with a stack trace on strict types (Microflows). For Pages: `{name, layout}` only. For Microflows: `{name}` (and `parameters` if any). Build the body afterward via `ped_update_document`.
3. **`mcp__mendix-studio-pro__ped_get_schema`** for the element type and diff your payload field-by-field against the `$constructor` schema.
4. **`mcp__mendix-studio-pro__search_mendix_knowledge_base`** with the verbatim error string. One KB query beats four blind retries.
5. **`mcp__mendix-studio-pro__web_fetch`** against `docs.mendix.com` for the relevant reference page.
6. **Only then** escalate to the user — and the escalation must include the verbatim error from each attempt above.

### Retry budgets — different per operation type

- **Page writes via Maia (§2):** 2 retries with refined JSON, then escalate.
- **Error fixes via `ped_update_document` after `ped_check_errors`:** **1 retry only — single-shot fix rule** (§5). If errors remain, STOP and report; do not retry.
- **General PED writes (entities, microflows, etc.):** the recovery ladder above (steps 1–6); no fixed call count, but each step must produce *new* evidence (a new schema, a new error, a new payload variant). After three different payload shapes return the same error, jump to step 4 (KB search) before a fourth retry.
- **Maia bridge calls — task-scoped loop cap.** Count consecutive failures on **the same logical operation** — same handle being polled, same prompt being re-tried after refinement, same `maia__reset` + re-probe cycle running without forward progress between resets. If that operation fails **3 consecutive times**, STOP firing further `maia__*` calls against it and surface the verbatim errors to the user. Different operations each failing once across a build is normal transient bridge noise: v4.2.0's auto-reconnect absorbs it cleanly — **continue past each one**. The signal you're in a stuck loop is REPETITION on a single target, not raw error count across the whole build. Empirically: a build that hits one Unknown handle on page A, one IOException on page B's submit, and one Unexpected shape on layout C is doing fine — three transient errors across three independent tasks, three independent recoveries. A build that calls `maia__reset` three times in a row without making forward progress between resets, or polls the same handle three times and gets the same error each time, is stuck — STOP that one and escalate. The cap is per-target, not global.

**Tiebreaker — Maia page write surfaces errors.** When `ped_check_errors` reports problems on a page that Maia just wrote, you may **either** re-prompt Maia with refined JSON (per §2's 2-retry cap) **or** PED-patch a specific attribute (per §5's 1-retry cap). Pick one path and respect that path's cap; don't combine them for an effective 3-retry budget on the same page.

**Maia-as-page-fixer tiebreaker — Page errors get a second opinion before user escalation.** After `ped_check_errors` reports errors on a page document, AND your one allowed `ped_update_document` fix per §5 didn't resolve them, **AND IF the failing doc is one Maia can edit (i.e. `Pages$Page`)**, hand it back to Maia BEFORE escalating to the user. Use this prompt template:

```
Page <Module>.<Page> has these errors after my fix attempt:
<verbatim ped_check_errors output>
Use pg_write_page to fix the page. Report back when done.
```

After Maia's attempt, re-run `ped_check_errors`. If errors still remain, THEN escalate to user. This mirrors §2's "Maia owns pages" doctrine — page errors deserve a Maia second-opinion before homework lands on the user. **Note:** this tiebreaker is for Page docs only — non-page docs (microflows, entities, view entities) stay on the §5 single-shot rule (Maia can't edit those reliably).

The user asked for a thing. Deliver it, or come back with concrete evidence about why a specific MCP boundary stopped you. See §7 #4 — letter-not-spirit compliance is the failure mode this rule prevents.

---

## 4. Read-back after every write — `SUCCESS` is necessary, not sufficient

`ped_create_document` returning `SUCCESS: Creating documents (1)` proves the document exists. It does NOT prove anything beyond the constructor's schema-declared minimum landed. Pages$Page is silently permissive — it accepts a `widgets` array, returns SUCCESS, and discards everything not in the constructor schema.

After every `ped_create_document` or `ped_update_document` where extras were sent:

1. **Read back.** `mcp__mendix-studio-pro__ped_read_document` on the slot you wrote. Assert the value is what you sent.
2. **Check errors after the full task batch is complete.** `ped_check_errors` once, *not* between every step. Mid-batch checks surface transient errors (a flow with origin set but no destination yet) and trigger thrash.

Skipping read-back ships hollow models that pass `ped_check_errors` and present empty pages, missing widgets, or unwired buttons in the running app.

---

## 12. Verification — three-part gate

Before claiming any feature done:

1. **`mcp__mendix-studio-pro__ped_check_errors`** returns no errors on every document touched.
2. **The runtime reflects the change.** `mcp__concord-mcp__save_all` → `refresh_project` → `stop_app` → `run_app` → poll `get_app_status` until `running`. Skipping the cycle means verifying the previous version.
3. **The user-visible behavior works end-to-end** — walk the full journey arc *Browse → Detail → Action → Side-effect → User-facing list*. Click chains on a single page miss orphan-page and shell-microflow failures. Every step in the arc must produce its expected outcome.

### Errors-before-`run_app` hard gate

**`run_app` is GATED by zero errors.** Before calling `mcp__concord-mcp__run_app`, run `ped_check_errors` on every document touched in this build. If ANY document has errors, do NOT call `run_app`. Resolve errors first via the §3+§5 fix ladder + the Maia tiebreaker (below). The runtime won't surface fixable model errors usefully — they'll appear as runtime crashes, console spew, or pages that 500 in the browser. Fix at the model layer where errors are actionable.

**Empirical (CocktailDemo33, 2026-05-10):** an agent called `run_app` 19 times trying to start an app that had two unresolved errors in the Studio Pro Error tab. Each call failed identically. The gate would have caught this on call 1 and routed the agent to fix the errors instead of looping on the runtime. Don't repeat it.

Self-reports of "verified," "working," "live," "done" are claims, not evidence. Evidence is screenshots from a click chain landing on the expected destination, DOM assertions against `.mx-name-*` selectors, and the verbatim `ped_check_errors` output.

If a Playwright MCP is attached in this environment (look for `mcp__playwright__*` tools in your tool surface), use it for the end-to-end walk and capture screenshots at each step. Without Playwright, the verification reduces to "fewer-than-end-to-end" — note that explicitly when reporting.

### Cadence — `save_all` + `refresh_project` after every batch

Verification at the end of a build is necessary but not sufficient. Studio Pro keeps every PED write in an in-memory model until something flushes it to the `.mpr` file on disk — auto-save, the user hitting Ctrl+S, or `mcp__concord-mcp__save_all`. Without that flush, your work exists only in Studio Pro's RAM.

- **After each batch, call `mcp__concord-mcp__save_all` followed by `mcp__concord-mcp__refresh_project`.** A batch is one or more `ped_create_module`, `ped_create_document`, or `ped_update_document` calls that finish a logical unit — a module created and named, a page authored end-to-end, a microflow body wired up, a domain change with all its associations.
- **Why the flush.** Without `save_all`, your writes don't land on `.mpr`. `ped_read_document` and `ped_check_errors` hit the in-memory model, so they appear correct — but a Studio Pro crash, restart, or another agent reading the disk file sees nothing. Empirically (2026-05-09 cocktail test): a build session ended with 100+ orphan `.mxunit` files in `mprcontents/` representing pages and a layout that existed in Studio Pro's in-memory model but never landed on disk because no batch ever called `save_all`.
- **Why the refresh.** Without `refresh_project`, subsequent reads may return stale state — the on-disk `.mpr` and Studio Pro's in-memory model can diverge after a save, and `refresh_project` reconciles them.
- **Cadence is "after each batch" — not "after each individual write" (would thrash) and not "at end of build" (loses work on crash).** Pick natural seams: module created → save+refresh; page authored → save+refresh; microflow wired → save+refresh; domain change committed → save+refresh.
- **Read-back (§4) and error-checks happen *after* the save+refresh of the same batch**, not before — so the read hits a consistent on-disk + in-memory state.

#### Time-based save fallback for visual-polish phases

Batch-based cadence works well during build phases (each new module / page / microflow creation is a natural batch boundary). **It DOES NOT work during pure-polish phases** — re-running `pg_write_page` against existing pages, tweaking theme variables, iterating on visual fidelity through Maia. Those phases iterate page → check → iterate without ever crossing a batch boundary, so `save_all` is never triggered.

**Hard rule: call `mcp__concord-mcp__save_all` + `refresh_project` at least every 15 minutes of continuous work, OR every 10 consecutive `pg_*` / `ped_update_document` calls without an intervening save — whichever comes first.** The dual trigger gives you both a wall-clock fallback and a deterministic count-based fallback that doesn't require time-tracking between tool calls. Flush on the next clean turn boundary once either threshold is reached. Cheap operation (one Ctrl+S to Studio Pro); the cost of skipping it is hard-crash-loses-an-hour-of-work.

**Empirical (CocktailDemo34, 2026-05-10):** Codex iterated on visual-polish for ~54 minutes without calling `save_all`, then the user's machine crashed 3 minutes after a belatedly-fired save. The crash happened to fall AFTER the save, so the build survived. Had the crash fired 4 minutes earlier, 54 minutes of polish work would have been lost. v4.2.0/v4.2.1's hard-crash resilience covers extension + bridge state; it does NOT cover unflushed model state. The 15-minute fallback closes the gap.

**Sane heuristic:** if your current iteration loop is going to make ≥3 more `pg_write_page` calls without hitting a batch boundary, save NOW. The next save will land on a natural batch boundary anyway.

---

## 13. Plan-before-write for non-trivial builds

If the user is asking for ≥2 named user journeys, ≥3 pages, or any dedicated theme/layout work, write a one-page build plan before touching the model. Cover:

- **Module name** (per §9) — single module for the whole app.
- **Layout** (per §10) — needed or not, and what shape.
- **Domain** — entities, attributes (with types and lengths), associations (parent / child / multiplicity), enumerations.
- **Behavior** — microflows by name, what each does, which entities they read / write / commit.
- **UI** — pages by name, layout reference, key widgets, navigation graph.
- **Theme (if applicable)** — brand variables, type scale, key color tokens.

**Surface the plan, then *proceed in the same turn* unless the user objects.** The plan is informational, not gating. It exists so the user can redirect early — not so you stop working. If the user is silent, take it as alignment and execute. If the user objects, revise and proceed.

This isn't ceremony — it's how the user catches a missing journey arc *before* you've committed the wrong shape.

For trivial builds (1–2 pages, default Atlas, no journey graph), skip the plan and proceed direct.

---

## 14. Persisting what you learn during a build

These rules cover what to do in general. Every project has its own conventions you'll discover *during* the build — domain glossary terms the user prefers, brittle widget patterns specific to this app's data, naming choices the user has corrected you on, integration quirks. Persist them so future sessions in this same project don't re-learn from scratch.

**Where to write learnings:**

- `.claude/rules/project/learned-<topic>.md` — your free space. Drop a `.md` file here for each durable learning. Concord auto-imports every `.md` in this folder into `CLAUDE.md` on its next Save, so future sessions load the file alongside these rules.
- The folder survives Concord upgrades — Concord pre-creates it once and never overwrites contents.
- Naming convention for clarity: prefix with `learned-` (e.g. `learned-domain-glossary.md`, `learned-widget-quirks.md`) so future readers can see at a glance what's user-authored vs. agent-discovered.

**What to write:**

- Domain terms the user has named (entity names, microflow conventions, theme-token vocabulary).
- Widget-shape gotchas you hit and resolved (with the verbatim error if relevant).
- Integration patterns specific to this project (REST endpoints, external system contracts).
- User corrections — when the user redirects you on naming or structure, write the rule down.

**What NOT to write:**

- Generic Mendix knowledge — already in this rules file or the bundled skills.
- Speculation — only persist learnings backed by evidence (a successful build, an error and its fix, an explicit user statement).
- Anything that would belong in the `concord-*.md` files themselves — those are Concord-managed and overwritten on every Save. **Never modify those files directly; your edits will be lost.**

The pattern in one line: *if it's true for this project and you'll need it next session, write `.claude/rules/project/learned-<topic>.md`.*

---

## 15. Search and external references

- **`mcp__mendix-studio-pro__search_mendix_knowledge_base`** with a verbatim error string. The KB is curated Mendix content.
- **`mcp__mendix-studio-pro__web_fetch`** for any URL the user gave you (visual references, external docs).
- **`docs.mendix.com`** is the canonical reference — search it, pull from it, cite it back to the user.

These are tools to use *during* the §3 recovery ladder, not a separate workflow. The "search before a fourth retry" rule lives in §3.

---

## Cross-reference

- **Concord shipped skills** (read on trigger, located at `.claude/skills/<name>/SKILL.md`): `mendix-microflow-common`, `mendix-microflow-syntax`, `mendix-microflow-update`, `mendix-page-gen`, `mendix-view-entities`, `mendix-workflow-common`, `mendix-workflow-update`.
- **Studio Pro MCP system prompt** — load via `mcp__mendix-studio-pro__ReadMcpResourceTool` against `mendix://studio-pro/system-prompt`. Doctrine on PED, schemas, safety, and the single-shot fix rule.
- **Studio Pro MCP `read_skill` directory** — `folder-structure`, `page-gen-common`, `microflow-common`, `microflow-expressions`, `microflow-update`, `microflow-xpath`, `view-entities`, `workflow-common`. Load via `mcp__mendix-studio-pro__read_skill` before the matching operation.
- **Project-specific rules** — drop additional `.md` files into `.claude/rules/project/`. Concord auto-discovers them and adds `@`-imports to `CLAUDE.md` so they auto-load alongside this file. Concord upgrades never overwrite anything in `.claude/rules/project/`.
