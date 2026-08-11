// ============================================================================
// Tests: OpcUaServerSinkWizardModel.HydrateFromExisting — round-trip fidelity.
// Reference: docs/sessions/2026-05-26-m2d3-sink-route-editors-plan-v2.md §3.2
// ============================================================================

using System;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class OpcUaServerSinkWizardModelEditTests
{
    [Fact]
    public void HydrateFromExisting_RoundTrip_AllFieldsPreserved()
    {
        var original = new OpcUaServerSinkWizardModel
        {
            InstanceId = "opcua-edge",
            Enabled = false,
            EndpointUrl = "opc.tcp://0.0.0.0:4841/custom",
            ApplicationUri = "urn:factory:edgeconnect",
            ApplicationName = "Factory EdgeConnect",
            NamespaceUri = "urn:factory:v2",
            RootFolder = "Factory",
            BrowsePathTemplate = "{deviceClass}/{sourceId}",
            NodeIdTemplate = "ns=2;s={gatewayId}/{stableTagId}",
            SecurityMode = OpcUaSecurityMode.None,
            ApplicationCertificatePath = "",
            MaxSessions = 5,
            MaxMonitoredItemsPerSession = 500,
            MinPublishingIntervalMs = 200,
        };
        original.UserTokenPolicies.Clear();
        original.UserTokenPolicies.Add(OpcUaUserTokenPolicy.Anonymous);

        var instance = original.BuildSinkInstance();
        var hydrated = OpcUaServerSinkWizardModel.HydrateFromExisting(instance);

        hydrated.InstanceId.Should().Be("opcua-edge");
        hydrated.Enabled.Should().BeFalse();
        hydrated.EndpointUrl.Should().Be("opc.tcp://0.0.0.0:4841/custom");
        hydrated.ApplicationUri.Should().Be("urn:factory:edgeconnect");
        hydrated.ApplicationName.Should().Be("Factory EdgeConnect");
        hydrated.NamespaceUri.Should().Be("urn:factory:v2");
        hydrated.RootFolder.Should().Be("Factory");
        hydrated.BrowsePathTemplate.Should().Be("{deviceClass}/{sourceId}");
        hydrated.NodeIdTemplate.Should().Be("ns=2;s={gatewayId}/{stableTagId}");
        hydrated.SecurityMode.Should().Be(OpcUaSecurityMode.None);
        hydrated.MaxSessions.Should().Be(5);
        hydrated.MaxMonitoredItemsPerSession.Should().Be(500);
        hydrated.MinPublishingIntervalMs.Should().Be(200);
        hydrated.UserTokenPolicies.Should().Contain(OpcUaUserTokenPolicy.Anonymous);
    }

    [Fact]
    public void HydrateFromExisting_WrongProtocol_ThrowsArgumentException()
    {
        var wrongProtocol = new ElpisEdgeConnect.Core.Configuration.SinkInstanceConfig
        {
            InstanceId = "mqtt-eremos",
            ProtocolName = "mqtt",
            Enabled = true,
        };

        var act = () => OpcUaServerSinkWizardModel.HydrateFromExisting(wrongProtocol);

        act.Should().Throw<ArgumentException>().WithMessage("*opcua*");
    }

    [Fact]
    public void HydrateFromExisting_CertPath_RoundTrips()
    {
        var original = new OpcUaServerSinkWizardModel
        {
            InstanceId = "opcua-secure",
            EndpointUrl = "opc.tcp://0.0.0.0:4840/edgeconnect",
            ApplicationUri = "urn:elpis:edgeconnect",
            ApplicationName = "Elpis EdgeConnect OPC UA Server",
            ApplicationCertificatePath = "C:/certs/server.pfx",
        };

        var instance = original.BuildSinkInstance();
        var hydrated = OpcUaServerSinkWizardModel.HydrateFromExisting(instance);

        hydrated.ApplicationCertificatePath.Should().Be("C:/certs/server.pfx");
    }
}
