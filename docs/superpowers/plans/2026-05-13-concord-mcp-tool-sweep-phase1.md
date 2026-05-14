# Concord MCP Tool Sweep — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the harness — a PowerShell driver, a JSONC test matrix, and a schema-discovery audit trail — then run the sweep against the live Concord MCP server at `http://127.0.0.1:7783/mcp` and commit the findings artifacts for triage.

**Architecture:** A single PowerShell driver (`scripts/concord-mcp-sweep.ps1`) reads a checked-in test matrix (`tests/concord-mcp-sweep/matrix.jsonc`), POSTs each entry as JSON-RPC `tools/call`, classifies the response, and emits two ledgers: machine-replayable `findings.json` and human-triage `findings.md`. Schemas for each tool are reverse-engineered from `MendixDomainModelTools.cs` and `MendixAdditionalTools.cs` and documented in `arg-shapes.md`.

**Tech Stack:** PowerShell 5.1 (Windows-bundled), `Invoke-WebRequest` for HTTP, native JSON cmdlets (`ConvertFrom-Json`, `ConvertTo-Json`). No new test framework; smoke-validation is by direct script invocation against the live server.

**Scope boundary:** This plan covers Phase 0 (pre-flight), Phase 1 (sweep execution), and produces the artifacts that feed Phase 2 (triage). Phase 3 (fixes), Phase 4 (re-test), and Phase 5 (Studio Pro verification) are out of scope and will be planned in a follow-up after triage.

**Prerequisites at execution time:**
- Concord MCP must be running and reachable at `http://127.0.0.1:7783/mcp`. Verify with: `curl -s -m 5 -X POST http://127.0.0.1:7783/mcp -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'` → returns a JSON envelope with `serverInfo.name = "concord-mcp"`.
- Mendix project `C:\Projects\Test_10_24_13` must be open in Studio Pro with `MyFirstModule` and `Sales` present (both empty user modules — the mutation targets).
- The two Monitors armed in setup (`bg2mvujvz` Studio Pro log, `bucwn0b2u` terminal.log) should remain armed throughout execution to catch any out-of-band errors.

**Concord MCP server details (for reference in tasks):**
- Endpoint: `POST http://127.0.0.1:7783/mcp`
- Body format: JSON-RPC 2.0, `{"jsonrpc":"2.0","id":N,"method":"tools/call","params":{"name":"X","arguments":{...}}}`
- Response envelope: `{"jsonrpc":"2.0","id":N,"result":{"content":[{"type":"text","text":"<json-encoded-payload>"}],"isError":false}}`
- The actual tool payload is JSON-encoded as a string inside `result.content[0].text`. Parse twice (outer envelope, then inner text).

**Tool source files (used heavily in Task 5 — schema discovery):**
- [src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs](../../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs) — ~50 of the 87 tools
- [src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs](../../../src/Concord.Core/Spmcp/Tools/MendixAdditionalTools.cs) — the remaining ~37 tools

---

## Task 1: Bootstrap directories and seed files

**Files:**
- Create: `tests/concord-mcp-sweep/matrix.jsonc`
- Create: `tests/concord-mcp-sweep/arg-shapes.md`
- Create: `tests/concord-mcp-sweep/.gitignore`

- [ ] **Step 1: Create the directory and the empty matrix scaffold**

Use the Write tool to create `tests/concord-mcp-sweep/matrix.jsonc` with this content:

```jsonc
// tests/concord-mcp-sweep/matrix.jsonc
// One entry per Concord MCP tool. See arg-shapes.md for the source-of-truth
// for each tool's argument shape, and the design spec at
// docs/superpowers/specs/2026-05-13-concord-mcp-tool-sweep-design.md for
// the classification rules.
//
// Entry schema:
//   name:     server-side tool name (must match tools/list)
//   family:   for findings.md grouping (DomainModel, Microflows, etc.)
//   phase:    "read" | "mutate" | "lifecycle"  — controls execution order
//   args:     exact JSON object sent as params.arguments
//   expected: "ok" | "error" | "either"
//   verify:   (optional) follow-up read on risky mutators; result lands
//             in side_effect_check sub-record of findings.json
//   notes:    free-text intent (especially for known-bug entries)
[
]
```

- [ ] **Step 2: Create the arg-shapes.md scaffold**

Use the Write tool to create `tests/concord-mcp-sweep/arg-shapes.md` with this content:

```markdown
# Concord MCP — argument shapes (reverse-engineered)

> Source of truth for the matrix.jsonc entries. For each tool, derived by
> grepping `MendixDomainModelTools.cs` and `MendixAdditionalTools.cs` for the
> `parameters?["X"]?.ToString()` access pattern and the explicit "X is
> required" guards. Where source allows multiple shapes, the most-likely-
> success shape is documented along with the ambiguity.

## Conventions

- **Required fields** are listed first; **optional** below them with default.
- File-line cite is to the C# method body, not the dispatch table.
- "Notes" captures source-visible quirks (e.g. ToString on a JsonArray; a
  branch for both string and array shape on the same field).

---
```

- [ ] **Step 3: Create a .gitignore so findings artifacts can be reviewed-before-commit**

Use the Write tool to create `tests/concord-mcp-sweep/.gitignore` with this content:

```
# Sweep output artifacts. Re-generated each run; commit deliberately, not
# automatically (see plan Task 9).
findings.json
findings.md
findings.json.tmp
```

- [ ] **Step 4: Commit**

```bash
git add tests/concord-mcp-sweep/matrix.jsonc tests/concord-mcp-sweep/arg-shapes.md tests/concord-mcp-sweep/.gitignore
git commit -m "$(cat <<'EOF'
chore(spmcp-sweep): bootstrap tool-sweep harness directory

Empty scaffolds for matrix.jsonc + arg-shapes.md + .gitignore.
Implementation follows in subsequent commits per the Phase 1 plan
at docs/superpowers/plans/2026-05-13-concord-mcp-tool-sweep-phase1.md.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Driver core — pre-flight + single-tool dispatch

**Files:**
- Create: `scripts/concord-mcp-sweep.ps1`

This task builds the smallest driver that can do an `initialize` handshake, fetch `tools/list`, and dispatch one `tools/call`. No classification or persistence yet — just plumbing.

- [ ] **Step 1: Create the driver skeleton with parameter binding**

Use the Write tool to create `scripts/concord-mcp-sweep.ps1` with this content:

```powershell
<#
.SYNOPSIS
    Sweep all Concord MCP tools and capture findings.
.DESCRIPTION
    Reads tests/concord-mcp-sweep/matrix.jsonc, POSTs each entry to the
    live concord-mcp server, classifies the response, and writes
    findings.json + findings.md to OutDir.
.PARAMETER Matrix
    Path to the test matrix.
.PARAMETER OutDir
    Directory for findings.json/findings.md output.
.PARAMETER Endpoint
    Concord MCP endpoint. Default: http://127.0.0.1:7783/mcp.
.PARAMETER Only
    Comma-list of tool names to run (subset of the matrix). Used for re-test.
.PARAMETER Phase
    Comma-list of phases to run: read, mutate, lifecycle.
.PARAMETER DryRun
    Print the planned execution without POSTing anything.
.EXAMPLE
    ./scripts/concord-mcp-sweep.ps1
    ./scripts/concord-mcp-sweep.ps1 -Only list_modules,read_project_info
    ./scripts/concord-mcp-sweep.ps1 -Phase read -DryRun
#>
[CmdletBinding()]
param(
    [string]$Matrix = "tests/concord-mcp-sweep/matrix.jsonc",
    [string]$OutDir = "tests/concord-mcp-sweep",
    [string]$Endpoint = "http://127.0.0.1:7783/mcp",
    [string[]]$Only = @(),
    [string[]]$Phase = @(),
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# --- JSON-RPC helpers ---------------------------------------------------

function Invoke-McpRpc {
    param(
        [Parameter(Mandatory)][string]$Method,
        [hashtable]$Params = @{},
        [int]$TimeoutSec = 30
    )
    $body = @{
        jsonrpc = "2.0"
        id      = [int](Get-Random -Maximum 999999)
        method  = $Method
        params  = $Params
    } | ConvertTo-Json -Depth 20 -Compress

    $resp = Invoke-WebRequest -Uri $Endpoint -Method POST `
        -ContentType "application/json" -Body $body `
        -TimeoutSec $TimeoutSec -UseBasicParsing
    return ($resp.Content | ConvertFrom-Json)
}

function Invoke-McpInitialize {
    return (Invoke-McpRpc -Method "initialize")
}

function Get-McpToolList {
    $env = Invoke-McpRpc -Method "tools/list"
    return $env.result.tools
}

function Invoke-McpToolCall {
    param(
        [Parameter(Mandatory)][string]$Name,
        [object]$Arguments = @{}
    )
    $params = @{ name = $Name; arguments = $Arguments }
    return (Invoke-McpRpc -Method "tools/call" -Params $params)
}

# --- Pre-flight ---------------------------------------------------------

Write-Host "[concord-mcp-sweep] pre-flight" -ForegroundColor Cyan
$init = Invoke-McpInitialize
Write-Host "  server: $($init.result.serverInfo.name) v$($init.result.serverInfo.version)"

$serverTools = Get-McpToolList
Write-Host "  tools/list returned $($serverTools.Count) tools"

Write-Host "[concord-mcp-sweep] driver core OK (pre-flight only — full execution lands in later tasks)" -ForegroundColor Green
```

- [ ] **Step 2: Smoke test pre-flight against the live server**

Run from PowerShell:

```powershell
./scripts/concord-mcp-sweep.ps1
```

Expected output:
```
[concord-mcp-sweep] pre-flight
  server: concord-mcp v1.3.0
  tools/list returned 87 tools
[concord-mcp-sweep] driver core OK (pre-flight only — full execution lands in later tasks)
```

If the version string differs (server bumped to 1.3.1 etc.) that's fine — the assertion is just "concord-mcp" and a count of 87.

If you get a connection error: confirm Studio Pro is running with Concord pane open. If `tools/list` returns fewer than 87 tools, that's itself a finding — stop and report.

- [ ] **Step 3: Smoke test a one-shot tool call**

Run from PowerShell:

```powershell
. ./scripts/concord-mcp-sweep.ps1
# After the script defines Invoke-McpToolCall, this should work in the same session:
$r = Invoke-McpToolCall -Name "list_modules" -Arguments @{}
$r.result.content[0].text | ConvertFrom-Json | Select-Object -ExpandProperty modules | Select-Object name
```

Expected: a list of 10 module names including `Administration`, `Sales`, `MyFirstModule`. If you get fewer than 10 or an error, the project isn't loaded correctly.

(Note: PowerShell dot-sourcing re-runs the pre-flight block, so you'll see the pre-flight output once before the function becomes callable. That's expected; this is just a smoke test, not the production execution path.)

- [ ] **Step 4: Commit**

```bash
git add scripts/concord-mcp-sweep.ps1
git commit -m "$(cat <<'EOF'
feat(spmcp-sweep): driver pre-flight (initialize + tools/list + single dispatch)

JSON-RPC helpers, initialize handshake, tools/list fetch, and an
Invoke-McpToolCall function. Smoke-tested against the live server:
returns 87 tools and list_modules returns the 10 expected modules.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Driver — matrix loader + JSON-RPC dispatch + classification

**Files:**
- Modify: `scripts/concord-mcp-sweep.ps1`
- Modify: `tests/concord-mcp-sweep/matrix.jsonc` (add one seed entry for smoke testing)

This task wires the matrix into the driver and adds the classification ladder. Output is console-only for now — persistence comes in Task 4.

- [ ] **Step 1: Add a single seed matrix entry for smoke testing**

Edit `tests/concord-mcp-sweep/matrix.jsonc` and replace the empty `[]` array body with:

```jsonc
[
  {
    "name": "list_modules",
    "family": "DomainModel",
    "phase": "read",
    "args": {},
    "expected": "ok",
    "notes": "No args required; returns all 10 modules. Used as classification smoke test."
  }
]
```

- [ ] **Step 2: Add the matrix loader to the driver**

Append the following section to `scripts/concord-mcp-sweep.ps1` immediately above the existing `# --- Pre-flight ---` comment block (so it loads matrix before pre-flight, in case pre-flight wants to cross-check):

```powershell
# --- Matrix loader -----------------------------------------------------

function Read-SweepMatrix {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path $Path)) {
        throw "Matrix file not found: $Path"
    }
    # JSONC: strip `// ... \n` and `/* ... */` comments before parsing.
    $raw = Get-Content -Path $Path -Raw
    $stripped = [regex]::Replace($raw, '(?ms)/\*.*?\*/', '')
    $stripped = [regex]::Replace($stripped, '(?m)//[^\n]*', '')
    return ($stripped | ConvertFrom-Json)
}
```

- [ ] **Step 3: Add classification logic to the driver**

Append the following section to `scripts/concord-mcp-sweep.ps1` after the matrix loader:

```powershell
# --- Classification ----------------------------------------------------

function Get-SeveritySuggestion {
    param([string]$ErrorText)
    if (-not $ErrorText) { return $null }
    if ($ErrorText -match 'KeyNotFound|NullReference|InvalidOperation') { return 'CRASH' }
    if ($ErrorText -match 'is required|must be|JsonArray|Invalid request format') { return 'SCHEMA' }
    if ($ErrorText -match 'not found.*directory|11\.5\.0|11\.10\.0|hardcoded|version mismatch') { return 'STALE' }
    if ($ErrorText -match 'not implemented yet') { return 'BUG' }
    return 'BUG'
}

function Test-SweepEntry {
    param([Parameter(Mandatory)][object]$Entry)

    $started = Get-Date
    $rawEnvelope = $null
    $errorSummary = $null
    $severity = $null
    $status = $null

    try {
        $args = if ($null -eq $Entry.args) { @{} } else { $Entry.args }
        $rawEnvelope = Invoke-McpToolCall -Name $Entry.name -Arguments $args
    } catch {
        $status = "FAIL"
        $severity = "TRANSPORT"
        $errorSummary = $_.Exception.Message
    }

    if ($null -eq $status) {
        # JSON-RPC envelope-level error
        if ($rawEnvelope.error) {
            $status = "FAIL"
            $severity = "TRANSPORT"
            $errorSummary = "$($rawEnvelope.error.code): $($rawEnvelope.error.message)"
        }
        # Server-side exception
        elseif ($rawEnvelope.result.isError -eq $true) {
            $status = "FAIL"
            $severity = "CRASH"
            $errorSummary = $rawEnvelope.result.content[0].text
        }
        else {
            # Parse the inner tool payload (it's a JSON-encoded string in content[0].text)
            $payload = $null
            try {
                $payload = $rawEnvelope.result.content[0].text | ConvertFrom-Json
            } catch {
                # Some tools may return non-JSON text payloads (e.g. plain strings).
                # Treat as PASS if expected ok/either, FAIL if expected error.
                $payload = @{ success = $true }
            }

            $hasError = ($null -ne $payload.error) -or ($payload.success -eq $false)
            switch ($Entry.expected) {
                "ok" {
                    if ($hasError) {
                        $status = "FAIL"
                        $errorSummary = if ($payload.error) { [string]$payload.error } else { "success:false" }
                        $severity = Get-SeveritySuggestion -ErrorText $errorSummary
                    } else {
                        $status = "PASS"
                    }
                }
                "error" {
                    if ($hasError) {
                        $status = "PASS"
                    } else {
                        $status = "FAIL"
                        $severity = "BUG"
                        $errorSummary = "expected structured error, got success"
                    }
                }
                "either" {
                    $status = "PASS"
                }
                default {
                    throw "Invalid expected value '$($Entry.expected)' for tool '$($Entry.name)'"
                }
            }
        }
    }

    $elapsedMs = [int]((Get-Date) - $started).TotalMilliseconds
    return [PSCustomObject]@{
        name          = $Entry.name
        family        = $Entry.family
        phase         = $Entry.phase
        status        = $status
        expected      = $Entry.expected
        args          = $Entry.args
        raw_response  = $rawEnvelope
        error_summary = $errorSummary
        severity      = $severity
        elapsed_ms    = $elapsedMs
        timestamp     = (Get-Date).ToUniversalTime().ToString("o")
    }
}
```

- [ ] **Step 4: Replace the existing trailing log line with a real execution loop**

In `scripts/concord-mcp-sweep.ps1`, replace this line:

```powershell
Write-Host "[concord-mcp-sweep] driver core OK (pre-flight only — full execution lands in later tasks)" -ForegroundColor Green
```

with:

```powershell
$entries = Read-SweepMatrix -Path $Matrix
Write-Host "  matrix has $($entries.Count) entries"

if ($DryRun) {
    Write-Host "[concord-mcp-sweep] -DryRun: planned execution" -ForegroundColor Yellow
    $entries | ForEach-Object { Write-Host ("    [{0}] {1}.{2}" -f $_.phase, $_.family, $_.name) }
    exit 0
}

Write-Host "[concord-mcp-sweep] executing" -ForegroundColor Cyan
$results = @()
foreach ($entry in $entries) {
    Write-Host -NoNewline ("  -> {0} ... " -f $entry.name)
    $r = Test-SweepEntry -Entry $entry
    $color = if ($r.status -eq "PASS") { "Green" } else { "Red" }
    Write-Host ("{0} ({1} ms){2}" -f $r.status, $r.elapsed_ms, $(if ($r.severity) { " [$($r.severity)]" } else { "" })) -ForegroundColor $color
    $results += $r
}
Write-Host "[concord-mcp-sweep] complete: $($results.Where{$_.status -eq 'PASS'}.Count) PASS / $($results.Where{$_.status -eq 'FAIL'}.Count) FAIL / $($results.Where{$_.status -eq 'SKIP'}.Count) SKIP" -ForegroundColor Cyan
```

- [ ] **Step 5: Smoke test against the seed entry**

Run from PowerShell:

```powershell
./scripts/concord-mcp-sweep.ps1
```

Expected output (last few lines):
```
  matrix has 1 entries
[concord-mcp-sweep] executing
  -> list_modules ... PASS (xxx ms)
[concord-mcp-sweep] complete: 1 PASS / 0 FAIL / 0 SKIP
```

- [ ] **Step 6: Smoke test classification on a known-FAIL tool**

Edit `tests/concord-mcp-sweep/matrix.jsonc` and append a second entry inside the array (before the closing `]`):

```jsonc
  ,{
    "name": "read_project_info",
    "family": "DomainModel",
    "phase": "read",
    "args": {},
    "expected": "ok",
    "notes": "KNOWN FAIL pre-sweep: ModuleProxy KeyNotFound (MendixDomainModelTools.cs:769-775)."
  }
```

Run again:

```powershell
./scripts/concord-mcp-sweep.ps1
```

Expected:
```
  -> list_modules ... PASS (xxx ms)
  -> read_project_info ... FAIL (xxx ms) [CRASH]
[concord-mcp-sweep] complete: 1 PASS / 1 FAIL / 0 SKIP
```

The `[CRASH]` severity comes from the `KeyNotFound` regex match in `Get-SeveritySuggestion`. If severity comes back blank or different, fix the regex before proceeding.

- [ ] **Step 7: Smoke test -DryRun**

```powershell
./scripts/concord-mcp-sweep.ps1 -DryRun
```

Expected output (last few lines):
```
  matrix has 2 entries
[concord-mcp-sweep] -DryRun: planned execution
    [read] DomainModel.list_modules
    [read] DomainModel.read_project_info
```

No HTTP calls should land on the server during DryRun.

- [ ] **Step 8: Commit**

```bash
git add scripts/concord-mcp-sweep.ps1 tests/concord-mcp-sweep/matrix.jsonc
git commit -m "$(cat <<'EOF'
feat(spmcp-sweep): matrix loader + JSON-RPC dispatch + classification

Adds Read-SweepMatrix (JSONC), Test-SweepEntry (classification ladder
per spec), and a console-only execution loop. Seed matrix contains
list_modules (known PASS) and read_project_info (known CRASH —
ModuleProxy KeyNotFound). -DryRun prints planned execution without
calling the server.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Driver — ledger writers (findings.json + findings.md)

**Files:**
- Modify: `scripts/concord-mcp-sweep.ps1`

This task wires in atomic-write JSON persistence and a Markdown renderer that runs after every entry, so partial findings survive interrupts.

- [ ] **Step 1: Add the JSON writer**

Append the following section to `scripts/concord-mcp-sweep.ps1` after the `# --- Classification ---` section:

```powershell
# --- Ledger writers ----------------------------------------------------

function Write-FindingsJson {
    param(
        [Parameter(Mandatory)][object[]]$Results,
        [Parameter(Mandatory)][string]$Path
    )
    $tmp = "$Path.tmp"
    ($Results | ConvertTo-Json -Depth 30) | Set-Content -Path $tmp -Encoding utf8
    # Atomic rename so a crash mid-write can't leave a half-file at $Path.
    Move-Item -Path $tmp -Destination $Path -Force
}
```

- [ ] **Step 2: Add the Markdown renderer**

Append the following section to `scripts/concord-mcp-sweep.ps1` immediately after `Write-FindingsJson`:

```powershell
function Write-FindingsMarkdown {
    param(
        [Parameter(Mandatory)][object[]]$Results,
        [Parameter(Mandatory)][string]$Path
    )
    $pass = ($Results | Where-Object { $_.status -eq 'PASS' }).Count
    $fail = ($Results | Where-Object { $_.status -eq 'FAIL' }).Count
    $skip = ($Results | Where-Object { $_.status -eq 'SKIP' }).Count
    $total = $Results.Count

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# Concord MCP tool sweep — findings")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("Generated: $(Get-Date -Format o)  ")
    [void]$sb.AppendLine("Endpoint: ``$Endpoint``  ")
    [void]$sb.AppendLine("Matrix: ``$Matrix``")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Summary")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Status | Count |")
    [void]$sb.AppendLine("|---|---|")
    [void]$sb.AppendLine("| PASS | $pass / $total |")
    [void]$sb.AppendLine("| FAIL | $fail / $total |")
    [void]$sb.AppendLine("| SKIP | $skip / $total |")
    [void]$sb.AppendLine("")

    # Banner if read phase is mostly broken
    $readResults = $Results | Where-Object { $_.phase -eq 'read' }
    if ($readResults.Count -gt 0) {
        $readFailRatio = ($readResults | Where-Object { $_.status -eq 'FAIL' }).Count / $readResults.Count
        if ($readFailRatio -gt 0.5) {
            [void]$sb.AppendLine("> **⚠ LIKELY SERVER MISCONFIGURATION** — more than 50% of read-phase tools failed. Investigate server/project state before triaging individual entries.")
            [void]$sb.AppendLine("")
        }
    }

    $families = $Results | Select-Object -ExpandProperty family -Unique | Sort-Object
    foreach ($fam in $families) {
        [void]$sb.AppendLine("## $fam")
        [void]$sb.AppendLine("")
        $famResults = $Results | Where-Object { $_.family -eq $fam }
        $famFails = $famResults | Where-Object { $_.status -eq 'FAIL' }
        $famPasses = $famResults | Where-Object { $_.status -eq 'PASS' }

        if ($famFails.Count -gt 0) {
            [void]$sb.AppendLine("### Failures")
            [void]$sb.AppendLine("")
            foreach ($r in $famFails) {
                $sev = if ($r.severity) { $r.severity } else { "unclassified" }
                [void]$sb.AppendLine("#### ``$($r.name)`` — **$sev**")
                [void]$sb.AppendLine("")
                [void]$sb.AppendLine("- Phase: ``$($r.phase)``")
                [void]$sb.AppendLine("- Expected: ``$($r.expected)``")
                [void]$sb.AppendLine("- Elapsed: $($r.elapsed_ms) ms")
                [void]$sb.AppendLine("- Args:")
                [void]$sb.AppendLine('  ```json')
                [void]$sb.AppendLine("  $($r.args | ConvertTo-Json -Depth 10 -Compress)")
                [void]$sb.AppendLine('  ```')
                [void]$sb.AppendLine("- Error: $($r.error_summary)")
                [void]$sb.AppendLine("- Resolution: _pending triage_")
                [void]$sb.AppendLine("")
            }
        }

        if ($famPasses.Count -gt 0) {
            [void]$sb.AppendLine("### Passes")
            [void]$sb.AppendLine("")
            foreach ($r in $famPasses) {
                [void]$sb.AppendLine("- ``$($r.name)`` ($($r.elapsed_ms) ms)")
            }
            [void]$sb.AppendLine("")
        }
    }

    Set-Content -Path $Path -Value $sb.ToString() -Encoding utf8
}
```

- [ ] **Step 3: Wire the writers into the execution loop**

In the existing execution loop in `scripts/concord-mcp-sweep.ps1`, replace this block:

```powershell
Write-Host "[concord-mcp-sweep] executing" -ForegroundColor Cyan
$results = @()
foreach ($entry in $entries) {
    Write-Host -NoNewline ("  -> {0} ... " -f $entry.name)
    $r = Test-SweepEntry -Entry $entry
    $color = if ($r.status -eq "PASS") { "Green" } else { "Red" }
    Write-Host ("{0} ({1} ms){2}" -f $r.status, $r.elapsed_ms, $(if ($r.severity) { " [$($r.severity)]" } else { "" })) -ForegroundColor $color
    $results += $r
}
Write-Host "[concord-mcp-sweep] complete: $($results.Where{$_.status -eq 'PASS'}.Count) PASS / $($results.Where{$_.status -eq 'FAIL'}.Count) FAIL / $($results.Where{$_.status -eq 'SKIP'}.Count) SKIP" -ForegroundColor Cyan
```

with:

```powershell
$jsonPath = Join-Path $OutDir "findings.json"
$mdPath = Join-Path $OutDir "findings.md"
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

Write-Host "[concord-mcp-sweep] executing" -ForegroundColor Cyan
Write-Host "  ledger: $jsonPath + $mdPath"
$results = @()
foreach ($entry in $entries) {
    Write-Host -NoNewline ("  -> {0} ... " -f $entry.name)
    $r = Test-SweepEntry -Entry $entry
    $color = if ($r.status -eq "PASS") { "Green" } else { "Red" }
    Write-Host ("{0} ({1} ms){2}" -f $r.status, $r.elapsed_ms, $(if ($r.severity) { " [$($r.severity)]" } else { "" })) -ForegroundColor $color
    $results += $r
    # Atomic-write after every entry so partial findings survive a crash.
    Write-FindingsJson -Results $results -Path $jsonPath
    Write-FindingsMarkdown -Results $results -Path $mdPath
}
Write-Host "[concord-mcp-sweep] complete: $($results.Where{$_.status -eq 'PASS'}.Count) PASS / $($results.Where{$_.status -eq 'FAIL'}.Count) FAIL / $($results.Where{$_.status -eq 'SKIP'}.Count) SKIP" -ForegroundColor Cyan
Write-Host "  artifacts: $jsonPath, $mdPath" -ForegroundColor Cyan
```

- [ ] **Step 4: Smoke test artifact writing**

Run:

```powershell
./scripts/concord-mcp-sweep.ps1
```

Expected output (last few lines):
```
  -> list_modules ... PASS (xxx ms)
  -> read_project_info ... FAIL (xxx ms) [CRASH]
[concord-mcp-sweep] complete: 1 PASS / 1 FAIL / 0 SKIP
  artifacts: tests/concord-mcp-sweep/findings.json, tests/concord-mcp-sweep/findings.md
```

Then verify the artifacts:

```powershell
Get-Item tests/concord-mcp-sweep/findings.json | Select-Object Length
Get-Content tests/concord-mcp-sweep/findings.md | Select-Object -First 40
```

`findings.json` should be non-empty (>500 bytes for 2 entries). `findings.md` should have a Summary table showing `1 / 2 PASS` and `1 / 2 FAIL`, and a `## DomainModel` section with `### Failures` containing `#### read_project_info — **CRASH**`.

- [ ] **Step 5: Verify atomic-write resilience**

The findings.json.tmp pattern is non-trivial to test without faking a crash, but at minimum verify there is no leftover tmp file:

```powershell
Test-Path tests/concord-mcp-sweep/findings.json.tmp
```

Expected: `False`.

- [ ] **Step 6: Commit**

```bash
git add scripts/concord-mcp-sweep.ps1
git commit -m "$(cat <<'EOF'
feat(spmcp-sweep): findings.json + findings.md writers (atomic, per-entry)

Both ledgers re-written after every matrix entry, so a mid-sweep crash
leaves accurate partial output. JSON via temp+rename for atomicity.
Markdown is grouped by family with severity-badged failure sections
and a banner for >50% read-phase failure.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Driver — phase filtering, -Only filter, verify: sub-records, interrupt handling

**Files:**
- Modify: `scripts/concord-mcp-sweep.ps1`

- [ ] **Step 1: Add filtering and phase-ordering logic**

In `scripts/concord-mcp-sweep.ps1`, locate the line:

```powershell
$entries = Read-SweepMatrix -Path $Matrix
Write-Host "  matrix has $($entries.Count) entries"
```

Immediately after the `Write-Host` line, insert:

```powershell

# Apply -Only filter (tool-name subset).
if ($Only.Count -gt 0) {
    $entries = $entries | Where-Object { $Only -contains $_.name }
    Write-Host "  -Only filter: $($entries.Count) entries match"
}

# Apply -Phase filter.
if ($Phase.Count -gt 0) {
    $entries = $entries | Where-Object { $Phase -contains $_.phase }
    Write-Host "  -Phase filter: $($entries.Count) entries match"
}

# Stable phase ordering: read → mutate → lifecycle. Preserves
# in-phase original order from matrix.jsonc.
$phaseOrder = @{ "read" = 0; "mutate" = 1; "lifecycle" = 2 }
$entries = $entries | Sort-Object @{ Expression = { $phaseOrder[$_.phase] }; Ascending = $true }
```

- [ ] **Step 2: Add verify: sub-record support to Test-SweepEntry**

In `scripts/concord-mcp-sweep.ps1`, locate this block inside `Test-SweepEntry`:

```powershell
    $elapsedMs = [int]((Get-Date) - $started).TotalMilliseconds
    return [PSCustomObject]@{
        name          = $Entry.name
        family        = $Entry.family
        phase         = $Entry.phase
        status        = $status
        expected      = $Entry.expected
        args          = $Entry.args
        raw_response  = $rawEnvelope
        error_summary = $errorSummary
        severity      = $severity
        elapsed_ms    = $elapsedMs
        timestamp     = (Get-Date).ToUniversalTime().ToString("o")
    }
```

Replace it with:

```powershell
    # Optional side-effect verifier — runs a follow-up tool and stores
    # its raw response as a sub-field. The mutation entry's top-level
    # status remains the mutation's own outcome unless the verifier
    # itself reveals the model didn't change (SIDE-EFFECT promotion
    # is a manual triage decision based on inspecting the sub-record).
    $sideEffectCheck = $null
    if ($status -eq "PASS" -and $Entry.verify) {
        $vArgs = if ($null -eq $Entry.verify.args) { @{} } else { $Entry.verify.args }
        try {
            $vEnvelope = Invoke-McpToolCall -Name $Entry.verify.name -Arguments $vArgs
            $sideEffectCheck = [PSCustomObject]@{
                tool         = $Entry.verify.name
                args         = $vArgs
                raw_response = $vEnvelope
                status       = if ($vEnvelope.result.isError) { "verifier_errored" } else { "ran" }
            }
        } catch {
            $sideEffectCheck = [PSCustomObject]@{
                tool         = $Entry.verify.name
                args         = $vArgs
                raw_response = $null
                status       = "verifier_threw"
                error        = $_.Exception.Message
            }
        }
    }

    $elapsedMs = [int]((Get-Date) - $started).TotalMilliseconds
    return [PSCustomObject]@{
        name              = $Entry.name
        family            = $Entry.family
        phase             = $Entry.phase
        status            = $status
        expected          = $Entry.expected
        args              = $Entry.args
        raw_response      = $rawEnvelope
        error_summary     = $errorSummary
        severity          = $severity
        side_effect_check = $sideEffectCheck
        elapsed_ms        = $elapsedMs
        timestamp         = (Get-Date).ToUniversalTime().ToString("o")
    }
```

- [ ] **Step 3: Add interrupt handling**

In `scripts/concord-mcp-sweep.ps1`, locate the execution loop:

```powershell
Write-Host "[concord-mcp-sweep] executing" -ForegroundColor Cyan
Write-Host "  ledger: $jsonPath + $mdPath"
$results = @()
foreach ($entry in $entries) {
```

Wrap the execution loop in a try/finally that flushes findings on exception. Replace the entire block from `Write-Host "[concord-mcp-sweep] executing"` through the post-loop `Write-Host "  artifacts: ..."` with:

```powershell
Write-Host "[concord-mcp-sweep] executing" -ForegroundColor Cyan
Write-Host "  ledger: $jsonPath + $mdPath"
$results = @()
try {
    foreach ($entry in $entries) {
        Write-Host -NoNewline ("  -> {0} ... " -f $entry.name)
        $r = Test-SweepEntry -Entry $entry
        $color = if ($r.status -eq "PASS") { "Green" } else { "Red" }
        Write-Host ("{0} ({1} ms){2}" -f $r.status, $r.elapsed_ms, $(if ($r.severity) { " [$($r.severity)]" } else { "" })) -ForegroundColor $color
        $results += $r
        Write-FindingsJson -Results $results -Path $jsonPath
        Write-FindingsMarkdown -Results $results -Path $mdPath
    }
}
finally {
    if ($results.Count -gt 0) {
        Write-FindingsJson -Results $results -Path $jsonPath
        Write-FindingsMarkdown -Results $results -Path $mdPath
    }
    Write-Host "[concord-mcp-sweep] complete: $($results.Where{$_.status -eq 'PASS'}.Count) PASS / $($results.Where{$_.status -eq 'FAIL'}.Count) FAIL / $($results.Where{$_.status -eq 'SKIP'}.Count) SKIP" -ForegroundColor Cyan
    Write-Host "  artifacts: $jsonPath, $mdPath" -ForegroundColor Cyan
}
```

- [ ] **Step 4: Smoke test -Phase filter**

Run:

```powershell
./scripts/concord-mcp-sweep.ps1 -Phase read -DryRun
```

Expected output (last few lines):
```
  matrix has 2 entries
  -Phase filter: 2 entries match
[concord-mcp-sweep] -DryRun: planned execution
    [read] DomainModel.list_modules
    [read] DomainModel.read_project_info
```

Now try a non-matching phase:

```powershell
./scripts/concord-mcp-sweep.ps1 -Phase mutate -DryRun
```

Expected: `-Phase filter: 0 entries match` and an empty planned-execution list.

- [ ] **Step 5: Smoke test -Only filter**

```powershell
./scripts/concord-mcp-sweep.ps1 -Only list_modules -DryRun
```

Expected: `-Only filter: 1 entries match` and one planned entry.

- [ ] **Step 6: Smoke test a verify: sub-record**

Edit `tests/concord-mcp-sweep/matrix.jsonc` and append a third entry inside the array:

```jsonc
  ,{
    "name": "list_modules",
    "family": "DomainModel",
    "phase": "read",
    "args": {},
    "expected": "ok",
    "verify": { "name": "list_modules", "args": {} },
    "notes": "Smoke test for verify: sub-record support. Verifier is a redundant read of the same tool — should land in side_effect_check."
  }
```

Run:

```powershell
./scripts/concord-mcp-sweep.ps1
Get-Content tests/concord-mcp-sweep/findings.json | ConvertFrom-Json | Where-Object { $_.side_effect_check -ne $null } | Select-Object name, @{n='verifier';e={$_.side_effect_check.status}}
```

Expected: one row with `name = list_modules` (the third entry) and `verifier = ran`.

After this smoke test, **remove the smoke-test entry** by editing matrix.jsonc back to just the two entries (`list_modules` + `read_project_info`).

- [ ] **Step 7: Commit**

```bash
git add scripts/concord-mcp-sweep.ps1 tests/concord-mcp-sweep/matrix.jsonc
git commit -m "$(cat <<'EOF'
feat(spmcp-sweep): -Phase + -Only filtering, verify: sub-records, interrupt-safe flush

Adds phase-ordered execution (read → mutate → lifecycle) with stable
sort, -Only and -Phase filter flags, side_effect_check sub-records
for verify: entries, and a try/finally guard that flushes findings
on Ctrl-C or unhandled exception.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Schema discovery — populate arg-shapes.md

**Files:**
- Modify: `tests/concord-mcp-sweep/arg-shapes.md`

This task is research-and-document, not code. The output is the reference doc that the next three tasks (matrix population) rely on. The 87 tools are grouped by family for organized walk-through.

- [ ] **Step 1: Extract method signatures from both tool source files**

Use the Grep tool to enumerate all public tool methods in both files:

```
Grep: pattern = `public async Task<.*?> ([A-Z]\w+)\(JsonObject `
      path    = src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs
      output_mode = content
      -n = true
```

Repeat for `MendixAdditionalTools.cs`. Together these should return ~87 method definitions (one per tool, possibly minus a few internal helpers).

- [ ] **Step 2: For each tool, extract parameter access pattern**

For each public method found in Step 1, use the Read tool to read the method body (typically 5-50 lines starting at the line number). Note the patterns:

- Required field: `var x = parameters?["x"]?.ToString();` followed by `if (string.IsNullOrEmpty(x)) return error(...)` or `throw`
- Optional field: same access pattern with a default fallback or simple null tolerance
- Array field: `parameters?["x"] as JsonArray` or `parameters?["x"]?.AsArray()`
- Object field: `parameters?["x"] as JsonObject`
- Nested traversal: `parameters?["x"]?["y"]`

- [ ] **Step 3: Write one section per tool in arg-shapes.md**

For each tool, append a section to `tests/concord-mcp-sweep/arg-shapes.md` using this exact template:

```markdown
### `<tool_name>` — _<Family>_

**Source:** [`<filename>.cs:<line>`](../../src/Concord.Core/Spmcp/Tools/<filename>.cs)

**Required:**
- `<field_name>` (string|object|array): _<one-line description from source>_

**Optional:**
- `<field_name>` (type, default `<value>`): _<description>_

**Notes:** _<any source-visible quirks; e.g. "ToString() on a JsonArray value yields '[]' — likely a bug" or "branches on string-vs-array shape"; otherwise "none">_

**Suggested matrix args:** `{ "<field_name>": "<sample_value>" }`

---
```

Worked example for one tool, to anchor the format:

```markdown
### `create_entity` — _DomainModel_

**Source:** [`MendixDomainModelTools.cs:920`](../../src/Concord.Core/Spmcp/Tools/MendixDomainModelTools.cs)

**Required:**
- `module_name` (string): name of the module to create the entity in.
- `entity_name` (string): name of the new entity.

**Optional:**
- `documentation` (string, default `""`): entity documentation.
- `generalization` (string, default `null`): qualified-name of a parent entity to generalize.
- `attributes` (array of object, default `[]`): each `{name, type}`.

**Notes:** Reads `parameters?["attributes"]` as JsonArray; ToString() would return `"[]"` for an empty list — the source explicitly handles this. Will be a SCHEMA-class finding if the matrix entry sends a plain string instead of an array.

**Suggested matrix args:** `{ "module_name": "MyFirstModule", "entity_name": "SweepEntity_create_entity" }`

---
```

- [ ] **Step 4: Walk all 87 tools by family**

Process the families in this order (matches the matrix order in subsequent tasks):

1. **Diagnostics & read-only utilities** (~11 tools): `analyze_project_patterns, check_model, check_project_errors, check_variable_name, diagnose_associations, get_last_error, get_last_error_domain, get_studio_pro_logs, list_available_tools, list_available_tools_domain, list_java_actions`
2. **DomainModel reads** (~7 tools): `list_modules, read_domain_model, read_project_info, query_model_elements, query_associations, read_attribute_details, validate_name`
3. **Microflows reads** (~5 tools): `list_microflows, read_microflow_details, list_nanoflows, read_nanoflow_details, list_scheduled_events`
4. **Pages reads** (2): `list_pages, read_page_details`
5. **Workflows reads** (2): `list_workflows, read_workflow_details`
6. **ConstantsEnums reads** (2): `list_constants, list_enumerations`
7. **Security reads** (5): `list_rules, read_security_info, read_entity_access_rules, read_microflow_security, audit_security`
8. **ProjectSettings reads** (4): `read_runtime_settings, read_configurations, list_rest_services, read_version_control`
9. **UiActions reads** (2): `get_app_status, get_active_run_configuration`
10. **DomainModel mutations** (~24): `create_module, rename_module, create_entity, create_multiple_entities, rename_entity, delete_model_element, copy_model_element, set_entity_generalization, remove_entity_generalization, add_attribute, update_attribute, rename_attribute, set_calculated_attribute, configure_system_attributes, add_event_handler, create_association, create_multiple_associations, update_association, rename_association, arrange_domain_model, create_domain_model_from_schema, manage_folders, set_documentation, rename_document`
11. **ConstantsEnums mutations** (~6): `create_constant, update_constant, configure_constant_values, create_enumeration, update_enumeration, rename_enumeration_value`
12. **Microflows mutations** (~7): `create_microflow, update_microflow, create_microflow_activity, create_microflow_activities_sequence, modify_microflow_activity, insert_before_activity, set_microflow_url`
13. **Pages mutations** (3): `generate_overview_pages, delete_document, exclude_document`
14. **ProjectSettings mutations** (3): `set_runtime_settings, set_configuration, sync_filesystem`
15. **Navigation** (1): `manage_navigation`
16. **UiActions lifecycle** (4): `save_all, run_app, stop_app, refresh_project`

If `tools/list` reveals tools not in this enumeration, document them in their best-fitting family and note `**Notes:** discovered via tools/list, no source-side family assignment found`.

- [ ] **Step 5: Final pass — record ambiguous cases**

Re-read arg-shapes.md. For any tool whose source has multiple shape branches (e.g. accepts either a string or a JsonArray for the same parameter), explicitly flag it with `**Notes:** AMBIGUOUS — accepts both <shape A> and <shape B>; matrix entry uses <shape A> per spec "most-likely-success" guidance.` These are the entries most likely to surface SCHEMA-class findings.

- [ ] **Step 6: Commit**

```bash
git add tests/concord-mcp-sweep/arg-shapes.md
git commit -m "$(cat <<'EOF'
docs(spmcp-sweep): arg-shapes.md — 87 reverse-engineered tool schemas

One section per tool with required/optional fields, source-line cite,
source-visible quirks, and a suggested matrix args object. Derived
by grepping MendixDomainModelTools.cs and MendixAdditionalTools.cs
for the parameters?["X"]?.ToString() access pattern. Ambiguous shape
cases (string vs array on the same field) explicitly flagged.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Populate matrix.jsonc — read phase (~40 entries)

**Files:**
- Modify: `tests/concord-mcp-sweep/matrix.jsonc`

This task uses `arg-shapes.md` from Task 6 as the source of truth for each entry's `args` field. The two existing entries (`list_modules`, `read_project_info`) stay; we just add the rest.

- [ ] **Step 1: Replace the matrix body with the full read-phase entry set**

Edit `tests/concord-mcp-sweep/matrix.jsonc`. Replace the existing `[...]` body with the following structure. **For each entry below**, fill in the `args` object from the corresponding `**Suggested matrix args:**` line in arg-shapes.md.

Entries to include, in this order (read phase, ~40 tools):

```jsonc
[
  // ---- Diagnostics & read-only utilities ----
  { "name": "list_available_tools",        "family": "Diagnostics",     "phase": "read", "args": {/* fill from arg-shapes.md */}, "expected": "ok",     "notes": "Enumeration of tool names; no args." },
  { "name": "list_available_tools_domain", "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "Domain-scoped variant." },
  { "name": "analyze_project_patterns",    "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "check_model",                 "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "check_project_errors",        "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "check_variable_name",         "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "diagnose_associations",       "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "get_last_error",              "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "either", "notes": "Source returns 'GetLastError not implemented yet' — known BUG severity." },
  { "name": "get_last_error_domain",       "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "either", "notes": "" },
  { "name": "get_studio_pro_logs",         "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "either", "notes": "KNOWN STALE pre-sweep: hardcoded 11.5.0 path (MendixAdditionalTools.cs:658)." },
  { "name": "list_java_actions",           "family": "Diagnostics",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },

  // ---- DomainModel reads ----
  { "name": "list_modules",                "family": "DomainModel",     "phase": "read", "args": {},        "expected": "ok",     "notes": "No args required; returns all 10 modules." },
  { "name": "read_domain_model",           "family": "DomainModel",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_project_info",           "family": "DomainModel",     "phase": "read", "args": {},        "expected": "ok",     "notes": "KNOWN CRASH pre-sweep: ModuleProxy KeyNotFound (MendixDomainModelTools.cs:769-775)." },
  { "name": "query_model_elements",        "family": "DomainModel",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "query_associations",          "family": "DomainModel",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_attribute_details",      "family": "DomainModel",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "validate_name",               "family": "DomainModel",     "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },

  // ---- Microflows reads ----
  { "name": "list_microflows",             "family": "Microflows",      "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_microflow_details",      "family": "Microflows",      "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "list_nanoflows",              "family": "Microflows",      "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_nanoflow_details",       "family": "Microflows",      "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "list_scheduled_events",       "family": "Microflows",      "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },

  // ---- Pages / Workflows / ConstantsEnums reads ----
  { "name": "list_pages",                  "family": "Pages",           "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_page_details",           "family": "Pages",           "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "list_workflows",              "family": "Workflows",       "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_workflow_details",       "family": "Workflows",       "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "list_constants",              "family": "ConstantsEnums",  "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "list_enumerations",           "family": "ConstantsEnums",  "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },

  // ---- Security reads ----
  { "name": "list_rules",                  "family": "Security",        "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_security_info",          "family": "Security",        "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_entity_access_rules",    "family": "Security",        "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_microflow_security",     "family": "Security",        "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "audit_security",              "family": "Security",        "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },

  // ---- ProjectSettings reads ----
  { "name": "read_runtime_settings",       "family": "ProjectSettings", "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_configurations",         "family": "ProjectSettings", "phase": "read", "args": {},        "expected": "ok",     "notes": "Returns Default configuration." },
  { "name": "list_rest_services",          "family": "ProjectSettings", "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "read_version_control",        "family": "ProjectSettings", "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },

  // ---- UiActions reads ----
  { "name": "get_app_status",              "family": "UiActions",       "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" },
  { "name": "get_active_run_configuration", "family": "UiActions",      "phase": "read", "args": {/*...*/}, "expected": "ok",     "notes": "" }
]
```

For each entry where `args` is shown as `{/*...*/}`, replace it with the actual JSON args object from arg-shapes.md's `**Suggested matrix args:**` line for that tool. For tools where arg-shapes.md says "Required: (none)" the args object stays `{}`.

- [ ] **Step 2: Smoke test read-phase coverage**

```powershell
./scripts/concord-mcp-sweep.ps1 -Phase read -DryRun
```

Expected: about 40 planned entries, each prefixed `[read]`.

If the count is off by more than 2 from 40, double-check the arg-shapes.md enumeration against `tools/list` from the server — there may be a tool you missed or a family I miscategorized.

- [ ] **Step 3: Run the read phase against the live server**

```powershell
./scripts/concord-mcp-sweep.ps1 -Phase read
```

Expected: ~40 entries executed, most PASS, with `read_project_info` and `get_studio_pro_logs` confirmed in the FAIL list. Findings are written to `tests/concord-mcp-sweep/findings.json` + `.md`.

If more than 20 FAIL: STOP and report — that's the LIKELY SERVER MISCONFIGURATION banner territory and probably means arg shapes are wrong, not that the tools are buggy. Re-check arg-shapes.md against actual tool source.

- [ ] **Step 4: Commit the matrix** (not findings — those land in Task 9)

```bash
git add tests/concord-mcp-sweep/matrix.jsonc
git commit -m "$(cat <<'EOF'
feat(spmcp-sweep): matrix.jsonc — read phase (40 entries)

All read-only tools across Diagnostics, DomainModel, Microflows,
Pages, Workflows, ConstantsEnums, Security, ProjectSettings,
UiActions. Args sourced from arg-shapes.md. Two known pre-sweep
failures (read_project_info CRASH, get_studio_pro_logs STALE)
left in for traceability.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Populate matrix.jsonc — mutate phase (~44 entries + ~6 verify pairs)

**Files:**
- Modify: `tests/concord-mcp-sweep/matrix.jsonc`

- [ ] **Step 1: Append the mutate-phase entries to matrix.jsonc**

Open `tests/concord-mcp-sweep/matrix.jsonc`. Before the closing `]`, append the following entries (insert a comma after the last existing read-phase entry). Apply the same `args` replacement pattern as Task 7 — fill `{/*...*/}` from arg-shapes.md's suggested args for each tool.

**Mutation-target convention** (per spec): primary target names use the suffix `<tool_name>` so findings can be cross-referenced.

```jsonc
  ,

  // ---- DomainModel mutations (targets first, then modify, then wire, then delete) ----
  { "name": "create_module",                    "family": "DomainModel",     "phase": "mutate", "args": { "module_name": "ConcordSweep_create_module" }, "expected": "ok", "notes": "Creates a sweep-specific module. If this fails, rename_module will SKIP via -Only filter at re-test time." },
  { "name": "create_entity",                    "family": "DomainModel",     "phase": "mutate", "args": { "module_name": "MyFirstModule", "entity_name": "SweepEntity_create_entity" }, "expected": "ok", "verify": { "name": "query_model_elements", "args": {/* match against new entity */} }, "notes": "Creates SweepEntity_create_entity in MyFirstModule. Verified via query_model_elements." },
  { "name": "create_multiple_entities",         "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "create_domain_model_from_schema",  "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "add_attribute",                    "family": "DomainModel",     "phase": "mutate", "args": { "module_name": "MyFirstModule", "entity_name": "SweepEntity_create_entity", "attribute_name": "SweepAttr_add_attribute", "type": "String" }, "expected": "ok", "notes": "Adds attribute to entity created above." },
  { "name": "update_attribute",                 "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "rename_attribute",                 "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "set_calculated_attribute",         "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "configure_system_attributes",      "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "add_event_handler",                "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "set_documentation",                "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "rename_entity",                    "family": "DomainModel",     "phase": "mutate", "args": { "module_name": "MyFirstModule", "old_name": "SweepEntity_create_entity", "new_name": "SweepEntityRenamed_rename_entity" }, "expected": "ok", "verify": { "name": "query_model_elements", "args": {/* match renamed entity */} }, "notes": "Renames the entity created earlier. Verified via query_model_elements." },
  { "name": "set_entity_generalization",        "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "remove_entity_generalization",     "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "copy_model_element",               "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "create_association",               "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "create_multiple_associations",     "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "update_association",               "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "rename_association",               "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "arrange_domain_model",             "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "manage_folders",                   "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "rename_document",                  "family": "DomainModel",     "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },

  // ---- ConstantsEnums mutations ----
  { "name": "create_enumeration",               "family": "ConstantsEnums",  "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "update_enumeration",               "family": "ConstantsEnums",  "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "rename_enumeration_value",         "family": "ConstantsEnums",  "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "create_constant",                  "family": "ConstantsEnums",  "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "update_constant",                  "family": "ConstantsEnums",  "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "configure_constant_values",        "family": "ConstantsEnums",  "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },

  // ---- Microflows mutations ----
  { "name": "create_microflow",                 "family": "Microflows",      "phase": "mutate", "args": { "module_name": "MyFirstModule", "microflow_name": "SweepMf_create_microflow" }, "expected": "ok", "verify": { "name": "list_microflows", "args": { "module_name": "MyFirstModule" } }, "notes": "Verified via list_microflows on target module." },
  { "name": "update_microflow",                 "family": "Microflows",      "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "create_microflow_activity",        "family": "Microflows",      "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "create_microflow_activities_sequence", "family": "Microflows",  "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "modify_microflow_activity",        "family": "Microflows",      "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "insert_before_activity",           "family": "Microflows",      "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "set_microflow_url",                "family": "Microflows",      "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },

  // ---- Pages mutations ----
  { "name": "generate_overview_pages",          "family": "Pages",           "phase": "mutate", "args": { "module_name": "MyFirstModule", "entity_names": ["SweepEntityRenamed_rename_entity"] }, "expected": "ok", "notes": "Historical terminal.log shows this tool iteratively patched its arg validation (5+ error variants). Args use array shape per current version." },

  // ---- ProjectSettings mutations ----
  { "name": "set_runtime_settings",             "family": "ProjectSettings", "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "set_configuration",                "family": "ProjectSettings", "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "sync_filesystem",                  "family": "ProjectSettings", "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },

  // ---- Navigation ----
  { "name": "manage_navigation",                "family": "Navigation",      "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },

  // ---- Deletes LAST in mutate phase ----
  { "name": "rename_module",                    "family": "DomainModel",     "phase": "mutate", "args": { "old_name": "ConcordSweep_create_module", "new_name": "ConcordSweep_rename_module" }, "expected": "ok", "notes": "Only the sweep-created module. Skips automatically if create_module failed (target won't exist)." },
  { "name": "delete_document",                  "family": "Pages",           "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "exclude_document",                 "family": "Pages",           "phase": "mutate", "args": {/*...*/}, "expected": "ok", "notes": "" },
  { "name": "delete_model_element",             "family": "DomainModel",     "phase": "mutate", "args": { "module_name": "MyFirstModule", "element_name": "SweepEntityRenamed_rename_entity" }, "expected": "ok", "verify": { "name": "query_model_elements", "args": {/* confirm gone */} }, "notes": "Deletes the renamed entity to close the create→rename→delete cycle. Verified via query_model_elements." }
```

- [ ] **Step 2: Smoke test mutate-phase coverage**

```powershell
./scripts/concord-mcp-sweep.ps1 -Phase mutate -DryRun
```

Expected: ~44 planned entries, each prefixed `[mutate]`.

- [ ] **Step 3: Verify exactly 6 entries have a `verify:` field**

```powershell
$m = Get-Content tests/concord-mcp-sweep/matrix.jsonc -Raw
$stripped = [regex]::Replace($m, '(?ms)/\*.*?\*/', '')
$stripped = [regex]::Replace($stripped, '(?m)//[^\n]*', '')
($stripped | ConvertFrom-Json) | Where-Object { $_.verify } | Select-Object name
```

Expected: at most 6 names. Per spec target: `create_entity`, `rename_entity`, `delete_model_element`, `create_microflow`, plus up to 2 more if you added verifiers during arg-shape walk. If you have zero verifiers, add at least the 4 listed above before proceeding.

- [ ] **Step 4: Commit**

```bash
git add tests/concord-mcp-sweep/matrix.jsonc
git commit -m "$(cat <<'EOF'
feat(spmcp-sweep): matrix.jsonc — mutate phase (44 entries, 6 verifiers)

Mutations dependency-ordered per spec: targets first, modify, wire,
constants/enums, microflows, pages, settings, navigation, deletes
last. Mutation-target names use SweepX_<tool_name> convention for
findings cross-reference. Six risky mutators carry verify: sub-records
(create_entity, rename_entity, delete_model_element, create_microflow,
plus 2 more if added in arg-shapes).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Populate matrix.jsonc — lifecycle phase + in-driver run_app poll

**Files:**
- Modify: `tests/concord-mcp-sweep/matrix.jsonc`
- Modify: `scripts/concord-mcp-sweep.ps1`

- [ ] **Step 1: Append lifecycle entries to matrix.jsonc**

Open `tests/concord-mcp-sweep/matrix.jsonc`. Before the closing `]`, append:

```jsonc
  ,

  // ---- Lifecycle (strict order: save → run → poll → stop → refresh-last) ----
  { "name": "save_all",          "family": "UiActions", "phase": "lifecycle", "args": {}, "expected": "ok", "notes": "Idempotent .mpr write. Run first to capture sweep mutations." },
  { "name": "run_app",           "family": "UiActions", "phase": "lifecycle", "args": {}, "expected": "ok", "notes": "Starts active configuration. Driver polls get_app_status until running (30s cap) before stop_app." },
  { "name": "stop_app",          "family": "UiActions", "phase": "lifecycle", "args": {}, "expected": "ok", "notes": "Stops the running app. Driver-managed wait happens between run_app and this entry." },
  { "name": "refresh_project",   "family": "UiActions", "phase": "lifecycle", "args": {}, "expected": "ok", "notes": "Invalidates cached model — DEAD LAST in the entire sweep." }
```

- [ ] **Step 2: Add the run_app → stop_app poll wait to the driver**

In `scripts/concord-mcp-sweep.ps1`, locate the foreach loop body:

```powershell
    foreach ($entry in $entries) {
        Write-Host -NoNewline ("  -> {0} ... " -f $entry.name)
        $r = Test-SweepEntry -Entry $entry
        $color = if ($r.status -eq "PASS") { "Green" } else { "Red" }
        Write-Host ("{0} ({1} ms){2}" -f $r.status, $r.elapsed_ms, $(if ($r.severity) { " [$($r.severity)]" } else { "" })) -ForegroundColor $color
        $results += $r
        Write-FindingsJson -Results $results -Path $jsonPath
        Write-FindingsMarkdown -Results $results -Path $mdPath
    }
```

Replace the body of the foreach with:

```powershell
    foreach ($entry in $entries) {
        Write-Host -NoNewline ("  -> {0} ... " -f $entry.name)
        $r = Test-SweepEntry -Entry $entry
        $color = if ($r.status -eq "PASS") { "Green" } else { "Red" }
        Write-Host ("{0} ({1} ms){2}" -f $r.status, $r.elapsed_ms, $(if ($r.severity) { " [$($r.severity)]" } else { "" })) -ForegroundColor $color
        $results += $r
        Write-FindingsJson -Results $results -Path $jsonPath
        Write-FindingsMarkdown -Results $results -Path $mdPath

        # Lifecycle wait: after run_app succeeds, poll get_app_status until
        # the app reports running, capped at 30s. Keeps the stop_app call
        # from racing against a still-starting app.
        if ($entry.name -eq "run_app" -and $r.status -eq "PASS") {
            Write-Host -NoNewline "    polling get_app_status until running (30s cap) "
            $deadline = (Get-Date).AddSeconds(30)
            $isRunning = $false
            while ((Get-Date) -lt $deadline) {
                Start-Sleep -Seconds 1
                Write-Host -NoNewline "."
                try {
                    $statusEnv = Invoke-McpToolCall -Name "get_app_status" -Arguments @{}
                    $payload = $statusEnv.result.content[0].text | ConvertFrom-Json
                    # The status payload shape isn't pinned by source-level schema;
                    # check the most common signals.
                    if ($payload.running -eq $true -or $payload.status -eq "running" -or $payload.is_running -eq $true) {
                        $isRunning = $true
                        break
                    }
                } catch { }
            }
            if ($isRunning) {
                Write-Host " running" -ForegroundColor Green
            } else {
                Write-Host " timed out (continuing anyway)" -ForegroundColor Yellow
            }
        }
    }
```

- [ ] **Step 3: Smoke test lifecycle-phase planning**

```powershell
./scripts/concord-mcp-sweep.ps1 -Phase lifecycle -DryRun
```

Expected: exactly 4 planned entries, each prefixed `[lifecycle]`, in the order `save_all` → `run_app` → `stop_app` → `refresh_project`.

- [ ] **Step 4: Commit**

```bash
git add tests/concord-mcp-sweep/matrix.jsonc scripts/concord-mcp-sweep.ps1
git commit -m "$(cat <<'EOF'
feat(spmcp-sweep): matrix.jsonc lifecycle phase + run_app→stop_app poll

Four lifecycle entries in strict order (save_all, run_app, stop_app,
refresh_project). Driver polls get_app_status every 1s for up to 30s
after run_app succeeds, so stop_app doesn't race against startup.
refresh_project is dead-last (invalidates cached model state).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Full sweep — execute, capture findings, commit artifacts

**Files:**
- Create: `tests/concord-mcp-sweep/findings.json` (sweep output)
- Create: `tests/concord-mcp-sweep/findings.md` (sweep output)
- Modify: `tests/concord-mcp-sweep/.gitignore` (commit-once override for this run's artifacts)

This is the payoff task — run the full matrix end-to-end and capture the result as a committed artifact. Phase 2 triage works from the committed findings.md.

- [ ] **Step 1: Final pre-flight check**

```powershell
./scripts/concord-mcp-sweep.ps1 -DryRun
```

Expected: matrix.jsonc reports ~87 entries (88 ± 2 acceptable), tools/list returns 87, no `tools not in matrix` warnings beyond what was documented during schema discovery.

If the count is off by more than 2: stop, re-check matrix.jsonc against arg-shapes.md, and fix before running the live sweep. A misaligned matrix is far cheaper to fix now than after a 5-minute sweep run.

- [ ] **Step 2: Confirm Studio Pro state**

Before the run, sanity-check:
- Studio Pro is open with `C:\Projects\Test_10_24_13` loaded.
- The terminal pane is visible (so Concord MCP is hosted).
- No long-running operation (compile, deploy, install) is in flight.
- Both armed Monitors (`bg2mvujvz` Studio Pro log, `bucwn0b2u` Concord terminal.log) are still active; if not, re-arm using the same commands from the setup.

- [ ] **Step 3: Run the full sweep**

```powershell
./scripts/concord-mcp-sweep.ps1
```

Expected duration: 30 seconds to 3 minutes (depends on `run_app` startup time and how many tools time out). The console prints `PASS`/`FAIL [SEVERITY]` per tool.

If the sweep stalls (no progress for 60s on a single tool): the entry's timeout will eventually fire (default 30s in `Invoke-WebRequest`); if even that doesn't recover, Ctrl-C — incremental findings are preserved by the try/finally guard.

At end of run, the console should report something like:
```
[concord-mcp-sweep] complete: 70 PASS / 15 FAIL / 0 SKIP
  artifacts: tests/concord-mcp-sweep/findings.json, tests/concord-mcp-sweep/findings.md
```

The exact split is the whole point — we don't predict numbers, we measure them. Anything from "all passing" (unlikely) to "half failing" is informative.

- [ ] **Step 4: Sanity-check the artifacts**

```powershell
Get-Item tests/concord-mcp-sweep/findings.json | Select-Object Length
(Get-Content tests/concord-mcp-sweep/findings.json | ConvertFrom-Json).Count
Get-Content tests/concord-mcp-sweep/findings.md | Select-Object -First 30
```

Expected:
- `findings.json` is non-empty (rough rule: 87 entries × 1-2 KB each ≈ 100-200 KB).
- The deserialized count matches the matrix entry count.
- `findings.md` Summary section shows your PASS/FAIL totals.

- [ ] **Step 5: Override .gitignore to commit this run's artifacts**

Use the Edit tool to update `tests/concord-mcp-sweep/.gitignore`. Replace its contents with:

```
# Sweep output artifacts. Re-generated each run; commit deliberately, not
# automatically (see plan Task 10 for the convention).
#
# findings.json
# findings.md
# findings.json.tmp
```

(Comments-out the ignore rules so the current run's artifacts can be committed. Re-tighten in a follow-up commit if you want subsequent runs to stay un-committed by default — but for triage flow, keeping committed is more useful since it gives `git diff` between sweep runs.)

- [ ] **Step 6: Commit the findings as the Phase 1 deliverable**

```bash
git add tests/concord-mcp-sweep/findings.json tests/concord-mcp-sweep/findings.md tests/concord-mcp-sweep/.gitignore
git commit -m "$(cat <<'EOF'
test(spmcp-sweep): Phase 1 sweep — initial findings (87 tools attempted)

First end-to-end run against the live concord-mcp v1.3.0 at
http://127.0.0.1:7783 with project Test_10_24_13 loaded in Studio Pro
10.24.13. Captured for Phase 2 triage. .gitignore commented-out so
subsequent runs produce visible diffs.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: Final plan handoff — Phase 2 readiness

**Files:**
- (no code changes)

- [ ] **Step 1: Verify all Phase 1 deliverables exist on disk and in HEAD**

```powershell
git log --oneline -10
Test-Path scripts/concord-mcp-sweep.ps1
Test-Path tests/concord-mcp-sweep/matrix.jsonc
Test-Path tests/concord-mcp-sweep/arg-shapes.md
Test-Path tests/concord-mcp-sweep/findings.json
Test-Path tests/concord-mcp-sweep/findings.md
```

Expected: all `True`, and the recent commits should include the spmcp-sweep series from Tasks 1, 2, 3, 4, 5, 6, 7, 8, 9, 10.

- [ ] **Step 2: Open findings.md and read it**

```powershell
Start-Process tests/concord-mcp-sweep/findings.md
```

(Or just `Get-Content tests/concord-mcp-sweep/findings.md` and read in terminal.)

This is the input to Phase 2 triage. The next plan (Phase 2-3) will look at:
- Each `FAIL` entry's severity, args, response, suspected root cause.
- Which fixes are < ~2 hour cheap (target: do them now).
- Which fixes are large enough to defer (target: capture as a follow-up).

- [ ] **Step 3: Surface the handoff to the user**

The Phase 1 plan terminates here. A summary message to the user should include:

```
Phase 1 sweep complete:
  - <N> PASS / <M> FAIL / 0 SKIP across 87 tools
  - findings.md committed at tests/concord-mcp-sweep/findings.md
  - matrix + driver committed for replay via -Only <tool>

Top FAIL severities (from findings.md Summary section):
  - CRASH: <list>
  - STALE: <list>
  - SCHEMA: <list>
  - BUG: <list>

Ready to plan Phase 2 (triage) and Phase 3 (fixes) — would you like me to invoke
writing-plans for the next phase, or do you want to triage findings.md by hand first?
```

---

## Self-review

### Spec coverage check

| Spec section | Covered by |
|---|---|
| Architecture: matrix.jsonc, driver, two ledgers | Task 1 (scaffold), Task 2 (driver core), Task 3 (matrix loader + classification), Task 4 (writers) |
| Architecture: `expected: ok\|error\|either` ternary | Task 3 step 3 (switch statement in Test-SweepEntry) |
| Architecture: mutation-target naming convention | Task 8 step 1 (SweepX_<tool_name> entries) |
| Workflow phases: phase ordering (read → mutate → lifecycle) | Task 5 step 1 (stable Sort-Object) |
| Workflow phases: stop conditions, `>50% read fail` banner | Task 4 step 2 (banner in Write-FindingsMarkdown) |
| Schema discovery: grep MendixDomainModelTools/MendixAdditionalTools | Task 6 (full walk) |
| Classification logic: 6-line ladder + severity inference | Task 3 step 3 (Test-SweepEntry, Get-SeveritySuggestion) |
| SIDE-EFFECT detection: ~6 verifier pairs as sub-records | Task 5 step 2 (verify: support), Task 8 step 3 (6-verifier count check) |
| Driver error handling: TRANSPORT, CRASH, TIMEOUT, try/finally | Task 3 step 3 + Task 5 step 3 |
| Fix workflow (Phase 3) — verify deploy target | Out of scope for this plan per user constraint; covered in Phase 3 plan |
| Phase 5 Studio Pro pass | Out of scope for this plan per user constraint |
| Success criteria — findings.json with 87 entries | Task 10 step 4 verifies count matches matrix |
| Success criteria — auto-memory written | Out of scope for this plan; covered in Phase 2 (triage) or Phase 3 (fixes) plan |

Gap: success criterion "auto-memory `project_concord_mcp_tool_sweep.md` written" lives in Phase 2/3 plan, not here. That's appropriate — auto-memory captures the *learnings* from the whole cycle, which we don't have until after fixes.

### Placeholder scan

- `{/*...*/}` patterns in Tasks 7 and 8 matrix entries — **intentional**: they're filled in from arg-shapes.md per the task instructions. The task body explicitly tells the executor to substitute. This is documentation of a substitution pattern, not a TODO.
- No literal "TBD", "TODO", "implement later", or "fill in details" anywhere else.

### Type consistency

- `Test-SweepEntry` return shape is defined in Task 3 step 3 and extended in Task 5 step 2 (adds `side_effect_check`). Subsequent tasks (4, 5+) reference the same field names: `name`, `family`, `phase`, `status`, `severity`, `elapsed_ms`. Consistent.
- `Invoke-McpToolCall` signature: `-Name <string> -Arguments <object>`. Used consistently in Task 3 step 3, Task 5 step 2 (verifier), Task 9 step 2 (poll loop).
- Matrix entry field names: `name, family, phase, args, expected, verify, notes`. Used consistently across Tasks 1, 3, 5, 7, 8, 9.

No naming drift detected.
