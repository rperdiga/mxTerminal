# Concord Build Rules — Core (Studio Pro 10.x – 11.9.x)

> **Don't guess. Don't fake. Don't break.**

Always-loaded for any session driving this Mendix project via Concord on Studio Pro 10.24.13–11.9.x. These rules govern *how* you work, not *what* to build.

**Companion files** (also always-loaded; sections numbered globally so cross-references resolve regardless of which file they're cited from):

- `concord-pages-and-themes.md` — §2 Pages-via-concord-mcp · §8 Studio Pro UI handoffs · §10 Layout-first · §11 Custom theme
- `concord-model-discipline.md` — §5 concord-mcp model discipline · §6 Update operations · §7 Don't ship orphans · §9 New project = new module

Concord ships matching skill packs alongside these rules. **Read the relevant skill before the matching operation:**

- Microflows → `Read` `.claude/skills/mendix-microflow-common/SKILL.md` (and `mendix-microflow-syntax` for expressions / XPath, `mendix-microflow-update` for mutations).
- Pages → `Read` `.claude/skills/mendix-page-gen/SKILL.md`.
- View entities / OQL → `Read` `.claude/skills/mendix-view-entities/SKILL.md`.
- Workflows → `Read` `.claude/skills/mendix-workflow-common/SKILL.md` (and `mendix-workflow-update` for mutations).

Skills carry mandatory shape constraints — load them before, not after, the operation. Diving into model calls without the matching skill is the source of most schema failures.

---

## 1. Tool hierarchy — closed set (2-tier)

The full set of allowed paths for working on this Mendix project on Studio Pro 10.x:

**Tier 1 — Concord MCP server (`mcp__concord-mcp__*`):** The only model-side tool surface available. All model reads, writes, and queries go here.

Tool catalog by family:

**Domain Model**
`list_modules`, `create_module`, `read_project_info`, `read_domain_model`, `create_entity`, `create_association`, `create_multiple_entities`, `create_multiple_associations`, `create_domain_model_from_schema`, `delete_model_element`, `add_attribute`, `update_attribute`, `rename_entity`, `rename_attribute`, `rename_association`, `rename_document`, `rename_module`, `set_entity_generalization`, `remove_entity_generalization`, `add_event_handler`, `set_calculated_attribute`, `configure_system_attributes`, `arrange_domain_model`, `manage_folders`, `validate_name`, `copy_model_element`, `set_documentation`, `query_associations`, `query_model_elements`, `read_attribute_details`

**Microflows**
`list_microflows`, `read_microflow_details`, `create_microflow`, `create_microflow_activity`, `create_microflow_activities_sequence`, `update_microflow`, `modify_microflow_activity`, `insert_before_activity`, `set_microflow_url`, `check_variable_name`, `list_nanoflows`, `read_nanoflow_details`, `list_scheduled_events`

**Pages**
`generate_overview_pages`, `list_pages`, `read_page_details`, `exclude_document`, `delete_document`

**Navigation**
`manage_navigation`

**Security**
`list_rules`, `read_security_info`, `read_entity_access_rules`, `read_microflow_security`, `audit_security`

**Constants / Enumerations**
`create_constant`, `list_constants`, `update_constant`, `create_enumeration`, `list_enumerations`, `update_enumeration`, `rename_enumeration_value`, `configure_constant_values`

**Runtime / Configuration**
`read_runtime_settings`, `set_runtime_settings`, `read_configurations`, `set_configuration`, `read_version_control`, `list_rest_services`, `sync_filesystem`

**Diagnostics**
`check_model`, `check_project_errors`, `get_last_error`, `get_last_error_domain`, `get_studio_pro_logs`, `diagnose_associations`, `list_available_tools`, `list_available_tools_domain`, `list_java_actions`, `analyze_project_patterns`

**Workflows**
`list_workflows`, `read_workflow_details`

**UI Actions** (same surface as all Concord versions)
`run_app`, `stop_app`, `save_all`, `refresh_project`, `get_app_status`, `get_active_run_configuration`

**Tier 2 — Direct filesystem** (Bash / PowerShell read + write): For styling work (`theme/styles/web/**`, `themesource/<module>/web/**`), custom JavaScript actions (`javasource/**`), and any path outside the Mendix model. Use Read, Write, Edit, or Bash/PowerShell tooling directly. **Never write to `.mpr` directly** — it is binary SQLite and corrupts on direct edit.

**Supporting (not a separate tier):**
- Your own reasoning — analysis, JSON construction, schema diffs, planning.
- Built-in web search + `docs.mendix.com` — when knowledge is missing or an error string needs researching.

**Maia is excluded from 10.x doctrine.** The bridge does not produce reliable output on Studio Pro 10.x; the build path is 100% concord-mcp. This is intentional and permanent. There is no Maia bridge, no Maia page-write tool, and no Maia recovery ladder on this version.

**Forbidden, every time:**

- Editing `.mpr` directly (binary SQLite; corrupts on direct write).
- Filesystem writes against model files. The only filesystem-shaped exceptions are theme/SCSS files and JavaScript actions under `javasource/`.
- mxbuild, mxcli, npm against the project model. The model is single-transaction-at-a-time; external CLIs bypass that contract.
- Referring to Studio Pro MCP server tools — that MCP server does not exist on 10.x. Only `mcp__concord-mcp__*` is valid.
- Manually attaching MCP servers (`claude mcp add ...`). Concord wires `.mcp.json` automatically. If `mcp__concord-mcp__*` tools are not visible in your tool surface, surface that to the user and stop.

If a path is not in this list, it is not an option. The right move when a tool boundary blocks you is §3 (persist with evidence), not a parallel filesystem path.

---

## 3. Persistence — verbatim evidence required, no one-shot bails

You are not allowed to declare an operation "blocked," "unsupported," "outside the tool surface," or "not feasible" without:

1. **At least one actual MCP attempt.**
2. **The verbatim error returned** (HTTP status + message body, or the literal MCP response).
3. **At least one recovery move** before escalation.

**The one-shot bail antipattern.** A common failure: hit one error → declare blocked → silently pivot to other work → leave the original goal unfinished. Forbidden. If you hit an obstacle that prevents the user's primary goal, escalating to the user IS the primary goal until resolved.

### Recovery ladder for unexpected MCP errors on 10.x

Symptoms that look like dead ends are usually payload shape issues or wrong tool selection. Try in order:

1. **Confirm the matching skill is loaded** (see preamble). Microflows → `mendix-microflow-common`. View entities → `mendix-view-entities`. Workflows → `mendix-workflow-common`. Without the skill you'll fight the schema.
2. **Strip to minimal shape and retry.** On 10.x, constructors may flatten nested wrappers — read the `check_model` output carefully and reduce your payload to the schema's minimum. For microflows: `{name}` plus parameters if any. Build the body afterward via `update_microflow` / `modify_microflow_activity`.
3. **`mcp__concord-mcp__check_model`** — run this after any create or update that returns unexpected results. Diff your payload field-by-field against what the error reports.
4. **`mcp__concord-mcp__get_last_error`** and **`mcp__concord-mcp__get_studio_pro_logs`** — pull verbatim Studio Pro-side error detail. One log query beats four blind retries.
5. **Web search** for the verbatim error string against `docs.mendix.com`. Use the agent's built-in web search; cite the relevant reference page back in your response.
6. **Only then** escalate to the user — and the escalation must include the verbatim error from each attempt above.

There is no Maia retry budget on 10.x. The 11.x Maia-as-page-fixer tiebreaker does not apply here.

### Retry budgets

- **Error fixes via model-update calls after `check_model`:** **1 retry only — single-shot fix rule** (§5). If errors remain, STOP and report; do not retry.
- **General model writes (entities, microflows, etc.):** the recovery ladder above (steps 1–6); no fixed call count, but each step must produce *new* evidence. After three different payload shapes return the same error, jump to step 5 (web search) before a fourth retry.

The user asked for a thing. Deliver it, or come back with concrete evidence about why a specific tool boundary stopped you.

---

## 4. Read-back after every write — `SUCCESS` is necessary, not sufficient

A create or update call returning success proves the document exists. It does NOT prove the full payload landed.

After every `create_*` or `update_*` call where non-trivial properties were sent:

1. **Read back.** Use the appropriate read tool (`mcp__concord-mcp__read_microflow_details`, `mcp__concord-mcp__read_page_details`, `mcp__concord-mcp__read_domain_model`, `mcp__concord-mcp__read_attribute_details`, etc.) on the element you just wrote. Assert the value is what you sent.
2. **Check errors after the full task batch is complete.** `mcp__concord-mcp__check_model` once, *not* between every step. Mid-batch checks surface transient errors (a flow with origin set but no destination yet) and trigger thrash.

Skipping read-back ships hollow models that pass `check_model` and present empty pages, missing microflow activities, or unwired buttons in the running app.

---

## 12. Verification — three-part gate

Before claiming any feature done:

1. **`mcp__concord-mcp__check_model`** returns no errors on every document touched.
2. **The runtime reflects the change.** `mcp__concord-mcp__save_all` → `refresh_project` → `stop_app` → `run_app` → poll `get_app_status` until `running`. Skipping the cycle means verifying the previous version.
3. **The user-visible behavior works end-to-end** — walk the full journey arc *Browse → Detail → Action → Side-effect → User-facing list*. Click chains on a single page miss orphan-page and shell-microflow failures. Every step in the arc must produce its expected outcome.

### Errors-before-`run_app` hard gate

**`run_app` is GATED by zero errors.** Before calling `mcp__concord-mcp__run_app`, run `mcp__concord-mcp__check_model` on every document touched in this build. If ANY document has errors, do NOT call `run_app`. Resolve errors first via the §3+§5 fix ladder. The runtime won't surface fixable model errors usefully — they'll appear as runtime crashes, console spew, or pages that 500 in the browser. Fix at the model layer where errors are actionable.

Self-reports of "verified," "working," "live," "done" are claims, not evidence. Evidence is screenshots from a click chain landing on the expected destination, DOM assertions against `.mx-name-*` selectors, and the verbatim `check_model` output.

If a Playwright MCP is attached in this environment (look for `mcp__playwright__*` tools in your tool surface), use it for the end-to-end walk and capture screenshots at each step. Without Playwright, the verification reduces to "fewer-than-end-to-end" — note that explicitly when reporting.

### Cadence — `save_all` + `refresh_project` after every batch

Verification at the end of a build is necessary but not sufficient. Studio Pro keeps every model write in an in-memory model until something flushes it to the `.mpr` file on disk — auto-save, the user hitting Ctrl+S, or `mcp__concord-mcp__save_all`. Without that flush, your work exists only in Studio Pro's RAM.

- **After each batch, call `mcp__concord-mcp__save_all` followed by `mcp__concord-mcp__refresh_project`.** A batch is one or more create or update calls that finish a logical unit — a module created and named, a microflow body wired up, a domain change with all its associations.
- **Why the flush.** Without `save_all`, your writes don't land on `.mpr`. Read-back tools and `check_model` hit the in-memory model, so they appear correct — but a Studio Pro crash or restart sees nothing on disk.
- **Why the refresh.** Without `refresh_project`, subsequent reads may return stale state — the on-disk `.mpr` and Studio Pro's in-memory model can diverge after a save, and `refresh_project` reconciles them.
- **Cadence is "after each batch" — not "after each individual write" (would thrash) and not "at end of build" (loses work on crash).** Pick natural seams: module created → save+refresh; microflow wired → save+refresh; domain change committed → save+refresh.
- **Read-back (§4) and error-checks happen *after* the save+refresh of the same batch**, not before — so the read hits a consistent on-disk + in-memory state.

#### Time-based save fallback for iterative phases

**Hard rule: call `mcp__concord-mcp__save_all` + `refresh_project` at least every 15 minutes of continuous work, OR every 10 consecutive model-update calls without an intervening save — whichever comes first.** The dual trigger gives you both a wall-clock fallback and a deterministic count-based fallback. Cheap operation; the cost of skipping it is hard-crash-loses-an-hour-of-work.

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

These rules cover what to do in general. Every project has its own conventions you'll discover *during* the build — domain glossary terms the user prefers, brittle patterns specific to this app's data, naming choices the user has corrected you on. Persist them so future sessions in this same project don't re-learn from scratch.

**Where to write learnings:**

- `.claude/rules/project/learned-<topic>.md` — your free space. Drop a `.md` file here for each durable learning. Concord auto-imports every `.md` in this folder into `CLAUDE.md` on its next Save, so future sessions load the file alongside these rules.
- The folder survives Concord upgrades — Concord pre-creates it once and never overwrites contents.
- Naming convention for clarity: prefix with `learned-` (e.g. `learned-domain-glossary.md`, `learned-widget-quirks.md`) so future readers can see at a glance what's user-authored vs. agent-discovered.

**What to write:**

- Domain terms the user has named (entity names, microflow conventions, theme-token vocabulary).
- Shape gotchas you hit and resolved (with the verbatim error if relevant).
- Integration patterns specific to this project (REST endpoints, external system contracts).
- User corrections — when the user redirects you on naming or structure, write the rule down.

**What NOT to write:**

- Generic Mendix knowledge — already in this rules file or the bundled skills.
- Speculation — only persist learnings backed by evidence (a successful build, an error and its fix, an explicit user statement).
- Anything that would belong in the `concord-*.md` files themselves — those are Concord-managed and overwritten on every Save. **Never modify those files directly; your edits will be lost.**

The pattern in one line: *if it's true for this project and you'll need it next session, write `.claude/rules/project/learned-<topic>.md`.*

---

## 15. Search and external references

On 10.x, the Studio Pro MCP server is not present, so there is no Studio Pro-hosted knowledge base or web-proxy tool. Use the agent's own capabilities:

- **Built-in web search** with a verbatim error string or a targeted question. Search `docs.mendix.com` explicitly for model-specific reference content.
- **`docs.mendix.com`** is the canonical reference — search it, pull from it, cite the specific page back to the user.
- **`mcp__concord-mcp__get_studio_pro_logs`** and **`mcp__concord-mcp__get_last_error`** for Studio Pro-side error detail before reaching for external references.

These are tools to use *during* the §3 recovery ladder, not a separate workflow. The "search before a fourth retry" rule lives in §3.

---

## Cross-reference

- **Concord shipped skills** (read on trigger, located at `.claude/skills/<name>/SKILL.md`): `mendix-microflow-common`, `mendix-microflow-syntax`, `mendix-microflow-update`, `mendix-page-gen`, `mendix-view-entities`, `mendix-workflow-common`, `mendix-workflow-update`.
- **Project-specific rules** — drop additional `.md` files into `.claude/rules/project/`. Concord auto-discovers them and adds `@`-imports to `CLAUDE.md` so they auto-load alongside this file. Concord upgrades never overwrite anything in `.claude/rules/project/`.
