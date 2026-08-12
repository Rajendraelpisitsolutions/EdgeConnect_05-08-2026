// ============================================================================
// File: Wire/ProtoWireDecoder.cs
// Purpose: INDEPENDENT minimal proto2 wire-format reader for the K2 golden
//          conformance suite (ADR-0035 Rule 2: same-generated-class round-trips
//          are necessary but not sufficient — common-mode error risk). This
//          decoder deliberately depends on NEITHER the vendored generated types
//          NOR Google.Protobuf parsing APIs: it interprets tag keys, varints,
//          fixed-width fields, and length-delimited fields itself from raw
//          bytes. It exposes FIELD PRESENCE, WIRE TYPE, and REPEATED-FIELD
//          ORDER — not merely decoded values — which is the central protection
//          against proto2 default-value omission bugs.
// Reference: plan v3 (frozen) §slice 2; slice-1 review verdict (independence
//            constraint); slice-2 self-tests in ProtoWireDecoderTests.cs.
// ============================================================================

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Wire;

/// <summary>The proto2 wire types (tag key low 3 bits).</summary>
internal enum ProtoWireType
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    StartGroup = 3,
    EndGroup = 4,
    Fixed32 = 5,
}

/// <summary>
/// One physically-present field occurrence on the wire, in encounter order.
/// Only the member matching <see cref="WireType"/> is meaningful; fixed-width
/// values are exposed as raw little-endian bits so tests control interpretation.
/// </summary>
internal sealed record ProtoWireField
{
    public required int FieldNumber { get; init; }

    public required ProtoWireType WireType { get; init; }

    /// <summary>Raw varint value (valid when <see cref="WireType"/> is Varint).</summary>
    public ulong VarintValue { get; init; }

    /// <summary>Raw little-endian 64-bit payload (valid for Fixed64; e.g. double bits).</summary>
    public ulong Fixed64Bits { get; init; }

    /// <summary>Raw little-endian 32-bit payload (valid for Fixed32; e.g. float bits).</summary>
    public uint Fixed32Bits { get; init; }

    /// <summary>Raw payload bytes (valid for LengthDelimited; decode nested messages by calling the decoder again).</summary>
    public byte[] LengthDelimitedBytes { get; init; } = [];
}

/// <summary>
/// Decodes a proto2 wire-format buffer into its physically-present fields, in
/// order of appearance. Unknown field numbers are decoded and returned like any
/// other field (safe skip: decoding always continues past them). Malformed
/// input throws <see cref="InvalidDataException"/>; group wire types (unused by
/// sparkplug_b.proto) throw <see cref="NotSupportedException"/>.
/// </summary>
internal static class ProtoWireDecoder
{
    /// <summary>The protobuf wire-format field-number maximum, (1 &lt;&lt; 29) - 1 = 536,870,911.</summary>
    private const long MaxFieldNumber = (1L << 29) - 1;

    public static IReadOnlyList<ProtoWireField> Decode(ReadOnlySpan<byte> bytes)
    {
        var fields = new List<ProtoWireField>();
        var offset = 0;

        while (offset < bytes.Length)
        {
            var tag = ReadVarint(bytes, ref offset, "tag");
            var fieldNumber = (long)(tag >> 3);
            var wireType = (ProtoWireType)(tag & 0x7);

            if (fieldNumber is < 1 or > MaxFieldNumber)
            {
                throw new InvalidDataException($"Invalid protobuf field number {fieldNumber} at offset {offset}.");
            }

            switch (wireType)
            {
                case ProtoWireType.Varint:
                    fields.Add(new ProtoWireField
                    {
                        FieldNumber = (int)fieldNumber,
                        WireType = wireType,
                        VarintValue = ReadVarint(bytes, ref offset, $"field {fieldNumber} varint"),
                    });
                    break;

                case ProtoWireType.Fixed64:
                    fields.Add(new ProtoWireField
                    {
                        FieldNumber = (int)fieldNumber,
                        WireType = wireType,
                        Fixed64Bits = ReadFixed(bytes, ref offset, 8, fieldNumber),
                    });
                    break;

                case ProtoWireType.Fixed32:
                    fields.Add(new ProtoWireField
                    {
                        FieldNumber = (int)fieldNumber,
                        WireType = wireType,
                        Fixed32Bits = (uint)ReadFixed(bytes, ref offset, 4, fieldNumber),
                    });
                    break;

                case ProtoWireType.LengthDelimited:
                    var length = ReadVarint(bytes, ref offset, $"field {fieldNumber} length");
                    if (length > (ulong)(bytes.Length - offset))
                    {
                        throw new InvalidDataException(
                            $"Field {fieldNumber} declares length {length} but only {bytes.Length - offset} bytes remain.");
                    }

                    fields.Add(new ProtoWireField
                    {
                        FieldNumber = (int)fieldNumber,
                        WireType = wireType,
                        LengthDelimitedBytes = bytes.Slice(offset, (int)length).ToArray(),
                    });
                    offset += (int)length;
                    break;

                case ProtoWireType.StartGroup:
                case ProtoWireType.EndGroup:
                    throw new NotSupportedException(
                        $"Group wire type {wireType} for field {fieldNumber} is not used by sparkplug_b.proto; input is corrupt.");

                default:
                    throw new InvalidDataException($"Unknown wire type {(int)wireType} for field {fieldNumber}.");
            }
        }

        return fields;
    }

    /// <summary>Base-128 varint, at most 10 bytes; the 10th byte may only contribute bit 63.</summary>
    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int offset, string what)
    {
        ulong value = 0;
        for (var i = 0; i < 10; i++)
        {
            if (offset >= bytes.Length)
            {
                throw new InvalidDataException($"Truncated varint ({what}) at offset {offset}.");
            }

            var b = bytes[offset++];
            if (i == 9 && (b & 0xFE) != 0)
            {
                throw new InvalidDataException($"Varint ({what}) overflows 64 bits at offset {offset - 1}.");
            }

            value |= (ulong)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException($"Varint ({what}) exceeds 10 bytes at offset {offset}.");
    }

    private static ulong ReadFixed(ReadOnlySpan<byte> bytes, ref int offset, int width, long fieldNumber)
    {
        if (offset + width > bytes.Length)
        {
            throw new InvalidDataException($"Truncated fixed{width * 8} for field {fieldNumber} at offset {offset}.");
        }

        ulong value = 0;
        for (var i = 0; i < width; i++)
        {
            value |= (ulong)bytes[offset + i] << (8 * i);
        }

        offset += width;
        return value;
    }
}
