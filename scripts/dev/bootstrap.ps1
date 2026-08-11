#requires -Version 7.0
<#
.SYNOPSIS
Onboarding bootstrap — verify prerequisites, restore, build, run unit tests.

.DESCRIPTION
First script a new contributor runs after cloning EdgeConnect. Walks through:

  1. Prerequisite check (.NET 8 SDK, pwsh 7, git on PATH).
  2. dotnet restore on the solution.
  3. dotnet build (must succeed with 0 warnings, 0 errors).
  4. dotnet test against the non-flaky filter.
  5. Surfaces the next-step env-var setup the developer needs.

Exits non-zero on any failure with an actionable message.

.PARAMETER SkipTests
Skip the test run. Useful when iterating on a script change.

.EXAMPLE
.\scripts\dev\bootstrap.ps1
#>

[CmdletBinding()]
param(
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
Push-Location $repoRoot
try {
    Write-Host ""
    Write-Host "=== EdgeConnect onboarding bootstrap ==="
    Write-Host ""

    # ── 1. Prerequisite check ────────────────────────────────────────────────
    Write-Host "[1/4] Checking prerequisites..."

    $dotnetVersion = (dotnet --version 2>$null)
    if (-not $dotnetVersion -or -not $dotnetVersion.StartsWith('8.')) {
        Write-Error "  .NET 8 SDK is required. Found: '$dotnetVersion'. Install from https://dotnet.microsoft.com/download/dotnet/8.0 and re-run."
        exit 1
    }
    Write-Host "  [ok] dotnet $dotnetVersion"

    if ($PSVersionTable.PSVersion.Major -lt 7) {
        Write-Error "  PowerShell 7+ is required. You're on $($PSVersionTable.PSVersion). Install from https://github.com/PowerShell/PowerShell/releases."
        exit 1
    }
    Write-Host "  [ok] pwsh $($PSVersionTable.PSVersion)"

    $gitVersion = (git --version 2>$null)
    if (-not $gitVersion) {
        Write-Error "  git is not on PATH."
        exit 1
    }
    Write-Host "  [ok] $gitVersion"
    Write-Host ""

    # ── 2. Restore ───────────────────────────────────────────────────────────
    Write-Host "[2/4] Restoring NuGet packages..."
    dotnet restore ElpisEdgeConnect.sln --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "  dotnet restore failed. If you're behind a proxy, set HTTP_PROXY / HTTPS_PROXY and retry."
        exit 1
    }
    Write-Host ""

    # ── 3. Build ─────────────────────────────────────────────────────────────
    Write-Host "[3/4] Building the solution..."
    dotnet build ElpisEdgeConnect.sln --no-restore --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "  Build failed. Look up the diagnostic in docs/onboarding/08-troubleshooting.md."
        exit 1
    }
    Write-Host ""

    # ── 4. Tests ─────────────────────────────────────────────────────────────
    if ($SkipTests) {
        Write-Host "[4/4] Skipping tests (-SkipTests)."
    }
    else {
        Write-Host "[4/4] Running unit tests (filter: Category!=Flaky)..."
        dotnet test ElpisEdgeConnect.sln --no-build --nologo --filter "Category!=Flaky"
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "  Some tests failed. If only ConfigSchemaModelTests failed, that's a known pre-existing issue (task_b3eda035). Otherwise see docs/onboarding/08-troubleshooting.md."
            # Don't exit — bootstrap should finish so the next-step message still prints.
        }
    }
    Write-Host ""

    # ── Next steps ───────────────────────────────────────────────────────────
    Write-Host "=== Bootstrap complete ==="
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host ""
    Write-Host "  1. Read docs/onboarding/02-dev-license.md and set:"
    Write-Host "       .\scripts\dev\sign-dev-license.ps1   # generates a license bound to THIS machine"
    Write-Host "       `$env:EDGECONNECT_LICENSE_PATH = `"$repoRoot\data\dev-license.local.json`""
    Write-Host ""
    Write-Host "  2. Run .\scripts\dev\setup-dev-config.ps1 to seed your dev gateway data root."
    Write-Host ""
    Write-Host "  3. Launch the Studio:"
    Write-Host "       dotnet run --project src\ElpisEdgeConnect.Management"
    Write-Host ""
    Write-Host "  4. Open http://127.0.0.1:5080/"
    Write-Host ""
}
finally {
    Pop-Location
}
