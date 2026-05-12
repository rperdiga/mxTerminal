---
name: mendix-project-context
description: Auto-generated project conventions from 'MCPExtension' on 2026-03-18 19:59. Load before building anything in this project.
---

# Project Conventions: MCPExtension
*Generated: 2026-03-18 19:59 — 4 entities, 9 microflows, 1 pages*

## Naming Conventions

**Entities:** PascalCase (100% consistent) — e.g. Customer, Product, SalesOrder, SalesOrderLine
**Attributes:** PascalCase (100% consistent)

**Microflow prefix patterns:**
  - `ACT_` — 7 microflows (77%) → main action microflows
  - `SUB_` — 2 microflows (22%) → sub-microflows (called by other microflows)

## Standard Patterns

**Standard audit tracking** (present on most entities): CreatedDate, ChangedDate, IsActive
⚠️ `CreatedDate` and `ChangedDate` are Mendix reserved names — enable via `configure_system_attributes` (NOT `add_attribute`)!
Add `IsActive` as a Boolean attribute manually (this one is safe).
**Default base entity:** `System.Object` (used by 4/4 entities)
**Associations:** 100% one-to-many (no many-to-many)
**Most common delete behavior:** `DeleteMeButKeepReferences`

## Module Structure

| Module | Entities | Associations | Microflows | Pages |
|--------|----------|--------------|------------|-------|
| MyFirstModule | 4 | 3 | 9 | 1 |

## Attribute Type Distribution

  - DateTime: 8 (30%)
  - String: 7 (26%)
  - Decimal: 5 (19%)
  - Boolean: 2 (7%)
  - Integer/Long: 2 (7%)
  - Enumeration: 2 (7%)

## Apply These Conventions

When creating anything new in this project:
1. **Names:** Use PascalCase for entities and attributes
2. **Microflows:** Prefix with `ACT_<Entity>_<Verb>` for actions, `SUB_` for sub-flows, `BCO_`/`ACO_` for event handlers
3. **Audit fields:** Call `configure_system_attributes` (has_created_date=true, has_changed_date=true) + add `IsActive` (Boolean) via `add_attribute`
5. **Associations:** Default to one-to-many with `DeleteMeButKeepReferences` delete behavior
6. **Before building:** Call `list_modules` + `read_domain_model` to see existing state
