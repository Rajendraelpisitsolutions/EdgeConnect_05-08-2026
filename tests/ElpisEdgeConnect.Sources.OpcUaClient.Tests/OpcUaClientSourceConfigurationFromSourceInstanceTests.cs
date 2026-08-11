// ============================================================================
// Tests: OpcUaClientSourceConfiguration.FromSourceInstance — pin the PR 7c-3
//        wire-shape contract. The host's OpcUaClientRegistrationExtensions
//        uses this method to translate gateway.json's protocol-agnostic
//        SourceInstanceConfig into the typed configuration that the
//        adapter consumes.
//
//        Invariants pinned (per PR 7c-3 plan, user lock 2026-05-29):
//
//          1. Wrong ProtocolName throws ArgumentException — defensive
//             against dispatcher routing bugs
//          2. Missing Connection block throws ArgumentException —
//             gateway.json schema validation should catch this earlier,
//             but FromSourceInstance must throw cleanly if it slips through
//          3. Missing endpointUrl throws ArgumentException — the only
//             truly required field on the Connection object
//          4. All other tuning fields fall back to v2.1 §2.5 locked
//             defaults when omitted (no operator-facing surprise)
//          5. Enum fields parse case-insensitively (operators hand-edit
//             gateway.json with "None" / "NONE" / "none" interchangeably)
//          6. Credentials block parses when present; null when absent
//          7. MonitoredItems array parses into typed records with all
//             nullable per-item overrides (sampling / queueSize / deadband)
// Reference: PR 7c-3 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaClientSourceConfigurationFromSourceInstanceTests
{
    // ─── Required fields ──────────────────────────────────────────────

    [Fact]
    public void FromSourceInstance_WrongProtocolName_ThrowsArgumentException()
    {
        var instance = MakeInstance(protocolName: "modbustcp", connectionJson: """{ "endpointUrl": "opc.tcp://h:4840" }""");

        var act = () => OpcUaClientSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Expected protocolName 'opcua-client'*modbustcp*");
    }

    [Fact]
    public void FromSourceInstance_MissingConnection_ThrowsArgumentException()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "test",
            ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
            DeviceId = "dev",
            Enabled = true,
            // Connection intentionally null
        };

        var act = () => OpcUaClientSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*missing the required OPC UA Client Connection*");
    }

    [Fact]
    public void FromSourceInstance_MissingEndpointUrl_ThrowsArgumentException()
    {
        var instance = MakeInstance(connectionJson: """{ "applicationUri": "urn:test" }""");

        var act = () => OpcUaClientSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*missing the required 'endpointUrl' field*");
    }

    // ─── Happy path with minimal config ────────────────────────────────

    [Fact]
    public void FromSourceInstance_MinimalConfig_AppliesAllLockedDefaults()
    {
        var instance = MakeInstance(connectionJson: """
            { "endpointUrl": "opc.tcp://server.local:4840" }
            """);

        var config = OpcUaClientSourceConfiguration.FromSourceInstance(instance);

        config.EndpointUrl.Should().Be("opc.tcp://server.local:4840");
        config.ApplicationName.Should().Be("Elpis EdgeConnect");
        config.ApplicationUri.Should().Be("urn:elpis:edgeconnect:opcua-client");
        config.SecurityMode.Should().Be(OpcUaSecurityMode.SignAndEncrypt);
        config.AuthMode.Should().Be(OpcUaAuthMode.Anonymous);
        // v2.1 §2.5 locked tuning defaults
        config.PublishingIntervalMs.Should().Be(50);
        config.SamplingIntervalMs.Should().Be(50);
        config.KeepAliveCount.Should().Be(20u);
        config.LifetimeCount.Should().Be(60u);
        config.MaxNotificationsPerPublish.Should().Be(1_000u);
        config.NotificationChannelCapacity.Should().Be(
            OpcUaClientSourceConfiguration.DefaultNotificationChannelCapacity);
        config.MonitoredItems.Should().BeEmpty();
        config.Credentials.Should().BeNull();
    }

    // ─── Enum case-insensitivity ───────────────────────────────────────

    [Theory]
    [InlineData("None", OpcUaSecurityMode.None)]
    [InlineData("none", OpcUaSecurityMode.None)]
    [InlineData("NONE", OpcUaSecurityMode.None)]
    [InlineData("Sign", OpcUaSecurityMode.Sign)]
    [InlineData("SignAndEncrypt", OpcUaSecurityMode.SignAndEncrypt)]
    public void FromSourceInstance_SecurityMode_ParsesCaseInsensitively(string raw, OpcUaSecurityMode expected)
    {
        var instance = MakeInstance(connectionJson: $$"""
            { "endpointUrl": "opc.tcp://h:4840", "securityMode": "{{raw}}" }
            """);

        OpcUaClientSourceConfiguration.FromSourceInstance(instance).SecurityMode.Should().Be(expected);
    }

    [Theory]
    [InlineData("Anonymous", OpcUaAuthMode.Anonymous)]
    [InlineData("UserName", OpcUaAuthMode.UserName)]
    [InlineData("username", OpcUaAuthMode.UserName)]
    [InlineData("Certificate", OpcUaAuthMode.Certificate)]
    public void FromSourceInstance_AuthMode_ParsesCaseInsensitively(string raw, OpcUaAuthMode expected)
    {
        var instance = MakeInstance(connectionJson: $$"""
            { "endpointUrl": "opc.tcp://h:4840", "authMode": "{{raw}}" }
            """);

        OpcUaClientSourceConfiguration.FromSourceInstance(instance).AuthMode.Should().Be(expected);
    }

    [Fact]
    public void FromSourceInstance_UnknownEnumValue_FallsBackToDefault()
    {
        var instance = MakeInstance(connectionJson: """
            { "endpointUrl": "opc.tcp://h:4840", "securityMode": "GarbageMode" }
            """);

        // Defensive — unknown mode falls back to the locked default
        // rather than throwing; gateway.json schema validation catches
        // this earlier in practice.
        OpcUaClientSourceConfiguration.FromSourceInstance(instance).SecurityMode
            .Should().Be(OpcUaSecurityMode.SignAndEncrypt);
    }

    // ─── Credentials ──────────────────────────────────────────────────

    [Fact]
    public void FromSourceInstance_CredentialsObject_ParsesAllFields()
    {
        var instance = MakeInstance(connectionJson: """
            {
                "endpointUrl": "opc.tcp://h:4840",
                "authMode": "UserName",
                "credentials": {
                    "username": "factorytalk",
                    "password": "secret",
                    "certificatePath": "/etc/edgeconnect/client.pfx"
                }
            }
            """);

        var config = OpcUaClientSourceConfiguration.FromSourceInstance(instance);

        config.Credentials.Should().NotBeNull();
        config.Credentials!.Username.Should().Be("factorytalk");
        config.Credentials.Password.Should().Be("secret");
        config.Credentials.CertificatePath.Should().Be("/etc/edgeconnect/client.pfx");
    }

    // ─── Monitored items ──────────────────────────────────────────────

    [Fact]
    public void FromSourceInstance_MonitoredItemsArray_ParsesAllItems()
    {
        var instance = MakeInstance(connectionJson: """
            {
                "endpointUrl": "opc.tcp://h:4840",
                "monitoredItems": [
                    { "nodeId": "ns=2;i=1", "displayName": "Counter" },
                    {
                        "nodeId": "ns=2;s=Sine",
                        "displayName": "Sine",
                        "samplingIntervalMs": 100,
                        "queueSize": 5,
                        "deadbandPercent": 2.5
                    }
                ]
            }
            """);

        var config = OpcUaClientSourceConfiguration.FromSourceInstance(instance);

        config.MonitoredItems.Should().HaveCount(2);
        config.MonitoredItems[0].NodeId.Should().Be("ns=2;i=1");
        config.MonitoredItems[0].DisplayName.Should().Be("Counter");
        config.MonitoredItems[0].SamplingIntervalMs.Should().BeNull();
        config.MonitoredItems[0].QueueSize.Should().BeNull();
        config.MonitoredItems[0].DeadbandPercent.Should().BeNull();

        config.MonitoredItems[1].NodeId.Should().Be("ns=2;s=Sine");
        config.MonitoredItems[1].SamplingIntervalMs.Should().Be(100);
        config.MonitoredItems[1].QueueSize.Should().Be(5u);
        config.MonitoredItems[1].DeadbandPercent.Should().Be(2.5);
    }

    [Fact]
    public void FromSourceInstance_MonitoredItemsMissingRequiredFields_SkipsItem()
    {
        // Defensive — items missing nodeId or displayName don't throw;
        // they're skipped so the rest of the config can still load.
        // gateway.json schema validation catches this upstream.
        var instance = MakeInstance(connectionJson: """
            {
                "endpointUrl": "opc.tcp://h:4840",
                "monitoredItems": [
                    { "nodeId": "ns=2;i=1", "displayName": "Counter" },
                    { "nodeId": "ns=2;i=2" },
                    { "displayName": "Orphan" }
                ]
            }
            """);

        var config = OpcUaClientSourceConfiguration.FromSourceInstance(instance);

        config.MonitoredItems.Should().HaveCount(1,
            "items missing nodeId or displayName are silently skipped — upstream schema validation handles the error");
    }

    // ─── Base SourceConfiguration fields ──────────────────────────────

    [Fact]
    public void FromSourceInstance_BaseFields_PropagateFromSourceInstance()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "factorytalk-east",
            ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
            DeviceId = "scada-east",
            Enabled = false,
            Connection = JsonSerializer.Deserialize<JsonElement>(
                """{ "endpointUrl": "opc.tcp://h:4840" }"""),
        };

        var config = OpcUaClientSourceConfiguration.FromSourceInstance(instance);

        config.InstanceId.Should().Be("factorytalk-east");
        config.ProtocolName.Should().Be(OpcUaClientSourceConfiguration.ProtocolNameConstant);
        config.DeviceId.Should().Be("scada-east");
        config.Enabled.Should().BeFalse();
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static SourceInstanceConfig MakeInstance(
        string instanceId = "test-instance",
        string protocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        string connectionJson = """{ "endpointUrl": "opc.tcp://h:4840" }""")
    {
        var conn = JsonSerializer.Deserialize<JsonElement>(connectionJson);
        return new SourceInstanceConfig
        {
            InstanceId = instanceId,
            ProtocolName = protocolName,
            DeviceId = "dev-test",
            Enabled = true,
            Connection = conn,
        };
    }
}
