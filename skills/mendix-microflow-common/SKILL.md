---
name: mendix-microflow-common
description: Use BEFORE creating any new Mendix microflow. Covers the pre-creation planning checklist (trace flow paths, verify gateway rules, check loop boundaries, identify annotation points), naming conventions (ACT_/CAL_/VAL_/OCH_/DS_/SUB_ prefixes), layout rules (parameters above start event, 200px horizontal / 100px vertical spacing, decision split conventions, error handler placement), the loop performance anti-patterns (no Show Message / Commit / Retrieve / Delete / REST / aggregate inside a loop), the XPath-vs-Expressions distinction, and variable scope rules. Trigger when the user describes a new microflow they want to create.
---

## Tools in this environment

- `ped_*` (e.g. `ped_create_document`, `ped_read_document`, `ped_update_document`) → `mcp__mendix-studio-pro__ped_*` (preferred — operate via the Studio Pro model API).

The skill body uses the short names inline. This header tells you which actual MCP tool to call.

# Microflow Creation - MANDATORY Pre-Creation Checklist

Before creating a microflow model, you MUST:

1. **Trace all flow paths on paper/mentally**:
    - List each possible path from Start to End
    - Count how many paths lead to each End Event
    - **Critical**: If ANY End Event or Action Activity has >1 incoming path → plan a Merge

2. **Verify gateway rules**:
    - ExclusiveSplit: Does EVERY possible case value have a flow?
    - InheritanceSplit: Is there a flow for EACH subtype AND the base type AND empty case ("")?
    - End Events: Does each have EXACTLY one incoming flow?

3. **Check loop boundaries**:
    - Are all flows between loop-internal objects staying within the loop?
    - Are no flows crossing between /objects and /objects/N/objects?

4. **Validate the flow structure** before building JSON:
    - Draw or list the flow connections
    - Verify connection indices (0=top, 1=right, 2=bottom, 3=left)
    - Confirm main flow goes left-to-right, errors go top-to-bottom

5. **Identify annotation points**. Add an annotation when:
    - A split condition references a business threshold (e.g., hardcoded values, configurable limits) → explain what the threshold represents and why
    - A split encodes a business rule not derivable from variable names alone → describe the business intent
    - A calculation or transformation involves more than one step or uses non-standard logic → explain what is being computed and why
    - An action has a side effect that isn't immediately visible (e.g., calling an external service, modifying unrelated data)

    Annotation content should answer: **"Why is this done this way?"**, not just "What does this do?"

**CRITICAL**: STOP and check if all of the above steps have been completed before proceeding. Only create the microflow after confirming that the flow structure is sound and all edge cases are accounted for. This upfront planning is essential to avoid common pitfalls and ensure a robust microflow design.

# Microflow Naming Conventions

This skill enables you to create properly named microflows following Mendix best practices. Consistent naming improves code maintainability and helps developers quickly understand a microflow's purpose and trigger.

## Naming Pattern

**Format**: `{PREFIX}_{Entity/Purpose}_{Operation}`

**Example**: `ACT_Vendor_StartWorkflow`

- **PREFIX**: Indicates the event type or trigger (see categories below)
- **Entity/Purpose**: The domain entity or general purpose
- **Operation**: The specific action being performed

**Prefixes**

- ACT\_{Purpose} - Action button/menu item
- CAL\_{Entity}\_{Attribute} - Calculated attribute
- VAL\_{Entity}\_{Attribute} - Validation
- OCH\_{Purpose} - On change (input elements)
- DS\_{Purpose} - Data source (list/grid/view)
- SUB\_{Purpose} - Sub-microflow
- TEST\_{Purpose} or UT\_{Purpose} - Unit test

## Common Patterns

- **Calculated attributes**: `CAL_Customer_FullName`, `CAL_Order_Total`
- **Validation**: `VAL_Product_Price`, `VAL_Customer_Email`
- **Button actions**: `ACT_Customer_Export`, `ACT_Order_Calculate`
- **Data sources**: `DS_Customer_ActiveList`, `DS_Product_FilteredGrid`
- **Input events**: `OCH_SearchTerm_Filter`
- **Sub-microflows**: `SUB_Email_FormatAddress`, `SUB_Date_CalculateAge`

## Anti-Patterns (DO NOT USE)

❌ **Generic names without prefixes**: `ProcessOrder`, `ValidateData`
❌ **Inconsistent casing**: `act_customer_create`, `ACT_Customer_Create` in same module
❌ **Wrong prefix for event type**: Using `ACT_` for a before-commit event
❌ **Overly long names**: `ACT_Customer_ValidateAndProcessAndSendEmailAndUpdateStatus`
❌ **Abbreviations without context**: `ACT_Cust_Proc` (unclear what "Proc" means)
❌ **Missing entity/purpose**: `ACT_Process`, `SUB_Calculate`

## Best Practices

✅ **Be specific**: `ACT_Order_CalculateTotal` is better than `ACT_Order_Calculate`
✅ **Use consistent terminology**: If you use "Create" in one place, don't use "New" elsewhere
✅ **Keep operations descriptive**: Use action verbs like Create, Update, Delete, Validate, Calculate, Send
✅ **Include entity name**: Makes it clear what data the microflow operates on
✅ **Group related microflows**: Similar prefixes help organize in the App Explorer

## Critical Constraints

⚠️ **Always use the appropriate prefix** for the microflow's trigger or purpose
⚠️ **Maintain consistency** within a module and across the application
⚠️ **Use underscores as separators**, not camelCase or spaces
⚠️ **Keep names concise but descriptive** - aim for clarity over brevity
⚠️ **Match the prefix to the actual event** - using wrong prefixes leads to confusion

# Mendix Microflow Layout Rules

This skill enables you to create well-structured, readable microflow diagrams following Mendix layout conventions. Microflows are visual business logic diagrams with activities, events, splits, and parameters connected by flows.

## Core Layout Principles

- **Primary flow direction**: Left-to-right (happy path/main flow)
- **Alternative flows**: Top-to-bottom (error handling, edge cases)
- **Grid alignment**: All elements snap to grid for consistency
- **Consistent spacing**: Maintain uniform distances between elements
- **Logical grouping**: Related activities positioned close together
- **Readability first**: Avoid crossing flows when possible

## Parameter Layout

Parameters are input values passed into a microflow (pentagon/house shapes pointing right).

**Positioning Rules:**

- **CRITICAL**: Position **directly above** the Start Event
- **CRITICAL**: Stack vertically with **100 pixels spacing** on the y-axis
- **Stack order**: Newest parameters go on top

**Example:**

```
[Parameter 3]  ← newest
(+100 px vertical space)
[Parameter 2]
(+100 px vertical space)
[Parameter 1]
(+100 px vertical space)
[Start Event]
```

## Activity Spacing

**Horizontal spacing** (left-to-right flow):

- **Standard activities**: 200 pixels between activity centers
- **After decisions**: Position branches with adequate separation

**Vertical spacing** (alternative flows):

- **Standard activities**: 100 pixels between activity centers
- **Split paths**: 100 pixels between parallel branches
- **Error handlers**: Position below main flow with clear separation

## Decision Split Layout

**For exclusive splits (XOR/Decision)**:

- **True/Yes path**: Continue to the right (main flow)
- **False/No path**: Branch downward (alternative)
- **Label connections** clearly
- **Merge paths** when logic reconverges

**Example:**

```
[Activity] → [Decision]
                 ↓ (false)
              [Alt Path]
                 ↓
             → [Merge] → [Continue path]
```

**For inheritance splits**:

- Position subtype branches on the right side of the split
- Stack subtype branches vertically with 130 pixels spacing

**Example:**

```
[Activity] → [Inheritance Split] ┬------→ [Activity]
                                 │  (Module.BaseType)
                                 │
                                 ├------→ [Activity]
                                 │  (Module.Subtype1)
                                 │
                                 └------→ [Activity]
                                    (empty case)
```

## Error Handling Layout

- **Error handlers** position below the main flow
- Use **consistent vertical offset** 200 pixels down
- **Connect error paths** to appropriate end events or recovery logic
- Keep error handling **visually distinct** from happy path

## Common Patterns

### Microflow with parameter

- **CRITICAL**: Parameters have NO flow connections - they are scope declarations, not execution steps.

BAD:

```
[Start] → [Parameter] → [Activity]
```

Good:

```
[Parameter (above)]
[Start] → [Activity]
```

### Linear Flow (Simple)

```
[Start] → [Retrieve] → [Change] → [Commit] → [End]
```

### Conditional Flow with Merge

```
[Start] → [Decision] → [Process A] [Merge] → [End]
            ↓                        ↑
          [Process B] ---------------┘
```

### Conditional Flow without Merge

```
[Start] → [Decision] → [Process A] → [End]
            ↓
          [Process B]
            ↓
          [End]
```

### Loop Pattern

```
[Start] → [Retrieve List] → [Loop [Process Sub steps]] → [End]
```

### Error Handling Pattern

```
[Start] → [Activity (with error handler)] → (success) [End]
                   ↓ (error)
               [Log Error]
                   ↓
               [End Error]
```

## Flows

## Annotation Layout

**CRITICAL: Annotations connect using AnnotationFlow, NOT SequenceFlow**

- AnnotationFlows are purely visual documentation
- They do NOT affect microflow execution
- Always check the flow type before adding to /flows array

### Flow types

CRITICAL: Annotations use AnnotationFlow ONLY, never SequenceFlow. AnnotationFlows are purely visual and don't affect execution.

### Direction

When objects are on the same axis i.e. the share the same x or y value, ensure the origin index and destination index align depending on the direction of the flow.

**Horizontal**

```
[Activity] --------------→ [Activity]
(origin index right) (destination index left)
```

**Vertical**

```
[Activity] (origin index bottom)
    ↓
[Activity] (destination index top)
```

## Annotations

Add annotations to microflows when logic becomes non-trivial to help future developers understand the flow:

**When to add annotations:**

- Before complex decision splits that evaluate business rules with multiple conditions
- Before decision splits that implement business rules with specific thresholds or values (e.g., $10,000 limit, 30-day window)
- Before loops that perform non-obvious operations or transformations
- At the start of error handling sections to explain recovery strategy
- Before microflow calls that are critical to understanding the overall logic
- To explain WHY a particular approach was chosen when it's not immediately obvious

**When NOT to add annotations:**

- For simple CRUD operations (create, retrieve, update, delete)
- For straightforward validation checks with obvious error messages
- For basic list operations with clear activity names
- When the activity names and flow structure are self-explanatory

**Annotation content guidelines:**

- Keep annotations brief (1-2 sentences max)
- Focus on WHY, not WHAT (the activities show what happens)
- Use plain language, avoid redundant technical jargon
- Place them just before the logic they describe

**Structure:** Annotations are `Microflows$Annotation` elements with `caption` (text content), `position`, and `size` properties.

## Best Practices

✅ **Minimize flow crossings**: Reorganize activities to avoid crossed lines
✅ **Keep related logic together**: Group activities that work on same data
✅ **Use annotations**: Add documentation for complex logic sections
✅ **Align elements**: Use grid snapping for professional appearance
✅ **Balance flow direction**: Don't overcomplicate vertical flows
✅ **Clear end events**: Every path should lead to an appropriate end event
✅ **Extract cohesive reusable logic**: When logic is cohesive and reusable, extract it into a separate Microflow and call it form our main Microflow.

## Anti-Patterns (DO NOT USE)

❌ **Spaghetti flows**: Multiple crossing lines that make logic hard to follow
❌ **Inconsistent spacing**: Random distances between elements
❌ **Backward flows**: Avoid right-to-left flows in main path
❌ **Crowded diagrams**: Too many activities without visual breathing room
❌ **Unlabeled decision paths**: Always label true/false or condition outcomes
❌ **Orphaned elements**: All activities must connect to flow

## Critical Constraints

⚠️ **Parameters MUST be positioned above Start Event** with 100px vertical spacing
⚠️ **Main flow direction is left-to-right** - maintain this convention
⚠️ **All paths must lead to an End Event** - no dangling flows
⚠️ **Use consistent spacing** throughout the microflow for readability
⚠️ **Align elements to grid** for professional appearance

# Performance Best Practices - MANDATORY RULES

**⚠️ NEVER generate inefficient microflow patterns listed below unless the user explicitly and specifically requests the inefficient pattern.**

## Loop Anti-Patterns — NEVER DO THESE

The following activities MUST NOT appear inside a loop body. Violations cause severe UX degradation or serious performance problems.

### ❌ NEVER: Show Message inside a loop

Placing a Show Message activity inside a loop causes a blocking dialog box to appear for EVERY iteration. The user must close each one individually before the next appears. This is unacceptable UX and must never be generated.

**WRONG:**

```
[Loop]
  → [Show Message: "Processed: $Item/Name"]   ← FORBIDDEN
```

**CORRECT:** Collect results and show a single summary message after the loop ends.

```
[Loop]
  → [Change Variable: $ResultMessage + $Item/Name + newline()]
→ (after loop) [Show Message: $ResultMessage]
```

---

### ❌ NEVER: Commit Object inside a loop

Committing individual objects one-by-one inside a loop triggers a separate database transaction per iteration. This is extremely inefficient and degrades performance proportionally to the list size.

**MANDATORY ENFORCEMENT RULE:**
**When creating any CreateObjectAction, ChangeObjectAction, or DeleteAction inside a LoopedActivity:**

1. **ALWAYS set `commit: "No"`** - NEVER set it to "Yes" or "YesWithoutEvents"
2. **ALWAYS add a CommitAction AFTER the loop** to batch-commit all changes
3. **For CreateObjectAction/ChangeObjectAction**: Create a list variable before the loop, add objects to it inside the loop, commit the list after
4. **For DeleteAction**: Use DeleteAction on the list after the loop, not inside

**WRONG:**

```
[Loop]
  → [Create Object: $NewItem, commit: "YesWithoutEvents"] ← FORBIDDEN
  → [Change Object: $Item, commit: "Yes"] ← FORBIDDEN
  → [Commit Object: $Item] ← FORBIDDEN
```

**CORRECT:**

```
[Before loop] [Create List: $CreatedItems]
[Loop]
  → [Create Object: $NewItem, commit: "No", outputVariableName: "NewItem"]
  → [Add to List: $CreatedItems, $NewItem]
[After Loop] [Commit List: $CreatedItems] ← single transaction
```

---

### ❌ NEVER: Retrieve from Database inside a loop

Retrieving objects from the database on every iteration causes N+1 query problems where N database calls replace what should be a single query. Performance degrades linearly with the number of iterations.

**WRONG:**

```
[Loop over $Orders]
  → [Retrieve $Order/Customer from database]   ← FORBIDDEN — 1 query per order
```

**CORRECT:** Retrieve all needed associated objects before the loop using a single XPath query with appropriate constraints.

```
[Before loop] [Retrieve all relevant Customers from database using XPath]
[Loop over $Orders]
  → [Find $Customer in pre-retrieved list using Find action]
```

---

### ❌ NEVER: Delete Object inside a loop

Deleting objects one-by-one inside a loop causes one database DELETE statement per iteration. Use bulk delete instead.

**WRONG:**

```
[Loop]
  → [Delete Object: $Item]   ← FORBIDDEN — one DELETE per iteration
```

**CORRECT:** Collect objects to delete in a list, then delete the entire list after the loop.

```
[Create List: $ItemsToDelete]
[Loop]
  → [Add to List: $ItemsToDelete, $Item]
→ (after loop) [Delete List: $ItemsToDelete]   ← single bulk delete
```

---

### ❌ NEVER: Call REST/Web Service or Send Email inside a loop

Calling external services or sending emails inside a loop makes one blocking network/SMTP call per iteration. This is slow, unreliable, and can exhaust external rate limits.

**WRONG:**

```
[Loop]
  → [Call REST Service: notify external system]   ← FORBIDDEN
  → [Send Email: confirmation to user]            ← FORBIDDEN
```

**CORRECT:** Collect the data needed for external calls during the loop, then process them after — or redesign to use a single batched API call.

---

### ❌ NEVER: Aggregate List inside a loop (when the list doesn't change)

Recomputing an aggregate (sum, count, min, max) on the same list on every iteration wastes CPU. Pre-compute it once before the loop.

**WRONG:**

```
[Loop]
  → [Aggregate: count($AllOrders)]   ← FORBIDDEN — recomputes same value each time
```

**CORRECT:** Compute the aggregate once before the loop and refer to the resulting variable inside the loop.

```
[Before loop] [Aggregate: count($AllOrders) → $OrderCount]
[Loop]
  → [use $OrderCount directly]
```

---

## General Performance Rules

✅ **Batch database operations**: Always prefer operating on lists over processing objects one-by-one.
✅ **Retrieve once, use many times**: Retrieve data before loops and reuse it; never re-retrieve the same data on each iteration.
✅ **Single commit after bulk changes**: Accumulate all object changes, then commit once outside the loop.
✅ **Minimize external I/O in loops**: Move REST calls, email sends, and sub-microflows with heavy I/O outside of loops.
✅ **Pre-compute invariants**: Compute any value that does not change across loop iterations before the loop.

## Critical Constraints

⚠️ **Show Message MUST NOT appear inside a loop** — breaks UX
⚠️ **Commit Object/List MUST NOT appear inside a loop** — use batch commit after the loop
⚠️ **Database Retrieve MUST NOT appear inside a loop** — retrieve before the loop with XPath
⚠️ **Delete Object MUST NOT appear inside a loop** — use Delete List after the loop
⚠️ **External service calls MUST NOT appear inside a loop** — batch or move outside

# XPath constraint vs Expressions - CRITICAL DISTINCTION

**⚠️ You MUST use the right syntax and skill for the right context.**

- Database query = XPath constraint with square brackets -> use `mendix-microflow-syntax` skill
- Everything else = Expressions without brackets -> use `mendix-microflow-syntax` skill

# Variable Scope in Microflows - CRITICAL RULE

**⚠️ CRITICAL: A variable is only accessible (in scope) in activities that are guaranteed to execute AFTER the activity that creates the variable.**

## Scope Rules

- Some activities that create variables are RetrieveAction, CreateObjectAction, CreateVariableAction, CreateListAction, AggregateListAction. MicroflowParameterObject is always in scope.
- A variable is IN SCOPE when if it is created before the split nodes that lead to the activity in question and there is a GUARANTEED flow path from the variable-creating activity to the activity in question.

## Common Error Patterns

- You remove a flow which cuts the activity that creates the variable from the rest of the flow, making the variable out of scope.
- You create a variable in one branch of an exclusive split

# Updating a microflow

CRITICAL: BEFORE ANY `mcp__mendix-studio-pro__ped_update_document` call on a microflow, you MUST ALWAYS read the `mendix-microflow-update` skill FIRST in the same session. This is a mandatory prerequisite - never update microflows without loading this skill.

## Diagnostics — concord-mcp fallbacks

When `mcp__mendix-studio-pro__ped_check_errors` does not surface enough context to diagnose a problem, reach for these concord-mcp tools as fallbacks (not replacements):

- `mcp__concord-mcp__check_model` — project-wide consistency check; surfaces errors across all documents, not just the one currently open.
- `mcp__concord-mcp__check_project_errors` — full error report including warnings and hints that `ped_check_errors` may omit.
- `mcp__concord-mcp__get_studio_pro_logs` — retrieves recent Studio Pro log output; useful for runtime or modeler-side errors that don't appear in model consistency results.
- `mcp__concord-mcp__get_last_error` — surfaces the most recent error recorded by Studio Pro; use when the modeler showed an error dialog that the agent did not observe directly.
- `mcp__concord-mcp__analyze_project_patterns` — heuristic pattern detection across the project; use when a microflow behaves unexpectedly and no model error is reported but a structural antipattern may be the cause.
