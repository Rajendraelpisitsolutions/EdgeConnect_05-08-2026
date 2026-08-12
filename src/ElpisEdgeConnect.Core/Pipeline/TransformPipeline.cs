// ============================================================================
// File: Pipeline/TransformPipeline.cs
// Purpose: Ordered execution of ITransformStep instances with per-step
//          metrics and empty-batch short-circuit.
// Reference: ARCHITECTURE_BLUEPRINT.md §5, PHASE1_EXECUTION_PLAN.md C1
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Pipeline;

/// <summary>
/// Executes a fixed, ordered list of <see cref="ITransformStep"/> instances
/// against a batch of <see cref="CanonicalDataPoint"/>s. Owns two buffers
/// internally and swaps them between steps so no list allocation is
/// required per invocation.
/// </summary>
/// <remarks>
/// The pipeline is intended to be called from a single route worker thread.
/// It is NOT thread-safe across concurrent callers.
/// </remarks>
public sealed class TransformPipeline
{
    private readonly IReadOnlyList<ITransformStep> _steps;
    private readonly List<CanonicalDataPoint> _bufferA = new();
    private readonly List<CanonicalDataPoint> _bufferB = new();

    /// <summary>
    /// Create a pipeline from an ordered step list. Callers should prefer
    /// <see cref="TransformPipelineBuilder"/> which enforces the locked
    /// ordering rules from blueprint §5.
    /// </summary>
    public TransformPipeline(IReadOnlyList<ITransformStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps = steps;
    }

    /// <summary>The steps in execution order.</summary>
    public IReadOnlyList<ITransformStep> Steps => _steps;

    /// <summary>
    /// Execute every step in order. The returned list is an internal buffer
    /// owned by the pipeline and is valid only until the next call to
    /// <see cref="Execute"/>. Callers that need to hold the result beyond
    /// that must copy it.
    /// </summary>
    public IReadOnlyList<CanonicalDataPoint> Execute(
        IReadOnlyList<CanonicalDataPoint> input,
        TransformContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        if (_steps.Count == 0 || input.Count == 0)
        {
            return input;
        }

        // Copy input into buffer A so steps never see the caller's list.
        _bufferA.Clear();
        _bufferA.AddRange(input);

        var current = _bufferA;
        var next = _bufferB;

        foreach (var step in _steps)
        {
            next.Clear();
            var inputCount = current.Count;
            var sw = Stopwatch.StartNew();
            step.Apply(current, next, context);
            sw.Stop();

            var outputCount = next.Count;
            var suppressed = inputCount - outputCount;
            context.Metrics.RecordStep(
                context.RouteId,
                step.Name,
                inputCount,
                outputCount,
                suppressed,
                sw.Elapsed);

            // Swap buffers.
            (current, next) = (next, current);

            // Short-circuit: nothing left to transform.
            if (current.Count == 0)
            {
                return current;
            }
        }

        return current;
    }
}
