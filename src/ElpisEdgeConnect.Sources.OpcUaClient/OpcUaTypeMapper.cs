// ============================================================================
// File: OpcUaTypeMapper.cs
// Purpose: Pure-logic translator from Opc.Ua.DataValue (the per-notification
//          unit the UA stack delivers via FastDataChangeCallback) into
//          ElpisEdgeConnect.Core.Model.CanonicalDataPoint. Owns the
//          taxonomy mapping for every UA built-in type the v2.1 §1.1
//          scope commits to.
//
// LOCKED behaviour:
//   * Scalars map per the table in the class XML doc — exhaustive
//   * Arrays map to CanonicalValueType.Array (the outer container; element
//     types are not declared at the canonical-model level)
//   * Null Variant → CanonicalValueType.Null, Value = null
//   * DataValue + Variant unwrapping recurses
//   * DeviceTimestamp = SourceTimestamp when set + non-min; else
//     ServerTimestamp; else UtcNow fallback
//   * GatewayTimestamp = UtcNow at translation time
//   * DateTime values are normalised to UTC if the source didn't (UA spec
//     says all timestamps are UTC, but defensive normalisation costs
//     little and survives a non-spec server)
//   * ExtensionObject → CanonicalValueType.Object with empty dictionary
//     for PR 4a; structure expansion deferred per v2.1 §1.1 + PR 4 plan
//     sign-off — flagged in the metadata as opcua.unexpandedTypeId
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
//            docs/core/canonical-data-model.md (Quality + Value contracts)
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;
using Opc.Ua;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Pure-logic <see cref="DataValue"/> → <see cref="CanonicalDataPoint"/>
/// translator. Handles every UA built-in type from the v2.1 §1.1 locked
/// scope.
/// </summary>
/// <remarks>
/// Scalar mapping:
/// <list type="table">
///   <listheader><term>UA built-in</term><description>→ Canonical</description></listheader>
///   <item><term>Null Variant</term><description>Null, Value = null</description></item>
///   <item><term>Boolean</term><description>Boolean, Value = bool</description></item>
///   <item><term>SByte / Byte / Int16 / UInt16 / Int32</term><description>Integer (int)</description></item>
///   <item><term>UInt32 / Int64 / UInt64</term><description>Long (long; UInt64 may overflow on extreme values, surfaces as Bad-quality with reason)</description></item>
///   <item><term>Float</term><description>Float (float)</description></item>
///   <item><term>Double</term><description>Double (double)</description></item>
///   <item><term>String</term><description>String</description></item>
///   <item><term>DateTime</term><description>DateTime (normalised to UTC)</description></item>
///   <item><term>Guid</term><description>String (canonical "D" formatted)</description></item>
///   <item><term>ByteString</term><description>ByteArray (byte[])</description></item>
///   <item><term>NodeId / ExpandedNodeId</term><description>String (canonical NodeId text)</description></item>
///   <item><term>XmlElement</term><description>String (OuterXml)</description></item>
///   <item><term>StatusCode (as value)</term><description>Long (the 32-bit code as long)</description></item>
///   <item><term>QualifiedName</term><description>String</description></item>
///   <item><term>LocalizedText</term><description>String</description></item>
///   <item><term>ExtensionObject (struct/UDT)</term><description>Object with empty dict + opcua.unexpandedTypeId metadata — structure expansion deferred</description></item>
/// </list>
/// Arrays (any of the above with ValueRank ≥ 1) map to
/// <see cref="CanonicalValueType.Array"/>.
/// </remarks>
internal sealed class OpcUaTypeMapper
{
    private readonly string _gatewayId;
    private readonly string _sourceInstanceId;
    private readonly string _protocolName;
    private readonly string _deviceId;
    private readonly Func<DateTime> _utcNow;

    /// <summary>
    /// Construct a translator scoped to a specific adapter instance.
    /// </summary>
    public OpcUaTypeMapper(
        string gatewayId,
        string sourceInstanceId,
        string protocolName,
        string deviceId,
        Func<DateTime>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        _gatewayId = gatewayId;
        _sourceInstanceId = sourceInstanceId;
        _protocolName = protocolName;
        _deviceId = deviceId;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Translate a single <see cref="DataValue"/> → <see cref="CanonicalDataPoint"/>.
    /// </summary>
    /// <param name="dataValue">The notification's value + quality + timestamps.</param>
    /// <param name="tagName">Canonical tag name from the <see cref="MonitoredItemConfig"/>.</param>
    /// <param name="tagPath">Canonical tag path (defaults to <paramref name="tagName"/> when null).</param>
    public CanonicalDataPoint Translate(DataValue dataValue, string tagName, string? tagPath = null)
    {
        ArgumentNullException.ThrowIfNull(dataValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);

        var (quality, qualityReason) = OpcUaQualityMapper.Map(dataValue.StatusCode);
        var (canonicalType, canonicalValue, additionalMetadata) = MapVariant(dataValue.WrappedValue);

        var deviceTimestamp = ResolveDeviceTimestamp(dataValue);
        var gatewayTimestamp = _utcNow();

        return new CanonicalDataPoint
        {
            GatewayId = _gatewayId,
            SourceInstanceId = _sourceInstanceId,
            ProtocolName = _protocolName,
            DeviceId = _deviceId,
            TagName = tagName,
            TagPath = tagPath ?? tagName,
            Value = canonicalValue,
            ValueType = canonicalType,
            Quality = quality,
            QualityReason = qualityReason,
            DeviceTimestamp = deviceTimestamp,
            GatewayTimestamp = gatewayTimestamp,
            Metadata = additionalMetadata,
        };
    }

    /// <summary>
    /// Map a UA <see cref="Variant"/> into the canonical taxonomy plus
    /// optional additional-metadata dictionary (used today only for the
    /// <c>opcua.unexpandedTypeId</c> marker on ExtensionObject inputs).
    /// </summary>
    internal static (CanonicalValueType Type, object? Value, IReadOnlyDictionary<string, object>? Metadata) MapVariant(Variant variant)
    {
        // Null Variant (BuiltInType.Null or wrapping a null) → Null.
        if (variant.TypeInfo is null || variant.TypeInfo.BuiltInType == BuiltInType.Null || variant.Value is null)
        {
            return (CanonicalValueType.Null, null, null);
        }

        // Arrays — ValueRank ≥ 1. The canonical model only declares the
        // outer Array container type; per-element typing is the
        // consumer's responsibility.
        if (variant.TypeInfo.ValueRank > 0)
        {
            return variant.Value is Array arr
                ? (CanonicalValueType.Array, arr, null)
                : (CanonicalValueType.Null, null, null);
        }

        // Scalar dispatch — exhaustive over the v2.1 §1.1 scope.
        return variant.TypeInfo.BuiltInType switch
        {
            BuiltInType.Boolean    => (CanonicalValueType.Boolean, (bool)variant.Value, null),

            BuiltInType.SByte      => (CanonicalValueType.Integer, (int)(sbyte)variant.Value, null),
            BuiltInType.Byte       => (CanonicalValueType.Integer, (int)(byte)variant.Value, null),
            BuiltInType.Int16      => (CanonicalValueType.Integer, (int)(short)variant.Value, null),
            BuiltInType.UInt16     => (CanonicalValueType.Integer, (int)(ushort)variant.Value, null),
            BuiltInType.Int32      => (CanonicalValueType.Integer, (int)variant.Value, null),

            BuiltInType.UInt32     => (CanonicalValueType.Long, (long)(uint)variant.Value, null),
            BuiltInType.Int64      => (CanonicalValueType.Long, (long)variant.Value, null),
            BuiltInType.UInt64     => (CanonicalValueType.Long, unchecked((long)(ulong)variant.Value), null),

            BuiltInType.Float      => (CanonicalValueType.Float, (float)variant.Value, null),
            BuiltInType.Double     => (CanonicalValueType.Double, (double)variant.Value, null),

            BuiltInType.String     => (CanonicalValueType.String, (string)variant.Value, null),
            BuiltInType.DateTime   => (CanonicalValueType.DateTime, NormaliseToUtc((DateTime)variant.Value), null),
            BuiltInType.Guid       => (CanonicalValueType.String, ((Uuid)variant.Value).GuidString, null),
            BuiltInType.ByteString => (CanonicalValueType.ByteArray, (byte[])variant.Value, null),

            BuiltInType.NodeId         => (CanonicalValueType.String, variant.Value.ToString() ?? string.Empty, null),
            BuiltInType.ExpandedNodeId => (CanonicalValueType.String, variant.Value.ToString() ?? string.Empty, null),
            BuiltInType.XmlElement     => (CanonicalValueType.String, ((System.Xml.XmlElement)variant.Value).OuterXml, null),
            BuiltInType.StatusCode     => (CanonicalValueType.Long, (long)((StatusCode)variant.Value).Code, null),
            BuiltInType.QualifiedName  => (CanonicalValueType.String, ((QualifiedName)variant.Value).ToString() ?? string.Empty, null),
            BuiltInType.LocalizedText  => (CanonicalValueType.String, ((LocalizedText)variant.Value).Text ?? string.Empty, null),

            BuiltInType.ExtensionObject => MapExtensionObject((ExtensionObject)variant.Value),

            // DataValue and Variant types can recurse when servers wrap
            // values pathologically. Unwrap and re-dispatch.
            BuiltInType.DataValue => MapVariant(((DataValue)variant.Value).WrappedValue),
            BuiltInType.Variant   => MapVariant((Variant)variant.Value),

            // Catch-all — any unmapped UA built-in becomes a Null-typed
            // canonical point. The adapter's diagnostics surface the type
            // mismatch elsewhere; downstream sinks see a deterministic
            // shape rather than a runtime exception.
            _ => (CanonicalValueType.Null, null, null),
        };
    }

    /// <summary>
    /// ExtensionObject → CanonicalValueType.Object with empty dict for
    /// PR 4a per the v2.1 §1.1 + PR 4 sign-off — structure expansion
    /// (one struct → multiple canonical points) is deferred to a
    /// post-pilot follow-up. We DO surface the UA <c>TypeId</c> in the
    /// metadata so operators see which vendor schema was bypassed.
    /// </summary>
    private static (CanonicalValueType Type, object? Value, IReadOnlyDictionary<string, object>? Metadata) MapExtensionObject(ExtensionObject ext)
    {
        var body = (IReadOnlyDictionary<string, object>)new Dictionary<string, object>(StringComparer.Ordinal);
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
        if (ext.TypeId is not null)
        {
            metadata["opcua.unexpandedTypeId"] = ext.TypeId.ToString() ?? string.Empty;
        }
        return (CanonicalValueType.Object, body, metadata);
    }

    private static DateTime NormaliseToUtc(DateTime dt) =>
        dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            // Unspecified → treat as UTC (UA spec says all timestamps are
            // UTC; this is a defensive normalisation for non-spec servers).
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        };

    /// <summary>
    /// DeviceTimestamp = SourceTimestamp when populated; else
    /// ServerTimestamp; else current UTC. The DateTime.MinValue check
    /// catches both unset-by-default and explicit "no source timestamp"
    /// servers.
    /// </summary>
    private DateTime ResolveDeviceTimestamp(DataValue dataValue)
    {
        if (dataValue.SourceTimestamp != DateTime.MinValue)
        {
            return NormaliseToUtc(dataValue.SourceTimestamp);
        }
        if (dataValue.ServerTimestamp != DateTime.MinValue)
        {
            return NormaliseToUtc(dataValue.ServerTimestamp);
        }
        return _utcNow();
    }
}
