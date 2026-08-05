// ============================================================================
// File: Configuration/PublishingSettings.cs
// Purpose: Publishing configuration sub-record for sink instances.
// Reference: ARCHITECTURE_BLUEPRINT.md §8.1 (sample), §18.3 (latency targets)
// Milestone: B1
// ============================================================================

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Publishing parameters common to all sink connector instances. Universal
/// fields (batch size, batch interval) are typed and validated by Core.
/// Protocol-specific publishing knobs (e.g., MQTT QoS, HTTP headers) are
/// captured in <see cref="Extras"/> via <see cref="JsonExtensionDataAttribute"/>
/// and parsed by the sink adapter when it initialises.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Extras"/> dictionary preserves any JSON properties that are
/// not declared as typed members on this record. This is how protocol-specific
/// fields like <c>"TopicPrefix"</c>, <c>"QoS"</c>, or <c>"PublishMode"</c>
/// from blueprint §8.1's MQTT sink sample reach the MQTT adapter without
/// Core needing to know what they mean.
/// </para>
/// <para>
/// Equality on this record uses the typed properties only at the record-equality
/// level; the <see cref="Extras"/> dictionary is compared by reference. Configs
/// are not compared for structural equality at runtime, so this is acceptable.
/// </para>
/// </remarks>
public sealed record PublishingSettings
{
    /// <summary>
    /// Target batch size for published messages. Default 100 per blueprint §8.1 sample.
    /// </summary>
    [Range(1, 100_000, ErrorMessage = "BatchSize must be between 1 and 100,000.")]
    [BundleTier(BundleTier.Include)]
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// Maximum time to wait before flushing a partial batch, in milliseconds.
    /// Default 250 ms per blueprint §8.1 sample.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "BatchIntervalMs must be at least 1.")]
    [BundleTier(BundleTier.Include)]
    public int BatchIntervalMs { get; init; } = 250;

    /// <summary>
    /// Captures any additional JSON properties that are not declared as
    /// typed members on this record. Used to pass protocol-specific
    /// publishing knobs (MQTT QoS, HTTP auth headers, etc.) through to the
    /// sink adapter for parsing. Populated automatically by
    /// <see cref="System.Text.Json.JsonSerializer"/>.
    /// </summary>
    /// <remarks>
    /// This is a World-2b overflow boundary for redaction (ADR-0020 Amendment 1):
    /// unknown keys here are operator-authored extras classified by the baseline
    /// (and any protocol overrides), not by a typed tier. It therefore carries no
    /// <c>[BundleTier]</c> and is exempt from the redaction drift guard.
    /// </remarks>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extras { get; init; }
}
