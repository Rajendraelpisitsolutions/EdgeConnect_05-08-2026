// ============================================================================
// File: Pipeline/TransformStepRegistry.cs
// Purpose: Registry mapping step kinds to factory delegates. Ships with the
//          four built-in kinds pre-registered. Future kinds (enrichment,
//          unit conversion, ...) will extend this table without touching
//          the pipeline or builder.
// Reference: PHASE1_EXECUTION_PLAN.md C1
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Pipeline.Steps;

namespace ElpisEdgeConnect.Core.Pipeline;

/// <summary>
/// Factory delegate that produces an <see cref="ITransformStep"/> from a
/// route's configuration. Returning <c>null</c> means "this step is not
/// needed for the given config" (e.g., empty tag-mapping dict).
/// </summary>
public delegate ITransformStep? TransformStepFactory(RouteConfig route);

/// <summary>
/// Registry of known transform step kinds. Built-ins are pre-registered.
/// </summary>
public sealed class TransformStepRegistry
{
    private readonly Dictionary<TransformStepKind, TransformStepFactory> _factories = new();

    /// <summary>Shared default registry with all four built-in kinds registered.</summary>
    public static TransformStepRegistry Default { get; } = CreateDefault();

    /// <summary>Create an empty registry.</summary>
    public TransformStepRegistry()
    {
    }

    /// <summary>Register or replace a factory for the given kind.</summary>
    public void Register(TransformStepKind kind, TransformStepFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[kind] = factory;
    }

    /// <summary>
    /// Build the step for a given kind. Returns <c>null</c> when the kind
    /// is registered but the config makes the step unnecessary.
    /// </summary>
    public ITransformStep? Build(TransformStepKind kind, RouteConfig route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!_factories.TryGetValue(kind, out var factory))
        {
            throw new InvalidOperationException($"No factory registered for transform step kind '{kind}'.");
        }
        return factory(route);
    }

    private static TransformStepRegistry CreateDefault()
    {
        var reg = new TransformStepRegistry();
        reg.Register(TransformStepKind.TagMapping, BuildTagMapping);
        reg.Register(TransformStepKind.Filter, BuildFilter);
        reg.Register(TransformStepKind.Deadband, BuildDeadband);
        reg.Register(TransformStepKind.RateLimit, BuildRateLimit);
        return reg;
    }

    private static ITransformStep? BuildTagMapping(RouteConfig route)
    {
        var map = route.Transforms?.TagMapping;
        if (map is null || map.Count == 0)
        {
            return null;
        }
        return new TagMappingStep(map);
    }

    private static ITransformStep? BuildFilter(RouteConfig route)
    {
        var filter = route.Filter;
        var include = filter.Include;
        var exclude = filter.Exclude;
        var step = new FilterStep(include, exclude);
        // Omit identity filters so an unconfigured route has a zero-step pipeline.
        return step.IsIdentity ? null : step;
    }

    private static ITransformStep? BuildDeadband(RouteConfig route)
    {
        var profile = route.Transforms;
        var abs = profile?.Deadband;
        var pct = profile?.DeadbandPercent;
        if ((abs is null || abs.Count == 0) && (pct is null || pct.Count == 0))
        {
            return null;
        }
        return new DeadbandStep(abs, pct);
    }

    private static ITransformStep? BuildRateLimit(RouteConfig route)
    {
        var rates = route.Transforms?.RateLimitMs;
        if (rates is null || rates.Count == 0)
        {
            return null;
        }
        return new RateLimitStep(rates);
    }
}
