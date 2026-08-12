// ============================================================================
// Tests: Focas2SourceWizardModel — pins the JSON shape the wizard emits
//        into SourceInstanceConfig.Connection AND the roundtrip parity
//        with Focas2SourceConfiguration.FromSourceInstance (the runtime
//        consumer). The Modbus wizard tests pin shape by string match
//        only; the FOCAS2 plan (v3 §8.1 test #3) calls the parity check
//        the "headline test" and the test project references
//        ElpisEdgeConnect.Sources.Focas2 specifically to enable it.
// Reference: docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md §8.1
// ============================================================================

using System;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.Focas2;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class Focas2SourceWizardModelTests
{
    // ── #1 ────────────────────────────────────────────────────────────────
    [Fact]
    public void Defaults_MatchAdapterDefaults()
    {
        // Cross-check the wizard's defaults against the runtime adapter
        // config's defaults — if either drifts, this test breaks loudly.
        var model = new Focas2SourceWizardModel();
        var reference = new Focas2SourceConfiguration
        {
            InstanceId = "ref",
            ProtocolName = Focas2SourceConfiguration.ProtocolNameConstant,
            DeviceId = "ref",
            IpAddress = "0.0.0.0",
        };

        model.Port.Should().Be(reference.Port).And.Be((ushort)8193);
        model.TimeoutSeconds.Should().Be(reference.TimeoutSeconds).And.Be(10);
        model.KeepAlive.Should().Be(reference.KeepAlive).And.BeTrue();
        model.InitialBackoffMs.Should().Be(reference.InitialBackoffMs).And.Be(5000);
        model.MaxBackoffMs.Should().Be(reference.MaxBackoffMs).And.Be(120_000);
        model.BackoffMultiplier.Should().Be(reference.BackoffMultiplier).And.Be(2.0);
        model.MaxConnectRetries.Should().Be(reference.MaxConnectRetries).And.Be(5);
    }

    // ── #2 ────────────────────────────────────────────────────────────────
    [Fact]
    public void BuildSourceInstance_ProtocolName_IsFocas2()
    {
        var model = new Focas2SourceWizardModel
        {
            InstanceId = "focas-1",
            IpAddress = "192.168.1.10",
        };

        var instance = model.BuildSourceInstance();

        instance.ProtocolName.Should().Be("focas2");
    }

    // ── #3 — HEADLINE PARITY TEST ─────────────────────────────────────────
    [Fact]
    public void BuildSourceInstance_PackedConnection_RoundtripsViaFromSourceInstance()
    {
        var model = new Focas2SourceWizardModel
        {
            InstanceId = "focas-cell-A",
            DeviceId = "F31i-A",
            DeviceName = "Cell A",
            DeviceClass = "cnc",
            Enabled = true,
            PollIntervalMs = 750,
            IpAddress = "192.168.10.20",
            Port = 8194,
            TimeoutSeconds = 7,
            KeepAlive = false,
            InitialBackoffMs = 3000,
            MaxBackoffMs = 60_000,
            BackoffMultiplier = 1.5,
            MaxConnectRetries = 3,
            DataPointsMode = Focas2DataPointSelectionMode.Selective,
        };
        model.SelectedGroupKeys.Add("axes");
        model.SelectedGroupKeys.Add("spindle");

        var instance = model.BuildSourceInstance();
        var typed = Focas2SourceConfiguration.FromSourceInstance(instance);

        typed.InstanceId.Should().Be("focas-cell-A");
        typed.ProtocolName.Should().Be("focas2");
        typed.DeviceId.Should().Be("F31i-A");
        typed.DeviceName.Should().Be("Cell A");
        typed.DeviceClass.Should().Be("cnc");
        typed.Enabled.Should().BeTrue();
        typed.PollIntervalMs.Should().Be(750);
        typed.IpAddress.Should().Be("192.168.10.20");
        typed.Port.Should().Be((ushort)8194);
        typed.TimeoutSeconds.Should().Be(7);
        typed.KeepAlive.Should().BeFalse();
        typed.InitialBackoffMs.Should().Be(3000);
        typed.MaxBackoffMs.Should().Be(60_000);
        typed.BackoffMultiplier.Should().Be(1.5);
        typed.MaxConnectRetries.Should().Be(3);
        typed.DataPoints.Should().BeEquivalentTo(new[] { "Axes/", "Spindle/" });
    }

    // ── #4 ────────────────────────────────────────────────────────────────
    [Fact]
    public void DataPoints_CollectAllMode_EmitsEmptyArray()
    {
        var model = new Focas2SourceWizardModel
        {
            InstanceId = "f",
            IpAddress = "1.2.3.4",
            DataPointsMode = Focas2DataPointSelectionMode.CollectAll,
        };
        // Even if the operator left selections behind from a previous toggle,
        // CollectAll mode wins — the picker state is ignored.
        model.SelectedGroupKeys.Add("axes");
        model.SelectedGroupKeys.Add("spindle");

        var dataPoints = model.BuildDataPointsList();

        dataPoints.Should().BeEmpty();
    }

    // ── #5 ────────────────────────────────────────────────────────────────
    [Fact]
    public void DataPoints_SelectiveMode_EmitsExpectedPrefixesAndPaths()
    {
        // Pins Locked O: prefix-or-exact emission per group. Axes uses
        // adapter-side prefix gating ("Axes/"); Alarms uses exact gating
        // ("Alarms/Active"). The wizard reflects each.
        var model = new Focas2SourceWizardModel
        {
            InstanceId = "f",
            IpAddress = "1.2.3.4",
            DataPointsMode = Focas2DataPointSelectionMode.Selective,
        };
        model.SelectedGroupKeys.Add("axes");
        model.SelectedGroupKeys.Add("alarms");

        var dataPoints = model.BuildDataPointsList();

        dataPoints.Should().BeEquivalentTo(new[] { "Axes/", "Alarms/Active" });
    }

    // ── #6 ────────────────────────────────────────────────────────────────
    [Fact]
    public void DataPoints_AllGroupsSelected_CollapsesToEmpty()
    {
        // Pins Locked O edge case: selecting every group is semantically
        // identical to "Collect all". Collapse to empty so the runtime
        // payload stays compact and future tag-map growth inherits cleanly.
        var model = new Focas2SourceWizardModel
        {
            InstanceId = "f",
            IpAddress = "1.2.3.4",
            DataPointsMode = Focas2DataPointSelectionMode.Selective,
        };
        foreach (var group in Focas2SourceWizardModel.DataPointGroups)
        {
            model.SelectedGroupKeys.Add(group.Key);
        }

        var dataPoints = model.BuildDataPointsList();

        dataPoints.Should().BeEmpty(
            "selecting every group is semantically equivalent to 'Collect all'");
    }

    // ── #7 ────────────────────────────────────────────────────────────────
    [Fact]
    public void BuildSourceInstance_DefaultsDeviceIdAndNameToInstanceId_WhenBlank()
    {
        var model = new Focas2SourceWizardModel
        {
            InstanceId = "focas-only-id",
            IpAddress = "127.0.0.1",
            // DeviceId, DeviceName intentionally left blank.
        };

        var instance = model.BuildSourceInstance();

        instance.DeviceId.Should().Be("focas-only-id");
        instance.DeviceName.Should().Be("focas-only-id");
    }

    // ── #8 ────────────────────────────────────────────────────────────────
    [Fact]
    public void BuildSourceInstance_PortOutOfUShortRange_Throws()
    {
        var model = new Focas2SourceWizardModel
        {
            InstanceId = "f",
            IpAddress = "1.2.3.4",
            Port = 70_000,  // > ushort.MaxValue
        };

        var act = () => model.BuildSourceInstance();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Port*");
    }

    // ── #9 ────────────────────────────────────────────────────────────────
    [Fact]
    public void BuildSourceInstance_IpAddressBlank_Throws()
    {
        var model = new Focas2SourceWizardModel
        {
            InstanceId = "f",
            IpAddress = "   ",  // whitespace only
        };

        var act = () => model.BuildSourceInstance();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*IpAddress*");
    }

    // ── #10 ───────────────────────────────────────────────────────────────
    [Fact]
    public void DefaultDeviceClass_IsCnc()
    {
        // Modbus defaults to "plc"; FOCAS2 is CNC-only by definition.
        var model = new Focas2SourceWizardModel();

        model.DeviceClass.Should().Be("cnc");
    }

    // ── #11 — HYDRATE ROUND-TRIP HEADLINE (M.2d.2 §5.5 Edit-mode hydration) ─
    [Fact]
    public void HydrateFromExisting_RoundTrips_ByteEquivalentSourceInstanceConfig()
    {
        // The Edit-mode router calls HydrateFromExisting to populate the
        // wizard from an existing SourceInstanceConfig; on Save the wizard
        // re-emits via BuildSourceInstance. The round-trip must be
        // byte-equivalent so a no-op edit produces no Connection-block
        // diff (and so RuntimeReloadClassifier can correctly classify
        // touched-only-cosmetic-fields edits).
        var original = new Focas2SourceWizardModel
        {
            InstanceId = "focas-cell-A",
            DeviceId = "F31i-A",
            DeviceName = "Cell A",
            DeviceClass = "cnc",
            Enabled = false,
            PollIntervalMs = 1500,
            IpAddress = "10.0.5.7",
            Port = 8194,
            TimeoutSeconds = 12,
            KeepAlive = false,
            InitialBackoffMs = 2500,
            MaxBackoffMs = 90_000,
            BackoffMultiplier = 1.8,
            MaxConnectRetries = 7,
            DataPointsMode = Focas2DataPointSelectionMode.Selective,
        };
        original.SelectedGroupKeys.Add("program");
        original.SelectedGroupKeys.Add("axes");
        original.SelectedGroupKeys.Add("production");

        var firstEmit = original.BuildSourceInstance();
        var hydrated = Focas2SourceWizardModel.HydrateFromExisting(firstEmit);
        var secondEmit = hydrated.BuildSourceInstance();

        // Identity + polling
        secondEmit.InstanceId.Should().Be(firstEmit.InstanceId);
        secondEmit.ProtocolName.Should().Be(firstEmit.ProtocolName);
        secondEmit.DeviceId.Should().Be(firstEmit.DeviceId);
        secondEmit.DeviceName.Should().Be(firstEmit.DeviceName);
        secondEmit.DeviceClass.Should().Be(firstEmit.DeviceClass);
        secondEmit.Enabled.Should().Be(firstEmit.Enabled);
        secondEmit.Polling.IntervalMs.Should().Be(firstEmit.Polling.IntervalMs);

        // Connection block — compare the raw JSON text to pin byte-equivalence.
        secondEmit.Connection.Should().NotBeNull();
        firstEmit.Connection.Should().NotBeNull();
        secondEmit.Connection!.Value.GetRawText().Should().Be(firstEmit.Connection!.Value.GetRawText());
    }

    // ── #12 ───────────────────────────────────────────────────────────────
    [Fact]
    public void HydrateFromExisting_EmptyDataPoints_HydratesToCollectAll()
    {
        // CollectAll mode emits an empty dataPoints array; the inverse maps
        // back to CollectAll, not Selective + zero groups (which would be a
        // distinct UI state).
        var original = new Focas2SourceWizardModel
        {
            InstanceId = "f",
            IpAddress = "1.1.1.1",
            DataPointsMode = Focas2DataPointSelectionMode.CollectAll,
        };
        var emitted = original.BuildSourceInstance();

        var hydrated = Focas2SourceWizardModel.HydrateFromExisting(emitted);

        hydrated.DataPointsMode.Should().Be(Focas2DataPointSelectionMode.CollectAll);
        hydrated.SelectedGroupKeys.Should().BeEmpty();
    }

    // ── #13 ───────────────────────────────────────────────────────────────
    [Fact]
    public void HydrateFromExisting_RecognisesAllGroupPaths()
    {
        // Every group's EmittedPaths must inverse-map to that group's Key —
        // this protects against drift if a new group is added to
        // DataPointGroups but HydrateFromExisting forgets to recognise it.
        foreach (var group in Focas2SourceWizardModel.DataPointGroups)
        {
            var original = new Focas2SourceWizardModel
            {
                InstanceId = $"focas-{group.Key}",
                IpAddress = "1.2.3.4",
                DataPointsMode = Focas2DataPointSelectionMode.Selective,
            };
            original.SelectedGroupKeys.Add(group.Key);
            var emitted = original.BuildSourceInstance();

            var hydrated = Focas2SourceWizardModel.HydrateFromExisting(emitted);

            hydrated.DataPointsMode.Should().Be(
                Focas2DataPointSelectionMode.Selective,
                because: $"group '{group.Key}' should hydrate as Selective");
            hydrated.SelectedGroupKeys.Should().BeEquivalentTo(
                new[] { group.Key },
                because: $"group '{group.Key}' emitted paths {string.Join(",", group.EmittedPaths)} should round-trip");
        }
    }

    // ── #14 ───────────────────────────────────────────────────────────────
    [Fact]
    public void HydrateFromExisting_WrongProtocol_Throws()
    {
        var modbus = new SourceInstanceConfig
        {
            InstanceId = "mb",
            ProtocolName = "modbustcp",
            DeviceId = "mb",
        };

        var act = () => Focas2SourceWizardModel.HydrateFromExisting(modbus);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*focas2*");
    }
}
