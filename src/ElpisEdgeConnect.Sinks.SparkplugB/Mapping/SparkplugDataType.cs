// ============================================================================
// File: Mapping/SparkplugDataType.cs
// Purpose: The Sparkplug datatype numbers this sink emits, pinned to the
//          DataType enum of the vendored sparkplug_b.proto (Tahu commit
//          46f25e79). Deliberately a first-party enum rather than the
//          generated one so the validated metric model (plan v2 §3.8) does
//          not depend on generated types; a unit test asserts each member
//          equals its generated-enum counterpart, which is what makes this
//          pin meaningful.
// Reference: ADR-0035 Rule 5; plan v3 (frozen) §1 (proto verification).
// ============================================================================

namespace ElpisEdgeConnect.Sinks.SparkplugB.Mapping;

/// <summary>
/// Sparkplug datatype numbers emitted by this sink (the node-only v1 subset of
/// the spec's ~20 types). Values are pinned to the vendored
/// <c>sparkplug_b.proto</c> <c>DataType</c> enum and verified by test.
/// </summary>
#pragma warning disable CA1720 // Identifier contains type name — these are the Sparkplug
                              // specification's own datatype names (DataType enum in the
                              // pinned sparkplug_b.proto); renaming would break the pin.
public enum SparkplugDataType : uint
{
    /// <summary>Signed 32-bit integer (wire: bit-preserved into the uint32 arm).</summary>
    Int32 = 3,

    /// <summary>Signed 64-bit integer (wire: bit-preserved into the uint64 arm).</summary>
    Int64 = 4,

    /// <summary>32-bit floating point.</summary>
    Float = 9,

    /// <summary>64-bit floating point.</summary>
    Double = 10,

    /// <summary>Boolean.</summary>
    Boolean = 11,

    /// <summary>UTF-8 string.</summary>
    String = 12,

    /// <summary>Milliseconds since the Unix epoch, UTC, unsigned 64-bit.</summary>
    DateTime = 13,

    /// <summary>Raw byte array.</summary>
    Bytes = 17,
}
#pragma warning restore CA1720
