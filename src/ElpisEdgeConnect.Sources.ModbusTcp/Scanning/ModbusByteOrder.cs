// ============================================================================
// File: Scanning/ModbusByteOrder.cs
// Purpose: Enumerates the byte-ordering conventions Modbus slaves can apply
//          when packing a multi-register value. The decoder (F3) consults
//          this to rearrange wire bytes before reinterpreting them as a
//          typed value.
//
//          Wire model: every Modbus register arrives high-byte-first on the
//          wire (that's fixed by the protocol). Byte-order variation shows
//          up when a value spans MULTIPLE registers and different PLCs
//          disagree about which register holds the most significant word
//          and which byte within a register is "first".
// Reference: PHASE3_EXECUTION_PLAN.md §5
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

/// <summary>
/// Byte-ordering conventions for multi-register Modbus values. The four
/// 4-byte variants cover every combination of (word order, byte order).
/// 8-byte values only support big- and little-endian — word-swap variants
/// are vanishingly rare on 64-bit payloads and will be added per-customer
/// if needed.
/// </summary>
public enum ModbusByteOrder
{
    /// <summary>2-byte big-endian (default). Wire byte A is the high byte → value = 0xAB.</summary>
    AB = 0,

    /// <summary>2-byte little-endian. Wire byte A is the low byte → value = 0xBA.</summary>
    BA = 1,

    /// <summary>4-byte big-endian (default). Wire bytes A,B,C,D → value 0xABCDCDEF.</summary>
    ABCD = 2,

    /// <summary>4-byte word-swap big-endian. Low word first, bytes within the word in big order.</summary>
    CDAB = 3,

    /// <summary>4-byte word-swap little-endian. High word first, bytes within the word reversed.</summary>
    BADC = 4,

    /// <summary>4-byte little-endian. Full reverse — wire A,B,C,D → value 0xDCBA.</summary>
    DCBA = 5,

    /// <summary>8-byte big-endian.</summary>
    ABCDEFGH = 6,

    /// <summary>8-byte little-endian. Full reverse.</summary>
    HGFEDCBA = 7,
}

/// <summary>
/// Width categories for <see cref="ModbusByteOrder"/> plus parsing helpers.
/// </summary>
public static class ModbusByteOrderExtensions
{
    /// <summary>
    /// Number of bytes consumed by a value with this byte order (2 / 4 / 8).
    /// Used by the config validator to reject byte-order / datatype-width
    /// mismatches.
    /// </summary>
    public static int ByteCount(this ModbusByteOrder order) => order switch
    {
        ModbusByteOrder.AB or ModbusByteOrder.BA => 2,
        ModbusByteOrder.ABCD or ModbusByteOrder.CDAB or ModbusByteOrder.BADC or ModbusByteOrder.DCBA => 4,
        ModbusByteOrder.ABCDEFGH or ModbusByteOrder.HGFEDCBA => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
    };

    /// <summary>
    /// Parse a user-supplied byte-order string (from config JSON or CSV).
    /// Case-insensitive. Returns <see langword="null"/> if <paramref name="raw"/>
    /// is null or empty; throws <see cref="ArgumentException"/> for any
    /// non-empty string that doesn't match one of the enum names.
    /// </summary>
    public static ModbusByteOrder? ParseOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (Enum.TryParse<ModbusByteOrder>(raw.Trim(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }
        throw new ArgumentException(
            $"'{raw}' is not a recognized Modbus byte order. Valid values: AB, BA, ABCD, CDAB, BADC, DCBA, ABCDEFGH, HGFEDCBA.",
            nameof(raw));
    }

    /// <summary>
    /// Pick a sensible default byte order given a tag width in registers:
    /// big-endian for every width (AB / ABCD / ABCDEFGH). Strings default
    /// to AB regardless of register count (two chars per register, high char
    /// first — matches the pymodbus / Siemens / Schneider convention).
    /// </summary>
    public static ModbusByteOrder DefaultFor(int byteCount) => byteCount switch
    {
        2 => ModbusByteOrder.AB,
        4 => ModbusByteOrder.ABCD,
        8 => ModbusByteOrder.ABCDEFGH,
        _ => ModbusByteOrder.AB,
    };
}
