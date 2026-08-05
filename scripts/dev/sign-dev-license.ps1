#requires -Version 7.0
<#
.SYNOPSIS
Sign a fresh dev license using the in-repo dev RSA private key.

.DESCRIPTION
The dev keypair is committed to the repo:

  public  : src/ElpisEdgeConnect.Core/Licensing/EmbeddedPublicKey.cs
  private : tests/ElpisEdgeConnect.Core.Tests/Licensing/TestRsaKeys.cs

This script extracts the private key constant to a temp .pem file, invokes
tools/LicenseGen to sign a long-validity Enterprise license with customer
'DevOnly' and every protocol + connectivity-studio module enabled, writes
the signed file to docs/onboarding/dev-license.json, and scrubs the
temp file.

Re-run when the license expires (default expiry is 5 years from issuance).

.PARAMETER ExpiresOn
ISO date the license expires. Default: 5 years from today (clamped to
2099 to avoid silly future dates).

.PARAMETER OutPath
Where to write the signed license. Default: docs/onboarding/dev-license.json.

.EXAMPLE
.\scripts\dev\sign-dev-license.ps1

.EXAMPLE
.\scripts\dev\sign-dev-license.ps1 -ExpiresOn 2030-12-31
#>

[CmdletBinding()]
param(
    [string]$ExpiresOn,
    [string]$OutPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
Push-Location $repoRoot
try {
    if (-not $ExpiresOn) {
        $ExpiresOn = (Get-Date).AddYears(5).ToString('yyyy-MM-dd')
    }
    if (-not $OutPath) {
        $OutPath = Join-Path $repoRoot 'docs/onboarding/dev-license.json'
    }

    Write-Host ""
    Write-Host "=== Sign dev license ==="
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
        # -NoNewline so we don't append a trailing newline to the PEM.
        Set-Content -Path $tmpKey -Value $pem -NoNewline -Encoding UTF8

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
            --gateway dev-gateway `
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
