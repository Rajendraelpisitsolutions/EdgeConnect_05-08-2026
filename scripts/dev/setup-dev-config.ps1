#requires -Version 7.0
<#
.SYNOPSIS
Seed a fresh dev data root with a minimal gateway config + identity stub.

.DESCRIPTION
Creates the directory structure the runtime expects:

  $DataRoot/
    config/
      current.json     copied from scripts/dev/templates/dev-current.json
    buffer/            (empty; SQLite buffer files materialize on first use)
    identity/          (empty; gateway-identity.json is created on first run)

Sets EDGECONNECT_DATA_ROOT for the current session and prints the env-var lines
to add to your $PROFILE for persistence.

Safe to re-run — won't overwrite an existing current.json unless -Force is set.

.PARAMETER DataRoot
Path to the data root. Defaults to <repo-root>/data.

.PARAMETER Force
Overwrite an existing current.json. Default: false (refuses if file exists).

.EXAMPLE
.\scripts\dev\setup-dev-config.ps1
#>

[CmdletBinding()]
param(
    [string]$DataRoot,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
if (-not $DataRoot) {
    $DataRoot = Join-Path $repoRoot 'data'
}

$configDir   = Join-Path $DataRoot 'config'
$bufferDir   = Join-Path $DataRoot 'buffer'
$identityDir = Join-Path $DataRoot 'identity'
$currentJson = Join-Path $configDir 'current.json'
$template    = Join-Path $PSScriptRoot 'templates/dev-current.json'

Write-Host ""
Write-Host "=== EdgeConnect dev-config setup ==="
Write-Host "  data root : $DataRoot"
Write-Host ""

if (-not (Test-Path -LiteralPath $template)) {
    Write-Error "Template missing: $template"
    exit 1
}

foreach ($d in @($DataRoot, $configDir, $bufferDir, $identityDir)) {
    if (-not (Test-Path -LiteralPath $d)) {
        New-Item -ItemType Directory -Path $d | Out-Null
        Write-Host "  [created] $d"
    }
    else {
        Write-Host "  [exists]  $d"
    }
}

if (Test-Path -LiteralPath $currentJson) {
    if (-not $Force) {
        Write-Warning "  current.json already exists. Re-run with -Force to overwrite."
    }
    else {
        Copy-Item -LiteralPath $template -Destination $currentJson -Force
        Write-Host "  [overwrote] $currentJson"
    }
}
else {
    Copy-Item -LiteralPath $template -Destination $currentJson
    Write-Host "  [seeded]    $currentJson"
}

# Set for current session.
$env:EDGECONNECT_DATA_ROOT = $DataRoot

Write-Host ""
Write-Host "=== Done ==="
Write-Host ""
Write-Host "For this shell session:"
Write-Host "  `$env:EDGECONNECT_DATA_ROOT = `"$DataRoot`"  (already set)"
Write-Host ""
Write-Host "Add to your `$PROFILE for persistence:"
Write-Host ""
Write-Host "  `$env:EDGECONNECT_DATA_ROOT    = `"$DataRoot`""
Write-Host "  `$env:EDGECONNECT_LICENSE_PATH = `"$repoRoot\docs\onboarding\dev-license.json`""
Write-Host ""
Write-Host "Launch the Studio:"
Write-Host "  dotnet run --project src\ElpisEdgeConnect.Management"
Write-Host ""
