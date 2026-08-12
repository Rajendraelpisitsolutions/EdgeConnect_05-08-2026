// ============================================================================
// File: Configuration/GatewaySettings.cs
// Purpose: Top-level gateway identity and host settings.
// Reference: ARCHITECTURE_BLUEPRINT.md §8.1 (sample), §10 (Gateway Identity)
// Milestone: B1
// ============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Gateway-level identity and host settings. This is the <c>Gateway</c>
/// section of the top-level configuration JSON. Per blueprint §10, the
/// gateway identity is established at first start and persisted; this record
/// holds the configurable subset of that identity plus host-process settings.
/// </summary>
public sealed record GatewaySettings
{
    /// <summary>
    /// Stable gateway identifier (e.g., <c>"GW-MENON-001"</c>). Required;
    /// must be unique across the customer's fleet. Used as a foundational
    /// identity key in canonical points, diagnostics, and license binding.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression(
        "^[A-Za-z0-9][A-Za-z0-9._-]*$",
        ErrorMessage = "GatewayId must start with a letter or digit and contain only letters, digits, dots, hyphens, and underscores.")]
    [BundleTier(BundleTier.Include)]
    public required string GatewayId { get; init; }

    /// <summary>Human-readable gateway name shown in the admin UI.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(256, MinimumLength = 1)]
    [BundleTier(BundleTier.Include)]
    public required string GatewayName { get; init; }

    /// <summary>
    /// Customer site or location label. Optional but recommended for fleet
    /// management. ISA-95 level 4 ("Site").
    /// </summary>
    [StringLength(256)]
    [BundleTier(BundleTier.Include)]
    public string? Site { get; init; }

    /// <summary>
    /// Stable identifier for the site, distinct from the human-readable
    /// <see cref="Site"/> label. Used in OPC UA browse paths, EREMOS V2
    /// rollups, and historian partitioning when present. Renaming
    /// <see cref="Site"/> does NOT change downstream references; SiteId
    /// is the operator-stable identity.
    /// </summary>
    [StringLength(128)]
    [RegularExpression(
        "^[A-Za-z0-9][A-Za-z0-9._-]*$",
        ErrorMessage = "SiteId must start with a letter or digit and contain only letters, digits, dots, hyphens, and underscores.")]
    [BundleTier(BundleTier.Include)]
    public string? SiteId { get; init; }

    /// <summary>
    /// Optional plant area / department label within the site. ISA-95
    /// level 3 ("Area"). Drives OPC UA browse-path templating and
    /// dashboard rollups.
    /// </summary>
    [StringLength(256)]
    [BundleTier(BundleTier.Include)]
    public string? Area { get; init; }

    /// <summary>
    /// Stable identifier for the area, distinct from the human-readable
    /// <see cref="Area"/> label. Same identity-vs-display split as
    /// <see cref="SiteId"/> / <see cref="Site"/>.
    /// </summary>
    [StringLength(128)]
    [RegularExpression(
        "^[A-Za-z0-9][A-Za-z0-9._-]*$",
        ErrorMessage = "AreaId must start with a letter or digit and contain only letters, digits, dots, hyphens, and underscores.")]
    [BundleTier(BundleTier.Include)]
    public string? AreaId { get; init; }

    /// <summary>
    /// Path to the license file relative to the gateway data directory.
    /// Default <c>"edgelicense.json"</c>.
    /// </summary>
    [StringLength(512)]
    [BundleTier(BundleTier.Include)]
    public string LicenseFile { get; init; } = "edgelicense.json";

    /// <summary>
    /// Minimum log level. One of <c>Trace</c>, <c>Debug</c>, <c>Information</c>,
    /// <c>Warning</c>, <c>Error</c>, <c>Critical</c>, <c>None</c>. Default
    /// <c>Information</c>. Stored as a string to avoid a Core dependency on
    /// <c>Microsoft.Extensions.Logging.Abstractions</c>; the host project
    /// parses this into the appropriate log level type.
    /// </summary>
    [RegularExpression(
        "^(Trace|Debug|Information|Warning|Error|Critical|None)$",
        ErrorMessage = "LogLevel must be one of: Trace, Debug, Information, Warning, Error, Critical, None.")]
    [BundleTier(BundleTier.Include)]
    public string LogLevel { get; init; } = "Information";

    /// <summary>
    /// Filesystem path (relative or absolute) to the gateway's persistent
    /// data directory. Holds buffers, drafts, history, audit logs, and the
    /// identity file. Default <c>"data/"</c>.
    /// </summary>
    /// <remarks>
    /// Intentionally NOT marked <c>[Required]</c> even though it must be
    /// non-empty: the field has a default value, so the runtime never sees
    /// it missing at deserialization, and the <c>[StringLength(MinimumLength=1)]</c>
    /// attribute already rejects empty strings via
    /// <c>Validator.TryValidateObject</c>. Marking it <c>[Required]</c>
    /// would cause NJsonSchema to emit it in the schema's <c>required</c>
    /// array, creating a schema/runtime mismatch where the schema rejects
    /// configs the runtime accepts (because the runtime fills the default).
    /// </remarks>
    [StringLength(512, MinimumLength = 1)]
    [BundleTier(BundleTier.Include)]
    public string DataPath { get; init; } = "data/";

    /// <summary>
    /// TCP port for the lightweight HTTP health check endpoint
    /// (<c>GET /health</c>). Default 8080 per blueprint §8.1 sample.
    /// </summary>
    [Range(1, 65535, ErrorMessage = "HealthCheckPort must be between 1 and 65535.")]
    [BundleTier(BundleTier.Include)]
    public int HealthCheckPort { get; init; } = 8080;

    /// <summary>Local management API settings.</summary>
    [BundleTier(BundleTier.Include)]
    public ManagementApiSettings ManagementApi { get; init; } = new();

    /// <summary>Watchdog settings.</summary>
    [BundleTier(BundleTier.Include)]
    public WatchdogSettings Watchdog { get; init; } = new();

    /// <summary>
    /// Tag-name patterns whose live VALUES must be masked (<c>***</c>) wherever
    /// they cross a diagnostic surface — primarily the Live Data Tap (ADR-0018
    /// Rule 6 / ADR-0018A). Each entry is an exact tag name or a glob
    /// (<c>*</c> / <c>?</c>), e.g. <c>recipe/secret_setpoint</c> or
    /// <c>recipe/*</c>. Matching is case-insensitive against
    /// <see cref="Model.CanonicalDataPoint.TagName"/>.
    /// <para>
    /// This is a value-PRIVACY allowlist for DIAGNOSTICS only. It masks only the
    /// value — identity, type, quality, and timestamps are preserved — and it
    /// does NOT affect the runtime data path (sinks still deliver the real
    /// value). Empty (default) masks nothing. The patterns themselves are not
    /// secret, so they are included in diagnostic bundles.
    /// </para>
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyList<string> SensitiveTags { get; init; } = Array.Empty<string>();

    // NOTE: Global store-and-forward settings are intentionally NOT part of
    // GatewaySettings for B1. Blueprint §8.1's locked sample shape stops at
    // Watchdog at the gateway level. Per-route buffer policy lives on
    // BufferPolicyConfig (route-scoped, blueprint §4.4); a global S+F
    // settings record will be introduced explicitly when the buffer
    // subsystem (C2a/C2b) lands and the blueprint is updated to include it.
}
