// ============================================================================
// File: Licensing/LicenseInfo.cs
// Purpose: Decoded, validated, immutable license payload.
// Reference: ARCHITECTURE_BLUEPRINT.md §7.2
//
// This is the in-memory representation of license.json AFTER:
//   1. JSON parse
//   2. RSA-PSS signature verification
//   3. Required-field structural check
//
// Once constructed, an instance is trusted: ILicenseManager hands it out as
// a snapshot and replaces it atomically on reload.
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Validated, immutable license payload. Constructed only by
/// <see cref="LicenseManager"/> after signature verification succeeds.
/// </summary>
/// <remarks>
/// Module and feature lookups go through <see cref="FrozenDictionary{TKey, TValue}"/>
/// to keep <see cref="ILicenseManager.IsModuleEnabled"/> on the
/// sub-100-nanosecond fast path required by the Phase 1 perf budget.
/// </remarks>
public sealed record LicenseInfo
{
    /// <summary>Stable identifier from the license file (e.g. <c>LIC-2026-0042</c>).</summary>
    public required string LicenseId { get; init; }

    /// <summary>Customer name as printed on the license.</summary>
    public required string Customer { get; init; }

    /// <summary>Gateway identity this license is bound to.</summary>
    public required string GatewayId { get; init; }

    /// <summary>Product edition.</summary>
    public required LicenseEdition Edition { get; init; }

    /// <summary>UTC issuance date (no time component).</summary>
    public required DateTime IssuedAt { get; init; }

    /// <summary>
    /// UTC expiry instant. Per the documented format, this is
    /// <em>end-of-day UTC</em> on the date in the license file
    /// (<c>23:59:59.999</c>) so a license dated <c>2026-04-07</c> remains
    /// valid throughout that day.
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>Global instance limits.</summary>
    public required LicenseLimits Limits { get; init; }

    /// <summary>
    /// Per-module entitlements keyed by module identifier
    /// (e.g. <c>"source.focas2"</c>, <c>"sink.mqtt"</c>). Lowercase, stable.
    /// </summary>
    public required FrozenDictionary<string, LicenseModule> Modules { get; init; }

    /// <summary>Optional feature flags (lowercase keys).</summary>
    public FrozenDictionary<string, bool> Features { get; init; } =
        FrozenDictionary<string, bool>.Empty;
}
