// ============================================================================
// File: Birth/SparkplugMetricState.cs
// Purpose: The WIRE-EXACT dynamic state of a metric, derived from the SHARED K2
//          wire normalizer (SparkplugMetricValueMapper) — the single source of
//          truth for value mapping, so there is no second partial implementation
//          (slice-3 review r1 B1). Because it is built from the validated model,
//          all typed rejections (null invariant, CLR type mismatch, unmappable
//          datatype, pre-epoch timestamp, undefined quality) happen here, making
//          birth planning a true pre-CONNECT validation stage. Float/double are
//          compared by their EXACT IEEE bit patterns (so +0.0 vs -0.0 and distinct
//          NaN payloads are unequal, matching the protobuf encoding); byte arrays
//          by contents; DateTimes by encoded milliseconds. Two equal states encode
//          to identical Sparkplug metric fields.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §5.3, §5.4, §9.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Birth;

/// <summary>
/// The wire-exact dynamic state of a single metric. Value equality here is exact
/// wire-byte equality: two equal instances encode identically; an inequality is a
/// real host-visible change since birth. Built only via the shared K2 mapper.
/// </summary>
internal sealed record SparkplugMetricState
{
    /// <summary>The mapped Sparkplug datatype.</summary>
    public required SparkplugDataType DataType { get; init; }

    /// <summary>Whether the metric is null.</summary>
    public required bool IsNull { get; init; }

    /// <summary>The acquisition timestamp in Unix milliseconds (the encoded precision).</summary>
    public required ulong TimestampMs { get; init; }

    /// <summary>The mapped quality code (null = Good, property omitted on the wire).</summary>
    public required int? QualityCode { get; init; }

    /// <summary>The mapped quality reason (derived from the canonical quality; source reason is not transmitted).</summary>
    public required string? QualityReason { get; init; }

    /// <summary>
    /// The bit-exact numeric arm: the wire bits for Int32, Int64, DateTime-ms, and the
    /// IEEE bit pattern for Float/Double. Null when the value is non-numeric or null.
    /// </summary>
    public required ulong? NumericBits { get; init; }

    /// <summary>The boolean arm (null when not a boolean).</summary>
    public required bool? BooleanValue { get; init; }

    /// <summary>The string arm, ordinal-compared (null when not a string).</summary>
    public required string? StringValue { get; init; }

    /// <summary>The byte arm as Base64 so content — not reference — is compared (null when not a byte array).</summary>
    public required string? BytesBase64 { get; init; }

    /// <summary>Build the wire-exact state via the shared K2 mapper (which validates everything).</summary>
    /// <param name="valueType">The canonical value type.</param>
    /// <param name="value">The value (null exactly when <paramref name="isNull"/>).</param>
    /// <param name="isNull">Whether the metric is null.</param>
    /// <param name="acquisitionTimestamp">The acquisition timestamp.</param>
    /// <param name="quality">The canonical data quality.</param>
    /// <returns>The wire-exact state.</returns>
    /// <exception cref="ElpisEdgeConnect.Core.Errors.AdapterException">
    /// Thrown (typed <c>SPARKPLUG.ENCODE_*</c>) for every mapping rejection (null invariant,
    /// CLR type mismatch, unmappable datatype, pre-epoch timestamp, undefined quality).
    /// </exception>
    public static SparkplugMetricState From(
        CanonicalValueType valueType, object? value, bool isNull, DateTimeOffset acquisitionTimestamp, DataQuality quality)
    {
        var model = SparkplugMetricValueMapper.Map(valueType, value, isNull, acquisitionTimestamp, quality);

        ulong? numeric = null;
        bool? boolean = null;
        string? str = null;
        string? bytes = null;

        if (model.UInt32Bits is { } u32)
        {
            numeric = u32;
        }
        else if (model.UInt64Bits is { } u64)
        {
            numeric = u64;
        }
        else if (model.FloatValue is { } f)
        {
            numeric = BitConverter.SingleToUInt32Bits(f); // exact IEEE bits: +0.0 != -0.0, NaN payloads distinct
        }
        else if (model.DoubleValue is { } d)
        {
            numeric = BitConverter.DoubleToUInt64Bits(d);
        }
        else if (model.BooleanValue is { } b)
        {
            boolean = b;
        }
        else if (model.StringValue is { } s)
        {
            str = s;
        }
        else if (model.BytesValue is { } by)
        {
            bytes = Convert.ToBase64String(by.AsSpan());
        }

        return new SparkplugMetricState
        {
            DataType = model.DataType,
            IsNull = model.IsNull,
            TimestampMs = model.TimestampMs,
            QualityCode = model.Quality,
            QualityReason = model.QualityReason,
            NumericBits = numeric,
            BooleanValue = boolean,
            StringValue = str,
            BytesBase64 = bytes,
        };
    }

    /// <summary>Build the state from a Core latest-value snapshot entry.</summary>
    /// <param name="value">The snapshot entry.</param>
    /// <returns>The wire-exact state.</returns>
    public static SparkplugMetricState FromLatestValue(LatestMetricValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return From(value.ValueType, value.Value, value.IsNull, value.TimestampUtc, value.Quality);
    }

    /// <summary>Build the state from a canonical data point (null-ness is value-absence).</summary>
    /// <param name="point">The data point.</param>
    /// <returns>The wire-exact state.</returns>
    public static SparkplugMetricState FromDataPoint(CanonicalDataPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return From(
            point.ValueType, point.Value, point.Value is null,
            SparkplugAcquisitionTimestamp.RequireUtc(point.DeviceTimestamp), point.Quality);
    }
}
