// ============================================================================
// Tests: OpcUaServerSinkWizardModel — pins the OPC UA Server sink wizard
//        view-model (M.2b.6). Key invariants:
//
//          * Locked M: consumes canonical OpcUaServerConfiguration
//            (NOT OpcUaServerSinkConfiguration). FromSinkInstance reads
//            every field the wizard writes.
//          * Locked O: full security schema visible — Mode (None / Sign
//            / SignAndEncrypt) is operator-settable. Test confirms each
//            mode roundtrips through the projection.
//          * Defaults match runtime contract (NamespaceUri v1, endpoint
//            on 0.0.0.0:4840, ApplicationUri urn:elpis:edgeconnect).
//          * Eager validation: endpoint URL scheme, ApplicationUri
//            non-empty, namespace fields non-empty, capacity numerics
//            positive, ≥1 token policy.
//          * Empty optional paths (cert + trusted/rejected dirs) are
//            OMITTED from the payload, not serialised as empty strings.
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §1, §5
// ============================================================================

using System;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class OpcUaServerSinkWizardModelTests
{
    // ─── Defaults ────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_MatchRuntimeContract()
    {
        var model = new OpcUaServerSinkWizardModel();

        model.Enabled.Should().BeTrue();
        model.EndpointUrl.Should().Be("opc.tcp://0.0.0.0:4840/edgeconnect");
        model.ApplicationUri.Should().Be("urn:elpis:edgeconnect");
        model.NamespaceUri.Should().Be("urn:elpis:edgeconnect:v1");
        model.RootFolder.Should().Be("EdgeConnect");
        model.SecurityMode.Should().Be(OpcUaSecurityMode.None, "MVP default; Sign/SignAndEncrypt activate in Milestone K");
        model.UserTokenPolicies.Should().Equal(OpcUaUserTokenPolicy.Anonymous);
        model.MaxSessions.Should().Be(10);
        model.MaxMonitoredItemsPerSession.Should().Be(1_000);
        model.MinPublishingIntervalMs.Should().Be(100);
    }

    // ─── Projection ──────────────────────────────────────────────────────

    [Fact]
    public void BuildSinkInstance_HappyPath_ProducesValidConfig()
    {
        var model = MakeMinimallyValidModel();
        model.InstanceId = "opcua-edge";

        var sink = model.BuildSinkInstance();

        sink.InstanceId.Should().Be("opcua-edge");
        sink.ProtocolName.Should().Be(OpcUaServerConfiguration.ProtocolNameConstant);
        sink.Enabled.Should().BeTrue();
    }

    [Fact]
    public void BuildSinkInstance_LockedM_RoundtripsViaCanonicalConfigurationName()
    {
        // Locked M pin: OpcUaServerConfiguration is the canonical record
        // (NOT OpcUaServerSinkConfiguration). FromSinkInstance is the
        // canonical projector. The wizard ↔ adapter contract holds when
        // every wizard field appears on the projection result.
        var model = new OpcUaServerSinkWizardModel
        {
            InstanceId = "opcua-1",
            EndpointUrl = "opc.tcp://10.0.5.42:4843/custom",
            ApplicationUri = "urn:elpis:edgeconnect:gateway-A",
            ApplicationName = "Gateway A",
            NamespaceUri = "urn:elpis:edgeconnect:v1",
            RootFolder = "Plant1",
            BrowsePathTemplate = "{site}/{line}/{tagName}",
            NodeIdTemplate = "ns=2;s={gatewayId}/{stableTagId}",
            SecurityMode = OpcUaSecurityMode.Sign,
            MaxSessions = 25,
            MaxMonitoredItemsPerSession = 2_500,
            MinPublishingIntervalMs = 200,
        };
        model.UserTokenPolicies.Clear();
        model.UserTokenPolicies.Add(OpcUaUserTokenPolicy.Anonymous);
        model.UserTokenPolicies.Add(OpcUaUserTokenPolicy.UserName);

        var instance = model.BuildSinkInstance();
        var cfg = OpcUaServerConfiguration.FromSinkInstance(instance);

        cfg.InstanceId.Should().Be("opcua-1");
        cfg.EndpointUrl.Should().Be("opc.tcp://10.0.5.42:4843/custom");
        cfg.ApplicationUri.Should().Be("urn:elpis:edgeconnect:gateway-A");
        cfg.ApplicationName.Should().Be("Gateway A");
        cfg.Namespace.NamespaceUri.Should().Be("urn:elpis:edgeconnect:v1");
        cfg.Namespace.RootFolder.Should().Be("Plant1");
        cfg.Namespace.BrowsePathTemplate.Should().Be("{site}/{line}/{tagName}");
        cfg.Namespace.NodeIdTemplate.Should().Be("ns=2;s={gatewayId}/{stableTagId}");
        cfg.Security.Mode.Should().Be(OpcUaSecurityMode.Sign);
        cfg.Security.UserTokenPolicies.Should().Contain(OpcUaUserTokenPolicy.Anonymous);
        cfg.Security.UserTokenPolicies.Should().Contain(OpcUaUserTokenPolicy.UserName);
        cfg.MaxSessions.Should().Be(25);
        cfg.MaxMonitoredItemsPerSession.Should().Be(2_500);
        cfg.MinPublishingIntervalMs.Should().Be(200);
    }

    [Theory]
    [InlineData(OpcUaSecurityMode.None)]
    [InlineData(OpcUaSecurityMode.Sign)]
    [InlineData(OpcUaSecurityMode.SignAndEncrypt)]
    public void BuildSinkInstance_AllSecurityModes_Roundtrip(OpcUaSecurityMode mode)
    {
        // Locked O: the wizard accepts the full schema today even though
        // non-None modes are runtime-rejected until Milestone K. Pin all
        // three modes roundtrip cleanly so a customer authoring forward-
        // compat config via the wizard gets the exact shape Core expects.
        var model = MakeMinimallyValidModel();
        model.SecurityMode = mode;

        var instance = model.BuildSinkInstance();
        var cfg = OpcUaServerConfiguration.FromSinkInstance(instance);

        cfg.Security.Mode.Should().Be(mode);
    }

    [Fact]
    public void BuildSinkInstance_EmptyCertPaths_OmitsFromPayload()
    {
        // Defensive: when operator leaves cert paths blank (the wizard's
        // default — auto-generate covers it), the payload must NOT emit
        // empty-string fields that downstream consumers might interpret
        // as "use this path".
        var model = MakeMinimallyValidModel();
        model.ApplicationCertificatePath = "";
        model.TrustedClientsPath = "";
        model.RejectedClientsPath = "";

        var instance = model.BuildSinkInstance();
        var cfg = OpcUaServerConfiguration.FromSinkInstance(instance);

        cfg.Security.ApplicationCertificatePath.Should().BeNull();
        cfg.Security.TrustedClientsPath.Should().BeNull();
        cfg.Security.RejectedClientsPath.Should().BeNull();
    }

    [Fact]
    public void BuildSinkInstance_CustomCertPath_RoundTrips()
    {
        var model = MakeMinimallyValidModel();
        model.ApplicationCertificatePath = @"C:\certs\edgeconnect-server.pfx";

        var instance = model.BuildSinkInstance();
        var cfg = OpcUaServerConfiguration.FromSinkInstance(instance);

        cfg.Security.ApplicationCertificatePath.Should().Be(@"C:\certs\edgeconnect-server.pfx");
    }

    // ─── Eager validation ────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateEndpointUrl_Blank_Rejected(string url)
    {
        var model = new OpcUaServerSinkWizardModel { EndpointUrl = url };
        model.ValidateEndpointUrl().Should().NotBeNull();
    }

    [Theory]
    [InlineData("http://localhost:4840")]
    [InlineData("tcp://localhost:4840")]
    [InlineData("opc.https://localhost:4840")]
    public void ValidateEndpointUrl_WrongScheme_Rejected(string url)
    {
        var model = new OpcUaServerSinkWizardModel { EndpointUrl = url };
        model.ValidateEndpointUrl().Should().NotBeNull()
            .And.Subject.Should().Contain("opc.tcp://");
    }

    [Theory]
    [InlineData("opc.tcp://0.0.0.0:4840/edgeconnect")]
    [InlineData("opc.tcp://10.0.5.42:4843/custom")]
    [InlineData("OPC.TCP://localhost:4840")]  // case-insensitive scheme per spec
    public void ValidateEndpointUrl_ValidScheme_Accepted(string url)
    {
        var model = new OpcUaServerSinkWizardModel { EndpointUrl = url };
        model.ValidateEndpointUrl().Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateApplicationUri_Blank_Rejected(string uri)
    {
        var model = new OpcUaServerSinkWizardModel { ApplicationUri = uri };
        model.ValidateApplicationUri().Should().NotBeNull();
    }

    [Fact]
    public void ValidateNamespace_BlankFields_Rejected()
    {
        var model = MakeMinimallyValidModel();
        model.NamespaceUri = "";
        model.ValidateNamespace().Should().NotBeNull();

        model.NamespaceUri = "urn:x";
        model.BrowsePathTemplate = "";
        model.ValidateNamespace().Should().NotBeNull();

        model.BrowsePathTemplate = "{tagName}";
        model.NodeIdTemplate = "";
        model.ValidateNamespace().Should().NotBeNull();

        model.NodeIdTemplate = "ns=2;s={stableTagId}";
        model.ValidateNamespace().Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateCapacity_NonPositiveMaxSessions_Rejected(int max)
    {
        var model = MakeMinimallyValidModel();
        model.MaxSessions = max;
        model.ValidateCapacity().Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void ValidateCapacity_NonPositivePublishingInterval_Rejected(int ms)
    {
        var model = MakeMinimallyValidModel();
        model.MinPublishingIntervalMs = ms;
        model.ValidateCapacity().Should().NotBeNull();
    }

    [Fact]
    public void ValidateUserTokenPolicies_Empty_Rejected()
    {
        var model = MakeMinimallyValidModel();
        model.UserTokenPolicies.Clear();
        model.ValidateUserTokenPolicies().Should().NotBeNull();
    }

    // ─── Defensive projection guards ─────────────────────────────────────

    [Fact]
    public void BuildSinkInstance_BlankEndpoint_Throws()
    {
        var model = MakeMinimallyValidModel();
        model.EndpointUrl = "";

        var act = () => model.BuildSinkInstance();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildSinkInstance_WrongScheme_Throws()
    {
        var model = MakeMinimallyValidModel();
        model.EndpointUrl = "http://localhost:4840";

        var act = () => model.BuildSinkInstance();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildSinkInstance_EmptyTokenPolicies_Throws()
    {
        var model = MakeMinimallyValidModel();
        model.UserTokenPolicies.Clear();

        var act = () => model.BuildSinkInstance();
        act.Should().Throw<InvalidOperationException>();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static OpcUaServerSinkWizardModel MakeMinimallyValidModel() => new()
    {
        InstanceId = "opcua-1",
    };
}
