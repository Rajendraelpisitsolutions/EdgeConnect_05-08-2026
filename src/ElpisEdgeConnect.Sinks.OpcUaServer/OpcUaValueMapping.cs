// ============================================================================
// File: OpcUaValueMapping.cs
// Purpose: Map between EdgeConnect's CanonicalValueType / CanonicalDataPoint
//          payload shape and OPC UA's DataType + Variant.
//
//          OPC UA requires a static DataType per Variable node. Once
//          a NodeId carries a DataType, switching it to a different
//          DataType is a contract change (per the namespace policy).
//          So this helper is the single source of truth for that
//          mapping; the node manager calls it once at node creation
//          (to set DataType) and on every value update (to wrap the
//          payload in a Variant).
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.1
//            shared-knowledge/contracts/opcua-namespace-policy.md
// ============================================================================

using System;
using System.Globalization;
using ElpisEdgeConnect.Core.Model;
using Opc.Ua;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Static value-mapping helpers between EdgeConnect canonical types
/// and OPC UA wire types.
/// </summary>
public static class OpcUaValueMapping
{
    /// <summary>
    /// Resolve the OPC UA DataType <see cref="NodeId"/> for a
    /// canonical type. Returned NodeIds reference the standard
    /// OPC UA namespace 0 BuiltInType identifiers.
    /// </summary>
    public static NodeId ResolveDataTypeId(CanonicalValueType type) => type switch
    {
        CanonicalValueType.Boolean => DataTypeIds.Boolean,
        CanonicalValueType.Integer => DataTypeIds.Int32,
        CanonicalValueType.Long => DataTypeIds.Int64,
        CanonicalValueType.Float => DataTypeIds.Float,
        CanonicalValueType.Double => DataTypeIds.Double,
        CanonicalValueType.String => DataTypeIds.String,
        CanonicalValueType.DateTime => DataTypeIds.DateTime,
        CanonicalValueType.ByteArray => DataTypeIds.ByteString,

        // Heterogeneous / unknown payloads degrade to the OPC UA
        // BaseDataType — clients can still read the Variant but
        // strongly-typed binding is on them.
        CanonicalValueType.Array => DataTypeIds.BaseDataType,
        CanonicalValueType.Object => DataTypeIds.Structure,
        CanonicalValueType.Null => DataTypeIds.BaseDataType,
        _ => DataTypeIds.BaseDataType,
    };

    /// <summary>
    /// Resolve the OPC UA <c>ValueRank</c> for a canonical type.
    /// <c>-1</c> (Scalar) for primitives; <c>1</c> (OneDimension) for
    /// arrays and byte-strings handled as raw bytes.
    /// </summary>
    public static int ResolveValueRank(CanonicalValueType type) => type switch
    {
        CanonicalValueType.Array => ValueRanks.OneDimension,
        _ => ValueRanks.Scalar,
    };

    /// <summary>
    /// Wrap a canonical value into an OPC UA <see cref="Variant"/>.
    /// Null values + <see cref="CanonicalValueType.Null"/> map to
    /// <see cref="Variant.Null"/>.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for setting the accompanying
    /// <see cref="StatusCode"/> from <see cref="OpcUaQualityMapping"/>.
    /// </remarks>
    public static Variant ToVariant(object? value, CanonicalValueType type)
    {
        if (value is null || type == CanonicalValueType.Null)
        {
            return Variant.Null;
        }

        return type switch
        {
            CanonicalValueType.Boolean => new Variant((bool)value),
            CanonicalValueType.Integer => new Variant(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            CanonicalValueType.Long => new Variant(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            CanonicalValueType.Float => new Variant(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            CanonicalValueType.Double => new Variant(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            CanonicalValueType.String => new Variant(value.ToString() ?? string.Empty),
            CanonicalValueType.DateTime => new Variant(ToUtc(value)),
            CanonicalValueType.ByteArray => new Variant((byte[])value),
            CanonicalValueType.Array => new Variant(value),
            CanonicalValueType.Object => new Variant(value.ToString() ?? string.Empty),
            _ => Variant.Null,
        };
    }

    private static DateTime ToUtc(object value)
    {
        var dt = (DateTime)value;
        return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
    }
}
