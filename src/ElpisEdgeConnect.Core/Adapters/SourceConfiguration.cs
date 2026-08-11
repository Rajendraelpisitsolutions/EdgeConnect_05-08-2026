// ============================================================================
// File: Adapters/SourceConfiguration.cs
// Purpose: Base class for all source-adapter configuration objects.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Base class for a source adapter's configuration. Protocol-specific
/// configuration records derive from this and add their own fields
/// (e.g., <c>Focas2SourceConfiguration</c>, <c>ModbusSourceConfiguration</c>).
/// </summary>
public abstract record SourceConfiguration
{
    /// <summary>Stable identifier of this source connector instance.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Display name for the UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Protocol module name (e.g., "focas2"). Must match the adapter's declared protocol.</summary>
    public required string ProtocolName { get; init; }

    /// <summary>Whether this instance is enabled in the running gateway.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Poll interval in milliseconds, for polling-mode adapters.</summary>
    public int PollIntervalMs { get; init; } = 1000;

    /// <summary>Identifier of the physical device behind this source.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Optional human-readable device name.</summary>
    public string? DeviceName { get; init; }

    /// <summary>
    /// Logical role of this source — surfaces on the per-tag MQTT topic as
    /// the <c>{deviceClass}</c> segment, and on every emitted
    /// <see cref="Model.CanonicalDataPoint.Metadata"/> as
    /// <c>"deviceClass"</c>. Initial vocabulary: <c>cnc</c>, <c>plc</c>,
    /// <c>daq</c>, <c>tracker</c>, <c>meter</c>, <c>gateway</c>. Reserved:
    /// <c>robot</c>, <c>sensor</c>, <c>hmi</c>, <c>scada</c>, <c>vision</c>.
    /// Per-protocol defaults: CNC adapters (FOCAS2, MTConnect) default to
    /// <c>"cnc"</c> for backward compatibility; protocol-agnostic adapters
    /// (Modbus) require an explicit value at config-validation time. See
    /// <c>shared-knowledge/contracts/eremos-per-tag-mqtt.md</c>.
    /// </summary>
    public string? DeviceClass { get; init; }

    /// <summary>Tags attached to this instance for filtering and routing.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    // ===== Asset hierarchy (per-source, ISA-95-style) =====================
    //
    // Site / Area travel through GatewaySettings; Line / Asset travel through
    // SourceConfiguration because one gateway may serve multiple lines or
    // assets on the same site. CanonicalDataPoint surfaces all of these to
    // sinks via the existing Metadata dictionary (no schema change to
    // CanonicalDataPoint required).

    /// <summary>
    /// Optional production line / cell label this source belongs to.
    /// ISA-95 level 2 ("Work Center"). Used by OPC UA browse paths and
    /// EREMOS V2 line-level rollups.
    /// </summary>
    public string? Line { get; init; }

    /// <summary>
    /// Stable identifier for the line, distinct from the human-readable
    /// <see cref="Line"/> label. Renaming the label does not change
    /// downstream references.
    /// </summary>
    public string? LineId { get; init; }

    /// <summary>
    /// Stable identifier for the physical asset (one machine / one PLC) this
    /// source is reading. ISA-95 level 1 ("Work Unit"). When the same physical
    /// machine moves between gateways (rare but happens during
    /// commissioning), <see cref="AssetId"/> follows the machine; the source
    /// instance id may change but historians and dashboards keep continuity
    /// via AssetId.
    /// </summary>
    public string? AssetId { get; init; }

    /// <summary>
    /// Machine class for grouping and capability inference. Free-form but
    /// the recommended vocabulary is: <c>"PLC"</c>, <c>"CNC"</c>,
    /// <c>"Drive"</c>, <c>"Robot"</c>, <c>"Sensor"</c>, <c>"DAQ"</c>,
    /// <c>"Meter"</c>, <c>"Gateway"</c>. Distinct from
    /// <see cref="SourceConfiguration.DeviceClass"/> which is the
    /// per-tag-MQTT-topic segment vocabulary; AssetClass is for fleet
    /// management semantics.
    /// </summary>
    public string? AssetClass { get; init; }

    /// <summary>
    /// Configured human-readable gateway identifier from
    /// <c>GatewayConfiguration.Gateway.GatewayId</c>, e.g.
    /// <c>"gw-line1-edge"</c>. Used by source adapters as the topic-segment
    /// gateway id and as <see cref="Model.CanonicalDataPoint.GatewayId"/>
    /// on every emitted point.
    /// <para>
    /// This is intentionally distinct from <see cref="Identity.IGatewayIdentity"/>,
    /// which carries a per-gateway UUID for auth / license / registration
    /// (Architecture Blueprint Locked Decision #19). The UUID is identity;
    /// this string is the operator-readable display name that surfaces on
    /// MQTT topics, dashboards, and audit logs.
    /// </para>
    /// <para>
    /// Populated at registration time by the host's
    /// <c>Add{Protocol}SourcesFromGatewayConfig</c> extension. When null
    /// (test fixtures / narrow harnesses), adapters fall back to the runtime
    /// <see cref="Identity.IGatewayIdentity"/> UUID.
    /// </para>
    /// </summary>
    public string? GatewayId { get; init; }
}
