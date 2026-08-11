// ============================================================================
// Tests: OpcUaClientSourceConfigurationTests — pin the configuration
//        record's shape, defaults, and required-field surface.
//
//        Invariants:
//          * Tuning-knob defaults match v2.1 §2.5 LOCKED values
//          * SecurityMode defaults to SignAndEncrypt per v2.1 §6 Q7
//          * AuthMode defaults to Anonymous per v2.1 §6 Q7
//          * SecurityPolicyUri defaults to Basic256Sha256
//          * SessionTimeoutMs defaults to 60,000 (v2.1 §2.5)
//          * MonitoredItems defaults to empty (operator hasn't picked
//            tags yet — adapter still in a valid state)
//          * Required fields (EndpointUrl) compile-time enforced
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5, §6 Q7
// ============================================================================

using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaClientSourceConfigurationTests
{
    private static OpcUaClientSourceConfiguration MinimalConfig() => new()
    {
        InstanceId = "opcua-test",
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "factorytalk",
        EndpointUrl = "opc.tcp://factorytalk.pilot.local:4840",
    };

    [Fact]
    public void ProtocolNameConstant_Pinned()
    {
        OpcUaClientSourceConfiguration.ProtocolNameConstant.Should().Be("opcua-client");
    }

    [Fact]
    public void LicenseModuleKey_Pinned()
    {
        OpcUaClientSourceConfiguration.LicenseModuleKey.Should().Be("source-opcua-client");
    }

    // ─── v2.1 §2.5 locked tuning-knob defaults ─────────────────────────

    [Fact]
    public void Defaults_TuningKnobs_MatchV21Section25()
    {
        // Drift between this test and LockedTuningKnobs.cs in the
        // StackCeiling benchmark project fails compilation only if both
        // refer to the same constants — they don't (StackCeiling has
        // its own copy because Benchmarks doesn't depend on this
        // project). This test pins the v2.1 §2.5 numbers on the
        // configuration record's defaults.
        var config = MinimalConfig();

        config.PublishingIntervalMs.Should().Be(50);
        config.SamplingIntervalMs.Should().Be(50);
        config.KeepAliveCount.Should().Be(20u);
        config.LifetimeCount.Should().Be(60u);
        config.MaxNotificationsPerPublish.Should().Be(1_000u);
        config.DefaultAnalogQueueSize.Should().Be(2u);
        config.DefaultDiscreteQueueSize.Should().Be(10u);
        config.SessionTimeoutMs.Should().Be(60_000u);
    }

    // ─── v2.1 §6 Q7 lab-baseline defaults ─────────────────────────────

    [Fact]
    public void Defaults_Security_MatchV21Q7LabBaseline()
    {
        var config = MinimalConfig();

        config.SecurityMode.Should().Be(OpcUaSecurityMode.SignAndEncrypt);
        config.SecurityPolicyUri.Should().Be("http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256");
        config.AuthMode.Should().Be(OpcUaAuthMode.Anonymous);
    }

    [Fact]
    public void Defaults_ApplicationIdentity_AreReasonable()
    {
        var config = MinimalConfig();

        config.ApplicationName.Should().Be("Elpis EdgeConnect");
        config.ApplicationUri.Should().Be("urn:elpis:edgeconnect:opcua-client");
        config.AutoAcceptUntrustedServerCertificate.Should().BeTrue(
            "v2.1 §6 Q7 lab baseline auto-trusts; production deployments override to false.");
    }

    [Fact]
    public void Defaults_MonitoredItems_AreEmpty()
    {
        var config = MinimalConfig();

        config.MonitoredItems.Should().NotBeNull();
        config.MonitoredItems.Should().BeEmpty(
            "empty monitored items is a valid state — adapter starts but emits no data until the wizard adds tags.");
    }

    // ─── Overrides accepted ───────────────────────────────────────────

    [Fact]
    public void Overrides_AcceptedAndPreserved()
    {
        var creds = new OpcUaClientCredentials { Username = "operator", Password = "shhh" };
        var items = new[]
        {
            new MonitoredItemConfig { NodeId = "ns=2;i=10", DisplayName = "Speed", SamplingIntervalMs = 100 },
        };

        var config = MinimalConfig() with
        {
            SecurityMode = OpcUaSecurityMode.Sign,
            AuthMode = OpcUaAuthMode.UserName,
            Credentials = creds,
            PublishingIntervalMs = 100,
            MonitoredItems = items,
        };

        config.SecurityMode.Should().Be(OpcUaSecurityMode.Sign);
        config.AuthMode.Should().Be(OpcUaAuthMode.UserName);
        config.Credentials.Should().BeSameAs(creds);
        config.PublishingIntervalMs.Should().Be(100);
        config.MonitoredItems.Should().HaveCount(1);
        config.MonitoredItems[0].NodeId.Should().Be("ns=2;i=10");
    }

    // ─── PR 4 amendment #1: notification channel capacity ───────────

    [Fact]
    public void NotificationChannelCapacity_DefaultIs1000()
    {
        MinimalConfig().NotificationChannelCapacity.Should().Be(1_000);
    }

    [Fact]
    public void NotificationChannelCapacityConstants_LockedAtPr4Amendment1()
    {
        OpcUaClientSourceConfiguration.MinimumNotificationChannelCapacity.Should().Be(100);
        OpcUaClientSourceConfiguration.DefaultNotificationChannelCapacity.Should().Be(1_000);
        OpcUaClientSourceConfiguration.MaximumNotificationChannelCapacity.Should().Be(10_000);
    }

    [Fact]
    public void EndpointUrl_IsRequired_OnRecord()
    {
        // Compile-time enforcement via `required` modifier is the primary
        // guard. This is a runtime smoke that the modifier is on the
        // expected property.
        var prop = typeof(OpcUaClientSourceConfiguration).GetProperty(
            nameof(OpcUaClientSourceConfiguration.EndpointUrl));
        prop.Should().NotBeNull();
        prop!.GetCustomAttributes(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), false)
            .Should().NotBeEmpty();
    }
}
