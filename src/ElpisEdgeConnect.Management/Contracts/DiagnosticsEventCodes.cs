// ============================================================================
// File: Contracts/DiagnosticsEventCodes.cs
// Purpose: Canonical event-code catalog — string constants for the
//          DiagnosticsEventDto.EventCode field. Centralised so producers
//          (RouteEventAggregator), consumers (Razor pages, future
//          alerting agent), and tests share one source of truth and
//          never regress to magic strings.
//
//          Naming convention mirrors EdgeConnect error codes:
//             MODULE.CATEGORY[_SUBCATEGORY]
//          all SCREAMING_SNAKE_CASE, dot-separated module prefix.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.1
//            CLAUDE.md §7 (code style — error code naming)
// ============================================================================

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// String constants for the catalog of <see cref="DiagnosticsEventDto.EventCode"/>
/// values the management API emits. Add new codes here; never inline
/// the strings in producer or consumer code.
/// </summary>
/// <remarks>
/// <para><strong>Reserved namespace prefixes</strong> — please respect these
/// when adding new codes. Namespace ownership prevents collisions as the
/// catalog grows and the event surface federates across gateways.</para>
/// <list type="bullet">
///   <item><c>ROUTE.*</c>    — route lifecycle (state changes, configuration).</item>
///   <item><c>SINK.*</c>     — sink lifecycle (degraded / draining / recovered).</item>
///   <item><c>SOURCE.*</c>   — source lifecycle. Reserved; Core's <c>IDiagnosticsService</c>
///                              does not yet expose a source-side event log
///                              (see M.1c.1 smoke-test follow-up).</item>
///   <item><c>BUFFER.*</c>   — buffer events (backpressure drops, retention evictions).</item>
///   <item><c>CONFIG.*</c>   — configuration audit (applied / rolled-back / draft.*).</item>
///   <item><c>SYSTEM.*</c>   — gateway lifecycle. Reserved for future use
///                              (gateway started / stopped, license-warning,
///                              time-skew, config-load-failed).</item>
///   <item><c>SECURITY.*</c> — security events. Reserved for future use
///                              (auth-failure, cert-error, hashed-cred rotation).</item>
/// </list>
/// </remarks>
public static class DiagnosticsEventCodes
{
    // ─── ROUTE.* ─────────────────────────────────────────────────────────

    /// <summary>A route transitioned between two <c>RouteState</c> values.</summary>
    public const string RouteStateChanged = "ROUTE.STATE_CHANGED";

    // ─── SINK.* ──────────────────────────────────────────────────────────

    /// <summary>A sink reported a publish failure and entered the degraded set.</summary>
    public const string SinkDegraded = "SINK.DEGRADED";

    /// <summary>A sink began draining its backlog after recovering connectivity.</summary>
    public const string SinkDraining = "SINK.DRAINING";

    /// <summary>A sink finished draining and returned to live publishing.</summary>
    public const string SinkRecovered = "SINK.RECOVERED";

    // ─── BUFFER.* ────────────────────────────────────────────────────────

    /// <summary>
    /// The backpressure controller dropped one or more points. The
    /// <see cref="DiagnosticsEventDto.DroppedCount"/> field carries the
    /// count; the human <see cref="DiagnosticsEventDto.Summary"/> carries
    /// the dimension (channel / buffer-capacity / buffer-retention).
    /// </summary>
    public const string BackpressureDropped = "BUFFER.BACKPRESSURE_DROPPED";

    /// <summary>
    /// The buffer skipped a single point it could not serialize
    /// ("quarantine-and-continue"). A data-quality signal — the
    /// <see cref="DiagnosticsEventDto.Summary"/> carries the tag and the
    /// serialization error. Distinct from <see cref="BackpressureDropped"/>,
    /// which is a capacity signal.
    /// </summary>
    public const string RoutePointQuarantined = "BUFFER.POINT_QUARANTINED";

    // ─── CONFIG.* (M.1c.2) ───────────────────────────────────────────────

    /// <summary>A draft configuration was created and persisted.</summary>
    public const string ConfigDraftCreated = "CONFIG.DRAFT_CREATED";

    /// <summary>A draft was discarded without being applied.</summary>
    public const string ConfigDraftDiscarded = "CONFIG.DRAFT_DISCARDED";

    /// <summary>A draft was successfully applied as the new current configuration.</summary>
    public const string ConfigApplied = "CONFIG.APPLIED";

    /// <summary>The active configuration was rolled back to a previous version.</summary>
    public const string ConfigRolledBack = "CONFIG.ROLLED_BACK";

    // ─── GATEWAY.* — boot-time signals (M.2b.3.1) ─────────────────────────

    /// <summary>
    /// FOCAS2 demo mode was activated at gateway startup via
    /// <c>EDGECONNECT_FOCAS2_FAKE_MODE</c>. Gateway-scoped (no RouteId),
    /// severity Critical. Persists for the process lifetime in
    /// <c>IGatewayStartupEventStore</c>.
    /// </summary>
    public const string Focas2FakeModeActivated = "GATEWAY.FOCAS2_FAKE_MODE_ACTIVATED";
}
