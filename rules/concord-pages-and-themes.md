# Concord Build Rules — Pages, Layouts, and Themes

> **Don't guess. Don't fake. Don't break.**

Companion file to `concord-build-rules.md` (core operational discipline) and `concord-model-discipline.md` (PED rules + reserved words + orphan-prevention). All three load together as Concord's always-loaded ruleset; section numbers (§1–§15) are globally unique across the three files, so cross-references resolve regardless of which file they're cited from.

This file owns the rules for **everything you can see in the rendered app** — page construction via Maia, custom layouts for branded apps, the sibling-theme-module pattern for SCSS, and the soft-stop pattern for Studio Pro UI handoffs you can't drive through the MCP tool surface.

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

### When any Maia bridge call returns a non-success response — recovery ladder

The bridge probe and the bridge itself fail in several distinct ways, and the same recovery applies to all of them. **Trigger this ladder on any non-success response from `maia__status`, `maia__wait`, `maia__send`, `maia__ask`, or `maia__reset`** — including but not limited to:

- *"All Maia transports unavailable. Maia panel not visible."* (the classic — DOM container not yet rendered, even when the panel **is** open)
- *"Unknown handle: <name>"* (handle map dropped — the bridge lost the named handle you asked it to wait on)
- *"poll() returned unexpected shape: ..."* (response parser failure — common during layout-edit operations, see §10)
- *"IOException: Unable to write data ..."* (CDP connection drop — bridge lost its socket to the Studio Pro CEF instance)
- Any other transport / connection / shape error returned by the bridge

Recovery, in order:

1. **Warm the panel.** Call `mcp__concord-mcp__maia__send` with `"ping"` (or any one-word string). Wait 2–3 seconds. This forces Maia to render the chat list and re-establishes a fresh handle.
2. **Re-probe** with `maia__status`. If it returns `idle` / `done` / a transport name, proceed.
3. **If still failing,** call `mcp__concord-mcp__maia__reset` to reinitialize bridge transports. Re-probe.
4. **If still failing,** stop and surface this exact instruction to the user: *"Click into the Maia panel in Studio Pro and type 'hi' (or any message). Reply when done."* Then re-probe on user confirmation.
5. **Only after step 4 fails** is "Maia unavailable" a real escalation. Surface the verbatim error from each step above and stop.

Do not skip ahead to "Maia is unavailable, I'll do everything else without it" — see §3 (one-shot bail forbidden) and §7 (no orphan pages). And do not loop on the ladder itself — see §3's hard cap on Maia bridge calls.

### CustomWidget exception

Neither `ped_*` nor `pg_*` reliably constructs `CustomWidgets$CustomWidget`. When a page needs a custom widget: build the page shell *without* it (a placeholder `Pages$DivContainer`), tell the user to drag the widget into the placeholder in Studio Pro, then refresh and verify.

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

### Soft-stops you can engineer around — seed data via self-service button

Some "manual UI step" handoffs can be eliminated by designing the app to do the work itself, instead of asking the user to click into Studio Pro. **The canonical example is seed data.** When a gallery page needs sample records to be navigable, the obvious-but-bad path is *App ▸ Settings ▸ Runtime ▸ After-Startup → `<Module>.SUB_SeedIfEmpty`* — a soft-stop the user has to click through. The right path: build the seed flow as a self-service button visible inside the running app. The agent (via Playwright in §12) clicks it autonomously after `run_app`; the button hides itself once seed-data has landed; the demo state stays clean.

**Pattern (validated in production, CocktailDemo33 2026-05-10):**

1. **Add a singleton flag entity.** Create `ProjectManage` (or `<App>Manage`) with one Boolean attribute `NeedProjectSeedData` (default `true`). Singleton-style: a microflow `IVU_ProjectManage_GetOrCreate` returns the one record, creating it on first call.
2. **Build the seed microflow normally.** `SUB_<Entity>_SeedIfEmpty` — guard at top: *if entity count > 0 then return*; otherwise create N records. **Last activity sets `ProjectManage.NeedProjectSeedData = false`** on the singleton record (and commits). Naming follows §9 (SUB- prefix for sub-microflows).
3. **Wire the button on the home page.** Add a "Seed sample data" `Pages$ActionButton` whose `visible` expression is bound to `ProjectManage.NeedProjectSeedData`. `onClick` calls the seed microflow. After the run, the flag flips false → the button vanishes.
4. **Click it via Playwright in the verification gate (§12).** After `run_app` reports `running`, drive a Playwright click against the button by `.mx-name-actionButton<N>` selector or accessible-name "Seed sample data". Then proceed with the full journey-arc walk — the gallery now has data.

**Why this beats After-Startup wiring:** the After-Startup runtime setting is in `App ▸ Settings ▸ Runtime`, which is **PED-unreachable** and Maia-handle-only — every clone build that needs seed data hits this soft-stop. The self-service button avoids the soft-stop entirely AND demonstrates the seed flow more visibly to whoever's watching the demo. Saves ~1 click per build × every build.

**When to skip this pattern:** seed data that MUST run on app startup (auth bootstrap, license activation, schema migrations) belongs in After-Startup. The self-service-button pattern is for sample-data-for-demos, not for production prerequisites.

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

**Empirical note (2026-05-09 cocktail test): layout-edit operations specifically appear to destabilize the Maia bridge.** Both observed `poll() returned unexpected shape` errors fired during layout-duplication / layout-edit attempts — page widget edits via Maia in the same session remained reliable. If the bridge errors during layout work, **do not retry through the §2 ladder** — surface to the user with manual Studio Pro instructions (App Explorer → right-click module → Add layout → base on `Atlas_Core.Atlas_TopBar` → customize chrome in the page editor), wait for confirmation, then resume. Treat layout-edit-via-Maia as opportunistic rather than the default; the manual path is the safe default for layout authoring until the bridge stabilizes here.

After creation, PED can verify existence via `ped_find_document` and pages can reference the layout by qualified name.

### When to skip layout-first

Default Atlas layouts are fine for **CRUD admin tools, internal dashboards, prototype apps** where Atlas's stock chrome is good enough. The trigger for layout-first is "the app has a brand identity that Atlas-default-blue will undermine."

**Plan the seed-data flow during the layout pass.** Branded clone builds almost always need a populated gallery for the journey-arc walk to feel real. Use §8's self-service-button pattern (`ProjectManage.NeedProjectSeedData` singleton + visibility-bound button on the home page) so the seed step lives inside the app rather than as an After-Startup soft-stop. Cheaper, faster, and visible in the demo.

---

## 11. Custom theme = sibling theme module + Atlas pattern

When the user asks for custom styling, branding, or anything beyond default Atlas — **never edit `themesource/atlas_core/`** (Atlas updates from Marketplace overwrite your edits). Create a sibling theme module instead.

**Recipe (order matters — do not skip ahead):**

1. **Create a new module** (e.g. `<ProjectName>_Theme`). Use `ped_create_module`.
2. **Mark as UI resources module** in Studio Pro — **before any SCSS is written.** Soft-stop: instruct the user to right-click the module → *Mark as UI resources module*, wait for explicit confirmation. Without the flag, your module loads before Atlas Core and your overrides won't take effect. This is a hard one-click step, not an escalation point. Resume immediately on confirmation.

   > **Guard: do not write any SCSS file (step 4 onwards) until the user has confirmed the module is marked as UI resources.** The flag determines load order; SCSS written into an unmarked module compiles in the wrong cascade and your overrides won't apply. Writing the partials first and asking the user to mark the module after is a known failure mode (2026-05-09 cocktail test) — by the time the soft-stop fires, you've already shipped a misconfigured cascade.

3. **Verify the flag landed** before proceeding. The module's icon turns green in App Explorer; if you can drive a Studio Pro screenshot or have the user confirm visually, do it. If the user reports difficulty marking the module, stay parked here — do not start writing files.
4. **Create the file layout** under `themesource/<modulename>/web/` mirroring the Atlas Web pattern:
   - `main.scss` — module entry point.
   - `custom-variables.scss` — brand variables (you create).
   - `design-properties.json` — optional widget design properties.
   
   Use `mcp__mendix-studio-pro__write_file` (the `/themes` domain is registered).
5. **Wire the import.** In `theme/web/custom-variables.scss` add only:
   ```scss
   @import "../../themesource/<modulename>/web/custom-variables.scss";
   ```
6. **Move (don't copy)** any pre-existing variables from `theme/web/custom-variables.scss` to your sibling module. The app-level file should now contain only the import line. Variables left at the app level override the sibling module's — this is the documented escape hatch for one-off overrides.
7. **Set load order.** App Settings → Theme tab → drag your sibling module **below** `Atlas_Core` (lower in list = higher precedence). Order is the override mechanism.
8. **Token-style. Don't hard-code.** Brand colors, fonts, radii, and spacing live as SCSS variables (`$brand-primary`, `$brand-accent`, `$font-family-base`, `$border-radius`, etc.) in your sibling module's `custom-variables.scss`. Don't drop hex codes or pixel values onto widgets via `class` properties.

**Studio Pro 11.10 defaults to Atlas 3 SASS.** Atlas 4 (CSS variables) is opt-in via `$use-css-variables: true;` at the top of `theme/web/custom-variables.scss` plus `:root { }` wrappers. Mixed Atlas-3-and-4 modules fall back to Atlas defaults — only opt in if all dependent modules support CSS variables.

**Pipeline + export.** Studio Pro watches `theme/` and `themesource/` and recompiles in-process — no `npm` or `mxbuild`; `mcp__concord-mcp__refresh_project` forces a rebuild. Note for module export: Mendix `.mpk` packaging excludes `theme/`, `themesource/<module>/`, `jsactions/`, and `javasource/` — they have to travel separately if re-imported elsewhere.

**See also:** §8 *"Soft-stops you can engineer around — seed data via self-service button."* When the theme'd home page needs populated content for a journey-arc walk, add the seed button there during the page-build pass. The button rides on the layout you just authored; the visibility binding hides it after first run.
