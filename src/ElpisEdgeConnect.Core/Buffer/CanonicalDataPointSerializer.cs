// ============================================================================
// File: Buffer/CanonicalDataPointSerializer.cs
// Purpose: Facade exposing serialize/deserialize for a chosen
//          ISerializationFormat. Lets the rest of the runtime stay format
//          agnostic until C2a's gate review locks the winner.
// Reference: PHASE1_EXECUTION_PLAN.md C2a
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Facade over an <see cref="ISerializationFormat"/>. Provides single-point
/// and batch round-trip helpers.
/// </summary>
public sealed class CanonicalDataPointSerializer
{
    /// <summary>Default serializer using the BinaryWriter format.</summary>
    public static CanonicalDataPointSerializer Binary { get; } = new(BinaryWriterFormat.Instance);

    /// <summary>Default serializer using the MessagePack format.</summary>
    public static CanonicalDataPointSerializer MessagePack { get; } = new(MessagePackFormat.Instance);

    /// <summary>The active format.</summary>
    public ISerializationFormat Format { get; }

    /// <summary>Construct a serializer over a specific format.</summary>
    public CanonicalDataPointSerializer(ISerializationFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        Format = format;
    }

    /// <summary>Serialize one point.</summary>
    public byte[] Serialize(CanonicalDataPoint point) => Format.Serialize(point);

    /// <summary>Deserialize one point.</summary>
    public CanonicalDataPoint Deserialize(byte[] bytes) => Format.Deserialize(bytes);

    /// <summary>Serialize a batch into one byte array per point.</summary>
    public IReadOnlyList<byte[]> SerializeBatch(IReadOnlyList<CanonicalDataPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var result = new byte[points.Count][];
        for (int i = 0; i < points.Count; i++)
        {
            result[i] = Format.Serialize(points[i]);
        }
        return result;
    }

    /// <summary>Deserialize a batch.</summary>
    public IReadOnlyList<CanonicalDataPoint> DeserializeBatch(IReadOnlyList<byte[]> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        var result = new CanonicalDataPoint[payloads.Count];
        for (int i = 0; i < payloads.Count; i++)
        {
            result[i] = Format.Deserialize(payloads[i]);
        }
        return result;
    }
}
