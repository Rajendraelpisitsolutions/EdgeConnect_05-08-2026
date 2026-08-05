// ============================================================================
// File: Configuration/SchemaRoundTripTests.cs
// Purpose: Round-trip the blueprint §8.1 sample JSON through BOTH
//          System.Text.Json (the runtime loader) AND a freshly generated
//          NJsonSchema schema (the schema validator). The two enforcement
//          layers must agree on what is and is not a valid configuration.
//
// Why this exists: an earlier independent review found two contract drifts
// between the C# runtime and the SchemaGen output:
//   1. Schemas emitted enums as integers while the runtime accepts strings.
//   2. Schemas set additionalProperties=false while the runtime accepts
//      unknown properties for forward compatibility.
// Both have been fixed in ConfigurationSchemaFactory. This test exists to
// keep them fixed forever — it fails if SchemaGen and the runtime ever
// describe different contracts again.
//
// Reference: REVIEW.md §10 "Docs-to-code mismatch", REVIEW.md §4.1
//            "Does the implementation match the scenario described in the tests?"
// ============================================================================

using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.SchemaValidation;
using FluentAssertions;
using NJsonSchema;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class SchemaRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The §8.1 blueprint sample subset, kept here in lock-step with the
    /// fixture in <see cref="GatewayConfigurationTests"/>. Any addition or
    /// removal here MUST also happen there. Both fixtures are checked
    /// against the blueprint sample, not against each other.
    /// </summary>
    private const string BlueprintSampleJson = """
        {
          "Gateway": {
            "GatewayId": "GW-MENON-001",
            "GatewayName": "Menon Factory Floor",
            "Site": "Menon Mumbai Plant",
            "LicenseFile": "license.json",
            "LogLevel": "Information",
            "DataPath": "data/",
            "ManagementApi": {
              "Enabled": true,
              "Port": 8443,
              "RequireAuth": true,
              "TlsCertPath": "certs/api.pfx"
            },
            "HealthCheckPort": 8080,
            "Watchdog": { "Enabled": true, "RestartOnFailure": true }
          },
          "Sources": [
            {
              "InstanceId": "focas-jyoti17",
              "ProtocolName": "focas2",
              "Enabled": true,
              "DeviceId": "Jyoti17CNC",
              "DeviceName": "Jyoti 17 CNC",
              "Connection": { "IpAddress": "192.168.2.34", "Port": 8193, "TimeoutSeconds": 10 },
              "Polling": { "IntervalMs": 5000, "MaxConsecutiveErrors": 10 },
              "Tags": ["mill", "bay-1", "focas2"]
            }
          ],
          "Sinks": [
            {
              "InstanceId": "mqtt-eremos-main",
              "ProtocolName": "mqtt",
              "Enabled": true,
              "Connection": {
                "BrokerHost": "eremos.example.com",
                "BrokerPort": 8883,
                "ClientId": "edgeconnect-menon-001",
                "UseTls": true
              },
              "Publishing": {
                "TopicPrefix": "eremos/menon/cnc",
                "BatchSize": 100,
                "BatchIntervalMs": 250,
                "QoS": 1
              }
            }
          ],
          "Routes": [
            {
              "RouteId": "jyoti17-to-eremos",
              "Name": "Jyoti 17 to EREMOS",
              "SourceInstanceId": "focas-jyoti17",
              "Filter": { "Include": ["*"] },
              "Transforms": {
                "TagMapping": {
                  "CncState_path1_CNC": "machine.state",
                  "Spindle/Speed": "spindle.speed"
                },
                "Deadband": { "spindle.speed": 0.5, "spindle.load": 1.0 },
                "EnrichmentTags": { "site": "Menon", "line": "Bay-1" }
              },
              "SinkInstanceIds": ["mqtt-eremos-main"],
              "Buffer": { "Mode": "StoreAndForward", "MaxDepth": 100000, "MaxAgeDays": 7 },
              "Delivery": { "Mode": "AtLeastOnce", "MaxRetries": 5, "FanoutParallel": true },
              "Enabled": true
            }
          ]
        }
        """;

    private static JsonSchema GenerateGatewaySchema()
    {
        var generator = ConfigurationSchemaFactory.CreateGenerator();
        return generator.Generate(typeof(GatewayConfiguration));
    }

    [Fact]
    public void RuntimeAcceptsBlueprintSample()
    {
        // Sanity baseline: the runtime layer accepts the §8.1 sample.
        var act = () => JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);

        act.Should().NotThrow();
    }

    [Fact]
    public void GeneratedSchemaAcceptsBlueprintSample()
    {
        // The single most important schema test: the JSON the runtime
        // accepts must validate cleanly against the generated schema.
        // If this fails, SchemaGen and the runtime are documenting two
        // different config formats — exactly the bug the prior independent
        // review caught.
        var schema = GenerateGatewaySchema();

        var errors = schema.Validate(BlueprintSampleJson);

        errors.Should().BeEmpty(
            because: "the runtime accepts the blueprint §8.1 sample, so the generated schema must also accept it. " +
                     "Errors: " + string.Join("; ", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void GeneratedSchemaRejectsExactlyOnceDeliveryMode()
    {
        // Belt-and-braces reinforcement of blueprint §19.7. The C# enum
        // doesn't include ExactlyOnce; the JSON converter rejects it; and
        // now the schema must also reject it. Three layers of defence
        // against accidental reintroduction.
        var schema = GenerateGatewaySchema();

        const string badJson = """
            {
              "Gateway": { "GatewayId": "g", "GatewayName": "g" },
              "Routes": [
                {
                  "RouteId": "r",
                  "Name": "r",
                  "SourceInstanceId": "s",
                  "SinkInstanceIds": ["k"],
                  "Delivery": { "Mode": "ExactlyOnce" }
                }
              ]
            }
            """;

        var errors = schema.Validate(badJson);

        errors.Should().NotBeEmpty(
            because: "ExactlyOnce is banned at the runtime level by blueprint §19.7 and the schema must reject it too.");
    }

    [Fact]
    public void GeneratedSchemaRejectsIntegerEnumValue()
    {
        // The runtime uses string-form enums (JsonStringEnumConverter).
        // A config file written with integer enum values would not parse
        // at runtime, so the schema must also reject it. This pins the
        // string-vs-integer enum representation fix.
        var schema = GenerateGatewaySchema();

        const string badJson = """
            {
              "Gateway": { "GatewayId": "g", "GatewayName": "g" },
              "Routes": [
                {
                  "RouteId": "r",
                  "Name": "r",
                  "SourceInstanceId": "s",
                  "SinkInstanceIds": ["k"],
                  "Buffer": { "Mode": 2 }
                }
              ]
            }
            """;

        var errors = schema.Validate(badJson);

        errors.Should().NotBeEmpty(
            because: "the runtime accepts only string enum values; the schema must match.");
    }

    [Fact]
    public void GeneratedSchemaAllowsForwardCompatibleUnknownProperty()
    {
        // The runtime ignores unknown JSON properties for forward compat
        // (verified in GatewayConfigurationTests.Deserialize_UnknownTopLevelProperty_IsIgnored).
        // The schema must also allow them, otherwise editors and pre-validation
        // tools would raise false errors on configs the runtime accepts.
        var schema = GenerateGatewaySchema();

        const string forwardJson = """
            {
              "Gateway": {
                "GatewayId": "GW-FUTURE-001",
                "GatewayName": "Future Gateway",
                "FutureField": "ignored"
              },
              "FutureSection": { "anything": 42 }
            }
            """;

        var errors = schema.Validate(forwardJson);

        errors.Should().BeEmpty(
            because: "the runtime accepts unknown properties for forward compatibility, " +
                     "and the schema must match. Errors: " + string.Join("; ", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void GeneratedSchemaListsDeliveryModeStringEnumValues()
    {
        // Structural pin on the schema's representation of DeliveryMode.
        // If anyone reverts SchemaGen back to integer-mode enums, the
        // schema would describe DeliveryMode as type=integer with
        // enum=[0,1] and this assertion would fail.
        var schema = GenerateGatewaySchema();

        var deliveryMode = schema.Definitions["DeliveryMode"];

        deliveryMode.Type.Should().Be(JsonObjectType.String);
        deliveryMode.Enumeration.Should().BeEquivalentTo(new[] { "AtMostOnce", "AtLeastOnce" });
    }

    [Fact]
    public void GeneratedSchemaListsBufferModeStringEnumValues()
    {
        var schema = GenerateGatewaySchema();

        var bufferMode = schema.Definitions["BufferMode"];

        bufferMode.Type.Should().Be(JsonObjectType.String);
        bufferMode.Enumeration.Should().BeEquivalentTo(new[] { "None", "InMemory", "StoreAndForward" });
    }

    [Fact]
    public void GeneratedSchemaListsDropPolicyStringEnumValues()
    {
        var schema = GenerateGatewaySchema();

        var dropPolicy = schema.Definitions["DropPolicy"];

        dropPolicy.Type.Should().Be(JsonObjectType.String);
        dropPolicy.Enumeration.Should().BeEquivalentTo(new[] { "DropOldest", "DropNewest", "Block" });
    }
}
