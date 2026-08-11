// ============================================================================
// File: Model/CanonicalDataPointFactory.cs
// Purpose: Thread-safe factory that issues CanonicalDataPoint instances with
//          correct gateway identity and monotonic per-source sequence numbers.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.1, Section 19.6
//
// LOCKED BEHAVIOR:
//   - One factory per (gateway, source instance) pair
//   - Sequence numbers are monotonic within a single factory instance
//   - Concurrent NewPoint calls produce strictly increasing sequence numbers
//   - Sequence starts at 1 (0 is reserved as "no committed cursor")
// ============================================================================

using System;
using System.Threading;

namespace ElpisEdgeConnect.Core.Model;

/// <summary>
/// Thread-safe factory that produces <see cref="CanonicalDataPoint"/> instances
/// with correct gateway identity and monotonically increasing sequence numbers
/// per source instance. Adapters should cache one factory per source instance
/// and call <see cref="NewBuilder"/> or <see cref="CreatePoint"/> for each
/// point emitted.
/// </summary>
/// <remarks>
/// <para>
/// The factory owns the sequence counter for a single source instance. Sequence
/// numbers are strictly monotonic even under concurrent calls from multiple
/// threads, enforced via <see cref="Interlocked.Increment(ref long)"/>.
/// </para>
/// <para>
/// Sequence numbers are not persisted across gateway restarts in Phase 1. The
/// buffer subsystem handles durability separately via its per-sink cursor
/// tracking. Restarting a gateway resets sequence counters to 1 for new points,
/// which is safe because the buffer keeps its own monotonic sequence space.
/// </para>
/// </remarks>
public sealed class CanonicalDataPointFactory
{
    /// <summary>
    /// Metadata key used to surface the source's logical role on every
    /// emitted point. Read by the MQTT sink's <c>{deviceClass}</c> topic
    /// placeholder. Documented in
    /// <c>shared-knowledge/contracts/eremos-per-tag-mqtt.md</c>.
    /// </summary>
    public const string DeviceClassMetadataKey = "deviceClass";

    private readonly string _gatewayId;
    private readonly string _sourceInstanceId;
    private readonly string _protocolName;
    private readonly string _deviceId;
    private readonly string? _deviceName;
    private readonly string? _deviceClass;
    private long _sequenceCounter;

    /// <summary>
    /// Create a new factory for a specific source instance.
    /// </summary>
    /// <param name="gatewayId">Stable gateway identifier.</param>
    /// <param name="sourceInstanceId">Source connector instance id (e.g., "focas-jyoti17").</param>
    /// <param name="protocolName">Protocol module name (e.g., "focas2").</param>
    /// <param name="deviceId">Physical device id.</param>
    /// <param name="deviceName">Optional human-readable device name.</param>
    /// <param name="deviceClass">
    /// Optional logical role of the source (e.g., <c>"cnc"</c>, <c>"plc"</c>).
    /// When non-null, every point produced by this factory carries
    /// <c>Metadata["deviceClass"] = deviceClass</c>. Used by the MQTT sink's
    /// <c>{deviceClass}</c> topic placeholder.
    /// </param>
    public CanonicalDataPointFactory(
        string gatewayId,
        string sourceInstanceId,
        string protocolName,
        string deviceId,
        string? deviceName = null,
        string? deviceClass = null)
    {
        // Identity values are foundational keys used by buffer cursors,
        // diagnostics, license enforcement, and downstream consumers. Reject
        // null, empty, AND whitespace-only inputs — a whitespace-only id is
        // never a legitimate value and accepting it produces silent bugs
        // downstream that are very hard to trace.
        if (string.IsNullOrWhiteSpace(gatewayId))
        {
            throw new ArgumentException("Gateway id cannot be null, empty, or whitespace.", nameof(gatewayId));
        }

        if (string.IsNullOrWhiteSpace(sourceInstanceId))
        {
            throw new ArgumentException("Source instance id cannot be null, empty, or whitespace.", nameof(sourceInstanceId));
        }

        if (string.IsNullOrWhiteSpace(protocolName))
        {
            throw new ArgumentException("Protocol name cannot be null, empty, or whitespace.", nameof(protocolName));
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("Device id cannot be null, empty, or whitespace.", nameof(deviceId));
        }

        // Normalize deviceClass: accept null/empty as "not specified" but
        // reject whitespace-only — that's a config typo masquerading as a value.
        if (deviceClass is not null && string.IsNullOrWhiteSpace(deviceClass))
        {
            throw new ArgumentException(
                "Device class cannot be whitespace-only; pass null to omit.",
                nameof(deviceClass));
        }

        _gatewayId = gatewayId;
        _sourceInstanceId = sourceInstanceId;
        _protocolName = protocolName;
        _deviceId = deviceId;
        _deviceName = deviceName;
        _deviceClass = deviceClass;
        _sequenceCounter = 0;
    }

    /// <summary>The gateway identifier this factory is bound to.</summary>
    public string GatewayId => _gatewayId;

    /// <summary>The source instance identifier this factory is bound to.</summary>
    public string SourceInstanceId => _sourceInstanceId;

    /// <summary>The protocol name this factory is bound to.</summary>
    public string ProtocolName => _protocolName;

    /// <summary>
    /// The device class this factory is bound to, or <see langword="null"/> if
    /// none was supplied at construction. Mirrored into every emitted point's
    /// <see cref="CanonicalDataPoint.Metadata"/> under
    /// <see cref="DeviceClassMetadataKey"/>.
    /// </summary>
    public string? DeviceClass => _deviceClass;

    /// <summary>The last sequence number issued by this factory.</summary>
    public long LastSequenceNumber => Interlocked.Read(ref _sequenceCounter);

    /// <summary>
    /// Allocate and return the next monotonic sequence number. Safe for
    /// concurrent use from multiple threads.
    /// </summary>
    public long NextSequence() => Interlocked.Increment(ref _sequenceCounter);

    /// <summary>
    /// Obtain a pre-populated builder with gateway, source, device identity,
    /// and a freshly allocated sequence number. Caller fills in tag, value,
    /// quality, and timestamps before calling <see cref="CanonicalDataPointBuilder.Build"/>.
    /// </summary>
    public CanonicalDataPointBuilder NewBuilder()
    {
        var builder = new CanonicalDataPointBuilder()
            .WithGateway(_gatewayId)
            .WithSource(_sourceInstanceId, _protocolName)
            .WithDevice(_deviceId, _deviceName)
            .WithSequence(NextSequence());
        if (_deviceClass is not null)
        {
            builder = builder.WithMetadata(
                new System.Collections.Generic.Dictionary<string, object>(System.StringComparer.Ordinal)
                {
                    [DeviceClassMetadataKey] = _deviceClass,
                });
        }
        return builder;
    }

    /// <summary>
    /// Fast path: create a canonical point in a single call. For hot loops
    /// where the builder allocation overhead matters.
    /// </summary>
    public CanonicalDataPoint CreatePoint(
        string tagName,
        string tagPath,
        object? value,
        CanonicalValueType valueType,
        DataQuality quality,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp,
        string? unit = null,
        string? originalTagName = null,
        string? qualityReason = null,
        System.Collections.Generic.IReadOnlyDictionary<string, object>? metadata = null)
    {
        return new CanonicalDataPoint
        {
            GatewayId = _gatewayId,
            SourceInstanceId = _sourceInstanceId,
            ProtocolName = _protocolName,
            DeviceId = _deviceId,
            DeviceName = _deviceName,
            TagName = tagName,
            TagPath = tagPath,
            OriginalTagName = originalTagName,
            Value = value,
            ValueType = valueType,
            Unit = unit,
            Quality = quality,
            QualityReason = qualityReason,
            DeviceTimestamp = deviceTimestamp,
            GatewayTimestamp = gatewayTimestamp,
            // Inject deviceClass into Metadata for sinks (especially MQTT).
            // If the caller passed their own metadata bag, we merge — caller
            // wins on conflict so adapters that override stay in control.
            Metadata = MergeDeviceClass(metadata, _deviceClass),
            SequenceNumber = NextSequence(),
        };
    }

    private static System.Collections.Generic.IReadOnlyDictionary<string, object>? MergeDeviceClass(
        System.Collections.Generic.IReadOnlyDictionary<string, object>? userMetadata,
        string? deviceClass)
    {
        if (deviceClass is null)
        {
            return userMetadata;
        }
        if (userMetadata is null)
        {
            return new System.Collections.Generic.Dictionary<string, object>(System.StringComparer.Ordinal)
            {
                [DeviceClassMetadataKey] = deviceClass,
            };
        }
        if (userMetadata.ContainsKey(DeviceClassMetadataKey))
        {
            // Caller already set deviceClass — respect their choice.
            return userMetadata;
        }
        var merged = new System.Collections.Generic.Dictionary<string, object>(
            userMetadata.Count + 1, System.StringComparer.Ordinal);
        foreach (var kvp in userMetadata)
        {
            merged[kvp.Key] = kvp.Value;
        }
        merged[DeviceClassMetadataKey] = deviceClass;
        return merged;
    }
}
