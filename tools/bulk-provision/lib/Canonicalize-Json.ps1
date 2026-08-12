# ============================================================================
# File: tools/bulk-provision/lib/Canonicalize-Json.ps1
# Purpose: Canonicalize a JSON string per Chip 3 v2 §5.4.1 spec — UTF-8 no BOM,
#          LF line endings, alphabetical property ordering (with the locked
#          root-key exception for `_provisioning`), 2-space indent, invariant
#          culture, no trailing whitespace, final newline.
#
#          The actual canonicalization logic dispatches into .NET's
#          System.Text.Json (already loaded in PowerShell 7+). Per v2 §5.5.2,
#          rendering/canonicalization logic stays .NET-portable; the PS file
#          is a thin shell that delegates.
#
#          Internal helper used by generate.ps1. Not operator-invokable
#          directly.
#
# Reference: docs/sessions/2026-05-21-chip3-provisioning-subsystem-plan-v2.md §5.4.1
# ============================================================================

Set-StrictMode -Version Latest

# ── Canonical root-key order (Chip 3 v2 §5.4.1) ───────────────────────────
# `_provisioning` first (always — `_` sorts before all alpha in ASCII anyway,
# but we emit it explicitly to make the contract visible to future readers),
# then canonical roots alphabetically. Any unknown `_*` root sorts after
# `_provisioning` alphabetically. Any non-`_` unknown root sorts after the
# canonical roots alphabetically (and gets a suspect-roots warning at the
# validate stage — see tools/ValidateConfig).
$script:CanonicalRootOrder = @(
    '_provisioning',
    'Gateway',
    'Routes',
    'Schemas',
    'Sinks',
    'Sources'
)

<#
.SYNOPSIS
Canonicalize a JSON string per Chip 3 v2 §5.4.1.

.DESCRIPTION
Parses a JSON string, re-emits it with:
- Alphabetical property ordering at every depth (root has a locked exception
  per §5.4.1 — `_provisioning` first, then canonical roots in order).
- 2-space indentation, LF newlines, invariant culture for all numbers.
- No trailing whitespace, final newline at EOF.
- UTF-8 encoding without BOM when written via the companion Write-CanonicalJson
  function.

This is the form that satisfies the deterministic-provisioning guarantee
(§5.4.4) — same inputs in two different generator runs produce byte-identical
output here, modulo only `_provisioning.generatedAt`.

.PARAMETER Json
The JSON string to canonicalize.

.OUTPUTS
The canonicalized JSON string. Always ends with a single LF.

.EXAMPLE
$canonical = ConvertTo-CanonicalJson -Json $generatedJson
$canonical | Set-CanonicalContent -Path .\out\gateway.json
#>
function ConvertTo-CanonicalJson {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory, Position = 0)]
        [string]$Json
    )

    # Parse via System.Text.Json so we get the .NET-canonical representation
    # invariant of PowerShell's stringification quirks (which differ between
    # PS 5.1 and 7.x — exactly the kind of host-OS leakage §5.4.3 forbids).
    Add-Type -AssemblyName System.Text.Json -ErrorAction SilentlyContinue
    $document = [System.Text.Json.JsonDocument]::Parse($Json)
    try {
        $sb = [System.Text.StringBuilder]::new()
        Write-CanonicalElement -Element $document.RootElement -Builder $sb -Depth 0 -IsRoot $true
        $result = $sb.ToString()
        # Single trailing LF; strip any extras (shouldn't happen but defence).
        $result = $result -replace "[\r\n]+$", ''
        return $result + "`n"
    } finally {
        $document.Dispose()
    }
}

<#
.SYNOPSIS
Write a canonical JSON string to a file with UTF-8 no BOM and LF line endings.

.DESCRIPTION
PowerShell's Set-Content has subtle encoding + line-ending behavior that
differs between 5.1 and 7.x. This helper bypasses it entirely by calling
[System.IO.File]::WriteAllBytes with explicit UTF-8 (no BOM) encoding, so
the byte output matches the §5.4.1 contract on every host OS / PS version.

.PARAMETER Path
The output file path.

.PARAMETER Content
The canonical JSON string. Should already end with `\n` (LF) per the
ConvertTo-CanonicalJson contract.
#>
function Write-CanonicalJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [string]$Path,

        [Parameter(Mandatory, Position = 1)]
        [string]$Content
    )

    # UTF8Encoding(false) = no BOM. Forced LF.
    $encoding = [System.Text.UTF8Encoding]::new($false)
    $contentLF = $Content -replace "`r`n", "`n"
    [System.IO.File]::WriteAllBytes($Path, $encoding.GetBytes($contentLF))
}

# ── Internal recursion ─────────────────────────────────────────────────────

function Write-CanonicalElement {
    param(
        [System.Text.Json.JsonElement]$Element,
        [System.Text.StringBuilder]$Builder,
        [int]$Depth,
        [bool]$IsRoot = $false
    )

    switch ($Element.ValueKind) {
        'Object' {
            Write-CanonicalObject -Element $Element -Builder $Builder -Depth $Depth -IsRoot $IsRoot
        }
        'Array' {
            Write-CanonicalArray -Element $Element -Builder $Builder -Depth $Depth
        }
        'String' {
            # JsonElement.GetRawText() returns the JSON-encoded string with
            # all standard escapes already applied. Use that for fidelity
            # — never re-stringify ourselves (locale risk).
            [void]$Builder.Append($Element.GetRawText())
        }
        'Number' {
            # GetRawText preserves the exact source representation; for
            # determinism that's what we want — same input → same output bytes.
            [void]$Builder.Append($Element.GetRawText())
        }
        'True'  { [void]$Builder.Append('true') }
        'False' { [void]$Builder.Append('false') }
        'Null'  { [void]$Builder.Append('null') }
        default {
            throw "ConvertTo-CanonicalJson: unexpected ValueKind '$($Element.ValueKind)'"
        }
    }
}

function Write-CanonicalObject {
    param(
        [System.Text.Json.JsonElement]$Element,
        [System.Text.StringBuilder]$Builder,
        [int]$Depth,
        [bool]$IsRoot
    )

    $properties = @($Element.EnumerateObject())
    if ($properties.Count -eq 0) {
        [void]$Builder.Append('{}')
        return
    }

    # `@(...)` around the whole if-else is load-bearing: PowerShell
    # unwraps single-element arrays returned from a function call. For an
    # object with exactly one property (e.g. `"Polling": {"IntervalMs":
    # 3000}`), Get-SortedRootKeys would return a 1-element string[] that
    # gets flattened to a single string during assignment, and the
    # downstream `$sortedKeys.Count` access throws under strict mode.
    $sortedKeys = @(
        if ($IsRoot) {
            Get-SortedRootKeys -PropertyNames @($properties | ForEach-Object Name)
        } else {
            $properties | ForEach-Object Name | Sort-Object -CaseSensitive
        }
    )

    [void]$Builder.Append('{')
    $indent = '  ' * ($Depth + 1)
    $closeIndent = '  ' * $Depth
    for ($i = 0; $i -lt $sortedKeys.Count; $i++) {
        $key = $sortedKeys[$i]
        $property = $properties | Where-Object { $_.Name -eq $key } | Select-Object -First 1
        [void]$Builder.Append("`n")
        [void]$Builder.Append($indent)
        [void]$Builder.Append((ConvertTo-JsonStringLiteral -Value $key))
        [void]$Builder.Append(': ')
        Write-CanonicalElement -Element $property.Value -Builder $Builder -Depth ($Depth + 1)
        if ($i -lt $sortedKeys.Count - 1) {
            [void]$Builder.Append(',')
        }
    }
    [void]$Builder.Append("`n")
    [void]$Builder.Append($closeIndent)
    [void]$Builder.Append('}')
}

function Write-CanonicalArray {
    param(
        [System.Text.Json.JsonElement]$Element,
        [System.Text.StringBuilder]$Builder,
        [int]$Depth
    )

    $items = @($Element.EnumerateArray())
    if ($items.Count -eq 0) {
        [void]$Builder.Append('[]')
        return
    }

    [void]$Builder.Append('[')
    $indent = '  ' * ($Depth + 1)
    $closeIndent = '  ' * $Depth
    for ($i = 0; $i -lt $items.Count; $i++) {
        [void]$Builder.Append("`n")
        [void]$Builder.Append($indent)
        Write-CanonicalElement -Element $items[$i] -Builder $Builder -Depth ($Depth + 1)
        if ($i -lt $items.Count - 1) {
            [void]$Builder.Append(',')
        }
    }
    [void]$Builder.Append("`n")
    [void]$Builder.Append($closeIndent)
    [void]$Builder.Append(']')
}

function Get-SortedRootKeys {
    [OutputType([string[]])]
    param(
        [string[]]$PropertyNames
    )
    # §5.4.1 root ordering: canonical roots in the locked order first, then
    # any `_`-prefixed unknown roots alphabetically, then any non-`_` unknown
    # roots alphabetically. The non-`_` case is forward-compat; suspect-roots
    # warning fires elsewhere in the pipeline.
    #
    # Implementation uses a plain hashtable instead of HashSet<string> --
    # the two-arg HashSet constructor fails to resolve under strict-mode
    # PowerShell when the input array binds as object[] rather than
    # string[]. The hashtable is plenty fast for the small root-key
    # cardinality (~5-15 entries).
    $present = @{}
    foreach ($name in $PropertyNames) { $present[$name] = $true }
    $ordered = [System.Collections.Generic.List[string]]::new()

    foreach ($canonical in $script:CanonicalRootOrder) {
        if ($present.ContainsKey($canonical)) {
            $ordered.Add($canonical)
            $present.Remove($canonical) | Out-Null
        }
    }

    $underscoreRemainder = @($PropertyNames | Where-Object { $_.StartsWith('_') -and $present.ContainsKey($_) } | Sort-Object -CaseSensitive)
    foreach ($k in $underscoreRemainder) {
        if ($present.ContainsKey($k)) {
            $ordered.Add($k)
            $present.Remove($k) | Out-Null
        }
    }

    $otherRemainder = @($PropertyNames | Where-Object { -not $_.StartsWith('_') -and $present.ContainsKey($_) } | Sort-Object -CaseSensitive)
    foreach ($k in $otherRemainder) {
        if ($present.ContainsKey($k)) {
            $ordered.Add($k)
            $present.Remove($k) | Out-Null
        }
    }

    return $ordered.ToArray()
}

function ConvertTo-JsonStringLiteral {
    [OutputType([string])]
    param([string]$Value)
    # Standard JSON string escape via System.Text.Json's writer.
    $stream = [System.IO.MemoryStream]::new()
    try {
        $writer = [System.Text.Json.Utf8JsonWriter]::new($stream)
        try {
            $writer.WriteStringValue($Value)
            $writer.Flush()
        } finally {
            $writer.Dispose()
        }
        return [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
    } finally {
        $stream.Dispose()
    }
}

# NOTE: no Export-ModuleMember. This file is dot-sourced from generate.ps1
# and from the Pester tests (`. (Join-Path $libRoot 'Canonicalize-Json.ps1')`),
# which exposes every function to the caller's scope automatically.
# Export-ModuleMember only works inside a .psm1 module / Import-Module
# context; calling it from a dot-sourced script throws at runtime.
