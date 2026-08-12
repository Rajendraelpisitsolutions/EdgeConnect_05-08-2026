// ============================================================================
// Tests: OpcUaClientCertManagerTests — pins store-path resolution +
//        SecurityConfiguration shape produced by the client-side cert
//        manager.
//
//        Invariants:
//          * DefaultStoreRoot is %LocalApplicationData%/EdgeConnect/
//            OpcUaClient/{InstanceId} — per-instance isolation
//          * Operator override on ApplicationCertificateStorePath wins
//          * BuildSecurityConfiguration produces the four expected store
//            paths (own / trusted / issuers / rejected)
//          * AutoAcceptUntrustedServerCertificate flag flows through
//          * Cert handling defaults (RejectSHA1SignedCertificates=false,
//            MinimumCertificateKeySize=2048) are baked in
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §6 Q7
// ============================================================================

using System.IO;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaClientCertManagerTests
{
    private static OpcUaClientSourceConfiguration BaseConfig() => new()
    {
        InstanceId = "opcua-test",
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "factorytalk",
        EndpointUrl = "opc.tcp://factorytalk.pilot.local:4840",
    };

    [Fact]
    public void DefaultStoreRoot_IsLocalAppDataPerInstance()
    {
        var config = BaseConfig() with { InstanceId = "my-source" };
        var mgr = new OpcUaClientCertManager(config);

        mgr.DefaultStoreRoot.Should().EndWith(
            Path.Combine("EdgeConnect", "OpcUaClient", "my-source"));
    }

    [Fact]
    public void EffectiveStoreRoot_DefaultsToDefaultStoreRoot()
    {
        var mgr = new OpcUaClientCertManager(BaseConfig());

        mgr.EffectiveStoreRoot.Should().Be(mgr.DefaultStoreRoot);
    }

    [Fact]
    public void EffectiveStoreRoot_OperatorOverride_Wins()
    {
        var config = BaseConfig() with { ApplicationCertificateStorePath = "/var/edgeconnect/opcua-client" };
        var mgr = new OpcUaClientCertManager(config);

        mgr.EffectiveStoreRoot.Should().Be("/var/edgeconnect/opcua-client");
    }

    [Fact]
    public void BuildSecurityConfiguration_FourStorePaths_AsExpected()
    {
        var config = BaseConfig() with { ApplicationCertificateStorePath = "/tmp/store" };
        var mgr = new OpcUaClientCertManager(config);

        var sec = mgr.BuildSecurityConfiguration();

        sec.ApplicationCertificate.StorePath.Should().Be(Path.Combine("/tmp/store", "own"));
        sec.TrustedPeerCertificates.StorePath.Should().Be(Path.Combine("/tmp/store", "trusted"));
        sec.TrustedIssuerCertificates.StorePath.Should().Be(Path.Combine("/tmp/store", "issuers"));
        sec.RejectedCertificateStore.StorePath.Should().Be(Path.Combine("/tmp/store", "rejected"));
    }

    [Fact]
    public void BuildSecurityConfiguration_ApplicationCertificate_UsesApplicationNameAsSubject()
    {
        var config = BaseConfig() with { ApplicationName = "MyCustomApp" };
        var mgr = new OpcUaClientCertManager(config);

        var sec = mgr.BuildSecurityConfiguration();

        sec.ApplicationCertificate.SubjectName.Should().Be("MyCustomApp");
    }

    [Fact]
    public void BuildSecurityConfiguration_AutoAcceptFlag_FlowsThrough()
    {
        var configAccept = BaseConfig() with { AutoAcceptUntrustedServerCertificate = true };
        new OpcUaClientCertManager(configAccept).BuildSecurityConfiguration()
            .AutoAcceptUntrustedCertificates.Should().BeTrue();

        var configReject = BaseConfig() with { AutoAcceptUntrustedServerCertificate = false };
        new OpcUaClientCertManager(configReject).BuildSecurityConfiguration()
            .AutoAcceptUntrustedCertificates.Should().BeFalse();
    }

    [Fact]
    public void BuildSecurityConfiguration_LockedDefaults_AreApplied()
    {
        var mgr = new OpcUaClientCertManager(BaseConfig());

        var sec = mgr.BuildSecurityConfiguration();

        sec.RejectSHA1SignedCertificates.Should().BeFalse();
        sec.MinimumCertificateKeySize.Should().Be((ushort)2048);
        sec.AddAppCertToTrustedStore.Should().BeTrue();
    }
}
