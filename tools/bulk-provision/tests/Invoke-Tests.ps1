#requires -Version 7.0
<#
.SYNOPSIS
Run the bulk-provision Pester harness with version + cwd discipline.

.DESCRIPTION
Per v4 §3.F2: Pester v5+ is required. This wrapper checks the installed
version, prints actionable install guidance on miss, then invokes
`Invoke-Pester` against the sibling `*.Tests.ps1` files with a structured
output configuration.

Each test file pushes into the bulk-provision root cwd so the locked
deterministic commands in v4 §6 step D run from the same directory the
operator README documents.

Reference: docs/sessions/2026-06-13-chip3-impl-session2-plan-v4-lock-final.md §3.F2

.PARAMETER Output
Verbosity level forwarded to Pester. Default 'Detailed'.

.EXAMPLE
pwsh ./tools/bulk-provision/tests/Invoke-Tests.ps1
#>

[CmdletBinding()]
param(
    [ValidateSet('None','Normal','Detailed','Diagnostic')]
    [string]$Output = 'Detailed'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Pester version gate (v4 §3.F2 enforcement) ───────────────────────────
$pester = Get-Module Pester -ListAvailable |
    Where-Object Version -ge ([Version]'5.0.0') |
    Sort-Object Version -Descending |
    Select-Object -First 1

if (-not $pester) {
    Write-Error @"
Pester v5+ is required. None installed (or only v3/v4 available).

To install for the current user:
    Install-Module Pester -MinimumVersion 5.0 -Force -Scope CurrentUser

If you have an older Pester loaded into the current session:
    Remove-Module Pester -Force
    Install-Module Pester -MinimumVersion 5.0 -Force -Scope CurrentUser

Then re-run: pwsh ./tools/bulk-provision/tests/Invoke-Tests.ps1
"@
    exit 2
}

Write-Host "[Invoke-Tests] Using Pester $($pester.Version) from $($pester.ModuleBase)"

# Force the v5 module into the current session, displacing any v3/v4 leftovers.
Remove-Module Pester -ErrorAction SilentlyContinue
Import-Module $pester.Path -Force

# ── Pester run configuration (v5 idiom) ──────────────────────────────────
$config = New-PesterConfiguration
$config.Run.Path           = $PSScriptRoot
$config.Run.Exit           = $false        # caller handles exit code
$config.Run.PassThru       = $true         # required so Invoke-Pester returns a result object
$config.Output.Verbosity   = $Output
$config.TestResult.Enabled = $false

$result = Invoke-Pester -Configuration $config

if ($result.FailedCount -gt 0) {
    Write-Host ""
    Write-Host "[Invoke-Tests] FAILED - $($result.FailedCount) test(s) failed; $($result.PassedCount) passed."
    exit 1
}

Write-Host ""
Write-Host "[Invoke-Tests] OK - $($result.PassedCount) test(s) passed; $($result.SkippedCount) skipped."
exit 0
