// ============================================================================
// File: ProductVersion.cs
// Purpose: The running build's version + release date, used for license
//          version-coverage checks (a license covers software versions
//          released on or before its expiry date). Bump both at each release.
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core;

/// <summary>
/// Identifies the running EdgeConnect build for license version-coverage checks.
/// <see cref="BuildDateUtc"/> is compared to the license expiry: a build released
/// after an expired license's <c>expiresAt</c> is a "newer version" not covered by
/// that license. Update both constants at every release.
/// </summary>
public static class ProductVersion
{
    /// <summary>Human-readable product version (e.g. shown in the UI / logs).</summary>
    public const string Version = "1.2.0";

    /// <summary>
    /// Release date of this build (UTC). A license covers builds with a release
    /// date on or before its <c>expiresAt</c>.
    /// </summary>
    public static readonly DateTime BuildDateUtc = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
}
