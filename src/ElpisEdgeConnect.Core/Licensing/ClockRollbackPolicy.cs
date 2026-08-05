// ============================================================================
// File: Licensing/ClockRollbackPolicy.cs
// Purpose: Defeat the "wind the system clock back" license bypass.
//
//          Expiry is evaluated against the system clock, so an expired license
//          would come back to life if the machine's date were moved back before
//          its expiresAt. To stop that, the runtime keeps a persisted
//          HIGH-WATER MARK ("anchor") of the latest UTC time it has ever
//          observed. Time is expected to move only FORWARD: if the clock is now
//          meaningfully earlier than the anchor, it has been rolled back and the
//          license must not be honoured.
//
//          This file is the pure decision logic (no I/O), so it is fully
//          unit-testable; persistence of the anchor lives in the host.
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Detects system-clock rollback, which would otherwise resurrect an expired
/// license. Pure and side-effect free.
/// </summary>
public static class ClockRollbackPolicy
{
    /// <summary>
    /// Slack allowed for legitimate backward clock corrections (NTP sync, small
    /// drift). Anything further back than this is treated as tampering.
    /// </summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// True when <paramref name="nowUtc"/> is meaningfully earlier than the
    /// highest time previously observed (<paramref name="anchorUtc"/>) — i.e.
    /// the clock has been wound back. False when there is no anchor yet (first
    /// run) or the clock has only moved forward / drifted within tolerance.
    /// </summary>
    public static bool IsRollback(DateTime nowUtc, DateTime? anchorUtc, TimeSpan tolerance)
    {
        if (anchorUtc is not { } anchor)
        {
            return false; // no history yet — nothing to compare against
        }
        return nowUtc < anchor - tolerance;
    }

    /// <summary>
    /// The new high-water mark. The anchor never moves backwards, and — crucially
    /// — it never advances FORWARD by more than the real time that actually
    /// elapsed (<paramref name="realElapsed"/>, measured from a monotonic clock)
    /// plus a little slack. This stops a wall-clock that is set far into the
    /// FUTURE from "poisoning" the anchor: without the cap, the anchor would jump
    /// to the fake future time, and then correcting the clock back to the real
    /// time would look like a rollback forever.
    /// </summary>
    /// <param name="nowUtc">Current wall-clock time.</param>
    /// <param name="anchorUtc">Previous anchor, or null on first run.</param>
    /// <param name="realElapsed">Monotonic time elapsed since the anchor was last written.</param>
    /// <param name="tolerance">Slack allowed on top of the real elapsed time.</param>
    public static DateTime Advance(DateTime nowUtc, DateTime? anchorUtc, TimeSpan realElapsed, TimeSpan tolerance)
    {
        if (anchorUtc is not { } anchor)
        {
            return nowUtc; // first run — trust the current time
        }

        // Cap how far forward the anchor may move to the real elapsed time (+ slack).
        var maxAnchor = anchor + (realElapsed > TimeSpan.Zero ? realElapsed : TimeSpan.Zero) + tolerance;
        var capped = nowUtc < maxAnchor ? nowUtc : maxAnchor;

        // Never move backwards.
        return capped > anchor ? capped : anchor;
    }

    /// <summary>
    /// True when the clock is earlier than the day the license was issued — a
    /// license can never legitimately be in use before it existed. A cheap,
    /// anchor-independent sanity check that catches gross date tampering even on
    /// a fresh install with no anchor yet.
    /// </summary>
    public static bool IsBeforeIssue(DateTime nowUtc, LicenseInfo? license) =>
        license is not null && nowUtc.Date < license.IssuedAt.Date;
}
