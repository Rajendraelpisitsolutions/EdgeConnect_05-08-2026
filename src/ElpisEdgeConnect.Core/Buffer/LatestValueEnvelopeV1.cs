// ============================================================================
// File: Buffer/LatestValueEnvelopeV1.cs
// Purpose: Versioned, self-describing codec for the value-carrying portion of a
//          latest_value manifest row (the BLOB column). Encodes/decodes a
//          Routing.LatestMetricValue with a FIXED MessagePack field layout and
//          EXPLICIT type discriminators — deliberately NOT the typeless/object
//          resolver MessagePackFormat uses. Fail-closed on every unknown/malformed
//          input: unknown codec version, unknown discriminator, malformed field,
//          unsupported CanonicalValueType, or an envelope↔column disagreement.
// Reference: docs/sessions/2026-07-14-sparkplug-b-k1.2-route-store-plan-v3.md §5
//            (pin LatestValueEnvelopeV1); v3.1-amendment §5 (decode cross-checks);
//            K1.2c session handoff §3 item 1.
//
// The physical latest_value columns (source/device/tag, value_type,
// route_buffer_sequence, schema_generation, updated_at) stay separate for
// validation and indexing; this envelope carries only the parts that have no
// column: the null state, the scalar value union, the acquisition timestamp,
// quality (+ reason), the unit, and the immutable static properties. On decode
// the caller supplies the column context so the two stores are cross-checked and
// can never drift silently.
// ============================================================================

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using MessagePack;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Codec for the <c>latest_value.envelope</c> BLOB (v1). Round-trips every
/// persisted metric arm — Boolean, Integer, Long, Float, Double, String, DateTime,
/// ByteArray, a known-null value carrying its real datatype — together with quality,
/// quality reason, unit and the immutable static properties. Never uses typeless
/// serialization; every value carries an explicit discriminator. Fails closed
/// (<see cref="CoreErrors.RouteStoreEnvelopeUnsupported"/>) on any input it cannot
/// represent or decode exactly.
/// </summary>
internal static class LatestValueEnvelopeV1
{
    /// <summary>The only codec version this binary writes and accepts.</summary>
    internal const int CodecVersion = 1;

    /// <summary>Fixed number of top-level fields in the envelope array (layout is positional).</summary>
    private const int FieldCount = 9;

    /// <summary>
    /// Explicit per-value discriminators. The small set (0-8) mirrors the
    /// <see cref="CanonicalValueType"/> scalar arms and drives both the main value and the
    /// static-property values; the 20+ range covers the wider immutable-scalar set that
    /// only static properties may carry (see <c>LatestMetricValue.ToImmutableStaticValue</c>).
    /// </summary>
    private enum Tag : byte
    {
        Null = 0,
        Boolean = 1,
        Int32 = 2,
        Int64 = 3,
        Single = 4,
        Double = 5,
        String = 6,
        DateTime = 7,
        Bytes = 8,

        // Static-property-only extras (no CanonicalValueType arm).
        SByte = 20,
        Byte = 21,
        Int16 = 22,
        UInt16 = 23,
        UInt32 = 24,
        UInt64 = 25,
        Decimal = 26,
        DateTimeOffset = 27,
        Guid = 28,
    }

    // ------------------------------------------------------------------------
    // Encode
    // ------------------------------------------------------------------------

    /// <summary>
    /// Encode the value-carrying portion of a manifest row. The metric identity,
    /// datatype, route-buffer sequence and generation live in columns and are NOT
    /// duplicated here (single source of truth); they are cross-checked on decode.
    /// </summary>
    /// <param name="value">The validated manifest value (from <c>LatestMetricValue.Create</c>).</param>
    /// <returns>The MessagePack-encoded envelope BLOB.</returns>
    /// <exception cref="BufferException">
    /// <see cref="CoreErrors.RouteStoreEnvelopeUnsupported"/> when a value or static
    /// property cannot be represented (an unsupported datatype slips through, or a
    /// static-property CLR type is outside the immutable-scalar set).
    /// </exception>
    internal static byte[] Encode(LatestMetricValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var buffer = new ArrayBufferWriter<byte>(64);
        var writer = new MessagePackWriter(buffer);

        writer.WriteArrayHeader(FieldCount);
        writer.Write(CodecVersion);                     // [0] codec version
        writer.Write((int)value.ValueType);             // [1] declared datatype
        writer.Write(value.IsNull);                     // [2] null state
        WriteMainValue(ref writer, value.ValueType, value.Value, value.IsNull); // [3] scalar union
        writer.Write(value.TimestampUtc.UtcTicks);      // [4] acquisition timestamp (UTC ticks)
        writer.Write((int)value.Quality);               // [5] quality
        WriteNullableString(ref writer, value.QualityReason); // [6] quality reason
        WriteNullableString(ref writer, value.Unit);    // [7] unit
        WriteStaticProperties(ref writer, value.StaticProperties); // [8] static properties

        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteMainValue(ref MessagePackWriter writer, CanonicalValueType type, object? value, bool isNull)
    {
        if (isNull)
        {
            writer.WriteNil();
            return;
        }

        switch (type)
        {
            case CanonicalValueType.Boolean: writer.Write((bool)value!); break;
            case CanonicalValueType.Integer: writer.Write((int)value!); break;
            case CanonicalValueType.Long: writer.Write((long)value!); break;
            case CanonicalValueType.Float: writer.Write((float)value!); break;
            case CanonicalValueType.Double: writer.Write((double)value!); break;
            case CanonicalValueType.String: writer.Write((string)value!); break;
            case CanonicalValueType.DateTime: WriteDateTime(ref writer, (DateTime)value!); break;
            case CanonicalValueType.ByteArray: writer.Write(((ImmutableArray<byte>)value!).AsSpan()); break;
            default:
                // Null/Array/Object never reach a persisted LatestMetricValue, but a
                // caller that hands us one directly must still fail closed.
                throw Unsupported($"value type '{type}' has no scalar envelope representation");
        }
    }

    private static void WriteStaticProperties(ref MessagePackWriter writer, IReadOnlyDictionary<string, object?>? props)
    {
        if (props is null)
        {
            writer.WriteNil();
            return;
        }

        writer.WriteMapHeader(props.Count);
        foreach (var (key, val) in props)
        {
            writer.Write(key);
            WriteTaggedScalar(ref writer, key, val);
        }
    }

    /// <summary>Write one static-property value as a 2-tuple [tag, payload] with an explicit discriminator.</summary>
    private static void WriteTaggedScalar(ref MessagePackWriter writer, string key, object? val)
    {
        writer.WriteArrayHeader(2);
        switch (val)
        {
            case null: writer.Write((byte)Tag.Null); writer.WriteNil(); break;
            case bool b: writer.Write((byte)Tag.Boolean); writer.Write(b); break;
            case int i: writer.Write((byte)Tag.Int32); writer.Write(i); break;
            case long l: writer.Write((byte)Tag.Int64); writer.Write(l); break;
            case float f: writer.Write((byte)Tag.Single); writer.Write(f); break;
            case double d: writer.Write((byte)Tag.Double); writer.Write(d); break;
            case string s: writer.Write((byte)Tag.String); writer.Write(s); break;
            case DateTime dt: writer.Write((byte)Tag.DateTime); WriteDateTime(ref writer, dt); break;
            case sbyte sb: writer.Write((byte)Tag.SByte); writer.Write(sb); break;
            case byte by: writer.Write((byte)Tag.Byte); writer.Write(by); break;
            case short sh: writer.Write((byte)Tag.Int16); writer.Write(sh); break;
            case ushort us: writer.Write((byte)Tag.UInt16); writer.Write(us); break;
            case uint ui: writer.Write((byte)Tag.UInt32); writer.Write(ui); break;
            case ulong ul: writer.Write((byte)Tag.UInt64); writer.Write(ul); break;
            case decimal dec: writer.Write((byte)Tag.Decimal); WriteDecimal(ref writer, dec); break;
            case DateTimeOffset dto: writer.Write((byte)Tag.DateTimeOffset); WriteDateTimeOffset(ref writer, dto); break;
            case Guid g: writer.Write((byte)Tag.Guid); writer.Write(g.ToByteArray()); break;
            case ImmutableArray<byte> bytes: writer.Write((byte)Tag.Bytes); writer.Write(bytes.AsSpan()); break;
            case byte[] rawBytes: writer.Write((byte)Tag.Bytes); writer.Write(rawBytes); break;
            default:
                throw Unsupported($"static property '{key}' has unsupported CLR type '{val.GetType()}'");
        }
    }

    private static void WriteDateTime(ref MessagePackWriter writer, DateTime dt)
    {
        writer.WriteArrayHeader(2);
        writer.Write(dt.Ticks);
        writer.Write((byte)dt.Kind);
    }

    private static void WriteDateTimeOffset(ref MessagePackWriter writer, DateTimeOffset dto)
    {
        writer.WriteArrayHeader(2);
        writer.Write(dto.Ticks);
        writer.Write((short)dto.Offset.TotalMinutes);
    }

    private static void WriteDecimal(ref MessagePackWriter writer, decimal dec)
    {
        var bits = decimal.GetBits(dec);
        writer.WriteArrayHeader(4);
        for (int i = 0; i < 4; i++)
        {
            writer.Write(bits[i]);
        }
    }

    private static void WriteNullableString(ref MessagePackWriter writer, string? value)
    {
        if (value is null)
        {
            writer.WriteNil();
        }
        else
        {
            writer.Write(value);
        }
    }

    // ------------------------------------------------------------------------
    // Decode
    // ------------------------------------------------------------------------

    /// <summary>
    /// Decode an envelope BLOB back into a <see cref="LatestMetricValue"/>, cross-checking
    /// the persisted columns so the duplicated fields cannot drift. The datatype the
    /// envelope carries must equal <paramref name="valueTypeColumn"/>; the reconstructed
    /// value uses <paramref name="metric"/> and <paramref name="routeBufferSequenceColumn"/>
    /// from the columns (the envelope never re-supplies the identity or the sequence).
    /// </summary>
    /// <param name="envelope">The persisted envelope bytes.</param>
    /// <param name="metric">The metric identity from the key columns.</param>
    /// <param name="valueTypeColumn">The datatype from the <c>value_type</c> column.</param>
    /// <param name="routeBufferSequenceColumn">The <c>route_buffer_sequence</c> column (non-negative).</param>
    /// <returns>The reconstructed manifest value.</returns>
    /// <exception cref="BufferException">
    /// <see cref="CoreErrors.RouteStoreEnvelopeUnsupported"/> on an unknown version, an
    /// unknown discriminator, a malformed field, or an envelope↔column disagreement.
    /// </exception>
    internal static LatestMetricValue Decode(
        ReadOnlyMemory<byte> envelope,
        CanonicalMetricKey metric,
        CanonicalValueType valueTypeColumn,
        long routeBufferSequenceColumn)
    {
        if (routeBufferSequenceColumn < 0)
        {
            throw Unsupported($"route_buffer_sequence column {routeBufferSequenceColumn} is negative");
        }

        try
        {
            var reader = new MessagePackReader(envelope);

            var fieldCount = reader.ReadArrayHeader();
            if (fieldCount != FieldCount)
            {
                throw Unsupported($"unexpected envelope field count {fieldCount} (expected {FieldCount})");
            }

            var version = reader.ReadInt32();
            if (version != CodecVersion)
            {
                throw Unsupported($"unknown codec version {version} (this binary writes v{CodecVersion})");
            }

            var valueTypeRaw = reader.ReadInt32();
            if (!Enum.IsDefined(typeof(CanonicalValueType), valueTypeRaw))
            {
                throw Unsupported($"unknown CanonicalValueType discriminator {valueTypeRaw}");
            }

            var valueType = (CanonicalValueType)valueTypeRaw;
            if (valueType != valueTypeColumn)
            {
                throw Unsupported(
                    $"envelope datatype '{valueType}' does not match the value_type column '{valueTypeColumn}'");
            }

            var isNull = reader.ReadBoolean();
            var value = ReadMainValue(ref reader, valueType, isNull);

            var timestamp = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);

            var qualityRaw = reader.ReadInt32();
            if (!Enum.IsDefined(typeof(DataQuality), qualityRaw))
            {
                throw Unsupported($"unknown DataQuality discriminator {qualityRaw}");
            }

            var qualityReason = ReadNullableString(ref reader);
            var unit = ReadNullableString(ref reader);
            var staticProps = ReadStaticProperties(ref reader);

            if (!reader.End)
            {
                throw Unsupported("trailing bytes after the envelope");
            }

            // LatestMetricValue.Create re-validates the null/value/type invariants and
            // re-copies ByteArray/static props to their immutable stored form.
            return LatestMetricValue.Create(
                metric,
                valueType,
                value,
                isNull,
                timestamp,
                (DataQuality)qualityRaw,
                routeBufferSequenceColumn,
                qualityReason,
                unit,
                staticProps);
        }
        catch (BufferException)
        {
            throw;
        }
        catch (Exception ex) when (ex is MessagePackSerializationException or EndOfStreamException
                                       or InvalidOperationException or FormatException
                                       or ArgumentException or OverflowException or InvalidCastException)
        {
            throw Unsupported($"malformed envelope: {ex.Message}");
        }
    }

    private static object? ReadMainValue(ref MessagePackReader reader, CanonicalValueType type, bool isNull)
    {
        if (isNull)
        {
            if (!reader.TryReadNil())
            {
                throw Unsupported("IsNull=true but the value slot is not nil");
            }

            return null;
        }

        return type switch
        {
            CanonicalValueType.Boolean => reader.ReadBoolean(),
            CanonicalValueType.Integer => reader.ReadInt32(),
            CanonicalValueType.Long => reader.ReadInt64(),
            CanonicalValueType.Float => reader.ReadSingle(),
            CanonicalValueType.Double => reader.ReadDouble(),
            CanonicalValueType.String => reader.ReadString()
                ?? throw Unsupported("String value slot is nil for a non-null metric"),
            CanonicalValueType.DateTime => ReadDateTime(ref reader),
            // Hand LatestMetricValue.Create the raw byte[]; it deep-copies to an
            // ImmutableArray<byte> itself (a pre-wrapped ImmutableArray is rejected there).
            CanonicalValueType.ByteArray => ReadBytesRaw(ref reader),
            _ => throw Unsupported($"value type '{type}' has no scalar envelope representation"),
        };
    }

    private static Dictionary<string, object?>? ReadStaticProperties(ref MessagePackReader reader)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        var count = reader.ReadMapHeader();
        var props = new Dictionary<string, object?>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadString()
                ?? throw Unsupported("static-property key is nil");
            props[key] = ReadTaggedScalar(ref reader);
        }

        return props;
    }

    private static object? ReadTaggedScalar(ref MessagePackReader reader)
    {
        var tupleLen = reader.ReadArrayHeader();
        if (tupleLen != 2)
        {
            throw Unsupported($"static-property tuple length {tupleLen} (expected 2)");
        }

        var tagRaw = reader.ReadByte();
        if (!Enum.IsDefined(typeof(Tag), tagRaw))
        {
            throw Unsupported($"unknown static-property discriminator {tagRaw}");
        }

        return (Tag)tagRaw switch
        {
            Tag.Null => ReadNilPayload(ref reader),
            Tag.Boolean => reader.ReadBoolean(),
            Tag.Int32 => reader.ReadInt32(),
            Tag.Int64 => reader.ReadInt64(),
            Tag.Single => reader.ReadSingle(),
            Tag.Double => reader.ReadDouble(),
            Tag.String => (object?)reader.ReadString() ?? throw Unsupported("string static property is nil"),
            Tag.DateTime => ReadDateTime(ref reader),
            Tag.Bytes => ImmutableArray.Create(ReadBytesRaw(ref reader)),
            Tag.SByte => reader.ReadSByte(),
            Tag.Byte => reader.ReadByte(),
            Tag.Int16 => reader.ReadInt16(),
            Tag.UInt16 => reader.ReadUInt16(),
            Tag.UInt32 => reader.ReadUInt32(),
            Tag.UInt64 => reader.ReadUInt64(),
            Tag.Decimal => ReadDecimal(ref reader),
            Tag.DateTimeOffset => ReadDateTimeOffset(ref reader),
            Tag.Guid => new Guid(ReadBytesRaw(ref reader)),
            _ => throw Unsupported($"unhandled static-property discriminator {tagRaw}"),
        };
    }

    private static object? ReadNilPayload(ref MessagePackReader reader)
    {
        if (!reader.TryReadNil())
        {
            throw Unsupported("null-tagged static property has a non-nil payload");
        }

        return null;
    }

    private static DateTime ReadDateTime(ref MessagePackReader reader)
    {
        var len = reader.ReadArrayHeader();
        if (len != 2)
        {
            throw Unsupported($"DateTime tuple length {len} (expected 2)");
        }

        var ticks = reader.ReadInt64();
        var kindRaw = reader.ReadByte();
        if (kindRaw > (byte)DateTimeKind.Local)
        {
            throw Unsupported($"unknown DateTimeKind {kindRaw}");
        }

        return new DateTime(ticks, (DateTimeKind)kindRaw);
    }

    private static DateTimeOffset ReadDateTimeOffset(ref MessagePackReader reader)
    {
        var len = reader.ReadArrayHeader();
        if (len != 2)
        {
            throw Unsupported($"DateTimeOffset tuple length {len} (expected 2)");
        }

        var ticks = reader.ReadInt64();
        var offsetMinutes = reader.ReadInt16();
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    private static decimal ReadDecimal(ref MessagePackReader reader)
    {
        var len = reader.ReadArrayHeader();
        if (len != 4)
        {
            throw Unsupported($"decimal bits length {len} (expected 4)");
        }

        var bits = new int[4];
        for (int i = 0; i < 4; i++)
        {
            bits[i] = reader.ReadInt32();
        }

        return new decimal(bits);
    }

    private static byte[] ReadBytesRaw(ref MessagePackReader reader)
    {
        var seq = reader.ReadBytes();
        if (seq is null)
        {
            throw Unsupported("byte-array slot is nil for a non-null value");
        }

        return seq.Value.ToArray();
    }

    private static string? ReadNullableString(ref MessagePackReader reader) =>
        reader.TryReadNil() ? null : reader.ReadString();

    private static BufferException Unsupported(string detail) =>
        new(CoreErrors.RouteStoreEnvelopeUnsupported,
            string.Create(CultureInfo.InvariantCulture, $"latest_value envelope (v{CodecVersion}): {detail}."));
}
