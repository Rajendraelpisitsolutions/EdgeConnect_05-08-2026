// ============================================================================
// Tests: ConfigRedactionEngine schema-aware overload (M-B B1). Pins world
//        routing (World-2 opaque connection blocks classified by name; World-1
//        typed fields descended structurally), the missing-rules provenance
//        warning (Q-B5), and determinism. B1 is baseline-only: no M1 typed
//        tiers are applied yet (that is B2).
// ============================================================================

using System;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Backup;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class ConfigRedactionEngineSchemaTests
{
    private readonly ConfigRedactionEngine _engine = new();
    private readonly SchemaNode _schema = ConfigSchemaModelBuilder.Build(typeof(GatewayConfiguration));
    private readonly BundleRedactionRulesRegistry _emptyRegistry =
        new(Array.Empty<IBundleRedactionRules>());

    private const string SampleConfig = """
        {
          "gateway": { "gatewayId": "gw-1", "gatewayName": "Plant 1" },
          "sources": [
            {
              "instanceId": "modbus-1",
              "protocolName": "modbustcp",
              "deviceId": "dev-1",
              "connection": { "host": "10.0.0.5", "port": 5020, "password": "p-secret", "privateKey": "PEMDATA" }
            }
          ],
          "sinks": [
            {
              "instanceId": "mqtt-1",
              "protocolName": "mqtt",
              "connection": { "brokerHost": "broker.local", "password": "m-secret" },
              "publishing": { "batchSize": 100, "qos": 1, "apiKey": "extra-secret" }
            }
          ]
        }
        """;

    [Fact]
    public void Redact_OpaqueConnectionSecrets_AreRedacted()
    {
        var result = _engine.Redact(SampleConfig, _schema, _emptyRegistry);

        result.Paths.Should().Contain("sources[0].connection.password");   // MASK
        result.Paths.Should().Contain("sources[0].connection.privateKey"); // STRIP
        result.Paths.Should().Contain("sinks[0].connection.password");     // MASK
        result.RedactedJson.Should().NotContain("p-secret")
            .And.NotContain("m-secret").And.NotContain("PEMDATA");
    }

    [Fact]
    public void Redact_ExtensionDataOverflowSecret_IsRedacted()
    {
        // 'apiKey' is an operator overflow key on PublishingSettings (World 2b),
        // classified by the baseline.
        var result = _engine.Redact(SampleConfig, _schema, _emptyRegistry);

        result.Paths.Should().Contain("sinks[0].publishing.apiKey");
        result.RedactedJson.Should().NotContain("extra-secret");
    }

    [Fact]
    public void Redact_TypedWorld1Fields_AreNotRedacted()
    {
        var result = _engine.Redact(SampleConfig, _schema, _emptyRegistry);

        // Top-level typed identity fields are World 1 — descended, never redacted.
        result.Paths.Should().NotContain(p => p.Contains("gatewayId"));
        result.Paths.Should().NotContain(p => p.Contains("instanceId"));
        result.RedactedJson.Should().Contain("gw-1").And.Contain("modbus-1");
    }

    [Fact]
    public void Redact_MissingProtocolRules_EmitsWarningPerConnectionBlock()
    {
        // No protocol rules registered in B1 → each opaque connection block
        // warns (Q-B5). The two connection blocks warn; publishing overflow does not.
        var result = _engine.Redact(SampleConfig, _schema, _emptyRegistry);

        result.Warnings.Should().HaveCount(2);
        result.Warnings.Select(w => w.Path).Should().BeEquivalentTo(new[]
        {
            "sources[0].connection",
            "sinks[0].connection",
        });
        result.Warnings.Should().OnlyContain(w => w.Message.Contains("Protocol rules unavailable"));
    }

    [Fact]
    public void Redact_SchemaAware_IsDeterministic()
    {
        var first = _engine.Redact(SampleConfig, _schema, _emptyRegistry);
        var second = _engine.Redact(SampleConfig, _schema, _emptyRegistry);

        second.RedactedJson.Should().Be(first.RedactedJson);
        second.Paths.Should().Equal(first.Paths);
        second.Warnings.Should().BeEquivalentTo(first.Warnings);
    }

    [Fact]
    public void Redact_NameOnlyOverload_HasNoWarnings()
    {
        // The M-A name-only path never raises warnings.
        var result = _engine.Redact(SampleConfig);
        result.Warnings.Should().BeEmpty();
    }
}
