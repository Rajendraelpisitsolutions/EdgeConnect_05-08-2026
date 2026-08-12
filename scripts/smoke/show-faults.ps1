<#
.SYNOPSIS
  M.P2.2 phase 3 smoke helper -- dump the current configuration-faults
  registry, grouped by kind.

.DESCRIPTION
  Wraps GET /api/v1/diagnostics/configuration-faults and renders it as
  a compact table grouped by entity kind (Source / Sink / Route). Used
  for baseline checks before each smoke scenario and for cross-checks
  after applying a draft.

.PARAMETER BaseUrl
  Management API base URL. Default: http://127.0.0.1:5080.

.EXAMPLE
  .\show-faults.ps1
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = "http://127.0.0.1:5080"
)

$ErrorActionPreference = "Stop"

try {
    $faults = Invoke-RestMethod -Uri "$BaseUrl/api/v1/diagnostics/configuration-faults" -Method Get
}
catch {
    Write-Host "Could not fetch diagnostics: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

if (-not $faults -or ($faults -is [array] -and $faults.Count -eq 0)) {
    Write-Host "Configuration-fault registry is empty." -ForegroundColor Green
    exit 0
}

# Normalise into an array regardless of how Invoke-RestMethod gave it
$arr = @($faults)
$count = $arr.Count

Write-Host "Configuration-fault registry ($count entry/entries):"
Write-Host ""

$arr |
    Sort-Object kind, instanceId |
    Group-Object kind |
    ForEach-Object {
        Write-Host "--- $($_.Name) ---"
        $_.Group |
            Select-Object instanceId, errorCode, observedAtUtc, message |
            Format-Table -Wrap -AutoSize |
            Out-String |
            Write-Host
    }
