// ============================================================================
// File: Diagnostics/PipelineHealthSnapshot.cs
// Purpose: Immutable view of one route's transform pipeline counters,
//          rolled up across the pipeline's transform steps.
// Reference: ARCHITECTURE_BLUEPRINT.md §13
// Milestone: C4 — phase 1.
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Pipeline-side rollup for one route. The collector populates this from
/// per-step counters fed by <c>ITransformMetricsRecorder</c>.
/// </summary>
public sealed record PipelineHealthSnapshot
{
    /// <summary>The route id this pipeline belongs to.</summary>
    public required string RouteId { get; init; }

    /// <summary>Lifetime count of pipeline invocations (one per intake batch).</summary>
    public required long BatchesProcessed { get; init; }

    /// <summary>Lifetime count of points entering the pipeline.</summary>
    public required long PointsIn { get; init; }

    /// <summary>Lifetime count of points leaving the pipeline (post-transform).</summary>
    public required long PointsOut { get; init; }

    /// <summary>Per-step counters in declaration order.</summary>
    public required IReadOnlyList<TransformStepStats> Steps { get; init; }
}

/// <summary>Counters for a single transform step inside a pipeline.</summary>
public sealed record TransformStepStats
{
    /// <summary>The step's kind name (e.g. "rate-limit", "downsample").</summary>
    public required string StepKind { get; init; }

    /// <summary>Lifetime count of step invocations.</summary>
    public required long Invocations { get; init; }

    /// <summary>Lifetime count of points entering the step.</summary>
    public required long PointsIn { get; init; }

    /// <summary>Lifetime count of points leaving the step.</summary>
    public required long PointsOut { get; init; }

    /// <summary>
    /// Lifetime count of points that the step chose to suppress
    /// (e.g. dropped by rate-limit, rejected by filter). Separate from
    /// <see cref="PointsIn"/> minus <see cref="PointsOut"/> in concept —
    /// a step may also add points, so the difference is not always a
    /// suppression count. Default 0.
    /// </summary>
    public long PointsSuppressed { get; init; }

    /// <summary>Lifetime cumulative wall-clock duration of step executions.</summary>
    public required TimeSpan TotalDuration { get; init; }
}
