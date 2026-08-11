// ============================================================================
// File: Contracts/RoutePipelineSummaryDto.cs
// Purpose: Pipeline-side rollup nested under RouteSummaryDto. Surfaces
//          the per-route transform pipeline counters — batches in/out,
//          total points in/out — plus per-step counters that let the
//          Route detail page answer "where in the pipeline is data
//          being dropped or transformed?"
//
//          Field shape mirrors Core's PipelineHealthSnapshot 1:1 with
//          one rename: Core's TransformStepStats.StepKind becomes
//          the wire's PipelineStepSummaryDto.Kind for consistency with
//          SinkKind / SourceKind / RouteKind naming.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.1
//            ARCHITECTURE_BLUEPRINT.md §13 (pipeline diagnostics)
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Pipeline rollup for one route — total batches, total points in/out,
/// and per-step counters. Nested under <see cref="RouteSummaryDto.Pipeline"/>.
/// </summary>
public sealed record RoutePipelineSummaryDto
{
    /// <summary>Lifetime count of pipeline invocations (one per intake batch).</summary>
    public required long BatchesProcessed { get; init; }

    /// <summary>Lifetime count of points entering the pipeline.</summary>
    public required long PointsIn { get; init; }

    /// <summary>Lifetime count of points leaving the pipeline (post-transform).</summary>
    public required long PointsOut { get; init; }

    /// <summary>Per-step counters in declaration order.</summary>
    public required IReadOnlyList<PipelineStepSummaryDto> Steps { get; init; }
}

/// <summary>
/// Per-step counters for one transform step in a route's pipeline.
/// Carries enough state for the Route detail page to flag which step
/// is dropping, suppressing, or running slow.
/// </summary>
public sealed record PipelineStepSummaryDto
{
    /// <summary>
    /// The step's kind ("rate-limit", "downsample", etc.). Multiple
    /// steps of the same kind in one pipeline share this label —
    /// Core's <c>TransformStepStats</c> doesn't carry a per-instance
    /// name today (small Core-side limitation noted for a future
    /// follow-up; not blocking).
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>Lifetime count of step invocations.</summary>
    public required long Invocations { get; init; }

    /// <summary>Lifetime count of points entering this step.</summary>
    public required long PointsIn { get; init; }

    /// <summary>Lifetime count of points leaving this step.</summary>
    public required long PointsOut { get; init; }

    /// <summary>
    /// Lifetime count of points the step actively chose to suppress —
    /// e.g. dropped by rate-limit, rejected by filter. Distinct from
    /// <c>PointsIn - PointsOut</c> since a step can also produce
    /// additional points (e.g. a fanout step).
    /// </summary>
    public long PointsSuppressed { get; init; }

    /// <summary>
    /// Average wall-clock duration per invocation, in milliseconds.
    /// Derived from <c>TotalDuration / Invocations</c>; null when the
    /// step has never been invoked. Used by the Route detail page to
    /// flag slow steps without surfacing the raw cumulative duration.
    /// </summary>
    public double? AverageDurationMs { get; init; }
}
