// ============================================================================
// File: MelsecTagDefinition.cs
// Purpose: One configured MELSEC tag — operator-facing device address text
//          (e.g. "D100", "W1A", "M200", "D100.3"), optional datatype hint,
//          per-tag word order, scan rate, and unit/scale/offset. Mirrors
//          S7TagDefinition's author experience.
// Reference: docs/sessions/2026-06-30-melsec-source-adapter-plan-v3.md
// ============================================================================

using ElpisEdgeConnect.Core.Tags;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>
/// Configuration for a single MELSEC tag. The <see cref="Address"/> carries the
/// raw operator-authored device address; the adapter resolves it at Initialize
/// time via the address parser and rejects the config on any parse failure.
/// </summary>
public sealed record MelsecTagDefinition
{
    /// <summary>Operator-stable tag name, used as <c>CanonicalDataPoint.TagName</c>.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Operator-authored MELSEC device address — e.g. <c>"D100"</c>, <c>"W1A"</c>,
    /// <c>"M200"</c>, or a word-bit address <c>"D100.3"</c>. Device-number radix is
    /// per-device (hex for X/Y/B/W/SB/SW/ZR, decimal for D/M/R/...; see ADR-0033 Rule 3).
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// Datatype hint — e.g. <c>"int16"</c>, <c>"uint16"</c>, <c>"int32"</c>,
    /// <c>"uint32"</c>, <c>"float32"</c>, <c>"bool"</c>. Optional; when null the
    /// adapter derives a default from the address (word device → int16, bit / word-bit → bool).
    /// </summary>
    public string? Datatype { get; init; }

    /// <summary>
    /// Word order for 32/64-bit values that span consecutive words. Default
    /// <see cref="MelsecWordOrder.LowWordFirst"/>. Ignored for 16-bit and bit types.
    /// </summary>
    public MelsecWordOrder WordOrder { get; init; } = MelsecWordOrder.LowWordFirst;

    /// <summary>Poll interval in milliseconds. Default 1 second.</summary>
    public int ScanRateMs { get; init; } = 1000;

    /// <summary>Engineering unit string surfaced on <c>CanonicalDataPoint.Unit</c>.</summary>
    public string? Unit { get; init; }

    /// <summary>Linear scale factor applied to numeric values (raw * Scale + Offset).</summary>
    public double? Scale { get; init; }

    /// <summary>Linear offset applied to numeric values.</summary>
    public double? Offset { get; init; }

    /// <summary>Optional protocol-agnostic semantics (display name, EU range, etc.).</summary>
    public TagSemantics? Semantics { get; init; }
}
