// ============================================================================
// File: MqttPayloadSerializer.cs
// Purpose: Serializes a batch of CanonicalDataPoint into a JSON array of
//          objects suitable for the Batch publish mode. Uses Utf8JsonWriter
//          into an ArrayBufferWriter<byte> for zero intermediate string
//          allocation.
// Reference: PHASE2_ENTRY.md — MQTT sink adapter plan
// ============================================================================

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sinks.Mqtt;

/// <summary>
/// Serializes a batch of canonical points to a compact JSON byte array.
/// The output shape is a JSON array of objects, one per point, with
/// camelCase field names and ISO 8601 UTC timestamps.
/// </summary>
public static class MqttPayloadSerializer
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = false,
    };

    /// <summary>
    /// Serialize the batch to a UTF-8 JSON byte array.
    /// </summary>
    public static byte[] SerializeBatch(IReadOnlyList<CanonicalDataPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var buffer = new ArrayBufferWriter<byte>(Math.Max(points.Count * 256, 64));
        using var writer = new Utf8JsonWriter(buffer, WriterOptions);

        writer.WriteStartArray();
        foreach (var point in points)
        {
            writer.WriteStartObject();

            writer.WriteString("gatewayId", point.GatewayId);
            writer.WriteString("sourceInstanceId", point.SourceInstanceId);
            writer.WriteString("protocolName", point.ProtocolName);
            writer.WriteString("deviceId", point.DeviceId);
            if (point.DeviceName is not null)
            {
                writer.WriteString("deviceName", point.DeviceName);
            }
            writer.WriteString("tagName", point.TagName);
            writer.WriteString("tagPath", point.TagPath);
            if (point.OriginalTagName is not null)
            {
                writer.WriteString("originalTagName", point.OriginalTagName);
            }

            WriteValue(writer, "value", point.Value, point.ValueType);
            writer.WriteString("valueType", point.ValueType.ToString());

            if (point.Unit is not null)
            {
                writer.WriteString("unit", point.Unit);
            }

            writer.WriteString("quality", point.Quality.ToString());
            if (point.QualityReason is not null)
            {
                writer.WriteString("qualityReason", point.QualityReason);
            }

            writer.WriteString("deviceTimestamp",
                RawValueFormatter.NormalizeToUtc(point.DeviceTimestamp)
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            writer.WriteString("gatewayTimestamp",
                RawValueFormatter.NormalizeToUtc(point.GatewayTimestamp)
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));

            writer.WriteNumber("sequenceNumber", point.SequenceNumber);

            if (point.Metadata is not null)
            {
                writer.WritePropertyName("metadata");
                JsonSerializer.Serialize(writer, point.Metadata);
            }

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.Flush();

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, string propertyName, object? value, CanonicalValueType valueType)
    {
        if (value is null || valueType == CanonicalValueType.Null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        switch (valueType)
        {
            case CanonicalValueType.Boolean:
                writer.WriteBoolean(propertyName, value is bool b ? b : Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.Integer:
                writer.WriteNumber(propertyName, value is int i ? i : Convert.ToInt32(value, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.Long:
                writer.WriteNumber(propertyName, value is long l ? l : Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.Float:
                writer.WriteNumber(propertyName, value is float f ? f : Convert.ToSingle(value, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.Double:
                writer.WriteNumber(propertyName, value is double d ? d : Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.String:
                writer.WriteString(propertyName, value.ToString());
                break;
            case CanonicalValueType.DateTime:
                var dt = value is DateTime dateTime ? dateTime : DateTime.MinValue;
                writer.WriteString(propertyName,
                    RawValueFormatter.NormalizeToUtc(dt).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                break;
            case CanonicalValueType.ByteArray:
                writer.WriteBase64String(propertyName, value is byte[] bytes ? bytes : Array.Empty<byte>());
                break;
            default:
                writer.WritePropertyName(propertyName);
                JsonSerializer.Serialize(writer, value);
                break;
        }
    }
}
