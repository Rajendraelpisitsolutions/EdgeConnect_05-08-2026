// ============================================================================
// File: Mapping/SparkplugMetricValueModel.cs
// Purpose: The VALIDATED per-value metric model (plan v2 §3.8 pipeline stage
//          "validated Sparkplug metric model"). All typed rejections happen in
//          SparkplugMetricValueMapper before an instance exists; a constructed
//          model is wire-encodable by definition, so the later protobuf
//          projection (slice 4) is effectively infallible.
//          The type is deliberately ASSEMBLY-INTERNAL (slice-3 review r1):
//          nothing outside this assembly can construct one, clone one via
//          `with`, or mutate one into an invalid state — the public surface is
//          the payload factories/encoder, not this model. Byte values are
//          ImmutableArray<byte> so no mutable backing array escapes. Signed
//          integers are already bit-preserved into their unsigned wire form
//          (plan v2 §3.2); DateTime values are already whole Unix milliseconds
//          (plan v2 §3.3).
// Reference: ADR-0035 Rule 5 (as amended); plan v3 (frozen) §3.3.
// ============================================================================

using System.Collections.Immutable;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Mapping;

/// <summary>
/// A validated, wire-ready metric value. Exactly one value arm is set unless
/// <see cref="IsNull"/> is true (then no arm is set and the wire carries
/// <c>is_null=true</c>). A null metric never carries a
/// <see cref="QualityReason"/> (frozen contract). Built only by
/// <see cref="SparkplugMetricValueMapper"/>; internal so no invalid instance
/// can be produced outside this assembly.
/// </summary>
internal sealed record SparkplugMetricValueModel
{
    /// <summary>Only <see cref="SparkplugMetricValueMapper"/> (and tests via InternalsVisibleTo) construct instances.</summary>
    internal SparkplugMetricValueModel()
    {
    }

    /// <summary>The pinned Sparkplug datatype (always present, even for a null value).</summary>
    public required SparkplugDataType DataType { get; init; }

    /// <summary>True when the wire carries <c>is_null=true</c> and no value arm.</summary>
    public required bool IsNull { get; init; }

    /// <summary>The metric timestamp in whole Unix milliseconds (UTC acquisition time).</summary>
    public required ulong TimestampMs { get; init; }

    /// <summary>The Sparkplug Quality code, or null to omit the property (Good).</summary>
    public int? Quality { get; init; }

    /// <summary>The controlled QualityReason wire value, or null to omit the property. Always null for a null metric.</summary>
    public string? QualityReason { get; init; }

    /// <summary>Bit-preserved Int32 (uint32 wire arm) — set for <see cref="SparkplugDataType.Int32"/>.</summary>
    public uint? UInt32Bits { get; init; }

    /// <summary>Bit-preserved Int64 (uint64 wire arm) — set for <see cref="SparkplugDataType.Int64"/>, and DateTime milliseconds for <see cref="SparkplugDataType.DateTime"/>.</summary>
    public ulong? UInt64Bits { get; init; }

    /// <summary>Float value — set for <see cref="SparkplugDataType.Float"/>.</summary>
    public float? FloatValue { get; init; }

    /// <summary>Double value — set for <see cref="SparkplugDataType.Double"/>.</summary>
    public double? DoubleValue { get; init; }

    /// <summary>Boolean value — set for <see cref="SparkplugDataType.Boolean"/>.</summary>
    public bool? BooleanValue { get; init; }

    /// <summary>String value — set for <see cref="SparkplugDataType.String"/> (may be empty, never null when set).</summary>
    public string? StringValue { get; init; }

    /// <summary>Byte-array value — set for <see cref="SparkplugDataType.Bytes"/> (may be zero-length; immutable, no mutable backing array escapes).</summary>
    public ImmutableArray<byte>? BytesValue { get; init; }
}
