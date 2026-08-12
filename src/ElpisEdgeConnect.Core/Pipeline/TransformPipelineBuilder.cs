// ============================================================================
// File: Pipeline/TransformPipelineBuilder.cs
// Purpose: Build a TransformPipeline from a RouteConfig using the registry.
//          Locks the execution order from blueprint §5:
//              TagMapping → Filter → Deadband → RateLimit
// Reference: ARCHITECTURE_BLUEPRINT.md §5, PHASE1_EXECUTION_PLAN.md C1
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Pipeline;

/// <summary>
/// Builds a <see cref="TransformPipeline"/> for a route. Step ordering is
/// LOCKED per blueprint §5; users can only control which steps are present
/// by populating the corresponding dictionaries in
/// <see cref="TransformProfileConfig"/> and the filter block in
/// <see cref="RouteConfig"/>.
/// </summary>
public sealed class TransformPipelineBuilder
{
    // Locked execution order. DO NOT change without updating blueprint §5.
    private static readonly TransformStepKind[] FixedOrder =
    {
        TransformStepKind.TagMapping,
        TransformStepKind.Filter,
        TransformStepKind.Deadband,
        TransformStepKind.RateLimit,
    };

    private readonly TransformStepRegistry _registry;

    /// <summary>Create a builder. Uses the default registry when none is supplied.</summary>
    public TransformPipelineBuilder(TransformStepRegistry? registry = null)
    {
        _registry = registry ?? TransformStepRegistry.Default;
    }

    /// <summary>Build a pipeline for the given route configuration.</summary>
    public TransformPipeline BuildFor(RouteConfig route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var steps = new List<ITransformStep>(FixedOrder.Length);
        foreach (var kind in FixedOrder)
        {
            var step = _registry.Build(kind, route);
            if (step is not null)
            {
                steps.Add(step);
            }
        }
        return new TransformPipeline(steps);
    }
}
