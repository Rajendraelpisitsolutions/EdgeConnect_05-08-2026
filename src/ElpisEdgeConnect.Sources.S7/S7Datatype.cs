// ============================================================================
// File: S7Datatype.cs
// Purpose: S7-native datatypes the adapter knows how to decode. Mirrors
//          the shape of Modbus's ModbusDatatype: an enum + spec record
//          + string parser.
//
//          MVP set covers the bulk of Customer B's PLC programs:
//             Bool, Byte, Word, Int, DWord, DInt, Real, String, Char
//          Extension set (forward-compat, decoded if encountered):
//             SInt, USInt, UInt, UDInt, LInt, ULInt, LReal
//          DTL (8-byte timestamp) is parser-known but not yet decoded —
//          adapter rejects DTL tags at validation with a clear message.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// S7 datatypes the adapter can decode from a raw byte payload.
/// </summary>
public enum S7Datatype
{
    /// <summary>1 bit (Bool / X). Address must include a bit offset.</summary>
    Bool = 0,

    /// <summary>1 byte unsigned (Byte / B).</summary>
    Byte = 1,

    /// <summary>1 byte signed (SInt).</summary>
    SInt = 2,

    /// <summary>1 byte unsigned (USInt) — synonym for Byte in MVP.</summary>
    USInt = 3,

    /// <summary>2 bytes signed (Int).</summary>
    Int = 4,

    /// <summary>2 bytes unsigned (UInt / Word). S7 'Word' is unsigned.</summary>
    Word = 5,

    /// <summary>4 bytes signed (DInt).</summary>
    DInt = 6,

    /// <summary>4 bytes unsigned (UDInt / DWord). S7 'DWord' is unsigned.</summary>
    DWord = 7,

    /// <summary>4 bytes IEEE-754 single-precision float (Real).</summary>
    Real = 8,

    /// <summary>8 bytes IEEE-754 double-precision float (LReal).</summary>
    LReal = 9,

    /// <summary>8 bytes signed (LInt).</summary>
    LInt = 10,

    /// <summary>8 bytes unsigned (ULInt).</summary>
    ULInt = 11,

    /// <summary>
    /// S7 string. Wire format is two header bytes (max length, current length)
    /// followed by N ASCII characters. The adapter reads (2 + N) bytes and
    /// trims to the current-length prefix.
    /// </summary>
    String = 12,

    /// <summary>Single ASCII character (1 byte).</summary>
    Char = 13,
}

/// <summary>
/// Resolved datatype + parameters. Captures the on-wire byte count, the
/// canonical type the value is materialized as, and (for
/// <see cref="S7Datatype.String"/>) the declared maximum character
/// count.
/// </summary>
public readonly record struct S7DatatypeSpec(S7Datatype Datatype, int MaxStringChars = 0)
{
    /// <summary>
    /// On-wire byte count for one value of this datatype. For
    /// <see cref="S7Datatype.String"/> includes the 2-byte length header.
    /// For <see cref="S7Datatype.Bool"/> the byte count is 1 — bits are
    /// addressed within a 1-byte read.
    /// </summary>
    public int ByteCount => Datatype switch
    {
        S7Datatype.Bool => 1,
        S7Datatype.Byte or S7Datatype.SInt or S7Datatype.USInt or S7Datatype.Char => 1,
        S7Datatype.Int or S7Datatype.Word => 2,
        S7Datatype.DInt or S7Datatype.DWord or S7Datatype.Real => 4,
        S7Datatype.LReal or S7Datatype.LInt or S7Datatype.ULInt => 8,
        S7Datatype.String => 2 + MaxStringChars,
        _ => throw new InvalidOperationException($"Unknown S7 datatype {Datatype}."),
    };

    /// <summary>The canonical-model type a decoded value materializes as.</summary>
    public CanonicalValueType CanonicalType => Datatype switch
    {
        S7Datatype.Bool => CanonicalValueType.Boolean,
        S7Datatype.Byte or S7Datatype.SInt or S7Datatype.USInt
            or S7Datatype.Int or S7Datatype.Word or S7Datatype.DInt => CanonicalValueType.Integer,
        S7Datatype.DWord or S7Datatype.LInt or S7Datatype.ULInt => CanonicalValueType.Long,
        S7Datatype.Real => CanonicalValueType.Float,
        S7Datatype.LReal => CanonicalValueType.Double,
        S7Datatype.String or S7Datatype.Char => CanonicalValueType.String,
        _ => CanonicalValueType.Null,
    };

    /// <summary>Whether linear scale/offset can be applied to the decoded value.</summary>
    public bool SupportsScaleOffset => Datatype is not (S7Datatype.Bool or S7Datatype.String or S7Datatype.Char);
}

/// <summary>
/// Parses string datatype hints (case-insensitive) into a typed
/// <see cref="S7DatatypeSpec"/>. Handles the S7-style
/// <c>"string[N]"</c> width syntax — e.g. <c>"string[16]"</c>.
/// </summary>
public static class S7DatatypeParser
{
    /// <summary>
    /// Parse <paramref name="raw"/> into a spec. Returns
    /// <paramref name="defaultSpec"/> when <paramref name="raw"/> is null
    /// or empty. Throws <see cref="ArgumentException"/> on unknown types.
    /// </summary>
    public static S7DatatypeSpec Parse(string? raw, S7DatatypeSpec defaultSpec)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultSpec;
        }

        var trimmed = raw.Trim();

        // string[N] syntax — extract the bracketed character count.
        if (trimmed.StartsWith("string", StringComparison.OrdinalIgnoreCase))
        {
            var bracketOpen = trimmed.IndexOf('[');
            var bracketClose = trimmed.IndexOf(']');
            if (bracketOpen > 0 && bracketClose > bracketOpen)
            {
                var span = trimmed.AsSpan(bracketOpen + 1, bracketClose - bracketOpen - 1);
                if (int.TryParse(span, out var n) && n > 0)
                {
                    return new S7DatatypeSpec(S7Datatype.String, n);
                }
            }
            throw new ArgumentException(
                $"S7 string datatype must declare a length: e.g. 'string[16]'. Got '{trimmed}'.",
                nameof(raw));
        }

        return trimmed.ToLowerInvariant() switch
        {
            "bool" or "bit" or "x" => new S7DatatypeSpec(S7Datatype.Bool),
            "byte" or "b" => new S7DatatypeSpec(S7Datatype.Byte),
            "sint" => new S7DatatypeSpec(S7Datatype.SInt),
            "usint" => new S7DatatypeSpec(S7Datatype.USInt),
            "int" or "i" or "w" => new S7DatatypeSpec(S7Datatype.Int),
            "uint" or "word" => new S7DatatypeSpec(S7Datatype.Word),
            "dint" or "d" => new S7DatatypeSpec(S7Datatype.DInt),
            "udint" or "dword" => new S7DatatypeSpec(S7Datatype.DWord),
            "real" or "float" or "float32" => new S7DatatypeSpec(S7Datatype.Real),
            "lreal" or "double" or "float64" => new S7DatatypeSpec(S7Datatype.LReal),
            "lint" => new S7DatatypeSpec(S7Datatype.LInt),
            "ulint" => new S7DatatypeSpec(S7Datatype.ULInt),
            "char" or "c" => new S7DatatypeSpec(S7Datatype.Char),
            _ => throw new ArgumentException(
                $"Unknown S7 datatype '{trimmed}'. Supported: bool, byte, sint, usint, int, " +
                "uint/word, dint, udint/dword, real, lreal, lint, ulint, char, string[N].",
                nameof(raw)),
        };
    }
}
