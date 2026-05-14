# Concord MCP — argument shapes (reverse-engineered)

> Source of truth for the matrix.jsonc entries. For each tool, derived by
> grepping `MendixDomainModelTools.cs` and `MendixAdditionalTools.cs` for the
> `parameters?["X"]?.ToString()` access pattern and the explicit "X is
> required" guards. Where source allows multiple shapes, the most-likely-
> success shape is documented along with the ambiguity.

## Conventions

- **Required fields** are listed first; **optional** below them with default.
- File-line cite is to the C# method body, not the dispatch table.
- "Notes" captures source-visible quirks (e.g. ToString on a JsonArray; a
  branch for both string and array shape on the same field).

---

## Family 1 — Diagnostics & Read-Only Utilities

### `analyze_project_patterns` — _Diagnostics_

**Source:** [`MendixAdditionalTools.cs:4432`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): scope analysis to a single module; omit for all modules.
- `save_skill` (string, default `"true"`): pass `"false"` to skip skill-file write.
- `skill_file_path` (string, default `null`): custom path for the generated skill YAML.

**Notes:** `save_skill` is read as `ToString().ToLowerInvariant() != "false"` — any non-"false" value enables it.

**Suggested matrix args:** `{ "module_name": "MyFirstModule" }`

---

### `check_model` — _Diagnostics_

**Source:** [`MendixDomainModelTools.cs:465`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter results to a single module; omit for all modules.

**Notes:** Uses null-safe `parameters?["module_name"]?.ToString()` — parameters object itself may be null.

**Suggested matrix args:** `{}`

---

### `check_project_errors` — _Diagnostics_

**Source:** [`MendixAdditionalTools.cs:789`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — the Core Interop surface does not expose a consistency-check API. No parameters are read.

**Suggested matrix args:** `{}`

---

### `check_variable_name` — _Diagnostics_

**Source:** [`MendixAdditionalTools.cs:3621`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `microflow_name` (string): simple or qualified name of the microflow.
- `variable_name` (string): variable name to check for conflicts.

**Optional:**
- `module_name` (string, default `null`): module to disambiguate when `microflow_name` is unqualified.

**Notes:** none

**Suggested matrix args:** `{ "microflow_name": "MyFirstModule.ACT_Example", "variable_name": "NewCustomer" }`

---

### `diagnose_associations` — _Diagnostics_

**Source:** [`MendixDomainModelTools.cs:1500`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): limit diagnosis to a single module; omit for all modules.

**Notes:** Uses null-safe access `parameters?["module_name"]`.

**Suggested matrix args:** `{}`

---

### `get_last_error` — _Diagnostics_

**Source:** [`MendixAdditionalTools.cs:617`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read. Returns the last recorded error string from the static `_lastError` field.

**Suggested matrix args:** `{}`

---

### `get_last_error_domain` — _Diagnostics_

**Source:** [`MendixDomainModelTools.cs:1585`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Registered separately from `get_last_error`; the `MendixDomainModelTools.GetLastError` implementation always returns a stub "not implemented" response. Distinct from `additional.GetLastError`.

**Suggested matrix args:** `{}`

---

### `get_studio_pro_logs` — _Diagnostics_

**Source:** [`MendixAdditionalTools.cs:644`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `level` (string, default `"ERROR"`): log level filter — one of `ERROR`, `WARN`, `INFO`, `ALL`.
- `last_minutes` (int, default `30`): how many minutes back to retrieve.

**Notes:** Reads `arguments?["last_minutes"]?.ToString()` and parses with `int.TryParse` — send as a JSON number or string. Path to Studio Pro log file is hardcoded to `11.5.0` version subfolder.

**Suggested matrix args:** `{ "level": "ERROR", "last_minutes": 10 }`

---

### `list_available_tools` — _Diagnostics_

**Source:** [`MendixAdditionalTools.cs:800`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Returns a hardcoded string array of tool names; no parameters read.

**Suggested matrix args:** `{}`

---

### `list_available_tools_domain` — _Diagnostics_

**Source:** [`MendixDomainModelTools.cs:1590`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Registered as a separate dispatch entry pointing to `MendixDomainModelTools.ListAvailableTools`. Returns a different (slightly older) hardcoded tool list than `list_available_tools`.

**Suggested matrix args:** `{}`

---

### `list_java_actions` — _Diagnostics_

**Source:** [`MendixAdditionalTools.cs:2706`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter by module; omit for all modules.

**Notes:** Uses null-safe access `parameters?["module_name"]`.

**Suggested matrix args:** `{}`

---

## Family 2 — DomainModel Reads

### `list_modules` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:22`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read at all; lists all modules unconditionally.

**Suggested matrix args:** `{}`

---

### `read_domain_model` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:821`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): return domain model for a single module; omit for all modules.

**Notes:** Uses null-safe `parameters?["module_name"]`. Omitting `module_name` returns all modules.

**Suggested matrix args:** `{ "module_name": "MyFirstModule" }`

---

### `read_project_info` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:745`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read; returns global project metadata.

**Suggested matrix args:** `{}`

---

### `query_model_elements` — _DomainModel_

**Source:** [`MendixAdditionalTools.cs:3413`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `type_name` (string): Mendix type string, e.g. `"Navigation$NavigationProfile"`, `"Microflows$Nanoflow"`. For entity queries use `"DomainModels$Entity"`.

**Optional:**
- `module_name` (string, default `null`): filter results by module.
- `include_properties` (bool, default `false`): include extended property data.
- `max_results` (int, default `50`): limit result count.

**Notes:** BUG-014 fix: for entity and association type names, routes through `IDomainModelHost` instead of `IUntypedModelHost.GetUnitsOfType` (which only works for top-level document units). Boolean parsed via `?.GetValue<bool>()`.

**Suggested matrix args:** `{ "type_name": "DomainModels$Entity", "module_name": "MyFirstModule", "max_results": 10 }`

---

### `query_associations` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2980`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- `entity_name` (string, default `null`): filter to associations involving this entity.
- `second_entity` (string, default `null`): filter to associations between `entity_name` and `second_entity`.
- `module_name` (string, default `null`): scope to a module.
- `direction` (string, default `"both"`): one of `"incoming"`, `"outgoing"`, `"both"`.

**Notes:** All parameters optional; no required fields. Delegates to `IDomainModelHost.QueryAssociations`.

**Suggested matrix args:** `{ "entity_name": "Customer", "module_name": "MyFirstModule" }`

---

### `read_attribute_details` — _DomainModel_

**Source:** [`MendixAdditionalTools.cs:4065`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `entity_name` (string): simple or qualified entity name (e.g. `"Customer"` or `"MyFirstModule.Customer"`).
- `attribute_name` (string): name of the attribute.

**Optional:**
- `module_name` (string, default `null`): disambiguate module for the entity.

**Notes:** Returns limited metadata — `maxLength`, `enumeration`, `defaultValue`, `calculatedMicroflow` are null because `AttributeRef` does not expose them at the Interop boundary (Task 14+ gap).

**Suggested matrix args:** `{ "entity_name": "Customer", "attribute_name": "Name" }`

---

### `validate_name` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2166`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `name` (string): the identifier string to validate.

**Optional:**
- `auto_fix` (bool, default `false`): if true, returns a suggested fixed version.

**Notes:** Returns `INameValidationResult` with `isValid`, `errorMessage`, and `fixedName`.

**Suggested matrix args:** `{ "name": "MyEntity" }`

---

## Family 3 — Microflows Reads

### `list_microflows` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:479`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter by module; omit for all modules.

**Notes:** none

**Suggested matrix args:** `{}`

---

### `read_microflow_details` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:522`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `microflow_name` (string): simple or qualified name (e.g. `"ACT_Example"` or `"MyFirstModule.ACT_Example"`).

**Optional:**
- `module_name` (string, default `null`): used to build qualified name when `microflow_name` is unqualified.

**Notes:** AMBIGUOUS — `microflow_name` may contain `.` (treated as fully qualified) or be plain (combined with `module_name`). Matrix entry uses plain name + `module_name`.

**Suggested matrix args:** `{ "microflow_name": "ACT_Example", "module_name": "MyFirstModule" }`

---

### `list_nanoflows` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:3262`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter by module.

**Notes:** Uses null-safe `parameters?["module_name"]`.

**Suggested matrix args:** `{}`

---

### `read_nanoflow_details` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:3192`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `nanoflow_name` (string): simple or qualified nanoflow name.

**Optional:**
- `module_name` (string, default `null`): used to build qualified name when unqualified.

**Notes:** Activity details are not available via the typed API; returns `activities: []` with an explanatory note.

**Suggested matrix args:** `{ "nanoflow_name": "MyNanoflow", "module_name": "MyFirstModule" }`

---

### `list_scheduled_events` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:3304`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter by module prefix on qualified name.

**Notes:** Requires IUntypedModel (Studio Pro 11+). Uses `parameters?["module_name"]`.

**Suggested matrix args:** `{}`

---

## Family 4 — Pages Reads

### `list_pages` — _Pages_

**Source:** [`MendixAdditionalTools.cs:3847`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter by module.
- `include_excluded` (bool, default `false`): include pages marked as excluded.

**Notes:** Requires IUntypedModel. `include_excluded` parsed via `?.GetValue<bool>()`.

**Suggested matrix args:** `{}`

---

### `read_page_details` — _Pages_

**Source:** [`MendixAdditionalTools.cs:4167`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `page_name` (string): simple or qualified page name.

**Optional:**
- `module_name` (string, default `null`): used to scope search when `page_name` is unqualified.

**Notes:** Returns flat properties plus `propertiesJson` dump. Sub-element traversal (widget tree) not available via `IUntypedModelHost`. Uses `parameters?["page_name"]`.

**Suggested matrix args:** `{ "page_name": "Customer_Overview", "module_name": "MyFirstModule" }`

---

## Family 5 — Workflows Reads

### `list_workflows` — _Workflows_

**Source:** [`MendixAdditionalTools.cs:4279`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter by module.

**Notes:** Requires IUntypedModel. Uses `parameters?["module_name"]`.

**Suggested matrix args:** `{}`

---

### `read_workflow_details` — _Workflows_

**Source:** [`MendixAdditionalTools.cs:4335`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `workflow_name` (string): simple or qualified workflow name.

**Optional:**
- `module_name` (string, default `null`): scope search.

**Notes:** Returns flat properties plus `propertiesJson`. Deep sub-element traversal (activities, outcomes) not available.

**Suggested matrix args:** `{ "workflow_name": "MyWorkflow", "module_name": "MyFirstModule" }`

---

## Family 6 — Constants & Enums Reads

### `list_constants` — _ConstantsEnums_

**Source:** [`MendixDomainModelTools.cs:519`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter by module; uses `QualifiedName` prefix matching.

**Notes:** Requires IUntypedModel (Studio Pro 11+). Returns `escalation: manual` stub on 10.x.

**Suggested matrix args:** `{}`

---

### `list_enumerations` — _ConstantsEnums_

**Source:** [`MendixDomainModelTools.cs:668`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter to a single module.

**Notes:** Per-module resilience: certain modules (system, App Store imports) may throw internally; those are recorded in `skippedModules[]` and iteration continues. Enumeration values not returned (gap in `IDomainModelHost`).

**Suggested matrix args:** `{}`

---

## Family 7 — Security Reads

### `list_rules` — _Security_

**Source:** [`MendixAdditionalTools.cs:2975`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — `IRule` documents are not exposed on the typed Interop surface.

**Suggested matrix args:** `{}`

---

### `read_security_info` — _Security_

**Source:** [`MendixAdditionalTools.cs:3103`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — requires `IModelRoot` sub-element traversal not exposed on `IUntypedModelHost`.

**Suggested matrix args:** `{}`

---

### `read_entity_access_rules` — _Security_

**Source:** [`MendixAdditionalTools.cs:3138`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — `DomainModels$AccessRule` requires `IModelUnit` sub-element traversal not exposed via `IUntypedModelHost`.

**Suggested matrix args:** `{}`

---

### `read_microflow_security` — _Security_

**Source:** [`MendixAdditionalTools.cs:3154`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — `allowedModuleRoles` property requires `IModelUnit` traversal; `IMicroflowAuthoringHost.ReadMicroflow` only exposes `AccessLevel` enum.

**Suggested matrix args:** `{}`

---

### `audit_security` — _Security_

**Source:** [`MendixAdditionalTools.cs:3171`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — full audit requires cross-type traversal across `Security$ProjectSecurity`, `Security$ModuleSecurity`, `DomainModels$AccessRule` and microflow roles, none accessible via flat `IUntypedModelHost`.

**Suggested matrix args:** `{}`

---

## Family 8 — ProjectSettings Reads

### `read_runtime_settings` — _ProjectSettings_

**Source:** [`MendixAdditionalTools.cs:2748`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read. Delegates directly to `HostServices.Model.ReadRuntimeSettings()`.

**Suggested matrix args:** `{}`

---

### `read_configurations` — _ProjectSettings_

**Source:** [`MendixAdditionalTools.cs:2808`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `configuration_name` (string, default `null`): filter to a specific configuration by name.

**Notes:** Uses null-safe `parameters?["configuration_name"]`. `IsActive`, `DatabaseType`, and `DatabaseConnectionString` returned as `false`/`null` (typed API gap, Phase 3 spike finding).

**Suggested matrix args:** `{}`

---

### `list_rest_services` — _ProjectSettings_

**Source:** [`MendixAdditionalTools.cs:3356`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- `module_name` (string, default `null`): filter by module prefix on qualified name.

**Notes:** Requires IUntypedModel. Resource sub-elements not available via `IUntypedModelHost`; raw `propertiesJson` is included in each result object.

**Suggested matrix args:** `{}`

---

### `read_version_control` — _ProjectSettings_

**Source:** [`MendixAdditionalTools.cs:2872`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read. Returns `available: false` if `IVersionControlHost.IsAvailable` is false.

**Suggested matrix args:** `{}`

---

## Family 9 — UI Actions Reads

### `get_app_status` — _UiActions_

**Source:** [`UiActionsBootstrap.cs:23`](../../src/Concord.Core/Mcp/UiActionsBootstrap.cs) — dispatch only; body in `StudioProActions.cs`.

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read. Returns composite status (run state, active configuration, etc.).

**Suggested matrix args:** `{}`

---

### `get_active_run_configuration` — _UiActions_

**Source:** [`UiActionsBootstrap.cs:22`](../../src/Concord.Core/Mcp/UiActionsBootstrap.cs) — dispatch only; body in `StudioProActions.cs`.

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read. Returns the name of the currently active run configuration.

**Suggested matrix args:** `{}`

---

## Family 10 — DomainModel Mutations

### `create_module` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:63`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `module_name` (string): name of the new module.

**Optional:**
- (none)

**Notes:** Returns error if module already exists (duplicate check via `GetModuleByName`).

**Suggested matrix args:** `{ "module_name": "SweepTestModule" }`

---

### `rename_module` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2471`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `module_name` (string): current name of the module.
- `new_name` (string): new name; must pass `ValidateName`.

**Optional:**
- (none)

**Notes:** Validates `new_name` via `HostServices.DomainModel.ValidateName` before renaming.

**Suggested matrix args:** `{ "module_name": "SweepTestModule", "new_name": "SweepTestModuleRenamed" }`

---

### `create_entity` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:932`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): name of the new entity.

**Optional:**
- `module_name` (string, default first module): module to create entity in.
- `persistable` (bool, default `true`): set to `false` for non-persistent.
- `entityType` (string, default `"persistent"`): `"persistent"` or `"non-persistent"`.
- `attributes` (array of object, default `[]`): each `{ name, type }` object; also accepted as `attribute_list` or `attrs`.
- `generalization` (string, default `null`): qualified parent entity name.
- `documentation` (string, default `null`): entity documentation.

**Notes:** AMBIGUOUS — `persistable` (bool) and `entityType` (string) both control the same `EntityKind`; `entityType` wins when both are present. `attributes` accepted as JsonArray OR stringified JSON array (Claude Code compatibility shim via `Utils.GetArrayParam`).

**Suggested matrix args:** `{ "module_name": "MyFirstModule", "entity_name": "SweepEntity_create_entity" }`

---

### `create_multiple_entities` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:1109`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entities` (array of object): each object has `entity_name` (or `name`), optional `module_name`, `entityType`, `attributes`, `generalization`, `documentation`.

**Optional:**
- `module_name` (string, default first module): default module for entities that don't specify their own.
- `persistable` (bool, default `true`): global default; overridden per entity if `entityType` is set.

**Notes:** Array accepted via `Utils.GetArrayParam` aliases: `entities`, `entity_list`, `entityList`. Auto-arranges domain model after creation (non-fatal if arrange fails).

**Suggested matrix args:** `{ "module_name": "MyFirstModule", "entities": [{ "entity_name": "SweepEntityA" }, { "entity_name": "SweepEntityB" }] }`

---

### `rename_entity` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2248`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): current entity name.
- `new_name` (string): new name; validated via `ValidateName`.

**Optional:**
- `module_name` (string, default `null`): disambiguate module.

**Notes:** none

**Suggested matrix args:** `{ "entity_name": "SweepEntity_create_entity", "new_name": "SweepEntityRenamed", "module_name": "MyFirstModule" }`

---

### `delete_model_element` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:1392`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `element_type` (string): one of `"entity"`, `"attribute"`, `"association"`, `"microflow"`, `"module"`, `"constant"`, `"enumeration"`.

**Optional:**
- `entity_name` (string, default `null`): required when `element_type` is `"entity"` or `"attribute"`; falls back to `element_name`.
- `element_name` (string, default `null`): alias for `entity_name`/`document_name`.
- `attribute_name` (string, default `null`): required when `element_type` is `"attribute"`.
- `association_name` (string, default `null`): required when `element_type` is `"association"`.
- `document_name` (string, default `null`): used for microflow/document type.
- `module_name` (string, default `null`): scope entity/association lookup to a module.

**Notes:** Types `module`, `constant`, `enumeration`, and `nanoflow` always return `escalation: manual`. Microflow deletion redirects to `delete_document`.

**Suggested matrix args:** `{ "element_type": "entity", "entity_name": "SweepEntityRenamed", "module_name": "MyFirstModule" }`

---

### `copy_model_element` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2196`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `element_type` (string): one of `"entity"`, `"microflow"`, `"constant"`, `"enumeration"`.
- `source_name` (string): name of the element to copy.
- `new_name` (string): name for the copy.

**Optional:**
- `source_module` (string, default `null`): module containing the source element.
- `target_module` (string, default same as `source_module`): module for the copy; BUG-013 fix defaults to source module if omitted.

**Notes:** none

**Suggested matrix args:** `{ "element_type": "entity", "source_name": "Customer", "new_name": "SweepEntity_copy", "source_module": "MyFirstModule" }`

---

### `set_entity_generalization` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:97`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): entity to set generalization on.
- `parent_entity` (string): the parent entity name.

**Optional:**
- `module_name` (string, default `null`): module for `entity_name`.
- `parent_module` (string, default `null`): module for `parent_entity`.

**Notes:** BUG-015 fix: prevents self-referencing generalization at both name level and GUID level.

**Suggested matrix args:** `{ "entity_name": "Order", "parent_entity": "Administration.FileDocument", "module_name": "MyFirstModule" }`

---

### `remove_entity_generalization` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:145`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): entity to remove generalization from.

**Optional:**
- `module_name` (string, default `null`): module scope.

**Notes:** Returns error if entity has no generalization to remove.

**Suggested matrix args:** `{ "entity_name": "Order", "module_name": "MyFirstModule" }`

---

### `add_attribute` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:289`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): entity to add attribute to.
- `attribute_name` (string): name of the new attribute.
- `attribute_type` (string): one of `String`, `Integer`, `Decimal`, `Long`, `Boolean`, `DateTime`, `AutoNumber`, `Binary`, `HashedString`, `Enumeration`, `Enumeration:QualifiedEnumName`.

**Optional:**
- `module_name` (string, default `null`): module scope.
- `default_value` (string, default `null`): default value expression.
- `documentation` (string, default `null`): attribute documentation.
- `enumeration_name` (string, default `null`): qualified enumeration name when `attribute_type` is `Enumeration`.
- `enumeration_values` (array of string, default `[]`): inline enum values when no existing enumeration is referenced; also accepted as `values`, `enum_values`, `enumerationValues`.
- `max_length` (int, default `null`): max length for String attributes.
- `localize_date` (bool, default `null`): localization flag for DateTime.

**Notes:** AMBIGUOUS — when `attribute_type` is `Enumeration`, the tool branches: (1) if type contains `":"` suffix, uses that as `enumQualifiedName`; (2) else checks `enumeration_name`; (3) else requires `enumeration_values` array. Sending `attribute_type: "Enumeration"` without either `enumeration_name` or `enumeration_values` returns an error.

**Suggested matrix args:** `{ "entity_name": "Customer", "attribute_name": "FullName", "attribute_type": "String", "module_name": "MyFirstModule" }`

---

### `update_attribute` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2577`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): entity containing the attribute.
- `attribute_name` (string): attribute to update.

**Optional:**
- `module_name` (string, default `null`): module scope.
- `type` (string, default `null`): new attribute type; accepts same values as `add_attribute`.
- `max_length` (int, default `null`): new max length.
- `localize_date` (bool, default `null`): new localize flag.
- `default_value` (string, default `null`): new default value.
- `documentation` (string, default `null`): new documentation.

**Notes:** At least one of `type`, `max_length`, `localize_date`, `default_value`, `documentation` must be provided. `type` parsed as `parameters["type"]?.ToString()` — note key is `"type"` not `"attribute_type"`.

**Suggested matrix args:** `{ "entity_name": "Customer", "attribute_name": "FullName", "type": "String", "max_length": 200, "module_name": "MyFirstModule" }`

---

### `rename_attribute` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2292`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): entity containing the attribute.
- `attribute_name` (string): current attribute name.
- `new_name` (string): new name; validated via `ValidateName`.

**Optional:**
- `module_name` (string, default `null`): module scope.

**Notes:** none

**Suggested matrix args:** `{ "entity_name": "Customer", "attribute_name": "FullName", "new_name": "CustomerName", "module_name": "MyFirstModule" }`

---

### `set_calculated_attribute` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:395`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): entity containing the attribute.
- `attribute_name` (string): attribute to mark as calculated.
- `microflow` (string): microflow name (simple or qualified) to use as calculator.

**Optional:**
- `module_name` (string, default `null`): module scope for entity lookup.

**Notes:** Searches all modules for `microflow` when unqualified. The microflow must already exist.

**Suggested matrix args:** `{ "entity_name": "Customer", "attribute_name": "FullName", "microflow": "MyFirstModule.CAL_Customer_FullName" }`

---

### `configure_system_attributes` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2000`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): entity to configure.

**Optional:**
- `module_name` (string, default `null`): module scope.
- `has_created_date` (bool, default `null`): enable/disable `createdDate` system attribute.
- `has_changed_date` (bool, default `null`): enable/disable `changedDate` system attribute.
- `has_owner` (bool, default `null`): enable/disable `owner` system attribute.
- `has_changed_by` (bool, default `null`): enable/disable `changedBy` system attribute.
- `persistable` (bool, default `null`): toggle entity persistence.

**Notes:** At least one of the optional booleans must be provided or tool returns an error. Returns error if entity has a generalization (system attrs only on root entities). All booleans parsed via `?.GetValue<bool>()`.

**Suggested matrix args:** `{ "entity_name": "Customer", "module_name": "MyFirstModule", "has_created_date": true, "has_changed_date": true }`

---

### `add_event_handler` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:181`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `entity_name` (string): entity to add event handler to.
- `event` (string): one of `create`, `commit`, `delete`, `rollback`.
- `moment` (string): one of `before`, `after`.
- `microflow` (string): microflow name (simple or qualified).

**Optional:**
- `module_name` (string, default `null`): module scope for entity lookup.
- `raise_error_on_false` (bool, default `true`): parsed via `AsValue().TryGetValue<bool>()`.

**Notes:** `event` + `moment` must form a valid `EventHandlerKind` combination; invalid pairs return error. Microflow resolved across all modules if unqualified.

**Suggested matrix args:** `{ "entity_name": "Customer", "event": "commit", "moment": "before", "microflow": "MyFirstModule.BCO_Customer_BeforeCommit" }`

---

### `create_association` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:1013`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `name` (string): association name; also accepted as `association_name`, `associationName`.
- `parent` (string): parent entity name; also accepted as `parent_entity`, `parentEntity`, `from_entity`.
- `child` (string): child entity name; also accepted as `child_entity`, `childEntity`, `to_entity`.

**Optional:**
- `type` (string, default `"one-to-many"`): `"one-to-many"` or `"many-to-many"`.
- `module_name` (string, default `null`): default module for both entities and association owner.
- `parent_module` (string, default `module_name`): override module for `parent` entity.
- `child_module` (string, default `module_name`): override module for `child` entity.
- `parent_delete_behavior` (string, default `"delete_me_but_keep_references"`): one of `cascade`/`delete_me_and_references`, `prevent`/`delete_me_if_no_references`, or default.
- `child_delete_behavior` (string, default `"delete_me_but_keep_references"`): same values.
- `owner` (string, default `"default"`): `"both"` for bidirectional reference-set ownership.
- `documentation` (string, default `null`): association documentation.

**Notes:** Parameter name aliases resolved via `Utils.GetParam`. `type` defaults to `one-to-many` if not provided.

**Suggested matrix args:** `{ "name": "Customer_Orders", "parent": "Customer", "child": "Order", "module_name": "MyFirstModule" }`

---

### `create_multiple_associations` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:1226`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `associations` (array of object): each object with `name`, `parent`, `child`; optional `type`, `parent_module`, `child_module`, `owner`, `parent_delete_behavior`, `child_delete_behavior`, `documentation`.

**Optional:**
- `module_name` (string, default `null`): default module for entity resolution when per-association modules not specified.

**Notes:** Array accepted via `Utils.GetArrayParam` aliases: `associations`, `association_list`, `associationList`, `assocs`. Silently skips associations where parent or child entities don't exist.

**Suggested matrix args:** `{ "associations": [{ "name": "Customer_Orders", "parent": "Customer", "child": "Order" }], "module_name": "MyFirstModule" }`

---

### `update_association` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2676`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `association_name` (string): current association name.

**Optional:**
- `module_name` (string, default `null`): scope association lookup.
- `type` (string, default `null`): new type — `"reference"` or `"referenceset"`.
- `owner` (string, default `null`): new owner — `"default"` or `"both"`.
- `parent_delete_behavior` (string, default `null`): new parent delete behavior.
- `child_delete_behavior` (string, default `null`): new child delete behavior.
- `documentation` (string, default `null`): new documentation.

**Notes:** At least one optional field must be provided. `type` parsed as `parameters["type"]?.ToString()` — note key is `"type"` not `"association_type"`.

**Suggested matrix args:** `{ "association_name": "Customer_Orders", "owner": "both", "module_name": "MyFirstModule" }`

---

### `rename_association` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2340`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `association_name` (string): current association name.
- `new_name` (string): new name; validated via `ValidateName`.

**Optional:**
- `module_name` (string, default `null`): scope association lookup.

**Notes:** none

**Suggested matrix args:** `{ "association_name": "Customer_Orders", "new_name": "Customer_OrderItems", "module_name": "MyFirstModule" }`

---

### `arrange_domain_model` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:1966`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `module_name` (string): module whose domain model to arrange.

**Optional:**
- `root_entity` (string, default `null`): entity to use as layout root.

**Notes:** `root_entity` passed directly to `ArrangeDomainModelRequest`; may be null.

**Suggested matrix args:** `{ "module_name": "MyFirstModule" }`

---

### `create_domain_model_from_schema` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:1337`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `schema` (object): JSON schema object describing the domain model to create.

**Optional:**
- `module_name` (string, default first module): target module.

**Notes:** `schema` must be a JsonObject (not a string). The object is serialized to JSON string and passed to `HostServices.DomainModel.CreateDomainModelFromSchema`. Auto-arranges after creation.

**Suggested matrix args:** `{ "module_name": "MyFirstModule", "schema": { "entities": [{ "name": "SweepEntity_schema", "attributes": [{ "name": "Title", "type": "String" }] }] } }`

---

### `manage_folders` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2080`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `action` (string): one of `"list"`, `"create"`, `"move_document"`.
- `module_name` (string): target module.

**Optional:**
- `folder_name` (string, default `null`): required when `action` is `"create"`.
- `parent_folder` (string, default `""`): parent folder path for create; empty string = module root.
- `document_name` (string, default `null`): required when `action` is `"move_document"`; qualified or simple name.
- `target_folder` (string, default `null`): target folder path for move; null = module root.

**Notes:** none

**Suggested matrix args:** `{ "action": "list", "module_name": "MyFirstModule" }`

---

### `set_documentation` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2893`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `element_type` (string): one of `"entity"`, `"attribute"`, `"association"`, `"domain_model"`.
- `documentation` (string): documentation text (empty string to clear).

**Optional:**
- `element_name` (string, default `null`): name of the element; required for all types except `domain_model` (where `module_name` is used instead).
- `module_name` (string, default `null`): module scope; required when `element_type` is `"domain_model"`.
- `entity_name` (string, default `null`): entity for `attribute` type; falls back to `element_name`.
- `attribute_name` (string, default `null`): attribute name when `element_type` is `"attribute"`.

**Notes:** For `attribute` type: `element_name` may be `"Entity.Attribute"` dotted form to avoid requiring separate `entity_name` and `attribute_name`.

**Suggested matrix args:** `{ "element_type": "entity", "element_name": "Customer", "documentation": "Represents a customer.", "module_name": "MyFirstModule" }`

---

### `rename_document` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:2380`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `document_name` (string): current name (simple or qualified `Module.Name`).
- `new_name` (string): new name; validated via `ValidateName`.

**Optional:**
- `module_name` (string, default `null`): module scope.
- `document_type` (string, default `null`): type filter — `"microflow"`, `"constant"`, or `"enumeration"`.

**Notes:** If `document_name` contains `.` and `module_name` is null, splits into module + name. Falls back to full `ListAllDocuments` search when direct qualified lookup fails.

**Suggested matrix args:** `{ "document_name": "ACT_Example", "new_name": "ACT_ExampleRenamed", "module_name": "MyFirstModule", "document_type": "microflow" }`

---

## Family 11 — Constants & Enums Mutations

### `create_constant` — _ConstantsEnums_

**Source:** [`MendixDomainModelTools.cs:509`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — typed `IConstant` write not exposed on Core Interop surface. No parameters read.

**Suggested matrix args:** `{}`

---

### `update_constant` — _ConstantsEnums_

**Source:** [`MendixDomainModelTools.cs:2771`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — typed `IConstant` write not exposed. No parameters read.

**Suggested matrix args:** `{}`

---

### `configure_constant_values` — _ConstantsEnums_

**Source:** [`MendixAdditionalTools.cs:4150`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — requires `IConfigurationSettings → IConstantValue → ISharedValue` mutation not exposed on typed Interop. No parameters read.

**Suggested matrix args:** `{}`

---

### `create_enumeration` — _ConstantsEnums_

**Source:** [`MendixDomainModelTools.cs:584`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `name` (string): enumeration name; also accepted as `enumeration_name`, `enumerationName`.
- `values` (array of string or object): enumeration values; also accepted as `enumeration_values`, `enum_values`, `enumerationValues`. Each item may be a string or `{ name, caption }` object.

**Optional:**
- `module_name` (string, default first module): module to create enumeration in.

**Notes:** AMBIGUOUS — `values` accepts both native JsonArray and stringified JSON array (Claude Code compatibility shim). Items may be plain strings or `{name, caption}` objects. Name resolved via `Utils.GetParam` aliases.

**Suggested matrix args:** `{ "name": "OrderStatus", "module_name": "MyFirstModule", "values": ["Draft", "Submitted", "Approved"] }`

---

### `update_enumeration` — _ConstantsEnums_

**Source:** [`MendixDomainModelTools.cs:2784`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `enumeration_name` (string): enumeration to update (simple or qualified).

**Optional:**
- `module_name` (string, default `null`): module scope.
- `add_values` (array of string, default `[]`): new values to add.
- `remove_values` (array of string, default `[]`): existing values to remove.
- `rename_values` (object, default `null`): `{ "OldName": "NewName" }` map.

**Notes:** At least one of `add_values`, `remove_values`, `rename_values` must be provided. All three are parsed as JsonArray/JsonObject directly (not via `Utils.GetArrayParam`).

**Suggested matrix args:** `{ "enumeration_name": "OrderStatus", "add_values": ["Cancelled"], "module_name": "MyFirstModule" }`

---

### `rename_enumeration_value` — _ConstantsEnums_

**Source:** [`MendixDomainModelTools.cs:2509`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `enumeration_name` (string): enumeration name (simple or qualified).
- `value_name` (string): current value name.
- `new_name` (string): new value name; validated via `ValidateName`.

**Optional:**
- `module_name` (string, default `null`): module scope.

**Notes:** When `enumeration_name` is unqualified and `module_name` is null, searches all modules via `ListAllDocuments("enumeration")`.

**Suggested matrix args:** `{ "enumeration_name": "OrderStatus", "value_name": "Draft", "new_name": "New", "module_name": "MyFirstModule" }`

---

## Family 12 — Microflows Mutations

### `create_microflow` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:979`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `name` (string): microflow name; also accepted as `microflow_name`, `microflowName`.
- `module_name` (string): module to create microflow in.

**Optional:**
- `access_level` (string, default `"CheckPerOperation"`): `"AllowAll"` or `"CheckPerOperation"` or `"Internal"`.
- `return_type` (string, default `null`/void): `"void"`, scalar type (`"String"`, `"Boolean"`, etc.), `"Object"`, or `"List"`.
- `return_entity` (string, default `null`): qualified entity name when `return_type` is `"Object"` or `"List"`; also accepted as `returnEntity`.
- `parameters` (array of object, default `[]`): each `{ name, type, entity?, is_list?, documentation? }`; also accepted as `params`, `microflow_parameters`.
- `documentation` (string, default `null`): microflow documentation.
- `folder_path` (string, default `null`): folder path within module; also accepted as `folderPath`.

**Notes:** `name` also accepted as `microflow_name` or `microflowName` (via `Utils.GetParam`). `return_type` also accepted as `returnType`. Parameter name resolved via `Utils.GetArrayParam` for `parameters`/`params`/`microflow_parameters`. Parameter objects support both `"object"` and `"list"` type values with `entity` field.

**Suggested matrix args:** `{ "name": "ACT_SweepTest", "module_name": "MyFirstModule" }`

---

### `update_microflow` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:3976`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `microflow_name` (string): microflow to update (simple or qualified).

**Optional:**
- `module_name` (string, default `null`): module scope.
- `url` (string, default `null`): set the microflow's REST URL path.
- `return_type` (string, default `null`): NOT actually applied — returns warning; requires IMicroflow.ReturnType mutation not exposed.
- `return_variable_name` (string, default `null`): NOT actually applied — returns warning; requires `ReturnVariableName` mutation not exposed.

**Notes:** Currently only `url` is actually applied. `return_type` and `return_variable_name` produce warnings in the response but are not persisted. At least one field must be provided.

**Suggested matrix args:** `{ "microflow_name": "ACT_SweepTest", "module_name": "MyFirstModule", "url": "/api/sweep-test" }`

---

### `create_microflow_activity` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:1145`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `microflow_name` (string): target microflow (simple or qualified).
- `activity_type` (string): one of the supported activity type strings (e.g. `create_object`, `retrieve_from_database`, `microflow_call`, `commit_object`, `delete_object`, `change_variable`, `log`, etc.).

**Optional:**
- `module_name` (string, default `null`): module scope for microflow resolution.
- `activity_config` (object, default `null`): activity-specific config; if omitted, remaining flat parameters are used (BUG-005 fix).
- `insert_position` (int, default `null`): 1-based insertion position.
- `insert_after_activity_index` (int, default `null`): alternative positioning — converted internally to `insert_position + 1`.
- _Any flat key_: if `activity_config` is absent, all top-level params except `microflow_name`, `module_name`, `activity_type`, `insert_position`, `insert_after_activity_index` are treated as activity config.

**Notes:** AMBIGUOUS — accepts either nested `activity_config` object or flat parameters alongside `activity_type`. BUG-004 fix: checks `"type"` key as fallback if `activity_type` is missing. Well-known activity config fields: `caption`, `output_variable`/`outputVariable`/`variable_name`, `entity`/`entity_name`/`entityName`, `microflow`/`microflow_name`/`calledMicroflow`, `java_action`/`javaAction`.

**Suggested matrix args:** `{ "microflow_name": "ACT_SweepTest", "module_name": "MyFirstModule", "activity_type": "log", "activity_config": { "message": "'SweepTest log entry'" } }`

---

### `create_microflow_activities_sequence` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:2238`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `microflow_name` (string): target microflow; also accepted as `microflowName`, `microflow`.
- `activities` (array of object): each `{ activity_type, activity_config? }`; also accepted as `activity_sequence`, `activitiesSequence`.

**Optional:**
- `module_name` (string, default `null`): module scope.

**Notes:** BUG-005 fix: if `activity_config` is absent per activity, the activity object itself (minus `activity_type`) is used as config. BUG-004 fix: `"type"` accepted as fallback for `"activity_type"`. Activities inserted in reverse order so first activity ends up at position 1. Variable name tracking propagates output variable names across activities within the sequence.

**Suggested matrix args:** `{ "microflow_name": "ACT_SweepTest", "module_name": "MyFirstModule", "activities": [{ "activity_type": "log", "activity_config": { "message": "'Step 1'" } }] }`

---

### `modify_microflow_activity` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:3673`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `microflow_name` (string): microflow to modify.
- `position` (int): 1-based position of the activity to modify.

**Optional:**
- `module_name` (string, default `null`): module scope.
- _Any other key_: all remaining keys (except `microflow_name`, `module_name`, `position`) are treated as property changes to apply (e.g. `caption`, `disabled`, `output_variable`, `commit`, `refresh_in_client`).

**Notes:** `position` parsed via `parameters["position"].GetValue<int>()` — must be a JSON integer. Validates position against `ReadActivities` count. At least one property-change key must be provided beyond the three reserved keys.

**Suggested matrix args:** `{ "microflow_name": "ACT_SweepTest", "module_name": "MyFirstModule", "position": 1, "caption": "Updated Caption" }`

---

### `insert_before_activity` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:3744`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `microflow_name` (string): target microflow.
- `before_position` (int): 1-based position of the existing activity to insert before.
- `activity` (object): activity definition with `type` (string, required) and any additional fields as activity parameters.

**Optional:**
- `module_name` (string, default `null`): module scope.

**Notes:** `activity.type` is required inside the activity object (note: `"type"` not `"activity_type"` unlike `create_microflow_activity`). `activity.output_variable`/`outputVariable`/`variable_name`, `entity`/`entity_name`/`entityName`, `microflow`/`microflow_name`/`calledMicroflow`, `java_action`/`javaAction` are well-known config fields.

**Suggested matrix args:** `{ "microflow_name": "ACT_SweepTest", "module_name": "MyFirstModule", "before_position": 1, "activity": { "type": "log", "message": "'inserted before'" } }`

---

### `set_microflow_url` — _Microflows_

**Source:** [`MendixAdditionalTools.cs:2908`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `microflow_name` (string): microflow to configure (simple or qualified).

**Optional:**
- `module_name` (string, default `null`): module scope.
- `url` (string, default `null`): URL path to set; omit for read-only info response. Pass empty string to clear.

**Notes:** When `url` is not present in parameters, returns an informational response instead of setting anything. `parameters.ContainsKey("url")` used to differentiate.

**Suggested matrix args:** `{ "microflow_name": "ACT_SweepTest", "module_name": "MyFirstModule", "url": "/sweep/test" }`

---

## Family 13 — Pages Mutations

### `generate_overview_pages` — _Pages_

**Source:** [`MendixAdditionalTools.cs:405`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `entity_names` (array of string): entity names to generate pages for; also accepted as `entityNames`, `entities`.
- `module_name` (string): module to generate pages in.

**Optional:**
- `generate_index_snippet` (bool, default `true`): whether to generate an index snippet.

**Notes:** `entity_names` accepted via `Utils.GetArrayParam` with aliases (captured from real failed Claude Code call: `entity_names`, `entityNames`, `entities`). AMBIGUOUS — accepts JsonArray or stringified JSON array.

**Suggested matrix args:** `{ "module_name": "MyFirstModule", "entity_names": ["Customer", "Order"] }`

---

### `delete_document` — _Pages_

**Source:** [`MendixAdditionalTools.cs:3912`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `document_name` (string): document to delete (simple name only — qualified name built from `module_name`).
- `module_name` (string): module containing the document.

**Optional:**
- `document_type` (string, default `null`): informational only; not used for resolution.

**Notes:** Qualified name is always built as `module_name.document_name` — does not accept pre-qualified names. Delegates deletion to `HostServices.PageGeneration.DeleteDocument`.

**Suggested matrix args:** `{ "document_name": "Customer_Overview", "module_name": "MyFirstModule" }`

---

### `exclude_document` — _Pages_

**Source:** [`MendixAdditionalTools.cs:2991`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `document_name` (string): document to exclude/include (simple or qualified name).

**Optional:**
- `module_name` (string, default `null`): module scope.
- `excluded` (bool, default `true`): `true` to exclude, `false` to re-include.

**Notes:** When unqualified and no `module_name`, searches all modules via `ListModuleDocuments`. `excluded` parsed via `?.GetValue<bool>() ?? true`.

**Suggested matrix args:** `{ "document_name": "Customer_Overview", "module_name": "MyFirstModule", "excluded": true }`

---

## Family 14 — ProjectSettings Mutations

### `set_runtime_settings` — _ProjectSettings_

**Source:** [`MendixAdditionalTools.cs:2774`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `key` (string): setting key to write. OR
- `settings` (array of object): batch mode — each `{ key, value }` object.

**Optional:**
- `value` (string, default `null`): value for single-key mode.

**Notes:** AMBIGUOUS — supports two shapes: (1) single `{ key, value }` or (2) batch `{ settings: [{ key, value }, ...] }`. When `settings` is a JsonArray, single `key`/`value` are ignored.

**Suggested matrix args:** `{ "key": "com.mendix.core.SessionTimeout", "value": "3600" }`

---

### `set_configuration` — _ProjectSettings_

**Source:** [`MendixAdditionalTools.cs:2844`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `configuration_name` (string): name of the configuration to activate.

**Optional:**
- (none)

**Notes:** May return `escalation: manual` if `SetActiveConfiguration` is not exposed on the Core host.

**Suggested matrix args:** `{ "configuration_name": "Default" }`

---

### `sync_filesystem` — _ProjectSettings_

**Source:** [`MendixAdditionalTools.cs:3961`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** Always returns `escalation: manual` — `IAppService.SynchronizeWithFileSystem` not exposed on typed Interop. No parameters read.

**Suggested matrix args:** `{}`

---

## Family 15 — Navigation

### `manage_navigation` — _Navigation_

**Source:** [`MendixAdditionalTools.cs:3543`](../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs)

**Required:**
- `pages` (array of object): required when `action` is `"add"` (default); each `{ caption, page_name, module_name }`.

**Optional:**
- `action` (string, default `"add"`): only `"add"` is functional; `"list"`, `"remove"`, `"set_icon"`, `"set_target"` return `escalation: manual`.
- `profile` (string, default `"Responsive"`): navigation profile to add items to.

**Notes:** Each page entry requires `caption`, `page_name`, and `module_name`. Optional `icon` (string) per entry accepted. Only append is supported; list/remove/modify require Studio Pro UI.

**Suggested matrix args:** `{ "action": "add", "pages": [{ "caption": "Customers", "page_name": "Customer_Overview", "module_name": "MyFirstModule" }] }`

---

## Family 16 — UI Actions Lifecycle

### `save_all` — _UiActions_

**Source:** [`UiActionsBootstrap.cs:20`](../../src/Concord.Core/Mcp/UiActionsBootstrap.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read. Triggers Studio Pro's Save All action.

**Suggested matrix args:** `{}`

---

### `run_app` — _UiActions_

**Source:** [`UiActionsBootstrap.cs:18`](../../src/Concord.Core/Mcp/UiActionsBootstrap.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read. Triggers Studio Pro's Run App action.

**Suggested matrix args:** `{}`

---

### `stop_app` — _UiActions_

**Source:** [`UiActionsBootstrap.cs:19`](../../src/Concord.Core/Mcp/UiActionsBootstrap.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read. Triggers Studio Pro's Stop App action.

**Suggested matrix args:** `{}`

---

### `refresh_project` — _UiActions_

**Source:** [`UiActionsBootstrap.cs:21`](../../src/Concord.Core/Mcp/UiActionsBootstrap.cs)

**Required:**
- (none)

**Optional:**
- (none)

**Notes:** No parameters read. Triggers Studio Pro's Refresh Project action.

**Suggested matrix args:** `{}`

---
