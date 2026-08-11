// ============================================================================
// File: Pipeline/TagMappingStepTests.cs
// Covers: tag rename, unmapped pass-through, OriginalTagName preservation.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Pipeline.Steps;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Pipeline.C1TestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Pipeline;

public sealed class TagMappingStepTests
{
    [Fact]
    public void Apply_MappedTag_Renamed_AndOriginalPreserved()
    {
        var step = new TagMappingStep(new Dictionary<string, string>
        {
            ["raw.rpm"] = "spindle.speed",
        });
        var input = new[] { Double("raw.rpm", "Spindle/Speed", 3000) };
        var output = new List<CanonicalDataPoint>();

        step.Apply(input, output, Ctx());

        output.Should().HaveCount(1);
        output[0].TagName.Should().Be("spindle.speed");
        output[0].OriginalTagName.Should().Be("raw.rpm");
        output[0].TagPath.Should().Be("Spindle/Speed"); // path unchanged
    }

    [Fact]
    public void Apply_UnmappedTag_PassesThroughUnchanged()
    {
        var step = new TagMappingStep(new Dictionary<string, string>
        {
            ["raw.rpm"] = "spindle.speed",
        });
        var point = Double("axis.load", "Axis/Load", 10);
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[] { point }, output, Ctx());

        output.Should().ContainSingle().Which.Should().Be(point);
    }

    /// <summary>
    /// R6 pin (Mutation 10): chained renames must preserve the VERY first
    /// OriginalTagName. A second TagMappingStep seeing an already-renamed
    /// point must not overwrite it.
    /// </summary>
    [Fact]
    public void Apply_ChainedRename_PreservesFirstOriginal()
    {
        var step1 = new TagMappingStep(new Dictionary<string, string>
        {
            ["raw.rpm"] = "spindle.speed",
        });
        var step2 = new TagMappingStep(new Dictionary<string, string>
        {
            ["spindle.speed"] = "device.1.speed",
        });

        var input = new[] { Double("raw.rpm", "Spindle/Speed", 3000) };
        var buf1 = new List<CanonicalDataPoint>();
        step1.Apply(input, buf1, Ctx());

        var buf2 = new List<CanonicalDataPoint>();
        step2.Apply(buf1, buf2, Ctx());

        buf2.Should().HaveCount(1);
        buf2[0].TagName.Should().Be("device.1.speed");
        buf2[0].OriginalTagName.Should().Be("raw.rpm");
    }

    [Fact]
    public void Apply_EmptyMap_IsIdentity()
    {
        var step = new TagMappingStep(new Dictionary<string, string>());
        var point = Double("t", "T", 1);
        var output = new List<CanonicalDataPoint>();

        step.Apply(new[] { point }, output, Ctx());

        output.Should().ContainSingle().Which.Should().Be(point);
    }
}
