---
name: mendix-workflow-update
description: Use BEFORE attempting to remove or modify any element in an existing Mendix workflow on Studio Pro 10.24.13–11.9.x. Documents what the agent can and cannot do — no dedicated workflow-mutation tools are exposed on this version. Covers the absolute deletion-safety rules and the correct workflow for guiding Studio Pro UI edits. Trigger when the user asks to remove, delete, or simplify a workflow's activities or outcomes.
---

## Tools in this environment

This skill is for **Studio Pro 10.24.13–11.9.x** (concord-mcp tool surface). The Mendix studio-pro MCP server and Maia are **not available** on this version.

Tools available for workflow inspection on 10.x:

- `mcp__concord-mcp__read_workflow_details` — read the structure of an existing workflow before advising on changes.
- `mcp__concord-mcp__list_workflows` — list all workflows in the project.
- `mcp__concord-mcp__check_project_errors` — validate project consistency after the user applies changes in Studio Pro.

**What is NOT available on 10.x:**

There are no dedicated workflow-mutation tools in the concord-mcp surface on this version. The agent cannot:

- Remove activities, outcomes, or splits from a workflow via a tool call.
- Add activities or outcomes to a workflow via a tool call.
- Modify user-targeting configuration directly.
- Rename workflow elements.

All workflow structural edits must be performed by the user in Studio Pro's native workflow editor. The agent's role is to inspect, advise, and validate.

---

# Removing & Replacing Objects in Workflows

## Rules You Must Never Break

These rules apply whether the agent or the user is making the change. Always communicate them before guiding Studio Pro edits.

**RULE 1 — Delete-start-activity**: It is forbidden to remove an element with type equal to `Workflows$StartWorkflowActivity`. The start activity is the workflow's entry point and cannot be deleted. If the user asks to remove it, explain why and suggest restructuring the workflow instead.

**RULE 2 — Delete-outcomes**: Elements with types equal to `Workflows$SingleUserTaskActivity` or `Workflows$MultiUserTaskActivity` must always have at least ONE outcome assigned to them. No matter what instruction you receive, this type of element must always have AT LEAST ONE outcome. Therefore, you can only remove outcomes as long as doing so does not violate this rule.

An element with the `Workflows$ParallelSplitActivity` type must always have at least TWO outcomes assigned to it. No matter what instruction you receive, this type of element must always have AT LEAST TWO outcomes. Therefore, you can only remove outcomes as long as doing so does not violate this rule.

So every time the user is asked to remove all the outcomes from those types of elements, they should remove the extra outcomes while keeping the ones that must remain.

---

## Workflow for Studio Pro Editing Guidance

Because the agent cannot mutate workflow structure on 10.x, follow this procedure:

1. **Read the current workflow** with `mcp__concord-mcp__read_workflow_details` to understand the existing structure before advising.

2. **Apply the deletion-safety rules** — before telling the user to remove anything, verify:
   - The target is not a `Workflows$StartWorkflowActivity`.
   - Removing an outcome from a user task or parallel split activity does not reduce the count below the minimum (1 for user tasks, 2 for parallel splits).

3. **Guide the Studio Pro edit** — describe the specific steps for the user to perform in Studio Pro's workflow editor:
   - Which activity to select.
   - What to right-click / which menu action to use.
   - What the result should look like.

4. **After the user confirms** the changes are saved, call `mcp__concord-mcp__check_project_errors` to surface any consistency issues introduced by the edit.

5. **If a workflow activity calls a microflow** that also needs modification, handle that separately using the `mendix-microflow-update` skill and concord-mcp microflow tools.

---

## Common Guidance Patterns

**Removing an outcome from a user task:**
- Confirm the user task will still have at least one outcome after removal (Rule 2).
- In Studio Pro: open the workflow, double-click the user task, navigate to the Outcomes tab, select the outcome to remove, click Delete.
- After save: run `mcp__concord-mcp__check_project_errors`.

**Removing an activity from the workflow:**
- In Studio Pro: select the activity in the workflow canvas, press Delete (or right-click → Delete).
- Studio Pro will prompt to reconnect the surrounding flow if needed.
- After save: run `mcp__concord-mcp__check_project_errors`.

**Replacing an activity:**
- In Studio Pro: delete the old activity, then drag the new activity type from the toolbox into the same position.
- Reconnect the incoming and outgoing flows.
- After save: run `mcp__concord-mcp__check_project_errors`.

**Simplifying a parallel split to fewer paths:**
- Confirm at least two outcomes will remain (Rule 2 for `Workflows$ParallelSplitActivity`).
- In Studio Pro: delete the unwanted parallel paths from the split activity.
- After save: run `mcp__concord-mcp__check_project_errors`.
