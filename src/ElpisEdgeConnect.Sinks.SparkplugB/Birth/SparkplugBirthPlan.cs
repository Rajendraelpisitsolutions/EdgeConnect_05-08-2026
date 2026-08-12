// ============================================================================
// File: Birth/SparkplugBirthPlan.cs
// Purpose: The immutable, deterministic result of planning an NBIRTH from a Core
//          latest-value snapshot (SparkplugBirthPlan), plus the alias-resolved
//          candidate manifest (ResolvedSparkplugBirthPlan) that the actor promotes
//          atomically only after a successful NBIRTH. All collections are immutable
//          (ImmutableArray / FrozenDictionary) so a caller cannot mutate the birth
//          authority (slice-3 review r1 B3/B4). Pure — no store, no MQTT.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.4, §9.
// ============================================================================

using System.Collections.Frozen;
using System.Collections.Immutable;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Birth;

/// <summary>
/// The planned NBIRTH manifest and derived state, before alias resolution: the samples
/// to birth (ordinal order), the keys to resolve aliases for, the wire-exact baseline,
/// and the static schema. An empty plan (empty route) is valid. Immutable.
/// </summary>
internal sealed record SparkplugBirthPlan
{
    /// <summary>The application metric samples to announce, in ordinal published-name order.</summary>
    public required ImmutableArray<SparkplugMetricSample> Metrics { get; init; }

    /// <summary>The distinct alias keys to resolve against the identity store (ordinal order).</summary>
    public required ImmutableArray<SparkplugAliasKey> ManifestKeys { get; init; }

    /// <summary>The wire-exact state of each metric at birth (the dirty-comparator baseline).</summary>
    public required FrozenDictionary<SparkplugAliasKey, SparkplugMetricState> Baseline { get; init; }

    /// <summary>The static (birth-only) schema of each announced metric (for material-change detection).</summary>
    public required FrozenDictionary<SparkplugAliasKey, SparkplugMetricSchema> Schema { get; init; }

    /// <summary>Whether the plan announces no application metrics (an empty route still births).</summary>
    public bool IsEmpty => Metrics.Length == 0;
}

/// <summary>
/// The alias-resolved birth manifest: one coherent, immutable candidate the actor feeds
/// to the K2 encoder (the ordered samples + the exact alias map) and promotes as the new
/// birth generation only after a successful NBIRTH. Its alias map is validated to be an
/// exact set match with the plan's manifest keys.
/// </summary>
internal sealed record ResolvedSparkplugBirthPlan
{
    /// <summary>The application metric samples to announce, in ordinal published-name order.</summary>
    public required ImmutableArray<SparkplugMetricSample> Metrics { get; init; }

    /// <summary>The exact application alias map (SparkplugAliasKey → alias) for the encoder.</summary>
    public required FrozenDictionary<SparkplugAliasKey, ulong> AliasMap { get; init; }

    /// <summary>The wire-exact birth baseline (promoted as the new generation on NBIRTH success).</summary>
    public required FrozenDictionary<SparkplugAliasKey, SparkplugMetricState> Baseline { get; init; }

    /// <summary>The static schema of each announced metric.</summary>
    public required FrozenDictionary<SparkplugAliasKey, SparkplugMetricSchema> Schema { get; init; }

    /// <summary>Whether the resolved manifest announces no application metrics.</summary>
    public bool IsEmpty => Metrics.Length == 0;
}
