// ============================================================================
// File: Mapping/CanonicalToSparkplugTypeMap.cs
// Purpose: THE single pinned CanonicalValueType -> Sparkplug datatype table
//          (ADR-0035 Rule 5, LOCKED). Collapse rules documented here at the
//          mapping site: Integer/Long map to Int32/Int64 (bit-preserving on
//          the wire, plan v2 §3.2); ByteArray maps to Bytes; Array and Object
//          have no scalar equivalent and are REJECTED with a typed error,
//          never silently dropped; Null declares no real datatype (a known-
//          null metric declares its real type and sets IsNull) and is
//          likewise rejected. Precision notes: the canonical model has no
//          Decimal type, so no Decimal->Double loss arises in v1; DateTime
//          values lose sub-millisecond precision (floored, plan v2 §3.3).
// Reference: ADR-0035 Rule 5; plan v3 (frozen) §3.3.
// ============================================================================

using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Mapping;

/// <summary>
/// The single source of truth for canonical-to-Sparkplug datatype mapping
/// (ADR-0035 Rule 5). Unmappable types throw <see cref="AdapterException"/>
/// with <see cref="SparkplugErrors.EncodeUnmappableDatatype"/>.
/// </summary>
public static class CanonicalToSparkplugTypeMap
{
    /// <summary>Map a canonical value type to its pinned Sparkplug datatype.</summary>
    /// <param name="valueType">The canonical value type.</param>
    /// <returns>The Sparkplug datatype.</returns>
    /// <exception cref="AdapterException">
    /// Thrown with <see cref="SparkplugErrors.EncodeUnmappableDatatype"/> for
    /// <see cref="CanonicalValueType.Array"/>, <see cref="CanonicalValueType.Object"/>,
    /// <see cref="CanonicalValueType.Null"/>, or an undefined value.
    /// </exception>
    public static SparkplugDataType Map(CanonicalValueType valueType) => valueType switch
    {
        CanonicalValueType.Boolean => SparkplugDataType.Boolean,
        CanonicalValueType.Integer => SparkplugDataType.Int32,
        CanonicalValueType.Long => SparkplugDataType.Int64,
        CanonicalValueType.Float => SparkplugDataType.Float,
        CanonicalValueType.Double => SparkplugDataType.Double,
        CanonicalValueType.String => SparkplugDataType.String,
        CanonicalValueType.DateTime => SparkplugDataType.DateTime,
        CanonicalValueType.ByteArray => SparkplugDataType.Bytes,
        CanonicalValueType.Null => throw Unmappable(valueType,
            "CanonicalValueType.Null declares no real datatype; a known-null metric must declare its actual type with IsNull=true."),
        CanonicalValueType.Array => throw Unmappable(valueType,
            "CanonicalValueType.Array has no scalar Sparkplug equivalent in node-only v1."),
        CanonicalValueType.Object => throw Unmappable(valueType,
            "CanonicalValueType.Object has no scalar Sparkplug equivalent in node-only v1."),
        _ => throw Unmappable(valueType, $"CanonicalValueType '{(int)valueType}' is not a defined value."),
    };

    private static AdapterException Unmappable(CanonicalValueType valueType, string detail) =>
        new(new AdapterError
        {
            Code = SparkplugErrors.EncodeUnmappableDatatype,
            Category = ErrorCategory.Configuration,
            Message = $"Cannot map canonical value type '{valueType}' to a Sparkplug datatype: {detail}",
            Retryable = false,
        });
}
