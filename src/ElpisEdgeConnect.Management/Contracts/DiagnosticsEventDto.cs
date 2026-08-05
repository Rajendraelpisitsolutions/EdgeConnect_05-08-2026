// ============================================================================
// File: Contracts/DiagnosticsEventDto.cs
// Purpose: Canonical operational-event shape for the management API.
//          One normalized DTO that covers every event source the
//          Studio + external consumers ever need to render —
//          route state changes, sink lifecycle (degraded / draining /
//          recovered), backpressure drops, and (future) config audit
//          entries.
//
//          Adopting a single normalized event surface NOW prevents
//          M.1c.2 (system-wide Diagnostics page) and later automation
//          features (alerting agent, EREMOS federation, ML correlation)
//          from each rewriting their own event flattening logic.
//
//          The DTO is the contract; Core's specialized per-source
//          event records (RouteStateChangedEvent / SinkEventEntry /
//          BackpressureDroppedEvent) stay as-is. The flattening +
//          severity-mapping + event-code assignment all happen in the
//          Management layer's RouteEventAggregator — never in Core.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.1
// ============================================================================

using System;
using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Severity classification for a <see cref="DiagnosticsEventDto"/>.
/// String-serialised on the wire ("Info" / "Warning" / "Error") via the
/// type-level <see cref="JsonStringEnumConverter"/> so wire shape is
/// stable regardless of the caller's JsonSerializerOptions.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiagnosticsSeverity
{
    /// <summary>Steady-state lifecycle event (route running, sink recovered).</summary>
    Info,

    /// <summary>Operational concern (sink degraded, backpressure dropping data).</summary>
    Warning,

    /// <summary>Hard fault (route faulted, irrecoverable adapter error).</summary>
    Error,
}

/// <summary>
/// One normalized operational event. Returned by
/// <c>GET /api/v1/routes/{id}/events</c> (M.1c.1) and (later)
/// <c>GET /api/v1/diagnostics/events</c> (M.1c.2). The shape stays
/// stable across event kinds — clients switch on <see cref="EventCode"/>,
/// not on the structural fields.
/// </summary>
public sealed record DiagnosticsEventDto
{
    /// <summary>UTC timestamp the event was observed by the supervisor.</summary>
    public required DateTime OccurredAtUtc { get; init; }

    /// <summary>
    /// Stable, parseable event identifier — e.g. <c>"ROUTE.STATE_CHANGED"</c>,
    /// <c>"SINK.DEGRADED"</c>, <c>"BUFFER.BACKPRESSURE_DROPPED"</c>. Follows
    /// the same <c>MODULE.CATEGORY[_SUBCATEGORY]</c> convention as
    /// EdgeConnect error codes (CLAUDE.md §7). The full catalog of values
    /// the API will emit is in <see cref="DiagnosticsEventCodes"/>.
    /// </summary>
    public required string EventCode { get; init; }

    /// <summary>Severity bucket for UI chip coloring and operator filtering.</summary>
    public required DiagnosticsSeverity Severity { get; init; }

    /// <summary>
    /// Operator-readable one-line summary. Human-only — automation and
    /// alerting code should branch on <see cref="EventCode"/>, never
    /// parse this string. Free-text shape allowed to evolve.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// The route this event belongs to. Null for gateway-scoped events
    /// (e.g. configuration audit entries — added in M.1c.2 — which belong
    /// to the gateway, not any route). Always populated for route, sink,
    /// and backpressure events.
    /// </summary>
    public string? RouteId { get; init; }

    /// <summary>Sink instance id when the event is sink-scoped; null otherwise.</summary>
    public string? SinkInstanceId { get; init; }

    /// <summary>
    /// Operator-readable actor who triggered the event, when known.
    /// Populated for configuration audit entries from
    /// <c>ConfigurationAuditEntry.Actor</c>; null for system-emitted
    /// route / sink / backpressure events. The same field will populate
    /// for future operator-initiated events (acknowledged-alert,
    /// forced-reconnect, manual rollback, …).
    /// </summary>
    public string? Actor { get; init; }

    /// <summary>
    /// Identity of the gateway that emitted this event. Populated by the
    /// API endpoint from <c>GatewaySettings.GatewayId</c>. Redundant for
    /// single-gateway consumers (always the same value); critical for
    /// future fleet / EREMOS / cloud-relay aggregation where events from
    /// multiple gateways are merged. Nullable so events deserialized from
    /// older snapshots or partial federation payloads where origin
    /// wasn't recorded don't fail validation.
    /// </summary>
    public string? GatewayId { get; init; }

    /// <summary>
    /// Route state before the transition. Populated for
    /// <see cref="DiagnosticsEventCodes.RouteStateChanged"/>; null otherwise.
    /// </summary>
    public string? FromState { get; init; }

    /// <summary>
    /// Route state after the transition. Populated for
    /// <see cref="DiagnosticsEventCodes.RouteStateChanged"/>; null otherwise.
    /// </summary>
    public string? ToState { get; init; }

    /// <summary>
    /// Number of points dropped, for
    /// <see cref="DiagnosticsEventCodes.BackpressureDropped"/>; null otherwise.
    /// </summary>
    public long? DroppedCount { get; init; }

    /// <summary>
    /// Operational incident correlation id. Reserved for grouping
    /// cascading events (e.g. a single sink-reconnect storm producing
    /// SINK.DEGRADED → SINK.DRAINING → SINK.RECOVERED into one
    /// operator-visible incident). <strong>Null in M.1c.1</strong> — Core's
    /// route + sink supervisors don't carry correlation context today.
    /// Will populate when incident-stitching ships in a later milestone.
    /// Shipping the field now lets the wire contract stay stable when
    /// it lights up; consumers (alerting agent, EREMOS bridge) can already
    /// code defensively against the nullable.
    /// </summary>
    public string? CorrelationId { get; init; }
}
