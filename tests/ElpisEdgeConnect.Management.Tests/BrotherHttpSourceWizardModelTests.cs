// ============================================================================
// Tests: BrotherHttpSourceWizardModel — pins the JSON shape the wizard
//        emits into SourceInstanceConfig.Connection, the defaults match
//        BrotherHttpSourceConfiguration's expectations, and the
//        DataPoints group picker collapses to empty when "every group"
//        is selected (matches FOCAS2's CollectAll equivalence).
//
//        Round-trip parity with BrotherHttpSourceConfiguration.FromSourceInstance
//        is covered separately by BrotherHttpSourceConfigurationTests in
//        the Sources.BrotherHttp.Tests project — Management does not
//        reference Sources.BrotherHttp so the wizard model stays
//        protocol-agnostic at the project-reference level.
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §10 step 11
// ============================================================================

using System;
using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class BrotherHttpSourceWizardModelTests
{
    [Fact]
    public void Defaults_MatchExpectedAdapterDefaults()
    {
        var model = new BrotherHttpSourceWizardModel();

        model.TimeoutSeconds.Should().Be(10);
        model.FaultThresholdConsecutiveFailures.Should().Be(3);
        model.InitialBackoffMs.Should().Be(5000);
        model.MaxBackoffMs.Should().Be(120_000);
        model.BackoffMultiplier.Should().Be(2.0);
        model.PollIntervalMs.Should().Be(3000);   // Q10 default
        model.DeviceClass.Should().Be("cnc");
        model.Enabled.Should().BeTrue();
        model.DataPointsMode.Should().Be(BrotherHttpDataPointSelectionMode.CollectAll);
    }

    [Fact]
    public void BuildSourceInstance_ProtocolName_IsBrotherHttp()
    {
        var model = new BrotherHttpSourceWizardModel
        {
            InstanceId = "brother-1",
            BaseUrl = "http://192.168.2.110",
        };

        var instance = model.BuildSourceInstance();

        instance.ProtocolName.Should().Be("brother-http");
    }

    [Fact]
    public void BuildSourceInstance_EmptyBaseUrl_Throws()
    {
        var model = new BrotherHttpSourceWizardModel
        {
            InstanceId = "brother-1",
            BaseUrl = "",
        };

        System.Action act = () => model.BuildSourceInstance();
        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*BaseUrl*");
    }

    [Fact]
    public void BuildSourceInstance_ZeroTimeout_Throws()
    {
        var model = new BrotherHttpSourceWizardModel
        {
            InstanceId = "brother-1",
            BaseUrl = "http://x",
            TimeoutSeconds = 0,
        };

        System.Action act = () => model.BuildSourceInstance();
        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*TimeoutSeconds*");
    }

    [Fact]
    public void BuildSourceInstance_PacksConnectionJson_WithBrotherSpecificFields()
    {
        var model = new BrotherHttpSourceWizardModel
        {
            InstanceId = "brother-line-A",
            DeviceId = "Brother-A",
            DeviceName = "Speedio Line A",
            DeviceClass = "cnc",
            Enabled = true,
            PollIntervalMs = 5000,
            BaseUrl = "http://192.168.2.110",
            TimeoutSeconds = 7,
            FaultThresholdConsecutiveFailures = 5,
            InitialBackoffMs = 3000,
            MaxBackoffMs = 60_000,
            BackoffMultiplier = 1.5,
        };

        var instance = model.BuildSourceInstance();

        instance.InstanceId.Should().Be("brother-line-A");
        instance.DeviceId.Should().Be("Brother-A");
        instance.DeviceName.Should().Be("Speedio Line A");
        instance.DeviceClass.Should().Be("cnc");
        instance.Enabled.Should().BeTrue();
        instance.Polling.IntervalMs.Should().Be(5000);

        instance.Connection.Should().NotBeNull();
        var conn = instance.Connection!.Value;
        conn.GetProperty("baseUrl").GetString().Should().Be("http://192.168.2.110");
        conn.GetProperty("timeoutSeconds").GetInt32().Should().Be(7);
        conn.GetProperty("faultThresholdConsecutiveFailures").GetInt32().Should().Be(5);
        conn.GetProperty("initialBackoffMs").GetInt32().Should().Be(3000);
        conn.GetProperty("maxBackoffMs").GetInt32().Should().Be(60_000);
        conn.GetProperty("backoffMultiplier").GetDouble().Should().Be(1.5);
        conn.GetProperty("dataPoints").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void BuildSourceInstance_BlankIdentity_FallsBackToInstanceId()
    {
        var model = new BrotherHttpSourceWizardModel
        {
            InstanceId = "brother-only-id",
            BaseUrl = "http://x",
            DeviceId = "",
            DeviceName = "",
        };

        var instance = model.BuildSourceInstance();

        instance.DeviceId.Should().Be("brother-only-id");
        instance.DeviceName.Should().Be("brother-only-id");
    }

    [Fact]
    public void BuildDataPointsList_CollectAllMode_ReturnsEmpty()
    {
        var model = new BrotherHttpSourceWizardModel
        {
            DataPointsMode = BrotherHttpDataPointSelectionMode.CollectAll,
        };
        model.SelectedGroupKeys.Add("tools");

        model.BuildDataPointsList().Should().BeEmpty(
            "CollectAll always emits empty regardless of SelectedGroupKeys");
    }

    [Fact]
    public void BuildDataPointsList_Selective_EmitsSelectedGroupPaths()
    {
        var model = new BrotherHttpSourceWizardModel
        {
            DataPointsMode = BrotherHttpDataPointSelectionMode.Selective,
        };
        model.SelectedGroupKeys.Add("tools");
        model.SelectedGroupKeys.Add("alarms");

        var result = model.BuildDataPointsList();

        result.Should().Contain("Tools/");
        result.Should().Contain("Alarms/");
        result.Should().NotContain("Maintenance/");
    }

    [Fact]
    public void BuildDataPointsList_AllGroupsSelected_CollapsesToEmpty()
    {
        var model = new BrotherHttpSourceWizardModel
        {
            DataPointsMode = BrotherHttpDataPointSelectionMode.Selective,
        };
        foreach (var group in BrotherHttpSourceWizardModel.DataPointGroups)
        {
            model.SelectedGroupKeys.Add(group.Key);
        }

        model.BuildDataPointsList().Should().BeEmpty(
            "every-group-selected is semantically identical to CollectAll");
    }

    [Fact]
    public void DataPointGroups_AllPathsEndWithSlash_IndicatingPrefixMatch()
    {
        // The catalog-membership check in BrotherTagMap.IsKnownPathOrPrefix
        // treats trailing-slash entries as prefixes. The wizard groups emit
        // prefix paths only (covers all leaves under each subtree) so the
        // operator can opt in to a whole category with one checkbox.
        foreach (var group in BrotherHttpSourceWizardModel.DataPointGroups)
        {
            foreach (var path in group.EmittedPaths)
            {
                path.Should().EndWith("/",
                    $"group '{group.Key}' path '{path}' must end with '/' for prefix matching");
            }
        }
    }

    // ── HYDRATE ROUND-TRIP (M.2d.2 §5.5 Edit-mode hydration) ───────────────
    [Fact]
    public void HydrateFromExisting_RoundTrips_ByteEquivalentSourceInstanceConfig()
    {
        var original = new BrotherHttpSourceWizardModel
        {
            InstanceId = "brother-line-A-cnc-1",
            DeviceId = "S700Xd1-A1",
            DeviceName = "Cell A — CNC 1",
            DeviceClass = "cnc",
            Enabled = false,
            PollIntervalMs = 5000,
            BaseUrl = "http://192.168.2.110",
            TimeoutSeconds = 8,
            FaultThresholdConsecutiveFailures = 5,
            InitialBackoffMs = 3000,
            MaxBackoffMs = 90_000,
            BackoffMultiplier = 1.6,
            DataPointsMode = BrotherHttpDataPointSelectionMode.Selective,
        };
        original.SelectedGroupKeys.Add("program");
        original.SelectedGroupKeys.Add("alarms");
        original.SelectedGroupKeys.Add("tools");

        var firstEmit = original.BuildSourceInstance();
        var hydrated = BrotherHttpSourceWizardModel.HydrateFromExisting(firstEmit);
        var secondEmit = hydrated.BuildSourceInstance();

        secondEmit.InstanceId.Should().Be(firstEmit.InstanceId);
        secondEmit.ProtocolName.Should().Be(firstEmit.ProtocolName);
        secondEmit.DeviceId.Should().Be(firstEmit.DeviceId);
        secondEmit.DeviceName.Should().Be(firstEmit.DeviceName);
        secondEmit.DeviceClass.Should().Be(firstEmit.DeviceClass);
        secondEmit.Enabled.Should().Be(firstEmit.Enabled);
        secondEmit.Polling.IntervalMs.Should().Be(firstEmit.Polling.IntervalMs);

        secondEmit.Connection.Should().NotBeNull();
        firstEmit.Connection.Should().NotBeNull();
        secondEmit.Connection!.Value.GetRawText().Should().Be(firstEmit.Connection!.Value.GetRawText());
    }

    [Fact]
    public void HydrateFromExisting_EmptyDataPoints_HydratesToCollectAll()
    {
        var original = new BrotherHttpSourceWizardModel
        {
            InstanceId = "b",
            BaseUrl = "http://1.2.3.4",
            DataPointsMode = BrotherHttpDataPointSelectionMode.CollectAll,
        };
        var emitted = original.BuildSourceInstance();

        var hydrated = BrotherHttpSourceWizardModel.HydrateFromExisting(emitted);

        hydrated.DataPointsMode.Should().Be(BrotherHttpDataPointSelectionMode.CollectAll);
        hydrated.SelectedGroupKeys.Should().BeEmpty();
    }

    [Fact]
    public void HydrateFromExisting_RecognisesAllGroupPaths()
    {
        // Drift guard: every group's EmittedPaths must inverse-map to its Key,
        // otherwise a new group added to DataPointGroups would round-trip lossily.
        foreach (var group in BrotherHttpSourceWizardModel.DataPointGroups)
        {
            var original = new BrotherHttpSourceWizardModel
            {
                InstanceId = $"brother-{group.Key}",
                BaseUrl = "http://1.2.3.4",
                DataPointsMode = BrotherHttpDataPointSelectionMode.Selective,
            };
            original.SelectedGroupKeys.Add(group.Key);
            var emitted = original.BuildSourceInstance();

            var hydrated = BrotherHttpSourceWizardModel.HydrateFromExisting(emitted);

            hydrated.DataPointsMode.Should().Be(
                BrotherHttpDataPointSelectionMode.Selective,
                because: $"group '{group.Key}' should hydrate as Selective");
            hydrated.SelectedGroupKeys.Should().BeEquivalentTo(
                new[] { group.Key },
                because: $"group '{group.Key}' emitted paths {string.Join(",", group.EmittedPaths)} should round-trip");
        }
    }

    [Fact]
    public void HydrateFromExisting_WrongProtocol_Throws()
    {
        var focas2 = new SourceInstanceConfig
        {
            InstanceId = "f",
            ProtocolName = "focas2",
            DeviceId = "f",
        };

        var act = () => BrotherHttpSourceWizardModel.HydrateFromExisting(focas2);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*brother-http*");
    }
}
