// ============================================================================
// File: Payloads/SparkplugMetricSample.cs
// Purpose: One canonical metric observation handed to the payload factories
//          (NBIRTH current values from the birth snapshot; NDATA change
//          events). A plain INPUT record — every sample is re-validated by
//          the mapper inside the factory before any bytes exist, so this type
//          carries no invariant of its own. K3 builds these from
//          LatestMetricValue rows and routed CanonicalDataPoints.
// Reference: plan v2 §1.6; plan v3 (frozen) §2.
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Payloads;

/// <summary>
/// One metric observation to encode. Validation (type agreement, null
/// invariant, timestamp range, quality mapping) happens inside the payload
/// factory via the validated-model stage.
/// </summary>
public sealed record SparkplugMetricSample
{
    /// <summary>The Edge-Node-scoped alias key (also supplies the published metric name).</summary>
    public required SparkplugAliasKey Key { get; init; }

    /// <summary>The declared canonical value type (a real type — never <c>Null</c>).</summary>
    public required CanonicalValueType ValueType { get; init; }

    /// <summary>The value; null exactly when <see cref="IsNull"/> is true.</summary>
    public object? Value { get; init; }

    /// <summary>Whether the value is explicitly null (<c>is_null=true</c> on the wire).</summary>
    public required bool IsNull { get; init; }

    /// <summary>The UTC acquisition instant (becomes the metric timestamp).</summary>
    public required DateTimeOffset AcquisitionTimestamp { get; init; }

    /// <summary>The canonical quality.</summary>
    public required DataQuality Quality { get; init; }
}
