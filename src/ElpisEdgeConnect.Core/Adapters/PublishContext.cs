// ============================================================================
// File: Adapters/PublishContext.cs
// Purpose: Per-batch replay context supplied to an IReplayAwareSinkAdapter so it
//          can distinguish historical replay from live data. Produced by the Core
//          replay-aware route path, which splits boundary-straddling batches so each
//          delivered batch is entirely one ReplayPhase. Carries BOTH watermarks of
//          the two-watermark cutover — H (the birth cutoff) and C (the catch-up
//          cutoff, null until captured) — so the phase ranges are exactly:
//              Replay:  seq < H
//              CatchUp: H <= seq < C
//              Live:    seq >= C
//          Private constructor + validated Create: the single-cutoff shape is not
//          publicly constructible.
// Reference: docs/decisions/0036-*.md Rule 6; plan v2.3 §1; PR #180 review rounds 1-2.
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// The replay context for a single published batch. The batch is guaranteed to be
/// entirely within one <see cref="Phase"/> (Core splits straddling batches at the
/// relevant watermark before calling). The sink consumes <see cref="Phase"/> as the
/// sole phase authority. Constructed via <see cref="Create"/>; the primitive
/// constructor is private, so an inconsistent context cannot be built.
/// </summary>
public sealed record PublishContext
{
    private PublishContext(
        string routeId, ReplaySessionId sessionId, ReplayEpochId epoch, ReplayPhase phase,
        long replayCutoffExclusive, long? catchUpCutoffExclusive, long batchFirstSequence, long batchLastSequence)
    {
        RouteId = routeId;
        SessionId = sessionId;
        Epoch = epoch;
        Phase = phase;
        ReplayCutoffExclusive = replayCutoffExclusive;
        CatchUpCutoffExclusive = catchUpCutoffExclusive;
        BatchFirstSequence = batchFirstSequence;
        BatchLastSequence = batchLastSequence;
    }

    /// <summary>The route this batch belongs to.</summary>
    public string RouteId { get; }

    /// <summary>The replay session this batch belongs to (retained across a same-session rebirth).</summary>
    public ReplaySessionId SessionId { get; }

    /// <summary>
    /// The epoch this batch belongs to. The sink honors the batch only when the epoch
    /// matches its current successful birth; a batch from a pre-rebirth epoch is rejected.
    /// </summary>
    public ReplayEpochId Epoch { get; }

    /// <summary>The (single) replay phase of this batch.</summary>
    public ReplayPhase Phase { get; }

    /// <summary>The birth cutoff <b>H</b> (exclusive): points with buffer sequence &lt; H are historical replay.</summary>
    public long ReplayCutoffExclusive { get; }

    /// <summary>
    /// The catch-up cutoff <b>C</b> (exclusive), or null before it is captured. Points
    /// with sequence in <c>[H, C)</c> are catch-up; points with sequence &gt;= C are
    /// live. Required (non-null) for a <see cref="ReplayPhase.CatchUp"/> or
    /// <see cref="ReplayPhase.Live"/> batch.
    /// </summary>
    public long? CatchUpCutoffExclusive { get; }

    /// <summary>Buffer sequence of the first point in this batch.</summary>
    public long BatchFirstSequence { get; }

    /// <summary>Buffer sequence of the last point in this batch.</summary>
    public long BatchLastSequence { get; }

    /// <summary>
    /// Create a validated context. Enforces: non-empty route; a defined
    /// <see cref="ReplayPhase"/>; non-negative epoch, H and sequences;
    /// <see cref="BatchFirstSequence"/> &lt;= <see cref="BatchLastSequence"/>; and, when
    /// present, <c>C &gt;= H</c>. The batch must fall entirely inside its phase range:
    /// Replay <c>last &lt; H</c>; CatchUp (C required) <c>first &gt;= H &amp;&amp; last &lt; C</c>;
    /// Live (C required) <c>first &gt;= C</c>.
    /// </summary>
    /// <param name="routeId">The route.</param>
    /// <param name="sessionId">The replay session.</param>
    /// <param name="epoch">The epoch this batch belongs to.</param>
    /// <param name="phase">The single phase of the batch.</param>
    /// <param name="replayCutoffExclusive">The birth cutoff H (exclusive).</param>
    /// <param name="catchUpCutoffExclusive">The catch-up cutoff C (exclusive), or null before capture.</param>
    /// <param name="batchFirstSequence">First buffer sequence.</param>
    /// <param name="batchLastSequence">Last buffer sequence.</param>
    /// <returns>The validated context.</returns>
    /// <exception cref="ArgumentException">Thrown when the route is empty or the phase undefined.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value is negative, the range is inverted, C &lt; H, a required C is missing, or the batch falls outside its phase range.</exception>
    public static PublishContext Create(
        string routeId,
        ReplaySessionId sessionId,
        ReplayEpochId epoch,
        ReplayPhase phase,
        long replayCutoffExclusive,
        long? catchUpCutoffExclusive,
        long batchFirstSequence,
        long batchLastSequence)
    {
        if (string.IsNullOrEmpty(routeId))
        {
            throw new ArgumentException("RouteId must be non-empty.", nameof(routeId));
        }

        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentException($"Unknown ReplayPhase '{(int)phase}'.", nameof(phase));
        }

        Require(replayCutoffExclusive >= 0, nameof(replayCutoffExclusive), replayCutoffExclusive, "ReplayCutoffExclusive (H) must be non-negative.");
        Require(batchFirstSequence >= 0, nameof(batchFirstSequence), batchFirstSequence, "BatchFirstSequence must be non-negative.");
        Require(batchLastSequence >= 0, nameof(batchLastSequence), batchLastSequence, "BatchLastSequence must be non-negative.");

        if (batchFirstSequence > batchLastSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(batchFirstSequence), batchFirstSequence,
                $"BatchFirstSequence must be <= BatchLastSequence ({batchLastSequence}).");
        }

        if (catchUpCutoffExclusive is { } c)
        {
            Require(c >= 0, nameof(catchUpCutoffExclusive), c, "CatchUpCutoffExclusive (C) must be non-negative.");
            if (c < replayCutoffExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(catchUpCutoffExclusive), c,
                    $"CatchUpCutoffExclusive (C) must be >= ReplayCutoffExclusive (H = {replayCutoffExclusive}).");
            }
        }

        switch (phase)
        {
            case ReplayPhase.Replay:
                if (batchLastSequence >= replayCutoffExclusive)
                {
                    throw new ArgumentOutOfRangeException(nameof(batchLastSequence), batchLastSequence,
                        $"A Replay-phase batch must be strictly below H ({replayCutoffExclusive}).");
                }

                break;

            case ReplayPhase.CatchUp:
                RequireCatchUp(catchUpCutoffExclusive, phase, out var cc);
                if (batchFirstSequence < replayCutoffExclusive)
                {
                    throw new ArgumentOutOfRangeException(nameof(batchFirstSequence), batchFirstSequence,
                        $"A CatchUp-phase batch must be at or above H ({replayCutoffExclusive}).");
                }

                if (batchLastSequence >= cc)
                {
                    throw new ArgumentOutOfRangeException(nameof(batchLastSequence), batchLastSequence,
                        $"A CatchUp-phase batch must be strictly below C ({cc}).");
                }

                break;

            case ReplayPhase.Live:
                RequireCatchUp(catchUpCutoffExclusive, phase, out var cl);
                if (batchFirstSequence < cl)
                {
                    throw new ArgumentOutOfRangeException(nameof(batchFirstSequence), batchFirstSequence,
                        $"A Live-phase batch must be at or above C ({cl}).");
                }

                break;
        }

        return new PublishContext(
            routeId, sessionId, epoch, phase, replayCutoffExclusive, catchUpCutoffExclusive, batchFirstSequence, batchLastSequence);
    }

    private static void RequireCatchUp(long? catchUpCutoffExclusive, ReplayPhase phase, out long value)
    {
        if (catchUpCutoffExclusive is not { } c)
        {
            throw new ArgumentOutOfRangeException(nameof(catchUpCutoffExclusive), catchUpCutoffExclusive,
                $"A {phase}-phase batch requires the catch-up cutoff C to be captured.");
        }

        value = c;
    }

    private static void Require(bool condition, string paramName, long value, string message)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(paramName, value, message);
        }
    }
}
