// ============================================================================
// File: Buffer/ISerializationFormat.cs
// Purpose: Pluggable serialization format for CanonicalDataPoint. Two
//          implementations ship in C2a (BinaryWriterFormat, MessagePackFormat)
//          and a winner is locked at the C2a gate review based on benchmarks.
// Reference: PHASE1_EXECUTION_PLAN.md C2a
// ============================================================================

using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Round-trippable byte-oriented serializer for
/// <see cref="CanonicalDataPoint"/>. Implementations must be:
/// (a) lossless across every <see cref="CanonicalValueType"/>;
/// (b) thread-safe (no per-instance state);
/// (c) deterministic — the same input bytes always decode to an
///     equal-by-value record.
/// </summary>
public interface ISerializationFormat
{
    /// <summary>Stable name (e.g. <c>"binary-v1"</c>, <c>"messagepack-v1"</c>).</summary>
    string Name { get; }

    /// <summary>Serialize a single point to a fresh byte array.</summary>
    byte[] Serialize(CanonicalDataPoint point);

    /// <summary>Deserialize a single point from a byte array.</summary>
    CanonicalDataPoint Deserialize(byte[] bytes);
}
