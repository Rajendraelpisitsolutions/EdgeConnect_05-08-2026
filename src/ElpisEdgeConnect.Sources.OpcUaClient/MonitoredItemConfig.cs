// ============================================================================
// File: MonitoredItemConfig.cs
// Purpose: Per-tag monitored-item configuration record for an OPC UA
//          Client subscription. Operators select these in the wizard
//          via the TagBrowseTreeView (ADR-0015 Rules 9 / 10); the
//          resulting list lives on OpcUaClientSourceConfiguration.
//
//          Per-item overrides for SamplingInterval / QueueSize allow
//          deviating from the connection-wide defaults locked at
//          v2.1 §2.5 — useful when a single fast-changing tag needs
//          faster sampling than the bulk.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5
//            docs/decisions/0015-wizard-contract.md Rule 9, Rule 10
// ============================================================================

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Per-monitored-item configuration carried inside
/// <see cref="OpcUaClientSourceConfiguration"/>.
/// </summary>
public sealed record MonitoredItemConfig
{
    /// <summary>
    /// Protocol-native OPC UA <c>NodeId</c> string (e.g.,
    /// <c>"ns=2;i=10000"</c>, <c>"ns=3;s=Channel1.Device1.Tag1"</c>).
    /// Selected by the operator through the browse picker;
    /// hand-entry is supported for advanced cases.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Operator-facing display name for the tag. Surfaces on
    /// <see cref="ElpisEdgeConnect.Core.Model.CanonicalDataPoint.TagName"/>
    /// after the canonical-tag transform pipeline; the wizard pre-fills
    /// this from the browse result's <c>DisplayName</c> but operators may
    /// edit it.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Optional per-item sampling-interval override (milliseconds).
    /// <see langword="null"/> = inherit the connection-wide default
    /// from <see cref="OpcUaClientSourceConfiguration.SamplingIntervalMs"/>
    /// (v2.1 §2.5 locked default: 50 ms).
    /// </summary>
    public int? SamplingIntervalMs { get; init; }

    /// <summary>
    /// Optional per-item queue-size override. <see langword="null"/> =
    /// inherit the connection-wide default
    /// (v2.1 §2.5 locked: 2 for analog, 10 for discrete/events; the
    /// wizard's tag-type heuristic picks which one).
    /// </summary>
    public uint? QueueSize { get; init; }

    /// <summary>
    /// Optional per-tag deadband — protocol-native % deadband sent on
    /// the monitored-item creation request. The OPC UA stack handles
    /// the dead-banding server-side, reducing notification volume
    /// without client-side filtering.
    /// </summary>
    public double? DeadbandPercent { get; init; }
}
