// ============================================================================
// File: Routing/ReplaySessionState.cs
// Purpose: The COMPOSITE, atomic replay-session-state capture. Birth requires the
//          replay boundary (H) and the birth snapshot to be captured in ONE
//          transaction — two independent reads (a boundary capture + a later
//          snapshot read) cannot guarantee that, because an append can land
//          between them. IReplaySessionStateProvider returns a coherent
//          (boundary, snapshot) pair; the snapshot carries its schema generation.
//
// The state objects are the ATOMIC UNIT and are carried INTACT through the
// lifecycle records (ReplaySessionStart/Rebirth/Cutover) — the lifecycle never
// re-decomposes them into independently-constructible boundary + snapshot fields,
// so a caller cannot pair a boundary from transaction A with a snapshot from
// transaction B. The Create factories additionally verify the pair is coherent
// (every snapshot value predates the cutoff). The primitive constructors are private.
// Reference: docs/decisions/0036-*.md Rule 5; plan v2.3 §3; PR #180 review rounds 1-2.
//            The persisted implementation (SqliteRouteStore) is K1.2.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// The coherent start state for a replay session: the replay boundary (H) and the
/// birth snapshot as of that boundary, captured atomically. Built via
/// <see cref="Create"/>, which verifies coherence; the primitive constructor is private.
/// </summary>
public sealed class ReplaySessionStartState
{
    private ReplaySessionStartState(ReplayBoundary boundary, LatestValueSnapshot snapshot)
    {
        Boundary = boundary;
        Snapshot = snapshot;
    }

    /// <summary>The replay boundary (H) captured with the snapshot.</summary>
    public ReplayBoundary Boundary { get; }

    /// <summary>The birth snapshot as of the boundary (carries its schema generation).</summary>
    public LatestValueSnapshot Snapshot { get; }

    /// <summary>
    /// Build a coherent start state. Every snapshot value must have been appended
    /// strictly before the boundary cutoff (<c>RouteBufferSequence &lt; Boundary.CutoffExclusive</c>);
    /// otherwise the pair does not describe a single instant.
    /// </summary>
    /// <param name="boundary">The captured boundary (H).</param>
    /// <param name="snapshot">The birth snapshot as of the boundary.</param>
    /// <returns>The coherent start state.</returns>
    /// <exception cref="ArgumentException">Thrown when a snapshot value is at or beyond the cutoff.</exception>
    public static ReplaySessionStartState Create(ReplayBoundary boundary, LatestValueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var max = snapshot.MaxRouteBufferSequence;
        if (max is not null && max >= boundary.CutoffExclusive)
        {
            throw new ArgumentException(
                $"Snapshot value at sequence {max} is not strictly below the boundary cutoff {boundary.CutoffExclusive}; the (boundary, snapshot) pair is not coherent.",
                nameof(snapshot));
        }

        return new ReplaySessionStartState(boundary, snapshot);
    }
}

/// <summary>
/// The coherent catch-up state: the catch-up cutoff (C) and the snapshot as of it,
/// captured atomically. Built via <see cref="Create"/>; the primitive constructor is private.
/// </summary>
public sealed class ReplaySessionCutoverState
{
    private ReplaySessionCutoverState(long cutoffExclusive, LatestValueSnapshot snapshot)
    {
        CutoffExclusive = cutoffExclusive;
        Snapshot = snapshot;
    }

    /// <summary>The catch-up cutoff (C, buffer sequence).</summary>
    public long CutoffExclusive { get; }

    /// <summary>The snapshot as of the cutoff (carries its schema generation).</summary>
    public LatestValueSnapshot Snapshot { get; }

    /// <summary>
    /// Build a coherent catch-up state. The cutoff must be non-negative and every
    /// snapshot value must have been appended strictly before it.
    /// </summary>
    /// <param name="cutoffExclusive">The catch-up cutoff (C).</param>
    /// <param name="snapshot">The snapshot as of the cutoff.</param>
    /// <returns>The coherent catch-up state.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the cutoff is negative.</exception>
    /// <exception cref="ArgumentException">Thrown when a snapshot value is at or beyond the cutoff.</exception>
    public static ReplaySessionCutoverState Create(long cutoffExclusive, LatestValueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (cutoffExclusive < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cutoffExclusive), cutoffExclusive, "CutoffExclusive must be non-negative.");
        }

        var max = snapshot.MaxRouteBufferSequence;
        if (max is not null && max >= cutoffExclusive)
        {
            throw new ArgumentException(
                $"Snapshot value at sequence {max} is not strictly below the catch-up cutoff {cutoffExclusive}; the pair is not coherent.",
                nameof(snapshot));
        }

        return new ReplaySessionCutoverState(cutoffExclusive, snapshot);
    }
}

/// <summary>
/// The composite capability that produces a coherent replay-session state in a
/// single transaction. This — NOT a separate boundary read plus a separate snapshot
/// read — is the birth correctness path. <see cref="IReplayBoundaryProvider"/>
/// remains useful for in-memory/non-snapshot consumers but is <b>insufficient by
/// itself</b> to construct a replay-session birth.
/// </summary>
public interface IReplaySessionStateProvider
{
    /// <summary>
    /// Atomically capture the coherent birth state — the replay boundary and the birth
    /// snapshot (as of that boundary) — in one transaction. This is the authoritative
    /// capture used for BOTH a new-session birth (<c>BeginReplaySessionAsync</c>) and a
    /// same-session rebirth (<c>RebirthAsync</c>).
    /// </summary>
    /// <param name="routeId">The route.</param>
    /// <param name="sinkId">The sink whose cursor defines the boundary.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The coherent birth state.</returns>
    ValueTask<ReplaySessionStartState> CaptureBirthStateAsync(string routeId, string sinkId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically capture the coherent catch-up state — the catch-up cutoff and the
    /// snapshot as of it — in one transaction.
    /// </summary>
    /// <param name="routeId">The route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The coherent catch-up state.</returns>
    ValueTask<ReplaySessionCutoverState> CaptureCutoverAsync(string routeId, CancellationToken cancellationToken);
}
