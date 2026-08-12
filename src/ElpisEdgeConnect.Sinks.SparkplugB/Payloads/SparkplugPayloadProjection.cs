// ============================================================================
// File: Payloads/SparkplugPayloadProjection.cs
// Purpose: The final, effectively-infallible projection stage (plan v2 §3.8):
//          validated models + structural decisions -> generated protobuf
//          Payload -> bytes. NO validation happens here — every input is
//          already a validated SparkplugMetricValueModel or a constrained
//          domain type. Quality properties project deterministically:
//          "Quality" (PropertyValue type Int32=3, int_value) first, then
//          "QualityReason" (type String=12, string_value) when present —
//          parallel keys/values lists, order controlled here (plan v2 §3.4).
// Reference: ADR-0035 Rules 2/5; spec [tck-id-payloads-propertyset-quality-*].
// ============================================================================

using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using Google.Protobuf;
using Org.Eclipse.Tahu.Protobuf;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Payloads;

/// <summary>
/// Projects validated pieces into the generated protobuf types and serializes.
/// Internal: the public surface is <see cref="SparkplugPayloadEncoder"/>.
/// </summary>
internal static class SparkplugPayloadProjection
{
    /// <summary>PropertyValue 'type' for a Signed 32-bit Integer property ([tck-id-payloads-propertyset-quality-value-type]).</summary>
    private const uint PropertyTypeInt32 = 3;

    /// <summary>PropertyValue 'type' for a String property.</summary>
    private const uint PropertyTypeString = 12;

    /// <summary>Project one metric from its validated model.</summary>
    /// <param name="model">The validated value model.</param>
    /// <param name="name">The metric name, or null to omit (alias-only NDATA metrics).</param>
    /// <param name="alias">The alias, or null to omit (e.g. Node Control/Rebirth, NDEATH bdSeq).</param>
    /// <param name="isHistorical">Whether to mark the metric <c>is_historical=true</c>.</param>
    /// <param name="includeTimestamp">Whether to include the metric timestamp (omitted only in NDEATH).</param>
    /// <returns>The generated metric.</returns>
    internal static Payload.Types.Metric ProjectMetric(
        SparkplugMetricValueModel model,
        string? name,
        ulong? alias,
        bool isHistorical,
        bool includeTimestamp = true)
    {
        var metric = new Payload.Types.Metric
        {
            Datatype = (uint)model.DataType,
        };

        if (name is not null)
        {
            metric.Name = name;
        }

        if (alias is { } aliasValue)
        {
            metric.Alias = aliasValue;
        }

        if (includeTimestamp)
        {
            metric.Timestamp = model.TimestampMs;
        }

        if (isHistorical)
        {
            metric.IsHistorical = true;
        }

        if (model.IsNull)
        {
            metric.IsNull = true;
        }
        else
        {
            SetValueArm(metric, model);
        }

        if (model.Quality is { } quality)
        {
            metric.Properties = BuildQualityProperties(quality, model.QualityReason);
        }

        return metric;
    }

    /// <summary>Serialize a payload to its wire bytes.</summary>
    /// <param name="payload">The payload.</param>
    /// <returns>The bytes.</returns>
    internal static byte[] Serialize(Payload payload) => payload.ToByteArray();

    private static void SetValueArm(Payload.Types.Metric metric, SparkplugMetricValueModel model)
    {
        switch (model.DataType)
        {
            case SparkplugDataType.Int32:
                metric.IntValue = model.UInt32Bits!.Value;
                break;
            case SparkplugDataType.Int64:
            case SparkplugDataType.DateTime:
                metric.LongValue = model.UInt64Bits!.Value;
                break;
            case SparkplugDataType.Float:
                metric.FloatValue = model.FloatValue!.Value;
                break;
            case SparkplugDataType.Double:
                metric.DoubleValue = model.DoubleValue!.Value;
                break;
            case SparkplugDataType.Boolean:
                metric.BooleanValue = model.BooleanValue!.Value;
                break;
            case SparkplugDataType.String:
                metric.StringValue = model.StringValue!;
                break;
            case SparkplugDataType.Bytes:
                metric.BytesValue = ByteString.CopyFrom(model.BytesValue!.Value.AsSpan());
                break;
            default:
                // Unreachable: the model's DataType is produced only by CanonicalToSparkplugTypeMap.
                throw new InvalidOperationException($"Unprojectable datatype '{model.DataType}'.");
        }
    }

    private static Payload.Types.PropertySet BuildQualityProperties(int quality, string? reason)
    {
        var properties = new Payload.Types.PropertySet();

        properties.Keys.Add(SparkplugQuality.QualityPropertyKey);
        properties.Values.Add(new Payload.Types.PropertyValue
        {
            Type = PropertyTypeInt32,
            IntValue = unchecked((uint)quality),
        });

        if (reason is not null)
        {
            properties.Keys.Add(SparkplugQuality.QualityReasonPropertyKey);
            properties.Values.Add(new Payload.Types.PropertyValue
            {
                Type = PropertyTypeString,
                StringValue = reason,
            });
        }

        return properties;
    }
}
