// ============================================================================
// File: Pipeline/TransformPipelineBuilderTests.cs
// Covers: fixed ordering, step omission rules, identity pipeline, integration.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Pipeline;
using ElpisEdgeConnect.Core.Pipeline.Steps;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Pipeline.C1TestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Pipeline;

public sealed class TransformPipelineBuilderTests
{
    private static RouteConfig BaseRoute(TransformProfileConfig? profile = null, TagFilterConfig? filter = null) => new()
    {
        RouteId = "r1",
        Name = "Route 1",
        SourceInstanceId = "src-1",
        SinkInstanceIds = new[] { "sink-1" },
        Filter = filter ?? new TagFilterConfig(),
        Transforms = profile,
    };

    [Fact]
    public void BuildFor_IdentityRoute_HasZeroSteps()
    {
        var pipeline = new TransformPipelineBuilder().BuildFor(BaseRoute());
        pipeline.Steps.Should().BeEmpty();
    }

    [Fact]
    public void BuildFor_AllProfileFieldsSet_OrderIsFixed()
    {
        // Pin (blueprint §5): TagMapping → Filter → Deadband → RateLimit.
        var profile = new TransformProfileConfig
        {
            TagMapping = new Dictionary<string, string> { ["a"] = "b" },
            Deadband = new Dictionary<string, double> { ["T"] = 1 },
            RateLimitMs = new Dictionary<string, int> { ["T"] = 1000 },
        };
        var filter = new TagFilterConfig { Include = new[] { "Spindle/**" } };
        var pipeline = new TransformPipelineBuilder().BuildFor(BaseRoute(profile, filter));

        pipeline.Steps.Select(s => s.Kind).Should().Equal(
            TransformStepKind.TagMapping,
            TransformStepKind.Filter,
            TransformStepKind.Deadband,
            TransformStepKind.RateLimit);
    }

    [Fact]
    public void BuildFor_IdentityFilterOmitted()
    {
        // Even with transforms, a default include-* filter must not create a FilterStep.
        var profile = new TransformProfileConfig
        {
            TagMapping = new Dictionary<string, string> { ["a"] = "b" },
        };
        var pipeline = new TransformPipelineBuilder().BuildFor(BaseRoute(profile));
        pipeline.Steps.Should().ContainSingle().Which.Kind.Should().Be(TransformStepKind.TagMapping);
    }

    [Fact]
    public void BuildFor_DeadbandWithBothModes_BuildsSingleStep()
    {
        var profile = new TransformProfileConfig
        {
            Deadband = new Dictionary<string, double> { ["A"] = 1 },
            DeadbandPercent = new Dictionary<string, double> { ["B"] = 0.1 },
        };
        var pipeline = new TransformPipelineBuilder().BuildFor(BaseRoute(profile));
        pipeline.Steps.Should().ContainSingle().Which.Should().BeOfType<DeadbandStep>();
    }

    [Fact]
    public void EndToEnd_FourStepPipeline_ProducesExpectedBatch()
    {
        var profile = new TransformProfileConfig
        {
            TagMapping = new Dictionary<string, string> { ["raw.rpm"] = "spindle.speed" },
            Deadband = new Dictionary<string, double> { ["Spindle/Speed"] = 10 },
            RateLimitMs = new Dictionary<string, int> { ["Spindle/Speed"] = 1000 },
        };
        var filter = new TagFilterConfig { Include = new[] { "Spindle/**" } };
        var pipeline = new TransformPipelineBuilder().BuildFor(BaseRoute(profile, filter));

        // Include a tag that filter drops and a tag that makes it all the way through.
        var points = new[]
        {
            Double("raw.rpm", "Spindle/Speed", 3000), // passes
            Double("axis.load", "Axis/Load", 5),     // dropped by filter
        };
        var result = pipeline.Execute(points, Ctx());

        result.Should().HaveCount(1);
        result[0].TagName.Should().Be("spindle.speed");
        result[0].OriginalTagName.Should().Be("raw.rpm");
    }
}
