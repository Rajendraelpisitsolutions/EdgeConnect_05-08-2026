// ============================================================================
// File: Buffer/IReplayBoundaryProvider.cs
// Purpose: Optional, protocol-neutral buffer capability that atomically reports a
//          sink's first pending sequence and the current append cutoff, so a
//          replay-aware route path can classify each batch as historical backlog
//          vs. new data WITHOUT changing the LOCKED IMessageBuffer contract.
// Reference: docs/decisions/0036-*.md Rule 6; plan v2.3 §1. Buffer implementations
//            (in-memory, SQLite) are added in K1.2 (this file is the contract only).
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// An atomically-captured replay boundary for one sink: where the sink's cursor
/// currently sits, and the exclusive sequence cutoff separating backlog that
/// existed at capture from data appended afterward.
/// <para>
/// A boundary is NECESSARY but <b>not sufficient by itself</b> to construct a
/// replay-session birth: birth also needs the coherent latest-value snapshot as of
/// the same instant. Capturing the boundary and the snapshot in two separate reads
/// allows an append to land between them, so the birth path uses the composite
/// <c>IReplaySessionStateProvider</c> (Routing), which captures both atomically.
/// This provider remains useful for in-memory/non-snapshot phase classification.
/// </para>
/// </summary>
/// <remarks>
/// The primitive constructor is PRIVATE; callers must use <see cref="Create"/>, which
/// enforces the invariants. <c>default(ReplayBoundary)</c> is (0, 0) — a valid,
/// safe empty boundary — so a defaulted value cannot represent an invalid state.
/// </remarks>
public readonly struct ReplayBoundary : IEquatable<ReplayBoundary>
{
    private ReplayBoundary(long firstPendingSequence, long cutoffExclusive)
    {
        FirstPendingSequence = firstPendingSequence;
        CutoffExclusive = cutoffExclusive;
    }

    /// <summary>
    /// The sink's current cursor — the first sequence it has not yet consumed. Equal
    /// to <see cref="CutoffExclusive"/> when the sink is fully caught up.
    /// </summary>
    public long FirstPendingSequence { get; }

    /// <summary>
    /// The append cutoff at capture time. Every point already buffered has sequence
    /// &lt; this value; every point appended after capture has sequence &gt;= this
    /// value. An EXCLUSIVE cutoff avoids an off-by-one high-water mark and represents
    /// the empty-buffer case naturally (<c>FirstPendingSequence == CutoffExclusive</c>).
    /// </summary>
    public long CutoffExclusive { get; }

    /// <summary>
    /// True when backlog existed at capture — there are buffered points
    /// (<see cref="FirstPendingSequence"/> &lt; <see cref="CutoffExclusive"/>) that
    /// must be replayed as historical before the sink is live.
    /// </summary>
    public bool HasBacklog => FirstPendingSequence < CutoffExclusive;

    /// <summary>
    /// Create a validated boundary: both sequences must be non-negative and the cursor
    /// must not sit past the cutoff (<paramref name="firstPendingSequence"/> &lt;=
    /// <paramref name="cutoffExclusive"/>).
    /// </summary>
    /// <param name="firstPendingSequence">The sink cursor (first unconsumed sequence).</param>
    /// <param name="cutoffExclusive">The exclusive append cutoff.</param>
    /// <returns>The validated boundary.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value is negative or the cursor exceeds the cutoff.</exception>
    public static ReplayBoundary Create(long firstPendingSequence, long cutoffExclusive)
    {
        if (firstPendingSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstPendingSequence), firstPendingSequence,
                "FirstPendingSequence must be non-negative.");
        }

        if (cutoffExclusive < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cutoffExclusive), cutoffExclusive,
                "CutoffExclusive must be non-negative.");
        }

        if (firstPendingSequence > cutoffExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(firstPendingSequence), firstPendingSequence,
                $"FirstPendingSequence must be <= CutoffExclusive ({cutoffExclusive}).");
        }

        return new ReplayBoundary(firstPendingSequence, cutoffExclusive);
    }

    /// <inheritdoc/>
    public bool Equals(ReplayBoundary other) =>
        FirstPendingSequence == other.FirstPendingSequence && CutoffExclusive == other.CutoffExclusive;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ReplayBoundary other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(FirstPendingSequence, CutoffExclusive);

    /// <summary>Value equality.</summary>
    public static bool operator ==(ReplayBoundary left, ReplayBoundary right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(ReplayBoundary left, ReplayBoundary right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => $"[{FirstPendingSequence}, {CutoffExclusive})";
}

/// <summary>
/// Optional buffer capability: atomically capture the <see cref="ReplayBoundary"/>
/// for a sink. Implemented by concrete buffers alongside <see cref="IMessageBuffer"/>;
/// each provides its own correct synchronization (in-memory: under the ring lock;
/// SQLite: in one read transaction). NOT part of the locked
/// <see cref="IMessageBuffer"/> contract.
/// </summary>
public interface IReplayBoundaryProvider
{
    /// <summary>
    /// Atomically capture the sink's first pending sequence and the current append
    /// cutoff. Does NOT advance the sink cursor. After capture, every
    /// subsequently-appended point has sequence &gt;= the returned
    /// <see cref="ReplayBoundary.CutoffExclusive"/>.
    /// </summary>
    /// <param name="sinkId">The sink whose cursor to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The captured boundary.</returns>
    ValueTask<ReplayBoundary> CaptureReplayBoundaryAsync(string sinkId, CancellationToken cancellationToken);
}
