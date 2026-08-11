// ============================================================================
// File: Pipeline/ITransformMetricsRecorder.cs
// Purpose: Test seam for per-step metrics. Production wiring to
//          System.Diagnostics.Metrics lands in Phase 2; C1 ships the
//          abstraction plus a default meter-backed recorder and an
//          in-memory test recorder.
// Reference: PHASE1_EXECUTION_PLAN.md C1 DoD
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Pipeline;

/// <summary>
/// Sink for per-step pipeline metrics. Implementations must be thread-safe;
/// however, within a single route the pipeline is called from one worker
/// thread only, so implementations may skip locking on the hot path.
/// </summary>
public interface ITransformMetricsRecorder
{
    /// <summary>
    /// Record the outcome of a single step invocation.
    /// </summary>
    /// <param name="routeId">Route the step executed under.</param>
    /// <param name="stepName">Step name (e.g. <c>"tag-mapping"</c>).</param>
    /// <param name="inputCount">Points entering the step.</param>
    /// <param name="outputCount">Points leaving the step.</param>
    /// <param name="suppressedCount">
    /// Points actively suppressed by the step. Must satisfy
    /// <c>outputCount + suppressedCount == inputCount</c>.
    /// </param>
    /// <param name="duration">Wall-clock time the step took.</param>
    void RecordStep(
        string routeId,
        string stepName,
        int inputCount,
        int outputCount,
        int suppressedCount,
        TimeSpan duration);
}

/// <summary>
/// Default no-op recorder. Used when the host does not inject a real one,
/// so pipelines can still run (with no metrics) in tests and dev.
/// </summary>
public sealed class NullTransformMetricsRecorder : ITransformMetricsRecorder
{
    /// <summary>Shared singleton.</summary>
    public static NullTransformMetricsRecorder Instance { get; } = new();

    private NullTransformMetricsRecorder() { }

    /// <inheritdoc/>
    public void RecordStep(string routeId, string stepName, int inputCount, int outputCount, int suppressedCount, TimeSpan duration)
    {
        // Intentionally empty.
    }
}
