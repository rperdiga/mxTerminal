---
name: mendix-page-gen
description: Use when creating or modifying a Mendix page on Studio Pro 10.24.13–11.9.x. Documents generate_overview_pages for scaffolding, list_pages and read_page_details for inspection, and exclude_document / delete_document for cleanup. Includes layout-first design doctrine and navigation graph thinking. Trigger when the user asks to create a new page, scaffold an overview, list existing pages, or wire navigation.
---

## Tools in this environment

This skill is for **Studio Pro 10.24.13–11.9.x** (concord-mcp tool surface). The Mendix studio-pro MCP server, Maia, and the Maia-fronted page-write surface are **not available** on this version.

Page tools available on 10.x:

- `mcp__concord-mcp__generate_overview_pages` — scaffold overview and detail pages for one or more entities. This is the primary automated page authoring tool on this version.
- `mcp__concord-mcp__list_pages` — list all pages in the project or a module.
- `mcp__concord-mcp__read_page_details` — read the structure of an existing page.
- `mcp__concord-mcp__exclude_document` — exclude a page from the deployment package without deleting it.
- `mcp__concord-mcp__delete_document` — permanently delete a page document.
- `mcp__concord-mcp__manage_navigation` — wire pages into the app navigation menu.
- `mcp__concord-mcp__check_project_errors` — validate project consistency after changes.
- `mcp__concord-mcp__refresh_project` — prompt Studio Pro to reload the project state.

**Scope of automated page authoring on 10.x:** The agent's automated page authoring surface is limited to `generate_overview_pages`. For richer pages on 10.x, the agent's role is limited to scaffolding and wiring. Custom widget composition, complex layouts, and DataView nesting belong in Studio Pro's native page editor.

---

# Page Generation on 10.x

## Primary Tool: generate_overview_pages

`mcp__concord-mcp__generate_overview_pages` scaffolds standard overview + detail page pairs for one or more entities. It is the recommended starting point for any new page work on 10.x.

**When to use it:**

- The user asks to generate pages for an entity or a set of entities.
- The user wants a standard CRUD interface (list + new/edit form) without heavy customization.
- As a first step before the user customizes in Studio Pro.

**What it produces:**

- An overview page (data grid listing all objects) for each requested entity.
- A new/edit detail page for each requested entity.
- Standard Atlas styling and layout — no custom widget composition.

**After calling generate_overview_pages:**

1. Call `mcp__concord-mcp__check_project_errors` to confirm no consistency errors.
2. Call `mcp__concord-mcp__list_pages` to confirm the expected pages are present.
3. Call `mcp__concord-mcp__refresh_project` so Studio Pro reflects the new pages.
4. If navigation wiring is needed, call `mcp__concord-mcp__manage_navigation`.

**Limitations:**

- Does not support custom widgets, complex DataView nesting, or widget-level property customization.
- Does not support snippet composition or non-standard layouts.
- For anything beyond standard CRUD scaffolding, tell the user: "I created the page scaffolding. Please customize the layout and widget composition in Studio Pro's native page editor."

---

## Inspecting Existing Pages

**List pages:**

```
mcp__concord-mcp__list_pages
  module: <ModuleName>   (optional — omit for all modules)
```

Returns a list of page documents in the specified module (or all modules if omitted). Use this to confirm generated pages exist and to find pages before reading their structure.

**Read page details:**

```
mcp__concord-mcp__read_page_details
  module: <ModuleName>
  pageName: <PageName>
```

Returns the structural details of a page. Use this to understand an existing page before suggesting modifications to the user, or to verify that a generated page has the expected layout.

---

## Removing Pages

**Exclude a page** (keeps it in the model but removes it from the deployment package):

```
mcp__concord-mcp__exclude_document
  documentType: Pages$Page
  documentName: <ModuleName>.<PageName>
```

Use when the user wants to hide a page without permanently deleting it.

**Delete a page** (permanent, irreversible):

```
mcp__concord-mcp__delete_document
  documentType: Pages$Page
  documentName: <ModuleName>.<PageName>
```

Always confirm with the user before calling `delete_document` — the operation cannot be undone via the agent. After deletion, call `mcp__concord-mcp__check_project_errors` to confirm no dangling references remain.

---

## Navigation Wiring

After generating pages, wire them into the app navigation:

```
mcp__concord-mcp__manage_navigation
  (see concord-mcp tool schema for parameter details)
```

Navigation changes take effect after `mcp__concord-mcp__refresh_project`.

---

# Page Design Doctrine (Mendix Invariants)

These principles apply regardless of version. They guide the agent's recommendations even when automated implementation is not possible.

## Layout-First

Always identify the page layout before designing widget composition:

- `Atlas_Core.Atlas_Default` — standard full-page layout.
- `Atlas_Core.PopupLayout` — modal popup / dialog.

Choose the layout before deciding on widget types. A popup layout has different content slots from a full-page layout.

## Navigation Graph Thinking

Before generating pages, map the navigation flow:

- What is the entry point for this feature? (menu item, button, or link)
- What object context is passed between pages? (page parameter)
- What are the exit paths? (back navigation, save, cancel)

When a page requires a context object (e.g., a Customer detail page), it must declare a page parameter of the correct entity type. `generate_overview_pages` handles this automatically for standard CRUD pages.

## Widget Naming

- Widget names must be unique within a page.
- Use camelCase: `layoutGrid1`, `dataGrid1`, `saveButton`.
- Names appear in listening targets — if a DataView listens to `dataGrid1`, a grid with exactly that name must exist on the same page.

## Standard Page Patterns

**Overview page (list):**
- Shows all objects in a data grid.
- Has a New button that opens the detail page with `CreateObjectClientAction`.
- Row click or Edit button opens the detail page for the selected object.

**Detail page (new/edit form):**
- Receives an entity object as a page parameter.
- Uses a DataView bound to the parameter for editing attributes.
- Has Save and Cancel buttons.

Both patterns are produced automatically by `generate_overview_pages`. For non-standard patterns, instruct the user to build in Studio Pro.

## Critical Constraints

⚠️ **`generate_overview_pages` is the only automated page authoring tool on 10.x** — do not attempt to simulate the 11.x page-write tool that requires Maia by constructing raw page JSON.
⚠️ **Always check project errors after page operations** — dangling references cause runtime failures.
⚠️ **Always confirm before delete_document** — page deletion is irreversible via the agent.
⚠️ **Refresh Studio Pro after changes** — the IDE does not auto-reload on agent writes.
