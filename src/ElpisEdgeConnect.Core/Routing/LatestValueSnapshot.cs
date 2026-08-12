// ============================================================================
// File: Routing/LatestValueSnapshot.cs
// Purpose: Protocol-neutral latest-value snapshot contracts — the current value /
//          null / datatype / quality (+ reason) / unit / static metadata /
//          timestamp for every metric a route has produced, plus the route-schema
//          generation. These are the birth-input AND observed-manifest data
//          contracts a replay-aware sink consumes. The persisted, atomic capture
//          is provided by IReplaySessionStateProvider (K1.2 implements it).
//
// Immutability + datatype contract (PR #180 review rounds 2-3, item 2 /
// ADR-0035 Rule 5): a persisted replay snapshot carries immutable values only.
// Scalar arms (Boolean/Integer/Long/Float/Double/String/DateTime) are stored as-is;
// ByteArray is SUPPORTED (ADR-0035 locks ByteArray -> Bytes) but stored as an
// immutable ImmutableArray<byte> copy so the caller's mutable byte[] is never
// retained or exposed. Array/Object have no scalar equivalent and are rejected here
// exactly as they are at the Sparkplug encoder. CanonicalValueType.Null is rejected
// outright (a persisted manifest row must carry a real datatype; a known-null metric
// uses its real type + IsNull=true). Undefined CanonicalValueType / DataQuality fail
// closed. Static-property values are validated to an immutable scalar set (byte[]
// deep-copied to ImmutableArray<byte>); nested dictionaries/arrays/object graphs are
// rejected. Snapshot keys must equal their own metric identity; the empty snapshot is
// generation-aware (no zero-generation singleton).
// Reference: docs/decisions/0035-*.md Rule 5; docs/decisions/0036-*.md Rule 5;
//            plan v2.3 §3; PR #180 review rounds 1-3.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Immutable identity of a canonical metric within a route: source instance +
/// physical device + canonical tag path. Stable across value changes and route
/// recreation. All components are non-empty (enforced by <see cref="Create"/>; the
/// primitive constructor is private). <c>default</c> has null components and is never
/// a valid snapshot key (a snapshot rejects it).
/// </summary>
public readonly struct CanonicalMetricKey : IEquatable<CanonicalMetricKey>
{
    private CanonicalMetricKey(string sourceInstanceId, string deviceId, string tagPath)
    {
        SourceInstanceId = sourceInstanceId;
        DeviceId = deviceId;
        TagPath = tagPath;
    }

    /// <summary>The source connector instance id (non-empty).</summary>
    public string SourceInstanceId { get; }

    /// <summary>The physical device id (non-empty).</summary>
    public string DeviceId { get; }

    /// <summary>The canonical hierarchical tag path (non-empty).</summary>
    public string TagPath { get; }

    /// <summary>Create a validated key; every component must be non-empty.</summary>
    /// <param name="sourceInstanceId">Source instance id.</param>
    /// <param name="deviceId">Device id.</param>
    /// <param name="tagPath">Canonical tag path.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException">Thrown when any component is null or empty.</exception>
    public static CanonicalMetricKey Create(string sourceInstanceId, string deviceId, string tagPath)
    {
        Require(sourceInstanceId, nameof(sourceInstanceId));
        Require(deviceId, nameof(deviceId));
        Require(tagPath, nameof(tagPath));
        return new CanonicalMetricKey(sourceInstanceId, deviceId, tagPath);
    }

    private static void Require(string value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Metric-key components must be non-empty.", paramName);
        }
    }

    /// <summary>
    /// True when every component is present. <c>default(CanonicalMetricKey)</c> — which
    /// bypasses the private constructor and has null components — is <b>not</b> valid.
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrEmpty(SourceInstanceId) &&
        !string.IsNullOrEmpty(DeviceId) &&
        !string.IsNullOrEmpty(TagPath);

    /// <summary>Throw when the key is not <see cref="IsValid"/> (e.g. a defaulted key).</summary>
    /// <param name="paramName">The parameter name to attribute the failure to.</param>
    /// <exception cref="ArgumentException">Thrown when the key is invalid.</exception>
    internal void EnsureValid(string paramName)
    {
        if (!IsValid)
        {
            throw new ArgumentException(
                "Canonical metric identity must contain source, device and tag path.", paramName);
        }
    }

    /// <inheritdoc/>
    public bool Equals(CanonicalMetricKey other) =>
        string.Equals(SourceInstanceId, other.SourceInstanceId, StringComparison.Ordinal) &&
        string.Equals(DeviceId, other.DeviceId, StringComparison.Ordinal) &&
        string.Equals(TagPath, other.TagPath, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CanonicalMetricKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(SourceInstanceId, DeviceId, TagPath);

    /// <summary>Value equality.</summary>
    public static bool operator ==(CanonicalMetricKey left, CanonicalMetricKey right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(CanonicalMetricKey left, CanonicalMetricKey right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => $"{SourceInstanceId}/{DeviceId}/{TagPath}";
}

/// <summary>
/// The current canonical state of one metric for a persisted replay snapshot: the
/// full host-visible state used for birth and for final-update comparison, plus the
/// manifest metadata (unit, static properties) used for schema-change detection.
/// Constructed via <see cref="Create"/>, which enforces the invariants.
/// <para>
/// Permitted value arms: the immutable scalars (Boolean, Integer, Long, Float, Double,
/// String, DateTime) stored as-is, plus ByteArray (ADR-0035 locks
/// <c>ByteArray → Bytes</c>), which is deep-copied to an immutable
/// <see cref="ImmutableArray{Byte}"/> so the caller's <c>byte[]</c> is never retained.
/// Array/Object have no scalar Sparkplug equivalent and are rejected here just as they
/// are at the encoder. <see cref="CanonicalValueType.Null"/> is rejected outright — a
/// known-null metric declares its real type with <c>IsNull=true</c>.
/// </para>
/// </summary>
public sealed class LatestMetricValue
{
    private LatestMetricValue(
        CanonicalMetricKey metric, CanonicalValueType valueType, object? value, bool isNull,
        DateTimeOffset timestampUtc, DataQuality quality, string? qualityReason, string? unit,
        IReadOnlyDictionary<string, object?>? staticProperties, long routeBufferSequence)
    {
        Metric = metric;
        ValueType = valueType;
        Value = value;
        IsNull = isNull;
        TimestampUtc = timestampUtc;
        Quality = quality;
        QualityReason = qualityReason;
        Unit = unit;
        StaticProperties = staticProperties;
        RouteBufferSequence = routeBufferSequence;
    }

    /// <summary>The metric identity.</summary>
    public CanonicalMetricKey Metric { get; }

    /// <summary>The declared canonical datatype of the metric (its real type even when currently null).</summary>
    public CanonicalValueType ValueType { get; }

    /// <summary>Current value (null iff <see cref="IsNull"/>). An immutable scalar when non-null; a ByteArray is exposed as an immutable <see cref="ImmutableArray{Byte}"/>.</summary>
    public object? Value { get; }

    /// <summary>True when the current value is null.</summary>
    public bool IsNull { get; }

    /// <summary>UTC acquisition timestamp of the current value (normalized to UTC on construction).</summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>Quality of the current value.</summary>
    public DataQuality Quality { get; }

    /// <summary>Reason for a non-Good quality (part of host-visible state for final-update comparison).</summary>
    public string? QualityReason { get; }

    /// <summary>Engineering unit (manifest metadata; a change is a schema change → rebirth).</summary>
    public string? Unit { get; }

    /// <summary>
    /// Static metric properties (manifest metadata; a change is a schema change →
    /// rebirth). Exposed as a read-only wrapper that cannot be downcast to a mutable
    /// dictionary. Property values are expected to be immutable scalars.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? StaticProperties { get; }

    /// <summary>The route-buffer sequence this value was appended at (required, non-negative — ties the value to a replay boundary).</summary>
    public long RouteBufferSequence { get; }

    /// <summary>Create a validated metric value for a persisted snapshot.</summary>
    /// <param name="metric">The metric identity.</param>
    /// <param name="valueType">The declared canonical datatype (immutable scalar arm; its real type even when null).</param>
    /// <param name="value">Current value (null iff <paramref name="isNull"/>; runtime type must match <paramref name="valueType"/>).</param>
    /// <param name="isNull">Whether the value is null.</param>
    /// <param name="timestamp">Acquisition timestamp (normalized to UTC).</param>
    /// <param name="quality">Quality.</param>
    /// <param name="routeBufferSequence">Route-buffer sequence (non-negative).</param>
    /// <param name="qualityReason">Optional non-Good quality reason.</param>
    /// <param name="unit">Optional engineering unit.</param>
    /// <param name="staticProperties">Optional static properties (defensively copied and wrapped read-only).</param>
    /// <returns>The validated value.</returns>
    /// <exception cref="ArgumentException">Thrown when the null/value invariant, arm support, or value/type agreement is violated.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the sequence is negative.</exception>
    public static LatestMetricValue Create(
        CanonicalMetricKey metric,
        CanonicalValueType valueType,
        object? value,
        bool isNull,
        DateTimeOffset timestamp,
        DataQuality quality,
        long routeBufferSequence,
        string? qualityReason = null,
        string? unit = null,
        IReadOnlyDictionary<string, object?>? staticProperties = null)
    {
        // A defaulted metric identity (null components, bypassing the private ctor) is invalid.
        metric.EnsureValid(nameof(metric));

        // Fail closed on undefined enums BEFORE any other logic (covers the null path too).
        if (!Enum.IsDefined(valueType))
        {
            throw new ArgumentException($"Unknown CanonicalValueType '{(int)valueType}'.", nameof(valueType));
        }

        if (!Enum.IsDefined(quality))
        {
            throw new ArgumentException($"Unknown DataQuality '{(int)quality}'.", nameof(quality));
        }

        if (isNull && value is not null)
        {
            throw new ArgumentException("IsNull=true requires a null Value.", nameof(value));
        }

        if (!isNull && value is null)
        {
            throw new ArgumentException("IsNull=false requires a non-null Value.", nameof(value));
        }

        if (routeBufferSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routeBufferSequence), routeBufferSequence,
                "RouteBufferSequence must be non-negative.");
        }

        // A persisted manifest row must carry a real datatype (even when currently null).
        if (valueType == CanonicalValueType.Null)
        {
            throw new ArgumentException(
                "CanonicalValueType.Null is not a valid persisted-snapshot datatype; a known-null metric must declare its real type with IsNull=true.",
                nameof(valueType));
        }

        // Array/Object have no scalar Sparkplug equivalent — rejected here as at the encoder.
        if (valueType is CanonicalValueType.Array or CanonicalValueType.Object)
        {
            throw new ArgumentException(
                $"CanonicalValueType.{valueType} has no scalar equivalent and is not supported in a persisted replay snapshot.",
                nameof(valueType));
        }

        // Normalize/validate a non-null value to its immutable stored form.
        var storedValue = isNull ? null : NormalizeValue(valueType, value!);

        IReadOnlyDictionary<string, object?>? props = staticProperties is null
            ? null
            : ToImmutableStaticProperties(staticProperties);

        return new LatestMetricValue(
            metric, valueType, storedValue, isNull, timestamp.ToUniversalTime(), quality, qualityReason, unit, props, routeBufferSequence);
    }

    /// <summary>The exact CLR input type required for a non-null value of each scalar arm; null for ByteArray (handled specially).</summary>
    private static Type? ExpectedClrType(CanonicalValueType valueType) => valueType switch
    {
        CanonicalValueType.Boolean => typeof(bool),
        CanonicalValueType.Integer => typeof(int),
        CanonicalValueType.Long => typeof(long),
        CanonicalValueType.Float => typeof(float),
        CanonicalValueType.Double => typeof(double),
        CanonicalValueType.String => typeof(string),
        CanonicalValueType.DateTime => typeof(DateTime),
        _ => null,
    };

    /// <summary>Validate a non-null value against its arm and return the immutable stored form (ByteArray → ImmutableArray&lt;byte&gt; copy).</summary>
    private static object NormalizeValue(CanonicalValueType valueType, object value)
    {
        if (valueType == CanonicalValueType.ByteArray)
        {
            if (value is byte[] bytes)
            {
                return ImmutableArray.Create(bytes); // deep copy — caller's array is never retained
            }

            throw new ArgumentException(
                $"A ByteArray value must be a byte[] (was '{value.GetType()}').", nameof(value));
        }

        var expected = ExpectedClrType(valueType);
        if (expected is null || value.GetType() != expected)
        {
            throw new ArgumentException(
                $"Value of runtime type '{value.GetType()}' does not match declared CanonicalValueType.{valueType} (expected '{expected?.Name ?? "<none>"}').",
                nameof(value));
        }

        return value;
    }

    /// <summary>Copy static properties into a read-only map, validating each value is an immutable scalar (byte[] → ImmutableArray&lt;byte&gt;).</summary>
    private static ReadOnlyDictionary<string, object?> ToImmutableStaticProperties(IReadOnlyDictionary<string, object?> input)
    {
        var copy = new Dictionary<string, object?>(input.Count);
        foreach (var (key, value) in input)
        {
            copy[key] = ToImmutableStaticValue(value, key);
        }

        return new ReadOnlyDictionary<string, object?>(copy);
    }

    /// <summary>Return the immutable form of a static-property value or throw for an unsupported (mutable/complex) value.</summary>
    private static object? ToImmutableStaticValue(object? value, string key) => value switch
    {
        null => null,
        bool or sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal or string or DateTime or DateTimeOffset or Guid => value,
        ImmutableArray<byte> => value,
        byte[] bytes => ImmutableArray.Create(bytes), // deep copy to an immutable form
        _ => throw new ArgumentException(
            $"Static-property '{key}' has value type '{value.GetType()}', which is not an allowed immutable scalar; nested dictionaries, arrays and object graphs are rejected.",
            nameof(value)),
    };
}

/// <summary>
/// An immutable point-in-time snapshot of every known metric's current value for a
/// route, tagged with the route-schema generation the values belong to. The input
/// map is defensively copied so the snapshot cannot be mutated after construction,
/// and each entry's key is verified to equal its own metric identity.
/// </summary>
public sealed class LatestValueSnapshot
{
    private readonly Dictionary<CanonicalMetricKey, LatestMetricValue> _values;

    /// <summary>Create a snapshot; the input map is copied and each key is checked against its value's metric identity.</summary>
    /// <param name="generation">The route-schema generation these values belong to.</param>
    /// <param name="values">The metric→current-value map (copied defensively).</param>
    /// <exception cref="ArgumentException">Thrown when a value is null or a key does not equal its value's own metric.</exception>
    public LatestValueSnapshot(
        Adapters.RouteSchemaGeneration generation,
        IReadOnlyDictionary<CanonicalMetricKey, LatestMetricValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var copy = new Dictionary<CanonicalMetricKey, LatestMetricValue>(values.Count);
        foreach (var (key, value) in values)
        {
            if (value is null)
            {
                throw new ArgumentException("Snapshot values must be non-null.", nameof(values));
            }

            // Reject an invalid (e.g. defaulted) key independently of the key==metric check —
            // a default key equals a default value.Metric, but neither is a real identity.
            key.EnsureValid(nameof(values));

            if (!key.Equals(value.Metric))
            {
                throw new ArgumentException(
                    $"Snapshot key '{key}' must equal the metric's own identity '{value.Metric}' (a default/mismatched key is rejected).",
                    nameof(values));
            }

            copy[key] = value;
        }

        Generation = generation;
        _values = copy;
    }

    /// <summary>Create an empty snapshot for a specific route-schema generation (an empty route still has a real generation).</summary>
    /// <param name="generation">The current route-schema generation.</param>
    /// <returns>An empty snapshot at <paramref name="generation"/>.</returns>
    public static LatestValueSnapshot CreateEmpty(Adapters.RouteSchemaGeneration generation) =>
        new(generation, new Dictionary<CanonicalMetricKey, LatestMetricValue>());

    /// <summary>The route-schema generation these values belong to.</summary>
    public Adapters.RouteSchemaGeneration Generation { get; }

    /// <summary>Number of metrics in the snapshot.</summary>
    public int Count => _values.Count;

    /// <summary>All metric identities in the snapshot.</summary>
    public IEnumerable<CanonicalMetricKey> Metrics => _values.Keys;

    /// <summary>The highest route-buffer sequence among the snapshot's metrics, or null when empty.</summary>
    public long? MaxRouteBufferSequence
    {
        get
        {
            long? max = null;
            foreach (var value in _values.Values)
            {
                if (max is null || value.RouteBufferSequence > max)
                {
                    max = value.RouteBufferSequence;
                }
            }

            return max;
        }
    }

    /// <summary>The current value for a metric, or null when the metric is unknown.</summary>
    /// <param name="key">The metric identity.</param>
    /// <returns>The current value, or null.</returns>
    public LatestMetricValue? TryGet(CanonicalMetricKey key) =>
        _values.TryGetValue(key, out var v) ? v : null;
}
