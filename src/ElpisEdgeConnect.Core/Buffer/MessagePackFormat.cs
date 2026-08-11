// ============================================================================
// File: Buffer/MessagePackFormat.cs
// Purpose: MessagePack-based serializer for CanonicalDataPoint. The second
//          of two formats prototyped for C2a; the winner is locked at the
//          C2a gate review based on benchmark results.
// Reference: PHASE1_EXECUTION_PLAN.md C2a
//
// Implementation note: Core records (CanonicalDataPoint, etc.) are kept
// serialization-attribute free per the architectural cleanliness goal. We
// route through a buffer-internal DTO that DOES carry MessagePack attributes
// and round-trip the typed value via the typeless contractless resolver.
// ============================================================================

using System;
using System.Collections.Generic;
using MessagePack;
using MessagePack.Resolvers;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// MessagePack-based <see cref="ISerializationFormat"/> implementation.
/// Routes through a buffer-internal DTO so Core records remain free of
/// serialization attributes.
/// </summary>
public sealed class MessagePackFormat : ISerializationFormat
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard
            .WithResolver(TypelessContractlessStandardResolver.Instance);

    /// <summary>Singleton instance.</summary>
    public static MessagePackFormat Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "messagepack-v1";

    /// <inheritdoc/>
    public byte[] Serialize(CanonicalDataPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var dto = ToDto(point);
        return MessagePackSerializer.Serialize(dto, Options);
    }

    /// <inheritdoc/>
    public CanonicalDataPoint Deserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var dto = MessagePackSerializer.Deserialize<CanonicalDataPointDto>(bytes, Options);
        return FromDto(dto);
    }

    private static CanonicalDataPointDto ToDto(CanonicalDataPoint p) => new()
    {
        GatewayId = p.GatewayId,
        SourceInstanceId = p.SourceInstanceId,
        ProtocolName = p.ProtocolName,
        DeviceId = p.DeviceId,
        DeviceName = p.DeviceName,
        TagName = p.TagName,
        TagPath = p.TagPath,
        OriginalTagName = p.OriginalTagName,
        Unit = p.Unit,
        Quality = (byte)p.Quality,
        QualityReason = p.QualityReason,
        // Send timestamps as UTC ticks for byte-stable representation
        // independent of MessagePack's DateTime extension type quirks.
        // R2 (C2a close-out): fail loud on non-UTC, do not silently shift.
        DeviceTimestampTicks = RequireUtcTicks(p.DeviceTimestamp, nameof(p.DeviceTimestamp)),
        GatewayTimestampTicks = RequireUtcTicks(p.GatewayTimestamp, nameof(p.GatewayTimestamp)),
        SequenceNumber = p.SequenceNumber,
        ValueType = (byte)p.ValueType,
        Value = NormalizeForSerialization(p.Value, p.ValueType),
        Metadata = p.Metadata is null ? null : new Dictionary<string, object?>(NormalizeMetadata(p.Metadata)),
    };

    private static CanonicalDataPoint FromDto(CanonicalDataPointDto d) => new()
    {
        GatewayId = d.GatewayId,
        SourceInstanceId = d.SourceInstanceId,
        ProtocolName = d.ProtocolName,
        DeviceId = d.DeviceId,
        DeviceName = d.DeviceName,
        TagName = d.TagName,
        TagPath = d.TagPath,
        OriginalTagName = d.OriginalTagName,
        Unit = d.Unit,
        Quality = (DataQuality)d.Quality,
        QualityReason = d.QualityReason,
        DeviceTimestamp = new DateTime(d.DeviceTimestampTicks, DateTimeKind.Utc),
        GatewayTimestamp = new DateTime(d.GatewayTimestampTicks, DateTimeKind.Utc),
        SequenceNumber = d.SequenceNumber,
        ValueType = (CanonicalValueType)d.ValueType,
        Value = DenormalizeFromSerialization(d.Value, (CanonicalValueType)d.ValueType),
        Metadata = d.Metadata is null
            ? null
            : DenormalizeMetadata(d.Metadata),
    };

    /// <summary>
    /// Apply minimal massaging so the typeless resolver round-trips a value
    /// of the declared CanonicalValueType. DateTime values are flattened to
    /// UTC ticks (long) so the wire encoding is byte-stable; arrays of
    /// primitives become object[] for the resolver to walk.
    /// </summary>
    private static object? NormalizeForSerialization(object? value, CanonicalValueType type)
    {
        if (value is null)
        {
            return null;
        }
        return type switch
        {
            CanonicalValueType.DateTime => RequireUtcTicks((DateTime)value, "Value"),
            _ => value,
        };
    }

    /// <summary>
    /// R2 (C2a close-out): fail loud on non-UTC <see cref="DateTime"/> input.
    /// </summary>
    private static long RequireUtcTicks(DateTime dt, string name)
    {
        if (dt.Kind != DateTimeKind.Utc)
        {
            throw new System.IO.InvalidDataException(
                $"MessagePackFormat: {name} must be DateTimeKind.Utc but was {dt.Kind}. " +
                "Adapters MUST emit canonical points with UTC timestamps (canonical data model contract).");
        }
        return dt.Ticks;
    }

    private static object? DenormalizeFromSerialization(object? value, CanonicalValueType type)
    {
        if (value is null)
        {
            return null;
        }
        switch (type)
        {
            case CanonicalValueType.Boolean: return Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
            case CanonicalValueType.Integer: return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            case CanonicalValueType.Long: return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            case CanonicalValueType.Float: return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            case CanonicalValueType.Double: return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            case CanonicalValueType.String: return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
            case CanonicalValueType.DateTime: return new DateTime(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
            case CanonicalValueType.ByteArray: return (byte[])value;
            case CanonicalValueType.Array:
                {
                    // MessagePack typeless deserializes arrays as object[].
                    return value is object[] arr ? arr : ((IEnumerable<object?>)value).ToArrayOfObjects();
                }
            case CanonicalValueType.Object:
                return value is IDictionary<object, object> raw
                    ? DenormalizeMetadata(ToStringKeyed(raw))
                    : value;
            default:
                return value;
        }
    }

    private static IEnumerable<KeyValuePair<string, object?>> NormalizeMetadata(IReadOnlyDictionary<string, object> source)
    {
        foreach (var kvp in source)
        {
            object? v = kvp.Value;
            if (v is DateTime dt)
            {
                v = RequireUtcTicks(dt, $"Metadata[{kvp.Key}]");
            }
            yield return new KeyValuePair<string, object?>(kvp.Key, v);
        }
    }

    private static Dictionary<string, object> DenormalizeMetadata(Dictionary<string, object?> source)
    {
        var dict = new Dictionary<string, object>(source.Count, StringComparer.Ordinal);
        foreach (var kvp in source)
        {
            if (kvp.Value is not null)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }
        return dict;
    }

    private static Dictionary<string, object?> ToStringKeyed(IDictionary<object, object> raw)
    {
        var dict = new Dictionary<string, object?>(raw.Count, StringComparer.Ordinal);
        foreach (var kvp in raw)
        {
            dict[Convert.ToString(kvp.Key, System.Globalization.CultureInfo.InvariantCulture)!] = kvp.Value;
        }
        return dict;
    }

    /// <summary>
    /// Buffer-internal MessagePack DTO. Public only because MessagePack's
    /// reflection-based resolver requires it; not intended for external use.
    /// Field XML docs are intentionally suppressed — the wire schema is
    /// fixed by <see cref="Name"/> and matches the same field set as
    /// <see cref="CanonicalDataPoint"/>.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
#pragma warning disable CS1591 // see class XML doc above.
    public sealed class CanonicalDataPointDto
    {
        public string GatewayId { get; set; } = string.Empty;
        public string SourceInstanceId { get; set; } = string.Empty;
        public string ProtocolName { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string? DeviceName { get; set; }
        public string TagName { get; set; } = string.Empty;
        public string TagPath { get; set; } = string.Empty;
        public string? OriginalTagName { get; set; }
        public string? Unit { get; set; }
        public byte Quality { get; set; }
        public string? QualityReason { get; set; }
        public long DeviceTimestampTicks { get; set; }
        public long GatewayTimestampTicks { get; set; }
        public long SequenceNumber { get; set; }
        public byte ValueType { get; set; }
        public object? Value { get; set; }
        public Dictionary<string, object?>? Metadata { get; set; }
    }
#pragma warning restore CS1591
}

internal static class MessagePackEnumerableExtensions
{
    public static object[] ToArrayOfObjects(this System.Collections.Generic.IEnumerable<object?> source)
    {
        var list = new System.Collections.Generic.List<object>();
        foreach (var v in source)
        {
            list.Add(v!);
        }
        return list.ToArray();
    }
}
