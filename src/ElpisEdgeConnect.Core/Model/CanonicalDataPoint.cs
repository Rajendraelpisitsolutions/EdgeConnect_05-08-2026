// ============================================================================
// File: Model/CanonicalDataPoint.cs
// Purpose: THE canonical internal data model. Every source adapter emits these;
//          every sink adapter consumes these; every transform operates on these.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.1
//
// LOCKED: This type is an architectural commitment. Changes require blueprint
//         revision. Adding optional fields is acceptable; changing required
//         fields or their semantics is not.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Model;

/// <summary>
/// The normalized internal representation of a single data point flowing through
/// the gateway pipeline. Every device read, event, or subscription update becomes
/// one or more <see cref="CanonicalDataPoint"/> instances before it enters the
/// routing engine.
/// </summary>
/// <remarks>
/// <para>
/// Designed to be immutable and thread-safe so that the same instance can be
/// dispatched to multiple sinks concurrently without copying.
/// </para>
/// <para>
/// Construction should go through <see cref="CanonicalDataPointFactory"/> to
/// ensure monotonic sequence numbers and consistent gateway/source identity.
/// Direct construction via <c>new</c> is intended for tests only.
/// </para>
/// </remarks>
public sealed record CanonicalDataPoint
{
    /// <summary>Identifier of the gateway instance that produced this point.</summary>
    public required string GatewayId { get; init; }

    /// <summary>Identifier of the source connector instance (e.g., "focas-jyoti17").</summary>
    public required string SourceInstanceId { get; init; }

    /// <summary>Protocol module name (e.g., "focas2", "mtlinki", "modbus").</summary>
    public required string ProtocolName { get; init; }

    /// <summary>Identifier of the physical device (e.g., "Jyoti17CNC").</summary>
    public required string DeviceId { get; init; }

    /// <summary>Human-readable device name. Optional.</summary>
    public string? DeviceName { get; init; }

    /// <summary>
    /// The canonical tag name as it should appear downstream. After any tag
    /// mapping transform, this is the name sinks and transforms should use.
    /// </summary>
    public required string TagName { get; init; }

    /// <summary>
    /// Hierarchical path for the tag, used for browse and grouping (e.g.,
    /// "Spindle/Speed"). Not always distinct from TagName.
    /// </summary>
    public required string TagPath { get; init; }

    /// <summary>
    /// The original tag name from the source adapter, before mapping. Useful
    /// for diagnostics and traceback.
    /// </summary>
    public string? OriginalTagName { get; init; }

    /// <summary>
    /// The value. Can be any of the types represented in
    /// <see cref="CanonicalValueType"/>. Must be <c>null</c> when
    /// <see cref="ValueType"/> is <see cref="CanonicalValueType.Null"/>
    /// and must be of the corresponding .NET runtime type for every other
    /// value type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Consistency rule (LOCKED):</b> the correspondence between
    /// <see cref="Value"/> and <see cref="ValueType"/> is the producing
    /// adapter's responsibility. The runtime does NOT validate this on
    /// the hot path because a per-point type check would double allocation
    /// cost at sustained throughput targets. A misbehaving adapter is the
    /// bug; the runtime is not the right layer to correct adapter bugs.
    /// </para>
    /// <para>
    /// Adapter authors should call <see cref="IsConsistent"/> or
    /// <see cref="TryValidateConsistency"/> from their tests to verify
    /// every emitted point. See <c>docs/core/canonical-data-model.md</c>
    /// for the full contract and testing guidance.
    /// </para>
    /// </remarks>
    public required object? Value { get; init; }

    /// <summary>
    /// Declared canonical type of <see cref="Value"/>. See the consistency
    /// rule on <see cref="Value"/> for the required correspondence between
    /// this field and the runtime type of the value.
    /// </summary>
    public required CanonicalValueType ValueType { get; init; }

    /// <summary>Engineering unit string (e.g., "rpm", "mm", "%"). Optional.</summary>
    public string? Unit { get; init; }

    /// <summary>Quality classification for this reading.</summary>
    public required DataQuality Quality { get; init; }

    /// <summary>
    /// Free-text reason accompanying a non-Good quality (e.g., "read timeout",
    /// "device in alarm"). Should be empty or null for Good quality.
    /// </summary>
    public string? QualityReason { get; init; }

    /// <summary>
    /// The timestamp the device reported for this value (if the protocol
    /// provides it), in UTC. If the protocol does not provide device timestamps,
    /// adapters should set this equal to <see cref="GatewayTimestamp"/>.
    /// </summary>
    public required System.DateTime DeviceTimestamp { get; init; }

    /// <summary>
    /// The UTC timestamp at which the gateway acquired or emitted this value.
    /// </summary>
    public required System.DateTime GatewayTimestamp { get; init; }

    /// <summary>
    /// Optional key-value metadata attached by the source adapter, transforms,
    /// or enrichment steps (e.g., site, line, shift).
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Monotonically increasing sequence number per source instance, assigned
    /// by <see cref="CanonicalDataPointFactory"/>. Used by buffer and sink
    /// cursors for ordering and replay. Not marked <c>required</c> (per
    /// blueprint §4.1) so that test fixtures and buffer replay paths can
    /// construct points without immediately allocating a sequence; the
    /// factory sets it on every production construction path.
    /// </summary>
    public long SequenceNumber { get; init; }

    /// <summary>
    /// Verify that this point is internally consistent. Returns <c>true</c>
    /// if (a) the runtime type of <see cref="Value"/> matches the declared
    /// <see cref="ValueType"/>, (b) any <see cref="System.DateTime"/> Value
    /// has <c>Kind == Utc</c>, and (c) <see cref="DeviceTimestamp"/> and
    /// <see cref="GatewayTimestamp"/> are both UTC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is intended for tests, debug tooling, and diagnostic
    /// agents. It is NOT called on the hot path by the runtime. See the
    /// consistency rule documented on <see cref="Value"/>.
    /// </para>
    /// <para>
    /// For the <see cref="CanonicalValueType.Array"/> and
    /// <see cref="CanonicalValueType.Object"/> types, only the outer
    /// container type is checked. Element-level type consistency for
    /// arrays is not verified because the canonical array type does not
    /// constrain its element type at the model level.
    /// </para>
    /// </remarks>
    public bool IsConsistent() => TryValidateConsistency(out _);

    /// <summary>
    /// Like <see cref="IsConsistent"/> but also returns a human-readable
    /// reason string when the point is inconsistent.
    /// </summary>
    /// <param name="reason">
    /// When the method returns <c>false</c>, contains a short explanation
    /// of the mismatch. <c>null</c> when the method returns <c>true</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the point passes every consistency check;
    /// <c>false</c> otherwise.
    /// </returns>
    public bool TryValidateConsistency(out string? reason)
    {
        // 1. Value must match ValueType.
        if (!TryValidateValueType(out reason))
        {
            return false;
        }

        // 2. DeviceTimestamp must be UTC. The canonical model promises UTC
        //    timestamps; local time is a bug per docs/core/canonical-data-model.md.
        if (DeviceTimestamp.Kind != System.DateTimeKind.Utc)
        {
            reason = $"DeviceTimestamp has Kind={DeviceTimestamp.Kind} but must be Utc";
            return false;
        }

        // 3. GatewayTimestamp must be UTC.
        if (GatewayTimestamp.Kind != System.DateTimeKind.Utc)
        {
            reason = $"GatewayTimestamp has Kind={GatewayTimestamp.Kind} but must be Utc";
            return false;
        }

        reason = null;
        return true;
    }

    private bool TryValidateValueType(out string? reason)
    {
        switch (ValueType)
        {
            case CanonicalValueType.Null:
                if (Value is null)
                {
                    reason = null;
                    return true;
                }
                reason = $"ValueType is Null but Value is of runtime type {Value.GetType().Name}";
                return false;

            case CanonicalValueType.Boolean:
                return CheckRuntimeType<bool>(out reason);

            case CanonicalValueType.Integer:
                return CheckRuntimeType<int>(out reason);

            case CanonicalValueType.Long:
                return CheckRuntimeType<long>(out reason);

            case CanonicalValueType.Float:
                return CheckRuntimeType<float>(out reason);

            case CanonicalValueType.Double:
                return CheckRuntimeType<double>(out reason);

            case CanonicalValueType.String:
                return CheckRuntimeType<string>(out reason);

            case CanonicalValueType.DateTime:
                if (Value is System.DateTime dt)
                {
                    if (dt.Kind != System.DateTimeKind.Utc)
                    {
                        reason = $"ValueType is DateTime but Value has Kind={dt.Kind} (expected Utc)";
                        return false;
                    }
                    reason = null;
                    return true;
                }
                reason = Value is null
                    ? "ValueType is DateTime but Value is null"
                    : $"ValueType is DateTime but Value is of runtime type {Value.GetType().Name} (expected DateTime)";
                return false;

            case CanonicalValueType.ByteArray:
                return CheckRuntimeType<byte[]>(out reason);

            case CanonicalValueType.Array:
                if (Value is System.Array)
                {
                    reason = null;
                    return true;
                }
                reason = Value is null
                    ? "ValueType is Array but Value is null"
                    : $"ValueType is Array but Value is of runtime type {Value.GetType().Name}";
                return false;

            case CanonicalValueType.Object:
                if (Value is IReadOnlyDictionary<string, object>)
                {
                    reason = null;
                    return true;
                }
                reason = Value is null
                    ? "ValueType is Object but Value is null"
                    : $"ValueType is Object but Value is of runtime type {Value.GetType().Name} (expected IReadOnlyDictionary<string, object>)";
                return false;

            default:
                reason = $"Unrecognised ValueType {ValueType}";
                return false;
        }
    }

    private bool CheckRuntimeType<T>(out string? reason)
    {
        if (Value is T)
        {
            reason = null;
            return true;
        }
        reason = Value is null
            ? $"ValueType is {ValueType} but Value is null"
            : $"ValueType is {ValueType} but Value is of runtime type {Value.GetType().Name} (expected {typeof(T).Name})";
        return false;
    }
}
