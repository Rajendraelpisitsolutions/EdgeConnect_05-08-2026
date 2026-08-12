// ============================================================================
// File: Mapping/SparkplugMetricValueMapper.cs
// Purpose: Maps one canonical value (declared type + value + null flag +
//          quality + acquisition timestamp) to the validated wire-ready
//          SparkplugMetricValueModel, applying the frozen rules:
//          - validation ORDER (slice-3 review r1): null/value agreement first,
//            then datatype, then timestamp, then quality, then the value arm —
//            stable, local error attribution before any unrelated conversion;
//          - datatype via CanonicalToSparkplugTypeMap (Array/Object/Null
//            rejected, ADR-0035 Rule 5);
//          - signed Int32/Int64 bit-preserved (unchecked two's-complement)
//            into the unsigned wire arms — checked casts are forbidden here,
//            they would corrupt or reject negatives (plan v2 §3.2);
//          - timestamps and DateTime values floored to whole Unix ms with
//            pre-epoch rejection (plan v2 §3.3);
//          - the frozen quality map with controlled QualityReason wire values;
//            a NULL metric keeps its quality code but NEVER carries
//            QualityReason (frozen contract, ADR-0035 Rule 5 amendment).
//          Accepts an empty string and a zero-length byte array as real
//          values (present on the wire), distinct from null.
// Reference: ADR-0035 Rule 5 (as amended); plan v3 (frozen) §3.3, §4.
// ============================================================================

using System.Collections.Immutable;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Mapping;

/// <summary>
/// Builds validated <see cref="SparkplugMetricValueModel"/> instances from
/// canonical inputs. All value-level typed rejections happen here (the
/// validated-model stage); the slice-4 protobuf projection never rejects.
/// Internal alongside the model — the public surface is the payload factories.
/// </summary>
internal static class SparkplugMetricValueMapper
{
    /// <summary>Map one canonical value to its validated wire-ready model.</summary>
    /// <param name="valueType">The declared canonical value type (a real type — never <see cref="CanonicalValueType.Null"/>).</param>
    /// <param name="value">The value; must be null exactly when <paramref name="isNull"/> is true.</param>
    /// <param name="isNull">Whether the value is explicitly null (<c>is_null=true</c> on the wire).</param>
    /// <param name="acquisitionTimestamp">The UTC acquisition instant (becomes the metric timestamp).</param>
    /// <param name="quality">The canonical quality.</param>
    /// <returns>The validated model.</returns>
    /// <exception cref="AdapterException">Thrown with a <see cref="SparkplugErrors"/> code for every rejection path.</exception>
    public static SparkplugMetricValueModel Map(
        CanonicalValueType valueType,
        object? value,
        bool isNull,
        DateTimeOffset acquisitionTimestamp,
        DataQuality quality)
    {
        // 1. Null/value agreement — the fundamental invariant reports first.
        if (isNull && value is not null)
        {
            throw NullInvariant("IsNull=true requires a null value.");
        }

        if (!isNull && value is null)
        {
            throw NullInvariant("IsNull=false requires a non-null value.");
        }

        // 2. Datatype; 3. timestamp; 4. quality.
        var dataType = CanonicalToSparkplugTypeMap.Map(valueType);
        var timestampMs = SparkplugTimestamp.ToUnixMilliseconds(acquisitionTimestamp);
        var (qualityCode, qualityReason) = SparkplugQuality.Map(quality);

        var model = new SparkplugMetricValueModel
        {
            DataType = dataType,
            IsNull = isNull,
            TimestampMs = timestampMs,
            Quality = qualityCode,
            // Frozen contract: a null metric never carries QualityReason.
            QualityReason = isNull ? null : qualityReason,
        };

        if (isNull)
        {
            return model;
        }

        // 5. The value arm.
        return dataType switch
        {
            SparkplugDataType.Int32 => model with { UInt32Bits = unchecked((uint)Require<int>(value!, valueType)) },
            SparkplugDataType.Int64 => model with { UInt64Bits = unchecked((ulong)Require<long>(value!, valueType)) },
            SparkplugDataType.Float => model with { FloatValue = Require<float>(value!, valueType) },
            SparkplugDataType.Double => model with { DoubleValue = Require<double>(value!, valueType) },
            SparkplugDataType.Boolean => model with { BooleanValue = Require<bool>(value!, valueType) },
            SparkplugDataType.String => model with { StringValue = Require<string>(value!, valueType) },
            SparkplugDataType.DateTime => model with { UInt64Bits = SparkplugTimestamp.ValueToUnixMilliseconds(Require<DateTime>(value!, valueType)) },
            SparkplugDataType.Bytes => model with { BytesValue = RequireBytes(value!, valueType) },
            _ => throw NullInvariant($"Unreachable datatype '{dataType}'."), // CanonicalToSparkplugTypeMap only returns the members above.
        };
    }

    private static T Require<T>(object value, CanonicalValueType valueType)
    {
        if (value is T typed)
        {
            return typed;
        }

        throw Mismatch(value, valueType, typeof(T).Name);
    }

    /// <summary>ByteArray accepts byte[] or the ImmutableArray&lt;byte&gt; stored form used by persisted snapshot rows; both yield an immutable copy.</summary>
    private static ImmutableArray<byte> RequireBytes(object value, CanonicalValueType valueType) => value switch
    {
        byte[] bytes => ImmutableArray.Create(bytes),
        ImmutableArray<byte> immutable => immutable,
        _ => throw Mismatch(value, valueType, "byte[] or ImmutableArray<byte>"),
    };

    private static AdapterException Mismatch(object value, CanonicalValueType valueType, string expected) =>
        new(new AdapterError
        {
            Code = SparkplugErrors.EncodeValueTypeMismatch,
            Category = ErrorCategory.Internal,
            Message = $"Value of runtime type '{value.GetType()}' does not match declared CanonicalValueType.{valueType} (expected {expected}).",
            Retryable = false,
        });

    private static AdapterException NullInvariant(string detail) =>
        new(new AdapterError
        {
            Code = SparkplugErrors.EncodeNullInvariant,
            Category = ErrorCategory.Internal,
            Message = detail,
            Retryable = false,
        });
}
