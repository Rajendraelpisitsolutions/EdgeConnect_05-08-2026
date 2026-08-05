// ============================================================================
// File: Buffer/BinaryWriterFormat.cs
// Purpose: Hand-rolled BinaryWriter-based serializer for CanonicalDataPoint.
//          One of two formats prototyped for C2a; the winner is locked at
//          the C2a gate review based on benchmark results.
// Reference: PHASE1_EXECUTION_PLAN.md C2a
//
// Wire format (version 1):
//   byte    version (=1)
//   string  GatewayId
//   string  SourceInstanceId
//   string  ProtocolName
//   string  DeviceId
//   nstring DeviceName
//   string  TagName
//   string  TagPath
//   nstring OriginalTagName
//   nstring Unit
//   byte    Quality
//   nstring QualityReason
//   long    DeviceTimestamp.Ticks (UTC)
//   long    GatewayTimestamp.Ticks (UTC)
//   long    SequenceNumber
//   byte    ValueType
//   value   (encoding depends on ValueType)
//   int     metadata count (-1 = null)
//   for each metadata entry: string key, encoded primitive value
//
// "string"  = length-prefixed UTF-8 (BinaryWriter default).
// "nstring" = byte present-flag (0 or 1) + optional string.
//
// Metadata primitives supported: Null, Boolean, Integer, Long, Float, Double,
//                                String, DateTime, ByteArray. Nested arrays
//                                and objects in metadata are not supported
//                                (and not used by C1 transforms).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Compact, hand-rolled binary serializer for <see cref="CanonicalDataPoint"/>.
/// </summary>
public sealed class BinaryWriterFormat : ISerializationFormat
{
    private const byte FormatVersion = 1;

    /// <summary>Singleton instance.</summary>
    public static BinaryWriterFormat Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "binary-v1";

    /// <inheritdoc/>
    public byte[] Serialize(CanonicalDataPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(FormatVersion);
            w.Write(point.GatewayId);
            w.Write(point.SourceInstanceId);
            w.Write(point.ProtocolName);
            w.Write(point.DeviceId);
            WriteNString(w, point.DeviceName);
            w.Write(point.TagName);
            w.Write(point.TagPath);
            WriteNString(w, point.OriginalTagName);
            WriteNString(w, point.Unit);
            w.Write((byte)point.Quality);
            WriteNString(w, point.QualityReason);
            w.Write(RequireUtcTicks(point.DeviceTimestamp, nameof(point.DeviceTimestamp)));
            w.Write(RequireUtcTicks(point.GatewayTimestamp, nameof(point.GatewayTimestamp)));
            w.Write(point.SequenceNumber);
            w.Write((byte)point.ValueType);
            WriteValue(w, point.ValueType, point.Value);
            WriteMetadata(w, point.Metadata);
        }
        return ms.ToArray();
    }

    /// <inheritdoc/>
    public CanonicalDataPoint Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var version = r.ReadByte();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported BinaryWriterFormat version: {version}");
        }

        var gatewayId = r.ReadString();
        var sourceInstanceId = r.ReadString();
        var protocolName = r.ReadString();
        var deviceId = r.ReadString();
        var deviceName = ReadNString(r);
        var tagName = r.ReadString();
        var tagPath = r.ReadString();
        var originalTagName = ReadNString(r);
        var unit = ReadNString(r);
        var quality = (DataQuality)r.ReadByte();
        var qualityReason = ReadNString(r);
        var deviceTs = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
        var gatewayTs = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
        var sequence = r.ReadInt64();
        var valueType = (CanonicalValueType)r.ReadByte();
        var value = ReadValue(r, valueType);
        var metadata = ReadMetadata(r);

        return new CanonicalDataPoint
        {
            GatewayId = gatewayId,
            SourceInstanceId = sourceInstanceId,
            ProtocolName = protocolName,
            DeviceId = deviceId,
            DeviceName = deviceName,
            TagName = tagName,
            TagPath = tagPath,
            OriginalTagName = originalTagName,
            Unit = unit,
            Quality = quality,
            QualityReason = qualityReason,
            DeviceTimestamp = deviceTs,
            GatewayTimestamp = gatewayTs,
            SequenceNumber = sequence,
            ValueType = valueType,
            Value = value,
            Metadata = metadata,
        };
    }

    // -- Helpers --------------------------------------------------------------

    private static void WriteNString(BinaryWriter w, string? s)
    {
        if (s is null)
        {
            w.Write((byte)0);
            return;
        }
        w.Write((byte)1);
        w.Write(s);
    }

    private static string? ReadNString(BinaryReader r) =>
        r.ReadByte() == 0 ? null : r.ReadString();

    private static void WriteValue(BinaryWriter w, CanonicalValueType type, object? value)
    {
        switch (type)
        {
            case CanonicalValueType.Null:
                break;
            // Numeric values are coerced to the declared wire width rather than
            // hard-unboxed. An adapter may legitimately box a value whose CLR
            // runtime type differs from the declared CanonicalValueType — e.g.
            // a counter read via a long path but mapped to ValueType.Integer
            // (MTConnect production/parts_count), or a transform that widened
            // an int to long. A direct unbox cast such as `(int)value!` on a
            // boxed `long` throws InvalidCastException; that exception is NOT a
            // SqliteException, so it escapes the buffer write path uncaught and
            // permanently strands the route's intake pump (a single mis-boxed
            // point silently kills ALL delivery on the route). Convert.* is
            // lossless for in-range values and keeps the wire encoding stable
            // (the same logical number serializes to identical bytes), so this
            // is purely a robustness widening — correctly-typed points are
            // unaffected. Out-of-range coercions (e.g. a long exceeding int)
            // still throw OverflowException, which is the right signal that the
            // declared ValueType is genuinely too narrow for the data.
            case CanonicalValueType.Boolean:
                w.Write(Convert.ToBoolean(value!, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.Integer:
                w.Write(Convert.ToInt32(value!, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.Long:
                w.Write(Convert.ToInt64(value!, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.Float:
                w.Write(Convert.ToSingle(value!, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.Double:
                w.Write(Convert.ToDouble(value!, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.String:
                w.Write((string)value!);
                break;
            case CanonicalValueType.DateTime:
                {
                    var dt = (DateTime)value!;
                    w.Write(RequireUtcTicks(dt, "Value"));
                    break;
                }
            case CanonicalValueType.ByteArray:
                {
                    var arr = (byte[])value!;
                    w.Write(arr.Length);
                    w.Write(arr);
                    break;
                }
            case CanonicalValueType.Array:
                {
                    var arr = (object[])value!;
                    w.Write(arr.Length);
                    for (int i = 0; i < arr.Length; i++)
                    {
                        WriteMetadataValue(w, arr[i]);
                    }
                    break;
                }
            case CanonicalValueType.Object:
                {
                    var dict = (IReadOnlyDictionary<string, object>)value!;
                    WriteMetadata(w, dict);
                    break;
                }
            default:
                throw new InvalidDataException($"Unknown CanonicalValueType: {type}");
        }
    }

    private static object? ReadValue(BinaryReader r, CanonicalValueType type)
    {
        switch (type)
        {
            case CanonicalValueType.Null:
                return null;
            case CanonicalValueType.Boolean:
                return r.ReadBoolean();
            case CanonicalValueType.Integer:
                return r.ReadInt32();
            case CanonicalValueType.Long:
                return r.ReadInt64();
            case CanonicalValueType.Float:
                return r.ReadSingle();
            case CanonicalValueType.Double:
                return r.ReadDouble();
            case CanonicalValueType.String:
                return r.ReadString();
            case CanonicalValueType.DateTime:
                return new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            case CanonicalValueType.ByteArray:
                {
                    var len = r.ReadInt32();
                    return r.ReadBytes(len);
                }
            case CanonicalValueType.Array:
                {
                    var len = r.ReadInt32();
                    var arr = new object[len];
                    for (int i = 0; i < len; i++)
                    {
                        arr[i] = ReadMetadataValue(r)!;
                    }
                    return arr;
                }
            case CanonicalValueType.Object:
                return ReadMetadata(r);
            default:
                throw new InvalidDataException($"Unknown CanonicalValueType: {type}");
        }
    }

    // Metadata uses a tagged primitive encoding. Type tag is one byte.
    private const byte MetaNull = 0;
    private const byte MetaBool = 1;
    private const byte MetaInt32 = 2;
    private const byte MetaInt64 = 3;
    private const byte MetaFloat = 4;
    private const byte MetaDouble = 5;
    private const byte MetaString = 6;
    private const byte MetaDateTime = 7;
    private const byte MetaBytes = 8;

    private static void WriteMetadata(BinaryWriter w, IReadOnlyDictionary<string, object>? meta)
    {
        if (meta is null)
        {
            w.Write(-1);
            return;
        }
        w.Write(meta.Count);
        foreach (var kvp in meta)
        {
            w.Write(kvp.Key);
            WriteMetadataValue(w, kvp.Value);
        }
    }

    private static Dictionary<string, object>? ReadMetadata(BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count < 0)
        {
            return null;
        }
        var dict = new Dictionary<string, object>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var key = r.ReadString();
            var value = ReadMetadataValue(r)!;
            dict[key] = value;
        }
        return dict;
    }

    private static void WriteMetadataValue(BinaryWriter w, object? value)
    {
        switch (value)
        {
            case null:
                w.Write(MetaNull);
                break;
            case bool b:
                w.Write(MetaBool); w.Write(b);
                break;
            case int i:
                w.Write(MetaInt32); w.Write(i);
                break;
            case long l:
                w.Write(MetaInt64); w.Write(l);
                break;
            case float f:
                w.Write(MetaFloat); w.Write(f);
                break;
            case double d:
                w.Write(MetaDouble); w.Write(d);
                break;
            case string s:
                w.Write(MetaString); w.Write(s);
                break;
            case DateTime dt:
                w.Write(MetaDateTime); w.Write(RequireUtcTicks(dt, "Metadata DateTime"));
                break;
            case byte[] bytes:
                w.Write(MetaBytes); w.Write(bytes.Length); w.Write(bytes);
                break;
            default:
                throw new InvalidDataException($"Unsupported metadata value runtime type: {value.GetType().Name}");
        }
    }

    /// <summary>
    /// R2 (C2a close-out): fail loud on non-UTC <see cref="DateTime"/> input.
    /// The canonical data model contract mandates that all DateTimes are UTC;
    /// silently calling <c>ToUniversalTime()</c> would shift a Local input
    /// based on the host's timezone, corrupting the timestamp without notice.
    /// The serializer is the right place to enforce this — adapter bugs
    /// surface immediately rather than after data has been buffered.
    /// </summary>
    private static long RequireUtcTicks(DateTime dt, string name)
    {
        if (dt.Kind != DateTimeKind.Utc)
        {
            throw new InvalidDataException(
                $"BinaryWriterFormat: {name} must be DateTimeKind.Utc but was {dt.Kind}. " +
                "Adapters MUST emit canonical points with UTC timestamps (canonical data model contract).");
        }
        return dt.Ticks;
    }

    private static object? ReadMetadataValue(BinaryReader r)
    {
        var tag = r.ReadByte();
        return tag switch
        {
            MetaNull => null,
            MetaBool => r.ReadBoolean(),
            MetaInt32 => r.ReadInt32(),
            MetaInt64 => r.ReadInt64(),
            MetaFloat => r.ReadSingle(),
            MetaDouble => r.ReadDouble(),
            MetaString => r.ReadString(),
            MetaDateTime => new DateTime(r.ReadInt64(), DateTimeKind.Utc),
            MetaBytes => r.ReadBytes(r.ReadInt32()),
            _ => throw new InvalidDataException($"Unknown metadata tag byte: {tag}"),
        };
    }
}
