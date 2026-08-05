// ============================================================================
// File: Birth/SparkplugBirthMetricNames.cs
// Purpose: A small, directly-testable published-name validation surface for birth
//          planning (slice-3 review r1 B4). Extracted so the reserved-name and
//          duplicate-name rules can be exercised in isolation even though a valid
//          source-qualified canonical key can never naturally render a reserved or
//          duplicate name (SourceInstanceId/DeviceId are required, non-empty, and
//          contain no '/', so the 3-part join is unambiguous and cannot equal
//          'bdSeq' or 'Node Control/Rebirth'). The checks remain as defense-in-depth
//          — they run BEFORE alias resolution.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §5.5, §9.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Birth;

/// <summary>Validates that application birth metric names are neither reserved nor duplicated.</summary>
internal static class SparkplugBirthMetricNames
{
    /// <summary>Throw when a published name collides with a reserved well-known Sparkplug metric name.</summary>
    /// <param name="name">The published metric name.</param>
    /// <exception cref="AdapterException">Thrown with <see cref="SparkplugErrors.BirthReservedMetricName"/>.</exception>
    public static void RequireNotReserved(string name)
    {
        if (string.Equals(name, SparkplugPayloadEncoder.BdSeqMetricName, StringComparison.Ordinal)
            || string.Equals(name, SparkplugPayloadEncoder.NodeControlRebirthMetricName, StringComparison.Ordinal))
        {
            throw Reject(SparkplugErrors.BirthReservedMetricName,
                $"metric '{name}' collides with a reserved well-known Sparkplug metric name.");
        }
    }

    /// <summary>
    /// Validate a sequence of published names: each must be non-reserved, and the set must
    /// have no ordinal duplicate. Returns the accumulating set so callers can validate
    /// incrementally.
    /// </summary>
    /// <param name="names">The published names, in manifest order.</param>
    /// <exception cref="AdapterException">
    /// Thrown with <see cref="SparkplugErrors.BirthReservedMetricName"/> or
    /// <see cref="SparkplugErrors.BirthDuplicateMetricName"/>.
    /// </exception>
    public static void ValidateAll(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            RequireNotReserved(name);
            if (!seen.Add(name))
            {
                throw Reject(SparkplugErrors.BirthDuplicateMetricName,
                    $"two birth metrics resolve to the same published name '{name}'.");
            }
        }
    }

    private static AdapterException Reject(string code, string message) =>
        new(new AdapterError
        {
            Code = code,
            Category = ErrorCategory.Internal,
            Message = message,
            Retryable = false,
        });
}
