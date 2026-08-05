// ============================================================================
// File: Pipeline/Steps/DeadbandStep.cs
// Purpose: Suppress readings whose numeric value has not changed by the
//          configured absolute or percentage threshold.
// Reference: ARCHITECTURE_BLUEPRINT.md §5, PHASE1_EXECUTION_PLAN.md C1
//
// Rules:
//   - First observation per tag is always passed (no prior value).
//   - Absolute mode:    pass when |current - last| >= threshold
//   - Percentage mode:  pass when |current - last| / max(|last|, epsilon) >= fraction
//   - Non-numeric values always pass (and reset stored last numeric to null).
//   - Null values always pass (they are not baselines).
//   - Tags with no entry in either map are identity (always pass).
//   - A tag may only appear in ONE mode; cross-record validation enforces this.
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Pipeline.Steps;

/// <summary>
/// Per-tag deadband with absolute and percentage modes. Stateful: stores the
/// last-emitted numeric value per tag, keyed by <see cref="CanonicalDataPoint.TagPath"/>.
/// </summary>
public sealed class DeadbandStep : ITransformStep
{
    private const double PercentEpsilon = 1e-12;

    private readonly FrozenDictionary<string, double> _absolute;
    private readonly FrozenDictionary<string, double> _percent;
    private readonly Dictionary<string, double> _last = new(StringComparer.Ordinal);

    /// <summary>
    /// Build a deadband step. Either map may be null or empty; validation
    /// that a tag is not in both is performed by
    /// <see cref="Configuration.CrossRecordValidator"/> at config-apply time.
    /// </summary>
    public DeadbandStep(
        IReadOnlyDictionary<string, double>? absolute,
        IReadOnlyDictionary<string, double>? percent)
    {
        _absolute = (absolute ?? new Dictionary<string, double>()).ToFrozenDictionary(StringComparer.Ordinal);
        _percent = (percent ?? new Dictionary<string, double>()).ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public string Name => "deadband";

    /// <inheritdoc/>
    public TransformStepKind Kind => TransformStepKind.Deadband;

    /// <summary>True if no tags have any deadband configured (step is a no-op).</summary>
    public bool IsEmpty => _absolute.Count == 0 && _percent.Count == 0;

    /// <inheritdoc/>
    public void Apply(
        IReadOnlyList<CanonicalDataPoint> input,
        List<CanonicalDataPoint> output,
        TransformContext context)
    {
        for (int i = 0; i < input.Count; i++)
        {
            var point = input[i];
            var key = point.TagPath;

            // Null values always pass; they do not establish a baseline.
            if (point.Value is null)
            {
                output.Add(point);
                continue;
            }

            if (!TryGetNumeric(point, out var current))
            {
                // Non-numeric value. Always pass, and clear any stored last so
                // a subsequent numeric reading starts fresh.
                _last.Remove(key);
                output.Add(point);
                continue;
            }

            bool hasAbs = _absolute.TryGetValue(key, out var absThreshold);
            bool hasPct = _percent.TryGetValue(key, out var pctFraction);
            if (!hasAbs && !hasPct)
            {
                // No deadband configured for this tag — identity and update baseline
                // for future (so if the tag later becomes configured the baseline is fresh).
                _last[key] = current;
                output.Add(point);
                continue;
            }

            if (!_last.TryGetValue(key, out var last))
            {
                // First observation — always pass.
                _last[key] = current;
                output.Add(point);
                continue;
            }

            double delta = Math.Abs(current - last);
            bool pass;
            if (hasAbs)
            {
                pass = delta >= absThreshold;
            }
            else
            {
                double denom = Math.Max(Math.Abs(last), PercentEpsilon);
                pass = (delta / denom) >= pctFraction;
            }

            if (pass)
            {
                _last[key] = current;
                output.Add(point);
            }
            // else: suppress; last-value remains the previously-emitted value
            //       so the comparison stays anchored to the last emission.
        }
    }

    private static bool TryGetNumeric(CanonicalDataPoint point, out double value)
    {
        switch (point.ValueType)
        {
            case Model.CanonicalValueType.Integer:
                value = Convert.ToDouble((int)point.Value!, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case Model.CanonicalValueType.Long:
                value = Convert.ToDouble((long)point.Value!, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case Model.CanonicalValueType.Float:
                value = (float)point.Value!;
                return true;
            case Model.CanonicalValueType.Double:
                value = (double)point.Value!;
                return true;
            default:
                value = default;
                return false;
        }
    }
}
