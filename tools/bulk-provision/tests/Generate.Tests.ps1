#requires -Version 7.0
# ============================================================================
# tests/Generate.Tests.ps1
#
# Asserts generate.ps1's CSV validation, happy-path, _provisioning block
# shape, and path-serialization-uses-/ contract.
#
# Per v4 §6 D4.
# ============================================================================

BeforeAll {
    $script:BulkProvisionRoot = Resolve-Path "$PSScriptRoot/.."
    $script:GenerateScript    = Join-Path $script:BulkProvisionRoot 'generate.ps1'
    $script:SamplesDir        = Join-Path $script:BulkProvisionRoot 'samples'
    $script:TmpOutBase        = Join-Path ([System.IO.Path]::GetTempPath()) "gen-test-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Path $script:TmpOutBase | Out-Null

    function New-TempCsv {
        param([string]$Content)
        $p = Join-Path $script:TmpOutBase "csv-$([guid]::NewGuid()).csv"
        [System.IO.File]::WriteAllBytes($p, [System.Text.UTF8Encoding]::new($false).GetBytes($Content))
        return $p
    }
}

AfterAll {
    Remove-Item -LiteralPath $script:TmpOutBase -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'generate.ps1 — CSV validation' {

    BeforeEach { Push-Location $script:BulkProvisionRoot }
    AfterEach  { Pop-Location }

    It 'rejects a CSV with a duplicate deviceId' {
        $csv = New-TempCsv -Content "deviceId,deviceName,host,enabled`ncnc-001,A,1.1.1.1,true`ncnc-001,B,2.2.2.2,true`n"
        $sidecar = Join-Path $script:SamplesDir 'sample-fanuc.gateway.yml'
        $outDir  = Join-Path $script:TmpOutBase 'dup-deviceid'

        $action = {
            & $script:GenerateScript -Csv $csv -Sidecar $sidecar -Template template-fanuc -OutDir $outDir -SkipValidate
        }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.CsvDuplicateDeviceId*'
        $action | Should -Throw -ExpectedMessage '*cnc-001*'
    }

    It 'rejects a CSV missing the deviceName column' {
        $csv = New-TempCsv -Content "deviceId,host,enabled`ncnc-001,1.1.1.1,true`n"
        $sidecar = Join-Path $script:SamplesDir 'sample-fanuc.gateway.yml'
        $outDir  = Join-Path $script:TmpOutBase 'missing-col'

        $action = {
            & $script:GenerateScript -Csv $csv -Sidecar $sidecar -Template template-fanuc -OutDir $outDir -SkipValidate
        }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.CsvMissingColumn*'
        $action | Should -Throw -ExpectedMessage '*deviceName*'
    }

    It 'rejects an empty CSV (header row only, no devices)' {
        $csv = New-TempCsv -Content "deviceId,deviceName,host,enabled`n"
        $sidecar = Join-Path $script:SamplesDir 'sample-fanuc.gateway.yml'
        $outDir  = Join-Path $script:TmpOutBase 'empty-csv'

        $action = {
            & $script:GenerateScript -Csv $csv -Sidecar $sidecar -Template template-fanuc -OutDir $outDir -SkipValidate
        }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.CsvEmpty*'
    }
}

Describe 'generate.ps1 — happy path against sample-fanuc' {

    BeforeAll {
        Push-Location $script:BulkProvisionRoot
        $script:HappyOutDir = Join-Path $script:TmpOutBase 'happy-fanuc'
        & $script:GenerateScript `
            -Csv (Join-Path $script:SamplesDir 'sample-fanuc.csv') `
            -Sidecar (Join-Path $script:SamplesDir 'sample-fanuc.gateway.yml') `
            -Template template-fanuc `
            -OutDir $script:HappyOutDir `
            -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
            -GeneratedAt 2026-01-01T00:00:00Z `
            -SkipValidate
        Pop-Location
    }

    It 'produces 3 gateway.json files + run-summary.json + MANIFEST.txt' {
        (Get-ChildItem -LiteralPath $script:HappyOutDir -Filter '*.gateway.json').Count | Should -Be 3
        Test-Path -LiteralPath (Join-Path $script:HappyOutDir 'run-summary.json') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $script:HappyOutDir 'MANIFEST.txt')      | Should -BeTrue
    }

    It 'populates the 9-field _provisioning block in every output' {
        foreach ($f in Get-ChildItem -LiteralPath $script:HappyOutDir -Filter '*.gateway.json') {
            $obj = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $obj._provisioning | Should -Not -BeNullOrEmpty
            $expected = @('configFingerprint','csvFingerprint','fleetId','gatewayProvisioningId','generatedAt','generatedBy','generatorVersion','templateId','templateSchemaVersion')
            $actual = @($obj._provisioning.PSObject.Properties.Name | Sort-Object)
            $actual | Should -Be $expected
        }
    }

    It 'serializes run-summary.json csv/sidecar paths with forward-slash and no absolute drive letter' {
        $summary = Get-Content -LiteralPath (Join-Path $script:HappyOutDir 'run-summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $summary.csv     | Should -Not -Match '\\'
        $summary.sidecar | Should -Not -Match '\\'
        $summary.csv     | Should -Not -Match '^[A-Za-z]:'   # no Windows drive
        $summary.sidecar | Should -Not -Match '^[A-Za-z]:'
        # And they should still look like file paths (not just hashes etc.).
        $summary.csv     | Should -Match '\.csv$'
        $summary.sidecar | Should -Match '\.gateway\.yml$'
    }
}

Describe 'generate.ps1 — sidecar schema integration (v3 A2)' {

    BeforeAll {
        Push-Location $script:BulkProvisionRoot
        $script:BrokenSidecarPath = Join-Path $script:TmpOutBase 'broken-sidecar.gateway.yml'
        # Schema-invalid: gatewayId is not a UUID.
        $brokenYaml = @'
gatewayId: "not-a-uuid"
gatewayName: "edge-acme-broken"
gatewayProvisioningId: "11111111-1111-1111-1111-111111111111"
fleetId: "fleet-acme"
site: "site-a"
mqttHost: "127.0.0.1"
mqttPort: 1883
mqttQos: 1
mqttClientIdPrefix: "edge-acme"
'@
        [System.IO.File]::WriteAllBytes(
            $script:BrokenSidecarPath,
            [System.Text.UTF8Encoding]::new($false).GetBytes($brokenYaml)
        )
        Pop-Location
    }

    BeforeEach { Push-Location $script:BulkProvisionRoot }
    AfterEach  { Pop-Location }

    It 'rejects schema-invalid sidecar BEFORE substitution (BulkProvision.SidecarSchemaViolation)' {
        $outDir = Join-Path $script:TmpOutBase 'sidecar-reject'
        $action = {
            & $script:GenerateScript `
                -Csv (Join-Path $script:SamplesDir 'sample-fanuc.csv') `
                -Sidecar $script:BrokenSidecarPath `
                -Template template-fanuc `
                -OutDir $outDir `
                -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
                -GeneratedAt 2026-01-01T00:00:00Z
            # Note: no -SkipValidate -- sidecar-validate stage MUST run and abort.
        }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.SidecarSchemaViolation*'

        # And substitution must NOT have started: no output files written.
        if (Test-Path -LiteralPath $outDir) {
            (Get-ChildItem -LiteralPath $outDir -Filter '*.gateway.json' -ErrorAction SilentlyContinue).Count | Should -Be 0
        }
    }

    It '-SkipValidate bypasses sidecar schema validation' {
        $outDir = Join-Path $script:TmpOutBase 'sidecar-skip'
        $action = {
            & $script:GenerateScript `
                -Csv (Join-Path $script:SamplesDir 'sample-fanuc.csv') `
                -Sidecar $script:BrokenSidecarPath `
                -Template template-fanuc `
                -OutDir $outDir `
                -GatewayProvisioningId 11111111-1111-1111-1111-111111111111 `
                -GeneratedAt 2026-01-01T00:00:00Z `
                -SkipValidate
        }
        $action | Should -Not -Throw
        # 3 output files produced despite the schema-invalid sidecar.
        (Get-ChildItem -LiteralPath $outDir -Filter '*.gateway.json').Count | Should -Be 3
    }
}
