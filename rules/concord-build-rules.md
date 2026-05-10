# Concord Build Rules — Core

> **Don't guess. Don't fake. Don't break.**

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

## 1. Tool hierarchy — closed set

The full set of allowed paths for working on this Mendix project:

1. **Studio Pro itself** — the IDE window, the Maia panel inside it, native UI actions.
2. **Studio Pro MCP server** (`mcp__mendix-studio-pro__*`):
   - `ped_*` — domain models, microflows, workflows, view entities (read / create / update / remove).
   - `oql_*` — OQL generation and reading for view entities (`oql_generate`, `oql_read`).
   - `read_skill`, `search_mendix_knowledge_base`, `web_fetch`.
   - `glob`, `read_file`, `write_file` — scoped to file domains registered by the server. As of Studio Pro 11.10 the registered roots are `/themes` and `/jsactions`. Always call `glob` first to confirm the current set; future Studio Pro versions may register additional roots.
3. **Concord MCP server** (`mcp__concord-mcp__*`):
   - UI actions: `run_app`, `stop_app`, `refresh_project`, `save_all`, `get_app_status`, `get_active_run_configuration`.
   - Maia bridge (Windows only): `maia__ask`, `maia__send`, `maia__status`, `maia__wait`, `maia__reset`. (The Concord MCP also exposes `maia__force_tier` as a debug aid; do not use it unless the user explicitly asks for transport-tier diagnostics.)
4. **Maia** in Studio Pro — reachable via the Concord bridge (Windows) or via you handing the user a copy-paste prompt for them to drop into Maia themselves (macOS).
5. **Your reasoning** — analysis, JSON construction, schema diffs, planning.
6. **Web search and `docs.mendix.com`** — when knowledge is missing.

**Forbidden, every time:**

- Editing `.mpr` directly (binary SQLite; corrupts on direct write).
- Filesystem writes against model files. The only filesystem-shaped exceptions are `/themes/**` and `/jsactions/**`, and even there, prefer `mcp__mendix-studio-pro__write_file` — the registered file-domain path.
- mxbuild, mxcli, npm against the project. The model is single-transaction-at-a-time; external CLIs bypass that contract.
- Direct `Bash` / `PowerShell` against the project's model directories. Read-only inspection is fine; writes are not.
- Manually attaching MCP servers (`claude mcp add ...`). Concord wires `.mcp.json` and `~/.codex/config.toml` automatically. If `mcp__mendix-studio-pro__*` or `mcp__concord-mcp__*` aren't visible in your tool surface, surface that to the user and stop — don't manually patch around it.

If a path is not in this list, it is not an option. The right move when an MCP boundary blocks you is §3 (persist with evidence), not a parallel filesystem path.

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
- **Maia bridge calls — hard cap.** If `maia__status` / `maia__wait` returns a non-success response **3 consecutive times**, STOP firing further `maia__ask` / `maia__send` / `maia__status` / `maia__wait` / `maia__reset` calls and surface the verbatim errors to the user. Walking off the §2 bridge ladder in a loop (re-warming, re-probing, re-resetting on every cycle) is a worse failure mode than escalation — it burns calls, fills the transcript, and never converges. The cap counts the bridge as a whole, not each tool individually: `status` failing then `send` failing then `reset` failing is three consecutive failures, full stop. Empirically grounded — non-converging bridge loops have run >40 calls in a single window without ever escalating.

**Tiebreaker — Maia page write surfaces errors.** When `ped_check_errors` reports problems on a page that Maia just wrote, you may **either** re-prompt Maia with refined JSON (per §2's 2-retry cap) **or** PED-patch a specific attribute (per §5's 1-retry cap). Pick one path and respect that path's cap; don't combine them for an effective 3-retry budget on the same page.

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

Self-reports of "verified," "working," "live," "done" are claims, not evidence. Evidence is screenshots from a click chain landing on the expected destination, DOM assertions against `.mx-name-*` selectors, and the verbatim `ped_check_errors` output.

If a Playwright MCP is attached in this environment (look for `mcp__playwright__*` tools in your tool surface), use it for the end-to-end walk and capture screenshots at each step. Without Playwright, the verification reduces to "fewer-than-end-to-end" — note that explicitly when reporting.

### Cadence — `save_all` + `refresh_project` after every batch

Verification at the end of a build is necessary but not sufficient. Studio Pro keeps every PED write in an in-memory model until something flushes it to the `.mpr` file on disk — auto-save, the user hitting Ctrl+S, or `mcp__concord-mcp__save_all`. Without that flush, your work exists only in Studio Pro's RAM.

- **After each batch, call `mcp__concord-mcp__save_all` followed by `mcp__concord-mcp__refresh_project`.** A batch is one or more `ped_create_module`, `ped_create_document`, or `ped_update_document` calls that finish a logical unit — a module created and named, a page authored end-to-end, a microflow body wired up, a domain change with all its associations.
- **Why the flush.** Without `save_all`, your writes don't land on `.mpr`. `ped_read_document` and `ped_check_errors` hit the in-memory model, so they appear correct — but a Studio Pro crash, restart, or another agent reading the disk file sees nothing. Empirically (2026-05-09 cocktail test): a build session ended with 100+ orphan `.mxunit` files in `mprcontents/` representing pages and a layout that existed in Studio Pro's in-memory model but never landed on disk because no batch ever called `save_all`.
- **Why the refresh.** Without `refresh_project`, subsequent reads may return stale state — the on-disk `.mpr` and Studio Pro's in-memory model can diverge after a save, and `refresh_project` reconciles them.
- **Cadence is "after each batch" — not "after each individual write" (would thrash) and not "at end of build" (loses work on crash).** Pick natural seams: module created → save+refresh; page authored → save+refresh; microflow wired → save+refresh; domain change committed → save+refresh.
- **Read-back (§4) and error-checks happen *after* the save+refresh of the same batch**, not before — so the read hits a consistent on-disk + in-memory state.

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
