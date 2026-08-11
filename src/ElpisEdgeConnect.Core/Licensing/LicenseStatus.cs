// ============================================================================
// File: Licensing/LicenseStatus.cs
// Purpose: Lifecycle status of the active license.
// Reference: ARCHITECTURE_BLUEPRINT.md §7.4
// ============================================================================

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Lifecycle state of the license loaded into the
/// <see cref="ILicenseManager"/>.
/// </summary>
public enum LicenseStatus
{
    /// <summary>No license has been loaded yet.</summary>
    NotLoaded = 0,

    /// <summary>License loaded, signature verified, not yet expired.</summary>
    Valid = 1,

    /// <summary>License past expiry but still inside the 30-day grace period.</summary>
    InGracePeriod = 2,

    /// <summary>License past expiry AND past grace period — config writes blocked.</summary>
    Expired = 3,

    /// <summary>License file was loaded but failed validation (corrupt or tampered).</summary>
    Invalid = 4,
}
