---
name: mendix-workflow-common
description: Use when creating or updating any Mendix workflow. Covers end-activity rules, outcome semantics (single-outcome flows must continue inline, multi-outcome flows branch), the user-targeting decision table (NoUserTargeting / XPathUserTargeting / XPathGroupTargeting / Microflow targeting), and workflow-specific expression rules ($WorkflowContext and $WorkflowInstance only). Trigger when the user describes a new workflow, modifies user-task assignments, adds decision splits, or writes workflow expressions.
---

## Tools in this environment

- `ped_*` (e.g. `ped_read_document`, `ped_update_document`, `ped_get_schema`) → `mcp__mendix-studio-pro__ped_*` (preferred — operate via the Studio Pro model API).

The skill body uses the short names inline to stay readable. This header tells you which actual MCP tool to call.

# General

Always make sure to follow and double check the CRITICAL/MANDATORY CHECKS, absolutely do not deviate from these instructions!

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
- **CRITICAL**: You MUST get the schema for `Workflows$XPathGroupTargeting` before creating it
- The constraint filters `System.WorkflowUserGroup` entities

### Microflow-based Targeting

When **complex business logic** is needed (e.g., "assign to the employee's direct manager"):

- Use `Workflows$MicroflowUserTargeting` (returns list of users) or `Workflows$MicroflowGroupTargeting` (returns list of groups)
- **CRITICAL**: You MUST get the schema before creating it
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

**CRITICAL CHECK**: Before writing ANY workflow expression, verify that all variables reference either `$WorkflowContext`, `$WorkflowInstance`. If unsure what variables are available, ask the user.
