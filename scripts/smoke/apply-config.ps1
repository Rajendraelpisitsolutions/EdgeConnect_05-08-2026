<#
.SYNOPSIS
  M.P2.2 phase 3 smoke helper -- import a draft, validate, apply.
  Prints the apply response (including the new `reload` block).

.DESCRIPTION
  Wraps three Management API calls -- POST /config/drafts, POST
  /config/drafts/{id}/validate, POST /config/drafts/{id}/apply --
  into one reproducible operation. Used by the manual smoke
  procedure in docs/smoke/mp22-hot-reload.md.

  Not wired to CI. Smoke is human verification by design (phase 3
  plan v2 §2 Q6 verdict); these helpers exist for reproducibility
  and demo value.

.PARAMETER ConfigPath
  Path to the gateway.json file to import as a draft.

.PARAMETER BaseUrl
  Management API base URL. Default: http://127.0.0.1:5080.

.PARAMETER Actor
  Actor name recorded in the audit entry. Default: "smoke".

.EXAMPLE
  .\apply-config.ps1 -ConfigPath .\smoke-1-clean.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ConfigPath,

    [string] $BaseUrl = "http://127.0.0.1:5080",

    [string] $Actor = "smoke"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $ConfigPath -PathType Leaf)) {
    throw "ConfigPath not found: $ConfigPath"
}

# The file holds a raw GatewayConfiguration; the endpoint wants it
# wrapped in CreateDraftRequestDto = { configuration: <config>, actor: "..." }.
# Build the envelope by string-concatenating the verbatim file content
# (avoids a PowerShell ConvertFrom-Json/ConvertTo-Json round-trip that
# could subtly reshape numbers or property casing).
$gatewayJson = Get-Content -Path $ConfigPath -Raw
$actorJson = ConvertTo-Json $Actor -Compress
$body = '{"configuration":' + $gatewayJson + ',"actor":' + $actorJson + '}'

Write-Host "==> Importing draft from $ConfigPath"
$draftResp = Invoke-RestMethod `
    -Uri "$BaseUrl/api/v1/config/drafts" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
$draftId = $draftResp.draftId
if (-not $draftId) {
    Write-Error "Import did not return a draftId. Response:"
    $draftResp | ConvertTo-Json -Depth 6 | Write-Host
    exit 1
}
Write-Host "    draftId = $draftId"

Write-Host "==> Validating draft"
$validateResp = Invoke-RestMethod `
    -Uri "$BaseUrl/api/v1/config/drafts/$([uri]::EscapeDataString($draftId))/validate" `
    -Method Post
if (-not $validateResp.isValid) {
    Write-Host "    VALIDATION FAILED -- apply aborted." -ForegroundColor Red
    $validateResp | ConvertTo-Json -Depth 6 | Write-Host
    exit 1
}
$warningCount = if ($validateResp.warnings) { $validateResp.warnings.Count } else { 0 }
Write-Host "    OK ($warningCount warning(s))"

Write-Host "==> Applying draft"
$applyBody = @{ actor = $Actor } | ConvertTo-Json -Compress
try {
    $applyResp = Invoke-RestMethod `
        -Uri "$BaseUrl/api/v1/config/drafts/$([uri]::EscapeDataString($draftId))/apply" `
        -Method Post `
        -ContentType "application/json" `
        -Body $applyBody
}
catch [System.Net.WebException] {
    Write-Host "    APPLY FAILED -- non-2xx response:" -ForegroundColor Red
    $_.Exception.Response | Format-List | Write-Host
    throw
}

Write-Host ""
Write-Host "==> Apply response"
$applyResp | ConvertTo-Json -Depth 8

# Emit the response object so callers can pipe into other helpers
# (e.g. `wait-for-reload.ps1 -VersionId (apply-config.ps1 ...).newVersionId`).
$applyResp
