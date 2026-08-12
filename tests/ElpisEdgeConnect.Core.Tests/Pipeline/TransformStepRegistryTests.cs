// ============================================================================
// File: Pipeline/TransformStepRegistryTests.cs
// Covers: default registry built-ins, custom registration, unknown kind.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Pipeline;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Pipeline;

public sealed class TransformStepRegistryTests
{
    private static RouteConfig Route(TransformProfileConfig? profile = null) => new()
    {
        RouteId = "r",
        Name = "r",
        SourceInstanceId = "s",
        SinkInstanceIds = new[] { "sink" },
        Filter = new TagFilterConfig(),
        Transforms = profile,
    };

    [Fact]
    public void Default_BuildsAllFourBuiltInsWhenConfigured()
    {
        var profile = new TransformProfileConfig
        {
            TagMapping = new Dictionary<string, string> { ["a"] = "b" },
            Deadband = new Dictionary<string, double> { ["T"] = 1 },
            RateLimitMs = new Dictionary<string, int> { ["T"] = 1000 },
        };
        var route = Route(profile) with { Filter = new TagFilterConfig { Include = new[] { "X/**" } } };

        TransformStepRegistry.Default.Build(TransformStepKind.TagMapping, route).Should().NotBeNull();
        TransformStepRegistry.Default.Build(TransformStepKind.Filter, route).Should().NotBeNull();
        TransformStepRegistry.Default.Build(TransformStepKind.Deadband, route).Should().NotBeNull();
        TransformStepRegistry.Default.Build(TransformStepKind.RateLimit, route).Should().NotBeNull();
    }

    [Fact]
    public void Default_ReturnsNullForUnconfiguredStep()
    {
        TransformStepRegistry.Default.Build(TransformStepKind.TagMapping, Route()).Should().BeNull();
    }

    [Fact]
    public void UnregisteredKind_Throws()
    {
        var reg = new TransformStepRegistry();
        Action act = () => reg.Build(TransformStepKind.TagMapping, Route());
        act.Should().Throw<InvalidOperationException>();
    }
}
