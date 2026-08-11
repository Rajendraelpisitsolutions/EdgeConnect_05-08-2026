// ============================================================================
// File: Decoding/ModbusDecoder.cs
// Purpose: Turn raw register / bit payloads returned by the transaction
//          executor into typed values ready for CanonicalDataPointFactory.
//          Handles datatype conversion, multi-register byte-ordering, and
//          stringN decoding with \0/space trimming.
//
// LOCKED PLACEMENT:
//   This decoder lives IN the Modbus assembly, not in Core. Per blueprint
//   LOCK #1, Core is protocol-agnostic — Modbus-specific byte-order /
//   register-packing knowledge would violate that. S7, EtherNet/IP, and
//   OPC UA have their own wire encodings and will ship their own decoders.
// Reference: PHASE3_EXECUTION_PLAN.md §5
// ============================================================================

using System;
using System.Buffers.Binary;
using System.Text;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Decoding;

/// <summary>
/// Pure, stateless decoder mapping raw Modbus payloads to typed .NET values.
/// Never allocates unless the datatype requires it (strings).
/// </summary>
public static class ModbusDecoder
{
    /// <summary>
    /// Characters trimmed from the end of decoded <c>stringN</c> values.
    /// PLCs pad short strings with either NULs or ASCII spaces; trim both.
    /// </summary>
    private static readonly char[] StringPadding = ['\0', ' '];

    /// <summary>
    /// Decode a register-based tag (FC03 / FC04) from a slice of the
    /// block's register payload. <paramref name="offset"/> is the tag's
    /// offset within the block; <paramref name="registerCount"/> is its
    /// width. Returns the decoded object boxed to match
    /// <see cref="ModbusDatatypeSpec.CanonicalType"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="spec"/> is <see cref="ModbusDatatype.Bool"/>
    /// (bits come through <see cref="DecodeBit"/>) or when
    /// <paramref name="byteOrder"/>'s byte count doesn't match
    /// <paramref name="spec"/>.
    /// </exception>
    public static object DecodeRegisters(
        ReadOnlySpan<ushort> blockRegisters,
        int offset,
        int registerCount,
        ModbusDatatypeSpec spec,
        ModbusByteOrder byteOrder)
    {
        if (spec.Datatype == ModbusDatatype.Bool)
        {
            throw new ArgumentException(
                "Bool tags must be read via Coil / DiscreteInput and decoded with DecodeBit.", nameof(spec));
        }
        // StringN uses a per-register high/low convention rather than a
        // whole-value multi-byte ordering, so any byte order is accepted
        // (we only look at whether it's "low-char-first" or not below).
        if (spec.Datatype != ModbusDatatype.StringN &&
            spec.ByteCount != byteOrder.ByteCount())
        {
            throw new ArgumentException(
                $"Byte-order {byteOrder} covers {byteOrder.ByteCount()} bytes but datatype {spec.Datatype} requires {spec.ByteCount}.",
                nameof(byteOrder));
        }

        var tagRegisters = blockRegisters.Slice(offset, registerCount);

        // Wire layout: each register → [high, low].
        Span<byte> wire = stackalloc byte[registerCount * 2];
        ModbusByteOrderReorder.RegistersToWireBytes(tagRegisters, wire);

        // For string we skip the reorder step — strings get AB-style per-
        // register handling below (byte-order applies to multi-register
        // numerics, not to character packing).
        if (spec.Datatype == ModbusDatatype.StringN)
        {
            return DecodeString(wire, spec.StringLengthChars, byteOrder);
        }

        Span<byte> reordered = stackalloc byte[registerCount * 2];
        ModbusByteOrderReorder.Reorder(wire, reordered, byteOrder);

        // Cast each branch to object explicitly so the switch expression
        // doesn't widen every numeric alternative to double (which would
        // swallow the concrete int/long/float distinction after boxing).
        return spec.Datatype switch
        {
            ModbusDatatype.Int16 => (object)(int)BinaryPrimitives.ReadInt16BigEndian(reordered),
            ModbusDatatype.UInt16 => (object)(int)BinaryPrimitives.ReadUInt16BigEndian(reordered),
            ModbusDatatype.Int32 => (object)BinaryPrimitives.ReadInt32BigEndian(reordered),
            ModbusDatatype.UInt32 => (object)(long)BinaryPrimitives.ReadUInt32BigEndian(reordered),
            ModbusDatatype.Float32 => (object)BinaryPrimitives.ReadSingleBigEndian(reordered),
            ModbusDatatype.Int64 => (object)BinaryPrimitives.ReadInt64BigEndian(reordered),
            ModbusDatatype.UInt64 => (object)unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(reordered)),
            ModbusDatatype.Float64 => (object)BinaryPrimitives.ReadDoubleBigEndian(reordered),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec, null),
        };
    }

    /// <summary>Decode a single bit from a Coil / Discrete Input read.</summary>
    public static object DecodeBit(bool raw) => raw;

    // =========================================================================
    // PRIVATE
    // =========================================================================

    /// <summary>
    /// Decode a Modbus-packed ASCII string. Two chars per register, high-char
    /// first is the near-universal convention (pymodbus, Siemens, Schneider).
    /// For <see cref="ModbusByteOrder.BA"/> / <see cref="ModbusByteOrder.BADC"/>
    /// / <see cref="ModbusByteOrder.DCBA"/> the per-register byte order is
    /// flipped so low-char-first PLCs also decode correctly. Trailing NUL and
    /// ASCII space characters are trimmed.
    /// </summary>
    private static string DecodeString(ReadOnlySpan<byte> wire, int lengthChars, ModbusByteOrder byteOrder)
    {
        Span<char> chars = stackalloc char[lengthChars];
        var charsWritten = 0;

        var lowCharFirst = byteOrder is ModbusByteOrder.BA or ModbusByteOrder.BADC or ModbusByteOrder.DCBA;

        for (var regIdx = 0; regIdx < wire.Length / 2 && charsWritten < lengthChars; regIdx++)
        {
            var high = wire[regIdx * 2];
            var low = wire[regIdx * 2 + 1];
            var firstChar = (char)(lowCharFirst ? low : high);
            var secondChar = (char)(lowCharFirst ? high : low);

            chars[charsWritten++] = firstChar;
            if (charsWritten < lengthChars)
            {
                chars[charsWritten++] = secondChar;
            }
        }

        // Convert to string and trim trailing pad chars (\0 AND ASCII space).
        var raw = new string(chars[..charsWritten]);
        return raw.TrimEnd(StringPadding);
    }
}
