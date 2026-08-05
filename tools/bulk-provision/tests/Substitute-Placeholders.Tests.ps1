#requires -Version 7.0
# ============================================================================
# tests/Substitute-Placeholders.Tests.ps1
#
# Negative + positive guard tests for lib/Substitute-Placeholders.ps1.
# Adversarial replacement test for the F3 regex callback fix.
#
# Per v4 §3.F2: cwd is locked to the bulk-provision root for every test.
# Per v4 §3.F3: adversarial replacement test asserts literal preservation
#               of $, \, $1 in placeholder values.
# Per v4 §6 D2: code prefix + token assertions, NEVER full-message match.
# ============================================================================

BeforeAll {
    $script:BulkProvisionRoot = Resolve-Path "$PSScriptRoot/.."
    . (Join-Path $script:BulkProvisionRoot 'lib/Substitute-Placeholders.ps1')

    $script:ValidPerRow = [ordered]@{
        instanceId = 'cnc-001-source'
        deviceId   = 'cnc-001'
        deviceName = 'Lathe-A1'
        host       = '192.168.10.21'
        enabled    = 'true'
    }

    $script:ValidPerGateway = [ordered]@{
        gatewayId             = '00000000-0000-0000-0000-000000000001'
        gatewayName           = 'edge-acme-site-a'
        gatewayProvisioningId = '11111111-1111-1111-1111-111111111111'
        fleetId               = 'fleet-acme'
        site                  = 'site-a'
        mqttHost              = '127.0.0.1'
        mqttPort              = '1883'
        mqttQos               = '1'
        mqttClientIdPrefix    = 'edge-acme'
    }

    $script:TemplateFanucPath = Join-Path $script:BulkProvisionRoot 'templates/template-fanuc-v1.json'
    $script:TemplateRaw = Get-Content -LiteralPath $script:TemplateFanucPath -Raw -Encoding UTF8
}

Describe 'Invoke-PlaceholderSubstitution — negative guards' {

    BeforeEach {
        Push-Location $script:BulkProvisionRoot
    }
    AfterEach {
        Pop-Location
    }

    It 'rejects a per-gateway placeholder appearing inside Sources[]' {
        # Inject a per-gateway marker into the Sources[].Connection (clearly inside Sources[]).
        $bad = $script:TemplateRaw -replace '"ipAddress": "\{\{ host \}\}"', '"ipAddress": "{{ mqttHost }}"'
        $action = { Invoke-PlaceholderSubstitution -Template $bad -PerRow $script:ValidPerRow -PerGateway $script:ValidPerGateway }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.PlaceholderScopeViolation*'
        $action | Should -Throw -ExpectedMessage '*Sources*'
        $action | Should -Throw -ExpectedMessage '*mqttHost*'
    }

    It 'rejects unresolved markers after substitution' {
        $bad = $script:TemplateRaw + '{{ unknownKey }}'
        $action = { Invoke-PlaceholderSubstitution -Template $bad -PerRow $script:ValidPerRow -PerGateway $script:ValidPerGateway }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.UnresolvedPlaceholder*'
        $action | Should -Throw -ExpectedMessage '*unknownKey*'
    }

    It 'rejects rendered output that is not valid JSON (unescaped quote in deviceName)' {
        $badRow = [ordered]@{
            instanceId = 'cnc-001-source'
            deviceId   = 'cnc-001'
            deviceName = 'Lathe"Broken'   # unescaped quote breaks JSON
            host       = '192.168.10.21'
            enabled    = 'true'
        }
        $action = { Invoke-PlaceholderSubstitution -Template $script:TemplateRaw -PerRow $badRow -PerGateway $script:ValidPerGateway }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.RenderedJsonInvalid*'
        $action | Should -Throw -ExpectedMessage '*cnc-001*'
    }

    It 'rejects a per-row placeholder dictionary with a missing key' {
        $missing = [ordered]@{
            instanceId = 'cnc-001-source'
            deviceId   = 'cnc-001'
            # deviceName MISSING
            host       = '192.168.10.21'
            enabled    = 'true'
        }
        $action = { Invoke-PlaceholderSubstitution -Template $script:TemplateRaw -PerRow $missing -PerGateway $script:ValidPerGateway }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.PlaceholderRegistryMismatch*'
        $action | Should -Throw -ExpectedMessage '*deviceName*'
    }

    It 'rejects a per-gateway dictionary with an extra unknown key' {
        # OrderedDictionary has no .Clone() method; copy by re-emitting an
        # [ordered] hashtable with the same key/value pairs plus the
        # adversarial extra field.
        $extra = [ordered]@{}
        foreach ($k in $script:ValidPerGateway.Keys) { $extra[$k] = $script:ValidPerGateway[$k] }
        $extra['unknownExtraField'] = 'oops'
        $action = { Invoke-PlaceholderSubstitution -Template $script:TemplateRaw -PerRow $script:ValidPerRow -PerGateway $extra }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.PlaceholderRegistryMismatch*'
        $action | Should -Throw -ExpectedMessage '*unknownExtraField*'
    }
}

Describe 'Invoke-PlaceholderSubstitution — positive guards' {

    BeforeEach { Push-Location $script:BulkProvisionRoot }
    AfterEach  { Pop-Location }

    It 'allows a per-row placeholder OUTSIDE Sources[] (e.g. instanceId in Routes.SourceInstanceId)' {
        # The canonical templates use {{ instanceId }} in Routes[].SourceInstanceId
        # for cross-reference within a single generated file. Since each output
        # file is per-row, this is the legitimate use case. The Fanuc template
        # already exercises this -- if substitution succeeds against the
        # untouched template, the scope guard is correctly permissive.
        $action = { Invoke-PlaceholderSubstitution -Template $script:TemplateRaw -PerRow $script:ValidPerRow -PerGateway $script:ValidPerGateway }
        $action | Should -Not -Throw
    }

    It 'succeeds with all placeholders correctly scoped (canonical Fanuc template)' {
        $rendered = Invoke-PlaceholderSubstitution -Template $script:TemplateRaw -PerRow $script:ValidPerRow -PerGateway $script:ValidPerGateway
        $rendered | Should -Not -BeNullOrEmpty

        # The result must parse as JSON (substitution succeeded + no markers
        # left + no syntax breakage from rendered values).
        # `_provisioning` block is still the template's placeholder shape here;
        # Set-ProvisioningBlock runs in the next pipeline stage.
        { [System.Text.Json.JsonDocument]::Parse($rendered).Dispose() } | Should -Not -Throw

        # No `{{ marker }}` left.
        $rendered | Should -Not -Match '\{\{\s*[A-Za-z_]'
    }

    It 'substitutes per-row values inside Sources[] and per-gateway values outside' {
        $rendered = Invoke-PlaceholderSubstitution -Template $script:TemplateRaw -PerRow $script:ValidPerRow -PerGateway $script:ValidPerGateway

        # Per-row value reaches Sources[] payload.
        $rendered | Should -Match '"DeviceId": "cnc-001"'
        $rendered | Should -Match '"ipAddress": "192.168.10.21"'

        # Per-gateway value reaches Gateway + Sinks payload.
        $rendered | Should -Match '"GatewayId": "00000000-0000-0000-0000-000000000001"'
        $rendered | Should -Match '"brokerHost": "127.0.0.1"'
    }
}

Describe 'Set-ProvisioningBlock — F3 regex callback fix' {

    BeforeEach { Push-Location $script:BulkProvisionRoot }
    AfterEach  { Pop-Location }

    It 'preserves literal `$` and `$1` in Set-ProvisioningBlock replacement (adversarial, v4 §3.F3)' {
        # The F3 fix was specifically about Set-ProvisioningBlock's regex
        # callback bypassing $1 / $$ / $& backreference interpretation.
        # Build a Block whose values contain `$` and `$1` and assert the
        # substitution produces them literally in the output JSON. Under
        # the old Regex::Escape + inverse-unescape chain, `$1` would have
        # been interpreted as a regex backreference and corrupted output.
        #
        # Note: backslash adversarial coverage is omitted here because
        # ConvertTo-Json escapes `\` to `\\` before the value reaches
        # Set-ProvisioningBlock, removing the F3 attack surface for that
        # character. The `$` / `$1` case is what F3 actually addressed.
        $rendered = $script:TemplateRaw   # raw template still has _provisioning marker
        $adversarialBlock = [ordered]@{
            generatedBy            = 'tools/$pro/$1-generator.ps1'
            generatorVersion       = '0.1.0'
            templateId             = 'template-fanuc'
            templateSchemaVersion  = 1
            fleetId                = 'fleet-acme'
            gatewayProvisioningId  = '11111111-1111-1111-1111-111111111111'
            generatedAt            = '2026-01-01T00:00:00Z'
            csvFingerprint         = 'deadbeef'
            configFingerprint      = ''
        }
        $result = Set-ProvisioningBlock -Json $rendered -Block $adversarialBlock

        # The adversarial substring must appear LITERALLY in the result.
        # If F3 regressed, $1 would have been replaced by the first
        # regex capture group (empty in our pattern) and the output
        # would contain "tools/$pro/-generator.ps1" instead.
        $result | Should -BeLike '*tools/$pro/$1-generator.ps1*'
    }

    It 'throws ProvisioningMarkerShapeMismatch when the _provisioning marker is missing' {
        $renderedWithoutMarker = $script:TemplateRaw -replace '"_provisioning"\s*:\s*\{\s*"_TEMPLATE_NOTE"\s*:\s*"[^"]*"\s*\}', '"_other": {}'
        $action = {
            Set-ProvisioningBlock `
                -Json $renderedWithoutMarker `
                -Block ([ordered]@{ key = 'value' })
        }
        $action | Should -Throw -ExpectedMessage '*BulkProvision.ProvisioningMarkerShapeMismatch*'
        $action | Should -Throw -ExpectedMessage '*0*'  # "found 0"
    }
}
