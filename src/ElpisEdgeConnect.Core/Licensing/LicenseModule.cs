// ============================================================================
// File: Licensing/LicenseModule.cs
// Purpose: Per-module entitlement record from the license payload.
// Reference: ARCHITECTURE_BLUEPRINT.md §7.2
// ============================================================================

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// One entry in the license's <c>modules</c> dictionary. Keys in that
/// dictionary are module identifiers like <c>"source.focas2"</c> or
/// <c>"sink.mqtt"</c>; the value is this record.
/// </summary>
public sealed record LicenseModule
{
    /// <summary>True if the module is enabled by this license.</summary>
    public required bool Enabled { get; init; }

    /// <summary>
    /// Maximum number of instances of this module that may be configured.
    /// <c>null</c> means unlimited (subject to the global instance limits in
    /// <see cref="LicenseLimits"/>).
    /// </summary>
    public int? MaxInstances { get; init; }
}
