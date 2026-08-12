// ============================================================================
// File: Birth/SparkplugBirthBaseline.cs
// Purpose: The wire-exact birth baseline + the monotonic dirtySinceBirth set (plan
//          v3 §5.3). The baseline is defensively frozen (slice-3 review r1 B3) so a
//          caller cannot mutate the birth authority after construction. Once a
//          metric differs from its birth state it stays dirty until cutover — so a
//          value that changes then returns still lands in the final update (the
//          10 -> 20 -> 10 case). At cutover the comparison is fail-closed: a dirty
//          announced metric MISSING from the cutover snapshot is surfaced as a
//          manifest-invariant violation (never silently dropped), and an unannounced
//          key present at cutover is surfaced as first-observed (never ignored).
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §5.3, §1.5, §9.
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Birth;

/// <summary>
/// The outcome of comparing the cutover snapshot against the birth baseline: the
/// metrics to emit in the final non-historical update (union of dirty and
/// changed-at-cutover, carrying their final states), plus the manifest deltas the
/// actor fails closed / rebirths on.
/// </summary>
internal sealed record SparkplugCutoverComparison
{
    /// <summary>The metrics to emit in the final update, each with its final (cutover) state.</summary>
    public required FrozenDictionary<SparkplugAliasKey, SparkplugMetricState> FinalUpdates { get; init; }

    /// <summary>Cutover metrics not in the birth manifest (same-generation first-observed growth).</summary>
    public required ImmutableArray<SparkplugAliasKey> FirstObservedKeys { get; init; }

    /// <summary>Announced metrics absent from the cutover snapshot (a manifest-invariant violation).</summary>
    public required ImmutableArray<SparkplugAliasKey> MissingAnnouncedKeys { get; init; }

    /// <summary>Whether the cutover key set exactly matches the announced manifest.</summary>
    public bool IsExactManifest => FirstObservedKeys.IsEmpty && MissingAnnouncedKeys.IsEmpty;
}

/// <summary>
/// Tracks, for one birth generation, which metrics have changed from their birth state
/// (monotonically) and computes the fail-closed catch-up cutover comparison.
/// </summary>
internal sealed class SparkplugBirthBaseline
{
    private readonly FrozenDictionary<SparkplugAliasKey, SparkplugMetricState> _baseline;
    private readonly HashSet<SparkplugAliasKey> _dirty = new();

    /// <summary>Construct a baseline from the per-metric birth states (defensively frozen).</summary>
    /// <param name="baseline">The wire-exact state of each metric at birth.</param>
    public SparkplugBirthBaseline(IReadOnlyDictionary<SparkplugAliasKey, SparkplugMetricState> baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        _baseline = baseline.ToFrozenDictionary(); // own an immutable copy — never the caller's reference
    }

    /// <summary>Construct the baseline from a resolved birth plan.</summary>
    /// <param name="plan">The resolved birth plan.</param>
    /// <returns>The baseline.</returns>
    public static SparkplugBirthBaseline FromResolvedPlan(ResolvedSparkplugBirthPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new SparkplugBirthBaseline(plan.Baseline);
    }

    /// <summary>The metrics announced at birth (immutable).</summary>
    public ImmutableArray<SparkplugAliasKey> BaselineMetrics => _baseline.Keys;

    /// <summary>Whether the metric has diverged from its birth state at any point since birth.</summary>
    /// <param name="key">The metric key.</param>
    /// <returns><c>true</c> if the metric is dirty.</returns>
    public bool IsDirty(SparkplugAliasKey key) => _dirty.Contains(key);

    /// <summary>
    /// Observe a metric's current wire-exact state during replay/catch-up. Marks it dirty
    /// if it differs from its birth baseline; once dirty it stays dirty (monotonic). A
    /// metric absent from the baseline is first-observed (handled by the classifier / rebirth
    /// path) and is ignored here.
    /// </summary>
    /// <param name="key">The metric key.</param>
    /// <param name="state">The metric's current wire-exact state.</param>
    public void Observe(SparkplugAliasKey key, SparkplugMetricState state)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(state);
        if (_baseline.TryGetValue(key, out var birth) && !birth.Equals(state))
        {
            _dirty.Add(key);
        }
    }

    /// <summary>
    /// Compare the cutover snapshot against the baseline. The final update is the union of the
    /// dirty set and any metric whose cutover state differs from birth, each carrying its final
    /// state. Manifest deltas (missing announced / extra unannounced keys) are surfaced, never
    /// silently dropped, for the actor to fail closed / rebirth on.
    /// </summary>
    /// <param name="cutoverStates">The wire-exact state of each metric as of the cutover.</param>
    /// <returns>The fail-closed cutover comparison.</returns>
    public SparkplugCutoverComparison Compare(
        IReadOnlyDictionary<SparkplugAliasKey, SparkplugMetricState> cutoverStates)
    {
        ArgumentNullException.ThrowIfNull(cutoverStates);

        // Deterministic diagnostics: deterministic ordinal published-name order.
        var missing = _baseline.Keys.Where(k => !cutoverStates.ContainsKey(k))
            .OrderBy(k => k.MetricName, StringComparer.Ordinal).ToImmutableArray();
        var firstObserved = cutoverStates.Keys.Where(k => !_baseline.ContainsKey(k))
            .OrderBy(k => k.MetricName, StringComparer.Ordinal).ToImmutableArray();

        var union = new HashSet<SparkplugAliasKey>(_dirty.Where(cutoverStates.ContainsKey));
        foreach (var (key, state) in cutoverStates)
        {
            if (_baseline.TryGetValue(key, out var birth) && !birth.Equals(state))
            {
                union.Add(key);
            }
        }

        return new SparkplugCutoverComparison
        {
            FinalUpdates = union.ToFrozenDictionary(key => key, key => cutoverStates[key]),
            FirstObservedKeys = firstObserved,
            MissingAnnouncedKeys = missing,
        };
    }
}
