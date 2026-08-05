#requires -Version 7.0
# ============================================================================
# tests/Canonicalize-Json.Tests.ps1
#
# Asserts the canonicalize layer's byte-level guarantees:
#   - UTF-8 NO BOM
#   - LF line endings (no CR)
#   - Root keys in locked order (_provisioning first, then canonical roots,
#     then _-prefixed unknowns, then non-_ unknowns)
#   - Nested object keys alphabetically sorted
#   - Single trailing LF
#   - Array order PRESERVED (sorting is for object keys only)
#
# Per v4 §6 D3.
# ============================================================================

BeforeAll {
    $script:BulkProvisionRoot = Resolve-Path "$PSScriptRoot/.."
    . (Join-Path $script:BulkProvisionRoot 'lib/Canonicalize-Json.ps1')
}

Describe 'ConvertTo-CanonicalJson — byte-level contract' {

    BeforeEach { Push-Location $script:BulkProvisionRoot }
    AfterEach  { Pop-Location }

    It 'orders root keys: _provisioning first, then canonical roots, then unknowns' {
        $input = @'
{
  "Sinks": [],
  "_provisioning": {"a": 1},
  "Routes": [],
  "Gateway": {"name": "g"},
  "Sources": [],
  "_extra": {},
  "zUnknown": {}
}
'@
        $out = ConvertTo-CanonicalJson -Json $input

        # Expected root order: _provisioning, Gateway, Routes, Sinks, Sources,
        # _extra, zUnknown.
        $idxProvisioning = $out.IndexOf('"_provisioning"')
        $idxGateway      = $out.IndexOf('"Gateway"')
        $idxRoutes       = $out.IndexOf('"Routes"')
        $idxSinks        = $out.IndexOf('"Sinks"')
        $idxSources      = $out.IndexOf('"Sources"')
        $idxExtra        = $out.IndexOf('"_extra"')
        $idxUnknown      = $out.IndexOf('"zUnknown"')

        $idxProvisioning | Should -BeLessThan $idxGateway
        $idxGateway      | Should -BeLessThan $idxRoutes
        $idxRoutes       | Should -BeLessThan $idxSinks
        $idxSinks        | Should -BeLessThan $idxSources
        $idxSources      | Should -BeLessThan $idxExtra
        $idxExtra        | Should -BeLessThan $idxUnknown
    }

    It 'sorts nested object keys alphabetically at every depth' {
        $input = '{"Gateway":{"zKey":1,"aKey":2,"middleKey":3}}'
        $out = ConvertTo-CanonicalJson -Json $input

        $idxA = $out.IndexOf('"aKey"')
        $idxM = $out.IndexOf('"middleKey"')
        $idxZ = $out.IndexOf('"zKey"')

        $idxA | Should -BeLessThan $idxM
        $idxM | Should -BeLessThan $idxZ
    }

    It 'PRESERVES array order (does NOT sort array elements)' {
        $input = '{"Sources":["c","a","b"]}'
        $out = ConvertTo-CanonicalJson -Json $input

        $idxC = $out.IndexOf('"c"')
        $idxA = $out.IndexOf('"a"')
        $idxB = $out.IndexOf('"b"')

        # Positional order matches input: c, a, b
        $idxC | Should -BeLessThan $idxA
        $idxA | Should -BeLessThan $idxB
    }

    It 'ends with exactly one LF and no trailing whitespace' {
        $out = ConvertTo-CanonicalJson -Json '{"x":1}'
        $out | Should -Match "`n$"
        $out | Should -Not -Match "`n`n$"
    }
}

Describe 'Write-CanonicalJson — UTF-8 no BOM + LF on disk' {

    BeforeEach {
        Push-Location $script:BulkProvisionRoot
        $script:TmpPath = Join-Path ([System.IO.Path]::GetTempPath()) "canon-test-$([guid]::NewGuid()).json"
    }
    AfterEach {
        Pop-Location
        Remove-Item -LiteralPath $script:TmpPath -Force -ErrorAction SilentlyContinue
    }

    It 'writes UTF-8 with NO byte-order-mark (first 3 bytes are NOT EF BB BF)' {
        $canonical = ConvertTo-CanonicalJson -Json '{"x":1}'
        Write-CanonicalJson -Path $script:TmpPath -Content $canonical
        $bytes = [System.IO.File]::ReadAllBytes($script:TmpPath)
        # First 3 bytes must NOT be the UTF-8 BOM.
        ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) | Should -BeFalse
    }

    It 'writes LF line endings only (no 0x0D bytes anywhere)' {
        $canonical = ConvertTo-CanonicalJson -Json '{"Gateway":{"name":"g","other":"o"}}'
        Write-CanonicalJson -Path $script:TmpPath -Content $canonical
        $bytes = [System.IO.File]::ReadAllBytes($script:TmpPath)
        $bytes | Should -Not -Contain 0x0D
        # Sanity: file is multi-line.
        ($bytes -contains 0x0A) | Should -BeTrue
    }
}
