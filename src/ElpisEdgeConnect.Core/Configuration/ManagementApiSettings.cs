// ============================================================================
// File: Configuration/ManagementApiSettings.cs
// Purpose: Local management REST API configuration sub-record.
// Reference: ARCHITECTURE_BLUEPRINT.md §8.1 (sample), §14 Phase 4
// Milestone: B1 (model only — the API itself lands in Phase 4)
// ============================================================================

using System.ComponentModel.DataAnnotations;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Configuration for the local REST management API exposed by the gateway.
/// The API itself lands in Phase 4; this record is the JSON-loadable shape
/// only and is consumed by the host project when it stands the API up.
/// </summary>
public sealed record ManagementApiSettings
{
    /// <summary>True if the management API is enabled. Default <c>true</c>.</summary>
    [BundleTier(BundleTier.Include)]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// TCP port the API listens on. Default 8443 per blueprint §8.1 sample.
    /// </summary>
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    [BundleTier(BundleTier.Include)]
    public int Port { get; init; } = 8443;

    /// <summary>
    /// True if the API requires authentication. Default <c>true</c> — the
    /// management API exposes config and license operations and should not
    /// be unauthenticated in any production deployment.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public bool RequireAuth { get; init; } = true;

    /// <summary>
    /// Filesystem path to the TLS certificate (PFX) used by the API
    /// listener. Optional; if absent, the host falls back to plain HTTP
    /// (intended for development only).
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public string? TlsCertPath { get; init; }
}
