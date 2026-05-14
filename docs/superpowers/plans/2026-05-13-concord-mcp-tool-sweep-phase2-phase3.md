# Concord MCP tool-sweep — Phase 2/3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drive the Phase 1 sweep's 33 FAILs down to ≥80/88 PASS by (a) reclassifying documented stubs + Task-15-dependents as `expected: "either"`, (b) adding a `setup` phase that bootstraps missing fixtures, (c) extracting a `TryPerModule<T>` helper to defeat Studio Pro 10.x ModuleProxy crashes, (d) fixing the `analyze_project_patterns` `commonDeleteBehavior` wiring bug, and (e) investigating 3 remaining real-bug suspects.

**Architecture:** Matrix-first (no rebuild) → C# fixes (three atomic commits). Phase 2a strips false-positive noise so Phase 2b investigates real bugs against a quiet baseline. Each Phase 2b commit is independently buildable, bisectable, and verified via `./scripts/concord-mcp-sweep.ps1 -Only <fixed-tools>`.

**Tech Stack:** C# / .NET 8 (Concord.Core), xUnit + FluentAssertions, PowerShell (sweep driver), JSONC (matrix). Spec: [docs/superpowers/specs/2026-05-13-concord-mcp-tool-sweep-phase2-phase3-design.md](../specs/2026-05-13-concord-mcp-tool-sweep-phase2-phase3-design.md). Parent spec: [docs/superpowers/specs/2026-05-13-concord-mcp-tool-sweep-design.md](../specs/2026-05-13-concord-mcp-tool-sweep-design.md). Phase 1 findings: [tests/concord-mcp-sweep/findings.md](../../../tests/concord-mcp-sweep/findings.md).

**Branch:** `feat/v5.0.0-w2-mcpx-merge` (already current).

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `tests/concord-mcp-sweep/matrix.jsonc` | Modify | Reclassify 14 entries → `expected: "either"`, add 5 `setup` entries, fix `rename_enumeration_value` reserved-word arg |
| `scripts/concord-mcp-sweep.ps1` | Modify | Extend `$phaseOrder` array to include `setup` first |
| `src/Concord.Core/Spmcp/Utils/Utils.cs` | Modify | Add static `TryPerModule<T>` helper |
| `tests/Concord.Core.Tests/SpmcpUtilsTests.cs` | Modify | Add 3 unit tests for `TryPerModule<T>` |
| `src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs` | Modify | Apply helper at `ReadProjectInfo` (~line 745+) and `UpdateEnumeration` (~line 2784) |
| `src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs` | Modify | Refactor `ListJavaActions` (~line 2706) to per-module + apply helper; wire `commonDeleteBehavior` into `statistics` anon-type (~line 4644) |
| `tests/concord-mcp-sweep/findings.md` | Auto-regenerated | Driver overwrites on each sweep run; resolution annotations added by hand at the end |
| `~/.claude/projects/c--Extensions-Terminal/memory/project_concord_mcp_tool_sweep.md` | Update | Auto-memory captures outcome |

---

## Phase 2a — Matrix cleanup (1 commit, no rebuild)

### Task 1: Patch driver phase-order to include `setup` first

**Files:**
- Modify: `scripts/concord-mcp-sweep.ps1:394`

- [ ] **Step 1: Edit the `$phaseOrder` hashtable**

Open `scripts/concord-mcp-sweep.ps1`. Replace the line at 394:

```powershell
$phaseOrder = @{ "read" = 0; "mutate" = 1; "lifecycle" = 2 }
```

with:

```powershell
$phaseOrder = @{ "setup" = 0; "read" = 1; "mutate" = 2; "lifecycle" = 3 }
```

- [ ] **Step 2: Update the `.PARAMETER Phase` doc comment**

Find the doc-comment line near top (around line 17):

```powershell
    Comma-list of phases to run: read, mutate, lifecycle.
```

Replace with:

```powershell
    Comma-list of phases to run: setup, read, mutate, lifecycle.
```

- [ ] **Step 3: Verify the script still parses**

Run: `pwsh -NoProfile -Command "& { . ./scripts/concord-mcp-sweep.ps1 -DryRun -ErrorAction Stop }"` — expect either a successful dry-run print or a connect-refused (acceptable, just proves the script parses).

Don't commit yet — Task 4 commits Phase 2a as a unit.

---

### Task 2: Reclassify documented stubs + Task-15 dependents to `expected: "either"`

**Files:**
- Modify: `tests/concord-mcp-sweep/matrix.jsonc`

**Background:** 14 entries currently have `expected: "ok"` but the source returns a deliberate `{ success: false, escalation: "manual" }` or throws `NotImplementedException("Pending Task 15...")`. These are documented non-implementations, not bugs.

- [ ] **Step 1: Reclassify the 5 Security stubs**

In `matrix.jsonc`, find each of the 5 Security entries (`list_rules`, `read_security_info`, `read_entity_access_rules`, `read_microflow_security`, `audit_security` — currently between lines ~263-301). For each, change `"expected": "ok"` to `"expected": "either"` and append to the `notes` field: ` // stub: escalation:manual until typed Interop expanded`.

Example diff for `list_rules`:

```diff
   {
     "name": "list_rules",
     "family": "Security",
     "phase": "read",
     "args": {},
-    "expected": "ok",
-    "notes": "Always returns escalation: manual — IRule documents not exposed on typed Interop surface."
+    "expected": "either",
+    "notes": "Always returns escalation: manual — IRule documents not exposed on typed Interop surface. // stub: escalation:manual until typed Interop expanded"
   }
```

Apply the same shape to all 5 Security entries.

- [ ] **Step 2: Reclassify the 3 ConstantsEnums stubs**

Find `create_constant`, `update_constant`, `configure_constant_values` (around lines 562-585). Apply the same reclassification.

- [ ] **Step 3: Reclassify the 1–2 ProjectSettings stubs**

- `sync_filesystem` (notes say "Always returns escalation: manual") — reclassify to `either`.
- `set_configuration` (notes say "may return escalation: manual") — reclassify to `either`.

- [ ] **Step 4: Reclassify the 2 Task-15 stubs + 2 lifecycle downstreams**

For `get_app_status`, `get_active_run_configuration`, `run_app`, `stop_app`: change `"expected": "ok"` to `"expected": "either"` and append to notes: ` // pending Task 15 — see Concord.Host10x\Interop\*Host10x.cs`.

- [ ] **Step 5: Fix `rename_enumeration_value` reserved-word arg**

Around line 558:

```diff
-    "args": { "enumeration_name": "SweepEnum_create_enumeration", "value_name": "Draft", "new_name": "New", "module_name": "MyFirstModule" },
+    "args": { "enumeration_name": "SweepEnum_create_enumeration", "value_name": "Draft", "new_name": "NewDraft", "module_name": "MyFirstModule" },
```

- [ ] **Step 6: Validate matrix parses**

Run:

```powershell
pwsh -NoProfile -Command "(Get-Content -Raw tests/concord-mcp-sweep/matrix.jsonc) -replace '(?ms)/\*.*?\*/', '' -replace '(?m)//[^\n]*', '' | ConvertFrom-Json | Measure-Object | Select-Object -ExpandProperty Count"
```

Expected: `88` (matches Phase 1 entry count — no entries added yet, Task 3 adds them).

Don't commit yet.

---

### Task 3: Add `setup` phase entries

**Files:**
- Modify: `tests/concord-mcp-sweep/matrix.jsonc`

**Background:** Phase 1 `read`-phase tools failed for `Customer`, `ACT_Example`, `Order`, `Customer_Overview` (8–9 tools total). Bootstrap these as fixtures before the `read` phase runs.

- [ ] **Step 1: Insert the `setup` block at the top of the matrix**

Open `matrix.jsonc`. The matrix is currently a JSON array `[ { read entries... }, { mutate... }, { lifecycle... } ]`. Find the first entry (the leading `[` and the first `{` block) and insert the following 5 entries **before** any existing `read`-phase entry — they must come first so the driver's phase-order sort runs them first.

Concrete insertion: open `matrix.jsonc`, locate the line that begins the first `read`-phase entry (search for `"phase": "read"`). Above the first `{` of that entry, insert:

```jsonc
  // ---- Setup phase: bootstrap fixtures for read-phase targets ----
  ,{
    "name": "create_entity", "family": "DomainModel", "phase": "setup",
    "args": { "module_name": "MyFirstModule", "entity_name": "Customer" },
    "expected": "either",
    "notes": "Fixture for query_associations, read_attribute_details, copy_model_element. expected:either for idempotency across reruns."
  }
  ,{
    "name": "add_attribute", "family": "DomainModel", "phase": "setup",
    "args": { "module_name": "MyFirstModule", "entity_name": "Customer", "attribute_name": "Name", "attribute_type": "String" },
    "expected": "either",
    "notes": "Fixture for read_attribute_details(Customer.Name)."
  }
  ,{
    "name": "create_entity", "family": "DomainModel", "phase": "setup",
    "args": { "module_name": "MyFirstModule", "entity_name": "Order" },
    "expected": "either",
    "notes": "Fixture for generate_overview_pages, create_association."
  }
  ,{
    "name": "create_microflow", "family": "Microflows", "phase": "setup",
    "args": { "name": "ACT_Example", "module_name": "MyFirstModule" },
    "expected": "either",
    "notes": "Fixture for read_microflow_details, check_variable_name, rename_document."
  }
  ,{
    "name": "create_microflow", "family": "Microflows", "phase": "setup",
    "args": { "name": "CAL_Customer_FullName", "module_name": "MyFirstModule" },
    "expected": "either",
    "notes": "Fixture for set_calculated_attribute."
  }
```

Important: the leading comma on each `,{` is intentional — `matrix.jsonc` uses JSONC's lenient comma style. If you're inserting at the very start, drop the leading comma on the first entry and keep it on the rest.

- [ ] **Step 2: Annotate the 3 remaining `read` entries that lack `setup` support**

Three reads still target content with no `create_*` MCP tool, so they stay `expected: "either"` without a fixture. Find each and append to its `notes`:

- `read_nanoflow_details` (`MyNanoflow`): ` // no create_nanoflow tool exists; expected:either by design`
- `read_workflow_details` (`MyWorkflow`): ` // no create_workflow tool exists; expected:either by design`
- `read_page_details` (`Customer_Overview`): ` // depends on generate_overview_pages (mutate phase); expected:either by design`

If these entries currently have `"expected": "ok"`, change to `"expected": "either"`.

- [ ] **Step 3: Validate matrix parses and count grew**

```powershell
pwsh -NoProfile -Command "(Get-Content -Raw tests/concord-mcp-sweep/matrix.jsonc) -replace '(?ms)/\*.*?\*/', '' -replace '(?m)//[^\n]*', '' | ConvertFrom-Json | Measure-Object | Select-Object -ExpandProperty Count"
```

Expected: `93` (88 + 5 new setup entries).

Don't commit yet.

---

### Task 4: Re-run sweep and commit Phase 2a

**Files:**
- Auto-generated: `tests/concord-mcp-sweep/findings.md`, `tests/concord-mcp-sweep/findings.json`

**Pre-requisite:** Studio Pro must be running with `Test_10_24_13` open and the Concord MCP server reachable at `http://127.0.0.1:7783/mcp`. If not, ask the user to launch Studio Pro and open the project, then continue.

- [ ] **Step 1: Run the full sweep**

```powershell
./scripts/concord-mcp-sweep.ps1
```

Expected: ~75–80 PASS / ~5–8 FAIL out of 93. The driver overwrites `findings.md` and `findings.json` incrementally.

- [ ] **Step 2: Inspect new baseline**

Open `tests/concord-mcp-sweep/findings.md`. Verify:
- All 5 Security entries now in PASS list (or absent from Failures section)
- All 3 ConstantsEnums stubs in PASS
- `sync_filesystem`, `set_configuration` in PASS
- `get_app_status`, `get_active_run_configuration`, `run_app`, `stop_app` in PASS
- `rename_enumeration_value` in PASS
- 5 setup-phase entries (`create_entity` × 2, `add_attribute`, `create_microflow` × 2) all in PASS (or `expected:either` so they count as PASS regardless)
- `query_associations`, `read_attribute_details`, `read_microflow_details`, `check_variable_name`, `copy_model_element` in PASS (fixtures now exist)

Remaining FAILs should be only category E (real bugs): `analyze_project_patterns`, `read_project_info`, `list_java_actions`, `update_enumeration`, `check_project_errors`, `set_runtime_settings`, `create_microflow_activity`, `modify_microflow_activity`, `insert_before_activity`.

- [ ] **Step 3: Commit Phase 2a**

```bash
git add tests/concord-mcp-sweep/matrix.jsonc scripts/concord-mcp-sweep.ps1 tests/concord-mcp-sweep/findings.md tests/concord-mcp-sweep/findings.json
git commit -m "$(cat <<'EOF'
fix(spmcp-sweep): Phase 2a matrix cleanup + setup phase

- Reclassify 10 escalation:manual stubs (5 Security + 3 Constants +
  sync_filesystem, set_configuration) → expected:either
- Reclassify 2 Task-15 stubs (get_app_status, get_active_run_configuration)
  and 2 downstream lifecycle tools (run_app, stop_app) → expected:either
- Add 5-entry setup phase (Customer, Customer.Name, Order, ACT_Example,
  CAL_Customer_FullName) so read-phase tools find their targets
- Fix rename_enumeration_value reserved-word arg ("New" → "NewDraft")
- Driver: extend phase order to setup → read → mutate → lifecycle

Baseline: ~XX/93 PASS (was 55/88). Remaining FAILs are real C# bugs to
fix in Phase 2b.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Replace `~XX` with the actual PASS count from the sweep output.

---

## Phase 2b commit 1 — `TryPerModule<T>` helper + ModuleProxy fixes

### Task 5: Write failing tests for `TryPerModule<T>`

**Files:**
- Modify: `tests/Concord.Core.Tests/SpmcpUtilsTests.cs`

**Background:** Existing test file (`tests/Concord.Core.Tests/SpmcpUtilsTests.cs:1-129`) tests `Utils.GetArrayParam`. We're adding 3 tests for the new `TryPerModule<T>` helper, following the same TDD discipline.

- [ ] **Step 1: Add `using` for the new types**

At the top of `SpmcpUtilsTests.cs`, after the existing `using` block (line 6), add:

```csharp
using System.Collections.Generic;
using Concord.Interop.Models;
using Microsoft.Extensions.Logging.Abstractions;
```

(`Concord.Interop.Models` is where `ModuleId` lives — verify by Grep for `public.*class.*ModuleId\b` or `public.*record.*ModuleId\b` if the namespace differs, and adjust accordingly.)

- [ ] **Step 2: Add the three failing tests at end of class**

Before the closing `}` of `public class SpmcpUtilsTests`, append:

```csharp
    [Fact]
    public void TryPerModule_HappyPath_ReturnsValueAndLeavesSkippedEmpty()
    {
        var moduleId = new ModuleId { Name = "MyFirstModule" };
        var skipped = new List<object>();
        var result = Utils.TryPerModule(
            moduleId,
            () => "the-value",
            skipped,
            "TestOp",
            NullLogger.Instance);
        result.Should().Be("the-value");
        skipped.Should().BeEmpty();
    }

    [Fact]
    public void TryPerModule_KeyNotFound_ReturnsDefaultAndRecordsSkip()
    {
        var moduleId = new ModuleId { Name = "SystemModule" };
        var skipped = new List<object>();
        var result = Utils.TryPerModule<string>(
            moduleId,
            () => throw new KeyNotFoundException("Mendix.Modeler.ExtensionLoader.ModelProxies.Projects.ModuleProxy"),
            skipped,
            "ListEnumerations",
            NullLogger.Instance);
        result.Should().BeNull();
        skipped.Should().HaveCount(1);
        var entry = skipped[0]!.GetType().GetProperty("module")!.GetValue(skipped[0]);
        entry!.ToString().Should().Be("SystemModule");
    }

    [Fact]
    public void TryPerModule_OtherException_RethrowsRatherThanSwallowing()
    {
        var moduleId = new ModuleId { Name = "MyFirstModule" };
        var skipped = new List<object>();
        Action act = () => Utils.TryPerModule<string>(
            moduleId,
            () => throw new InvalidOperationException("unrelated bug"),
            skipped,
            "TestOp",
            NullLogger.Instance);
        act.Should().Throw<InvalidOperationException>().WithMessage("unrelated bug");
        skipped.Should().BeEmpty();
    }
```

- [ ] **Step 3: Run the failing tests**

```powershell
dotnet test tests/Concord.Core.Tests --filter "FullyQualifiedName~TryPerModule"
```

Expected: 3 compilation errors (TryPerModule doesn't exist yet) OR 3 test failures (helper missing). The point is the build fails or tests fail — both prove we wrote real tests against a real gap.

---

### Task 6: Implement `TryPerModule<T>`

**Files:**
- Modify: `src/Concord.Core/Spmcp/Utils/Utils.cs`

- [ ] **Step 1: Add needed `using` directives at top of `Utils.cs`**

After `using System.Text.Json.Nodes;` (line 3), add:

```csharp
using System;
using System.Collections.Generic;
using Concord.Interop.Models;
using Microsoft.Extensions.Logging;
```

(Adjust `Concord.Interop.Models` to wherever `ModuleId` actually lives — same lookup as Task 5 Step 1.)

- [ ] **Step 2: Append the helper to the `Utils` class**

Inside `public class Utils`, before the closing `}` (after the existing `GetArrayParam` method), append:

```csharp
    /// <summary>
    /// Wraps a per-module ExtensionsAPI call that may throw KeyNotFoundException
    /// for ModuleProxy on Studio Pro 10.x system / App-Store modules. Returns the
    /// call result on success; records the skip and returns default(T) on the
    /// known-pattern failure. Non-matching exceptions re-throw so unrelated bugs
    /// aren't silently swallowed.
    /// </summary>
    public static T? TryPerModule<T>(
        ModuleId moduleId,
        Func<T> call,
        List<object> skipped,
        string operation,
        ILogger logger)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is KeyNotFoundException
                                || ex.Message.Contains("ModuleProxy"))
        {
            logger.LogWarning(ex, "{Operation} failed for module {Module}", operation, moduleId.Name);
            skipped.Add(new
            {
                module = moduleId.Name,
                operation,
                error = ex.Message,
                note = "Module's index isn't queryable via this Studio Pro version's extension API (often happens for system / App-Store-imported modules on 10.x). Skipped."
            });
            return default;
        }
    }
```

- [ ] **Step 3: Run the tests; expect green**

```powershell
dotnet test tests/Concord.Core.Tests --filter "FullyQualifiedName~TryPerModule"
```

Expected: 3 tests PASS.

- [ ] **Step 4: Run the full Concord.Core.Tests suite to check nothing else broke**

```powershell
dotnet test tests/Concord.Core.Tests
```

Expected: all tests pass (existing + 3 new).

---

### Task 7: Apply `TryPerModule<T>` at `ReadProjectInfo`

**Files:**
- Modify: `src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs:745-810` (approximate — verify with Read before editing)

**Background:** The per-module Select walk in `ReadProjectInfo` at line ~757 calls `ListEntities`, `ListEnumerations`, `ListModuleDocuments` per module. Any of these can throw on 10.x ModuleProxy. Today the outer call returns `error:`. We want it to skip bad modules and continue.

- [ ] **Step 1: Read the current `ReadProjectInfo` body**

Run a Read at `MendixDomainModelTools.cs` offset ~745, limit 100 lines, to confirm exact line numbers of the per-module Select walk before editing.

- [ ] **Step 2: Refactor the per-module walk**

Replace the `Select(moduleId => { ... })` block with a loop that uses `TryPerModule`. Concrete shape — adapt to the actual code structure:

```csharp
var moduleInfos = new List<object>();
var skippedModules = new List<object>();
foreach (var moduleId in allModuleIds)
{
    var entityRefs = Utils.TryPerModule(moduleId,
        () => HostServices.DomainModel.ListEntities(moduleId),
        skippedModules, "ListEntities", _logger);
    if (entityRefs == null) continue;

    var enumerationRefs = Utils.TryPerModule(moduleId,
        () => HostServices.DomainModel.ListEnumerations(moduleId),
        skippedModules, "ListEnumerations", _logger);
    if (enumerationRefs == null) continue;

    var microflowDocs = Utils.TryPerModule(moduleId,
        () => HostServices.Model.ListModuleDocuments(moduleId, "Microflow"),
        skippedModules, "ListModuleDocuments(Microflow)", _logger);
    if (microflowDocs == null) continue;

    var constantDocs = Utils.TryPerModule(moduleId,
        () => HostServices.Model.ListModuleDocuments(moduleId, "Constant"),
        skippedModules, "ListModuleDocuments(Constant)", _logger);
    if (constantDocs == null) continue;

    // ... existing per-entity association walk, wrapped in its own try/catch as today ...

    moduleInfos.Add(new
    {
        name = moduleId.Name,
        entityCount = entityRefs.Count,
        // ... existing projection ...
    });
}

// Add `using Terminal.Spmcp.Utils;` at file top if not already present.

return JsonSerializer.Serialize(new
{
    success = true,
    project = projectInfo,
    modules = moduleInfos,
    skippedModules = skippedModules.Count == 0 ? null : skippedModules,
});
```

Preserve the existing per-entity association walk's inner try/catch (currently empty `catch { }` at line ~774) — that's a different exception class, not the ModuleProxy pattern.

- [ ] **Step 3: Add `using Terminal.Spmcp.Utils;` to the file's `using` block**

If not already imported. Check the top of `MendixDomainModelTools.cs` — adjust namespace name to match what `Utils.cs` declares (line 1: `namespace Terminal.Spmcp.Utils;`).

- [ ] **Step 4: Verify build still succeeds**

```powershell
dotnet build src/Concord.Core
```

Expected: `Build succeeded` with no new errors. (Deploy-to-Mendix happens after Task 9.)

Don't commit yet — Tasks 8 and 9 add the other two call sites first.

---

### Task 8: Apply `TryPerModule<T>` at `ListJavaActions`

**Files:**
- Modify: `src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs:2706-2742`

**Background:** Unlike `ReadProjectInfo`, `ListJavaActions` currently calls `HostServices.MicroflowAuthoring.ListJavaActions(null)` for the "all modules" case, and the per-module iteration happens *inside* that Interop call (which is where it throws). Refactor to drive the per-module loop ourselves so we can wrap each call with the helper.

- [ ] **Step 1: Re-read the current method**

Read `MendixAdditionalTools.cs` from line 2706, 40 lines, to confirm exact code.

- [ ] **Step 2: Replace the body**

Find the existing method body and replace with:

```csharp
public async Task<string> ListJavaActions(JsonObject parameters)
{
    await Task.CompletedTask;
    try
    {
        var moduleName = parameters?["module_name"]?.ToString();
        var result = new List<object>();
        var skipped = new List<object>();

        IReadOnlyList<ModuleId> targetModules;
        if (!string.IsNullOrEmpty(moduleName))
        {
            var moduleId = HostServices.Model.GetModuleByName(moduleName);
            if (moduleId == null)
                return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
            targetModules = new[] { moduleId.Value };
        }
        else
        {
            targetModules = HostServices.Model.ListModules();
        }

        foreach (var moduleId in targetModules)
        {
            var jaList = Utils.TryPerModule(moduleId,
                () => HostServices.MicroflowAuthoring.ListJavaActions(moduleId),
                skipped, "ListJavaActions", _logger);
            if (jaList == null) continue;
            foreach (var jad in jaList)
            {
                result.Add(new
                {
                    name = jad.Document.QualifiedName,
                    qualifiedName = jad.Document.QualifiedName,
                    module = jad.Module,
                    parameterCount = jad.ParameterNames.Count,
                    parameters = jad.ParameterNames.Select(p => new { name = p }).ToList<object>()
                });
            }
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            totalJavaActions = result.Count,
            javaActions = result,
            skippedModules = skipped.Count == 0 ? null : skipped,
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error listing Java actions");
        return JsonSerializer.Serialize(new { error = ex.Message });
    }
}
```

- [ ] **Step 3: Ensure `using` for the helper**

Confirm `MendixAdditionalTools.cs` has `using Terminal.Spmcp.Utils;` near the top. If not, add it.

- [ ] **Step 4: Build to verify**

```powershell
dotnet build src/Concord.Core
```

Expected: `Build succeeded`. Watch for `IReadOnlyList<ModuleId>` resolution — if `ModuleId` is a struct, `IReadOnlyList<ModuleId>` works; if it's a class, adjust. Use `IReadOnlyList<>` rather than `List<>` so the both-paths assignment type-checks.

Don't commit yet.

---

### Task 9: Apply `TryPerModule<T>` at `UpdateEnumeration`

**Files:**
- Modify: `src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs:2784+`

**Background:** Single-module path. Helper returns `default(T)`; on null, return a structured error to the MCP client rather than letting the call crash.

- [ ] **Step 1: Read the current method**

Read `MendixDomainModelTools.cs` from line 2784, 100 lines.

- [ ] **Step 2: Wrap the `HostServices.DomainModel.UpdateEnumeration(...)` call**

Find the line at ~2871: `HostServices.DomainModel.UpdateEnumeration(foundEnumRef.Value, ...)`. Wrap with `TryPerModule`:

```csharp
// Before:
//   HostServices.DomainModel.UpdateEnumeration(foundEnumRef.Value, ...);

// After:
var skipped = new List<object>();
var updated = Utils.TryPerModule<bool>(
    moduleId,  // resolved earlier in the method
    () => { HostServices.DomainModel.UpdateEnumeration(foundEnumRef.Value, /* existing args */); return true; },
    skipped, "UpdateEnumeration", _logger);
if (!updated)
{
    return JsonSerializer.Serialize(new {
        success = false,
        error = $"Module '{moduleId.Name}' is not queryable on this Studio Pro version; ModuleProxy not registered. Try a different module or edit the enumeration via the Studio Pro UI.",
        details = skipped,
    });
}
```

Adapt variable names (`moduleId`, existing args) to whatever the surrounding code uses — read the full method to make this edit correctly.

- [ ] **Step 3: Build**

```powershell
dotnet build src/Concord.Core
```

Expected: `Build succeeded`.

---

### Task 10: Deploy, restart Studio Pro, re-sweep, commit Phase 2b commit 1

**Files:** none directly — verification + commit step.

- [ ] **Step 1: Verify deploy to `Test_10_24_13`**

The parent spec flagged: does `DeployToMendix` reach `C:\Projects\Test_10_24_13`?

```powershell
Get-ChildItem C:\Projects\Test_10_24_13\userlib\Concord*.dll | Select-Object Name, LastWriteTime
```

If `LastWriteTime` is from the just-completed `dotnet build` of Task 9, deploy works — skip Step 2. Otherwise:

- [ ] **Step 2 (conditional): Add `Test_10_24_13` to `DeployToMendix` target**

Open `src/Concord.Host10x/Concord.Host10x.csproj` (or wherever `DeployToMendix` is defined — Grep for `<Target Name="DeployToMendix"`). Add `C:\Projects\Test_10_24_13` to the deploy paths. Then re-run `dotnet build` and re-check the timestamp.

- [ ] **Step 3: Restart Studio Pro and confirm Concord MCP server is reachable**

Ask the user to restart Studio Pro and reopen `Test_10_24_13`. Verify:

```powershell
Invoke-WebRequest -Uri http://127.0.0.1:7783/mcp -Method POST -ContentType "application/json" -Body '{"jsonrpc":"2.0","id":1,"method":"initialize"}' -UseBasicParsing | Select-Object -ExpandProperty StatusCode
```

Expected: `200`.

- [ ] **Step 4: Targeted re-sweep**

```powershell
./scripts/concord-mcp-sweep.ps1 -Only read_project_info,list_java_actions,update_enumeration
```

Expected: all 3 PASS. If a tool now returns `success: true` with a non-null `skippedModules`, that's the helper working correctly — count as PASS.

- [ ] **Step 5: Commit Phase 2b commit 1**

```bash
git add src/Concord.Core/Spmcp/Utils/Utils.cs tests/Concord.Core.Tests/SpmcpUtilsTests.cs src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs tests/concord-mcp-sweep/findings.md tests/concord-mcp-sweep/findings.json
git commit -m "$(cat <<'EOF'
fix(spmcp-sweep): TryPerModule helper for 10.x ModuleProxy crashes

Extracted the per-module try/catch+skippedModules idiom already present in
list_enumerations into a shared Utils.TryPerModule<T> helper. Applied at
three Studio Pro 10.x crash sites:

- read_project_info: per-module ListEntities/ListEnumerations/
  ListModuleDocuments walk now skips bad modules instead of failing the
  whole call.
- list_java_actions: refactored from single ListJavaActions(null) call to
  per-module loop so each module can be wrapped independently.
- update_enumeration: single-module path returns structured error on
  ModuleProxy failure instead of crashing.

3 unit tests added covering happy path, KeyNotFound, and re-throw of
unrelated exceptions.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 2b commit 2 — `analyze_project_patterns` `commonDeleteBehavior`

### Task 11: Wire `commonDeleteBehavior` into the `statistics` anon-type

**Files:**
- Modify: `src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs:4644-4720` (verify with Read)

**Background:** `MendixAdditionalTools.cs:4856` and `:4894` read `(string)statistics.commonDeleteBehavior`, but the `statistics` anon-type assembled at line 4644 doesn't include that property. The data is computed elsewhere (likely as part of the association walk earlier in the same method).

- [ ] **Step 1: Read the `statistics` anon-type assembly and search for the computed value**

Read `MendixAdditionalTools.cs` from line 4620, 100 lines, to see the full `statistics` assembly site.

Then Grep up the same method for the variable that holds the per-association `deleteBehavior` aggregation:

```
Grep "deleteBehavior" in src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs
```

The aggregation is likely a `Dictionary<string,int>` of delete-behavior name → count, with the "most common" computed via `.OrderByDescending(kv => kv.Value).First().Key`.

- [ ] **Step 2: Identify whether the aggregation exists**

Two cases:

**Case A: The aggregation already exists** — variable holding it is in scope. Just add the projection:

```diff
   var statistics = new
   {
       totalEntities,
       totalAssociations,
       // ... existing fields ...
       eventHandlerDistribution = eventHandlerTypes,
       entitiesWithCreatedDate = new { ... },
+      commonDeleteBehavior = deleteBehaviorCounts.Count > 0
+          ? deleteBehaviorCounts.OrderByDescending(kv => kv.Value).First().Key
+          : "n/a",
       // ... rest ...
   };
```

(Adjust the variable name `deleteBehaviorCounts` to match the actual local in scope.)

**Case B: The aggregation doesn't exist** — need to compute it. In the existing per-association walk earlier in the method (search for the loop that increments `assocOneToMany`/`assocManyToMany`), add:

```csharp
var deleteBehaviorCounts = new Dictionary<string, int>();
// inside the per-association loop:
var db = assoc.ChildDeleteBehavior?.ToString() ?? "Unknown";
deleteBehaviorCounts[db] = deleteBehaviorCounts.GetValueOrDefault(db) + 1;
```

Then add the projection from Case A.

- [ ] **Step 3: Build**

```powershell
dotnet build src/Concord.Core
```

Expected: `Build succeeded`.

- [ ] **Step 4: Deploy verify + Studio Pro restart**

Same shape as Task 10 Steps 1–3. Ask user to restart Studio Pro.

- [ ] **Step 5: Targeted re-sweep**

```powershell
./scripts/concord-mcp-sweep.ps1 -Only analyze_project_patterns
```

Expected: PASS.

- [ ] **Step 6: Commit Phase 2b commit 2**

```bash
git add src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs tests/concord-mcp-sweep/findings.md tests/concord-mcp-sweep/findings.json
git commit -m "$(cat <<'EOF'
fix(spmcp-sweep): wire commonDeleteBehavior into analyze_project_patterns

The statistics anon-type read at MendixAdditionalTools.cs:4856 and :4894
was missing the commonDeleteBehavior property, so the tool crashed with
"anonymous type does not contain a definition for 'commonDeleteBehavior'".
Wire the aggregation through the statistics projection.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase 2b commit 3 — Real bugs investigation (3 suspects)

### Task 12: Investigate `check_project_errors`, `set_runtime_settings`, microflow-activity sequencing

**Files:** None up front — investigation step that drives Task 13.

**Background:** These 3 tools (5 entries: `check_project_errors`, `set_runtime_settings`, `create_microflow_activity`, `modify_microflow_activity`, `insert_before_activity`) returned `success:false` in Phase 1 with little error context. Source dive determines whether each is a real bug or a matrix issue.

- [ ] **Step 1: Read each tool's implementation**

Locate via Grep — these methods live in `MendixAdditionalTools.cs` or `MendixDomainModelTools.cs`:

```
Grep "public.*Task<string>\s+CheckProjectErrors\b" in src/Concord.Core/Spmcp/Tools
Grep "public.*Task<string>\s+SetRuntimeSettings\b" in src/Concord.Core/Spmcp/Tools
Grep "public.*Task<string>\s+CreateMicroflowActivity\b" in src/Concord.Core/Spmcp/Tools
Grep "public.*Task<string>\s+ModifyMicroflowActivity\b" in src/Concord.Core/Spmcp/Tools
Grep "public.*Task<string>\s+InsertBeforeActivity\b" in src/Concord.Core/Spmcp/Tools
```

Read each method body and look for the `success: false` return path. Classify:

| Bucket | Meaning | Action |
|---|---|---|
| Real C# bug | Code branches into `success:false` from a real logic error | Fix in this commit |
| Stub-pattern | Returns `escalation: "manual"` like the Security tools | Roll into a Phase 2a follow-up (reclassify matrix to `either`) |
| Matrix arg-shape bug | Tool rejects the args we sent, but a different shape would work | Fix matrix args; not a code change |
| Sequencing issue | Tool requires prior state the matrix doesn't establish | Fix matrix (e.g., add to mutate-phase pre-step, or change `position` semantics) |

- [ ] **Step 2: Document classifications inline**

Open `tests/concord-mcp-sweep/findings.md` and append at the bottom, under a new section `## Phase 2b commit 3 investigation:`

For each of the 5 entries:
```markdown
- `check_project_errors`: <classification> — <one-line rationale>
- `set_runtime_settings`: <classification> — <one-line rationale>
- `create_microflow_activity`: <classification> — <one-line rationale>
- `modify_microflow_activity`: <classification> — <one-line rationale>
- `insert_before_activity`: <classification> — <one-line rationale>
```

- [ ] **Step 3: Time-box check**

If any one bug's investigation exceeds 2 hours of source diving, stop and defer. Update the resolution annotation in `findings.md` to `Deferred to follow-up: <reason>`. The success criteria explicitly allows this.

---

### Task 13: Apply Task 12 classifications

**Files:** depends on classifications from Task 12.

**Concrete strategy by classification:**

- [ ] **Step 1: Fix real C# bugs (if any)**

For each entry classified as "Real C# bug" in Task 12: write a test, fix the code, verify with `dotnet test` and a targeted sweep. Commit per bug:

```bash
git commit -m "fix(spmcp-sweep): <tool_name> — <one-line bug summary>"
```

- [ ] **Step 2: Reclassify stub-patterns and matrix issues**

For each entry classified as "Stub-pattern" or "Matrix arg-shape bug" or "Sequencing issue":
- Edit `tests/concord-mcp-sweep/matrix.jsonc` (reclassify to `either` OR change args OR move to setup/mutate phase as appropriate)
- Append rationale to the entry's `notes` field
- Bundle all matrix-only fixes into one commit:

```bash
git add tests/concord-mcp-sweep/matrix.jsonc tests/concord-mcp-sweep/findings.md
git commit -m "fix(spmcp-sweep): matrix patches for Task 12 classifications"
```

- [ ] **Step 3: Targeted re-sweep**

For all 5 entries (regardless of fix shape):

```powershell
./scripts/concord-mcp-sweep.ps1 -Only check_project_errors,set_runtime_settings,create_microflow_activity,modify_microflow_activity,insert_before_activity
```

Expected: all PASS, OR FAIL with resolution annotation pointing at a deferred follow-up.

---

## Phase 3 close-out

### Task 14: Final full sweep + Phase 4 manual checklist + auto-memory

**Files:**
- Modify: `tests/concord-mcp-sweep/findings.md`
- Create: `~/.claude/projects/c--Extensions-Terminal/memory/project_concord_mcp_tool_sweep.md`

- [ ] **Step 1: Final full sweep**

```powershell
./scripts/concord-mcp-sweep.ps1
```

Expected: ≥80/93 PASS, all remaining FAILs annotated `Deferred to follow-up: <reason>` in `findings.md`. Any new unanticipated FAIL is a regression — investigate before close-out.

- [ ] **Step 2: Add Phase 4 manual checklist to `findings.md`**

Append to `findings.md` (carry over from parent spec §"Phase 5"):

```markdown
## Phase 4 — Manual Studio Pro verification

After all sweep-driven fixes are green, manually verify in Studio Pro:

- [ ] UI redraw: does the domain-model designer reflect MCP-driven entity changes immediately, or only after `refresh_project`?
- [ ] Undo stack: does Ctrl-Z roll back MCP-driven mutations cleanly?
- [ ] Focus / modal interference: does `run_app` steal focus from the terminal pane? Does `stop_app` leave a stale "running" pill?
- [ ] Settings modal: does a `set_runtime_settings` change reflect when the Settings modal is reopened?
- [ ] Concurrent edits: start an entity rename in the UI, fire `rename_attribute` via MCP mid-keystroke. What happens?
```

Ask the user to fill it in by hand. Don't fake any boxes.

- [ ] **Step 3: Write/update auto-memory**

Write `~/.claude/projects/c--Extensions-Terminal/memory/project_concord_mcp_tool_sweep.md`:

```markdown
---
name: project-concord-mcp-tool-sweep
description: Phase 1/2/3 MCP tool sweep — harness, findings, fix patterns, and Studio Pro 10.x ModuleProxy quirk
metadata:
  type: project
---

Concord MCP tool sweep — landed on `feat/v5.0.0-w2-mcpx-merge`.

**Harness:** `scripts/concord-mcp-sweep.ps1` + `tests/concord-mcp-sweep/matrix.jsonc` exercise all ~93 MCP tools. Phases: setup → read → mutate → lifecycle. Findings written to `tests/concord-mcp-sweep/findings.{md,json}` incrementally.

**Re-run a single tool:**
```
./scripts/concord-mcp-sweep.ps1 -Only <tool_name>
```

**Outcome:** Phase 1 baseline 55/88 PASS → final <NN>/93 PASS after Phase 2/3.

**Studio Pro 10.x ModuleProxy quirk (most important takeaway):** Mendix's ExtensionsAPI throws `KeyNotFoundException` for `ModuleProxy` when called against system / App-Store-imported modules. Fix pattern: wrap per-module ExtensionsAPI calls with `Utils.TryPerModule<T>` (in `src/Concord.Core/Spmcp/Utils/Utils.cs`). Helper returns `default(T)` on the known crash pattern and records the skip in a `skippedModules[]` list; non-matching exceptions re-throw. Applied at `read_project_info`, `list_java_actions`, `update_enumeration` — any new per-module tool should use the helper.

**Why:** Phase 1 found 4 distinct ModuleProxy crashes that all came from the same Mendix bug. One helper, three call sites.

**How to apply:** When you write any new MCP tool that iterates `HostServices.Model.ListModules()` and calls per-module ExtensionsAPI methods, wrap each call with `Utils.TryPerModule<T>` and surface `skippedModules` in the tool's response payload. See `list_enumerations` for the established template.

**Documented stubs (don't get bitten again):** ~10 MCP tools intentionally return `{ success: false, escalation: "manual", message: ... }` because the typed Interop doesn't expose the surface yet. They're not bugs — matrix entries carry `expected: "either"` so they don't pollute the FAIL count. See the Security family (5 tools) and Constants writes (3 tools) for the pattern.

**Task 15 / W4 backlog:** `get_app_status`, `get_active_run_configuration` (and downstream `run_app`/`stop_app` polling) are NotImplementedException stubs in `Concord.Host10x\Interop\*Host10x.cs`. Deferred to Task 15 / W4 branch — they're not on `feat/v5.0.0-w2-mcpx-merge`.
```

Also update `MEMORY.md` index:

```markdown
- [Concord MCP tool sweep](project_concord_mcp_tool_sweep.md) — harness, findings, ModuleProxy 10.x quirk
```

- [ ] **Step 4: Mark Phase 3 close-out commit**

```bash
git add tests/concord-mcp-sweep/findings.md
git commit -m "$(cat <<'EOF'
docs(spmcp-sweep): Phase 3 close-out — Phase 4 manual checklist + final baseline

Final sweep baseline: <NN>/93 PASS. Remaining FAILs annotated with
deferral rationale. Manual Studio Pro verification checklist appended
for Neo to fill in.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-review checklist (against spec)

- ✅ Matrix `setup → read → mutate → lifecycle` ordering — Tasks 1, 3
- ✅ 10 escalation:manual stubs reclassified `expected:"either"` with audit notes — Task 2 (Steps 1–3)
- ✅ 4 Task-15-dependent tools reclassified — Task 2 (Step 4)
- ✅ `TryPerModule<T>` helper + ≥3 unit tests — Tasks 5, 6
- ✅ ModuleProxy CRASH severities resolved at 3 call sites (read_project_info, list_java_actions, update_enumeration) — Tasks 7, 8, 9
- ✅ `analyze_project_patterns` `commonDeleteBehavior` fix — Task 11
- ✅ Remaining 3 real-bug investigation (check_project_errors, set_runtime_settings, microflow-activity) with classification + fix-or-defer — Tasks 12, 13
- ✅ Final full-sweep verification + ≥80 PASS — Task 14 Step 1
- ✅ Phase 4 manual checklist appended to findings.md — Task 14 Step 2
- ✅ Auto-memory updated — Task 14 Step 3
- ✅ Atomic-commit cadence: 1 (Phase 2a) + 1 (TryPerModule) + 1 (commonDeleteBehavior) + 1 (Task 13) + 1 (close-out) = 5 commits maximum
