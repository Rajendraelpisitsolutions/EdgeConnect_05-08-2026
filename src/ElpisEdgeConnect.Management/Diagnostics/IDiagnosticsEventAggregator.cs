// ============================================================================
// File: Diagnostics/IDiagnosticsEventAggregator.cs
// Purpose: System-wide event aggregator — the seam between Core's
//          per-route ring buffers, the configuration manager's audit
//          log, and the M.1c.2 Diagnostics page.
//
//          Distinct from M.1c.1's IRouteEventAggregator: that one is
//          route-scoped, this one aggregates across the whole gateway
//          and merges in the configuration audit stream. Together they
//          give the management surface a uniform DiagnosticsEventDto
//          stream — exactly the "single normalized event model"
//          architecture review recommendation.
//
//          The seam is in Management, not Core, per locked rule #1
//          (Core stays protocol/presentation agnostic).
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.2
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Contracts;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>
/// Aggregates the gateway-wide event surface — every route's events
/// plus the configuration audit log — into one normalized stream of
/// <see cref="DiagnosticsEventDto"/>s with server-side filtering and
/// retention metadata. Also exposes the audit-log hash-chain
/// verification result for the compliance banner.
/// </summary>
public interface IDiagnosticsEventAggregator
{
    /// <summary>
    /// Return events matching <paramref name="filter"/> across every
    /// route plus the configuration audit log, sorted desc by
    /// <c>OccurredAtUtc</c> and capped at <c>filter.EffectiveLimit</c>.
    /// The response carries retention metadata so the UI can show
    /// "147 of ~12,000 events" rather than just a flat list.
    /// </summary>
    /// <remarks>
    /// Filtering is applied AND-wise across all set fields. When
    /// <c>filter.RouteId</c> is set, audit entries (which have null
    /// <c>RouteId</c>) are skipped as an optimization.
    /// </remarks>
    Task<DiagnosticsEventsResponse> GetRecentEventsAsync(
        DiagnosticsEventFilter filter,
        CancellationToken ct);

    /// <summary>
    /// Re-verify the SHA-256 hash chain on the configuration audit log
    /// from genesis. Returns a structured status — verified-or-not, count
    /// of entries checked, failure reason if the chain broke. Never throws
    /// for chain-corruption; instead returns <c>Verified = false</c>
    /// with the reason.
    /// </summary>
    /// <remarks>
    /// This is an expensive operation (full audit log scan + SHA-256 per
    /// entry); the API hosts it on a separate endpoint so the rapidly-
    /// polled events endpoint doesn't re-verify on every refresh.
    /// </remarks>
    Task<AuditChainStatus> VerifyAuditChainAsync(CancellationToken ct);
}
