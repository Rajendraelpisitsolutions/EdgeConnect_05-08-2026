// ============================================================================
// Tests: MqttSinkWizardModel.HydrateFromExisting — round-trip fidelity.
//        The canonical contract: HydrateFromExisting(BuildSinkInstance())
//        must reproduce every wizard field that BuildSinkInstance packs.
// Reference: docs/sessions/2026-05-26-m2d3-sink-route-editors-plan-v2.md §3.2
// ============================================================================

using System;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sinks.Mqtt;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class MqttSinkWizardModelEditTests
{
    [Fact]
    public void HydrateFromExisting_RoundTrip_AllFieldsPreserved()
    {
        var original = new MqttSinkWizardModel
        {
            InstanceId = "mqtt-eremos",
            Enabled = false,
            BrokerHost = "mqtt.factory.local",
            BrokerPort = 8883,
            UseTls = true,
            KeepAliveSeconds = 60,
            ClientId = "edge-gw-01",
            Authentication = MqttAuthenticationMode.UsernamePassword,
            Username = "operator",
            Password = "s3cret",
            PublishMode = MqttPublishMode.PerTag,
            BatchTopicTemplate = "custom/{gatewayId}/batch",
            PerTagTopicTemplate = "eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}",
            QosLevel = 0,
            RetainDefault = true,
            ReconnectDelayMs = 3000,
            MaxReconnectDelayMs = 30_000,
            ReconnectMultiplier = 1.5,
        };

        var instance = original.BuildSinkInstance();
        var hydrated = MqttSinkWizardModel.HydrateFromExisting(instance);

        hydrated.InstanceId.Should().Be("mqtt-eremos");
        hydrated.Enabled.Should().BeFalse();
        hydrated.BrokerHost.Should().Be("mqtt.factory.local");
        hydrated.BrokerPort.Should().Be(8883);
        hydrated.UseTls.Should().BeTrue();
        hydrated.KeepAliveSeconds.Should().Be(60);
        hydrated.ClientId.Should().Be("edge-gw-01");
        hydrated.Authentication.Should().Be(MqttAuthenticationMode.UsernamePassword);
        hydrated.Username.Should().Be("operator");
        hydrated.Password.Should().Be("s3cret");
        hydrated.PublishMode.Should().Be(MqttPublishMode.PerTag);
        hydrated.BatchTopicTemplate.Should().Be("custom/{gatewayId}/batch");
        hydrated.PerTagTopicTemplate.Should().Be("eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}");
        hydrated.QosLevel.Should().Be(0);
        hydrated.RetainDefault.Should().BeTrue();
        hydrated.ReconnectDelayMs.Should().Be(3000);
        hydrated.MaxReconnectDelayMs.Should().Be(30_000);
        hydrated.ReconnectMultiplier.Should().Be(1.5);
    }

    [Fact]
    public void HydrateFromExisting_AnonymousAuth_SetsAuthModeNone()
    {
        var original = new MqttSinkWizardModel
        {
            InstanceId = "mqtt-anon",
            BrokerHost = "localhost",
            Authentication = MqttAuthenticationMode.None,
        };
        var instance = original.BuildSinkInstance();

        var hydrated = MqttSinkWizardModel.HydrateFromExisting(instance);

        hydrated.Authentication.Should().Be(MqttAuthenticationMode.None);
        hydrated.Username.Should().BeEmpty();
        hydrated.Password.Should().BeEmpty();
    }

    [Fact]
    public void HydrateFromExisting_WrongProtocol_ThrowsArgumentException()
    {
        var wrongProtocol = new ElpisEdgeConnect.Core.Configuration.SinkInstanceConfig
        {
            InstanceId = "opcua-demo",
            ProtocolName = "opcua-server",
            Enabled = true,
        };

        var act = () => MqttSinkWizardModel.HydrateFromExisting(wrongProtocol);

        act.Should().Throw<ArgumentException>().WithMessage("*mqtt*");
    }

    [Fact]
    public void HydrateFromExisting_EmptyClientId_RoundTripsAsEmpty()
    {
        var original = new MqttSinkWizardModel
        {
            InstanceId = "mqtt-noid",
            BrokerHost = "localhost",
            ClientId = "",  // optional — not packed
        };
        var instance = original.BuildSinkInstance();

        var hydrated = MqttSinkWizardModel.HydrateFromExisting(instance);

        hydrated.ClientId.Should().BeEmpty();
    }
}
