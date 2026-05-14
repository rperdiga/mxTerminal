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

# --- Classification ----------------------------------------------------

function Get-SeveritySuggestion {
    param([string]$ErrorText)
    if (-not $ErrorText) { return $null }
    if ($ErrorText -match 'KeyNotFound|NullReference|InvalidOperation|not present in the dictionary|Object reference not set|was not present') { return 'CRASH' }
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
        $entryArgs = if ($null -eq $Entry.args) { @{} } else { $Entry.args }
        $rawEnvelope = Invoke-McpToolCall -Name $Entry.name -Arguments $entryArgs
    } catch {
        $status = "FAIL"
        $errorSummary = $_.Exception.Message
        # Distinguish socket/connection timeouts from other transport errors.
        # The driver's Invoke-WebRequest call has a 30s default TimeoutSec; when
        # it fires, the exception message typically contains "timed out" or
        # "operation has timed out". Connection-aborted-by-host (the 30s app
        # start case for run_app/stop_app) also produces a transport-like
        # exception but is more usefully classified as TIMEOUT for triage.
        if ($errorSummary -match 'timed out|operation has timed out|established connection was aborted|operation was canceled') {
            $severity = "TIMEOUT"
        } else {
            $severity = "TRANSPORT"
        }
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

    # Optional side-effect verifier -- runs a follow-up tool and stores
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
}

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

function Write-FindingsMarkdown {
    param(
        [Parameter(Mandatory)][object[]]$Results,
        [Parameter(Mandatory)][string]$Path
    )
    $pass = @($Results | Where-Object { $_.status -eq 'PASS' }).Count
    $fail = @($Results | Where-Object { $_.status -eq 'FAIL' }).Count
    $skip = @($Results | Where-Object { $_.status -eq 'SKIP' }).Count
    $total = $Results.Count

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# Concord MCP tool sweep -- findings")
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
    $readResults = @($Results | Where-Object { $_.phase -eq 'read' })
    if ($readResults.Count -gt 0) {
        $readFailRatio = @($readResults | Where-Object { $_.status -eq 'FAIL' }).Count / $readResults.Count
        if ($readFailRatio -gt 0.5) {
            [void]$sb.AppendLine("> **WARNING -- LIKELY SERVER MISCONFIGURATION** -- more than 50% of read-phase tools failed. Investigate server/project state before triaging individual entries.")
            [void]$sb.AppendLine("")
        }
    }

    $families = $Results | Select-Object -ExpandProperty family -Unique | Sort-Object
    foreach ($fam in $families) {
        [void]$sb.AppendLine("## $fam")
        [void]$sb.AppendLine("")
        $famResults = @($Results | Where-Object { $_.family -eq $fam })
        $famFails = @($famResults | Where-Object { $_.status -eq 'FAIL' })
        $famPasses = @($famResults | Where-Object { $_.status -eq 'PASS' })

        if ($famFails.Count -gt 0) {
            [void]$sb.AppendLine("### Failures")
            [void]$sb.AppendLine("")
            foreach ($r in $famFails) {
                $sev = if ($r.severity) { $r.severity } else { "unclassified" }
                [void]$sb.AppendLine("#### ``$($r.name)`` -- **$sev**")
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

# --- Pre-flight ---------------------------------------------------------

Write-Host "[concord-mcp-sweep] pre-flight" -ForegroundColor Cyan
$init = Invoke-McpInitialize
Write-Host "  server: $($init.result.serverInfo.name) v$($init.result.serverInfo.version)"

$serverTools = Get-McpToolList
Write-Host "  tools/list returned $($serverTools.Count) tools"

$entries = Read-SweepMatrix -Path $Matrix
Write-Host "  matrix has $($entries.Count) entries"

# Cross-check matrix vs server tools/list. Drift is logged as MISSING (server
# has it, matrix doesn't) or EXTRA (matrix has it, server doesn't). Does not
# abort the run; surfaces as findings entries with synthesized records so
# triage sees the gap.
$serverNames = @($serverTools | ForEach-Object { $_.name })
$matrixNames = @($entries | ForEach-Object { $_.name })
$missing = @($serverNames | Where-Object { $_ -notin $matrixNames })
$extra   = @($matrixNames | Where-Object { $_ -notin $serverNames })
if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
    Write-Host "  drift: $($missing.Count) MISSING, $($extra.Count) EXTRA" -ForegroundColor Yellow
} else {
    Write-Host "  matrix matches server tools/list exactly"
}
$driftFindings = @()
foreach ($n in $missing) {
    $driftFindings += [PSCustomObject]@{
        name              = $n
        family            = "Unknown"
        phase             = "drift"
        status            = "FAIL"
        expected          = "n/a"
        args              = @{}
        raw_response      = $null
        error_summary     = "Server advertises this tool via tools/list but no matrix entry covers it. Add a matrix entry."
        severity          = "MISSING"
        side_effect_check = $null
        elapsed_ms        = 0
        timestamp         = (Get-Date).ToUniversalTime().ToString("o")
    }
}
foreach ($n in $extra) {
    $driftFindings += [PSCustomObject]@{
        name              = $n
        family            = "Unknown"
        phase             = "drift"
        status            = "FAIL"
        expected          = "n/a"
        args              = @{}
        raw_response      = $null
        error_summary     = "Matrix has this tool but tools/list does not advertise it. Server may have removed it or matrix is stale."
        severity          = "MISSING"
        side_effect_check = $null
        elapsed_ms        = 0
        timestamp         = (Get-Date).ToUniversalTime().ToString("o")
    }
}

# Apply -Only filter (tool-name subset).
if ($Only.Count -gt 0) {
    $entries = @($entries | Where-Object { $Only -contains $_.name })
    Write-Host "  -Only filter: $($entries.Count) entries match"
}

# Apply -Phase filter.
if ($Phase.Count -gt 0) {
    $entries = @($entries | Where-Object { $Phase -contains $_.phase })
    Write-Host "  -Phase filter: $($entries.Count) entries match"
}

# Stable phase ordering: read -> mutate -> lifecycle. Preserves
# in-phase original order from matrix.jsonc.
# NOTE: Sort-Object -Stable requires PowerShell 7+; for Windows PowerShell 5.1
# compatibility we tag each entry with its original index and use it as a
# tie-breaker, which achieves the same stable-sort semantics.
$phaseOrder = @{ "read" = 0; "mutate" = 1; "lifecycle" = 2 }
$i = 0
$entries = $entries | ForEach-Object { Add-Member -InputObject $_ -NotePropertyName _idx -NotePropertyValue ($i++) -PassThru }
$entries = $entries | Sort-Object @{ Expression = { $phaseOrder[$_.phase] }; Ascending = $true }, @{ Expression = { $_._idx }; Ascending = $true }

if ($DryRun) {
    Write-Host "[concord-mcp-sweep] -DryRun: planned execution" -ForegroundColor Yellow
    $entries | ForEach-Object { Write-Host ("    [{0}] {1}.{2}" -f $_.phase, $_.family, $_.name) }
    exit 0
}

$jsonPath = Join-Path $OutDir "findings.json"
$mdPath = Join-Path $OutDir "findings.md"
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

Write-Host "[concord-mcp-sweep] executing" -ForegroundColor Cyan
Write-Host "  ledger: $jsonPath + $mdPath"
$results = @()
$results += $driftFindings   # prepend drift entries so they appear first in findings.json
try {
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
                    # AppStatusInfo (ActionResult.cs:22) serialises Running as a string
                    # "running" | "stopped" | "unknown" -- not a boolean. The fallback
                    # branches guard against future schema evolution.
                    if ($payload.Running -eq "running" -or $payload.status -eq "running" -or $payload.is_running -eq $true) {
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
}
finally {
    if ($results.Count -gt 0) {
        Write-FindingsJson -Results $results -Path $jsonPath
        Write-FindingsMarkdown -Results $results -Path $mdPath
    }
    Write-Host "[concord-mcp-sweep] complete: $(@($results | Where-Object {$_.status -eq 'PASS'}).Count) PASS / $(@($results | Where-Object {$_.status -eq 'FAIL'}).Count) FAIL / $(@($results | Where-Object {$_.status -eq 'SKIP'}).Count) SKIP" -ForegroundColor Cyan
    Write-Host "  artifacts: $jsonPath, $mdPath" -ForegroundColor Cyan
}
