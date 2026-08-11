#requires -Version 5.1
<#
.SYNOPSIS
Sign a fresh dev license, bound to THIS machine's gateway identity, using the
in-repo dev RSA private key.

.DESCRIPTION
The dev keypair is committed to the repo:

  public  : src/ElpisEdgeConnect.Core/Licensing/EmbeddedPublicKey.cs
  private : tests/ElpisEdgeConnect.Core.Tests/Licensing/TestRsaKeys.cs

This script resolves this machine's gateway identity, extracts the private key
constant to a temp .pem file, invokes tools/LicenseGen to sign a long-validity
Enterprise license with customer 'DevOnly' and every protocol +
connectivity-studio module enabled, writes the signed file to the gitignored
data/dev-license.local.json, and scrubs the temp file.

Re-run when the license expires (default expiry is 5 years from issuance).

GATEWAY BINDING
This script used to hardcode --gateway dev-gateway. That string is never
produced as a machine identity: ADR-0038 mints a fresh UUID on any machine
that has not run EdgeConnect before, and ADR-0036 makes LicenseManager reject
a license whose gatewayId does not match. So the committed license activated
on no fresh machine and blocked onboarding with:

    License 'LIC-DEV-...' is issued for gateway 'dev-gateway', but this
    gateway's identity is '<uuid>'.

The gateway id now defaults to whatever this machine actually resolves,
read from the same machine-anchored locations GatewayIdentityStore uses.
Pass -GatewayId to sign for a different machine.

Note that Gateway.GatewayId in current.json is a DIFFERENT field and is not
what binding checks - that comes from GatewayIdentityStore.

.PARAMETER ExpiresOn
ISO date the license expires. Default: 5 years from today (clamped to
2099 to avoid silly future dates).

.PARAMETER GatewayId
Gateway identity to bind the license to. Default: this machine's resolved
identity. Supply explicitly to sign a license for another machine.

.PARAMETER OutPath
Where to write the signed license. Default: data/dev-license.local.json,
which is gitignored - a license bound to one machine must NOT be committed,
which is exactly how the broken dev-gateway license came to be shared.

.EXAMPLE
.\scripts\dev\sign-dev-license.ps1

.EXAMPLE
.\scripts\dev\sign-dev-license.ps1 -ExpiresOn 2030-12-31

.EXAMPLE
.\scripts\dev\sign-dev-license.ps1 -GatewayId 97c11d12-1234-5678-9abc-def012345678
#>

[CmdletBinding()]
param(
    [string]$ExpiresOn,
    [string]$GatewayId,
    [string]$OutPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Mirrors GatewayIdentityStore.ForIdentityPath: machine-wide record first
# (authoritative, and survives deleting any per-data-root folder), then the
# Windows registry mirror, then the per-data-root file. Records are stored as
# "<gatewayId>|<machineFingerprint>", so only the part before the pipe is the id.
function Resolve-MachineGatewayId {
    $candidates = @(
        (Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'Elpis\EdgeConnect\identity')
    )

    $dataRoot = [Environment]::GetEnvironmentVariable('EDGECONNECT_DATA_ROOT')
    if ([string]::IsNullOrWhiteSpace($dataRoot)) {
        $dataRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'EdgeConnect'
    }
    $candidates += (Join-Path $dataRoot 'identity')

    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) {
            $raw = (Get-Content -LiteralPath $path -Raw).Trim()
            if ($raw) { return $raw.Split('|')[0].Trim() }
        }
    }

    # Registry mirror - the one location that survives wiping ProgramData entirely.
    try {
        $reg = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Elpis\EdgeConnect' -Name 'GatewayId' -ErrorAction Stop
        if ($reg.GatewayId) { return ([string]$reg.GatewayId).Split('|')[0].Trim() }
    } catch { }

    return $null
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
Push-Location $repoRoot
try {
    if (-not $ExpiresOn) {
        $ExpiresOn = (Get-Date).AddYears(5).ToString('yyyy-MM-dd')
    }
    if (-not $OutPath) {
        # data/ is gitignored. Deliberately NOT docs/onboarding/dev-license.json:
        # that file is committed, and a machine-bound license in a shared file is
        # the original defect.
        $OutPath = Join-Path $repoRoot 'data/dev-license.local.json'
    }
    $outDir = Split-Path -Parent $OutPath
    if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }

    if (-not $GatewayId) {
        $GatewayId = Resolve-MachineGatewayId
        if (-not $GatewayId) {
            Write-Error @"
Could not resolve this machine's gateway identity, so the license would be
bound to nothing usable.

The identity is minted on first run. Either:
  * start EdgeConnect once, then re-run this script; or
  * pass the id explicitly:  -GatewayId <uuid>

The startup log states it verbatim: "Gateway identity resolved: <uuid>".
"@
            exit 1
        }
        $idSource = 'resolved from this machine'
    }
    else {
        $idSource = 'supplied explicitly'
    }

    Write-Host ""
    Write-Host "=== Sign dev license ==="
    Write-Host "  gateway : $GatewayId  ($idSource)"
    Write-Host "  expires : $ExpiresOn"
    Write-Host "  out     : $OutPath"
    Write-Host ""

    $keysFile = Join-Path $repoRoot 'tests/ElpisEdgeConnect.Core.Tests/Licensing/TestRsaKeys.cs'
    if (-not (Test-Path -LiteralPath $keysFile)) {
        Write-Error "Dev private key file missing: $keysFile"
        exit 1
    }

    # Extract the PrivatePem const value from TestRsaKeys.cs. The file embeds
    # the PEM as a C# string-concat literal of the form
    #   "-----BEGIN PRIVATE KEY-----\n" + "..." + ... + "-----END PRIVATE KEY-----\n"
    # We pull each "..." segment between the BEGIN and END markers and join
    # them with newlines.
    $content = Get-Content -LiteralPath $keysFile -Raw
    $beginIdx = $content.IndexOf('"-----BEGIN PRIVATE KEY-----')
    $endIdx = $content.IndexOf('"-----END PRIVATE KEY-----\n"')
    if ($beginIdx -lt 0 -or $endIdx -lt 0) {
        Write-Error "Could not locate PEM markers in TestRsaKeys.cs. File may have changed shape."
        exit 1
    }
    $endIdx += '"-----END PRIVATE KEY-----\n"'.Length
    $pemSegment = $content.Substring($beginIdx, $endIdx - $beginIdx)
    # Extract individual string segments
    $segmentRegex = [regex]'"([^"]*)"'
    $segments = $segmentRegex.Matches($pemSegment) | ForEach-Object { $_.Groups[1].Value }
    $pem = ($segments -join '').Replace('\n', "`n")

    $tmpKey = [System.IO.Path]::GetTempFileName()
    try {
        # Write UTF-8 with NO byte-order mark. Set-Content -Encoding UTF8 emits a
        # BOM on Windows PowerShell 5.1 (but not on pwsh 7), and a BOM ahead of
        # "-----BEGIN PRIVATE KEY-----" makes the PEM unparseable - which is why
        # this script used to demand -Version 7.0. Going through .NET directly
        # gives identical bytes on both, so 5.1 works too.
        [System.IO.File]::WriteAllText($tmpKey, $pem, (New-Object System.Text.UTF8Encoding $false))

        # Build LicenseGen if needed.
        if (-not (Test-Path (Join-Path $repoRoot 'tools/LicenseGen/bin/Debug/net8.0/LicenseGen.dll'))) {
            Write-Host "  Building tools/LicenseGen..."
            dotnet build (Join-Path $repoRoot 'tools/LicenseGen/LicenseGen.csproj') --nologo --verbosity minimal
            if ($LASTEXITCODE -ne 0) {
                Write-Error "LicenseGen build failed."
                exit 1
            }
        }

        $modules = @(
            'core-runtime=true'
            'sink-mqtt=true'
            'sink-opc-ua-server=true'
            'sink-http=true'
            'sink-tcp=true'
            'source-modbus-tcp=true'
            'source-focas2=true'
            'source-mtconnect=true'
            'source-brother-http=true'
            'source-s7=true'
            'source-opc-ua-client=true'
            'connectivity-studio=true'
            'historian-bridge=true'
        ) -join ','

        & dotnet run --project (Join-Path $repoRoot 'tools/LicenseGen') --no-build -- `
            new `
            --customer DevOnly `
            --gateway $GatewayId `
            --edition Enterprise `
            --expires $ExpiresOn `
            --license-id "LIC-DEV-$(Get-Date -Format 'yyyyMMdd')" `
            --modules $modules `
            --max-sources 50 `
            --max-sinks 5 `
            --max-routes 100 `
            --private-key $tmpKey `
            --out $OutPath

        if ($LASTEXITCODE -ne 0) {
            Write-Error "LicenseGen 'new' failed (exit $LASTEXITCODE)."
            exit 1
        }
    }
    finally {
        if (Test-Path -LiteralPath $tmpKey) {
            Remove-Item -LiteralPath $tmpKey -Force
        }
    }

    Write-Host ""
    Write-Host "[done] Signed dev license at $OutPath"
    Write-Host ""
    Write-Host "Next:"
    Write-Host "  `$env:EDGECONNECT_LICENSE_PATH = `"$OutPath`""
    Write-Host ""
}
finally {
    Pop-Location
}
