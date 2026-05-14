# Concord Build Rules — Model Discipline (Studio Pro 10.x – 11.9.x)

> **Don't guess. Don't fake. Don't break.**

Companion file to `concord-build-rules.md` (core operational discipline) and `concord-pages-and-themes.md` (page construction + layouts + theme). All three load together as Concord's always-loaded ruleset for Studio Pro 10.24.13–11.9.x; section numbers (§1–§15) are globally unique across the three files, so cross-references resolve regardless of which file they're cited from.

This file owns the rules for **everything inside the model** — concord-mcp tool discipline (the create/update/read primitives for domain models, microflows, pages, and associations), update-operation traps (set-vs-add, element-typed properties, removal restrictions), the named failure modes that produce shipped-but-broken apps, and the new-project-new-module discipline that keeps `MyFirstModule` clean.

---

## 5. Concord MCP model discipline

Doctrine for `mcp__concord-mcp__*` model operations on 10.x — don't re-derive these:

- **Check for existing documents before any create.** Use `mcp__concord-mcp__list_modules`, `mcp__concord-mcp__list_microflows`, `mcp__concord-mcp__list_pages`, or `mcp__concord-mcp__query_model_elements` first. If matches found, read each; only create if zero results. Names are case-sensitive. Use `mcp__concord-mcp__validate_name` to confirm a name is safe before using it.
- **`mcp__concord-mcp__check_model` after any create or update.** Schema behavior on 10.x differs from what Mendix's own docs describe for 11.x — always check after writing, not just at end of build.
- **Constructor shapes may flatten nested wrappers.** When creating entities, microflows, or associations, build to the minimum shape the tool accepts. Read back after creation to see what actually landed before adding more properties. Build iteratively: create → read-back → update → read-back → check_model.
- **Include `$Type` equivalents where the tool schema requires them;** never add undocumented fields and expect them to be silently preserved — concord-mcp on 10.x may drop unrecognized fields.
- **Never write to `.mpr` directly.** All model writes go through `mcp__concord-mcp__*` tools.
- **`mcp__concord-mcp__check_model`** runs after ALL mutations for the current task complete, not between intermediate steps. Mid-batch checks surface transient errors (a flow with origin set but no destination yet) and cause unnecessary thrash.
- **Single-shot fix rule (hard limit).** After `check_model` reports errors you get *exactly one* update call to fix them. Re-run `check_model`. If errors remain → **STOP and REPORT.** Do not retry. Surface the verbatim errors and the fix attempt; the user decides next steps. There is no Maia fallback on 10.x.

### Constructor-flattening and silent-permissive vs. strict-extras behavior

This behavior pattern applies on 10.x just as it did on 11.x's PED engine:

- **Permissive types** (some page and navigation element types) accept an extras payload, return success, and discard fields not in the schema. You will not receive an error — the extras simply won't be there on read-back.
- **Strict types** (microflows, entities, associations) return a failure or schema validation error when extra fields are present. These fail loudly, which is better than silent drops.

**Defense:** after every create, use the read-back step (§4) to confirm the fields you care about actually landed. Do not trust success alone.

### Reserved words

Names rejected by Mendix (case-insensitive). Includes all Java keywords plus Mendix-specific identifiers: `type`, `MendixObject`, `__filename__`, `changedby`, `changeddate`, `context`, `createddate`, `currentUser`, `empty`, `guid`, `id`, `object`, `owner`, `submetaobjectname`, `con`, plus the predefined-variable names `currentDeviceType`, `currentIndex`, `currentSession`, `latestError`, `latestSoapFault`, `latestHttpResponse`. Any Java keyword (`class`, `interface`, `enum`, etc.) is also reserved.

Even if the user explicitly asks for a reserved word, substitute. Standard pattern: `<EntityName><ReservedWord>` (e.g. `Customer.Type` → `Customer.CustomerType`). Use `mcp__concord-mcp__validate_name` to check a proposed name before committing to it.

---

## 6. Update operation rules

These constraints apply to the concord-mcp model tools on 10.x, reflecting the same underlying PED engine behavior:

- **`set` is for primitives only.** Never on arrays, never on element-typed properties.
- **`add` is for arrays only.**
- **`remove` takes array path + index.**
- **Element-typed properties cannot be `set` after add.** Most common trap. If a property is a model element (an action, a widget reference, an entity reference), specify it in the same operation that adds the parent element. To change later: remove the parent, add again with the new value.
- **Some primitive properties also have this trap.** String-typed but behaviorally immutable after add. When in doubt: specify at add time.
- **Some widget types cannot be removed at all.** When a structural change is needed on a page, rebuild the containing parent rather than trying to remove individual children.
- **Microflow call parameter mappings cannot be added or removed after the call action is created.** The tool auto-fills empty stubs at creation. Set the stub `argument` value; never try to add new mappings (creates duplicates that can't be removed).
- **Empty string `""` is not a valid expression.** For expression-typed string properties, omit the property entirely if you don't want a value — `""` fails `check_model`.
- **Order matters in batch updates.** Referenced elements must be added before referencing elements. When in doubt, split into two calls and read the new IDs between.
- **After any mutation, re-read before the next mutation.** Removing an element shifts subsequent indices; the `mendix-microflow-update` skill has the canonical recipe.
- **Use `mcp__concord-mcp__update_association` to change multiplicity, delete behavior, or the owner side of an existing association.** Call `mcp__concord-mcp__query_associations` first to confirm the current state; call `mcp__concord-mcp__check_model` after.

---

## 7. Don't ship orphans, shells, or hollow microflows

Six named failure modes from forensic builds. Guard against each by name, every time.

**1. Orphan pages.** Every non-home page must have at least one navigation entry-point: a button click, a menu item, a list-item action, or another microflow's `ShowPageAction`. Wire navigation in the same iteration as page creation; don't defer "for later." Use `mcp__concord-mcp__manage_navigation` to register menu items. **A page document with no widget tree (or only the default Atlas content placeholder) is also an orphan** — its existence in the model means nothing if it's not built out.

**Acknowledged-placeholder exception.** If you must create a page document as a *forward reference* — e.g., you're wiring a menu item to `Page_About` now and will fill its widgets in a later step of the same build — give it the minimum recognizable content via the page tools available on 10.x (see §2): a title and a brief descriptive line like *"This page is in progress."* This is a narrow exception — most pages should be built fully when created. If you use it, the page is a debt you owe; come back inside the same build and finish it before the verification gate (§12).

**2. Shell microflows.** Every microflow you create gets a body that does the thing it's named for. After creation, walk the runtime call path (button → microflow → side-effect) and confirm the side-effect happened (a record exists, a value updated, a page appears). Use `mcp__concord-mcp__read_microflow_details` to verify a microflow has activities beyond empty connectors. A microflow with only connectors and no activities is empty, not minimal.

**3. ActionButton wiring trap.** Action-type properties on buttons and other clickable widgets are element-typed (§6). Specify the action at *add* time. To change it later: remove the whole button and re-add with the new action.

**4. Letter-not-spirit compliance.** A request for "5 pages and 3 microflows" is satisfied when the user can walk the journey end-to-end in a browser, not when the count of objects matches. If the user can't browse → click → see-something-happen → see-it-persist → find-it-on-another-page, the build isn't done. Counts are not deliverables; behaviors are.

**5. End-of-build "manual steps required" sections.** Forbidden as a substitute for finishing the work. Genuinely-required UI handoffs (Mark-as-UI-resources, App Settings → Runtime → After-Startup, Navigation defaults) belong **inline in the build flow as soft-stops** — surface, wait for confirmation, resume — not stacked at the end as a punt-list. **Self-check before any end-of-build summary:** scan your draft for the words *"manual"*, *"requires"*, *"in Studio Pro"*, *"after this"*. Each match is a soft-stop you skipped. Go back and resolve them inline.

**6. Read-loop anti-pattern.** If you find yourself calling the same read tool on the same document **3+ times without a write in between**, STOP. You are likely fighting a state-divergence problem. Reading more won't resolve it; each read returns the same view from the same source.

The fix:

1. Call `mcp__concord-mcp__save_all` to flush Studio Pro's in-memory state to disk.
2. Call `mcp__concord-mcp__refresh_project` to ensure subsequent reads see the reconciled state.
3. **Then** re-read.

If the read still doesn't match expectations after save → refresh → re-read, surface to the user with the verbatim read output and what you expected to see. Do not continue spinning on the same document.

When the user describes an app to clone or build, derive the **journey arc** they're really asking for — *Browse → Detail → Action → Side-effect → User-facing list/evidence* — and deliver every step. Specify the arc explicitly in your plan (§13) so the user can check it. Renames mid-build cause downstream churn (and module name is effectively immutable once references exist), so commit to entity / page / microflow names in the plan, not in the writes.

---

## 9. New project = new module

Don't pollute `MyFirstModule`. For every new project or new feature area:

1. **Create a module:** `mcp__concord-mcp__create_module` with a name reflecting the app domain (PascalCase, alphanumeric and underscore only, no leading digit, no spaces). Plan the final name on first creation — module renames via `mcp__concord-mcp__rename_module` are possible but costly if other documents already reference the module, and some references may not update automatically. Commit to the name in your plan (§13) before any writes.
2. **Constructor accepts only `{ name }`.** Marking a module as a UI resources module (for theme work, see §11) is a Studio Pro UI step — see §8.
3. **Place every entity, microflow, page, and view entity in this module.** Use `mcp__concord-mcp__manage_folders` to inspect existing structure, then derive the right `folderPath` before each create. Module root is anti-pattern.
4. **Domain model is automatically created** when the module is created. Don't try to create it again; use the appropriate update tool to add entities and associations to it.
