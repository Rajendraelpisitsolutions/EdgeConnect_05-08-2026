// ============================================================================
// File: Birth/SparkplugMaterialSchemaClassifier.cs
// Purpose: Classifies an incoming metric against the announced birth manifest
//          (plan v3 §5.1/§5.2) and provides the pure fail-closed guard the actor
//          calls (slice-3 review r1). Cases:
//            - FirstObserved  — a canonical key not in the manifest: SUPPORTED
//              same-generation growth (the actor requests a SchemaChange rebirth).
//            - MaterialMutation — an already-announced metric whose announced static
//              schema (the mapped datatype in v1) changed: DEFERRED generation-
//              changing mutation the actor must FAIL CLOSED on. EnsurePublishable
//              throws SPARKPLUG.MATERIAL_SCHEMA_MUTATION so the classification is
//              enforced, not merely observed.
//            - KnownUnchanged — a known metric with an unchanged static schema.
//          Only the ANNOUNCED static schema (v1: datatype) is material; Unit and
//          arbitrary static properties are not announced in v1 (v3.3 amendment).
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §5.1, §5.2, §9.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Birth;

/// <summary>How an incoming metric relates to the announced birth manifest.</summary>
internal enum SparkplugMetricClassification
{
    /// <summary>A known metric whose static schema is unchanged — publish as NDATA.</summary>
    KnownUnchanged = 0,

    /// <summary>A metric not in the manifest — supported same-generation growth (request SchemaChange rebirth).</summary>
    FirstObserved = 1,

    /// <summary>A known metric whose announced static schema (datatype) changed — deferred; fail closed.</summary>
    MaterialMutation = 2,
}

/// <summary>Classifies an incoming metric against the announced birth manifest and enforces it.</summary>
internal static class SparkplugMaterialSchemaClassifier
{
    /// <summary>Classify an incoming metric's key + static schema against the manifest.</summary>
    /// <param name="manifest">The announced static schema per metric.</param>
    /// <param name="key">The incoming metric's alias key.</param>
    /// <param name="incoming">The incoming metric's static schema.</param>
    /// <returns>The classification.</returns>
    public static SparkplugMetricClassification Classify(
        IReadOnlyDictionary<SparkplugAliasKey, SparkplugMetricSchema> manifest,
        SparkplugAliasKey key,
        SparkplugMetricSchema incoming)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(incoming);

        if (!manifest.TryGetValue(key, out var announced))
        {
            return SparkplugMetricClassification.FirstObserved;
        }

        return announced == incoming
            ? SparkplugMetricClassification.KnownUnchanged
            : SparkplugMetricClassification.MaterialMutation;
    }

    /// <summary>Classify a canonical data point against the manifest.</summary>
    /// <param name="manifest">The announced static schema per metric.</param>
    /// <param name="point">The incoming data point.</param>
    /// <returns>The classification.</returns>
    /// <exception cref="AdapterException">
    /// Thrown (typed <c>SPARKPLUG.ENCODE_UNMAPPABLE_DATATYPE</c>) when the point's value type
    /// has no Sparkplug equivalent.
    /// </exception>
    public static SparkplugMetricClassification Classify(
        IReadOnlyDictionary<SparkplugAliasKey, SparkplugMetricSchema> manifest,
        CanonicalDataPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var key = SparkplugAliasKey.FromCanonical(
            CanonicalMetricKey.Create(point.SourceInstanceId, point.DeviceId, point.TagPath));
        return Classify(manifest, key, SparkplugMetricSchema.From(point.ValueType));
    }

    /// <summary>
    /// Fail closed on a material mutation. <see cref="SparkplugMetricClassification.MaterialMutation"/>
    /// throws <see cref="SparkplugErrors.MaterialSchemaMutation"/>; <c>KnownUnchanged</c> and
    /// <c>FirstObserved</c> are legitimate control outcomes and return (the actor performs the
    /// rebirth handshake for first-observed separately). An undefined value fails closed. The actor
    /// calls this before publishing DATA for a known metric (slice 5). (Returning does NOT imply
    /// "publishable" — FirstObserved returns too; the name only marks the mutation gate.)
    /// </summary>
    /// <param name="classification">The classification.</param>
    /// <param name="metricName">The metric's published name (for the diagnostic).</param>
    /// <exception cref="AdapterException">Thrown with <see cref="SparkplugErrors.MaterialSchemaMutation"/> on a material mutation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown fail-closed on an undefined classification.</exception>
    public static void ThrowIfMaterialMutation(SparkplugMetricClassification classification, string metricName)
    {
        switch (classification)
        {
            case SparkplugMetricClassification.KnownUnchanged:
            case SparkplugMetricClassification.FirstObserved:
                return;

            case SparkplugMetricClassification.MaterialMutation:
                throw new AdapterException(new AdapterError
                {
                    Code = SparkplugErrors.MaterialSchemaMutation,
                    Category = ErrorCategory.Internal,
                    Message = $"metric '{metricName}' changed its announced static schema (a generation-changing " +
                              "material mutation, deferred post-K3); refusing to publish.",
                    Retryable = false,
                });

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(classification), classification, "Undefined metric classification; failing closed.");
        }
    }
}
