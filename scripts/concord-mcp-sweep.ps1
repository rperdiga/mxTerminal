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

Write-Host "[concord-mcp-sweep] driver core OK (pre-flight only -- full execution lands in later tasks)" -ForegroundColor Green
