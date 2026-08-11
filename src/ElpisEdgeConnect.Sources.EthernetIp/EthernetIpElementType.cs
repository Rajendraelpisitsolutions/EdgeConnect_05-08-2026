// ============================================================================
// File: EthernetIpElementType.cs
// Purpose: The set of Allen-Bradley atomic element types the EtherNet/IP MVP
//          adapter can read, plus the mapping to a canonical value type, the
//          on-wire byte width, and a case-insensitive parser. Mirrors the
//          S7Datatype / ModbusDatatype vocabulary pattern.
//
//          MVP scope: atomics + AB STRING only. UDT / array / structured-tag
//          decoding lands with the UDT walker in a later milestone (multi-
//          protocol pilot expansion plan v2.1 §5.2 — deferred items).
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §3.1, §3.4
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// Allen-Bradley atomic element types supported by the EtherNet/IP source
/// adapter. The names match the Logix vocabulary operators author tags in.
/// </summary>
public enum EthernetIpElementType
{
    /// <summary>1-bit boolean (Logix <c>BOOL</c>).</summary>
    Bool = 0,

    /// <summary>8-bit signed integer (Logix <c>SINT</c>).</summary>
    Sint = 1,

    /// <summary>16-bit signed integer (Logix <c>INT</c>).</summary>
    Int = 2,

    /// <summary>32-bit signed integer (Logix <c>DINT</c>).</summary>
    Dint = 3,

    /// <summary>64-bit signed integer (Logix <c>LINT</c>).</summary>
    Lint = 4,

    /// <summary>32-bit IEEE-754 float (Logix <c>REAL</c>).</summary>
    Real = 5,

    /// <summary>64-bit IEEE-754 float (Logix <c>LREAL</c>).</summary>
    Lreal = 6,

    /// <summary>Allen-Bradley <c>STRING</c> (<c>{LEN:DINT, DATA:SINT[82]}</c>).</summary>
    String = 7,
}

/// <summary>
/// Helpers mapping <see cref="EthernetIpElementType"/> to canonical value
/// types, on-wire widths, and parsing operator-authored datatype hints.
/// </summary>
public static class EthernetIpElementTypeExtensions
{
    /// <summary>
    /// The canonical value type a decoded value of this element type carries.
    /// <c>uint</c>-style widening is unnecessary — AB atomics are all signed.
    /// </summary>
    public static CanonicalValueType CanonicalType(this EthernetIpElementType type) => type switch
    {
        EthernetIpElementType.Bool => CanonicalValueType.Boolean,
        EthernetIpElementType.Sint => CanonicalValueType.Integer,
        EthernetIpElementType.Int => CanonicalValueType.Integer,
        EthernetIpElementType.Dint => CanonicalValueType.Integer,
        EthernetIpElementType.Lint => CanonicalValueType.Long,
        EthernetIpElementType.Real => CanonicalValueType.Float,
        EthernetIpElementType.Lreal => CanonicalValueType.Double,
        EthernetIpElementType.String => CanonicalValueType.String,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown EtherNet/IP element type."),
    };

    /// <summary>
    /// True for numeric element types that accept a linear <c>scale</c> /
    /// <c>offset</c>. Bool and String are excluded — scaling them is meaningless.
    /// </summary>
    public static bool SupportsScaleOffset(this EthernetIpElementType type) =>
        type is not (EthernetIpElementType.Bool or EthernetIpElementType.String);

    /// <summary>
    /// Parse an operator-authored datatype hint (case-insensitive) into an
    /// element type. Accepts the Logix names (<c>BOOL</c>, <c>SINT</c>,
    /// <c>INT</c>, <c>DINT</c>, <c>LINT</c>, <c>REAL</c>, <c>LREAL</c>,
    /// <c>STRING</c>).
    /// </summary>
    /// <exception cref="ArgumentException">When the hint is unknown.</exception>
    public static EthernetIpElementType Parse(string? datatype)
    {
        if (string.IsNullOrWhiteSpace(datatype))
        {
            throw new ArgumentException("EtherNet/IP datatype is required.", nameof(datatype));
        }

        return datatype.Trim().ToUpperInvariant() switch
        {
            "BOOL" => EthernetIpElementType.Bool,
            "SINT" => EthernetIpElementType.Sint,
            "INT" => EthernetIpElementType.Int,
            "DINT" => EthernetIpElementType.Dint,
            "LINT" => EthernetIpElementType.Lint,
            "REAL" => EthernetIpElementType.Real,
            "LREAL" => EthernetIpElementType.Lreal,
            "STRING" => EthernetIpElementType.String,
            _ => throw new ArgumentException(
                $"Unknown EtherNet/IP datatype '{datatype}'. Accepted: BOOL, SINT, INT, DINT, LINT, REAL, LREAL, STRING.",
                nameof(datatype)),
        };
    }

    /// <summary>
    /// Parse, returning <see langword="null"/> instead of throwing on unknown
    /// input. Used by validators that want to report a friendly error.
    /// </summary>
    public static EthernetIpElementType? ParseOrNull(string? datatype)
    {
        try
        {
            return Parse(datatype);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
