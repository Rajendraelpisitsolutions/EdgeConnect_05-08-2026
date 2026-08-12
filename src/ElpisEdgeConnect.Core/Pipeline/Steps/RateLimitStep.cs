// ============================================================================
// File: Pipeline/Steps/RateLimitStep.cs
// Purpose: Per-tag minimum-interval throttle. First point per tag passes;
//          subsequent points within the threshold are suppressed.
// Reference: ARCHITECTURE_BLUEPRINT.md §5, PHASE1_EXECUTION_PLAN.md C1
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Pipeline.Steps;

/// <summary>
/// Cap publish frequency per tag. For each configured tag, a point is
/// emitted only if at least the configured interval has elapsed since the
/// last emission. Tags not in the map pass unchanged.
/// </summary>
/// <remarks>
/// State key is <see cref="CanonicalDataPoint.TagPath"/> so renamed tags
/// are throttled by their canonical identity. Wall clock comes from
/// <see cref="TransformContext.UtcNow"/> so tests are deterministic.
/// </remarks>
public sealed class RateLimitStep : ITransformStep
{
    private readonly FrozenDictionary<string, TimeSpan> _intervals;
    private readonly Dictionary<string, DateTime> _lastEmitted = new(StringComparer.Ordinal);

    /// <summary>Build a rate-limit step from a per-tag millisecond map.</summary>
    public RateLimitStep(IReadOnlyDictionary<string, int>? ratesMs)
    {
        var map = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        if (ratesMs is not null)
        {
            foreach (var (tag, ms) in ratesMs)
            {
                if (ms <= 0)
                {
                    // Validation should have caught this; throw so misconfigured
                    // callers fail loudly instead of silently passing all points.
                    throw new ArgumentException(
                        $"RateLimit for tag '{tag}' is {ms}ms; must be strictly positive.",
                        nameof(ratesMs));
                }
                map[tag] = TimeSpan.FromMilliseconds(ms);
            }
        }
        _intervals = map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public string Name => "rate-limit";

    /// <inheritdoc/>
    public TransformStepKind Kind => TransformStepKind.RateLimit;

    /// <summary>True if no tags are configured (step is a no-op).</summary>
    public bool IsEmpty => _intervals.Count == 0;

    /// <inheritdoc/>
    public void Apply(
        IReadOnlyList<CanonicalDataPoint> input,
        List<CanonicalDataPoint> output,
        TransformContext context)
    {
        var now = context.UtcNow;
        for (int i = 0; i < input.Count; i++)
        {
            var point = input[i];
            var key = point.TagPath;
            if (!_intervals.TryGetValue(key, out var interval))
            {
                output.Add(point);
                continue;
            }

            if (_lastEmitted.TryGetValue(key, out var last))
            {
                if ((now - last) < interval)
                {
                    // Within throttle window — suppress.
                    continue;
                }
            }

            _lastEmitted[key] = now;
            output.Add(point);
        }
    }
}
