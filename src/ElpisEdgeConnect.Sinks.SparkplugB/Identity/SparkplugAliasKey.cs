// ============================================================================
// File: Identity/SparkplugAliasKey.cs
// Purpose: The Edge-Node-scoped Sparkplug alias key (ADR-0035 Rule 4 /
//          ADR-0036 Rule 5): SourceInstanceId + DeviceId + canonical TagPath —
//          deliberately NO RouteId, so aliases stay stable when a route is
//          recreated or moved. Mirrors Core's CanonicalMetricKey (all three
//          components non-empty). The published metric name is the
//          source-qualified join `{SourceInstanceId}/{DeviceId}/{TagPath}`;
//          Source/Device must not contain '/' so the join is unambiguous.
//          Well-known metrics (bdSeq, Node Control/Rebirth) have NO alias key —
//          they are structurally excluded from any alias map keyed by this
//          type (plan v2 §3.1).
// Reference: ADR-0035 Rule 4; plan v3 (frozen) §1.
// ============================================================================

using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Identity;

/// <summary>
/// A validated, Edge-Node-scoped alias key. Built via <see cref="Create"/> or
/// <see cref="FromCanonical"/>. Ordinal comparison on the published
/// <see cref="MetricName"/> gives the deterministic metric ordering
/// (plan v2 §3.7).
/// </summary>
public sealed record SparkplugAliasKey : IComparable<SparkplugAliasKey>
{
    private SparkplugAliasKey(string sourceInstanceId, string deviceId, string tagPath)
    {
        SourceInstanceId = sourceInstanceId;
        DeviceId = deviceId;
        TagPath = tagPath;
        MetricName = $"{sourceInstanceId}/{deviceId}/{tagPath}";
    }

    /// <summary>The source connector instance id (non-empty, no '/').</summary>
    public string SourceInstanceId { get; }

    /// <summary>The physical device id (non-empty, no '/').</summary>
    public string DeviceId { get; }

    /// <summary>The canonical hierarchical tag path (non-empty; may contain '/').</summary>
    public string TagPath { get; }

    /// <summary>The source-qualified published metric name (ADR-0035 Rule 4).</summary>
    public string MetricName { get; }

    /// <summary>Create a validated alias key.</summary>
    /// <param name="sourceInstanceId">Source instance id (non-empty, no '/').</param>
    /// <param name="deviceId">Device id (non-empty, no '/').</param>
    /// <param name="tagPath">Canonical tag path (non-empty, no leading/trailing '/').</param>
    /// <returns>The key.</returns>
    /// <exception cref="AdapterException">Thrown with <see cref="SparkplugErrors.IdentityInvalid"/> on any violated constraint.</exception>
    public static SparkplugAliasKey Create(string sourceInstanceId, string deviceId, string tagPath)
    {
        RequireElement(sourceInstanceId, nameof(sourceInstanceId), allowSlash: false);
        RequireElement(deviceId, nameof(deviceId), allowSlash: false);
        RequireElement(tagPath, nameof(tagPath), allowSlash: true);
        if (tagPath[0] == '/' || tagPath[^1] == '/')
        {
            throw Invalid(nameof(tagPath), tagPath, "must not start or end with '/'");
        }

        return new SparkplugAliasKey(sourceInstanceId, deviceId, tagPath);
    }

    /// <summary>Create the alias key for a Core canonical metric identity (already validated non-empty by Core).</summary>
    /// <param name="key">The canonical metric key.</param>
    /// <returns>The alias key.</returns>
    public static SparkplugAliasKey FromCanonical(CanonicalMetricKey key) =>
        Create(key.SourceInstanceId, key.DeviceId, key.TagPath);

    /// <inheritdoc/>
    public int CompareTo(SparkplugAliasKey? other) =>
        other is null ? 1 : string.CompareOrdinal(MetricName, other.MetricName);

    /// <summary>Ordinal metric-name less-than.</summary>
    public static bool operator <(SparkplugAliasKey? left, SparkplugAliasKey? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Ordinal metric-name less-than-or-equal.</summary>
    public static bool operator <=(SparkplugAliasKey? left, SparkplugAliasKey? right) =>
        left is null || left.CompareTo(right) <= 0;

    /// <summary>Ordinal metric-name greater-than.</summary>
    public static bool operator >(SparkplugAliasKey? left, SparkplugAliasKey? right) =>
        left is not null && left.CompareTo(right) > 0;

    /// <summary>Ordinal metric-name greater-than-or-equal.</summary>
    public static bool operator >=(SparkplugAliasKey? left, SparkplugAliasKey? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    private static void RequireElement(string value, string name, bool allowSlash)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw Invalid(name, value, "must be non-empty");
        }

        if (!allowSlash && value.Contains('/', StringComparison.Ordinal))
        {
            throw Invalid(name, value, "must not contain '/' (it would make the source-qualified metric name ambiguous)");
        }
    }

    private static AdapterException Invalid(string name, string? value, string constraint) =>
        new(new AdapterError
        {
            Code = SparkplugErrors.IdentityInvalid,
            Category = ErrorCategory.Configuration,
            Message = $"Alias-key element '{name}' {constraint} (was '{value}').",
            Retryable = false,
        });
}
