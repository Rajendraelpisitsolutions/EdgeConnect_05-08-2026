#requires -Version 7.0
# ============================================================================
# tests/Sidecar.Tests.ps1
#
# Pester coverage for tools/ValidateSidecar/ CLI + tools/bulk-provision/
# sidecar-schema.json (v3 §7.D — 12 tests).
#
# Pattern:
#   - BeforeAll runs `dotnet build` ONCE on the ValidateSidecar project.
#   - Each test invokes `dotnet run --no-build --project ...` so the
#     incremental build cache is reused. Per v3 Q3.
#
# Reference: docs/sessions/2026-06-14-chip3-impl-session2.5-plan-v3-lock-final.md
# ============================================================================

BeforeAll {
    $script:BulkProvisionRoot = Resolve-Path "$PSScriptRoot/.."
    $script:ValidateSidecarProject = (Resolve-Path "$script:BulkProvisionRoot/../ValidateSidecar/ValidateSidecar.csproj").Path
    $script:SchemaPath = (Resolve-Path "$script:BulkProvisionRoot/sidecar-schema.json").Path
    $script:SamplesDir = Join-Path $script:BulkProvisionRoot 'samples'
    $script:TmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "sidecar-test-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Path $script:TmpDir | Out-Null

    # Build ValidateSidecar ONCE so the per-test dotnet run --no-build invocations
    # hit the cache. Failure here will fail every test downstream.
    Push-Location $script:BulkProvisionRoot
    try {
        $buildOutput = & dotnet build $script:ValidateSidecarProject --nologo 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "ValidateSidecar build failed (exit $LASTEXITCODE):`n$buildOutput"
        }
    } finally {
        Pop-Location
    }

    function Invoke-ValidateSidecar {
        param(
            [string]$SidecarPath,
            [switch]$Verbose
        )
        Push-Location $script:BulkProvisionRoot
        try {
            $argsList = @('run', '--no-build', '--project', $script:ValidateSidecarProject, '--',
                          '--schema', $script:SchemaPath, '--sidecar', $SidecarPath)
            if ($Verbose) { $argsList += '--verbose' }
            $output = & dotnet @argsList 2>&1
            return [pscustomobject]@{
                ExitCode = $LASTEXITCODE
                Output   = ($output -join "`n")
            }
        } finally {
            Pop-Location
        }
    }

    function New-TempSidecar {
        param([string]$Name, [string]$Content)
        $p = Join-Path $script:TmpDir "$Name"
        [System.IO.File]::WriteAllBytes($p, [System.Text.UTF8Encoding]::new($false).GetBytes($Content))
        return $p
    }

    # Reusable well-formed YAML body that each negative test mutates.
    $script:GoodYaml = @'
gatewayId: "00000000-0000-0000-0000-000000000001"
gatewayName: "edge-acme-site-a"
gatewayProvisioningId: "11111111-1111-1111-1111-111111111111"
fleetId: "fleet-acme"
site: "site-a"
mqttHost: "127.0.0.1"
mqttPort: 1883
mqttQos: 1
mqttClientIdPrefix: "edge-acme"
'@
}

AfterAll {
    Remove-Item -LiteralPath $script:TmpDir -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'ValidateSidecar — schema validation' {

    It 'validates a well-formed YAML sidecar (sample-fanuc.gateway.yml)' {
        $r = Invoke-ValidateSidecar -SidecarPath (Join-Path $script:SamplesDir 'sample-fanuc.gateway.yml')
        $r.ExitCode | Should -Be 0
    }

    It 'validates a well-formed JSON sidecar (A1)' {
        $jsonContent = @'
{
  "gatewayId": "00000000-0000-0000-0000-000000000001",
  "gatewayName": "edge-acme-site-a",
  "gatewayProvisioningId": "11111111-1111-1111-1111-111111111111",
  "fleetId": "fleet-acme",
  "site": "site-a",
  "mqttHost": "127.0.0.1",
  "mqttPort": 1883,
  "mqttQos": 1,
  "mqttClientIdPrefix": "edge-acme"
}
'@
        $p = New-TempSidecar -Name "good-$([guid]::NewGuid()).json" -Content $jsonContent
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 0
    }

    It 'rejects a sidecar missing a required field (gatewayId)' {
        $bad = $script:GoodYaml -replace 'gatewayId:.*\n', ''
        $p = New-TempSidecar -Name "missing-field-$([guid]::NewGuid()).yml" -Content $bad
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 1
        $r.Output  | Should -Match 'gatewayId'
        $r.Output  | Should -Match 'required field is missing'
    }

    It 'rejects a sidecar with an extra unknown field (additionalProperties: false)' {
        # PowerShell here-strings DO NOT include the trailing newline before
        # the closing `'@`, so $script:GoodYaml ends with the last `"edge-acme"`
        # with no LF. We must insert one ourselves before appending the extra
        # key or YamlDotNet sees `"edge-acme"extraThing: oops` as a single
        # malformed scalar and returns exit 3 (parse failure) instead of
        # exit 1 (schema violation). First Pester run on the user's box
        # surfaced exactly that.
        $bad = $script:GoodYaml + "`nextraThing: oops`n"
        $p = New-TempSidecar -Name "extra-$([guid]::NewGuid()).yml" -Content $bad
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 1
        $r.Output  | Should -Match 'extraThing'
    }

    It 'rejects a sidecar with the wrong type on mqttPort (string instead of integer)' {
        $bad = $script:GoodYaml -replace 'mqttPort: 1883', 'mqttPort: "1883"'
        $p = New-TempSidecar -Name "wrong-type-$([guid]::NewGuid()).yml" -Content $bad
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 1
        $r.Output  | Should -Match 'wrong type'
        $r.Output  | Should -Match 'expected integer'
        # Default output MUST NOT leak the raw NJsonSchema kind name.
        $r.Output  | Should -Not -Match 'IntegerExpected'
    }

    It 'rejects an invalid enum value on mqttQos (mqttQos: 3)' {
        $bad = $script:GoodYaml -replace 'mqttQos: 1', 'mqttQos: 3'
        $p = New-TempSidecar -Name "bad-enum-$([guid]::NewGuid()).yml" -Content $bad
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 1
        $r.Output  | Should -Match 'mqttQos'
        $r.Output  | Should -Match 'value is not one of the allowed values'
        $r.Output  | Should -Not -Match 'NotInEnumeration'
    }

    It 'rejects an invalid pattern on mqttClientIdPrefix (spaces not allowed)' {
        $bad = $script:GoodYaml -replace 'mqttClientIdPrefix: "edge-acme"', 'mqttClientIdPrefix: "bad spaces here"'
        $p = New-TempSidecar -Name "bad-pattern-$([guid]::NewGuid()).yml" -Content $bad
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 1
        $r.Output  | Should -Match 'value does not match the required pattern'
    }

    It 'rejects an invalid UUID format on gatewayId' {
        $bad = $script:GoodYaml -replace 'gatewayId: "00000000-0000-0000-0000-000000000001"', 'gatewayId: "not-a-uuid"'
        $p = New-TempSidecar -Name "bad-uuid-$([guid]::NewGuid()).yml" -Content $bad
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 1
        $r.Output  | Should -Match 'gatewayId'
        $r.Output  | Should -Match 'value must be a valid UUID'
    }
}

Describe 'ValidateSidecar — parse failures' {

    It 'returns exit 3 on malformed YAML' {
        # Unbalanced quotes: well-formed enough to start, breaks mid-stream.
        $bad = @'
gatewayId: "00000000-0000-0000-0000-000000000001
gatewayName: "edge-acme-site-a"
'@
        $p = New-TempSidecar -Name "malformed-$([guid]::NewGuid()).yml" -Content $bad
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 3
        $r.Output  | Should -Match 'sidecar parse failed'
        # Operator-friendly: no raw .NET exception stack frames.
        $r.Output  | Should -Not -Match 'at YamlDotNet\.'
    }

    It 'returns exit 3 on malformed JSON' {
        $bad = '{ "gatewayId": "00000000-0000-0000-0000-000000000001", }'
        $p = New-TempSidecar -Name "malformed-$([guid]::NewGuid()).json" -Content $bad
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 3
        $r.Output  | Should -Match 'sidecar parse failed'
        $r.Output  | Should -Match 'invalid JSON'
    }

    It 'returns exit 3 on unsupported extension (.txt)' {
        $p = New-TempSidecar -Name "sidecar-$([guid]::NewGuid()).txt" -Content $script:GoodYaml
        $r = Invoke-ValidateSidecar -SidecarPath $p
        $r.ExitCode | Should -Be 3
        $r.Output  | Should -Match 'unsupported sidecar extension'
        $r.Output  | Should -Match '\.yml/\.yaml/\.json'
    }
}

Describe 'ValidateSidecar — verbose mode' {

    It 'default output suppresses raw NJsonSchema diagnostics; --verbose includes them' {
        $bad = $script:GoodYaml -replace 'gatewayId:.*\n', ''
        $p = New-TempSidecar -Name "verbose-test-$([guid]::NewGuid()).yml" -Content $bad

        $default = Invoke-ValidateSidecar -SidecarPath $p
        $default.ExitCode | Should -Be 1
        # Default output: operator-friendly only, NO raw kind name.
        $default.Output | Should -Not -Match 'PropertyRequired'
        $default.Output | Should -Not -Match '\[raw\]'

        $verbose = Invoke-ValidateSidecar -SidecarPath $p -Verbose
        $verbose.ExitCode | Should -Be 1
        # --verbose output: contains [raw] tag + the kind name + the field name.
        # Per v3 §3 A4 we do NOT assert the exact path string (could be #/gatewayId, gatewayId, etc.).
        $verbose.Output | Should -Match '\[raw\]'
        $verbose.Output | Should -Match 'PropertyRequired'
        $verbose.Output | Should -Match 'gatewayId'
    }
}
