// ============================================================================
// File: Tags/TagSemantics.cs
// Purpose: Per-tag protocol-AGNOSTIC semantic metadata. Lives alongside
//          the protocol-specific tag definition (ModbusTagDefinition,
//          Focas2 collector tag entry, S7TagDefinition, etc.) — those carry
//          the addressing / wire-level details; TagSemantics carries what
//          downstream sinks need regardless of upstream protocol.
//
//          Consumed by every sink that needs semantic awareness:
//            * OPC UA Server   — DisplayName, Description, EngineeringUnits,
//                                EURange, AccessLevel, browsePath grouping
//                                from Category, NodeId stability via
//                                StableTagId
//            * MQTT (batch)    — optional richer JSON payload when the
//                                publishMode is Batch
//            * future HTTP     — same
//            * future historian — engineering units, value semantic for
//                                compression / retention
//          Sinks consume by tagSemantics; they NEVER touch protocol-specific
//          tag definitions.
//
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone G.6
// Milestone: G.6 (Phase 4)
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Tags;

/// <summary>
/// Protocol-agnostic semantic metadata that every tag carries regardless
/// of the source protocol. Sinks consume this to expose DisplayName,
/// Description, EngineeringUnit, EURange, AccessLevel, namespace grouping,
/// etc. without protocol awareness.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separation of concerns:</b> protocol-specific tag definitions
/// (<c>ModbusTagDefinition</c>, FOCAS2 collector tag entries,
/// future <c>S7TagDefinition</c>, etc.) own ADDRESSING and WIRE
/// representation. <see cref="TagSemantics"/> owns SEMANTIC meaning.
/// Both can change independently — renaming a display name doesn't
/// touch the wire-level configuration, and vice versa.
/// </para>
/// <para>
/// <b>StableTagId is the identity contract.</b> <see cref="Name"/> is
/// the human-readable label and CAN be renamed by operators.
/// <see cref="StableTagId"/> is decoupled from display and acts as the
/// stable identity used in OPC UA NodeId derivation, EREMOS historian
/// references, and any external programmatic binding. Persisted in
/// gateway.json once and never regenerated.
/// </para>
/// </remarks>
public sealed record TagSemantics
{
    /// <summary>
    /// Stable system-assigned identifier. Persisted in gateway.json
    /// across config edits; never changes when operators rename the
    /// display label. Used in OPC UA NodeId derivation
    /// (<c>ns=2;s={gatewayId}/{sourceId}/{stableTagId}</c>) and any
    /// other external reference that must survive renames.
    /// <para>
    /// Defaults to <see cref="Name"/> on first introduction (no-config
    /// authoring friction), but once assigned it must be preserved.
    /// </para>
    /// </summary>
    public required string StableTagId { get; init; }

    /// <summary>
    /// Operator-readable name. Becomes the OPC UA BrowseName /
    /// DisplayName default, the MQTT topic segment, and the dashboard
    /// label. Can be renamed without affecting identity references
    /// (those use <see cref="StableTagId"/>).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional longer / richer display label distinct from <see cref="Name"/>.
    /// When present, sinks SHOULD prefer this for human-facing surfaces
    /// (OPC UA DisplayName, dashboard headings). When absent, fall back
    /// to <see cref="Name"/>.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Free-text operator description. Surfaces as the OPC UA
    /// Description attribute and as a tooltip in dashboards.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Engineering unit (e.g. <c>"rpm"</c>, <c>"mm/min"</c>, <c>"%"</c>,
    /// <c>"°C"</c>). Drives the OPC UA EngineeringUnits property and
    /// downstream chart axis labels. Should match the
    /// <c>shared-knowledge/contracts/cnc-vocabulary.md</c> entry for
    /// the tag when the tag is in that vocabulary.
    /// </summary>
    public string? EngineeringUnit { get; init; }

    /// <summary>
    /// Optional process / engineering high limit. Drives OPC UA EURange
    /// upper bound and alarm-threshold defaults. Pair with
    /// <see cref="LowLimit"/>; one without the other surfaces as a
    /// half-range hint to consumers.
    /// </summary>
    public double? HighLimit { get; init; }

    /// <summary>Optional process / engineering low limit. See <see cref="HighLimit"/>.</summary>
    public double? LowLimit { get; init; }

    /// <summary>
    /// Whether this tag supports write-back through the source adapter.
    /// Drives OPC UA AccessLevel (<c>CurrentRead | CurrentWrite</c> vs
    /// just <c>CurrentRead</c>). Default false (read-only). Adapters
    /// without the <c>SourceCapabilities.WriteBack</c> capability MUST
    /// leave this false; sinks may also gate writable surfaces on adapter
    /// capability independent of this field.
    /// </summary>
    public bool Writable { get; init; }

    /// <summary>
    /// Optional taxonomy hint for namespace grouping. Recommended
    /// vocabulary from <c>shared-knowledge/contracts/cnc-vocabulary.md</c>:
    /// <c>Status</c>, <c>Alarm</c>, <c>Production</c>, <c>Axis</c>,
    /// <c>Spindle</c>, <c>Tool</c>, <c>Program</c>, <c>Energy</c>,
    /// <c>Temperature</c>, <c>Counter</c>. Null = uncategorized.
    /// </summary>
    /// <remarks>
    /// OPC UA Server's <c>browsePathTemplate</c> can use <c>{category}</c>
    /// as a folder level when the operator opts in.
    /// </remarks>
    public string? Category { get; init; }

    /// <summary>
    /// Optional value-semantic hint distinct from the wire-level value
    /// type. Used for chart rendering defaults, historian compression
    /// hints, OPC UA node-type selection. Null = no hint.
    /// </summary>
    public ValueSemantic? ValueSemantic { get; init; }

    /// <summary>
    /// Free-form hints carried opaquely to sinks. Keys / values are
    /// sink-defined; unrecognized keys are ignored. Useful for
    /// per-tag overrides without bloating the formal schema (historian
    /// retention overrides, deadband percentages, dashboard colors,
    /// etc.).
    /// </summary>
    public IReadOnlyDictionary<string, string> Hints { get; init; }
        = new Dictionary<string, string>(System.StringComparer.Ordinal);
}
