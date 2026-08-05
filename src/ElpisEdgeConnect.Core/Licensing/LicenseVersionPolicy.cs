// ============================================================================
// File: Licensing/LicenseVersionPolicy.cs
// Purpose: Version-coverage rule — a license covers software versions released
//          on or before its expiry. After expiry, a build released AFTER the
//          license's expiry date is a "newer version" that is not licensed to
//          run (version restriction).
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Determines whether the running build is licensed to run, and whether the
/// license is nearing expiry (renewal reminder).
/// </summary>
public static class LicenseVersionPolicy
{
    /// <summary>Days-before-expiry at which the renewal reminder should surface.</summary>
    public const int RenewalReminderDays = 30;

    /// <summary>
    /// True when this build is NEWER than an expired license covers — i.e. the
    /// license is Expired / in grace AND this build was released after the
    /// license's <c>expiresAt</c>. Such a newer version is not licensed to run.
    /// Returns false while the license is Valid (full access) or absent.
    /// </summary>
    public static bool IsVersionRestricted(LicenseStatus status, LicenseInfo? current, DateTime buildDateUtc)
    {
        if (current is null)
        {
            return false;
        }
        if (status is not (LicenseStatus.Expired or LicenseStatus.InGracePeriod))
        {
            return false;
        }
        return buildDateUtc.Date > current.ExpiresAt.Date;
    }

    /// <summary>
    /// True when a valid license is within <see cref="RenewalReminderDays"/> of
    /// expiry — the point at which the UI should surface the renewal reminder
    /// while still allowing normal usage.
    /// </summary>
    public static bool IsExpiringSoon(LicenseStatus status, LicenseInfo? current, DateTime nowUtc)
    {
        if (current is null || status != LicenseStatus.Valid)
        {
            return false;
        }
        var days = (current.ExpiresAt - nowUtc).TotalDays;
        return days >= 0 && days <= RenewalReminderDays;
    }
}
