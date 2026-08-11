// ============================================================================
// File: Adapters/ReplaySessionIdentifiers.cs
// Purpose: Typed identifiers for the replay-session lifecycle. Both are readonly
//          structs whose long constructor is PRIVATE — callers must go through the
//          validating Create factory, which rejects negatives. The default value
//          (zero) is valid and safe, so `default(ReplaySessionId)` cannot enter an
//          invalid state. Prevents accidental raw-long coupling and negative ids.
// Reference: docs/decisions/0036-*.md Rule 4/6; PR #180 review round 2 (items 3, 5).
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Identity of one replay session (one Edge-Node session/epoch). Monotonic within a
/// route; a new session (reconnect) gets a new id. Every lifecycle record and
/// <see cref="PublishContext"/> carries it so a delayed callback from an old session
/// can be rejected. Construct via <see cref="Create"/> (rejects negatives);
/// <c>default</c> is 0 and valid.
/// </summary>
public readonly struct ReplaySessionId : IEquatable<ReplaySessionId>
{
    private ReplaySessionId(long value) => Value = value;

    /// <summary>The underlying non-negative monotonic value.</summary>
    public long Value { get; }

    /// <summary>Create a validated session id.</summary>
    /// <param name="value">A non-negative value.</param>
    /// <returns>The session id.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public static ReplaySessionId Create(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "ReplaySessionId must be non-negative.");
        }

        return new ReplaySessionId(value);
    }

    /// <inheritdoc/>
    public bool Equals(ReplaySessionId other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ReplaySessionId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Value equality.</summary>
    public static bool operator ==(ReplaySessionId left, ReplaySessionId right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(ReplaySessionId left, ReplaySessionId right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => $"session:{Value}";
}

/// <summary>
/// Identity of one replay epoch within a session. A session
/// (<see cref="ReplaySessionId"/>) is retained across a same-session rebirth, but each
/// successful NBIRTH (the initial begin, then every rebirth) establishes a NEW epoch.
/// The sink treats the epoch of its most recent successful birth as authoritative and
/// rejects/ignores publish, cutover and rebirth-request inputs whose epoch does not
/// match — so a delayed pre-rebirth batch or cutover cannot affect the current birth
/// generation. Construct via <see cref="Create"/> (rejects negatives); <c>default</c>
/// is 0 and valid.
/// </summary>
public readonly struct ReplayEpochId : IEquatable<ReplayEpochId>
{
    private ReplayEpochId(long value) => Value = value;

    /// <summary>The underlying non-negative monotonic epoch counter.</summary>
    public long Value { get; }

    /// <summary>Create a validated epoch id.</summary>
    /// <param name="value">A non-negative value.</param>
    /// <returns>The epoch id.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public static ReplayEpochId Create(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "ReplayEpochId must be non-negative.");
        }

        return new ReplayEpochId(value);
    }

    /// <inheritdoc/>
    public bool Equals(ReplayEpochId other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ReplayEpochId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Value equality.</summary>
    public static bool operator ==(ReplayEpochId left, ReplayEpochId right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(ReplayEpochId left, ReplayEpochId right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => $"epoch:{Value}";
}

/// <summary>
/// The route-schema generation of a snapshot. A material route-schema change
/// (source, filter, tag mapping, published naming, transform output schema) starts a
/// new generation; birth uses only current-generation rows so a removed metric is not
/// re-announced. Construct via <see cref="Create"/> (rejects negatives);
/// <c>default</c> is 0 and valid.
/// </summary>
public readonly struct RouteSchemaGeneration : IEquatable<RouteSchemaGeneration>
{
    private RouteSchemaGeneration(long value) => Value = value;

    /// <summary>The underlying non-negative monotonic generation counter.</summary>
    public long Value { get; }

    /// <summary>Create a validated generation.</summary>
    /// <param name="value">A non-negative value.</param>
    /// <returns>The generation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public static RouteSchemaGeneration Create(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "RouteSchemaGeneration must be non-negative.");
        }

        return new RouteSchemaGeneration(value);
    }

    /// <inheritdoc/>
    public bool Equals(RouteSchemaGeneration other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RouteSchemaGeneration other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Value equality.</summary>
    public static bool operator ==(RouteSchemaGeneration left, RouteSchemaGeneration right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(RouteSchemaGeneration left, RouteSchemaGeneration right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => $"gen:{Value}";
}
