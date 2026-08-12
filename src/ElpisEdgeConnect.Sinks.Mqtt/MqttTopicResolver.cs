// ============================================================================
// File: MqttTopicResolver.cs
// Purpose: Resolves topic templates by substituting placeholders with
//          sanitized runtime values. Sanitization rules are locked per the
//          MQTT sink plan.
// Reference: PHASE2_ENTRY.md — MQTT sink adapter plan
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sinks.Mqtt;

/// <summary>
/// Resolves an MQTT topic template by replacing placeholders with
/// sanitized runtime values. The sanitization rules prevent accidental
/// topic-level injection from field-generated identifiers.
/// </summary>
public static class MqttTopicResolver
{
    /// <summary>
    /// Default value used for the <c>{deviceClass}</c> placeholder when the
    /// emitting source did not declare one. Preserves backward compatibility
    /// with v1 publishers (everything was implicitly CNC). Documented in
    /// <c>shared-knowledge/contracts/eremos-per-tag-mqtt.md</c>.
    /// </summary>
    public const string DefaultDeviceClass = "cnc";

    /// <summary>
    /// Resolve a topic template. Recognized placeholders: <c>{gatewayId}</c>,
    /// <c>{sourceId}</c>, <c>{routeId}</c>, <c>{tagName}</c>,
    /// <c>{deviceClass}</c>. Unrecognized placeholders pass through unchanged.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="template"/> is null or empty.
    /// </exception>
    public static string Resolve(
        string template,
        string? gatewayId = null,
        string? sourceId = null,
        string? routeId = null,
        string? tagName = null,
        string? deviceClass = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(template);

        var result = template
            .Replace("{gatewayId}", Sanitize(gatewayId, "unknown"), StringComparison.Ordinal)
            .Replace("{sourceId}", Sanitize(sourceId, "unknown"), StringComparison.Ordinal)
            .Replace("{routeId}", Sanitize(routeId, "default"), StringComparison.Ordinal)
            .Replace("{tagName}", Sanitize(tagName, "_unknown_"), StringComparison.Ordinal)
            .Replace("{deviceClass}", Sanitize(deviceClass, DefaultDeviceClass), StringComparison.Ordinal);

        return result;
    }

    /// <summary>
    /// Sanitize a single placeholder value for safe use in an MQTT topic
    /// segment. Rules are locked per the plan:
    /// <list type="bullet">
    ///   <item><c>/</c>, <c>+</c>, <c>#</c> → <c>_</c></item>
    ///   <item>null byte stripped</item>
    ///   <item>leading/trailing whitespace trimmed</item>
    ///   <item>null or empty → <paramref name="fallback"/></item>
    ///   <item>case preserved, Unicode preserved</item>
    /// </list>
    /// </summary>
    internal static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        // Replace MQTT wildcard characters and topic separators that would
        // break the topic structure if they appear inside a segment value.
        var safe = trimmed
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace("+", "_", StringComparison.Ordinal)
            .Replace("#", "_", StringComparison.Ordinal)
            .Replace("\0", string.Empty, StringComparison.Ordinal);

        return safe.Length == 0 ? fallback : safe;
    }
}
