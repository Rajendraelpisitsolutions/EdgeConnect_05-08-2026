// ============================================================================
// File: Licensing/LicenseEnforcementPolicy.cs
// Purpose: Tunable policy values for license expiration and warning emission.
// Reference: ARCHITECTURE_BLUEPRINT.md §7.4
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Policy values that drive <see cref="LicenseExpirationTracker"/> and the
/// gate's grace-period behaviour. The default values are taken from
/// blueprint §7.4 and should not be changed without an explicit decision.
/// </summary>
public sealed record LicenseEnforcementPolicy
{
    /// <summary>
    /// Length of the grace period after expiry during which configuration
    /// changes are still allowed but warnings escalate. Default: 30 days.
    /// </summary>
    public required TimeSpan GracePeriod { get; init; }

    /// <summary>
    /// Day boundaries before expiry at which to raise warnings. Default:
    /// <c>{ 30, 7, 1 }</c>.
    /// </summary>
    public required IReadOnlyList<int> WarnDaysBeforeExpiry { get; init; }

    /// <summary>The locked default policy from blueprint §7.4.</summary>
    public static LicenseEnforcementPolicy Default { get; } = new()
    {
        GracePeriod = TimeSpan.FromDays(30),
        WarnDaysBeforeExpiry = new[] { 30, 7, 1 },
    };
}
