// ============================================================================
// File: Licensing/LicenseExpirationTracker.cs
// Purpose: Pure function that maps (now, expiresAt, policy) to a status,
//          days remaining, and warning level. Stateless and deterministic
//          so tests can pin every boundary.
// Reference: ARCHITECTURE_BLUEPRINT.md §7.4
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Snapshot returned by <see cref="LicenseExpirationTracker.Evaluate"/>.
/// </summary>
public sealed record LicenseExpirationSnapshot
{
    /// <summary>Current lifecycle status.</summary>
    public required LicenseStatus Status { get; init; }

    /// <summary>
    /// Days from <c>now</c> to <c>expiresAt</c>. Negative once past expiry.
    /// Truncated toward zero (matches <see cref="TimeSpan.Days"/>).
    /// </summary>
    public required int DaysUntilExpiry { get; init; }

    /// <summary>
    /// Time remaining in the grace period. Set only when
    /// <see cref="Status"/> is <see cref="LicenseStatus.InGracePeriod"/>;
    /// <c>null</c> otherwise.
    /// </summary>
    public TimeSpan? RemainingGrace { get; init; }

    /// <summary>
    /// Warning level the host should surface for this snapshot. Computed
    /// from the policy's <c>WarnDaysBeforeExpiry</c> boundaries plus the
    /// grace state.
    /// </summary>
    public required LicenseWarningLevel WarningLevel { get; init; }

    /// <summary>True if a warning event should be raised at this boundary.</summary>
    public required bool IsWarningBoundary { get; init; }
}

/// <summary>
/// Stateless evaluator that maps a wall-clock instant plus an expiration
/// date to a <see cref="LicenseExpirationSnapshot"/>. All time inputs are
/// expected to be in UTC.
/// </summary>
public static class LicenseExpirationTracker
{
    /// <summary>
    /// Evaluate the license state given the current UTC time.
    /// </summary>
    /// <param name="utcNow">Current UTC instant.</param>
    /// <param name="expiresAt">
    /// Expiration instant. Per the documented format this is end-of-day UTC
    /// of the date in the license file (<c>23:59:59.999</c>).
    /// </param>
    /// <param name="policy">Enforcement policy (grace period + warn boundaries).</param>
    public static LicenseExpirationSnapshot Evaluate(
        DateTime utcNow,
        DateTime expiresAt,
        LicenseEnforcementPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var delta = expiresAt - utcNow;
        var days = (int)Math.Floor(delta.TotalDays);

        if (utcNow <= expiresAt)
        {
            // Still valid. Check if we're at a warn boundary.
            var (level, atBoundary) = ClassifyPreExpiry(days, policy.WarnDaysBeforeExpiry);
            return new LicenseExpirationSnapshot
            {
                Status = LicenseStatus.Valid,
                DaysUntilExpiry = days,
                RemainingGrace = null,
                WarningLevel = level,
                IsWarningBoundary = atBoundary,
            };
        }

        var graceEnd = expiresAt + policy.GracePeriod;
        if (utcNow < graceEnd)
        {
            return new LicenseExpirationSnapshot
            {
                Status = LicenseStatus.InGracePeriod,
                DaysUntilExpiry = days, // already negative
                RemainingGrace = graceEnd - utcNow,
                WarningLevel = LicenseWarningLevel.Warning,
                IsWarningBoundary = true,
            };
        }

        return new LicenseExpirationSnapshot
        {
            Status = LicenseStatus.Expired,
            DaysUntilExpiry = days,
            RemainingGrace = null,
            WarningLevel = LicenseWarningLevel.Critical,
            IsWarningBoundary = true,
        };
    }

    private static (LicenseWarningLevel Level, bool AtBoundary) ClassifyPreExpiry(
        int daysUntilExpiry,
        IReadOnlyList<int> warnDays)
    {
        // The boundary fires when the day-count reaches the threshold value
        // exactly. With Math.Floor on days, this means the warning surfaces
        // throughout the final day before the threshold tips over.
        var atBoundary = false;
        var level = LicenseWarningLevel.Info;
        foreach (var d in warnDays)
        {
            if (daysUntilExpiry == d)
            {
                atBoundary = true;
            }
        }

        if (daysUntilExpiry <= 1)
        {
            level = LicenseWarningLevel.Critical;
        }
        else if (daysUntilExpiry <= 7)
        {
            level = LicenseWarningLevel.Warning;
        }
        else if (daysUntilExpiry <= 30)
        {
            level = LicenseWarningLevel.Info;
        }
        else
        {
            level = LicenseWarningLevel.Info;
        }

        return (level, atBoundary);
    }
}
