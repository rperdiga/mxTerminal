# Concord Build Rules — Model Discipline

> **Don't guess. Don't fake. Don't break.**

Companion file to `concord-build-rules.md` (core operational discipline) and `concord-pages-and-themes.md` (page construction + layouts + theme module). All three load together as Concord's always-loaded ruleset; section numbers (§1–§15) are globally unique across the three files, so cross-references resolve regardless of which file they're cited from.

This file owns the rules for **everything inside the model** — `ped_*` discipline (the Studio Pro MCP's create/update/remove primitives), update-operation traps (set-vs-add, element-typed properties, removal restrictions), the named failure modes that produce shipped-but-broken apps, and the new-project-new-module discipline that keeps `MyFirstModule` clean.

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

Six named failure modes from forensic builds. Guard against each by name, every time.

**1. Orphan pages.** Every non-home page must have at least one navigation entry-point: a button click, a menu item, a list-item action, or another microflow's `ShowPageAction`. Wire navigation in the same iteration as page creation; don't defer "for later." **A page document with no widget tree (or only the default Atlas content placeholder) is also an orphan** — its existence in the model means nothing if it's not built out.

**Acknowledged-placeholder exception.** If you must create a page document as a *forward reference* — e.g., you're wiring a menu item to `Page_About` now and will fill its widgets in a later step of the same build — give it the minimum recognizable content via Maia (per §2): the layout call, a title widget with the page's intended name, and a `Pages$Container` with a brief descriptive line like *"This page is in progress. Coming soon: account settings, profile edit, sign out."* Studio Pro and the running app then show something meaningful instead of blank. This is a narrow exception — most pages should be built fully when created. If you use it, the page is a debt you owe; come back inside the same build and finish it before the verification gate (§12).

**2. Shell microflows.** Every microflow you create gets a body that does the thing it's named for. After creation, walk the runtime call path (button → microflow → side-effect) and confirm the side-effect happened (a record exists, a value updated, a page appears). A microflow with only `SequenceFlow` connectors and no Activities is empty, not minimal.

**3. ActionButton wiring trap.** `Pages$ActionButton.action` is element-typed (§6). Specify `action` at *add* time. To change it later: remove the whole button and re-add with the new action. Don't `set` on `action` after the fact — it fails silently.

**4. Letter-not-spirit compliance.** A request for "5 pages and 3 microflows" is satisfied when the user can walk the journey end-to-end in a browser, not when the count of objects matches. If the user can't browse → click → see-something-happen → see-it-persist → find-it-on-another-page, the build isn't done. Counts are not deliverables; behaviors are.

**5. End-of-build "manual steps required" sections.** Forbidden as a substitute for finishing the work. The Maia bridge and Studio Pro UI handoff pattern (§8) exist specifically to avoid leaving the user with homework. Genuinely-required UI handoffs (Mark-as-UI-resources, App Settings → Runtime → After-Startup, Navigation defaults) belong **inline in the build flow as soft-stops** — surface, wait for confirmation, resume — not stacked at the end as a punt-list. **Self-check before any end-of-build summary:** scan your draft for the words *"manual"*, *"requires"*, *"in Studio Pro"*, *"after this"*, *"~5 minutes"*. Each match is a soft-stop you skipped. Go back and resolve them inline.

**6. Read-loop anti-pattern.** If you find yourself calling `ped_read_document` on the same document **3+ times without a write in between**, STOP. You are likely fighting a state-divergence problem — Studio Pro's in-memory model differs from the disk `.mpr`, or you're confused about what actually landed. Reading more won't resolve it; each read returns the same view from the same source.

The fix:

1. Call `mcp__concord-mcp__save_all` to flush Studio Pro's in-memory state to disk.
2. Call `mcp__concord-mcp__refresh_project` to ensure subsequent reads see the reconciled state.
3. **Then** re-read.

If the read still doesn't match expectations after save → refresh → re-read, surface to the user with the verbatim read output and what you expected to see. Do not continue spinning on the same document; that's the loop §3 forbids in a different shape.

When the user describes an app to clone or build, derive the **journey arc** they're really asking for — *Browse → Detail → Action → Side-effect → User-facing list/evidence* — and deliver every step. Specify the arc explicitly in your plan (§13) so the user can check it. Renames mid-build cause downstream churn (and module name is immutable per §9), so commit to entity / page / microflow names in the plan, not in the writes.

---

## 9. New project = new module

Don't pollute `MyFirstModule`. For every new project or new feature area:

1. **Create a module:** `mcp__mendix-studio-pro__ped_create_module` with a name reflecting the app domain (PascalCase, alphanumeric and underscore only, no leading digit, no spaces). Plan the final name on first creation — module renames are a Studio Pro UI step (right-click → Rename), not an MCP operation, and `set /name` on `Projects$Module` is rejected verbatim.
2. **Constructor accepts only `{ name }`.** Marking a module as a UI resources module (for theme work, see §11) is a Studio Pro UI step — see §8.
3. **Place every entity, microflow, page, and view entity in this module.** Use `mcp__mendix-studio-pro__read_skill` `folder-structure` skill to learn each doc type's canonical home (`/Domain Model/`, `/Pages/<EntityFolder>/`, `/Microflows/<EntityFolder>/`). Module root is anti-pattern — `ped_list_folder` first to inspect existing structure, then derive the right `folderPath` before each create.
4. **Domain model is automatically created.** Don't try to `ped_create_document` it; use `ped_update_document` against the module's domain model with the module name as `documentName`.
