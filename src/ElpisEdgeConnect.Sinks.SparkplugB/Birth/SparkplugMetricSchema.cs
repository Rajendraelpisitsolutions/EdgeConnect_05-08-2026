// ============================================================================
// File: Birth/SparkplugMetricSchema.cs
// Purpose: The STATIC (birth-only) wire schema of an announced Sparkplug metric.
//          In v1 the NBIRTH announces name + alias + DATATYPE + acquisition
//          timestamp — it does NOT announce Unit or arbitrary static properties
//          (slice-3 review r1 B2). The material-schema classifier therefore models
//          only what is actually announced: the mapped datatype. Unit / static
//          properties become material only once NBIRTH carries them (post-v1); see
//          the v3.3 amendment. Changing an already-announced metric's datatype is a
//          generation-changing MATERIAL mutation (deferred post-K3, plan v3 §5.2).
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §5.2, §9.
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Birth;

/// <summary>
/// The static, birth-only, wire-announced schema of a metric: the mapped Sparkplug
/// datatype. Value equality is the material-change test.
/// </summary>
internal sealed record SparkplugMetricSchema
{
    /// <summary>The mapped Sparkplug wire datatype (the only announced static property in v1).</summary>
    public required SparkplugDataType DataType { get; init; }

    /// <summary>Build the schema for a canonical value type.</summary>
    /// <param name="valueType">The canonical value type.</param>
    /// <returns>The static schema.</returns>
    /// <exception cref="ElpisEdgeConnect.Core.Errors.AdapterException">
    /// Thrown with <see cref="SparkplugErrors.EncodeUnmappableDatatype"/> when the value type
    /// has no Sparkplug equivalent.
    /// </exception>
    public static SparkplugMetricSchema From(CanonicalValueType valueType) =>
        new() { DataType = CanonicalToSparkplugTypeMap.Map(valueType) };
}
