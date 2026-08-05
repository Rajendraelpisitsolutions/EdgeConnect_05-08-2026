// ============================================================================
// File: Model/CanonicalDataPointBuilder.cs
// Purpose: Fluent builder for adapters constructing CanonicalDataPoint values
//          in a more ergonomic way than the record initializer syntax.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.1, PHASE1_EXECUTION_PLAN.md A1
//
// Usage:
//   var point = builder
//       .WithTag("spindle.speed", "Spindle/Speed")
//       .WithValue(3500.0, CanonicalValueType.Double)
//       .WithUnit("rpm")
//       .WithGoodQuality(deviceTimestamp)
//       .Build();
//
// The factory (CanonicalDataPointFactory) uses this internally and applies
// gateway id, source instance id, protocol, and sequence numbering so that
// adapter code only has to provide the protocol-specific fields.
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Model;

/// <summary>
/// Mutable fluent builder for <see cref="CanonicalDataPoint"/>. Not thread-safe;
/// use one builder per construction.
/// </summary>
/// <remarks>
/// Prefer <see cref="CanonicalDataPointFactory"/> for production code — the
/// factory assigns gateway identity and monotonic sequence numbers automatically.
/// Direct builder use is primarily intended for tests.
/// </remarks>
public sealed class CanonicalDataPointBuilder
{
    private string? _gatewayId;
    private string? _sourceInstanceId;
    private string? _protocolName;
    private string? _deviceId;
    private string? _deviceName;
    private string? _tagName;
    private string? _tagPath;
    private string? _originalTagName;
    private object? _value;
    private CanonicalValueType _valueType = CanonicalValueType.Null;
    private string? _unit;
    private DataQuality _quality = DataQuality.Unknown;
    private string? _qualityReason;
    private DateTime _deviceTimestamp;
    private DateTime _gatewayTimestamp;
    private IReadOnlyDictionary<string, object>? _metadata;
    private long _sequenceNumber;

    /// <summary>Set the identity of the gateway instance.</summary>
    public CanonicalDataPointBuilder WithGateway(string gatewayId)
    {
        _gatewayId = gatewayId;
        return this;
    }

    /// <summary>Set the source connector instance and its protocol name.</summary>
    public CanonicalDataPointBuilder WithSource(string sourceInstanceId, string protocolName)
    {
        _sourceInstanceId = sourceInstanceId;
        _protocolName = protocolName;
        return this;
    }

    /// <summary>Set the physical device identity.</summary>
    public CanonicalDataPointBuilder WithDevice(string deviceId, string? deviceName = null)
    {
        _deviceId = deviceId;
        _deviceName = deviceName;
        return this;
    }

    /// <summary>Set the canonical tag name and path.</summary>
    public CanonicalDataPointBuilder WithTag(string tagName, string tagPath, string? originalTagName = null)
    {
        _tagName = tagName;
        _tagPath = tagPath;
        _originalTagName = originalTagName;
        return this;
    }

    /// <summary>Set the value and its declared canonical type.</summary>
    public CanonicalDataPointBuilder WithValue(object? value, CanonicalValueType valueType)
    {
        _value = value;
        _valueType = valueType;
        return this;
    }

    /// <summary>Set the engineering unit.</summary>
    public CanonicalDataPointBuilder WithUnit(string? unit)
    {
        _unit = unit;
        return this;
    }

    /// <summary>
    /// Mark the point Good and set both device and gateway timestamps. If the
    /// device provides its own timestamp, use that; otherwise pass the current
    /// gateway time and this call will mirror it to both fields.
    /// </summary>
    public CanonicalDataPointBuilder WithGoodQuality(DateTime deviceTimestamp)
    {
        _quality = DataQuality.Good;
        _qualityReason = null;
        _deviceTimestamp = deviceTimestamp;
        _gatewayTimestamp = DateTime.UtcNow;
        return this;
    }

    /// <summary>Mark the point with a specific quality and optional reason.</summary>
    public CanonicalDataPointBuilder WithQuality(DataQuality quality, string? reason = null)
    {
        _quality = quality;
        _qualityReason = reason;
        return this;
    }

    /// <summary>Explicitly set both timestamps.</summary>
    public CanonicalDataPointBuilder WithTimestamps(DateTime deviceTimestamp, DateTime gatewayTimestamp)
    {
        _deviceTimestamp = deviceTimestamp;
        _gatewayTimestamp = gatewayTimestamp;
        return this;
    }

    /// <summary>Attach metadata.</summary>
    public CanonicalDataPointBuilder WithMetadata(IReadOnlyDictionary<string, object>? metadata)
    {
        _metadata = metadata;
        return this;
    }

    /// <summary>Set the sequence number (usually assigned by the factory).</summary>
    public CanonicalDataPointBuilder WithSequence(long sequenceNumber)
    {
        _sequenceNumber = sequenceNumber;
        return this;
    }

    /// <summary>
    /// Construct the <see cref="CanonicalDataPoint"/>. Throws
    /// <see cref="InvalidOperationException"/> if required fields are missing.
    /// </summary>
    public CanonicalDataPoint Build()
    {
        if (string.IsNullOrEmpty(_gatewayId))
        {
            throw new InvalidOperationException("GatewayId is required.");
        }

        if (string.IsNullOrEmpty(_sourceInstanceId))
        {
            throw new InvalidOperationException("SourceInstanceId is required.");
        }

        if (string.IsNullOrEmpty(_protocolName))
        {
            throw new InvalidOperationException("ProtocolName is required.");
        }

        if (string.IsNullOrEmpty(_deviceId))
        {
            throw new InvalidOperationException("DeviceId is required.");
        }

        if (string.IsNullOrEmpty(_tagName))
        {
            throw new InvalidOperationException("TagName is required.");
        }

        if (string.IsNullOrEmpty(_tagPath))
        {
            throw new InvalidOperationException("TagPath is required.");
        }

        if (_deviceTimestamp == default)
        {
            throw new InvalidOperationException("DeviceTimestamp must be set.");
        }

        if (_gatewayTimestamp == default)
        {
            throw new InvalidOperationException("GatewayTimestamp must be set.");
        }

        return new CanonicalDataPoint
        {
            GatewayId = _gatewayId,
            SourceInstanceId = _sourceInstanceId,
            ProtocolName = _protocolName,
            DeviceId = _deviceId,
            DeviceName = _deviceName,
            TagName = _tagName,
            TagPath = _tagPath,
            OriginalTagName = _originalTagName,
            Value = _value,
            ValueType = _valueType,
            Unit = _unit,
            Quality = _quality,
            QualityReason = _qualityReason,
            DeviceTimestamp = _deviceTimestamp,
            GatewayTimestamp = _gatewayTimestamp,
            Metadata = _metadata,
            SequenceNumber = _sequenceNumber,
        };
    }
}
