// ============================================================================
// File: S7Address.cs
// Purpose: Parsed S7 absolute address. Operators author S7 addresses in
//          the canonical Siemens syntax:
//              DB10.DBX0.0     — DB 10, byte 0, bit 0
//              DB10.DBB1       — DB 10, byte 1, byte-wide
//              DB10.DBW2       — DB 10, byte 2, word (2 bytes)
//              DB10.DBD4       — DB 10, byte 4, dword (4 bytes)
//              M10.5           — Marker byte 10, bit 5
//              MB10 / MW10 / MD10  — Marker byte / word / dword
//              I0.0 / Q4.2     — Process inputs / outputs (bit)
//              IB0 / IW2 / ID4 — Inputs at byte/word/dword
//              QB4 / QW6 / QD8 — Outputs at byte/word/dword
//              T5 / C3         — Timer / counter
//
//          S7AddressParser produces a structured S7Address from any of
//          these. The parser is pure (no IO, no library deps) so it's
//          fully unit-testable in isolation — and changing the supported
//          syntax never touches the runtime data path.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
// ============================================================================

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Structured S7 absolute address. <see cref="DbNumber"/> is meaningful
/// only when <see cref="Area"/> is <see cref="S7MemoryArea.DataBlock"/>.
/// <see cref="BitOffset"/> is meaningful only when the addressed item
/// is a single bit; otherwise it's <c>0</c>.
/// </summary>
public readonly record struct S7Address(
    S7MemoryArea Area,
    int DbNumber,
    int ByteOffset,
    int BitOffset,
    S7AddressWidthHint WidthHint)
{
    /// <summary>True when the address points at a single bit (X / dot-bit syntax).</summary>
    public bool IsBit => WidthHint == S7AddressWidthHint.Bit;

    /// <summary>
    /// Operator-facing rendering — same syntax the parser accepts, suitable
    /// for log lines and the TagPath field on <c>CanonicalDataPoint</c>.
    /// </summary>
    public override string ToString() => Area switch
    {
        S7MemoryArea.DataBlock => WidthHint switch
        {
            S7AddressWidthHint.Bit => $"DB{DbNumber}.DBX{ByteOffset}.{BitOffset}",
            S7AddressWidthHint.Byte => $"DB{DbNumber}.DBB{ByteOffset}",
            S7AddressWidthHint.Word => $"DB{DbNumber}.DBW{ByteOffset}",
            S7AddressWidthHint.DWord => $"DB{DbNumber}.DBD{ByteOffset}",
            _ => $"DB{DbNumber}@{ByteOffset}",
        },
        S7MemoryArea.Marker => WidthHint switch
        {
            S7AddressWidthHint.Bit => $"M{ByteOffset}.{BitOffset}",
            S7AddressWidthHint.Byte => $"MB{ByteOffset}",
            S7AddressWidthHint.Word => $"MW{ByteOffset}",
            S7AddressWidthHint.DWord => $"MD{ByteOffset}",
            _ => $"M@{ByteOffset}",
        },
        S7MemoryArea.Input => WidthHint switch
        {
            S7AddressWidthHint.Bit => $"I{ByteOffset}.{BitOffset}",
            S7AddressWidthHint.Byte => $"IB{ByteOffset}",
            S7AddressWidthHint.Word => $"IW{ByteOffset}",
            S7AddressWidthHint.DWord => $"ID{ByteOffset}",
            _ => $"I@{ByteOffset}",
        },
        S7MemoryArea.Output => WidthHint switch
        {
            S7AddressWidthHint.Bit => $"Q{ByteOffset}.{BitOffset}",
            S7AddressWidthHint.Byte => $"QB{ByteOffset}",
            S7AddressWidthHint.Word => $"QW{ByteOffset}",
            S7AddressWidthHint.DWord => $"QD{ByteOffset}",
            _ => $"Q@{ByteOffset}",
        },
        S7MemoryArea.Timer => $"T{ByteOffset}",
        S7MemoryArea.Counter => $"C{ByteOffset}",
        _ => $"?@{ByteOffset}",
    };
}

/// <summary>
/// Width hint encoded in the operator-facing address syntax. The
/// scan-planner uses this to validate the configured datatype against
/// the address width (e.g. a <c>DBW</c> address requires a 2-byte
/// datatype like <c>Int</c> or <c>Word</c>).
/// </summary>
public enum S7AddressWidthHint
{
    /// <summary>No width hint encoded; planner derives from datatype.</summary>
    Unspecified = 0,
    /// <summary>Single bit — address syntax included a bit offset.</summary>
    Bit = 1,
    /// <summary>Byte (1 byte).</summary>
    Byte = 2,
    /// <summary>Word (2 bytes).</summary>
    Word = 3,
    /// <summary>Double word (4 bytes).</summary>
    DWord = 4,
}

/// <summary>
/// Pure-text parser that converts an operator-authored S7 address
/// string into a structured <see cref="S7Address"/>. No IO, no library
/// dependencies — fully unit-testable.
/// </summary>
public static class S7AddressParser
{
    /// <summary>
    /// Parse <paramref name="address"/>. Throws
    /// <see cref="ArgumentException"/> with a precise message when the
    /// syntax is malformed.
    /// </summary>
    public static S7Address Parse(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var s = address.Trim().ToUpperInvariant();

        // DB-relative addresses: "DB<num>.<DBX|DBB|DBW|DBD><offset>[.bit]"
        if (s.StartsWith("DB", StringComparison.Ordinal))
        {
            return ParseDbAddress(s);
        }

        // Markers: M<byte>.<bit>, MB<byte>, MW<byte>, MD<byte>
        if (s.StartsWith('M'))
        {
            return ParseAreaAddress(s, areaPrefix: 'M', area: S7MemoryArea.Marker);
        }

        // Inputs: I<byte>.<bit>, IB<byte>, IW<byte>, ID<byte>
        if (s.StartsWith('I') || s.StartsWith('E')) // E = German Eingang
        {
            return ParseAreaAddress(s, areaPrefix: s[0], area: S7MemoryArea.Input);
        }

        // Outputs: Q<byte>.<bit>, QB<byte>, QW<byte>, QD<byte>
        if (s.StartsWith('Q') || s.StartsWith('A')) // A = German Ausgang
        {
            return ParseAreaAddress(s, areaPrefix: s[0], area: S7MemoryArea.Output);
        }

        // Timer: T<num>
        if (s.StartsWith('T') && s.Length >= 2 && char.IsDigit(s[1]))
        {
            if (!int.TryParse(s.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var t))
            {
                throw new ArgumentException($"Invalid timer index in '{address}'.", nameof(address));
            }
            return new S7Address(S7MemoryArea.Timer, DbNumber: 0, ByteOffset: t, BitOffset: 0, S7AddressWidthHint.Word);
        }

        // Counter: C<num>
        if (s.StartsWith('C') && s.Length >= 2 && char.IsDigit(s[1]))
        {
            if (!int.TryParse(s.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var c))
            {
                throw new ArgumentException($"Invalid counter index in '{address}'.", nameof(address));
            }
            return new S7Address(S7MemoryArea.Counter, DbNumber: 0, ByteOffset: c, BitOffset: 0, S7AddressWidthHint.Word);
        }

        throw new ArgumentException(
            $"Unrecognized S7 address '{address}'. Examples: " +
            "DB10.DBX0.0, DB10.DBW2, M10.5, MB10, I0.0, QW6, T5, C3.",
            nameof(address));
    }

    private static S7Address ParseDbAddress(string s)
    {
        // "DB" already matched; find the dot separating the DB number
        // from the inner access specifier.
        var dot = s.IndexOf('.', 2);
        if (dot < 0)
        {
            throw new ArgumentException(
                $"DB address must include the access specifier (DBX/DBB/DBW/DBD): got '{s}'.",
                nameof(s));
        }

        if (!int.TryParse(s.AsSpan(2, dot - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var dbNumber))
        {
            throw new ArgumentException($"DB number must be a positive integer: got '{s}'.", nameof(s));
        }

        var inner = s[(dot + 1)..]; // DBX0.0 / DBB1 / DBW2 / DBD4
        if (inner.StartsWith("DBX", StringComparison.Ordinal))
        {
            var (offset, bit) = ParseBitAddress(inner[3..], s);
            return new S7Address(S7MemoryArea.DataBlock, dbNumber, offset, bit, S7AddressWidthHint.Bit);
        }
        if (inner.StartsWith("DBB", StringComparison.Ordinal))
        {
            return new S7Address(S7MemoryArea.DataBlock, dbNumber, ParseByteOffset(inner[3..], s), 0, S7AddressWidthHint.Byte);
        }
        if (inner.StartsWith("DBW", StringComparison.Ordinal))
        {
            return new S7Address(S7MemoryArea.DataBlock, dbNumber, ParseByteOffset(inner[3..], s), 0, S7AddressWidthHint.Word);
        }
        if (inner.StartsWith("DBD", StringComparison.Ordinal))
        {
            return new S7Address(S7MemoryArea.DataBlock, dbNumber, ParseByteOffset(inner[3..], s), 0, S7AddressWidthHint.DWord);
        }
        throw new ArgumentException(
            $"DB address inner specifier must be one of DBX/DBB/DBW/DBD: got '{s}'.",
            nameof(s));
    }

    private static S7Address ParseAreaAddress(string s, char areaPrefix, S7MemoryArea area)
    {
        var remainder = s.AsSpan(1);

        // Width-prefixed forms first: <Prefix>B/W/D<offset>.
        if (remainder.Length > 0)
        {
            var widthChar = remainder[0];
            if (widthChar is 'B' or 'W' or 'D')
            {
                var offset = ParseByteOffset(remainder[1..].ToString(), s);
                var hint = widthChar switch
                {
                    'B' => S7AddressWidthHint.Byte,
                    'W' => S7AddressWidthHint.Word,
                    'D' => S7AddressWidthHint.DWord,
                    _ => S7AddressWidthHint.Unspecified,
                };
                return new S7Address(area, DbNumber: 0, ByteOffset: offset, BitOffset: 0, hint);
            }
        }

        // Bit-form: <Prefix><byte>.<bit>
        var (byteOffset, bitOffset) = ParseBitAddress(remainder.ToString(), s);
        _ = areaPrefix; // captured only to make trace messages unambiguous
        return new S7Address(area, DbNumber: 0, ByteOffset: byteOffset, BitOffset: bitOffset, S7AddressWidthHint.Bit);
    }

    private static (int ByteOffset, int Bit) ParseBitAddress(string text, string full)
    {
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            throw new ArgumentException(
                $"Bit-form address must include a bit offset like 'M10.5' or 'DB1.DBX0.4': got '{full}'.",
                nameof(full));
        }
        if (!int.TryParse(text.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out var byteOffset))
        {
            throw new ArgumentException($"Invalid byte offset in '{full}'.", nameof(full));
        }
        if (!int.TryParse(text.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var bit) || bit is < 0 or > 7)
        {
            throw new ArgumentException($"Bit offset must be 0..7 in '{full}'.", nameof(full));
        }
        return (byteOffset, bit);
    }

    private static int ParseByteOffset(string text, string full)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0)
        {
            throw new ArgumentException($"Byte offset must be a non-negative integer in '{full}'.", nameof(full));
        }
        return offset;
    }
}
