---
name: mendix-microflow
description: Step-by-step guide for creating and populating Mendix microflows using MCP tools. Covers creation, parameters, activities, expressions, and verification.
---

# Building a Mendix Microflow

Follow these steps in order to avoid common sequencing mistakes.

## Step 1 — Check what already exists

```
list_microflows   module_name=<module>
```

If the microflow already exists, use `read_microflow_details` to inspect it before modifying.

## Step 2 — Create the microflow

```
create_microflow
  name = "ACT_Order_Process"          ← use "name" NOT "microflow_name" (alias accepted)
  module_name = "OrderModule"
  return_type = "Boolean"             ← optional: String, Integer, Boolean, Decimal, DateTime, Void, Object, List
  return_entity = "Order"             ← required only when return_type is Object or List
  return_expression = "$ProcessResult" ← optional: set the end-event expression directly
```

**Naming conventions:**
- Actions: `ACT_<Entity>_<Verb>` (e.g., `ACT_Order_Create`)
- Sub-microflows: `SUB_<description>`
- Rules: `RUL_<description>`
- Before/After commit: `BCO_<Entity>` / `ACO_<Entity>`

**IMPORTANT — Two-step return type:**
If `return_type` is `Object` or `List`, the return type is NOT applied during creation. After creating, call:
```
update_microflow  microflow_name=<name>  module_name=<module>  return_type=Object  return_entity=<entity>
```

## Step 3 — Add parameters (if needed)

Parameters are specified in the `parameters` array on `create_microflow`:
```json
{
  "name": "ACT_Order_Calculate",
  "module_name": "OrderModule",
  "parameters": [
    { "name": "Order", "type": "Object", "entity": "OrderModule.Order" },
    { "name": "Quantity", "type": "Integer" }
  ]
}
```

## Step 4 — Add activities

Use `create_microflow_activities` with the `activities` array.

```json
{
  "microflow_name": "ACT_Order_Calculate",
  "module_name": "OrderModule",
  "activities": [
    {
      "activity_type": "create_object",
      "activity_config": {
        "entity": "OrderModule.OrderLine",
        "variable_name": "NewOrderLine",
        "commit": "No"
      }
    },
    {
      "activity_type": "change_attribute",
      "activity_config": {
        "variable": "NewOrderLine",
        "attribute": "Quantity",
        "value": "$Quantity"
      }
    },
    {
      "activity_type": "commit",
      "activity_config": {
        "variable": "NewOrderLine",
        "with_events": true
      }
    }
  ]
}
```

### Supported activity_type values

| Type | Description |
|------|-------------|
| `create_object` | Create a new Mendix object (also: `create_variable`, `create`) |
| `change_attribute` | Change an attribute value on an object |
| `change_association` | Set/clear an association on an object |
| `change_object` | Change multiple attributes/associations at once |
| `commit` | Commit object(s) to the database |
| `rollback` | Rollback object (discard changes) |
| `delete` | Delete an object |
| `retrieve_from_database` | Retrieve objects via XPath from DB (also: `retrieve`, `retrieve_database`) |
| `retrieve_by_association` | Retrieve via association (also: `association_retrieve`) |
| `microflow_call` | Call another microflow |
| `show_message` | Show a dialog to the user |
| `create_list` | Create an empty list |
| `change_list` | Add/remove items in a list |
| `sort_list` | Sort a list |
| `filter_list` | Filter a list |
| `find_in_list` | Find item(s) in a list |
| `aggregate_list` | Aggregate (count, sum, max, min, avg) |

### Expression syntax rules

- String literals: **single quotes** — `'Hello World'` (NOT double quotes)
- Variable reference: `$VariableName` (with `$` prefix in expressions)
- Empty check: `$Variable = empty` or `$Variable != empty`
- Attribute access: `$Variable/AttributeName`
- Boolean: `true` or `false` (lowercase)
- Double quotes in expression values are **auto-converted to single quotes** as a convenience

### CommitEnum values (case-sensitive)

| Value | Meaning |
|-------|---------|
| `Yes` | Commit with events (triggers before/after commit handlers) |
| `YesWithoutEvents` | Commit without triggering events |
| `No` | Do not commit (default) |

## Step 5 — Fix output variable names (if needed)

**IMPORTANT:** The `output_variable` parameter is ignored during activity creation. If the response contains `warnings` about `output_variable`, rename the variable after creation:

```
modify_microflow_activity
  microflow_name = <name>
  module_name = <module>
  activity_index = <1-based index>
  changes = { "output_variable": "MyVariableName" }
```

## Step 6 — Verify

```
read_microflow_details  microflow_name=<name>  module_name=<module>
```

Check the `activities` array to confirm everything is in the correct order and has the right configuration.

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Using `microflow_name` instead of `name` for create_microflow | Now accepted as alias |
| `return_type=Object` not applied | Call `update_microflow` after create to set it |
| `output_variable` ignored | Use `modify_microflow_activity` post-creation |
| String literals with double quotes | Use single quotes in expressions: `'text'` |
| `$` prefix in API params (e.g., retrieve source variable) | API params take raw name without `$` — the `$` is only for expressions |
| CommitEnum `"yes"` lowercase | Must be `"Yes"` (capital Y) |
| Activities in wrong order | create_microflow_activities inserts in reverse — the first in the array ends up first in the microflow |

## Deleting a microflow

Use `delete_document` (NOT `delete_model_element`):
```
delete_document  document_name=<name>  module_name=<module>  document_type=microflow
```
