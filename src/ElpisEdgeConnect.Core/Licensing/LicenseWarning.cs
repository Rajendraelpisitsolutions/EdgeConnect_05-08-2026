// ============================================================================
// File: Licensing/LicenseWarning.cs
// Purpose: Event payload for ILicenseManager.WarningRaised. Surfaces
//          expiration approaching, grace-period entered, and grace-period
//          exhausted events to host code (which decides how to log them).
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>Severity classification for a <see cref="LicenseWarning"/>.</summary>
public enum LicenseWarningLevel
{
    /// <summary>Informational only (e.g., 30 days to expiry).</summary>
    Info = 0,

    /// <summary>Warning level (e.g., 7 days to expiry, in grace period).</summary>
    Warning = 1,

    /// <summary>Critical (e.g., 1 day to expiry, grace exhausted).</summary>
    Critical = 2,
}

/// <summary>
/// Notification raised by <see cref="ILicenseManager.WarningRaised"/> when
/// the license crosses an expiration boundary or enters/exits the grace
/// period. Core never logs; the host subscribes and logs/alerts.
/// </summary>
public sealed record LicenseWarning
{
    /// <summary>Severity of the warning.</summary>
    public required LicenseWarningLevel Level { get; init; }

    /// <summary>Stable code from <see cref="Errors.CoreErrors"/> if applicable; otherwise null.</summary>
    public string? Code { get; init; }

    /// <summary>Human-readable description.</summary>
    public required string Message { get; init; }

    /// <summary>Days until expiry (negative once past expiry).</summary>
    public int DaysUntilExpiry { get; init; }

    /// <summary>License status at the time the warning was raised.</summary>
    public LicenseStatus Status { get; init; }

    /// <summary>UTC time the warning was raised.</summary>
    public DateTime RaisedAtUtc { get; init; }
}
