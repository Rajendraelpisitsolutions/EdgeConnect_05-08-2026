// ============================================================================
// File: Model/CanonicalValueType.cs
// Purpose: Enumerates the value types carried by CanonicalDataPoint.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.1
// ============================================================================

namespace ElpisEdgeConnect.Core.Model;

/// <summary>
/// The set of value types that a <see cref="CanonicalDataPoint"/> can carry.
/// Every industrial protocol value must be mappable to one of these types after
/// normalization.
/// </summary>
/// <remarks>
/// This enum is deliberately small. Protocol-specific types (e.g. Modbus word,
/// Fanuc BCD) must be converted to one of these canonical types by the adapter.
/// Adding a new type here is an architectural change and requires blueprint
/// revision.
/// </remarks>
public enum CanonicalValueType
{
    /// <summary>Value is null (quality typically Bad or Uncertain).</summary>
    Null = 0,

    /// <summary>Boolean true/false.</summary>
    Boolean = 1,

    /// <summary>32-bit signed integer.</summary>
    Integer = 2,

    /// <summary>64-bit signed integer.</summary>
    Long = 3,

    /// <summary>32-bit floating point.</summary>
    Float = 4,

    /// <summary>64-bit floating point.</summary>
    Double = 5,

    /// <summary>UTF-8 string.</summary>
    String = 6,

    /// <summary>UTC DateTime.</summary>
    DateTime = 7,

    /// <summary>Byte array (raw binary payload).</summary>
    ByteArray = 8,

    /// <summary>Homogeneous array of a primitive type.</summary>
    Array = 9,

    /// <summary>Structured object (dictionary of primitives). Rare; prefer flattening.</summary>
    Object = 10,
}
