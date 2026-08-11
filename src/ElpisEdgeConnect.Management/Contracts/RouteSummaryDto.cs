// ============================================================================
// File: Contracts/RouteSummaryDto.cs
// Purpose: API contract shapes for the management surface. Locked at
//          v1 — Blazor components and external HTTP consumers both
//          deserialize these. Schema-breaking changes require a
//          /api/v2 sibling per the same versioning policy that
//          governs the OPC UA namespace contract.
//
//          Kept deliberately small at M.1a — only the fields the
//          Overview page needs. M.1b/c grow this surface.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1a
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Overview-grid summary of one route — enough for the Overview
/// page to render a status card without round-tripping for details.
/// </summary>
public sealed record RouteSummaryDto
{
    /// <summary>Stable route identifier.</summary>
    public required string RouteId { get; init; }

    /// <summary>UTC timestamp this snapshot was built.</summary>
    public required DateTime ObservedAtUtc { get; init; }

    /// <summary>
    /// Numeric route state for compactness — clients display the
    /// string form via the <see cref="StateName"/> companion.
    /// </summary>
    public required int State { get; init; }

    /// <summary>Operator-readable state ("Running", "Stopped", "Degraded", ...).</summary>
    public required string StateName { get; init; }

    /// <summary>Source instance summary, when one is attached to the route.</summary>
    public RouteSourceSummaryDto? Source { get; init; }

    /// <summary>Sink instance summaries (zero or more per route).</summary>
    public IReadOnlyList<RouteSinkSummaryDto> Sinks { get; init; } = Array.Empty<RouteSinkSummaryDto>();

    /// <summary>Buffer rollup; null when no observation has landed yet.</summary>
    public RouteBufferSummaryDto? Buffer { get; init; }

    /// <summary>
    /// Pipeline rollup (per-step counters + lifetime totals). Null when
    /// no observation has landed yet, or when the route has no pipeline
    /// configured. Added in M.1c.1; older API consumers reading the type
    /// stay compatible because the field is nullable.
    /// </summary>
    public RoutePipelineSummaryDto? Pipeline { get; init; }

    /// <summary>Total state-transition count since gateway start.</summary>
    public long StateTransitionCount { get; init; }

    /// <summary>Total backpressure drops since gateway start.</summary>
    public long BackpressureDropCount { get; init; }

    /// <summary>
    /// Total points QUARANTINED since gateway start — skipped because the
    /// buffer could not serialize them. A data-quality signal (malformed value
    /// / unsupported type upstream), distinct from a backpressure drop; the
    /// rest of the route's data still flows.
    /// </summary>
    public long QuarantinedPointCount { get; init; }

    /// <summary>
    /// Route-level fault code, populated only when <see cref="StateName"/>
    /// is <c>"Faulted"</c> and the fault is a cross-record configuration
    /// error (e.g., <c>CONFIG.ROUTE_REFERENCES_MISSING_SOURCE</c>). Added
    /// in M.P2.1 phase 3; nullable to stay backward-compatible with
    /// earlier consumers.
    /// </summary>
    public string? LastErrorCode { get; init; }

    /// <summary>Operator-readable description of the route-level fault, when present.</summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>UTC timestamp the route-level fault was first observed.</summary>
    public DateTime? LastErrorAtUtc { get; init; }
}

/// <summary>Source-side summary nested under <see cref="RouteSummaryDto"/>.</summary>
public sealed record RouteSourceSummaryDto
{
    /// <summary>Source instance id.</summary>
    public required string SourceInstanceId { get; init; }

    /// <summary>Protocol name (e.g. "modbustcp", "focas2", "s7").</summary>
    public required string ProtocolName { get; init; }

    /// <summary>Adapter state name.</summary>
    public required string StateName { get; init; }

    /// <summary>Lifetime points-observed counter.</summary>
    public long PointsObserved { get; init; }

    /// <summary>UTC timestamp of the most recent point observed.</summary>
    public DateTime? LastPointAtUtc { get; init; }

    /// <summary>Most recent error code (e.g. "MODBUS.SOCKET_ERROR"), if any.</summary>
    public string? LastErrorCode { get; init; }

    /// <summary>Most recent error message, if any.</summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>UTC timestamp of the most recent error.</summary>
    public DateTime? LastErrorAtUtc { get; init; }
}

/// <summary>Sink-side summary nested under <see cref="RouteSummaryDto"/>.</summary>
public sealed record RouteSinkSummaryDto
{
    /// <summary>Sink instance id.</summary>
    public required string SinkInstanceId { get; init; }

    /// <summary>
    /// Destination protocol name (e.g. "mqtt", "opcua-server"), resolved from
    /// config by <see cref="Diagnostics.RouteInventoryBuilder"/>. Null when the
    /// builder had no config match — the operator-facing "type" beside the
    /// destination name on the Overview card. Nullable to stay backward-
    /// compatible with consumers that predate this field.
    /// </summary>
    public string? ProtocolName { get; init; }

    /// <summary>True when the sink last reported a publish failure and has not recovered.</summary>
    public bool IsDegraded { get; init; }

    /// <summary>True between SinkDraining and SinkRecovered events.</summary>
    public bool IsDraining { get; init; }

    /// <summary>Lifetime degraded-event count.</summary>
    public long DegradationEventCount { get; init; }

    /// <summary>Lifetime recovery-event count.</summary>
    public long RecoveryEventCount { get; init; }

    /// <summary>Adapter state name; null when the supervisor hasn't reported yet.</summary>
    public string? AdapterStateName { get; init; }

    /// <summary>Number of active sessions when the sink supports session tracking; null when not.</summary>
    public int? ActiveSessionCount { get; init; }

    /// <summary>Most recent error code, if any.</summary>
    public string? LastErrorCode { get; init; }

    /// <summary>Most recent error message, if any.</summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>UTC timestamp of the most recent error.</summary>
    public DateTime? LastErrorAtUtc { get; init; }
}

/// <summary>Buffer rollup nested under <see cref="RouteSummaryDto"/>.</summary>
public sealed record RouteBufferSummaryDto
{
    /// <summary>Buffer mode name ("InMemory" / "StoreAndForward").</summary>
    public required string Mode { get; init; }

    /// <summary>Current depth (messages in buffer).</summary>
    public required long CurrentDepth { get; init; }

    /// <summary>Lifetime enqueue count.</summary>
    public long TotalEnqueued { get; init; }

    /// <summary>Lifetime drain (consumed) count.</summary>
    public long TotalDrained { get; init; }

    /// <summary>Lifetime drop count (overflow + retention combined).</summary>
    public long TotalDropped { get; init; }

    /// <summary>On-disk size in bytes (StoreAndForward only; ~0 for InMemory).</summary>
    public long SizeBytes { get; init; }
}
