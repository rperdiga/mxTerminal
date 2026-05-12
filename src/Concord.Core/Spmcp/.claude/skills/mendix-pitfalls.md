---
name: mendix-pitfalls
description: Quick reference card for known Mendix MCP tool gotchas — parameter names, deletion rules, values, and sequencing traps.
---

# Mendix MCP Tool — Pitfalls Quick Reference

## Parameter Name Cheat Sheet

These aliases are now accepted, but use the canonical name when possible:

| Tool | Canonical param | Also accepted |
|------|----------------|---------------|
| `create_microflow` | `name` | `microflow_name` |
| `create_enumeration` | `name` | `enumeration_name` |
| `create_association` | `parent` | `parent_entity`, `from_entity` |
| `create_association` | `child` | `child_entity`, `to_entity` |
| `create_association` | `name` | `association_name` |
| `create_microflow_activities` | `activity_type` | `type` |
| `configure_constant_values` | `module_name` + `constant_name` | `"Module.ConstantName"` qualified format |

## Deletion — Use the RIGHT Tool

| Element | Correct tool | WRONG tool |
|---------|-------------|------------|
| Entity | `delete_model_element` type=entity | — |
| Attribute | `delete_model_element` type=attribute | — |
| Association | `delete_model_element` type=association (needs BOTH entity_name AND association_name) | — |
| Microflow | `delete_document` document_type=microflow | ~~delete_model_element~~ |
| Page | `delete_document` document_type=page | — |
| Enumeration | `delete_model_element` type=enumeration | — |
| Constant | `delete_model_element` type=constant | — |
| Nanoflow | **CANNOT DELETE via API** — do it manually in Studio Pro | — |
| Module | **CANNOT DELETE via API** — do it manually in Studio Pro | — |

## CommitEnum Values (case-sensitive!)

| Value | Meaning |
|-------|---------|
| `Yes` | Commit + trigger before/after commit events |
| `YesWithoutEvents` | Commit without triggering events |
| `No` | Don't commit (default) |

❌ Wrong: `"yes"`, `"YES"`, `"yes_without_events"`, `"commit"`
✅ Correct: `"Yes"`, `"YesWithoutEvents"`, `"No"`

## Variable Names in API vs Expressions

| Context | Format | Example |
|---------|--------|---------|
| Expression value (XPath, change value) | With `$` prefix | `$Order`, `$Order/TotalAmount` |
| API parameter (variable_name, change_variable, etc.) | WITHOUT `$` prefix | `Order`, `NewCustomer` |

❌ Wrong: `{ "variable": "$Order" }` in activity_config
✅ Correct: `{ "variable": "Order" }` in activity_config, `$Order` in expression values

## Sequence-Sensitive Operations

These require a specific sequence — don't try to do it in one call:

1. **Microflow with Object/List return type:**
   - `create_microflow` → then `update_microflow` (with return_type + return_entity)

2. **Renaming activity output variable:**
   - `create_microflow_activities` → then `modify_microflow_activity` (to set output_variable)
   - If response contains `warnings` about `output_variable`, you MUST do this step

3. **Sample data pipeline:**
   - `generate_sample_data` (handles bootstrap automatically unless `auto_setup=false`)
   - OR: `generate_sample_data auto_setup=false` → then `setup_data_import` manually

4. **Entity with generalization:**
   - `create_entity` → then `set_entity_generalization`

## Association Direction (Confusing!)

In Mendix terminology (confusingly inconsistent with the API):
- **parent** in our API = the "one" side (e.g., one Order → many OrderLines → Order is `parent`)
- **child** in our API = the "many" side (OrderLine is `child`)

Think of it as: parent entity owns the children.

## Untyped Model — Read-Only!

Tools that use the untyped model (`read_security_info`, `read_entity_access_rules`, `audit_security`, `query_model_elements`) are **READ-ONLY**. Security settings cannot be written via MCP.

## IPageGenerationService Warning

`generate_overview_pages` creates broken bindings for:
- Enumeration attributes → CE1613 consistency errors
- Association attributes → CE1613 consistency errors

Use it only for simple String/Integer/Boolean entities, or fix the bindings manually afterward.

## Mendix Reserved Words — NEVER Use These as Entity/Attribute Names

Using any of these causes consistency errors (CE) that Studio Pro shows but `check_project_errors` / `check_model` may NOT catch.

### Mendix System Keywords (case-insensitive)
```
changedby  changeddate  createddate  currentUser  empty  false  guid  id
MendixObject  object  owner  submetaobjectname  true  type  context  com  con
```

### Java Language Keywords
```
abstract  assert  boolean  break  byte  case  catch  char  class  const
continue  default  do  double  else  enum  extends  final  finally  float
for  goto  if  implements  import  instanceof  int  interface  long  native
new  null  package  private  protected  public  return  short  static  strictfp
super  switch  synchronized  this  throw  throws  transient  try  void  volatile  while
```

### High-Risk Names to Avoid
- `id` — every entity already has a system ID
- `type` — system-reserved
- `object` — system-reserved
- `owner` — system attribute name
- `changedBy` / `ChangedBy` — system attribute name
- `createdDate` / `CreatedDate` — **system attribute name** (see below)
- `changedDate` / `ChangedDate` — **system attribute name** (see below)

### CreatedDate / ChangedDate — Use System Attributes Instead!

❌ **WRONG**: `add_attribute name=CreatedDate type=DateTime` → CE error, conflicts with Mendix system attribute
✅ **CORRECT**: Use `configure_system_attributes` to enable tracking on the entity:
```
configure_system_attributes  entity_name=Customer  module_name=MyFirstModule
  has_created_date=true  has_changed_date=true  has_owner=true  has_changed_by=true
```
This adds the system-managed audit fields without the naming conflict.

## Always Call These First

Before building anything in a module, call:
```
list_modules          → know what user modules exist
read_domain_model     → know what entities/associations already exist
```

Before creating a microflow:
```
list_microflows  module_name=<module>   → avoid duplicate creation errors
```
