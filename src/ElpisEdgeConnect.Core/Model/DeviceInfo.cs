// ============================================================================
// File: Model/DeviceInfo.cs
// Purpose: Identity metadata about a physical device behind a source adapter.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4 (implied)
// ============================================================================

namespace ElpisEdgeConnect.Core.Model;

/// <summary>
/// Identity metadata about a physical device. Populated by source adapters
/// where the protocol exposes identity information (FOCAS2 system info,
/// OPC UA ServerInfo, etc.). Used for diagnostics and enrichment.
/// </summary>
public sealed record DeviceInfo
{
    /// <summary>Stable device identifier used throughout the gateway.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Human-readable device name.</summary>
    public required string DeviceName { get; init; }

    /// <summary>Vendor / manufacturer name (e.g., "Fanuc", "Siemens", "Brother").</summary>
    public string? Vendor { get; init; }

    /// <summary>Model identifier (e.g., "0i-TF Plus", "840D").</summary>
    public string? Model { get; init; }

    /// <summary>Firmware / controller version.</summary>
    public string? FirmwareVersion { get; init; }

    /// <summary>Device serial number, if readable.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>The UTC timestamp at which this info was last refreshed by the adapter.</summary>
    public required System.DateTime LastUpdatedAt { get; init; }
}
