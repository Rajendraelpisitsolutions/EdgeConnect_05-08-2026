<#
.SYNOPSIS
    Removes stale / orphaned "Elpis EdgeConnect" Add/Remove Programs (ARP)
    entries left behind by rolled-back or failed MSI installs.

.DESCRIPTION
    During development, interrupted MSI installs (error 1603/1920 rollbacks)
    can leave orphaned Uninstall registry keys. These show up as duplicate
    "Elpis EdgeConnect" rows in Apps and Features with a blank icon and no
    working uninstall - Windows Installer no longer knows about the product,
    so the user cannot remove them through the UI.

    This script targets ONLY entries that match ALL of the following, so it
    can never touch a healthy install:
      * DisplayName / Publisher identifies it as Elpis EdgeConnect
      * WindowsInstaller = 1            (an MSI entry, not the Burn bundle)
      * SystemComponent not 1          (visible; the live MSI is hidden by Burn)
      * InstallLocation missing/empty OR the folder does not exist
                                       (i.e. no files are actually installed)

    For each match it first attempts a clean msiexec /x (in case Windows
    Installer still has partial metadata), then removes any leftover ARP
    registry key directly.

    The LIVE install is safe: the Burn bundle entry has WindowsInstaller unset,
    and the chained MSI is SystemComponent=1 - both are excluded by design.

.PARAMETER Apply
    Actually perform the removal. Without it the script runs in preview mode
    and only lists what it WOULD remove.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\Clean-StaleArpEntries.ps1
    Preview (default) - lists orphans, changes nothing.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\Clean-StaleArpEntries.ps1 -Apply
    Actually clean them (run elevated).

.NOTES
    Run elevated (Administrator) when using -Apply; HKLM writes require it.
#>
[CmdletBinding()]
param(
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

$uninstallRoots = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
)

function Test-IsElpis {
    param($props)
    return ($props.DisplayName -like '*Elpis EdgeConnect*') -or
           ($props.DisplayName -like '*Elpis*' -and $props.DisplayName -like '*EdgeConnect*') -or
           ($props.Publisher   -like '*Elpis IT Solutions*' -and $props.DisplayName -like '*EdgeConnect*')
}

function Test-IsOrphanMsi {
    param($props)
    # Must be an MSI entry (not the Burn bundle).
    if ($props.WindowsInstaller -ne 1) { return $false }
    # Never touch the live install: Burn hides the chained MSI as SystemComponent=1.
    if ($props.SystemComponent -eq 1)  { return $false }
    # Orphan = no real install on disk.
    $loc = $props.InstallLocation
    if ([string]::IsNullOrWhiteSpace($loc)) { return $true }
    return -not (Test-Path -LiteralPath $loc)
}

$found = @()
foreach ($root in $uninstallRoots) {
    if (-not (Test-Path $root)) { continue }
    Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
        $props = Get-ItemProperty $_.PsPath -ErrorAction SilentlyContinue
        if ($null -eq $props) { return }
        if (-not (Test-IsElpis $props)) { return }
        if (-not (Test-IsOrphanMsi $props)) { return }
        $found += [pscustomobject]@{
            KeyPath     = $_.PsPath
            ProductCode = $_.PSChildName
            DisplayName = $props.DisplayName
            Version     = $props.DisplayVersion
        }
    }
}

if ($found.Count -eq 0) {
    Write-Host "No stale Elpis EdgeConnect ARP entries found. Nothing to clean." -ForegroundColor Green
    return
}

Write-Host "Stale / orphaned Elpis EdgeConnect ARP entries:" -ForegroundColor Yellow
$found | Format-Table ProductCode, Version, DisplayName -AutoSize | Out-String | Write-Host

if (-not $Apply) {
    Write-Host "PREVIEW ONLY - nothing was changed. Re-run elevated with -Apply to remove them." -ForegroundColor Cyan
    return
}

foreach ($e in $found) {
    Write-Host "Removing $($e.DisplayName) $($e.Version)  $($e.ProductCode) ..." -ForegroundColor Yellow
    # 1) Best-effort clean uninstall in case Windows Installer still has metadata.
    try {
        $proc = Start-Process msiexec.exe -ArgumentList "/x $($e.ProductCode) /qn /norestart" -Wait -PassThru -ErrorAction Stop
        Write-Host "   msiexec /x exit code: $($proc.ExitCode)"
    } catch {
        Write-Host "   msiexec /x not applicable ($($_.Exception.Message))"
    }
    # 2) Remove any leftover ARP registry key directly (true orphan).
    if (Test-Path -LiteralPath $e.KeyPath) {
        try {
            Remove-Item -LiteralPath $e.KeyPath -Recurse -Force
            Write-Host "   Removed registry key." -ForegroundColor Green
        } catch {
            Write-Host "   FAILED to remove key: $($_.Exception.Message)" -ForegroundColor Red
        }
    } else {
        Write-Host "   Registry key already gone." -ForegroundColor Green
    }
}

Write-Host "Done. Re-open Apps and Features to confirm the duplicates are gone." -ForegroundColor Green
