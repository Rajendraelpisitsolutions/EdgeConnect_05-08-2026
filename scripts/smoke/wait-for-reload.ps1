<#
.SYNOPSIS
  M.P2.2 phase 3 smoke helper -- poll diagnostics until the apply's
  reconcile reaches a terminal state.

.DESCRIPTION
  Use this when the apply response carried `reload.status = "InProgress"`.
  Polls GET /api/v1/diagnostics/configuration-faults until the timeout
  expires, then dumps the current fault snapshot.

  This is a thin convenience wrapper -- the diagnostics surface is the
  durable source of truth. The helper just spares you typing the URL
  by hand in a loop.

.PARAMETER VersionId
  The applied version id whose reconcile you're waiting on. Currently
  used only for the log line; the diagnostics endpoint is global.

.PARAMETER TimeoutSec
  Total seconds to poll. Default: 60.

.PARAMETER IntervalSec
  Polling interval. Default: 2.

.PARAMETER BaseUrl
  Management API base URL. Default: http://127.0.0.1:5080.

.EXAMPLE
  .\wait-for-reload.ps1 -VersionId 2026-05-16T12-30-00-000Z-042 -TimeoutSec 30
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $VersionId,

    [int] $TimeoutSec = 60,

    [int] $IntervalSec = 2,

    [string] $BaseUrl = "http://127.0.0.1:5080"
)

$ErrorActionPreference = "Stop"

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$attempts = 0

Write-Host "Polling /diagnostics/configuration-faults for up to $TimeoutSec s (target version $VersionId)..."

while ((Get-Date) -lt $deadline) {
    $attempts++
    try {
        $faults = Invoke-RestMethod -Uri "$BaseUrl/api/v1/diagnostics/configuration-faults" -Method Get
    }
    catch {
        Write-Host "  attempt ${attempts}: error contacting diagnostics -- $($_.Exception.Message)" -ForegroundColor Yellow
        Start-Sleep -Seconds $IntervalSec
        continue
    }

    # Diagnostics endpoint returns the live fault registry; if your
    # reconcile produced no faults, the list will be empty as soon as
    # the coordinator clears the previously-faulted entries.
    $count = if ($faults -is [array]) { $faults.Count } else { if ($faults) { 1 } else { 0 } }
    Write-Host "  attempt ${attempts}: $count fault(s) in registry"

    if ($faults) {
        $faults | Format-Table -AutoSize | Out-String | Write-Host
    }

    Start-Sleep -Seconds $IntervalSec
}

Write-Host ""
Write-Host "Timed out after $TimeoutSec s. Final fault snapshot:"
try {
    $final = Invoke-RestMethod -Uri "$BaseUrl/api/v1/diagnostics/configuration-faults" -Method Get
    $final | ConvertTo-Json -Depth 6
}
catch {
    Write-Host "  could not fetch final snapshot: $($_.Exception.Message)" -ForegroundColor Red
}
