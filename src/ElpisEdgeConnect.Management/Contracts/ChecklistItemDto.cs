// ============================================================================
// File: Contracts/ChecklistItemDto.cs
// Purpose: One row in the commissioning checklist surface. Each row
//          represents a single yes/no question a field engineer
//          answers during handover, evaluated server-side against
//          existing diagnostic signals.
//
//          Wire-shape contract:
//             * Status is a 4-state enum (string on the wire) so
//               consumers tell "broken" (Fail) from "fresh, retry"
//               (Pending) from "doesn't apply here" (NotApplicable).
//             * Id is stable across catalog versions and matches the
//               documented catalog entries (DF-1, GW-3, etc.). Stable
//               so future support tooling and customer handover
//               artifacts can reference checks by ID.
//             * Detail is operator-readable — confirmation when Pass,
//               actionable next step when Fail.
//             * Link is an optional drill-in URL to the surface where
//               the operator would fix the problem (typically /sinks
//               or /routes detail pages).
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1d
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Status of a single checklist item. Wire-serialized as a string
/// ("Pass" / "Fail" / "NotApplicable" / "Pending") via the type-level
/// <see cref="JsonStringEnumConverter"/>, so consumers parse a stable
/// label rather than relying on enum integer ordering.
/// </summary>
/// <remarks>
/// <para>The 4-state model is deliberate. Industrial operators interpret
/// <c>Pending</c> and <c>NotApplicable</c> very differently:</para>
/// <list type="bullet">
///   <item><c>Pending</c> = "the gateway just started; wait for data."
///         Fresh-start scenario; retry the check later.</item>
///   <item><c>NotApplicable</c> = "this check doesn't apply to your
///         gateway's configuration." e.g. checking OPC UA client
///         sessions on a gateway with no OPC UA Server sinks.</item>
/// </list>
/// <para>Collapsing them into a single "skip" state would make fresh
/// gateways look defective and configured-but-not-applicable scenarios
/// look like operator action is needed when it isn't.</para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChecklistStatus
{
    /// <summary>Check verified positive against system state.</summary>
    Pass,

    /// <summary>Check verified negative; operator action required.</summary>
    Fail,

    /// <summary>Check doesn't apply to this gateway's configuration.</summary>
    NotApplicable,

    /// <summary>Insufficient data yet (fresh start, observation window not elapsed).</summary>
    Pending,
}

/// <summary>
/// One row of the commissioning checklist. The Studio renders a list
/// of these grouped by <see cref="Category"/>.
/// </summary>
public sealed record ChecklistItemDto
{
    /// <summary>
    /// Stable check identifier (e.g. <c>"DF-1"</c>, <c>"GW-3"</c>). Matches
    /// the documented catalog and remains stable across catalog version
    /// bumps so support tooling can reference checks by id.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Operator-facing category — one of <c>"Gateway"</c>, <c>"Data flow"</c>,
    /// <c>"Recoverability"</c>, <c>"Connectivity"</c>. Future categories
    /// (<c>Security</c>, <c>Performance</c>, <c>Redundancy</c>) are
    /// reserved in <see cref="Diagnostics"/>-related docs but not emitted
    /// in schemaVersion=1.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>One-line operator-readable title for the check.</summary>
    public required string Title { get; init; }

    /// <summary>The evaluated status.</summary>
    public required ChecklistStatus Status { get; init; }

    /// <summary>
    /// Operator-readable detail string. When <see cref="Status"/> is
    /// <see cref="ChecklistStatus.Pass"/>, a brief positive confirmation
    /// ("1 route configured"). When <see cref="ChecklistStatus.Fail"/>,
    /// an actionable next step ("opcua-demo is degraded: MQTT.NOT_CONNECTED").
    /// Null when no additional context is meaningful.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Optional drill-in URL pointing to the surface where the operator
    /// can investigate or fix a failed check. Typically <c>"/sinks"</c>,
    /// <c>"/routes"</c>, or a specific instance detail page. Null when
    /// no obvious drill-in target exists.
    /// </summary>
    public string? Link { get; init; }
}
