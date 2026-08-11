// ============================================================================
// File: Pipeline/TransformContext.cs
// Purpose: Per-invocation context passed into every ITransformStep.Apply call.
// Reference: PHASE1_EXECUTION_PLAN.md C1
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Pipeline;

/// <summary>
/// Immutable per-invocation context for a transform pipeline run. Carries
/// identifying information and a clock so steps remain deterministic under
/// test.
/// </summary>
public sealed record TransformContext
{
    /// <summary>Route this invocation is running under.</summary>
    public required string RouteId { get; init; }

    /// <summary>Owning gateway's identifier.</summary>
    public required string GatewayId { get; init; }

    /// <summary>Current UTC wall-clock as perceived by the pipeline.</summary>
    public required DateTime UtcNow { get; init; }

    /// <summary>Metrics sink. Never null; use <see cref="NullTransformMetricsRecorder.Instance"/> for no-op.</summary>
    public required ITransformMetricsRecorder Metrics { get; init; }
}
