// ============================================================================
// File: OpcUaMonitoredItemBuilder.cs
// Purpose: Pure-logic translator from MonitoredItemConfig → Opc.Ua.Client.MonitoredItem.
//          Per-item SamplingInterval / QueueSize / DeadbandPercent
//          overrides fall back to connection-wide defaults from
//          OpcUaClientSourceConfiguration.
//
// LOCKED behaviour:
//   * AttributeId = Value (always — adapter reads value attribute only)
//   * DiscardOldest = true (always — edge prefers fresh data over backfill;
//     v2.1 §2.5 lock)
//   * NodeId parsed via OPC stack's NodeId(string) constructor
//   * DeadbandPercent populates a DataChangeFilter(PercentDeadband) when
//     set; null = no server-side filter (client-side COV layer in
//     EtherNet/IP equivalent doesn't apply here — OPC UA does it server
//     side when configured)
//   * Per-item SamplingInterval overrides config default; null inherits
//   * Per-item QueueSize overrides config default; null inherits
//     DefaultAnalogQueueSize (discrete-vs-analog heuristic deferred)
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5
// ============================================================================

using System;
using Opc.Ua;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Build an OPC UA <see cref="MonitoredItem"/> from a
/// <see cref="MonitoredItemConfig"/>. Pure logic — no Session / no I/O.
/// </summary>
internal static class OpcUaMonitoredItemBuilder
{
    /// <summary>
    /// Build a single monitored item. <paramref name="connectionDefaults"/>
    /// supplies the connection-wide tuning knobs the item inherits when
    /// it doesn't specify its own.
    /// </summary>
    public static MonitoredItem Build(
        MonitoredItemConfig itemConfig,
        OpcUaClientSourceConfiguration connectionDefaults)
    {
        ArgumentNullException.ThrowIfNull(itemConfig);
        ArgumentNullException.ThrowIfNull(connectionDefaults);

        if (string.IsNullOrWhiteSpace(itemConfig.NodeId))
        {
            throw new ArgumentException(
                "MonitoredItemConfig.NodeId must be non-empty.",
                nameof(itemConfig));
        }

        var samplingInterval = itemConfig.SamplingIntervalMs ?? connectionDefaults.SamplingIntervalMs;
        var queueSize = itemConfig.QueueSize ?? connectionDefaults.DefaultAnalogQueueSize;

        var monitoredItem = new MonitoredItem
        {
            DisplayName = itemConfig.DisplayName,
            StartNodeId = new NodeId(itemConfig.NodeId),
            AttributeId = Attributes.Value,
            MonitoringMode = MonitoringMode.Reporting,
            SamplingInterval = samplingInterval,
            QueueSize = queueSize,
            DiscardOldest = true,
        };

        if (itemConfig.DeadbandPercent.HasValue)
        {
            monitoredItem.Filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Percent,
                DeadbandValue = itemConfig.DeadbandPercent.Value,
            };
        }

        return monitoredItem;
    }
}
