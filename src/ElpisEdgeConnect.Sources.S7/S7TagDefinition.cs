// ============================================================================
// File: S7TagDefinition.cs
// Purpose: One configured S7 tag — operator-facing address text + datatype
//          + scan-rate + optional unit/scale/offset. Mirrors
//          ModbusTagDefinition's role.
//
//          The Address property carries the raw operator-authored text;
//          the adapter resolves it at Initialize time via S7AddressParser
//          and stores the typed result alongside for hot-path use.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
// ============================================================================

using ElpisEdgeConnect.Core.Tags;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Configuration for a single S7 tag. Identical author-experience shape
/// to <c>ModbusTagDefinition</c>: a stable name, an address (operator-
/// readable Siemens syntax), an optional datatype hint, scan rate, and
/// per-tag scale/offset/unit. Optional TagSemantics carries the
/// G.6 metadata that downstream sinks surface as display names,
/// engineering units, and OPC UA AccessLevel.
/// </summary>
public sealed record S7TagDefinition
{
    /// <summary>
    /// Operator-stable tag name. Used as the
    /// <c>CanonicalDataPoint.TagName</c> and (when absent on
    /// <see cref="Semantics"/>) as the StableTagId for downstream
    /// identity contracts (e.g. OPC UA NodeId).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Operator-authored S7 address — e.g. <c>"DB10.DBW2"</c>,
    /// <c>"M10.5"</c>, <c>"IB0"</c>. Parsed via
    /// <see cref="S7AddressParser.Parse(string)"/> at adapter
    /// Initialize time.
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// Datatype hint. Optional — when null, derived from the address
    /// width (DBX→bool, DBB→byte, DBW→word/int, DBD→dword/dint/real).
    /// Accepts S7-style strings: <c>"bool"</c>, <c>"int"</c>,
    /// <c>"word"</c>, <c>"dint"</c>, <c>"real"</c>, <c>"string[16]"</c>,
    /// etc. See <see cref="S7DatatypeParser"/>.
    /// </summary>
    public string? Datatype { get; init; }

    /// <summary>Poll interval in milliseconds. Default 1 second.</summary>
    public int ScanRateMs { get; init; } = 1000;

    /// <summary>Engineering unit string surfaced on <c>CanonicalDataPoint.Unit</c>.</summary>
    public string? Unit { get; init; }

    /// <summary>Linear scale factor applied to numeric values (raw * Scale + Offset).</summary>
    public double? Scale { get; init; }

    /// <summary>Linear offset applied to numeric values.</summary>
    public double? Offset { get; init; }

    /// <summary>
    /// Optional protocol-agnostic semantics (G.6) — display name,
    /// description, engineering unit, EU range, writable flag,
    /// category. When present, surfaces on the OPC UA Server sink's
    /// node properties so SCADA clients see authoritative display
    /// metadata.
    /// </summary>
    public TagSemantics? Semantics { get; init; }
}
