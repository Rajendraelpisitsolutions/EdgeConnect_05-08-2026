// ============================================================================
// File: Configuration/SourceInstanceConfig.cs
// Purpose: JSON DTO for a source connector instance.
// Reference: ARCHITECTURE_BLUEPRINT.md §2 (Connector Instance), §8.1 (sample)
// Milestone: B1
//
// This is a JSON-loadable DTO. The runtime adapter contract lives in
// ElpisEdgeConnect.Core.Adapters.SourceConfiguration. The host project
// (Phase 2) is responsible for translating this DTO + the protocol-specific
// Connection block into the typed adapter configuration when initialising
// each adapter instance.
// ============================================================================

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// JSON DTO for a single source connector instance — one configured use of
/// a protocol module against one physical device. Distinct from the runtime
/// <see cref="Adapters.SourceConfiguration"/> which adapter implementations
/// receive after Core has loaded and validated the JSON.
/// </summary>
public sealed record SourceInstanceConfig
{
    /// <summary>
    /// Stable identifier for this source connector instance (e.g.,
    /// <c>"focas-jyoti17"</c>). Lowercase alphanumeric with hyphens, must
    /// start with a letter or digit.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression(
        "^[A-Za-z0-9][A-Za-z0-9._-]*$",
        ErrorMessage = "InstanceId must start with a letter or digit and contain only letters, digits, dots, hyphens, and underscores.")]
    [BundleTier(BundleTier.Include)]
    public required string InstanceId { get; init; }

    /// <summary>
    /// Protocol module name (e.g., <c>"focas2"</c>, <c>"mtlinki"</c>,
    /// <c>"modbus"</c>). Lowercase, must match the adapter's declared
    /// <see cref="Adapters.ISourceAdapter.ProtocolName"/>.
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
    /// Identifier of the physical device behind this source (e.g.,
    /// <c>"Jyoti17CNC"</c>).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression(
        "^[A-Za-z0-9][A-Za-z0-9._-]*$",
        ErrorMessage = "DeviceId must start with a letter or digit and contain only letters, digits, dots, hyphens, and underscores.")]
    [BundleTier(BundleTier.Include)]
    public required string DeviceId { get; init; }

    /// <summary>Optional human-readable device name.</summary>
    [StringLength(256)]
    [BundleTier(BundleTier.Include)]
    public string? DeviceName { get; init; }

    /// <summary>
    /// Logical role of this source — surfaces on the per-tag MQTT topic as
    /// the <c>{deviceClass}</c> segment. Initial vocabulary: <c>cnc</c>,
    /// <c>plc</c>, <c>daq</c>, <c>tracker</c>, <c>meter</c>, <c>gateway</c>.
    /// Reserved: <c>robot</c>, <c>sensor</c>, <c>hmi</c>, <c>scada</c>,
    /// <c>vision</c>. CNC adapters (FOCAS2, MTConnect) default to
    /// <c>"cnc"</c> when omitted; protocol-agnostic adapters (Modbus)
    /// require an explicit value at validation time. See
    /// <c>shared-knowledge/contracts/eremos-per-tag-mqtt.md</c>.
    /// </summary>
    [StringLength(32)]
    [RegularExpression(
        "^[a-z0-9-]+$",
        ErrorMessage = "DeviceClass must be lowercase ASCII alphanumeric and hyphens only.")]
    [BundleTier(BundleTier.Include)]
    public string? DeviceClass { get; init; }

    /// <summary>
    /// Protocol-specific connection block. Held opaquely as <see cref="JsonElement"/>
    /// because the schema differs per protocol (FOCAS2 has IpAddress+Port,
    /// Modbus has UnitId, MQTT has BrokerHost+ClientId, etc.). The protocol
    /// adapter is responsible for parsing this into its own typed connection
    /// record during <see cref="Adapters.ISourceAdapter.InitializeAsync"/>.
    /// </summary>
    /// <remarks>
    /// This is a World-2 opaque boundary for redaction (ADR-0020 Amendment 1):
    /// keys inside are name-classified by the protocol's redaction rules and the
    /// baseline, not by a typed tier. It therefore carries no <c>[BundleTier]</c>
    /// and is exempt from the redaction drift guard.
    /// </remarks>
    public JsonElement? Connection { get; init; }

    /// <summary>
    /// Polling parameters. Optional; defaults supplied by
    /// <see cref="PollingSettings"/> when omitted.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public PollingSettings Polling { get; init; } = new();

    /// <summary>
    /// Free-form tags attached to this instance for filtering and routing.
    /// Default empty.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyList<string> Tags { get; init; } = [];
}
