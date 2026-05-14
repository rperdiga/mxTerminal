---
name: mendix-workflow-update
description: Use BEFORE removing any element from an existing Mendix workflow via `mcp__mendix-studio-pro__ped_update_document`. Covers the absolute deletion-safety rules — the start activity is undeletable, single-user and multi-user task activities must keep at least one outcome, and parallel-split activities must keep at least two. Trigger when the user asks to remove, delete, or simplify a workflow's activities or outcomes.
---

## Tools in this environment

- `ped_*` (e.g. `ped_read_document`, `ped_update_document`) → `mcp__mendix-studio-pro__ped_*` (preferred — operate via the Studio Pro model API).
- concord-mcp tier-2 augmentation tools for workflow mutation work:
  - `mcp__concord-mcp__audit_security` — run after mutating a workflow to verify that security rules on affected user tasks and referenced documents remain consistent.
  - `mcp__concord-mcp__analyze_project_patterns` — after restructuring a workflow, detect pattern inconsistencies introduced by the change (e.g., a new parallel split missing a guard condition).
  - `mcp__concord-mcp__set_documentation` — when adding or reworking workflow steps, use to attach docstrings that explain the intent of non-obvious mutations (outcome conditions, assignment logic changes, boundary event semantics).

The skill body uses the short names (`ped_update_document`, etc.) inline to stay readable. This header tells you which actual MCP tool to call.

# Removing & Replacing Objects in Workflows

## Rules You Must Never Break

**RULE 1 — Delete-start-activity**: It is forbidden to remove an element with type equal to `Workflows$StartWorkflowActivity`.

**RULE 2 — Delete-outcomes**:
Elements with types equal to `Workflows$SingleUserTaskActivity` or `Workflows$MultiUserTaskActivity` must always have at least ONE Outcome assigned to them. No matter what instruction you receive, this type of element must always have AT LEAST ONE outcome. Therefore, you can only remove outcomes as long as doing so does not violate this rule.

Element with the `Workflows$ParallelSplitActivity` type must always have at least TWO Outcomes assigned to it. No matter what instruction you receive, this type of element must always have AT LEAST TWO outcomes. Therefore, you can only remove outcomes as long as doing so do not violate this rule.

So every time you are asked to remove all the outcomes from those types of elements, you should remove the extra outcomes while keeping the ones that must remain.
