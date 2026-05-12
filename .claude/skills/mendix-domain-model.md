---
name: mendix-domain-model
description: Step-by-step guide for building a Mendix domain model correctly using MCP tools. Covers entities, attributes, associations, and layout.
---

# Building a Mendix Domain Model

Follow these steps in order to avoid common mistakes.

## Step 1 — Discover the project state first

Always start by calling these two tools before creating anything:

```
list_modules          → see all modules (user + Marketplace)
read_domain_model     → see existing entities and associations in target module
```

**Never skip this.** Creating a duplicate entity or association will fail silently or produce an error.

## Step 2 — Create entities

Use `create_entity` for a single entity, or `create_multiple_entities` for bulk creation.

**Key parameters:**
- `entity_name` — required, PascalCase (e.g., `Customer`, `OrderLine`)
- `module_name` — required unless there is only one user module

**Entity types** (optional `entity_type` param):
- `persistable` (default) — stored in database
- `non_persistable` — in-memory only (e.g., helper/DTO objects)
- `external` — OData/external source

## Step 3 — Add attributes

Use `add_attribute` for each attribute.

**Supported types:** `String`, `Integer`, `Long`, `Decimal`, `Boolean`, `DateTime`, `AutoNumber`, `Binary`, `HashedString`

**For enumerations:**
- Reference an existing enum: `attribute_type = "Enumeration:StatusEnum"` (qualified with module if cross-module: `"Enumeration:MyModule.StatusEnum"`)
- Or create a new enum inline: `attribute_type = "Enumeration"` + `enumeration_values = ["Active", "Inactive"]`

## Step 4 — Create associations

Use `create_association` to link entities.

**Key parameters:**
| Parameter | Description |
|-----------|-------------|
| `name` | Association name (also accepted: `association_name`) |
| `parent` | The "one" side entity — e.g., `Order` (also accepted: `parent_entity`) |
| `child` | The "many" side entity — e.g., `OrderLine` (also accepted: `child_entity`) |
| `type` | `one-to-many` (default) or `many-to-many` |
| `module_name` | Module where the association is created |

**CRITICAL direction rule:**
- `parent` = the entity on the "one" side (one Order has many OrderLines → Order is parent)
- `child` = the entity on the "many" side (OrderLine is child)
- Mendix internally calls this "Parent=owner/many-side, Child=one-side" which is confusing — trust the description above

**Delete behaviors:**
- `parent_delete_behavior`: `delete_me_and_references` (cascade) / `delete_me_but_keep_references` (default) / `delete_me_if_no_references`
- `child_delete_behavior`: same options

## Step 5 — Arrange layout (optional but recommended)

After creating entities and associations, call:
```
arrange_domain_model  module_name=<module>
```
This positions entities nicely on the canvas using association-aware hierarchical layout.

## Step 6 — Verify

Call `read_domain_model` again to confirm everything was created correctly.

## Reserved Words — NEVER Use These as Names

Mendix will show CE errors in Studio Pro for reserved words — and `check_project_errors` may NOT catch them.

**Mendix system keywords (case-insensitive):** `id`, `type`, `object`, `owner`, `changedBy`, `createdDate`, `changedDate`, `guid`, `true`, `false`, `empty`, `null`, `context`, `currentUser`, `MendixObject`, `submetaobjectname`

**Java keywords:** `class`, `new`, `return`, `int`, `long`, `double`, `float`, `boolean`, `char`, `void`, `static`, `final`, `public`, `private`, `protected`, `import`, `package`, `interface`, `enum`, `extends`, `implements`, `super`, `this`, `if`, `else`, `for`, `while`, `do`, `switch`, `case`, `break`, `continue`, `try`, `catch`, `throw`, `throws`, `finally`, `null`, `true`, `false`, `abstract`, `native`, `volatile`, `transient`, `synchronized`, `instanceof`

**Safe alternatives for common problematic names:**
- `id` → use `ExternalId`, `ReferenceCode`, `DocumentNumber`
- `type` → use `OrderType`, `CustomerCategory`, `RecordKind`
- `status` → `status` itself is OK, but double-check — use `OrderStatus`, `ProcessingStatus` to be safe
- `createdDate` / `changedDate` → **use system attributes instead** (see below)
- `owner` → use `AssignedTo`, `AccountOwner`, `ResponsibleUser`

## Audit Fields — Use System Attributes, NOT Manual Attributes

❌ **WRONG**: Adding `CreatedDate` / `ChangedDate` / `Owner` as regular attributes via `add_attribute`
These are Mendix reserved system attribute names → **7 CE errors guaranteed**

✅ **CORRECT**: Enable them via entity system configuration:
```
configure_system_attributes  entity_name=Customer  module_name=MyFirstModule
  has_created_date=true  has_changed_date=true  has_owner=true  has_changed_by=true
```

Do this for every entity that needs audit tracking. The system manages these automatically.

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Using `parent_entity` / `child_entity` | Now accepted as aliases for `parent` / `child` |
| Using `association_name` instead of `name` | Now accepted as alias |
| Using `enumeration_name` for create_enumeration name | Now accepted as alias for `name` |
| Forgetting `module_name` | Always specify it explicitly — default fallback may pick wrong module |
| Creating entity before its module exists | Call `create_module` first if the module doesn't exist |
| Cross-module association | Qualify entity names: `parent = "ModuleA.Order"`, `child = "ModuleB.Product"` |
| Adding `CreatedDate`/`ChangedDate` as attributes | Use `configure_system_attributes` instead |
| Using Java or Mendix reserved words as names | See reserved words list above |

## Entity Generalization (Inheritance)

To make `Child` inherit from `Parent`:
```
set_entity_generalization  entity_name=Child  parent_entity=Parent
```
Common base: `System.FileDocument` (for file storage), `System.Image` (for images).
