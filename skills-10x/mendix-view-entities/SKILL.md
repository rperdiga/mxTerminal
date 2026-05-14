---
name: mendix-view-entities
description: Use when the user asks about view entities, OQL queries, or view-entity inspection on Studio Pro 10.24.13–11.9.x. Documents what the agent can and cannot do on this version — read-only inspection via query_model_elements and read_domain_model; no automated OQL authoring tools. Trigger when the user mentions a view entity, OQL, or describes a read-only query over the domain model.
---

## Tools in this environment

This skill is for **Studio Pro 10.24.13–11.9.x** (concord-mcp tool surface). The Mendix studio-pro MCP server and Maia are **not available** on this version.

Tools relevant to view entities on 10.x:

- `mcp__concord-mcp__query_model_elements` — query model elements by type, including entities and their source types. Use to enumerate view entities in a module.
- `mcp__concord-mcp__read_domain_model` — read the full domain model for a module, including entity source types.
- `mcp__concord-mcp__read_attribute_details` — read attribute details for a specific entity.
- `mcp__concord-mcp__query_associations` — query associations related to an entity.
- `mcp__concord-mcp__check_project_errors` — validate project consistency.
- `mcp__concord-mcp__check_model` — run model-level consistency checks.

**What is NOT available on 10.x:**

- `oql_read` and `oql_generate` — these are studio-pro MCP tools, not in the concord-mcp surface on 10.x.
- `ped_create_document`, `ped_update_document`, `ped_find_document`, `ped_check_errors` — studio-pro MCP tools, not available.
- Automated view entity authoring or OQL generation.

---

# View Entities on Studio Pro 10.x

## Scope Limitation

**View-entity authoring on 10.x requires Studio Pro's native UI.** Concord can read view-entity definitions via `mcp__concord-mcp__query_model_elements` and `mcp__concord-mcp__read_domain_model`, but does not expose dedicated OQL authoring tools on this version.

What the agent can do:

- Inspect whether view entities exist in a module.
- Read entity source type to identify a view entity vs. a regular entity.
- Read attribute lists on a view entity (attributes derived from OQL SELECT columns, as stored in the model).
- Query associations involving view entities.
- Explain OQL syntax and help the user write OQL to paste into Studio Pro's OQL editor manually.

What the agent cannot do on 10.x:

- Create a `DomainModels$ViewEntitySourceDocument` — no `ped_create_document` available.
- Write or update OQL programmatically — no `oql_generate` available.
- Read the raw OQL string from an existing view entity — no `oql_read` available.

---

## Identifying View Entities

Use `mcp__concord-mcp__query_model_elements` to list entities and filter for view entities:

```
mcp__concord-mcp__query_model_elements
  elementType: DomainModels$Entity
  moduleName: <ModuleName>
```

A view entity has `source.$Type = "DomainModels$OqlViewEntitySource"`. Any entity without this field (or with a different source type) is a regular entity.

Alternatively, use `mcp__concord-mcp__read_domain_model` to read the full domain model and inspect all entity source types at once:

```
mcp__concord-mcp__read_domain_model
  moduleName: <ModuleName>
```

---

## OQL Reference — For Manual Authoring in Studio Pro

The agent can help you author the correct OQL to paste into Studio Pro's view entity editor. Use the reference below to construct valid OQL.

### What OQL Is

OQL (Object Query Language) is Mendix's SQL-like query language for reading data from domain model entities. It uses the same keywords as SQL (`SELECT`, `FROM`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`) but operates on Mendix entities and associations instead of database tables.

Key differences from SQL:

- Entity names use module-qualified notation: `Module.Entity` (e.g., `MyModule.Order`).
- Association traversal replaces explicit joins: `Module.Entity/Module.AssociationName/Module.TargetEntity`.
- OQL is read-only — it cannot insert, update, or delete data.
- View entities require OQL v2 to be enabled in app settings.
- Non-persistable and external entities cannot be used in OQL queries.
- Attribute aliases are **mandatory** in view entity queries.

### Basic Structure

```sql
SELECT [DISTINCT] { * | { <expression> [AS <alias>] } [, ...] }
FROM Module.Entity [AS alias]
[INNER JOIN | LEFT [OUTER] JOIN | RIGHT [OUTER] JOIN | FULL [OUTER] JOIN
    Module.Entity/Module.AssocName/Module.TargetEntity [AS alias]]
[WHERE <condition>]
[GROUP BY <expr>[, ...]]
[HAVING <condition>]    -- only valid with GROUP BY
[ORDER BY <expr> [ASC|DESC][, ...]]   -- only valid with LIMIT or OFFSET in view entities
[LIMIT <n>]
[OFFSET <n>]
```

- `SELECT` and `FROM` are mandatory.
- In view entities, `ORDER BY` is only valid when `LIMIT` or `OFFSET` is also present.

### Entity and Attribute Names

- Use `Module.Entity` notation (e.g., `MyModule.Order`).
- Attribute paths use `/`: `MyModule.Order/TotalAmount`.
- Names are **case-sensitive** — match the domain model exactly.
- Reserved words must be quoted with `" "` (e.g., `"Order"`).

### Association Traversal

In `JOIN` — bring in a related entity:

```sql
FROM MyModule.Order AS o
LEFT JOIN MyModule.Order/MyModule.Order_Customer/MyModule.Customer AS c
    ON ...
```

Pattern: `Module.SourceEntity/Module.AssociationName/Module.TargetEntity`

In `SELECT` / `WHERE` — access an attribute across an association:

```sql
SELECT MyModule.Order/MyModule.Order_Customer/MyModule.Customer/Name AS customerName
FROM MyModule.Order
```

### Division

Use `:` for division (not `/`): `TotalAmount : Quantity`.

### System Variables

Wrap in single quotes: `'[%CurrentDateTime%]'`, `'[%CurrentUser%]'`.

### NULL Handling

If the `WHERE` expression evaluates to `NULL`, the row is excluded. Use `IS NULL` / `IS NOT NULL` for null checks.

### Scalar Functions

`CAST`, `COALESCE`, `DATEDIFF`, `DATEPART`, `LENGTH`, `LOWER`, `UPPER`, `REPLACE`, `ROUND`

### Aggregate Functions

`COUNT`, `AVG`, `MAX`, `MIN`, `SUM`

### UNION

```sql
SELECT ... FROM ...
UNION [ALL]
SELECT ... FROM ...
```

### Constraints

- Cross-module OQL is not supported. All referenced entities must be in the same module.
- Non-persistable and external entities cannot be referenced.

---

## Workflow for View Entity Work on 10.x

1. **Inspect** existing view entities with `mcp__concord-mcp__query_model_elements` or `mcp__concord-mcp__read_domain_model`.
2. **Draft OQL** in collaboration with the user using the syntax reference above.
3. **Tell the user** to create or update the view entity in Studio Pro:
   - Create: Domain model → right-click → Add view entity → paste OQL in the OQL editor.
   - Update: Open the view entity in the domain model → OQL editor → replace the OQL string.
4. **After the user confirms** the change is saved, call `mcp__concord-mcp__check_project_errors` to surface any OQL validation issues that Studio Pro reports.

Do NOT attempt to create or modify a `DomainModels$ViewEntitySourceDocument` via concord-mcp tools on 10.x — none of the mutation tools exposed on this version support OQL document writes.

## Critical Constraints

⚠️ **OQL authoring on 10.x is a human step** — the agent drafts OQL; the user applies it in Studio Pro.
⚠️ **Always verify attribute and association names** against the domain model before drafting OQL — case-sensitive mismatches are silent until model check.
⚠️ **Identify the correct FROM entity first** — "all X with their Y" means X is the FROM entity, not Y.
⚠️ **Attribute aliases are mandatory** in view entity SELECT columns.
⚠️ **ORDER BY requires LIMIT or OFFSET** in view entities.
