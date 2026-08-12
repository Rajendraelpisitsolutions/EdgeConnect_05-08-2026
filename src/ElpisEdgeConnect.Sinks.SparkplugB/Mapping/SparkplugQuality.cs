// ============================================================================
// File: Mapping/SparkplugQuality.cs
// Purpose: The frozen quality mapping (ADR-0035 Rule 5 as amended 2026-07-19;
//          owner GO): Good OMITS the Quality property entirely (never emit
//          192); Bad = 0; Stale = 500; Uncertain and Unknown map to 0 (BAD)
//          with the CONTROLLED QualityReason wire values "quality uncertain" /
//          "quality unknown". QualityReason is an EdgeConnect-defined
//          property, present ONLY for those two lossy mappings; source
//          free-text reasons (e.g. "read timeout") are NEVER exposed on the
//          wire — they stay in EdgeConnect diagnostics. Any future passthrough
//          requires a separate property and an ADR amendment.
// Reference: plan v3 (frozen) §3.3, §4; spec quality codes 0/192/500
//            ([tck-id-payloads-propertyset-quality-value-value]).
// ============================================================================

using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Mapping;

/// <summary>
/// The frozen <see cref="DataQuality"/> → Sparkplug quality mapping. A result
/// with null <see cref="Quality"/> means the property is omitted (implies
/// GOOD); <see cref="QualityReason"/> is non-null only for the two controlled
/// wire values.
/// </summary>
/// <param name="Quality">The Sparkplug Quality code, or null to omit the property (Good).</param>
/// <param name="QualityReason">The controlled EdgeConnect QualityReason wire value, or null to omit.</param>
public readonly record struct SparkplugQualityResult(int? Quality, string? QualityReason);

/// <summary>The frozen quality mapping table and its wire constants.</summary>
public static class SparkplugQuality
{
    /// <summary>Sparkplug quality code BAD.</summary>
    public const int QualityBad = 0;

    /// <summary>Sparkplug quality code STALE.</summary>
    public const int QualityStale = 500;

    /// <summary>The Quality property key (Sparkplug-standard well-known property).</summary>
    public const string QualityPropertyKey = "Quality";

    /// <summary>The EdgeConnect-defined QualityReason property key (not a Sparkplug-standard property).</summary>
    public const string QualityReasonPropertyKey = "QualityReason";

    /// <summary>Controlled wire value for <see cref="DataQuality.Uncertain"/>.</summary>
    public const string ReasonUncertain = "quality uncertain";

    /// <summary>Controlled wire value for <see cref="DataQuality.Unknown"/>.</summary>
    public const string ReasonUnknown = "quality unknown";

    /// <summary>Map a canonical quality to its frozen Sparkplug encoding.</summary>
    /// <param name="quality">The canonical quality.</param>
    /// <returns>The quality code (or omit) and controlled reason (or omit).</returns>
    /// <exception cref="AdapterException">
    /// Thrown with <see cref="SparkplugErrors.EncodeQualityUndefined"/> for an
    /// undefined <see cref="DataQuality"/> value.
    /// </exception>
    public static SparkplugQualityResult Map(DataQuality quality) => quality switch
    {
        DataQuality.Good => new SparkplugQualityResult(null, null),
        DataQuality.Bad => new SparkplugQualityResult(QualityBad, null),
        DataQuality.Stale => new SparkplugQualityResult(QualityStale, null),
        DataQuality.Uncertain => new SparkplugQualityResult(QualityBad, ReasonUncertain),
        DataQuality.Unknown => new SparkplugQualityResult(QualityBad, ReasonUnknown),
        _ => throw new AdapterException(new AdapterError
        {
            Code = SparkplugErrors.EncodeQualityUndefined,
            Category = ErrorCategory.Internal,
            Message = $"DataQuality '{(int)quality}' is not a defined value.",
            Retryable = false,
        }),
    };
}
