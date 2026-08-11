// ============================================================================
// File: Licensing/LicenseEdition.cs
// Purpose: Edition enum for the LicenseInfo payload.
// Reference: ARCHITECTURE_BLUEPRINT.md §7.2
// ============================================================================

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Product edition encoded in the license payload. Used for UI labelling and
/// for sanity-checking the module list against expected coverage; the actual
/// per-module entitlements always come from <see cref="LicenseInfo.Modules"/>,
/// not from this enum.
/// </summary>
public enum LicenseEdition
{
    /// <summary>Unknown / unset (default for an empty struct).</summary>
    Unknown = 0,

    /// <summary>Starter edition — minimum viable feature set.</summary>
    Starter = 1,

    /// <summary>Professional edition — all standard protocols and features.</summary>
    Professional = 2,

    /// <summary>Enterprise edition — all features plus fleet management.</summary>
    Enterprise = 3,

    /// <summary>CNC Pro edition — FANUC FOCAS2, Brother HTTP, MTConnect sources; MQTT destination.</summary>
    CncPro = 4,

    /// <summary>Trial Period edition — time-limited; all sources and destinations.</summary>
    TrialPeriod = 5,
}
