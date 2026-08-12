// ============================================================================
// Tests: OpcUaClientApplicationConfigurationBuilderTests — pins the
//        ApplicationConfiguration shape produced from
//        OpcUaClientSourceConfiguration.
//
//        Invariants:
//          * ApplicationName / ApplicationUri / ApplicationType flow through
//          * SecurityConfiguration comes from the cert manager
//          * TransportQuotas + ClientConfiguration carry sensible defaults
//          * DefaultSessionTimeout reflects config's SessionTimeoutMs
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5
// ============================================================================

using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Opc.Ua;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaClientApplicationConfigurationBuilderTests
{
    private static OpcUaClientSourceConfiguration BaseConfig() => new()
    {
        InstanceId = "opcua-test",
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "factorytalk",
        EndpointUrl = "opc.tcp://factorytalk.pilot.local:4840",
    };

    private static OpcUaClientApplicationConfigurationBuilder BuilderFor(OpcUaClientSourceConfiguration config) =>
        new(config, new OpcUaClientCertManager(config));

    [Fact]
    public void Build_ApplicationIdentity_FlowsThrough()
    {
        var config = BaseConfig() with
        {
            ApplicationName = "MyCustomApp",
            ApplicationUri = "urn:my:edgeconnect",
        };

        var appConfig = BuilderFor(config).Build();

        appConfig.ApplicationName.Should().Be("MyCustomApp");
        appConfig.ApplicationUri.Should().Be("urn:my:edgeconnect");
        appConfig.ApplicationType.Should().Be(ApplicationType.Client);
    }

    [Fact]
    public void Build_ClientConfigurationSessionTimeout_ReflectsConfig()
    {
        var config = BaseConfig() with { SessionTimeoutMs = 90_000 };

        var appConfig = BuilderFor(config).Build();

        appConfig.ClientConfiguration.Should().NotBeNull();
        appConfig.ClientConfiguration!.DefaultSessionTimeout.Should().Be(90_000);
    }

    [Fact]
    public void Build_TransportQuotas_HaveSensibleDefaults()
    {
        var appConfig = BuilderFor(BaseConfig()).Build();

        appConfig.TransportQuotas.Should().NotBeNull();
        appConfig.TransportQuotas!.OperationTimeout.Should().Be(30_000);
        appConfig.TransportQuotas.MaxMessageSize.Should().Be(4_194_304);
        appConfig.TransportQuotas.ChannelLifetime.Should().Be(300_000);
        appConfig.TransportQuotas.SecurityTokenLifetime.Should().Be(3_600_000);
    }

    [Fact]
    public void Build_SecurityConfiguration_ComesFromCertManager()
    {
        var config = BaseConfig() with
        {
            ApplicationCertificateStorePath = "/tmp/test-store",
            AutoAcceptUntrustedServerCertificate = false,
        };

        var appConfig = BuilderFor(config).Build();

        appConfig.SecurityConfiguration.Should().NotBeNull();
        appConfig.SecurityConfiguration!.AutoAcceptUntrustedCertificates.Should().BeFalse();
        appConfig.SecurityConfiguration.ApplicationCertificate.StorePath
            .Should().Be(System.IO.Path.Combine("/tmp/test-store", "own"),
                "cert manager uses Path.Combine for cross-platform consistency");
    }

    [Fact]
    public void Build_TraceConfiguration_StaysQuiet()
    {
        // OPC stack tracing is very chatty; benchmarks need it disabled
        // to avoid measuring trace I/O. TraceMasks=0 = silent.
        var appConfig = BuilderFor(BaseConfig()).Build();

        appConfig.TraceConfiguration.Should().NotBeNull();
        appConfig.TraceConfiguration!.TraceMasks.Should().Be(0);
    }
}
