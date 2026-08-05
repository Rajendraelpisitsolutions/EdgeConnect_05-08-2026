// ============================================================================
// File: Pipeline/FilterStepTests.cs
// Covers: include/exclude semantics, glob matching on TagPath, identity
//          shortcut, contract violation on null/empty TagPath.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Pipeline.Steps;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Pipeline.C1TestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Pipeline;

public sealed class FilterStepTests
{
    [Fact]
    public void IncludeStar_AllPointsPass()
    {
        var step = new FilterStep(new[] { "*" }, exclude: null);
        step.IsIdentity.Should().BeTrue();

        var points = new[]
        {
            Double("a", "A/1", 1),
            Double("b", "B/1", 2),
        };
        var output = new List<CanonicalDataPoint>();
        step.Apply(points, output, Ctx());
        output.Should().HaveCount(2);
    }

    [Fact]
    public void IncludeGlob_OnlyMatchingTagPathPasses()
    {
        var step = new FilterStep(new[] { "Spindle/**" }, exclude: null);
        var points = new[]
        {
            Double("a", "Spindle/Speed", 1),
            Double("b", "Axis/1/Load", 2),
        };
        var output = new List<CanonicalDataPoint>();
        step.Apply(points, output, Ctx());

        output.Should().ContainSingle().Which.TagPath.Should().Be("Spindle/Speed");
    }

    [Fact]
    public void ExcludeWinsOverInclude()
    {
        // R5 pin: exclude takes precedence.
        var step = new FilterStep(new[] { "**" }, new[] { "Spindle/Speed" });
        var points = new[]
        {
            Double("a", "Spindle/Speed", 1),
            Double("b", "Spindle/Load", 2),
        };
        var output = new List<CanonicalDataPoint>();
        step.Apply(points, output, Ctx());

        output.Should().ContainSingle().Which.TagPath.Should().Be("Spindle/Load");
    }

    [Fact]
    public void NullTagPath_ThrowsLoudly()
    {
        var step = new FilterStep(new[] { "*" }, exclude: null);
        // IsIdentity would short-circuit at the builder level; here we exercise
        // the guard by forcing a non-identity filter.
        step = new FilterStep(new[] { "Spindle/**" }, null);

        var badPoint = Double("a", " ", 1); // whitespace TagPath
        var output = new List<CanonicalDataPoint>();

        System.Action act = () => step.Apply(new[] { badPoint }, output, Ctx());
        act.Should().Throw<ConfigurationException>()
            .Where(ex => ex.Error.Code == CoreErrors.PipelineInvalidFilterPattern);
    }
}
