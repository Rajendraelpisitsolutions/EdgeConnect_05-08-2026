// ============================================================================
// File: Configuration/GatewayConfigurationTests.cs
// Covers: B1 root aggregate. Round-trips a faithful subset of the blueprint
//         §8.1 sample through System.Text.Json and verifies every nested
//         section. This is the single most important test in B1 — it pins
//         the JSON shape against the locked sample.
// ============================================================================

using System;
using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class GatewayConfigurationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// A faithful subset of blueprint §8.1's sample configuration. Covers
    /// every B1 record type at least once.
    /// </summary>
    /// <remarks>
    /// This fixture is the test of record for B1's contract with the locked
    /// §8.1 sample. Adding fields here without first updating the blueprint
    /// is a docs-to-code mismatch (REVIEW.md §10) and must be rejected at
    /// review time. Removing fields that the blueprint sample contains is
    /// the same kind of failure.
    /// </remarks>
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

    [Fact]
    public void Deserialize_BlueprintSample_GatewaySection()
    {
        var config = JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);

        config.Should().NotBeNull();
        config!.Gateway.GatewayId.Should().Be("GW-MENON-001");
        config.Gateway.GatewayName.Should().Be("Menon Factory Floor");
        config.Gateway.Site.Should().Be("Menon Mumbai Plant");
        config.Gateway.LicenseFile.Should().Be("license.json");
        config.Gateway.LogLevel.Should().Be("Information");
        config.Gateway.DataPath.Should().Be("data/");
        config.Gateway.HealthCheckPort.Should().Be(8080);
    }

    [Fact]
    public void Deserialize_BlueprintSample_ManagementApiSubSection()
    {
        var config = JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);

        config!.Gateway.ManagementApi.Enabled.Should().BeTrue();
        config.Gateway.ManagementApi.Port.Should().Be(8443);
        config.Gateway.ManagementApi.RequireAuth.Should().BeTrue();
        config.Gateway.ManagementApi.TlsCertPath.Should().Be("certs/api.pfx");
    }

    [Fact]
    public void Deserialize_BlueprintSample_WatchdogSubSection()
    {
        var config = JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);

        config!.Gateway.Watchdog.Enabled.Should().BeTrue();
        config.Gateway.Watchdog.RestartOnFailure.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_BlueprintSample_SourcesSection()
    {
        var config = JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);

        config!.Sources.Should().HaveCount(1);
        var source = config.Sources[0];
        source.InstanceId.Should().Be("focas-jyoti17");
        source.ProtocolName.Should().Be("focas2");
        source.Enabled.Should().BeTrue();
        source.DeviceId.Should().Be("Jyoti17CNC");
        source.DeviceName.Should().Be("Jyoti 17 CNC");
        source.Polling.IntervalMs.Should().Be(5000);
        source.Polling.MaxConsecutiveErrors.Should().Be(10);
        source.Tags.Should().Equal("mill", "bay-1", "focas2");
        source.Connection.Should().NotBeNull();
    }

    [Fact]
    public void Deserialize_BlueprintSample_SinksSection()
    {
        var config = JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);

        config!.Sinks.Should().HaveCount(1);
        var sink = config.Sinks[0];
        sink.InstanceId.Should().Be("mqtt-eremos-main");
        sink.ProtocolName.Should().Be("mqtt");
        sink.Enabled.Should().BeTrue();
        sink.Publishing.BatchSize.Should().Be(100);
        sink.Publishing.BatchIntervalMs.Should().Be(250);
    }

    [Fact]
    public void Deserialize_BlueprintSample_PublishingExtrasArePreserved()
    {
        // Protocol-specific fields like TopicPrefix and QoS are MQTT-specific
        // and Core does not type them. They must survive deserialization via
        // the [JsonExtensionData] dictionary on PublishingSettings.
        var config = JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);

        var sink = config!.Sinks[0];
        sink.Publishing.Extras.Should().NotBeNull();
        var extras = sink.Publishing.Extras!;
        extras.Should().ContainKey("TopicPrefix");
        extras["TopicPrefix"].GetString().Should().Be("eremos/menon/cnc");
        extras.Should().ContainKey("QoS");
        extras["QoS"].GetInt32().Should().Be(1);
    }

    [Fact]
    public void Deserialize_BlueprintSample_RoutesSection()
    {
        var config = JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);

        config!.Routes.Should().HaveCount(1);
        var route = config.Routes[0];
        route.RouteId.Should().Be("jyoti17-to-eremos");
        route.Name.Should().Be("Jyoti 17 to EREMOS");
        route.SourceInstanceId.Should().Be("focas-jyoti17");
        route.SinkInstanceIds.Should().Equal("mqtt-eremos-main");
        route.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_BlueprintSample_RouteFilterTransformsBufferDelivery()
    {
        var config = JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);
        var route = config!.Routes[0];

        route.Filter.Include.Should().Equal("*");
        route.Filter.Exclude.Should().BeNull();

        route.Transforms.Should().NotBeNull();
        route.Transforms!.TagMapping.Should().NotBeNull();
        route.Transforms.TagMapping!["Spindle/Speed"].Should().Be("spindle.speed");
        route.Transforms.Deadband!["spindle.speed"].Should().Be(0.5);

        route.Buffer.Mode.Should().Be(BufferMode.StoreAndForward);
        route.Buffer.MaxDepth.Should().Be(100_000);
        route.Buffer.MaxAgeDays.Should().Be(7);

        route.Delivery.Mode.Should().Be(DeliveryMode.AtLeastOnce);
        route.Delivery.MaxRetries.Should().Be(5);
        route.Delivery.FanoutParallel.Should().BeTrue();
    }

    [Fact]
    public void Serialize_BlueprintSample_RoundTripsBackToEquivalentConfig()
    {
        var original = JsonSerializer.Deserialize<GatewayConfiguration>(BlueprintSampleJson, JsonOptions);
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<GatewayConfiguration>(json, JsonOptions);

        roundTripped.Should().NotBeNull();
        roundTripped!.Gateway.GatewayId.Should().Be(original!.Gateway.GatewayId);
        roundTripped.Sources.Should().HaveCount(original.Sources.Count);
        roundTripped.Sinks.Should().HaveCount(original.Sinks.Count);
        roundTripped.Routes.Should().HaveCount(original.Routes.Count);
        roundTripped.Routes[0].SinkInstanceIds.Should().Equal(original.Routes[0].SinkInstanceIds);
    }

    [Fact]
    public void Deserialize_MissingGatewayId_Throws()
    {
        // The `required` modifier on GatewayId is enforced by System.Text.Json.
        const string badJson = """
            {
              "Gateway": {
                "GatewayName": "Missing-id Gateway"
              }
            }
            """;

        var act = () => JsonSerializer.Deserialize<GatewayConfiguration>(badJson, JsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_MissingGatewaySection_Throws()
    {
        const string badJson = """
            {
              "Sources": [],
              "Sinks": [],
              "Routes": []
            }
            """;

        var act = () => JsonSerializer.Deserialize<GatewayConfiguration>(badJson, JsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_EmptyConfig_AllowsZeroSourcesSinksRoutes()
    {
        const string minimalJson = """
            {
              "Gateway": {
                "GatewayId": "GW-MIN-001",
                "GatewayName": "Minimal Gateway"
              }
            }
            """;

        var config = JsonSerializer.Deserialize<GatewayConfiguration>(minimalJson, JsonOptions);

        config.Should().NotBeNull();
        config!.Sources.Should().BeEmpty();
        config.Sinks.Should().BeEmpty();
        config.Routes.Should().BeEmpty();
        config.Gateway.LicenseFile.Should().Be("edgelicense.json"); // default
        config.Gateway.LogLevel.Should().Be("Information");      // default
        config.Gateway.HealthCheckPort.Should().Be(8080);        // default
    }

    [Fact]
    public void Deserialize_UnknownTopLevelProperty_IsIgnored()
    {
        // Forward compatibility: future config versions may add fields. Loading
        // an "ahead-of-version" config must not throw — unknown properties are
        // ignored by System.Text.Json by default.
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

        var config = JsonSerializer.Deserialize<GatewayConfiguration>(forwardJson, JsonOptions);

        config.Should().NotBeNull();
        config!.Gateway.GatewayId.Should().Be("GW-FUTURE-001");
    }

    // ─── ADR-0030: reserved underscore-prefix namespace ──────────────────

    [Fact]
    public void UnderscorePrefixedRoot_SurvivesRoundTrip()
    {
        // ADR-0030 contract: `_`-prefixed root keys are reserved metadata
        // (Chip 3 stamps `_provisioning`; future tooling will stamp
        // `_diagnostics`). They must survive Deserialize → Serialize so
        // post-deployment audits can answer "which generator produced this
        // gateway?"
        const string json = """
            {
              "Gateway": { "GatewayId": "GW-PROV-001", "GatewayName": "Provisioned" },
              "_provisioning": {
                "generatorVersion": "v1.0.0",
                "templateId": "fanuc-v1",
                "csvSourceHash": "sha256:abc123",
                "generatedAt": "2026-05-21T10:00:00Z",
                "gatewayProvisioningId": "prov-7c3a-9f1e"
              }
            }
            """;

        var roundOne = JsonSerializer.Deserialize<GatewayConfiguration>(json, JsonOptions);
        roundOne!.ExtensionData.Should().ContainKey("_provisioning",
            "ADR-0030: `_`-prefixed roots land in ExtensionData via [JsonExtensionData]");

        var serialized = JsonSerializer.Serialize(roundOne, JsonOptions);
        serialized.Should().Contain("_provisioning",
            "ADR-0030: re-serializing must emit the captured `_`-prefixed root verbatim");

        var roundTwo = JsonSerializer.Deserialize<GatewayConfiguration>(serialized, JsonOptions);
        roundTwo!.ExtensionData.Should().ContainKey("_provisioning");
        roundTwo.ExtensionData!["_provisioning"].GetProperty("templateId").GetString()
            .Should().Be("fanuc-v1",
                "the contents of `_provisioning` must be byte-equivalent across round trips");
    }

    [Fact]
    public void NonUnderscoreUnknownRoot_CapturedInExtensionData_ForWarningSurface()
    {
        // ADR-0030 contract: non-`_`-prefixed unknown roots are captured in
        // ExtensionData (for forward-compat) but downstream validators
        // SHOULD warn — they likely indicate a canonical-field typo like
        // "Soures" (missing 'c'). Capture-with-warning is the contract;
        // tooling like `tools/ValidateConfig/` flags these to operators.
        const string json = """
            {
              "Gateway": { "GatewayId": "GW-TYPO-001", "GatewayName": "Typo" },
              "Soures": [ {"InstanceId": "looks-like-a-typo"} ]
            }
            """;

        var config = JsonSerializer.Deserialize<GatewayConfiguration>(json, JsonOptions);

        config!.ExtensionData.Should().NotBeNull(
            "ADR-0030: unknown roots — even suspect typos — land in ExtensionData rather than being silently dropped, so the warning surface has data to flag");
        config.ExtensionData.Should().ContainKey("Soures");
        config.Sources.Should().BeEmpty(
            "the suspect key did NOT silently populate Sources — the schema-typed root remained empty, preserving canonical-field typo-protection");
    }
}
