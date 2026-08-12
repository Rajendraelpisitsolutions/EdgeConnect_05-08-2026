#requires -Version 7.0
<#
.SYNOPSIS
Bulk-provision gateway.json configs from a CSV of devices + a sidecar.

.DESCRIPTION
Offline generator subsystem (Chip 3). Reads a CSV of device rows + a
gateway sidecar file, materializes one or more gateway.json configs by
substituting placeholders into a protocol template, canonicalizes the
output (UTF-8 no BOM, LF endings, sorted keys), and writes them to the
output directory along with a deterministic `_provisioning` block.

This script is a THIN SHELL per Chip 3 v2 plan §5.5.2 — it handles
argument parsing, file IO, and orchestration only. All rendering,
canonicalization, validation, and fingerprinting logic lives in the
sibling `lib/*.ps1` modules so that the heavy lifting stays portable to
a future .NET re-host without rewriting the algorithm.

.PARAMETER Csv
Path to the input CSV. Required columns: deviceId, deviceName, host,
enabled. Extra columns are tolerated and surfaced in the run summary
but ignored by the substitution pass.

.PARAMETER Sidecar
Path to the gateway sidecar (YAML or JSON; auto-detected by extension).
Carries per-gateway placeholders — see templates/MANIFEST.md for the
canonical list.

.PARAMETER Template
Logical template id (e.g. `template-fanuc`) OR a path to a template
file. Logical ids resolve to `templates/<id>-v<version>.json` — the
default version is 1; pin a different version with -TemplateVersion.

.PARAMETER TemplateVersion
Override the resolved template version when -Template is a logical id.
Default: 1.

.PARAMETER OutDir
Output directory. Created if it does not exist. The generator writes
one gateway.json per CSV row (filename: `<deviceId>.gateway.json`) plus
a `run-summary.json` and a `MANIFEST.txt` listing every output file with
its SHA-256 hash. Default: `out/`.

.PARAMETER GatewayProvisioningId
Override the auto-generated provisioning id. Default: a fresh GUID per
run. Passed in explicitly when re-running to verify the deterministic
guarantee (§5.4.4).

.PARAMETER GeneratedAt
Override the `_provisioning.generatedAt` timestamp. Defaults to
DateTimeOffset.UtcNow at run start. The deterministic guarantee is
"byte-identical modulo generatedAt"; pin this only for the round-trip
test.

.PARAMETER SkipValidate
Disables BOTH the sidecar schema validation that runs before substitution
AND the per-output gateway-config schema validation that runs after
canonicalization. Default: false (validate is mandatory unless explicitly
skipped for local debugging). Use only when iterating against deliberately
broken inputs; re-run without -SkipValidate before deploying generated
configs to a gateway box.

.EXAMPLE
./generate.ps1 -Csv .\samples\sample-fanuc.csv -Sidecar .\samples\sample-fanuc.gateway.yml -Template template-fanuc -OutDir .\out

.EXAMPLE
# Re-run with pinned ids to confirm deterministic output.
./generate.ps1 -Csv .\samples\sample-fanuc.csv -Sidecar .\samples\sample-fanuc.gateway.yml -Template template-fanuc -OutDir .\out -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 -GeneratedAt 2026-01-01T00:00:00Z
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Csv,

    [Parameter(Mandatory)]
    [string]$Sidecar,

    [Parameter(Mandatory)]
    [string]$Template,

    [int]$TemplateVersion = 1,

    [string]$OutDir = (Join-Path $PSScriptRoot 'out'),

    [string]$GatewayProvisioningId,

    [string]$GeneratedAt,

    [switch]$SkipValidate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Constants ────────────────────────────────────────────────────────────
$script:GENERATOR_VERSION = '0.1.0'
$script:GENERATED_BY      = 'tools/bulk-provision/generate.ps1'

# ── Stable error codes (v4 §6 A.B1) ──────────────────────────────────────
# Every throw in this file is prefixed with one of these codes so Pester
# tests assert on the code + substring tokens, never on full messages.
$script:ErrCodes = @{
    TemplateNotFound          = 'BulkProvision.TemplateNotFound'
    SidecarNotFound           = 'BulkProvision.SidecarNotFound'
    SidecarFormatUnsupported  = 'BulkProvision.SidecarFormatUnsupported'
    SidecarYamlMalformed      = 'BulkProvision.SidecarYamlMalformed'
    CsvNotFound               = 'BulkProvision.CsvNotFound'
    CsvEmpty                  = 'BulkProvision.CsvEmpty'
    CsvMissingColumn          = 'BulkProvision.CsvMissingColumn'
    CsvDuplicateDeviceId      = 'BulkProvision.CsvDuplicateDeviceId'
    ConfigSchemaViolation     = 'BulkProvision.ConfigSchemaViolation'
    SidecarSchemaViolation    = 'BulkProvision.SidecarSchemaViolation'
}

# ── Lib loading ──────────────────────────────────────────────────────────
$libRoot = Join-Path $PSScriptRoot 'lib'
. (Join-Path $libRoot 'Canonicalize-Json.ps1')
. (Join-Path $libRoot 'Substitute-Placeholders.ps1')

# ── Path resolution ──────────────────────────────────────────────────────
function Resolve-TemplatePath {
    param([string]$TemplateRef, [int]$Version)
    if (Test-Path -LiteralPath $TemplateRef -PathType Leaf) {
        return (Resolve-Path -LiteralPath $TemplateRef).Path
    }
    $candidate = Join-Path $PSScriptRoot "templates/$TemplateRef-v$Version.json"
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "$($script:ErrCodes.TemplateNotFound): template '$TemplateRef' v$Version not found at $candidate. Known templates live in tools/bulk-provision/templates/ (see MANIFEST.md)."
    }
    return (Resolve-Path -LiteralPath $candidate).Path
}

function Resolve-TemplateId {
    param([string]$TemplateRef)
    if (Test-Path -LiteralPath $TemplateRef -PathType Leaf) {
        # File-form: strip `-vN.json` suffix to recover the logical id.
        $name = [System.IO.Path]::GetFileNameWithoutExtension($TemplateRef)
        return $name -replace '-v\d+$', ''
    }
    return $TemplateRef
}

# ── Sidecar parsing ──────────────────────────────────────────────────────
function Read-Sidecar {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$($script:ErrCodes.SidecarNotFound): sidecar not found: $Path"
    }
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    $content = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    switch ($ext) {
        '.json' { return ($content | ConvertFrom-Json -AsHashtable) }
        '.yml'  { return ConvertFrom-Yaml-Minimal -Yaml $content }
        '.yaml' { return ConvertFrom-Yaml-Minimal -Yaml $content }
        default { throw "$($script:ErrCodes.SidecarFormatUnsupported): sidecar must be .json/.yml/.yaml - got '$ext'" }
    }
}

function ConvertFrom-Yaml-Minimal {
    # Minimal flat-keys-only YAML parser — sidecar is documented as
    # one-level-deep `key: value`. We avoid a YAML module dependency to
    # keep the bootstrap zero-install per v2 §5.5.4.
    param([string]$Yaml)
    $result = @{}
    foreach ($line in ($Yaml -split "`n")) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        $idx = $trimmed.IndexOf(':')
        if ($idx -lt 1) {
            throw "$($script:ErrCodes.SidecarYamlMalformed): sidecar YAML malformed line (no key:value): '$line'"
        }
        $key = $trimmed.Substring(0, $idx).Trim()
        $value = $trimmed.Substring($idx + 1).Trim()
        # Strip surrounding quotes if present.
        if ($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $result[$key] = $value
    }
    return $result
}

# ── CSV parsing ──────────────────────────────────────────────────────────
function Read-CsvRows {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$($script:ErrCodes.CsvNotFound): CSV not found: $Path"
    }
    $rows = @(Import-Csv -LiteralPath $Path -Encoding UTF8)
    if ($rows.Count -eq 0) {
        throw "$($script:ErrCodes.CsvEmpty): CSV is empty or has only a header - at least one device row is required."
    }
    # Required-column check.
    $required = @('deviceId', 'deviceName', 'host', 'enabled')
    $headers = @($rows[0].PSObject.Properties.Name)
    foreach ($col in $required) {
        if ($col -notin $headers) {
            throw "$($script:ErrCodes.CsvMissingColumn): CSV missing required column: '$col'. Required: $($required -join ', ')."
        }
    }
    # Duplicate-deviceId check.
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $rows) {
        if (-not $seen.Add($row.deviceId)) {
            throw "$($script:ErrCodes.CsvDuplicateDeviceId): CSV duplicate deviceId '$($row.deviceId)' appears more than once."
        }
    }
    return $rows
}

# ── Fingerprinting ───────────────────────────────────────────────────────
function Get-Sha256Hex {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
        return -join ($hash | ForEach-Object { $_.ToString('x2') })
    } finally {
        $sha.Dispose()
    }
}

function Get-Sha256HexOfString {
    param([string]$Text)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
        return -join ($hash | ForEach-Object { $_.ToString('x2') })
    } finally {
        $sha.Dispose()
    }
}

# ── Provisioning block ──────────────────────────────────────────────────
function Build-ProvisioningBlock {
    param(
        [string]$TemplateId,
        [int]$TemplateSchemaVersion,
        [string]$FleetId,
        [string]$GatewayProvisioningId,
        [string]$GeneratedAtIso,
        [string]$CsvFingerprint,
        [string]$ConfigFingerprint  # Filled by caller AFTER canonicalization.
    )
    return [ordered]@{
        generatedBy            = $script:GENERATED_BY
        generatorVersion       = $script:GENERATOR_VERSION
        templateId             = $TemplateId
        templateSchemaVersion  = $TemplateSchemaVersion
        fleetId                = $FleetId
        gatewayProvisioningId  = $GatewayProvisioningId
        generatedAt            = $GeneratedAtIso
        csvFingerprint         = $CsvFingerprint
        configFingerprint      = $ConfigFingerprint
    }
}

# ── Main ─────────────────────────────────────────────────────────────────
function Invoke-BulkProvision {
    Write-Host "[bulk-provision] generate.ps1 v$($script:GENERATOR_VERSION)"

    $templatePath = Resolve-TemplatePath -TemplateRef $Template -Version $TemplateVersion
    $templateId   = Resolve-TemplateId -TemplateRef $Template
    Write-Host "  template     : $templateId (v$TemplateVersion) → $templatePath"

    if (-not $GatewayProvisioningId) {
        $GatewayProvisioningId = [guid]::NewGuid().ToString()
    }
    if (-not $GeneratedAt) {
        $GeneratedAt = [DateTimeOffset]::UtcNow.ToString('o')
    }
    Write-Host "  provisioning : $GatewayProvisioningId"
    Write-Host "  generatedAt  : $GeneratedAt"

    $csvAbs    = (Resolve-Path -LiteralPath $Csv).Path
    $sidecarAbs= (Resolve-Path -LiteralPath $Sidecar).Path
    # Per v4 §3.Q2: serialized run-summary paths are $PWD-relative + forward-
    # slash normalized so the run-summary.json compares byte-identical across
    # Windows + Linux hosts. Filesystem operations continue to use the
    # absolute form -- normalization is for serialized output ONLY.
    $csvRel    = (Resolve-Path -LiteralPath $Csv -Relative)    -replace '\\', '/'
    $sidecarRel= (Resolve-Path -LiteralPath $Sidecar -Relative) -replace '\\', '/'
    $csvFingerprint = Get-Sha256Hex -Path $csvAbs

    # ── Sidecar schema validation (v3 §7.C) ──────────────────────────────
    # Runs BEFORE Read-Sidecar so malformed YAML surfaces as the validator's
    # exit 3 with an operator-friendly wrapped message, NOT a less-precise
    # PowerShell parse error. -SkipValidate bypasses BOTH this stage AND the
    # per-output ValidateConfig stage below.
    if (-not $SkipValidate) {
        $sidecarValidator = (Resolve-Path (Join-Path $PSScriptRoot '../ValidateSidecar/ValidateSidecar.csproj')).Path
        $schemaPath       = (Resolve-Path (Join-Path $PSScriptRoot 'sidecar-schema.json')).Path
        Write-Host "  validating sidecar via tools/ValidateSidecar..."
        $sidecarValidateResult = & dotnet run --project $sidecarValidator -- `
            --schema $schemaPath --sidecar $sidecarAbs 2>&1
        $sidecarValidateExit = $LASTEXITCODE
        if ($sidecarValidateExit -ne 0) {
            # Re-emit captured stderr on the host's stderr stream, not via
            # Write-Host (which mixes channels). [Console]::Error.WriteLine
            # preserves the stream separation per v3 §7.C.
            foreach ($line in $sidecarValidateResult) {
                [Console]::Error.WriteLine($line)
            }
            throw "$($script:ErrCodes.SidecarSchemaViolation): sidecar validation failed (exit $sidecarValidateExit). Review stderr above."
        }
        Write-Host "  [ok] sidecar valid"
    }

    $sidecar = Read-Sidecar -Path $sidecarAbs
    $rows    = Read-CsvRows -Path $csvAbs
    Write-Host "  csv rows     : $($rows.Count)"

    if (-not (Test-Path -LiteralPath $OutDir)) {
        New-Item -ItemType Directory -Path $OutDir | Out-Null
    }
    # [System.IO.File]::* uses the .NET process's CurrentDirectory, which can
    # diverge from PowerShell's $PWD after an interactive `cd`. Resolve OutDir
    # to absolute here so every downstream Write-CanonicalJson / WriteAllBytes
    # call targets the directory the operator actually meant.
    $OutDir = (Resolve-Path -LiteralPath $OutDir).Path
    $templateRaw = Get-Content -LiteralPath $templatePath -Raw -Encoding UTF8

    $outputs = @()
    foreach ($row in $rows) {
        $instanceId = "$($row.deviceId)-source"
        $perRow = [ordered]@{
            instanceId = $instanceId
            deviceId   = $row.deviceId
            deviceName = $row.deviceName
            host       = $row.host
            enabled    = $row.enabled
        }
        # Per-gateway placeholders come from the sidecar — see MANIFEST.md.
        $rendered = Invoke-PlaceholderSubstitution `
            -Template $templateRaw `
            -PerRow $perRow `
            -PerGateway $sidecar

        # Replace the template's _provisioning marker with the real block.
        # ConfigFingerprint is computed over the canonical body MINUS
        # generatedAt (§5.4.4) — left empty here, filled by canonicalize stage.
        $provisioning = Build-ProvisioningBlock `
            -TemplateId $templateId `
            -TemplateSchemaVersion $TemplateVersion `
            -FleetId $sidecar.fleetId `
            -GatewayProvisioningId $GatewayProvisioningId `
            -GeneratedAtIso $GeneratedAt `
            -CsvFingerprint $csvFingerprint `
            -ConfigFingerprint ''

        $rendered = Set-ProvisioningBlock -Json $rendered -Block $provisioning

        # Canonicalize.
        $canonical = ConvertTo-CanonicalJson -Json $rendered

        # Now compute configFingerprint over the canonical body minus
        # the generatedAt line + the fingerprint placeholder line itself,
        # then re-emit. Two-pass keeps the §5.4.4 contract clean.
        $fingerprintInput = $canonical `
            -replace '"generatedAt":\s*"[^"]*"', '"generatedAt": "<<pinned>>"' `
            -replace '"configFingerprint":\s*"[^"]*"', '"configFingerprint": "<<pinned>>"'
        $configFingerprint = Get-Sha256HexOfString -Text $fingerprintInput
        $canonical = $canonical -replace '"configFingerprint":\s*""', "`"configFingerprint`": `"$configFingerprint`""

        $outName = "$($row.deviceId).gateway.json"
        $outPath = Join-Path $OutDir $outName
        Write-CanonicalJson -Path $outPath -Content $canonical

        $outputs += [ordered]@{
            deviceId           = $row.deviceId
            file               = $outName
            sha256             = Get-Sha256Hex -Path $outPath
            configFingerprint  = $configFingerprint
        }
        Write-Host "  [ok] $outName"
    }

    # Run summary.
    $summary = [ordered]@{
        generatorVersion      = $script:GENERATOR_VERSION
        templateId            = $templateId
        templateSchemaVersion = $TemplateVersion
        gatewayProvisioningId = $GatewayProvisioningId
        generatedAt           = $GeneratedAt
        csvFingerprint        = $csvFingerprint
        csv                   = $csvRel
        sidecar               = $sidecarRel
        outputs               = $outputs
    }
    $summaryJson = ConvertTo-CanonicalJson -Json ($summary | ConvertTo-Json -Depth 10 -Compress:$false)
    Write-CanonicalJson -Path (Join-Path $OutDir 'run-summary.json') -Content $summaryJson

    # MANIFEST.txt — flat list of output files + hashes, for diffing
    # between runs.
    $manifestLines = @("# bulk-provision MANIFEST.txt", "# generator: $($script:GENERATED_BY) v$($script:GENERATOR_VERSION)", "")
    foreach ($o in $outputs) { $manifestLines += "$($o.sha256)  $($o.file)" }
    [System.IO.File]::WriteAllBytes(
        (Join-Path $OutDir 'MANIFEST.txt'),
        ([System.Text.UTF8Encoding]::new($false).GetBytes((($manifestLines -join "`n") + "`n")))
    )

    # Validate stage.
    if (-not $SkipValidate) {
        Write-Host "  validating outputs via tools/ValidateConfig..."
        $validatorProject = Join-Path $PSScriptRoot '..\ValidateConfig\ValidateConfig.csproj'
        $validatorProject = (Resolve-Path -LiteralPath $validatorProject).Path
        foreach ($o in $outputs) {
            $outPath = Join-Path $OutDir $o.file
            $result  = & dotnet run --project $validatorProject -- $outPath 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host $result
                throw "$($script:ErrCodes.ConfigSchemaViolation): schema validation failed for $($o.file) (exit $LASTEXITCODE). Run halted; review stderr above."
            }
        }
        Write-Host "  [ok] all outputs valid"
    } else {
        Write-Host "  [warn] -SkipValidate: schema validation bypassed (debug only)"
    }

    Write-Host "[done] bulk-provision complete: $($outputs.Count) gateway.json files in $OutDir"
}

Invoke-BulkProvision
