// ============================================================================
// File: Eremos/TopicShapeAnalyzer.cs
// Purpose: Per the v2 plan §5.2 and §5.3, validate the topic shape AND
//          detect canonical-tag-path collisions after MQTT sanitization.
//
//          Encapsulates the resolved Gate 4 regex AND the collision-
//          detection algorithm. Used both by EremosV2ContractValidator
//          (as a Gate 4 subgate) and by standalone unit tests in
//          TopicShapeCollisionTests.
//
// Reference: docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §5
//            src/ElpisEdgeConnect.Sinks.Mqtt/MqttTopicResolver.cs:67-84
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ElpisEdgeConnect.Sinks.Mqtt;

namespace ElpisEdgeConnect.Integration.Tests.Eremos;

/// <summary>
/// Tools for validating MQTT topic shape against the EREMOS V2 PerTag
/// contract + detecting canonical-tag-path collisions after sanitization.
/// </summary>
public static class TopicShapeAnalyzer
{
    /// <summary>
    /// The Gate 4 regex (v2 §5.2). Five segments:
    /// <c>eremos / {gw} / {deviceClass} / {src} / {sanitized-tag}</c>.
    /// Allows mixed case (canonical tag paths sanitize to mixed case;
    /// MqttTopicResolver preserves case per §5.1) + underscore + hyphen
    /// + alphanumeric. ASCII-only by design — Unicode catalogs would
    /// require both a regex update and a contract update.
    /// </summary>
    public static readonly Regex Phase0TopicRegex = new(
        "^eremos/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+$",
        RegexOptions.Compiled);

    /// <summary>
    /// Validate a single MQTT topic against the Phase 0 topic regex.
    /// Returns true if the topic matches the locked shape.
    /// </summary>
    public static bool IsValidTopicShape(string topic) =>
        !string.IsNullOrEmpty(topic) && Phase0TopicRegex.IsMatch(topic);

    /// <summary>
    /// Detect collisions where two or more distinct canonical tag paths
    /// sanitize to the same MQTT topic segment. Returns the collision
    /// groups; empty enumerable means zero collisions.
    /// </summary>
    /// <param name="canonicalTagPaths">
    /// The set of canonical tag paths configured on a source. Typically
    /// the source's <c>Connection.DataPoints</c> after normalisation,
    /// OR the source's full canonical catalog (BrotherTagMap /
    /// Focas2TagMap) for an exhaustive audit.
    /// </param>
    public static IReadOnlyList<TopicCollision> DetectCollisions(IEnumerable<string> canonicalTagPaths)
    {
        var fallback = "_unknown_"; // Same fallback MqttTopicResolver uses for tag-name placeholder
        var groups = canonicalTagPaths
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => (canonical: p, sanitized: MqttTopicResolver.Sanitize(p, fallback)))
            .GroupBy(x => x.sanitized)
            .Where(g => g.Count() > 1)
            .Select(g => new TopicCollision(g.Key, g.Select(x => x.canonical).Distinct().ToList()))
            .Where(c => c.CollidingCanonicalPaths.Count > 1) // distinct() may collapse duplicates
            .ToList();

        return groups;
    }
}

/// <summary>
/// One collision: the resulting MQTT segment + the distinct canonical
/// tag paths that all sanitize to it.
/// </summary>
public sealed record TopicCollision(
    string MqttSegment,
    IReadOnlyList<string> CollidingCanonicalPaths);
