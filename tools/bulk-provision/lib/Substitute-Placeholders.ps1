# ============================================================================
# File: tools/bulk-provision/lib/Substitute-Placeholders.ps1
# Purpose: Pure literal placeholder substitution per Chip 3 v2 §5.5.3 — NO
#          expressions, NO conditionals, NO loops, NO recursive expansion.
#          This is the boundary that keeps the subsystem out of
#          "we accidentally built a templating engine" land.
#
#          Anti-templating-engine guards:
#            * Per-row placeholders MUST appear only within Sources[].
#            * Per-gateway placeholders MUST NOT appear within Sources[].
#            * Any `{{ ... }}` marker remaining after substitution → throw.
#            * Result must parse as JSON; if not, throw with the row id.
#
# Reference: docs/sessions/2026-05-21-chip3-provisioning-subsystem-plan-v2.md
#            §5.1 (per-row vs per-gateway scopes), §5.5.3 (boundary).
# ============================================================================

Set-StrictMode -Version Latest

# ── Stable error codes (v4 §3.F3 + §6 A.B1) ──────────────────────────────
# Every throw in this file is prefixed with one of these codes so Pester
# tests assert on the code + substring tokens, never on full messages.
# Codes are not enumerated as types -- a string prefix is enough and
# survives refactoring without breaking the assertion contract.
#
# Variable name is `$script:SubErrCodes` (not `$script:ErrCodes`) because
# generate.ps1 dot-sources this file AFTER defining its own
# `$script:ErrCodes` with generator-specific codes. Sharing the
# `$script:ErrCodes` name would silently overwrite generate.ps1's set
# during dot-source -- a real bug we hit on the first pwsh-7 smoke run.
$script:SubErrCodes = @{
    PlaceholderScopeViolation       = 'BulkProvision.PlaceholderScopeViolation'
    UnresolvedPlaceholder           = 'BulkProvision.UnresolvedPlaceholder'
    RenderedJsonInvalid             = 'BulkProvision.RenderedJsonInvalid'
    PlaceholderRegistryMismatch     = 'BulkProvision.PlaceholderRegistryMismatch'
    ProvisioningMarkerShapeMismatch = 'BulkProvision.ProvisioningMarkerShapeMismatch'
}

# ── Placeholder name registry ─────────────────────────────────────────────
# Keep these in lock-step with templates/MANIFEST.md. Adding a placeholder
# requires updating both lists AND the MANIFEST table.
$script:PerRowPlaceholders = @(
    'instanceId',
    'deviceId',
    'deviceName',
    'host',
    'enabled'
)
$script:PerGatewayPlaceholders = @(
    'gatewayId',
    'gatewayName',
    'gatewayProvisioningId',
    'fleetId',
    'site',
    'mqttHost',
    'mqttPort',
    'mqttQos',
    'mqttClientIdPrefix'
)

<#
.SYNOPSIS
Substitute placeholders into a template string and return the rendered JSON.

.DESCRIPTION
Performs pure literal `{{ key }}` → value replacement. Both per-row and
per-gateway placeholders are substituted in a single pass. Anti-templating-
engine guards run before and after the pass — see §5.5.3.

.PARAMETER Template
The raw template string (post Read-Raw, pre-substitution).

.PARAMETER PerRow
Hashtable / OrderedDict of per-row placeholder values. Keys must match
$script:PerRowPlaceholders. Extra keys → throw. Missing keys → throw.

.PARAMETER PerGateway
Hashtable / OrderedDict of per-gateway placeholder values. Keys must match
$script:PerGatewayPlaceholders. Extra keys → throw. Missing keys → throw.

.OUTPUTS
The rendered template string (still un-canonicalized). Caller is
responsible for the canonicalize stage.
#>
function Invoke-PlaceholderSubstitution {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [string]$Template,

        [Parameter(Mandatory)]
        [object]$PerRow,

        [Parameter(Mandatory)]
        [object]$PerGateway
    )

    # ── 1. Validate placeholder dictionaries against the registry ────────
    Assert-PlaceholderDict -Name 'PerRow' -Provided $PerRow -Expected $script:PerRowPlaceholders
    Assert-PlaceholderDict -Name 'PerGateway' -Provided $PerGateway -Expected $script:PerGatewayPlaceholders

    # ── 2. Anti-templating-engine scope guards ──────────────────────────
    # Per-row placeholder OUTSIDE Sources[]   → forbidden.
    # Per-gateway placeholder INSIDE Sources[] → forbidden.
    # Implementation: split the template at the Sources[] boundary and
    # scan each side. This is approximate (depends on the template being
    # well-formed JSON with `"Sources":` as the unambiguous marker) but
    # it's the cheapest scope check that catches real misuse.
    Assert-PlaceholderScopes -Template $Template

    # ── 3. Pure literal substitution ─────────────────────────────────────
    $rendered = $Template
    foreach ($key in $script:PerRowPlaceholders) {
        $marker = "{{ $key }}"
        $value  = [string]$PerRow[$key]
        $rendered = $rendered.Replace($marker, $value)
    }
    foreach ($key in $script:PerGatewayPlaceholders) {
        $marker = "{{ $key }}"
        $value  = [string]$PerGateway[$key]
        $rendered = $rendered.Replace($marker, $value)
    }

    # ── 4. No unresolved markers left ────────────────────────────────────
    $unresolved = [regex]::Matches($rendered, '\{\{\s*[A-Za-z_][A-Za-z0-9_]*\s*\}\}')
    if ($unresolved.Count -gt 0) {
        $names = ($unresolved | ForEach-Object { $_.Value } | Select-Object -Unique) -join ', '
        throw "$($script:SubErrCodes.UnresolvedPlaceholder): unresolved placeholder(s) after substitution: $names. Either add them to the registry in Substitute-Placeholders.ps1 + MANIFEST.md, or remove them from the template."
    }

    # ── 5. Result must parse as JSON ─────────────────────────────────────
    try {
        [System.Text.Json.JsonDocument]::Parse($rendered).Dispose() | Out-Null
    } catch {
        throw "$($script:SubErrCodes.RenderedJsonInvalid): rendered output is not valid JSON for deviceId '$($PerRow.deviceId)': $($_.Exception.Message). Most likely cause: a placeholder value contains an unescaped quote / brace."
    }

    return $rendered
}

<#
.SYNOPSIS
Replace the template's _provisioning marker with the real 9-field block.

.DESCRIPTION
Templates ship with a placeholder `_provisioning` object containing a
`_TEMPLATE_NOTE` marker. This function locates that object literally and
replaces it with the generated provisioning block JSON. Keeps the
substitution boundary visible (one explicit replacement, not a
ConvertFrom-Json → mutate → ConvertTo-Json roundtrip that would lose
the canonicalization deferred to the next stage).

.PARAMETER Json
The rendered (post-substitution) JSON string.

.PARAMETER Block
OrderedDict of provisioning fields.
#>
function Set-ProvisioningBlock {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [string]$Json,

        [Parameter(Mandatory)]
        [object]$Block
    )

    # Emit a compact JSON form of the block; canonicalize stage will
    # reshape it into the final pretty-printed sorted form.
    $blockJson = $Block | ConvertTo-Json -Compress -Depth 10

    # The template's marker spans from `"_provisioning":` to the close
    # brace of that object. Regex match against the template literal --
    # the template ships with a known shape.
    $pattern = '"_provisioning"\s*:\s*\{\s*"_TEMPLATE_NOTE"\s*:\s*"[^"]*"\s*\}'
    $replacement = "`"_provisioning`": $blockJson"
    $count = ([regex]::Matches($Json, $pattern)).Count
    if ($count -ne 1) {
        throw "$($script:SubErrCodes.ProvisioningMarkerShapeMismatch): expected exactly 1 _provisioning template marker, found $count. The template may have been edited away from the canonical shape (see templates/MANIFEST.md)."
    }
    # Per v4 §3.F3: callback-based [regex]::Replace bypasses $1/$$/$& back-
    # reference interpretation entirely. The previous Regex::Escape +
    # inverse-unescape chain happened to work but would have silently
    # corrupted any replacement string containing a literal `$` -- e.g. a
    # CSV with deviceName containing a price marker.
    return [regex]::Replace($Json, $pattern, { param($m) $replacement })
}

# ── Internal guards ──────────────────────────────────────────────────────

function Assert-PlaceholderDict {
    param(
        [string]$Name,
        [object]$Provided,
        [string[]]$Expected
    )
    # Coerce keys to strings explicitly -- $Provided is typed `object` so
    # PowerShell hands back $Provided.Keys as a loosely-typed collection.
    # Earlier versions used [HashSet[string]]::new($keys, [StringComparer]::Ordinal)
    # but that two-arg constructor fails to resolve under strict-mode
    # because the keys array binds as object[], not string[].
    $providedKeys = @($Provided.Keys | ForEach-Object { [string]$_ })

    $missing = @($Expected     | Where-Object { $_ -notin $providedKeys })
    $extra   = @($providedKeys | Where-Object { $_ -notin $Expected })

    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        $msg = "$($script:SubErrCodes.PlaceholderRegistryMismatch): $Name placeholder dictionary mismatch."
        if ($missing.Count -gt 0) { $msg += " Missing: $($missing -join ', ')." }
        if ($extra.Count -gt 0)   { $msg += " Unknown: $($extra -join ', '). Add to the registry only if you also updated MANIFEST.md." }
        throw $msg
    }
}

function Assert-PlaceholderScopes {
    param([string]$Template)
    # Anti-templating-engine boundary -- but only one direction.
    #
    # Per-gateway placeholders MUST NOT appear inside Sources[]. That
    # rule catches a real misuse: per-gateway values would otherwise
    # vary per source instance, defeating the "fleet-wide" semantics
    # the operator expects from the sidecar.
    #
    # Per-row placeholders MAY appear anywhere in the file. The
    # original chip-3 v2 spec wrote a symmetric guard ("per-row only
    # inside Sources[]"), but the locked template designs in MANIFEST.md
    # reference per-row {{ instanceId }} in Routes[].SourceInstanceId
    # for the legitimate cross-reference between a Source and its Route
    # within the same generated file. Since the generator writes one
    # gateway.json per CSV row, every placeholder substitution happens
    # within a single device's context -- per-row data is the file's
    # natural scope.
    #
    # See docs/sessions/2026-06-13-chip3-impl-session2-plan-v4-lock-final.md
    # §3 for the relaxation rationale captured during the pwsh-7
    # foundation smoke.
    $sourcesIdx = $Template.IndexOf('"Sources"')
    if ($sourcesIdx -lt 0) {
        # Template has no Sources block -- scope guard not applicable.
        return
    }
    $sinksIdx  = $Template.IndexOf('"Sinks"')
    $routesIdx = $Template.IndexOf('"Routes"')

    $endCandidates = @($sinksIdx, $routesIdx) | Where-Object { $_ -gt $sourcesIdx }
    if ($endCandidates.Count -eq 0) {
        # Sources is the last root -- scope guard not applicable (rare).
        return
    }
    $sourcesEnd = ($endCandidates | Measure-Object -Minimum).Minimum
    $insideSources = $Template.Substring($sourcesIdx, $sourcesEnd - $sourcesIdx)

    foreach ($key in $script:PerGatewayPlaceholders) {
        $marker = "{{ $key }}"
        if ($insideSources.Contains($marker)) {
            throw "$($script:SubErrCodes.PlaceholderScopeViolation): per-gateway placeholder '$marker' appears INSIDE Sources[]. Per-gateway placeholders substitute fleet-wide fields; if you need per-row variation move the value to the CSV and add a per-row placeholder."
        }
    }
}

# NOTE: no Export-ModuleMember. This file is dot-sourced from generate.ps1
# and from the Pester tests, which exposes every function to the caller's
# scope automatically. Export-ModuleMember only works inside a .psm1
# module / Import-Module context; calling it from a dot-sourced script
# throws at runtime.
