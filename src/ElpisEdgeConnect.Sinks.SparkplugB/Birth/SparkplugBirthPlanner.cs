// ============================================================================
// File: Birth/SparkplugBirthPlanner.cs
// Purpose: Builds an immutable SparkplugBirthPlan from a Core LatestValueSnapshot
//          (the pure snapshot→wire mapping, plan v3 §1.4) and resolves it against a
//          store-supplied alias map into a ResolvedSparkplugBirthPlan (slice-3
//          review r1 B4). Planning derives the source-qualified alias key + name,
//          validates names (reserved/duplicate, before alias resolution), the
//          encoder samples, the wire-exact baseline, and the static schema — all via
//          the shared K2 mapper, so an invalid snapshot value is rejected before
//          alias allocation / bdSeq reservation / CONNECT. Resolution requires an
//          exact key-set match with the store map and rejects alias 0 / duplicates.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.4, §5.5, §9.
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Birth;

/// <summary>Plans and alias-resolves an NBIRTH from a coherent latest-value snapshot.</summary>
internal static class SparkplugBirthPlanner
{
    /// <summary>
    /// Build the birth plan for a snapshot. The manifest is the snapshot's observed set,
    /// ordered by ordinal published name (matching the encoder's NBIRTH ordering).
    /// </summary>
    /// <param name="snapshot">The coherent birth/rebirth snapshot.</param>
    /// <returns>The immutable birth plan (empty when the snapshot has no metrics).</returns>
    /// <exception cref="AdapterException">
    /// Thrown on a reserved/duplicate published name, or a typed <c>SPARKPLUG.ENCODE_*</c>
    /// when a metric cannot map to the wire.
    /// </exception>
    public static SparkplugBirthPlan Plan(LatestValueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var samples = new List<SparkplugMetricSample>();
        var baseline = new Dictionary<SparkplugAliasKey, SparkplugMetricState>();
        var schema = new Dictionary<SparkplugAliasKey, SparkplugMetricSchema>();

        foreach (var key in snapshot.Metrics)
        {
            var value = snapshot.TryGet(key)
                ?? throw Reject(SparkplugErrors.IdentityInvalid, $"snapshot metric '{key}' has no value.");
            var aliasKey = SparkplugAliasKey.FromCanonical(key);

            samples.Add(new SparkplugMetricSample
            {
                Key = aliasKey,
                ValueType = value.ValueType,
                Value = value.Value,
                IsNull = value.IsNull,
                AcquisitionTimestamp = value.TimestampUtc,
                Quality = value.Quality,
            });
            baseline[aliasKey] = SparkplugMetricState.FromLatestValue(value); // validates via the shared mapper
            schema[aliasKey] = SparkplugMetricSchema.From(value.ValueType);
        }

        samples.Sort((a, b) => string.CompareOrdinal(a.Key.MetricName, b.Key.MetricName));

        // Final-name validation (reserved + duplicate) runs before alias resolution.
        SparkplugBirthMetricNames.ValidateAll(samples.Select(s => s.Key.MetricName));

        return new SparkplugBirthPlan
        {
            Metrics = samples.ToImmutableArray(),
            ManifestKeys = samples.Select(s => s.Key).ToImmutableArray(),
            Baseline = baseline.ToFrozenDictionary(),
            Schema = schema.ToFrozenDictionary(),
        };
    }

    /// <summary>
    /// Resolve a plan against a store-supplied alias map into the coherent candidate manifest
    /// the actor feeds to the encoder. The alias map must be an EXACT set match with the plan's
    /// manifest keys; alias 0 and duplicate alias values are rejected.
    /// </summary>
    /// <param name="plan">The birth plan.</param>
    /// <param name="aliases">The store-resolved alias map for the plan's manifest keys.</param>
    /// <returns>The immutable resolved manifest.</returns>
    /// <exception cref="AdapterException">
    /// Thrown with <see cref="SparkplugErrors.AliasTableMismatch"/> (set mismatch),
    /// <see cref="SparkplugErrors.AliasZeroReserved"/>, or <see cref="SparkplugErrors.AliasDuplicate"/>.
    /// </exception>
    public static ResolvedSparkplugBirthPlan Resolve(
        SparkplugBirthPlan plan, IReadOnlyDictionary<SparkplugAliasKey, ulong> aliases)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(aliases);

        var manifestKeys = plan.ManifestKeys.ToHashSet();
        var missing = manifestKeys.Where(k => !aliases.ContainsKey(k)).Select(k => k.MetricName).Order(StringComparer.Ordinal).ToList();
        var extra = aliases.Keys.Where(k => !manifestKeys.Contains(k)).Select(k => k.MetricName).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            throw Reject(SparkplugErrors.AliasTableMismatch,
                "the resolved alias map and the birth manifest must be an exact set match. " +
                $"Keys missing an alias: [{string.Join(", ", missing)}]; aliases with no metric: [{string.Join(", ", extra)}].");
        }

        var seenAliases = new HashSet<ulong>();
        foreach (var key in plan.ManifestKeys)
        {
            var alias = aliases[key];
            if (alias == 0UL)
            {
                throw Reject(SparkplugErrors.AliasZeroReserved, $"alias 0 is reserved; metric '{key.MetricName}' cannot use it.");
            }

            if (!seenAliases.Add(alias))
            {
                throw Reject(SparkplugErrors.AliasDuplicate, $"alias {alias} is assigned to more than one metric.");
            }
        }

        var aliasMap = plan.ManifestKeys.ToFrozenDictionary(k => k, k => aliases[k]);

        return new ResolvedSparkplugBirthPlan
        {
            Metrics = plan.Metrics,
            AliasMap = aliasMap,
            Baseline = plan.Baseline,
            Schema = plan.Schema,
        };
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
