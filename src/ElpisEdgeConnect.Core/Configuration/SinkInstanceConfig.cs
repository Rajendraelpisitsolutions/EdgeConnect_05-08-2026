// ============================================================================
// File: Configuration/SinkInstanceConfig.cs
// Purpose: JSON DTO for a sink connector instance.
// Reference: ARCHITECTURE_BLUEPRINT.md §2 (Connector Instance), §8.1 (sample)
// Milestone: B1
// ============================================================================

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// JSON DTO for a single sink connector instance — one configured use of a
/// sink protocol module against one destination. Distinct from the runtime
/// <see cref="Adapters.SinkConfiguration"/> which adapter implementations
/// receive after Core has loaded and validated the JSON.
/// </summary>
public sealed record SinkInstanceConfig
{
    /// <summary>
    /// Stable identifier for this sink connector instance (e.g.,
    /// <c>"mqtt-eremos-main"</c>).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression(
        "^[A-Za-z0-9][A-Za-z0-9._-]*$",
        ErrorMessage = "InstanceId must start with a letter or digit and contain only letters, digits, dots, hyphens, and underscores.")]
    [BundleTier(BundleTier.Include)]
    public required string InstanceId { get; init; }

    /// <summary>
    /// Protocol module name (e.g., <c>"mqtt"</c>, <c>"http"</c>, <c>"tcp"</c>,
    /// <c>"opcua-server"</c>). Must match the adapter's declared
    /// <see cref="Adapters.ISinkAdapter.ProtocolName"/>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    [RegularExpression(
        "^[a-z][a-z0-9-]*$",
        ErrorMessage = "ProtocolName must be lowercase, start with a letter, and contain only lowercase letters, digits, and hyphens.")]
    [BundleTier(BundleTier.Include)]
    public required string ProtocolName { get; init; }

    /// <summary>
    /// True if this instance is enabled in the running gateway. Default <c>true</c>.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Protocol-specific connection block. Held opaquely as <see cref="JsonElement"/>
    /// — the sink adapter parses it during initialisation.
    /// </summary>
    /// <remarks>
    /// This is a World-2 opaque boundary for redaction (ADR-0020 Amendment 1):
    /// keys inside are name-classified by the protocol's redaction rules and the
    /// baseline, not by a typed tier. It therefore carries no <c>[BundleTier]</c>
    /// and is exempt from the redaction drift guard.
    /// </remarks>
    public JsonElement? Connection { get; init; }

    /// <summary>
    /// Publishing parameters. Universal fields are typed; protocol-specific
    /// extras (MQTT topic prefix, HTTP headers, etc.) are preserved in
    /// <see cref="PublishingSettings.Extras"/> via <see cref="System.Text.Json.Serialization.JsonExtensionDataAttribute"/>.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public PublishingSettings Publishing { get; init; } = new();
}
