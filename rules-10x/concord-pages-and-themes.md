# Concord Build Rules — Pages, Layouts, and Themes (Studio Pro 10.x – 11.9.x)

> **Don't guess. Don't fake. Don't break.**

Companion file to `concord-build-rules.md` (core operational discipline) and `concord-model-discipline.md` (model rules + reserved words + orphan-prevention). All three load together as Concord's always-loaded ruleset for Studio Pro 10.24.13–11.9.x; section numbers (§1–§15) are globally unique across the three files, so cross-references resolve regardless of which file they're cited from.

This file owns the rules for **everything you can see in the rendered app** — page construction via concord-mcp tools, custom layouts for branded apps, the sibling-theme-module pattern for SCSS authored via direct filesystem, and the soft-stop pattern for Studio Pro UI handoffs you can't drive through the tool surface.

---

## 2. Pages via concord-mcp

On Studio Pro 10.x, Maia's `pg_*` page-write surface is not available. Page construction goes through concord-mcp tools and — for richer page work — Studio Pro UI handoffs.

**Available page tools on 10.x:**

- `mcp__concord-mcp__generate_overview_pages` — scaffolds list/detail overview pages for one or more entities. This is the primary automated page-generation tool on 10.x.
- `mcp__concord-mcp__list_pages` — enumerate existing pages.
- `mcp__concord-mcp__read_page_details` — read a page's current state.
- `mcp__concord-mcp__exclude_document` — exclude a document from the project build.
- `mcp__concord-mcp__delete_document` — remove a document entirely.

**Read `.claude/skills/mendix-page-gen/SKILL.md` first.** It carries the canonical widget catalog and verification recipe, even where the write path differs on 10.x.

### What concord-mcp page tools can do on 10.x

1. **Scaffold list + detail pages** for an entity via `mcp__concord-mcp__generate_overview_pages`. This is the right starting point for standard CRUD-style views.
2. **Wire navigation** from generated pages into the app menu via `mcp__concord-mcp__manage_navigation`.
3. **Read page structure** via `mcp__concord-mcp__read_page_details` to verify what landed.
4. **Delete or exclude** pages that shouldn't ship via `delete_document` / `exclude_document`.

### Limitations on 10.x — and how to work around them

For richer pages on 10.x, use Studio Pro's native page editor. Concord can wire up navigation and microflows and scaffold list/detail pages, but is not the right tool for custom widget composition on this version. The Maia bridge and its page-write capability are absent on 10.x; complex page layouts, branded widget trees, and custom container structures require a Studio Pro UI handoff (see §8).

**Pattern for complex pages on 10.x:**

1. Use `mcp__concord-mcp__generate_overview_pages` to scaffold the base structure.
2. Hand off to the user for widget-level customization inside the Studio Pro page editor (soft-stop pattern — §8).
3. After the user confirms the page is shaped correctly, verify via `mcp__concord-mcp__read_page_details` and `mcp__concord-mcp__check_model`.
4. Continue wiring microflows and navigation from your tool surface.

**Do not create empty page shells as placeholders** and leave them unwired. A page document with no widget tree is an orphan (§7 #1). If you must forward-reference a page, give it the minimum scaffold via `generate_overview_pages` (even a single-entity overview is better than blank) and come back to it before the verification gate (§12).

### Verification after any page operation

1. `mcp__concord-mcp__read_page_details` — confirm structure.
2. `mcp__concord-mcp__check_model` — confirm no errors on the page document or the domain model.
3. `mcp__concord-mcp__refresh_project` — sync Studio Pro's in-memory state.

---

## 8. Studio Pro UI handoffs — soft-stop pattern, never punt-list

A class of Mendix doc types and configuration is **tool-unreachable on 10.x** — you cannot create or modify them through `mcp__concord-mcp__*` tools. When one of these comes up in your build, **handle it inline as a soft-stop, not as a punt-list at the end.**

### The soft-stop pattern

1. Detect the handoff need at the natural point in the build (e.g., before adding pages that need a layout, you discover the layout doesn't exist; before configuring login, you discover the Navigation document needs editing).
2. Surface a single clear instruction to the user. Be specific: module name, what to right-click, what to select, what value to enter.
3. Wait for confirmation. Resume immediately on confirmation.
4. **Verify the result via read-back.** Use `mcp__concord-mcp__list_pages`, `mcp__concord-mcp__list_modules`, or `mcp__concord-mcp__query_model_elements` to confirm the expected artifact exists before continuing.
5. **Never bail to a punt-list.** This is a one-step gate, not an escalation point.

### The handoff catalog (10.x)

| Doc type / setting | What it controls | Path to handle |
|---|---|---|
| **`Pages$Layout`** | Custom top bar, sidebar, footer chrome | Manual: App Explorer → right-click module → Add layout (or duplicate an Atlas layout, then customize). PED / concord-mcp can verify existence via `query_model_elements` and pages can reference the layout by name, but cannot author its widgets. See §10. |
| **`Navigation$NavigationDocument`** | App menu items, default home page per role, app title, app icon, login page | Manual: *App ▸ Navigation*. Set the menu items and default home page. App icon / favicon: open the **Web** profile → set Title-bar icon and Application icon. Where `mcp__concord-mcp__manage_navigation` does not cover the needed operation, use this manual path. |
| **After-Startup microflow** | Runs automatically when the app boots (e.g. seed-data) | *App ▸ Settings ▸ Runtime tab ▸ After Startup*. Manual UI step. |
| **Mark-as-UI-resources** | Marks a module as a theme module so its SCSS compiles in the right load order | Right-click module → *Mark as UI resources module*. Icon turns green. Manual UI step — not on the MCP surface. See §11. |
| **`Menus$MenuDocument`** | Side-menu documents distinct from the project Navigation menu | Manual; concord-mcp can read schemas but not author menu items. |
| **`JsonStructures$JsonStructure`, `ImportMappings$ImportMapping`** | REST/import-mapping schemas | Manual: hand the user the JSON snippet to paste into a JSON Structure, the entities the Import Mapping should produce, and the field-to-attribute table. Build the surrounding microflow via concord-mcp. |
| **`Images$ImageCollection` / `Images$Image`** | Image / icon binary asset bundles | Manual upload through Studio Pro. Cannot author binary assets. |
| **Page widget tree (complex layouts)** | Non-scaffold widget composition, branded pages | Manual: Studio Pro page editor. See §2 above. |

### Reference-by-name still works

Tool-unreachable types **can be referenced by qualified name** from tool-reachable docs once the user has created them. Confirm the qualified name via `mcp__concord-mcp__query_model_elements` or `mcp__concord-mcp__list_pages` before referencing — wrong-name references fail with a model error.

### Soft-stops you can engineer around — seed data via self-service button

Some "manual UI step" handoffs can be eliminated by designing the app to do the work itself. **The canonical example is seed data.** When a gallery page needs sample records to be navigable, the obvious-but-bad path is *App ▸ Settings ▸ Runtime ▸ After-Startup* — a soft-stop the user has to click through. The right path: build the seed flow as a self-service button visible inside the running app.

**Pattern:**

1. **Add a singleton flag entity.** Create `ProjectManage` with one Boolean attribute `NeedProjectSeedData` (default `true`). A microflow `IVU_ProjectManage_GetOrCreate` returns the one record, creating it on first call.
2. **Build the seed microflow.** `SUB_<Entity>_SeedIfEmpty` — guard at top: if entity count > 0, return; otherwise create N records. Last activity sets `ProjectManage.NeedProjectSeedData = false` on the singleton record and commits.
3. **Wire a button on the home page.** A "Seed sample data" button whose `visible` expression is bound to `ProjectManage.NeedProjectSeedData`. After the run, the flag flips false and the button vanishes.
4. **Click it via Playwright in the verification gate (§12).** After `run_app` reports `running`, drive a Playwright click against the button, then proceed with the full journey-arc walk.

**When to skip this pattern:** seed data that MUST run on app startup (auth bootstrap, schema migrations) belongs in After-Startup. The self-service-button pattern is for sample-data-for-demos.

---

## 10. Layout-first for branded apps

For any app with a brand identity that won't be served by default Atlas chrome — **build the layout BEFORE the pages, in your project module.** It is the chassis everything else rides on.

**Strong triggers — when any of these are present, layout-first is *required*, not optional:**

- The user provided a **reference URL** to clone or model after.
- The user provided a **visual mockup, screenshot, or spec document.**
- The user provided **brand colors, a logo, or custom typography** upfront.
- The user said *"make it look like X,"* *"in the style of X,"* *"feels like a polished consumer app."*

Each of these is hard signal that default Atlas chrome will visibly undermine the deliverable.

A `Pages$Layout` document holds the persistent chrome (top nav, sidebar, footer) and a content slot pages plug into. Atlas's `Atlas_TopBar` is exactly this — a layout document in `Atlas_Core` that every default page uses. The right pattern for branded apps:

1. **Create one layout per layout shape the app needs.** Typically two: a main chrome layout (top bar + content + footer) and a stripped layout (no nav — for login, error pages, popups).
2. **Author the chrome inside the layout** via the Studio Pro page editor (§8 handoff) — top bar with logo + nav menu, footer, sidebar if any, content slot where pages plug in. This happens **once**, in one document.
3. **Every page in the app references your layout** by qualified name. The page widget tree is then just the **content area** — page-specific widgets in the placeholder slot. No per-page header rebuilding.

### Path to create on 10.x

`Pages$Layout` is not writable via concord-mcp tools on 10.x. Use the soft-stop pattern (§8):

1. Instruct the user to create the layout via App Explorer → right-click module → Add layout → base on `Atlas_Core.Atlas_TopBar`.
2. Wait for explicit user confirmation.
3. Verify existence via `mcp__concord-mcp__query_model_elements` before building any pages that reference it.
4. Pages can then reference the layout by qualified name (`<Module>.<LayoutName>`).

### When to skip layout-first

Default Atlas layouts are fine for **CRUD admin tools, internal dashboards, prototype apps** where Atlas's stock chrome is good enough. The trigger for layout-first is "the app has a brand identity that Atlas-default-blue will undermine."

---

## 11. Custom theme = sibling theme module + Atlas pattern

When the user asks for custom styling, branding, or anything beyond default Atlas — **never edit `themesource/atlas_core/`** (Atlas updates from Marketplace overwrite your edits). Create a sibling theme module instead.

**On 10.x, all SCSS file writes use direct filesystem tooling (Bash / PowerShell / Read / Write / Edit).** There is no Studio Pro MCP file domain on 10.x. The theme paths live under the project root:

```
<project-root>/
  theme/
    web/
      custom-variables.scss      ← add your @import line here
  themesource/
    <YourModuleName>/
      web/
        main.scss                ← module entry point
        custom-variables.scss    ← brand variables
        design-properties.json  ← optional widget design properties
```

**Recipe (order matters — do not skip ahead):**

1. **Create a new module** (e.g. `<ProjectName>_Theme`). Use `mcp__concord-mcp__create_module`.
2. **Mark as UI resources module** in Studio Pro — **before any SCSS is written.** Soft-stop: instruct the user to right-click the module → *Mark as UI resources module*, wait for explicit confirmation. Without the flag, your module loads before Atlas Core and your overrides won't take effect.

   > **Guard: do not write any SCSS file (step 4 onwards) until the user has confirmed the module is marked as UI resources.** The flag determines load order; SCSS written into an unmarked module compiles in the wrong cascade and your overrides won't apply.

3. **Verify the flag landed** before proceeding. The module's icon turns green in App Explorer. Have the user confirm visually. If the user reports difficulty marking the module, stay parked here — do not start writing files.
4. **Create the file layout** under `themesource/<modulename>/web/` using direct filesystem writes (Write / Edit / Bash):
   - `main.scss` — module entry point (import your custom-variables and any partials).
   - `custom-variables.scss` — brand variables.
   - `design-properties.json` — optional widget design properties.
5. **Wire the import.** In `theme/web/custom-variables.scss` add only:
   ```scss
   @import "../../themesource/<modulename>/web/custom-variables.scss";
   ```
6. **Move (don't copy)** any pre-existing variables from `theme/web/custom-variables.scss` to your sibling module. The app-level file should now contain only the import line. Variables left at the app level override the sibling module's — this is the documented escape hatch for one-off overrides.
7. **Set load order.** App Settings → Theme tab → drag your sibling module **below** `Atlas_Core` (lower in list = higher precedence). This is a Studio Pro UI step (soft-stop — §8).
8. **Token-style. Don't hard-code.** Brand colors, fonts, radii, and spacing live as SCSS variables (`$brand-primary`, `$brand-accent`, `$font-family-base`, `$border-radius`, etc.) in your sibling module's `custom-variables.scss`. Don't drop hex codes or pixel values onto widgets via `class` properties.

**Studio Pro 10.x uses Atlas 3 SASS by default.** Mixed Atlas-3-and-4 modules fall back to Atlas defaults — do not opt into `$use-css-variables: true` unless all dependent modules explicitly support CSS variables.

**Pipeline + export.** Studio Pro watches `theme/` and `themesource/` and recompiles in-process — no `npm` or `mxbuild` needed. `mcp__concord-mcp__refresh_project` forces a rebuild. Note for module export: Mendix `.mpk` packaging excludes `theme/`, `themesource/<module>/`, `jsactions/`, and `javasource/` — they travel separately if re-imported elsewhere.
