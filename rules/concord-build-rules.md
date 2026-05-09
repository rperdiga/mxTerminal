# Concord Build Rules

> **Don't guess. Don't fake. Don't break.**

Always-loaded for any session driving this Mendix project via Concord. These rules govern *how* you work, not *what* to build.

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

## 2. Pages always go through Maia, never `ped_*`

Doctrine, not preference. The Maia system prompt published with Studio Pro 11.10+ says (paraphrased): use `pg_*` tools exclusively for all page reading, creation, and modification — never use PED for pages.

The `pg_*` tools live inside Maia and are not exposed to your tool surface from inside Concord. To reach them, drive Maia via the Concord bridge.

**Read `.claude/skills/mendix-page-gen/SKILL.md` first.** It carries the canonical JSON template, the widget catalog, and the verification recipe.

### Windows recipe (`mcp__concord-mcp__maia__ask`)

1. **Build the full minified `pg_write_page` JSON locally** — full layout call, all widgets, all actions, all bindings, in one document. Don't ask Maia to design the page; only to write it.
2. **Call `mcp__concord-mcp__maia__ask`** with this template:
   ```
   Use pg_write_page to create or update the page:
     module: <ModuleName>
     pageName: <PageName>
     content: <minified JSON>

   After writing, call ped_check_errors on the page document and on the
   module's DomainModels$DomainModel. Report any errors verbatim.
   ```
3. **Verify directly** — do not trust Maia's self-report. Call `mcp__mendix-studio-pro__ped_check_errors` with `documentType: Pages$Page documentName: <Module>.<Page>` and again on the domain model. Then `mcp__mendix-studio-pro__ped_read_document` to confirm the widgets you sent landed in the slot you sent them to.
4. **Refresh Studio Pro:** `mcp__concord-mcp__refresh_project`.
5. **On errors,** rebuild the JSON addressing the specific errors and repeat 2–4. **Cap: 2 retries per page** (matches §3's per-operation retry budget). After the second failure, surface the JSON, Maia's response, and the `ped_check_errors` output, and stop.

**Do not create page documents while Maia is unavailable.** If the warm-up ladder below cannot reach a working Maia, do not fall back to writing empty page shells via PED "to be filled in later." A page document with an empty widget tree is an orphan (§7 #1) and shipping one is failure (§7 #5). Stop at the warm-up ladder's escalation point and leave page work for after Maia is reachable.

### macOS recipe (no Maia bridge)

1. Build the same `pg_write_page` JSON locally.
2. **Print a copy-paste block** to the user with explicit instructions: "Open Maia in Studio Pro, paste this prompt, send, reply `done` when Maia finishes."
3. **Stop and wait** for the user's confirmation.
4. **Verify directly** — `ped_check_errors` and `ped_read_document` exactly as in steps 3–4 above. Do not trust the user's "done" alone.
5. **Refresh Studio Pro** as in step 4.

### When `maia__status` returns "Maia panel not visible" — recovery ladder

The bridge probe can return *"All Maia transports unavailable. Maia panel not visible."* even when the panel **is** open. The probe checks for a rendered chat-list DOM container; a freshly-opened-but-untouched panel may not have rendered it yet. Recovery, in order:

1. **Warm the panel.** Call `mcp__concord-mcp__maia__send` with `"ping"` (or any one-word string). Wait 2–3 seconds. This forces Maia to render the chat list.
2. **Re-probe** with `maia__status`. If it returns `idle` / `done` / a transport name, proceed.
3. **If still failing,** call `mcp__concord-mcp__maia__reset` to reinitialize bridge transports. Re-probe.
4. **If still failing,** stop and surface this exact instruction to the user: *"Click into the Maia panel in Studio Pro and type 'hi' (or any message). Reply when done."* Then re-probe on user confirmation.
5. **Only after step 4 fails** is "Maia unavailable" a real escalation. Surface the verbatim error from each step above and stop.

Do not skip ahead to "Maia is unavailable, I'll do everything else without it" — see §3 (one-shot bail forbidden) and §7 (no orphan pages).

### CustomWidget exception

Neither `ped_*` nor `pg_*` reliably constructs `CustomWidgets$CustomWidget`. When a page needs a custom widget: build the page shell *without* it (a placeholder `Pages$DivContainer`), tell the user to drag the widget into the placeholder in Studio Pro, then refresh and verify.

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

## 5. Studio Pro MCP discipline (`ped_*` rules)

Doctrine from the Maia system prompt — don't re-derive these:

- **`ped_find_document` before any create.** If matches found, read each; only create if zero results. Names are case-sensitive.
- **`ped_get_schema` before any create or add.** Schema cache is not stable across operations. Get fresh.
- **Use `$constructor` property names for create/add — not paths from read results.** Constructors flatten nested wrappers. Constructor sees `objects`; read sees `objectCollection.objects`. When *creating*, build to `objects`. When *updating* an existing element, the path is `/objectCollection/objects/N`.
- **Include `$Type`** for all `$constructor` and `$element` slots; **NEVER** include `$Type` inside `$object` value objects (parser confusion).
- **Never include `$ID` when creating.** Internal references use `$id(/path)` with 0-based indices.
- **DomainModel is special** — nameless, exactly one per module, never created. *Update* it with the module name as `documentName`.
- **`ped_check_errors`** runs after ALL mutations for the current task complete, not between intermediate steps.
- **Single-shot fix rule** (Maia doctrine, hard limit). After `ped_check_errors` reports errors you get *exactly one* `ped_update_document` to fix them. Re-run `ped_check_errors`. If errors remain → **STOP and REPORT.** Do not retry. Surface the verbatim errors and the fix attempt; the user decides next steps.

### Reserved words

Names rejected by Mendix (case-insensitive). Includes all Java keywords plus Mendix-specific identifiers. The full list from Mendix is `type`, `MendixObject`, `__filename__`, `changedby`, `changeddate`, `context`, `createddate`, `currentUser`, `empty`, `guid`, `id`, `object`, `owner`, `submetaobjectname`, `con`, plus the predefined-variable names `currentDeviceType`, `currentIndex`, `currentSession`, `latestError`, `latestSoapFault`, `latestHttpResponse`. Any Java keyword (`class`, `interface`, `enum`, etc.) is also reserved.

Even if the user explicitly asks for a reserved word, substitute. Standard pattern: `<EntityName><ReservedWord>` (e.g. `Customer.Type` → `Customer.CustomerType`).

---

## 6. Update operation rules

- **`set`** is for primitives only. Never on arrays, never on element-typed properties.
- **`add`** is for arrays only.
- **`remove`** takes array path + index.
- **Element-typed properties cannot be `set` after add.** Most common trap. Examples: `Pages$ActionButton.action`, `Pages$ActionButton.icon`, `Pages$DynamicImageViewer.entityRef`, `Pages$DynamicText.attributeRef`. Specify them in the same operation that adds the parent. To change later: `remove` the parent, `add` again with the new value.
- **Some primitive properties also have this trap.** Notably `Pages$DivContainer.name` — string-typed, but `set` after add fails. Treat the same: specify at add time.
- **Some widget types cannot be removed at all:** `Pages$DynamicText`, `Pages$ListView`, `Pages$LayoutGrid`, `Pages$DivContainer`, `Pages$ActionButton`. When a structural change is needed, rebuild the containing parent.
- **`Microflows$MicroflowCallAction` parameter mappings cannot be added or removed after the call action is created.** PED auto-fills empty stubs at creation. `set` the stub `argument` at `/.../parameterMappings/M/argument`; never `add` new mappings (creates duplicates that can't be removed).
- **Empty string `""` is not a valid expression.** For `dynamicClasses` and similar expression-typed string properties, omit the property entirely if you don't want a value — `""` fails the model checker.
- **Order matters in batch updates.** Referenced elements must be added before referencing elements. `$id(/path)` references for newly-added items don't always resolve across array boundaries within the same call — when in doubt, split into two calls and read the new IDs between.
- **After any mutation, re-read before the next mutation.** Removing an element shifts subsequent indices; the `mendix-microflow-update` skill has the canonical recipe.

---

## 7. Don't ship orphans, shells, or hollow microflows

Five named failure modes from forensic builds. Guard against each by name, every time.

**1. Orphan pages.** Every non-home page must have at least one navigation entry-point: a button click, a menu item, a list-item action, or another microflow's `ShowPageAction`. Wire navigation in the same iteration as page creation; don't defer "for later." **A page document with no widget tree (or only the default Atlas content placeholder) is also an orphan** — its existence in the model means nothing if it's not built out.

**Acknowledged-placeholder exception.** If you must create a page document as a *forward reference* — e.g., you're wiring a menu item to `Page_About` now and will fill its widgets in a later step of the same build — give it the minimum recognizable content via Maia (per §2): the layout call, a title widget with the page's intended name, and a `Pages$Container` with a brief descriptive line like *"This page is in progress. Coming soon: account settings, profile edit, sign out."* Studio Pro and the running app then show something meaningful instead of blank. This is a narrow exception — most pages should be built fully when created. If you use it, the page is a debt you owe; come back inside the same build and finish it before the verification gate (§12).

**2. Shell microflows.** Every microflow you create gets a body that does the thing it's named for. After creation, walk the runtime call path (button → microflow → side-effect) and confirm the side-effect happened (a record exists, a value updated, a page appears). A microflow with only `SequenceFlow` connectors and no Activities is empty, not minimal.

**3. ActionButton wiring trap.** `Pages$ActionButton.action` is element-typed (§6). Specify `action` at *add* time. To change it later: remove the whole button and re-add with the new action. Don't `set` on `action` after the fact — it fails silently.

**4. Letter-not-spirit compliance.** A request for "5 pages and 3 microflows" is satisfied when the user can walk the journey end-to-end in a browser, not when the count of objects matches. If the user can't browse → click → see-something-happen → see-it-persist → find-it-on-another-page, the build isn't done. Counts are not deliverables; behaviors are.

**5. End-of-build "manual steps required" sections.** Forbidden as a substitute for finishing the work. The Maia bridge and Studio Pro UI handoff pattern (§8) exist specifically to avoid leaving the user with homework. Genuinely-required UI handoffs (Mark-as-UI-resources, App Settings → Runtime → After-Startup, Navigation defaults) belong **inline in the build flow as soft-stops** — surface, wait for confirmation, resume — not stacked at the end as a punt-list. **Self-check before any end-of-build summary:** scan your draft for the words *"manual"*, *"requires"*, *"in Studio Pro"*, *"after this"*, *"~5 minutes"*. Each match is a soft-stop you skipped. Go back and resolve them inline.

When the user describes an app to clone or build, derive the **journey arc** they're really asking for — *Browse → Detail → Action → Side-effect → User-facing list/evidence* — and deliver every step. Specify the arc explicitly in your plan (§13) so the user can check it. Renames mid-build cause downstream churn (and module name is immutable per §9), so commit to entity / page / microflow names in the plan, not in the writes.

---

## 8. Studio Pro UI handoffs — soft-stop pattern, never punt-list

A class of Mendix doc types and configuration is **PED-unreachable** — you cannot create or modify them through `mcp__mendix-studio-pro__*` tools. Most are reachable through Maia (`mcp__concord-mcp__maia__ask`); a few require the user to click in Studio Pro themselves. Either way, when one of these comes up in your build, **handle it inline as a soft-stop, not as a punt-list at the end.**

### The soft-stop pattern

1. Detect the handoff need at the natural point in the build (e.g., before adding pages that need a layout, you discover the layout doesn't exist; before configuring login, you discover the Navigation document needs editing).
2. Try Maia first — `maia__ask` with a clear natural-language request to do the work in Studio Pro on your behalf. Maia, being inside Studio Pro, has access to creation UIs you don't.
3. If Maia can't or won't, surface a single clear instruction to the user. Wait for confirmation. Resume immediately on confirmation.
4. **Verify the result via PED reads after resumption.** Don't trust user/Maia self-reports.
5. **Never bail to a punt-list.** This is a one-step gate, not an escalation point.

### The handoff catalog

| Doc type / setting | What it controls | Path to handle |
|---|---|---|
| **`Pages$Layout`** | Custom top bar, sidebar, footer chrome | Maia: *"Create a new layout `<Module>.<LayoutName>` based on `Atlas_Core.Atlas_TopBar` with these chrome components: ..."*. Manual fallback: App Explorer → right-click module → Add layout (or duplicate Atlas layout, then customize). PED can verify existence (`ped_find_document`) and let pages reference it via `Pages$LayoutCall`, but cannot author its widgets. See §10. |
| **`Navigation$NavigationDocument`** | App menu items, default home page per role, app title, app icon, favicon, login page, PWA settings | Maia or manual: *App ▸ Navigation*. Set the menu items the build needs, the default home page (e.g. `<Module>.Page_Home`), and the app title. App icon / favicon: open the **Web** profile → set Title-bar icon (16/32 px) and Application icon (≥256 px). Mendix regenerates `icon-16.png`, `icon-32.png`, `favicon.ico`, `apple-touch-icon.png`, manifest icons at deploy time. |
| **After-Startup microflow** | Runs automatically when the app boots (e.g. seed-data) | App ▸ Settings ▸ Runtime tab ▸ After Startup → `<Module>.<MicroflowName>`. Manual UI step. |
| **Mark-as-UI-resources** | Marks a module as a theme module so its SCSS compiles in the right load order | Right-click module → Mark as UI resources module. Icon turns green. Manual UI step (`markAsUIResource` is not on the MCP surface). See §11. |
| **`Menus$MenuDocument`** | Side-menu documents distinct from the project Navigation menu | Maia or manual; PED can read schemas but not author menu items. |
| **`JsonStructures$JsonStructure`, `ImportMappings$ImportMapping`** | REST/import-mapping schemas | Manual: hand the user the JSON snippet to paste into a JSON Structure, the entities the Import Mapping should produce, and the field-to-attribute table. Build the surrounding microflow (entities, calls, parsing-result handling) via PED. |
| **`Images$ImageCollection` / `Images$Image`** | Image / icon binary asset bundles | Manual upload through Studio Pro. PED cannot author binary assets. |
| **CustomWidget instances** | Marketplace widget instances on pages | Manual: build the page shell with a placeholder `Pages$DivContainer`, instruct user to drag the widget in, refresh + verify. |

### Reference-by-name still works

PED-unreachable types **can be referenced by qualified name** from PED-reachable docs once the user (or Maia) has created them. Examples that work: `Pages$Page.layoutCall.layout` accepts `"<Module>.<LayoutName>"`; `Microflows$ImportXmlAction.resultHandling.importMappingCall.mapping` accepts `"<Module>.MyImportMapping"`. Confirm the qualified name via `ped_find_document` before referencing — wrong-name references fail with `"Reference with qualified name X of type Y not found."`

---

## 9. New project = new module

Don't pollute `MyFirstModule`. For every new project or new feature area:

1. **Create a module:** `mcp__mendix-studio-pro__ped_create_module` with a name reflecting the app domain (PascalCase, alphanumeric and underscore only, no leading digit, no spaces). Plan the final name on first creation — module renames are a Studio Pro UI step (right-click → Rename), not an MCP operation, and `set /name` on `Projects$Module` is rejected verbatim.
2. **Constructor accepts only `{ name }`.** Marking a module as a UI resources module (for theme work, see §11) is a Studio Pro UI step — see §8.
3. **Place every entity, microflow, page, and view entity in this module.** Use `mcp__mendix-studio-pro__read_skill` `folder-structure` skill to learn each doc type's canonical home (`/Domain Model/`, `/Pages/<EntityFolder>/`, `/Microflows/<EntityFolder>/`). Module root is anti-pattern — `ped_list_folder` first to inspect existing structure, then derive the right `folderPath` before each create.
4. **Domain model is automatically created.** Don't try to `ped_create_document` it; use `ped_update_document` against the module's domain model with the module name as `documentName`.

---

## 10. Layout-first for branded apps

For any app with a brand identity that won't be served by default Atlas chrome — **build the layout BEFORE the pages, in your project module.** It is the chassis everything else rides on.

**Strong triggers — when any of these are present, layout-first is *required*, not optional:**

- The user provided a **reference URL** to clone or model after (*"build something like thecocktailproject.com,"* *"a clone of allrecipes.com"*).
- The user provided a **visual mockup, screenshot, or spec document.**
- The user provided **brand colors, a logo, or custom typography** upfront.
- The user said *"make it look like X,"* *"in the style of X,"* *"feels like a polished consumer app."*

Each of these is hard signal that default Atlas chrome will visibly undermine the deliverable. Build the layout first, the theme module second (§11), then build pages that ride on the layout. Don't reverse the order.

A `Pages$Layout` document holds the persistent chrome (top nav, sidebar, footer) and a content slot pages plug into via `Pages$LayoutCall`. Atlas's `Atlas_TopBar` is exactly this — a layout document in `Atlas_Core` that every default page uses. The right pattern for branded apps is:

1. **Create one layout per layout shape the app needs.** Typically two: a main chrome layout (top bar + content + footer) and a stripped layout (no nav — for age-gates, login, error pages, popups).
2. **Author the chrome inside the layout** — top bar with logo + nav menu, footer, sidebar if any, content slot (`Pages$Placeholder` widget) where pages plug in. This happens **once**, in one document.
3. **Every page in the app uses your layout** via `Pages$LayoutCall.layout: "<Module>.<LayoutName>"`. The page widget tree is then just the **content area** — page-specific widgets in the placeholder slot. No per-page header rebuilding.

### Why this matters

- The cocktailproject site (and most branded apps) has a single chrome shape across every page. Without a custom layout, you stamp the chrome onto every page individually — slow, inconsistent, error-prone.
- Pages-via-Maia (§2) is per-page. Building chrome once via a layout reduces total Maia traffic.
- Theme variables (§11) cascade into the layout's chrome the same way they cascade into pages — define the brand once.

### Path to create

`Pages$Layout` is **PED-unreachable** for both creation and widget editing (per §8). Two paths:

1. **Maia (preferred).** Use `maia__ask` to create the layout based on `Atlas_Core.Atlas_TopBar` and describe the chrome you want in natural language (logo placement, nav menu, footer shape). Maia drives layout creation inside Studio Pro.
2. **Manual fallback.** App Explorer → right-click module → Add layout → base on Atlas_TopBar → customize the chrome in the page editor. Use the soft-stop pattern (§8) — instruct, wait, resume.

After creation, PED can verify existence via `ped_find_document` and pages can reference the layout by qualified name.

### When to skip layout-first

Default Atlas layouts are fine for **CRUD admin tools, internal dashboards, prototype apps** where Atlas's stock chrome is good enough. The trigger for layout-first is "the app has a brand identity that Atlas-default-blue will undermine."

---

## 11. Custom theme = sibling theme module + Atlas pattern

When the user asks for custom styling, branding, or anything beyond default Atlas — **never edit `themesource/atlas_core/`** (Atlas updates from Marketplace overwrite your edits). Create a sibling theme module instead.

**Recipe:**

1. **Create a new module** (e.g. `<ProjectName>_Theme`). Use `ped_create_module`.
2. **Mark as UI resources module** in Studio Pro. Soft-stop: instruct the user to right-click the module → *Mark as UI resources module*, wait for confirmation. Without the flag, your module loads before Atlas Core and your overrides won't take effect — this is a hard one-click step, not an escalation point. Resume immediately on confirmation.
3. **Create the file layout** under `themesource/<modulename>/web/` mirroring the Atlas Web pattern:
   - `main.scss` — module entry point.
   - `custom-variables.scss` — brand variables (you create).
   - `design-properties.json` — optional widget design properties.
   
   Use `mcp__mendix-studio-pro__write_file` (the `/themes` domain is registered).
4. **Wire the import.** In `theme/web/custom-variables.scss` add only:
   ```scss
   @import "../../themesource/<modulename>/web/custom-variables.scss";
   ```
5. **Move (don't copy)** any pre-existing variables from `theme/web/custom-variables.scss` to your sibling module. The app-level file should now contain only the import line. Variables left at the app level override the sibling module's — this is the documented escape hatch for one-off overrides.
6. **Set load order.** App Settings → Theme tab → drag your sibling module **below** `Atlas_Core` (lower in list = higher precedence). Order is the override mechanism.
7. **Token-style. Don't hard-code.** Brand colors, fonts, radii, and spacing live as SCSS variables (`$brand-primary`, `$brand-accent`, `$font-family-base`, `$border-radius`, etc.) in your sibling module's `custom-variables.scss`. Don't drop hex codes or pixel values onto widgets via `class` properties.

**Studio Pro 11.10 defaults to Atlas 3 SASS.** Atlas 4 (CSS variables) is opt-in via `$use-css-variables: true;` at the top of `theme/web/custom-variables.scss` plus `:root { }` wrappers. Mixed Atlas-3-and-4 modules fall back to Atlas defaults — only opt in if all dependent modules support CSS variables.

**Pipeline + export.** Studio Pro watches `theme/` and `themesource/` and recompiles in-process — no `npm` or `mxbuild`; `mcp__concord-mcp__refresh_project` forces a rebuild. Note for module export: Mendix `.mpk` packaging excludes `theme/`, `themesource/<module>/`, `jsactions/`, and `javasource/` — they have to travel separately if re-imported elsewhere.

---

## 12. Verification — three-part gate

Before claiming any feature done:

1. **`mcp__mendix-studio-pro__ped_check_errors`** returns no errors on every document touched.
2. **The runtime reflects the change.** `mcp__concord-mcp__save_all` → `refresh_project` → `stop_app` → `run_app` → poll `get_app_status` until `running`. Skipping the cycle means verifying the previous version.
3. **The user-visible behavior works end-to-end** — walk the full journey arc *Browse → Detail → Action → Side-effect → User-facing list*. Click chains on a single page miss orphan-page and shell-microflow failures. Every step in the arc must produce its expected outcome.

Self-reports of "verified," "working," "live," "done" are claims, not evidence. Evidence is screenshots from a click chain landing on the expected destination, DOM assertions against `.mx-name-*` selectors, and the verbatim `ped_check_errors` output.

If a Playwright MCP is attached in this environment (look for `mcp__playwright__*` tools in your tool surface), use it for the end-to-end walk and capture screenshots at each step. Without Playwright, the verification reduces to "fewer-than-end-to-end" — note that explicitly when reporting.

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
- Anything that would belong in `concord-build-rules.md` itself — that's Concord-managed and overwritten on every Save. **Never modify that file directly; your edits will be lost.**

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
