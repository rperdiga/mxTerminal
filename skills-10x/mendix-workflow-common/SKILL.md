---
name: mendix-workflow-common
description: Use when creating or updating any Mendix workflow on Studio Pro 10.24.13–11.9.x. Documents the available inspection tools (list_workflows, read_workflow_details), the authoring surface limitation (workflow structure is authored in Studio Pro's native editor), end-activity rules, outcome semantics, user-targeting decision table, and workflow expression rules. Trigger when the user describes a new workflow, asks about workflow structure, modifies user-task assignments, or writes workflow expressions.
---

## Tools in this environment

This skill is for **Studio Pro 10.24.13–11.9.x** (concord-mcp tool surface). The Mendix studio-pro MCP server and Maia are **not available** on this version.

Tools relevant to workflows on 10.x:

- `mcp__concord-mcp__list_workflows` — list all workflow documents in the project or a module.
- `mcp__concord-mcp__read_workflow_details` — read the structure of an existing workflow, including activities, outcomes, and user-targeting configuration.
- `mcp__concord-mcp__check_project_errors` — validate project consistency after any related change (e.g., after modifying a microflow called by a workflow).
- `mcp__concord-mcp__analyze_project_patterns` — identify structural patterns in the project that relate to workflow context entities or microflows.

**What is NOT available on 10.x:**

There are no dedicated workflow-mutation tools in the concord-mcp surface on 10.x. Adding or removing workflow activities, configuring user tasks, or modifying workflow outcomes must be done in Studio Pro's native workflow editor.

**What the agent can do on 10.x:**

- Inspect existing workflows with `list_workflows` and `read_workflow_details`.
- Explain workflow structure and doctrine to guide Studio Pro editing.
- Assist with writing expressions used in workflow activities (see Expressions section below).
- Assist with writing XPath constraints for user-targeting configurations.
- Identify the microflows wired to workflow activities and assist with modifying those microflows (using concord-mcp microflow tools).

---

# General

Always make sure to follow and double check the CRITICAL/MANDATORY CHECKS, absolutely do not deviate from these instructions!

## Inspecting Workflows

**List workflows:**

```
mcp__concord-mcp__list_workflows
  module: <ModuleName>   (optional — omit for all modules)
```

**Read workflow details:**

```
mcp__concord-mcp__read_workflow_details
  module: <ModuleName>
  workflowName: <WorkflowName>
```

Use `read_workflow_details` to understand the current structure before making any recommendations for Studio Pro edits.

---

## End Activity - Critical Rules

**CRITICAL CHECK**: Before adding an end activity to the main flow, ask: "Can workflow execution ever reach this point, or do all paths already terminate?" If all paths terminate, do NOT add a main flow end activity. If multiple paths are available and they can be early terminated do so.

## Outcomes - Critical Rules

- **Single outcome**: If a user task has only one possible path forward, the next activity goes directly in the flow (NOT IN THE OUTCOME).
- **Multiple outcomes = BRANCHING**: Only place activities in outcomes when the user must choose between 2+ different paths.

### Correct Patterns

✅ CORRECT (A single outcome, single path): User Task → Outcome A → Next Activity (directly in the flow (NOT IN THE OUTCOME))
✅ CORRECT (Multiple outcomes, branching): User Task → Outcome A → Activity X → Outcome B → Activity Y
❌ WRONG (Single outcome wrapping next activity): User Task → Outcome "Complete" (or "Done", "Submit", "OK") → Next Activity

**MANDATORY CHECK**: Before adding ANY activity inside an outcome, ask:

1. "How many outcomes does this activity have?"
    - If answer is 1 → Place activities in flow (NOT IN THE OUTCOME) OR add a second outcome if branching is actually needed
    - If answer is 2+ → Verify each outcome leads to a genuinely different path

**CRITICAL**: This rule applies to ALL outcome types (boolean, enumeration, user task) and ALL parent activities (CallMicroflowActivity, ExclusiveSplitActivity, UserTask, AIAgentTaskActivity). If there's only one possible path, place activities in the flow (NOT IN THE OUTCOME).

## User Targeting

User targeting determines which users receive a workflow task in their inbox. The `userTargeting` property is present on all user task activities (`Workflows$SingleUserTaskActivity`, `Workflows$MultiUserTaskActivity`).

**⚠️ CRITICAL**: When in doubt or when targeting is unspecified, always use `Workflows$NoUserTargeting`. Never invent or guess a targeting mechanism.

### Targeting Type Decision

**🚨 MANDATORY PRE-CHECK - ALWAYS RUN BEFORE SETTING userTargeting 🚨**

Before setting `userTargeting`, analyze the user's request carefully:

**"Who should receive this task based on the user's description?"**

| Situation                                                                                                                                                   | Targeting type                                                            |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| Target is unspecified, unclear, or ambiguous                                                                                                                | `Workflows$NoUserTargeting` (default)                                     |
| Specific user role or attribute is named (e.g. "managers", "admins")                                                                                        | `Workflows$XPathUserTargeting`                                            |
| A noun or noun phrase performs or receives the task action (e.g. "manager approves", "reviewer accepts", "team lead signs off") — the subject is the target | `Workflows$XPathUserTargeting`                                            |
| A named workflow group is referenced (e.g. "Finance group")                                                                                                 | `Workflows$XPathGroupTargeting`                                           |
| Complex or conditional logic is described (e.g. "assign to direct manager")                                                                                 | `Workflows$MicroflowUserTargeting` or `Workflows$MicroflowGroupTargeting` |

**Note**: XPath constraints for targeting follow the same syntax as microflow XPath. Refer to the `mendix-microflow-syntax` skill for full syntax reference.

**⚠️ CRITICAL**: Analyze the user's language carefully. Role mentions in approval/action context often imply targeting!

### XPath User Targeting - Role-Based (Most Common)

**⚠️ CRITICAL POST-CREATION STEP**: Always inform the user to verify the role exists.

**If the role name is not a known system role** (e.g. "criminal", "vendor", "dog"):

- Still use `Workflows$XPathUserTargeting` with a constraint that references the role name as given by the user (e.g. `System.UserRoles = '[%UserRole_Criminal%]'`)
- Do NOT fall back to `Workflows$NoUserTargeting` just because the role name is unusual

When a **user role or attribute** is explicitly named (e.g. "assign to managers"):

**Example patterns:**

- Filter by role: `[(System.UserRoles = '[%UserRole_User%]')]`
- Filter by multiple roles:
    - With `or` (either role is eligible): `[(System.UserRoles = '[%UserRole_HR%]' or System.UserRoles = '[%UserRole_Manager%]')]` — e.g. "HR or manager can approve"
    - With `and` (role + additional condition): `[(System.UserRoles = '[%UserRole_Manager%]' and Active = true())]` — e.g. "active managers only"
- Filter by attribute: `[(Department = 'Sales')]`
- Complex filters: `[(Active = true() and System.UserRoles = '[%UserRole_Admin%]')]`

### XPath Group Targeting

When a **workflow group** is explicitly referenced:

- Use `Workflows$XPathGroupTargeting` with an `xPathConstraint` that filters workflow groups
- The constraint filters `System.WorkflowUserGroup` entities

### Microflow-based Targeting

When **complex business logic** is needed (e.g., "assign to the employee's direct manager"):

- Use `Workflows$MicroflowUserTargeting` (returns list of users) or `Workflows$MicroflowGroupTargeting` (returns list of groups)
- The microflow receives the workflow context as a parameter and must return a list of the appropriate type

### Common Mistakes to Avoid

❌ **WRONG**: User says "manager approves" → You use NoUserTargeting
✅ **CORRECT**: User says "manager approves" → Use XPathUserTargeting with role filter

❌ **WRONG**: User says "send to Finance team" → You use NoUserTargeting
✅ **CORRECT**: User says "send to Finance team" → Use XPathGroupTargeting

❌ **WRONG**: Unclear targeting → You guess or invent a mechanism
✅ **CORRECT**: Unclear targeting → Use NoUserTargeting (the safe default)

❌ **WRONG**: User says "HR and manager can approve" → You create two separate user tasks
✅ **CORRECT**: User says "HR and manager can approve" → Create ONE user task with XPathUserTargeting: `[(System.UserRoles = '[%UserRole_HR%]' or System.UserRoles = '[%UserRole_Manager%]')]`

## Expressions - Critical Rules

For general expression syntax, refer to the `mendix-microflow-syntax` skill.

**WORKFLOW-SPECIFIC VARIABLE RULES:**

1. **ONLY use these variables in workflow expressions:**
    - `$WorkflowContext` — The workflow's parameter entity containing business data (accessed as `$WorkflowContext/AttributeName`)
    - `$WorkflowInstance` — The System.Workflow entity representing the workflow instance itself (accessed as `$WorkflowInstance/AttributeName`)

2. **DO NOT use:**
    - Microflow-specific system variables (`$currentUser`, `$currentSession`, etc.) — These are NOT available in workflows
    - Local variables from microflows — Workflows are separate from microflows
    - Random variable names — All variables must be from workflow context, workflow instance, or preceding activity outputs

3. **Examples:**
    - ✅ CORRECT: `$WorkflowContext/Amount > 1000` — Access business data from workflow parameter
    - ✅ CORRECT: `$WorkflowInstance/DueDate` — Access workflow instance metadata
    - ❌ WRONG: `$currentUser/Name` — Not available in workflows
    - ❌ WRONG: `$Order/Amount` — Use `$WorkflowContext` instead

**WHERE EXPRESSIONS ARE USED IN WORKFLOWS:**

- ExclusiveSplitActivity `expression` property (decision logic)
- Boundary event `firstExecutionTime` property (timer expressions)
- Workflow `dueDate` property
- User task `dueDate` property
- WaitForTimerActivity `delay` property

**CRITICAL CHECK**: Before writing ANY workflow expression, verify that all variables reference either `$WorkflowContext` or `$WorkflowInstance`. If unsure what variables are available, ask the user.

---

## Workflow for Studio Pro Editing Guidance

Because the agent cannot mutate workflow structure on 10.x, the recommended approach is:

1. Call `mcp__concord-mcp__read_workflow_details` to read the current workflow structure.
2. Explain the required changes in terms of Studio Pro's workflow editor (activity types, outcome configuration, targeting settings).
3. If a workflow activity calls a microflow, assist with modifying that microflow using the `mendix-microflow-update` skill and concord-mcp microflow tools.
4. After the user makes changes in Studio Pro, call `mcp__concord-mcp__check_project_errors` to surface any consistency issues.
