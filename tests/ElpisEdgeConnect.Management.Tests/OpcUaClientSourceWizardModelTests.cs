// ============================================================================
// Tests: OpcUaClientSourceWizardModel — pin the PR 7c-4 wire-shape
//        contract + amendments (de-dup canonicalisation, selection
//        summary, idempotent edit-mode save, back-nav persistence).
//
//        Round-trip invariant: BuildSourceInstance →
//        OpcUaClientSourceConfiguration.FromSourceInstance (PR 7c-3) →
//        BuildFromExisting produces a model whose BuildSourceInstance
//        emits byte-equivalent JSON to the original.
// Reference: PR 7c plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class OpcUaClientSourceWizardModelTests
{
    // ─── BuildSourceInstance — identity + connection ──────────────────

    [Fact]
    public void BuildSourceInstance_PopulatesIdentityFields()
    {
        var model = new OpcUaClientSourceWizardModel
        {
            InstanceId = "opcua-factorytalk-east",
            DeviceId = "scada-east",
            DeviceName = "FactoryTalk East SCADA",
            DeviceClass = "scada",
            Enabled = true,
            EndpointUrl = "opc.tcp://h:4840",
        };

        var instance = model.BuildSourceInstance();

        instance.InstanceId.Should().Be("opcua-factorytalk-east");
        instance.ProtocolName.Should().Be("opcua-client");
        instance.DeviceId.Should().Be("scada-east");
        instance.DeviceName.Should().Be("FactoryTalk East SCADA");
        instance.DeviceClass.Should().Be("scada");
        instance.Enabled.Should().BeTrue();
    }

    [Fact]
    public void BuildSourceInstance_DefaultsDeviceIdAndNameToInstanceId_WhenEmpty()
    {
        var model = new OpcUaClientSourceWizardModel
        {
            InstanceId = "opcua-1",
            EndpointUrl = "opc.tcp://h:4840",
        };

        var instance = model.BuildSourceInstance();

        instance.DeviceId.Should().Be("opcua-1");
        instance.DeviceName.Should().Be("opcua-1");
    }

    [Fact]
    public void BuildSourceInstance_EmitsConnectionWithAllConfiguredFields()
    {
        var model = new OpcUaClientSourceWizardModel
        {
            InstanceId = "opcua-1",
            EndpointUrl = "opc.tcp://h:4840",
            ApplicationUri = "urn:custom",
            ApplicationName = "Custom App",
            SessionTimeoutMs = 30_000,
            SecurityMode = OpcUaSecurityMode.SignAndEncrypt,
            AuthMode = OpcUaAuthMode.Anonymous,
            PublishingIntervalMs = 100,
            SamplingIntervalMs = 100,
            KeepAliveCount = 30,
            LifetimeCount = 120,
        };

        var instance = model.BuildSourceInstance();
        var conn = instance.Connection!.Value;

        conn.GetProperty("endpointUrl").GetString().Should().Be("opc.tcp://h:4840");
        conn.GetProperty("applicationUri").GetString().Should().Be("urn:custom");
        conn.GetProperty("applicationName").GetString().Should().Be("Custom App");
        conn.GetProperty("sessionTimeoutMs").GetUInt32().Should().Be(30_000u);
        conn.GetProperty("securityMode").GetString().Should().Be("SignAndEncrypt");
        conn.GetProperty("authMode").GetString().Should().Be("Anonymous");
        conn.GetProperty("publishingIntervalMs").GetInt32().Should().Be(100);
        conn.GetProperty("keepAliveCount").GetUInt32().Should().Be(30u);
        conn.GetProperty("lifetimeCount").GetUInt32().Should().Be(120u);
    }

    [Fact]
    public void BuildSourceInstance_AnonymousAuth_EmitsNoCredentialsBlock()
    {
        var model = new OpcUaClientSourceWizardModel
        {
            InstanceId = "opcua-1",
            EndpointUrl = "opc.tcp://h:4840",
            AuthMode = OpcUaAuthMode.Anonymous,
            Username = "should-be-ignored",
        };

        var instance = model.BuildSourceInstance();
        var conn = instance.Connection!.Value;

        conn.TryGetProperty("credentials", out _).Should().BeFalse(
            "Anonymous auth must NOT emit a credentials block even if fields are populated");
    }

    [Fact]
    public void BuildSourceInstance_UserNameAuth_EmitsCredentialsBlock()
    {
        var model = new OpcUaClientSourceWizardModel
        {
            InstanceId = "opcua-1",
            EndpointUrl = "opc.tcp://h:4840",
            SecurityMode = OpcUaSecurityMode.SignAndEncrypt,
            AuthMode = OpcUaAuthMode.UserName,
            Username = "factorytalk",
            Password = "secret",
        };

        var instance = model.BuildSourceInstance();
        var conn = instance.Connection!.Value;
        var creds = conn.GetProperty("credentials");

        creds.GetProperty("username").GetString().Should().Be("factorytalk");
        creds.GetProperty("password").GetString().Should().Be("secret");
    }

    // ─── BuildSourceInstance — monitored items ────────────────────────

    [Fact]
    public void BuildSourceInstance_EmitsMonitoredItemsArray()
    {
        var model = new OpcUaClientSourceWizardModel
        {
            InstanceId = "opcua-1",
            EndpointUrl = "opc.tcp://h:4840",
            MonitoredItems =
            {
                new MonitoredItemWizardRow { NodeId = "ns=2;i=1", DisplayName = "Counter" },
                new MonitoredItemWizardRow
                {
                    NodeId = "ns=2;s=Sine", DisplayName = "Sine",
                    SamplingIntervalMs = 100, QueueSize = 5, DeadbandPercent = 1.5,
                },
            },
        };

        var instance = model.BuildSourceInstance();
        var items = instance.Connection!.Value.GetProperty("monitoredItems");

        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("nodeId").GetString().Should().Be("ns=2;i=1");
        items[0].GetProperty("displayName").GetString().Should().Be("Counter");
        items[1].GetProperty("samplingIntervalMs").GetInt32().Should().Be(100);
        items[1].GetProperty("queueSize").GetUInt32().Should().Be(5u);
        items[1].GetProperty("deadbandPercent").GetDouble().Should().Be(1.5);
    }

    // ─── TryAddMonitoredItem — amendment #2 (canonical de-dup) ────────

    [Fact]
    public void TryAddMonitoredItem_NewNodeId_ReturnsTrue_AndAppends()
    {
        var model = NewModelWithBasics();

        var added = model.TryAddMonitoredItem("ns=2;i=1", "Counter");

        added.Should().BeTrue();
        model.MonitoredItems.Should().HaveCount(1);
        model.MonitoredItems[0].DisplayName.Should().Be("Counter");
    }

    [Fact]
    public void TryAddMonitoredItem_SemanticallyEqualExistingNodeId_ReturnsFalse_NoDuplicate()
    {
        // Amendment #2 (user lock 2026-05-29) — manual tag entry uses
        // OpcUaNodeIdCanonicalizer. "ns=2;i=00000001" is semantically
        // the same NodeId as "ns=2;i=1"; adding the second MUST collapse
        // to the existing row.
        var model = NewModelWithBasics();
        model.TryAddMonitoredItem("ns=2;i=1", "Counter");

        var added = model.TryAddMonitoredItem("ns=2;i=00000001", "Duplicate");

        added.Should().BeFalse(
            "amendment #2 — semantic canonicalisation must collapse the duplicate");
        model.MonitoredItems.Should().HaveCount(1);
    }

    [Fact]
    public void TryAddMonitoredItem_EmptyNodeId_ReturnsFalse()
    {
        var model = NewModelWithBasics();

        model.TryAddMonitoredItem("", "Anything").Should().BeFalse();
        model.TryAddMonitoredItem("   ", "Anything").Should().BeFalse();
        model.MonitoredItems.Should().BeEmpty();
    }

    [Fact]
    public void TryAddMonitoredItem_EmptyDisplayName_FallsBackToNodeId()
    {
        var model = NewModelWithBasics();

        model.TryAddMonitoredItem("ns=2;i=1", "").Should().BeTrue();

        model.MonitoredItems[0].DisplayName.Should().Be("ns=2;i=1");
    }

    // ─── SelectionSummary — amendment #3 (live Browse panel) ──────────

    [Fact]
    public void SelectionSummary_ReflectsCurrentMonitoredItemCount()
    {
        var model = NewModelWithBasics();
        for (var i = 0; i < 10_500; i++)
        {
            model.MonitoredItems.Add(new MonitoredItemWizardRow
            {
                NodeId = $"ns=2;i={i}",
                DisplayName = $"Tag{i}",
            });
        }

        var summary = model.SelectionSummary;

        summary.Severity.Should().Be(TagCountSeverity.Informational,
            "10K - 30K is the Informational tier");
        summary.ExpectedSubscriptions.Should().Be(11,
            "10_500 items / 1000 per sub = ceil(11) subscriptions");
    }

    // ─── CanSave / Tag-count threshold gating ─────────────────────────

    [Fact]
    public void CanSave_BlockedByOver100KTagCount_ReturnsFalse()
    {
        // PR 7c amendment #3 — Blocked severity disables Save.
        var model = NewModelWithBasics();
        // Faux-allocate 100_000 items via a placeholder so we don't
        // burn time on 100K real allocations.
        // 100_000 = OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession
        // (internal to Sources.OpcUaClient — referenced by literal).
        for (var i = 0; i < TagCountThresholds.BlockedLowerBound + 1; i++)
        {
            model.MonitoredItems.Add(new MonitoredItemWizardRow
            {
                NodeId = $"ns=2;i={i}",
                DisplayName = $"T{i}",
            });
        }

        model.SelectionSummary.BlocksSave.Should().BeTrue();
        model.CanSave.Should().BeFalse(
            "amendment #3 — Blocked tag count disables Save");
    }

    [Fact]
    public void CanSave_HappyPath_ReturnsTrue()
    {
        var model = NewModelWithBasics();
        model.MonitoredItems.Add(new MonitoredItemWizardRow { NodeId = "ns=2;i=1", DisplayName = "T" });

        model.CanSave.Should().BeTrue();
    }

    [Fact]
    public void CanSave_MissingInstanceIdOrEndpoint_ReturnsFalse()
    {
        new OpcUaClientSourceWizardModel { EndpointUrl = "opc.tcp://h:4840" }
            .CanSave.Should().BeFalse("missing InstanceId");

        new OpcUaClientSourceWizardModel { InstanceId = "id" }
            .CanSave.Should().BeFalse("missing EndpointUrl");

        new OpcUaClientSourceWizardModel { InstanceId = "id", EndpointUrl = "http://nope" }
            .CanSave.Should().BeFalse("non-opc.tcp scheme");
    }

    // ─── Coherence validation ────────────────────────────────────────

    [Fact]
    public void ValidateCoherenceIssues_UserNameOverNone_FlagsSecurityIssue()
    {
        var model = NewModelWithBasics();
        model.SecurityMode = OpcUaSecurityMode.None;
        model.AuthMode = OpcUaAuthMode.UserName;
        model.Username = "u";
        model.Password = "p";

        var issues = model.ValidateCoherenceIssues();

        issues.Select(i => i.Code).Should().Contain("OPCUA.UNSAFE_USERNAME_OVER_NONE");
    }

    [Fact]
    public void ValidateCoherenceIssues_UserNameWithoutCredentials_Flags()
    {
        var model = NewModelWithBasics();
        model.AuthMode = OpcUaAuthMode.UserName;

        var issues = model.ValidateCoherenceIssues();

        issues.Select(i => i.Code).Should().Contain("OPCUA.USERNAME_CREDENTIALS_MISSING");
    }

    [Fact]
    public void ValidateCoherenceIssues_LifetimeShorterThan3xKeepAlive_Flags()
    {
        var model = NewModelWithBasics();
        model.KeepAliveCount = 20;
        model.LifetimeCount = 50;   // < 3 × 20

        model.ValidateCoherenceIssues().Select(i => i.Code)
            .Should().Contain("OPCUA.CONFIG_LIFETIME_TOO_SHORT");
    }

    // ─── Round-trip: Build → FromSourceInstance → BuildFromExisting ──

    [Fact]
    public void RoundTrip_BuildThenHydrate_PreservesAllConfigurationFields()
    {
        // This is the load-bearing test for amendment #5 (idempotent
        // edit-mode save). When the operator changes nothing, the
        // rebuilt instance must be byte-equivalent to the original so
        // the host-side reconfigure diff sees zero changes.
        var original = new OpcUaClientSourceWizardModel
        {
            InstanceId = "opcua-roundtrip",
            DeviceId = "scada",
            DeviceName = "Test SCADA",
            DeviceClass = "scada",
            Enabled = true,
            EndpointUrl = "opc.tcp://h:4840",
            ApplicationUri = "urn:custom",
            ApplicationName = "App",
            SessionTimeoutMs = 45_000,
            SecurityMode = OpcUaSecurityMode.SignAndEncrypt,
            AuthMode = OpcUaAuthMode.UserName,
            Username = "u", Password = "p",
            PublishingIntervalMs = 50,
            SamplingIntervalMs = 50,
            KeepAliveCount = 20,
            LifetimeCount = 60,
            MaxNotificationsPerPublish = 1_000,
            DefaultAnalogQueueSize = 2,
            DefaultDiscreteQueueSize = 10,
            NotificationChannelCapacity = 1_000,
            MonitoredItems =
            {
                new MonitoredItemWizardRow { NodeId = "ns=2;i=1", DisplayName = "Counter" },
                new MonitoredItemWizardRow
                {
                    NodeId = "ns=2;s=Sine", DisplayName = "Sine",
                    SamplingIntervalMs = 100, QueueSize = 5, DeadbandPercent = 1.5,
                },
            },
        };

        var instance1 = original.BuildSourceInstance();
        var hydrated = OpcUaClientSourceWizardModel.BuildFromExisting(instance1);
        var instance2 = hydrated.BuildSourceInstance();

        var json1 = JsonSerializer.Serialize(instance1.Connection!.Value);
        var json2 = JsonSerializer.Serialize(instance2.Connection!.Value);

        json2.Should().Be(json1,
            "amendment #5 — round-trip Build→Hydrate→Build must produce byte-equivalent Connection JSON "
            + "so an unchanged edit-mode save produces a no-op reconfigure diff");
    }

    [Fact]
    public void RoundTrip_MonitoredItemsOrder_Preserved()
    {
        // Tag order is operator-meaningful (it drives the subscription
        // batching boundaries from the planner). BuildFromExisting MUST
        // preserve it exactly.
        var original = NewModelWithBasics();
        foreach (var i in new[] { "c", "a", "b" })
        {
            original.MonitoredItems.Add(new MonitoredItemWizardRow
            {
                NodeId = $"ns=2;s={i}",
                DisplayName = i,
            });
        }

        var hydrated = OpcUaClientSourceWizardModel.BuildFromExisting(original.BuildSourceInstance());

        hydrated.MonitoredItems.Select(mi => mi.DisplayName).Should().Equal("c", "a", "b");
    }

    // ─── Back-navigation persistence (amendment #4) ───────────────────

    [Fact]
    public void Selections_SurviveModelInstanceLifetime()
    {
        // Amendment #4 — when the operator goes Browse → select tags →
        // Review → Back to Browse, selections must persist. The model
        // holds them; the razor binds the model. The pinned invariant
        // is: mutating the model and re-reading produces the latest
        // state (rather than the razor erasing state on re-render).
        var model = NewModelWithBasics();
        model.TryAddMonitoredItem("ns=2;i=1", "Counter");
        model.TryAddMonitoredItem("ns=2;i=2", "Sine");

        // Simulate the razor re-binding to the model after Back-nav.
        model.MonitoredItems.Should().HaveCount(2);
        model.MonitoredItems[0].DisplayName.Should().Be("Counter");
        model.MonitoredItems[1].DisplayName.Should().Be("Sine");

        // The summary recomputes — also stable across the back-nav.
        model.SelectionSummary.ExpectedSubscriptions.Should().Be(1);
    }

    // ─── helpers ──────────────────────────────────────────────────────

    private static OpcUaClientSourceWizardModel NewModelWithBasics() => new()
    {
        InstanceId = "opcua-test",
        EndpointUrl = "opc.tcp://h:4840",
        ApplicationUri = "urn:test",
    };
}
