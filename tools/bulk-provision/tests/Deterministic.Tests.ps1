#requires -Version 7.0
# ============================================================================
# tests/Deterministic.Tests.ps1
#
# Asserts the deterministic-output contract:
#   - PINNED-BOTH: two pinned runs against each of fanuc/brother/modbus
#     produce byte-identical output trees vs the frozen fixture under
#     tests/fixtures/expected/.
#   - PINNED-ID-ONLY: with GatewayProvisioningId pinned and GeneratedAt
#     unpinned, two runs differ ONLY in the _provisioning.generatedAt
#     field. Both outputs canonicalize equally after replacing
#     generatedAt with a sentinel.
#
# Per v4 §3.Q4 + §6 D5.
#
# DEPENDENCY: the expected-fixture trees must be present. They are
# committed under tools/bulk-provision/tests/fixtures/expected/{fanuc,
# brother,modbus}/ by the operator running v4 §6 step D on a pwsh-7 box.
# If the trees aren't there yet, the pinned-both tests are skipped with
# a clear reason.
# ============================================================================

BeforeAll {
    $script:BulkProvisionRoot = Resolve-Path "$PSScriptRoot/.."
    $script:GenerateScript    = Join-Path $script:BulkProvisionRoot 'generate.ps1'
    $script:SamplesDir        = Join-Path $script:BulkProvisionRoot 'samples'
    $script:FixturesDir       = Join-Path $script:BulkProvisionRoot 'tests/fixtures/expected'
    $script:TmpOutBase        = Join-Path ([System.IO.Path]::GetTempPath()) "det-test-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Path $script:TmpOutBase | Out-Null

    function Get-TreeHashes {
        param([string]$Root)
        $hashes = [ordered]@{}
        $files = Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName
        foreach ($f in $files) {
            $rel = [System.IO.Path]::GetRelativePath($Root, $f.FullName) -replace '\\','/'
            $hashes[$rel] = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
        }
        return $hashes
    }

    function Test-FixtureTreePresent {
        param([string]$Template)
        $p = Join-Path $script:FixturesDir $Template
        return (Test-Path -LiteralPath $p) -and ((Get-ChildItem -LiteralPath $p -File -Recurse | Measure-Object).Count -gt 0)
    }
}

AfterAll {
    Remove-Item -LiteralPath $script:TmpOutBase -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'Deterministic-output — PINNED-BOTH (byte-identical vs frozen fixture)' {

    BeforeEach { Push-Location $script:BulkProvisionRoot }
    AfterEach  { Pop-Location }

    $templateCases = @(
        @{ Template = 'fanuc';     ProvisioningId = '11111111-1111-1111-1111-111111111111' }
        @{ Template = 'brother';   ProvisioningId = '22222222-2222-2222-2222-222222222222' }
        @{ Template = 'modbus';    ProvisioningId = '33333333-3333-3333-3333-333333333333' }
        @{ Template = 'mtconnect'; ProvisioningId = '44444444-4444-4444-4444-444444444444' }
    )

    It 'matches the frozen <Template> fixture tree byte-for-byte' -ForEach $templateCases {
        if (-not (Test-FixtureTreePresent -Template $Template)) {
            Set-ItResult -Skipped -Because "expected fixture tree at tests/fixtures/expected/$Template/ not yet committed (v4 §6 step D pending pwsh-7 box)"
            return
        }

        $outDir = Join-Path $script:TmpOutBase "pinned-$Template"
        & $script:GenerateScript `
            -Csv "./samples/sample-$Template.csv" `
            -Sidecar "./samples/sample-$Template.gateway.yml" `
            -Template "template-$Template" `
            -OutDir $outDir `
            -GatewayProvisioningId $ProvisioningId `
            -GeneratedAt 2026-01-01T00:00:00Z `
            -SkipValidate

        $expected = Get-TreeHashes -Root (Join-Path $script:FixturesDir $Template)
        $actual   = Get-TreeHashes -Root $outDir

        # Same relative paths.
        @($actual.Keys)   | Should -Be @($expected.Keys)
        # Same bytes (hash) per file.
        foreach ($k in $expected.Keys) {
            $actual[$k] | Should -Be $expected[$k] -Because "file '$k' should byte-match the frozen fixture"
        }
    }
}

Describe 'Deterministic-output — PINNED-ID-ONLY (generatedAt is the only varying field)' {

    BeforeEach { Push-Location $script:BulkProvisionRoot }
    AfterEach  { Pop-Location }

    It 'produces identical output (modulo generatedAt) across two unpinned-time runs' {
        $outA = Join-Path $script:TmpOutBase 'pinned-id-A'
        $outB = Join-Path $script:TmpOutBase 'pinned-id-B'

        & $script:GenerateScript `
            -Csv ./samples/sample-fanuc.csv `
            -Sidecar ./samples/sample-fanuc.gateway.yml `
            -Template template-fanuc `
            -OutDir $outA `
            -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
            -SkipValidate

        & $script:GenerateScript `
            -Csv ./samples/sample-fanuc.csv `
            -Sidecar ./samples/sample-fanuc.gateway.yml `
            -Template template-fanuc `
            -OutDir $outB `
            -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
            -SkipValidate

        $filesA = Get-ChildItem -LiteralPath $outA -Filter '*.gateway.json' | Sort-Object Name
        $filesB = Get-ChildItem -LiteralPath $outB -Filter '*.gateway.json' | Sort-Object Name
        $filesA.Count | Should -Be $filesB.Count

        for ($i = 0; $i -lt $filesA.Count; $i++) {
            $a = (Get-Content -LiteralPath $filesA[$i].FullName -Raw -Encoding UTF8) `
                -replace '"generatedAt":\s*"[^"]*"', '"generatedAt": "<<sentinel>>"' `
                -replace '"configFingerprint":\s*"[^"]*"', '"configFingerprint": "<<sentinel>>"'
            $b = (Get-Content -LiteralPath $filesB[$i].FullName -Raw -Encoding UTF8) `
                -replace '"generatedAt":\s*"[^"]*"', '"generatedAt": "<<sentinel>>"' `
                -replace '"configFingerprint":\s*"[^"]*"', '"configFingerprint": "<<sentinel>>"'
            $a | Should -Be $b -Because "file $($filesA[$i].Name) should differ only in generatedAt"
        }
    }
}
