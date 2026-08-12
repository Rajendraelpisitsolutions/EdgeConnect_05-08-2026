#requires -Version 7.0
# ============================================================================
# tests/RoundTripValidate.Tests.ps1
#
# Generates output for each of the three templates and validates each
# *.gateway.json against the canonical configuration schema via
# tools/ValidateConfig. Exit code 0 = pass.
#
# Per v4 §6 D6.
#
# DEPENDENCY: requires .NET 8 SDK + a successful build of
# tools/ValidateConfig. The test exercises `dotnet run --project`
# inline, so the SDK presence is a hard prereq.
# ============================================================================

BeforeAll {
    $script:BulkProvisionRoot = Resolve-Path "$PSScriptRoot/.."
    $script:GenerateScript    = Join-Path $script:BulkProvisionRoot 'generate.ps1'
    $script:SamplesDir        = Join-Path $script:BulkProvisionRoot 'samples'
    $script:TmpOutBase        = Join-Path ([System.IO.Path]::GetTempPath()) "rtv-test-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Path $script:TmpOutBase | Out-Null
}

AfterAll {
    Remove-Item -LiteralPath $script:TmpOutBase -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'Round-trip validate — generate then ValidateConfig per template' {

    BeforeEach { Push-Location $script:BulkProvisionRoot }
    AfterEach  { Pop-Location }

    $templateCases = @(
        @{ Template = 'fanuc';     ProvisioningId = '11111111-1111-1111-1111-111111111111' }
        @{ Template = 'brother';   ProvisioningId = '22222222-2222-2222-2222-222222222222' }
        @{ Template = 'modbus';    ProvisioningId = '33333333-3333-3333-3333-333333333333' }
        @{ Template = 'mtconnect'; ProvisioningId = '44444444-4444-4444-4444-444444444444' }
    )

    It 'generates valid output for <Template> (validate stage exit 0)' -ForEach $templateCases {
        $outDir = Join-Path $script:TmpOutBase "rtv-$Template"
        # The generator's validate stage already invokes ValidateConfig per
        # row and throws on non-zero exit. If this succeeds without
        # throwing, the round-trip contract holds for this template.
        $action = {
            & $script:GenerateScript `
                -Csv "./samples/sample-$Template.csv" `
                -Sidecar "./samples/sample-$Template.gateway.yml" `
                -Template "template-$Template" `
                -OutDir $outDir `
                -GatewayProvisioningId $ProvisioningId `
                -GeneratedAt 2026-01-01T00:00:00Z
            # No -SkipValidate -- validate stage MUST run.
        }
        $action | Should -Not -Throw

        # Sanity: 3 outputs landed.
        (Get-ChildItem -LiteralPath $outDir -Filter '*.gateway.json').Count | Should -Be 3
    }
}
