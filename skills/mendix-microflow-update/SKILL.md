---
name: mendix-microflow-update
description: Use BEFORE any mutation of an existing Mendix microflow via `mcp__mendix-studio-pro__ped_update_document` — covers auto-deletion of flows on object remove, the mandatory re-read-after-mutation rule, edge-to-edge 70px spacing math, batching rules (multi-remove safe, add+remove on same path forbidden), and step-by-step recipes for replace and insert. Trigger when the user asks to add, remove, replace, or rearrange microflow activities, flows, splits, loops, or end events on an existing microflow.
---

## Tools in this environment

- `ped_read_document`, `ped_update_document` → `mcp__mendix-studio-pro__ped_read_document`, `mcp__mendix-studio-pro__ped_update_document`.
- `mcp__concord-mcp__modify_microflow_activity` — one-shot mutation of an existing activity's properties without a full replace cycle. Use when you need to change a single property (e.g., commit mode, caption, expression) on an activity that is already connected to the flow. Prefer `ped_update_document` for structural changes (adding/removing objects or flows); use this tool for targeted in-place edits.
- `mcp__concord-mcp__insert_before_activity` — inserts a new activity before a named existing activity, maintaining flow connections. Use when ordered insertion at a specific position is required and rebuilding flows manually would be error-prone. The existing `ped_update_document` Insert recipe (above) remains the primary path; reach for this tool when the insertion point is unambiguous by activity name.
- `mcp__concord-mcp__exclude_document` — excludes a microflow from compilation. Rare; use only when the user explicitly asks to exclude a microflow (e.g., to suppress errors during development without deleting it).
- `mcp__concord-mcp__set_microflow_url` — exposes a microflow as a published URL endpoint. Use when the user asks to make a microflow callable via REST or to set its URL path.

The skill body uses the short names inline. This header tells you which actual MCP tool to call.

# Removing & Replacing Objects in Microflows

## Rules You Must Never Break

**RULE 1 — Auto-deletion**: Removing an object from `/objectCollection/objects` automatically deletes ALL flows connected to it. NEVER manually remove those flows — they are already gone.
Removing a flow does NOT delete connected objects.

**RULE 2 — Re-read after every mutation**: After ANY `ped_update_document` that adds or removes elements, you MUST call `ped_read_document` before your next `ped_update_document`.
WHY: Removing index 2 shifts everything after it down by 1. If "Activity C" was at index 3, it is now at index 2. If you skip re-reading and use `$id(/objects/3)`, you will reference the WRONG object.

**RULE 3**: Carefully plan the layout when you add new objects. NEVER OVERLAP OBJECTS. ALWAYS adjust the `relativeMiddlePoint` property when you are placing new objects into an existing flow.

## Before Modifying Microflow with Splits/Loops

STOP and answer:

1. Does this change affect a split branch? → List ALL caseValues that need flows
2. Am I inserting into a loop? → Where does it fit in the loop's execution sequence?
3. Am I modifying flows? → Read current flows first, plan replacements
4. Multiple start events error? → Did I create an object without incoming flow in a loop?

Only proceed after answering all questions.

## Object Sizes

**Objects can be resized by the user.** NEVER assume a hardcoded size — always read `size.width` and `size.height` from the document for every existing object before doing any layout calculation.

**Edge-to-edge gap** between connected objects: 70px. Do NOT use more or less.

## Positioning New Objects and Preventing Overlaps

When you change the structure of the microflow (e.g., adding, moving, resizing or removing objects or loops), you MUST consider how this affects the layout.

CRITICAL RULES FOR LAYOUT:

Before adding ANY object to a microflow, resizing ANY existing object, or adding ANY loop, you MUST follow these steps to calculate the new position and ensure no overlaps occur:

STEP 1 — Read and record actual sizes:

- Call `ped_read_document` to obtain the current `size.width` and `size.height` of every existing object.
- Use these actual values for size and position — NEVER substitute defaults for objects that already exist in the microflow.
- For each existing object: occupied_x = (relativeMiddlePoint.x - size.width/2, relativeMiddlePoint.x + size.width/2)
- For each existing object: occupied_y = (relativeMiddlePoint.y - size.height/2, relativeMiddlePoint.y + size.height/2)

STEP 2 — Plan new object position using EDGE-TO-EDGE spacing:

HORIZONTAL SPACING (all object types):

- The 70px gap is measured from the **right edge** of the previous object to the **left edge** of the next.
- If the next object is a loop, the same 70px gap applies from the previous object's right edge to the loop's left edge.
- Formula (general): `new_x = prev_x + prev_width/2 + 70 + new_width/2`
- Always substitute the actual `prev_width` and `new_width` read from the document (or the default size table above for brand-new objects).

**Loops:**

- Loop LEFT EDGE must be exactly 70px after the previous activity's RIGHT EDGE.
- Formula: `loop_x = (prev_x + prev_width/2) + 70 + (loop_width/2)`

**Step-by-step calculation for loops:**

1. Calculate previous activity's right edge: `prev_right = prev_x + (prev_width/2)`
2. Calculate desired loop left edge: `loop_left = prev_right + 70`
3. Calculate loop center: `loop_x = loop_left + (loop_width/2)`

**Example** (prev_x = 380, prev_width = 120, loop_width = 300):

- Step 1: prev_right = 380 + 60 = 440
- Step 2: loop_left = 440 + 70 = 510
- Step 3: loop_x = 510 + 150 = 660 ✓

**Objects AFTER loops:**

- Next object must be positioned based on loop's RIGHT EDGE, not loop's center.
- Formula: `next_x = (loop_x + loop_width/2) + 70 + next_width/2`
- Example — loop_x = 660, loop_width = 300, next_width = 120:
  → loop_right = 660 + 150 = 810
  → next_x = 810 + 70 + 60 = 940 ✓

VERTICAL SPACING (edge-to-edge, same 70px rule):

- The 70px gap is measured from the **bottom edge** of the upper object to the **top edge** of the lower object.
- Formula: `new_y = prev_y + prev_height/2 + 70 + new_height/2`
- Alternative (split) branches: apply this formula from the main-flow object's center downward for each branch.
- Error handlers: apply the same edge-to-edge formula; do NOT use a fixed 200px offset.

STEP 3 — Verify exact spacing:

- New object bounds MUST NOT intersect with any existing object bounds.
- Horizontal check: `gap = (new_x - new_width/2) - (prev_x + prev_width/2) == 70`
- Vertical check: `gap = (new_y - new_height/2) - (prev_y + prev_height/2) == 70`
- For loops: `(loop_x - loop_width/2) - (prev_x + prev_width/2) == 70`
- If the calculated gap is anything other than 70, recalculate the position.

STEP 4 — After adding, if ANY existing object overlaps with the new object:

- IMMEDIATELY move the overlapping object(s) in the same update call.
- NEVER leave overlaps to fix later.

CRITICAL: Skipping this calculation is FORBIDDEN and results in unprofessional microflows.

## Batching

- Multiple removes on SAME path in one call: SAFE (API reorders by descending index)
- Add + remove on SAME path in one call: NEVER
- Operations on DIFFERENT paths (e.g., remove from `/flows` + add to `/objectCollection/objects`): SAFE

## Recipe: Replace Object B with New Object D

Setup: Start→A→B→C→End (objects at indices 0–4, 4 flows connecting objects).

**Step 1 — Remove B:**
TOOL CALL: ped_update_document: operations: [{path: "/objectCollection/objects", operation: {type: "remove", index: 2}}]
TOOL EFFECT: B deleted, flows A→B and B→C auto-deleted.

**Step 2 — STOP. Re-read:**
TOOL CALL: ped_read_document: paths: ["/objectCollection/objects", "/flows"]
TOOL RESULT: objects: [Start, A, C, End] — flows: [Start→A, C→End]
NOTE: C shifted 3→2, End shifted 4→3. Only two flows remain. A and C are not connected.

**Step 3 — Add D:**
TOOL CALL: ped_update_document: operations: [{path: "/objectCollection/objects", operation: {type: "add", value: {$Type: "...", ...}}}]
TOOL RESULT: Success.

**Step 4 — STOP. Re-read:**
TOOL CALL: ped_read_document: paths: ["/objectCollection/objects", "/flows"]
TOOL RESULT: objects: [Start, A, C, End, D] — flows: [Start→A, C→End]

**Step 5 — Reconnect using ONLY Step 4 indices:**
TOOL CALL: ped_update_document: operations: [
{path: "/flows", operation: {type: "add", value: {
$Type: "Microflows$SequenceFlow",
originId: "$id(/objectCollection/objects/1)", ← A
destinationId: "$id(/objectCollection/objects/4)", ← D
}}},
{path: "/flows", operation: {type: "add", value: {
$Type: "Microflows$SequenceFlow",
originId: "$id(/objectCollection/objects/4)", ← D
destinationId: "$id(/objectCollection/objects/2)", ← C (index 2, NOT 3!)
}}}
]
TOOL RESULT: objects: [Start, A, C, End, D] — flows: [Start→A, C→End, A→D, D→C]

## Recipe: Insert Object D Between B and C

Setup: Start→A→B→C→End (same example as in the previous recipe). Remember flow at index 2 is B→C.

**Step 1 — Remove B→C flow AND add D (different paths → safe to batch):**
TOOL CALL: ped_update_document: operations: [
{path: "/flows", operation: {type: "remove", index: 2}},
{path: "/objectCollection/objects", operation: {type: "add", value: {$Type: "...", ...}}}
]
TOOL RESULT: Success.
NOTE: If B is an ExclusiveSplit, note the caseValue from the B→C flow BEFORE this step.

**Step 2 — STOP. Re-read:**
TOOL CALL: ped_read_document: paths: ["/objectCollection/objects", "/flows"]
TOOL RESULT: objects: [Start, A, B, C, End, D] — flows: [Start→A, A→B, C→End]

**Step 3 — Reconnect using ONLY Step 2 indices:**
TOOL CALL: ped_update_document: operations: [
{path: "/flows", operation: {type: "add", value: {
$Type: "Microflows$SequenceFlow",
originId: "$id(/objectCollection/objects/2)", ← B
destinationId: "$id(/objectCollection/objects/5)", ← D
caseValue: <SAME as old B→C flow if B is a split, otherwise omit>
}}},
{path: "/flows", operation: {type: "add", value: {
$Type: "Microflows$SequenceFlow",
originId: "$id(/objectCollection/objects/5)", ← D
destinationId: "$id(/objectCollection/objects/3)", ← C
}}}
]
TOOL RESULT: objects: [Start, A, B, C, End, D] — flows: [Start→A, A→B, C→End, B→D, D→C]
