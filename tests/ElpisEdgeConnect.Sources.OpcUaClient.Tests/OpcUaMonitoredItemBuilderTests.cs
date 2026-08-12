// ============================================================================
// Tests: OpcUaMonitoredItemBuilderTests — pins the pure-logic
//        MonitoredItemConfig → Opc.Ua.Client.MonitoredItem translation.
//
//        Invariants:
//          * NodeId string parsed correctly (ns=2;i=10, ns=3;s=Channel.Tag)
//          * DisplayName flows through
//          * AttributeId = Value always (locked)
//          * DiscardOldest = true always (v2.1 §2.5 lock — edge prefers
//            fresh over backfill)
//          * MonitoringMode = Reporting always
//          * Per-item SamplingInterval override wins over config default
//          * Per-item QueueSize override wins over config default
//          * Defaults fall through from OpcUaClientSourceConfiguration
//          * DeadbandPercent populates a DataChangeFilter (Percent) only
//            when set; null → no filter
//          * Blank / null NodeId rejected
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5
// ============================================================================

using System;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Opc.Ua;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaMonitoredItemBuilderTests
{
    private static OpcUaClientSourceConfiguration Defaults() => new()
    {
        InstanceId = "opcua-test",
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "factorytalk",
        EndpointUrl = "opc.tcp://factorytalk.pilot.local:4840",
    };

    [Fact]
    public void Build_NumericNodeId_ParsedCorrectly()
    {
        var itemConfig = new MonitoredItemConfig
        {
            NodeId = "ns=2;i=10000",
            DisplayName = "Speed",
        };

        var item = OpcUaMonitoredItemBuilder.Build(itemConfig, Defaults());

        item.StartNodeId.ToString().Should().Be("ns=2;i=10000");
    }

    [Fact]
    public void Build_StringNodeId_ParsedCorrectly()
    {
        var itemConfig = new MonitoredItemConfig
        {
            NodeId = "ns=3;s=Channel1.Device1.Tag1",
            DisplayName = "Tag",
        };

        var item = OpcUaMonitoredItemBuilder.Build(itemConfig, Defaults());

        item.StartNodeId.ToString().Should().Be("ns=3;s=Channel1.Device1.Tag1");
    }

    [Fact]
    public void Build_DisplayName_FlowsThrough()
    {
        var item = OpcUaMonitoredItemBuilder.Build(
            new MonitoredItemConfig { NodeId = "ns=2;i=1", DisplayName = "Pilot/Speed" },
            Defaults());

        item.DisplayName.Should().Be("Pilot/Speed");
    }

    [Fact]
    public void Build_AttributeId_IsValueLock()
    {
        // Adapter reads the Value attribute only — locked.
        var item = OpcUaMonitoredItemBuilder.Build(
            new MonitoredItemConfig { NodeId = "ns=2;i=1", DisplayName = "T" },
            Defaults());

        item.AttributeId.Should().Be(Attributes.Value);
    }

    [Fact]
    public void Build_DiscardOldest_IsTrueLock()
    {
        // v2.1 §2.5 — edge prefers fresh data over backfill.
        var item = OpcUaMonitoredItemBuilder.Build(
            new MonitoredItemConfig { NodeId = "ns=2;i=1", DisplayName = "T" },
            Defaults());

        item.DiscardOldest.Should().BeTrue();
    }

    [Fact]
    public void Build_MonitoringMode_IsReporting()
    {
        var item = OpcUaMonitoredItemBuilder.Build(
            new MonitoredItemConfig { NodeId = "ns=2;i=1", DisplayName = "T" },
            Defaults());

        item.MonitoringMode.Should().Be(MonitoringMode.Reporting);
    }

    [Fact]
    public void Build_PerItemSamplingIntervalOverride_WinsOverDefault()
    {
        var defaults = Defaults() with { SamplingIntervalMs = 50 };
        var itemConfig = new MonitoredItemConfig
        {
            NodeId = "ns=2;i=1",
            DisplayName = "FastTag",
            SamplingIntervalMs = 10,
        };

        var item = OpcUaMonitoredItemBuilder.Build(itemConfig, defaults);

        item.SamplingInterval.Should().Be(10);
    }

    [Fact]
    public void Build_NullSamplingInterval_InheritsConfigDefault()
    {
        var defaults = Defaults() with { SamplingIntervalMs = 75 };
        var itemConfig = new MonitoredItemConfig
        {
            NodeId = "ns=2;i=1",
            DisplayName = "Inherited",
            SamplingIntervalMs = null,
        };

        var item = OpcUaMonitoredItemBuilder.Build(itemConfig, defaults);

        item.SamplingInterval.Should().Be(75);
    }

    [Fact]
    public void Build_PerItemQueueSizeOverride_WinsOverDefault()
    {
        var defaults = Defaults() with { DefaultAnalogQueueSize = 2 };
        var itemConfig = new MonitoredItemConfig
        {
            NodeId = "ns=2;i=1",
            DisplayName = "FastEventTag",
            QueueSize = 50,
        };

        var item = OpcUaMonitoredItemBuilder.Build(itemConfig, defaults);

        item.QueueSize.Should().Be(50u);
    }

    [Fact]
    public void Build_NullQueueSize_InheritsAnalogDefault()
    {
        var defaults = Defaults() with { DefaultAnalogQueueSize = 3 };
        var itemConfig = new MonitoredItemConfig
        {
            NodeId = "ns=2;i=1",
            DisplayName = "Inherited",
            QueueSize = null,
        };

        var item = OpcUaMonitoredItemBuilder.Build(itemConfig, defaults);

        item.QueueSize.Should().Be(3u);
    }

    [Fact]
    public void Build_DeadbandPercentSet_ProducesPercentDataChangeFilter()
    {
        var itemConfig = new MonitoredItemConfig
        {
            NodeId = "ns=2;i=1",
            DisplayName = "AnalogWithDeadband",
            DeadbandPercent = 5.0,
        };

        var item = OpcUaMonitoredItemBuilder.Build(itemConfig, Defaults());

        item.Filter.Should().BeOfType<DataChangeFilter>();
        var filter = (DataChangeFilter)item.Filter!;
        filter.DeadbandType.Should().Be((uint)DeadbandType.Percent);
        filter.DeadbandValue.Should().Be(5.0);
        filter.Trigger.Should().Be(DataChangeTrigger.StatusValue);
    }

    [Fact]
    public void Build_DeadbandPercentNull_NoFilterAttached()
    {
        var itemConfig = new MonitoredItemConfig
        {
            NodeId = "ns=2;i=1",
            DisplayName = "NoDeadband",
            DeadbandPercent = null,
        };

        var item = OpcUaMonitoredItemBuilder.Build(itemConfig, Defaults());

        item.Filter.Should().BeNull();
    }

    [Fact]
    public void Build_BlankNodeId_Throws()
    {
        var itemConfig = new MonitoredItemConfig
        {
            NodeId = "   ",
            DisplayName = "Invalid",
        };

        var act = () => OpcUaMonitoredItemBuilder.Build(itemConfig, Defaults());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*NodeId*non-empty*");
    }
}
