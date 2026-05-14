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

# --- Pre-flight ---------------------------------------------------------

Write-Host "[concord-mcp-sweep] pre-flight" -ForegroundColor Cyan
$init = Invoke-McpInitialize
Write-Host "  server: $($init.result.serverInfo.name) v$($init.result.serverInfo.version)"

$serverTools = Get-McpToolList
Write-Host "  tools/list returned $($serverTools.Count) tools"

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
