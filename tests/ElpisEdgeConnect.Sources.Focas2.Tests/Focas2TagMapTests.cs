// ============================================================================
// File: Focas2TagMapTests.cs
// Purpose: Unit tests for Focas2TagMap — static tag registry and dynamic
//          tag definition builder.
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

public sealed class Focas2TagMapTests
{
    [Fact]
    public void StaticTags_ContainsExpectedEntries()
    {
        var tags = Focas2TagMap.StaticTags;

        tags.Should().HaveCountGreaterOrEqualTo(20);
        tags.Should().Contain(t => t.TagName == "status/run_state");
        tags.Should().Contain(t => t.TagName == "spindle/speed");
        tags.Should().Contain(t => t.TagName == "alarms/count");
        tags.Should().Contain(t => t.TagName == "production/cycle_time");
    }

    [Fact]
    public void ToolTags_UseDottedNames_ForEremosIngestion()
    {
        // EREMOS V2 matches these names literally — they must not regress to
        // snake_case. See Focas2TagMap "Tool" section header for the rationale.
        Focas2TagMap.ToolCurrentNumber.TagName.Should().Be("Tool.CurrentNo");
        Focas2TagMap.ToolOffsetType.TagName.Should().Be("Tool.OffsetType");
        Focas2TagMap.ToolLifeEnabled.TagName.Should().Be("Tool.LifeEnabled");

        Focas2TagMap.ToolOffset(13, Focas2TagMap.OffsetGeometryRadius).TagName.Should().Be("Tool.13.GeomRad");
        Focas2TagMap.ToolOffset(13, Focas2TagMap.OffsetGeometryRadius).TagPath.Should().Be("Tool/13/GeomRad");
        Focas2TagMap.ToolLifeGroup(2, Focas2TagMap.LifeCounter, CanonicalValueType.Integer).TagName
            .Should().Be("ToolLife.2.Counter");
    }

    [Fact]
    public void StaticTags_DoNotContainStructuredObjectToolTags()
    {
        // The structured tool/offsets and tool/life Object tags were replaced
        // by flat per-tool/per-group tags that EREMOS can ingest.
        var tags = Focas2TagMap.StaticTags;
        tags.Should().NotContain(t => t.TagName == "tool/offsets");
        tags.Should().NotContain(t => t.TagName == "tool/life");
    }

    [Fact]
    public void AxisAbsolute_ReturnsCorrectNames()
    {
        var entry = Focas2TagMap.AxisAbsolute("X");

        entry.TagName.Should().Be("axes/x/absolute");
        entry.TagPath.Should().Be("Axes/X/Absolute");
        entry.ValueType.Should().Be(CanonicalValueType.Double);
        entry.Unit.Should().Be("mm");
    }

    [Fact]
    public void BuildTagDefinitions_IncludesStaticAndDynamic()
    {
        var axisNames = new List<string> { "X", "Y", "Z" };

        var defs = Focas2TagMap.BuildTagDefinitions(axisNames);

        // Static tags + 4 position types * 3 axes + 2 servo diag * 3 axes + 12 fan statuses
        var staticCount = Focas2TagMap.StaticTags.Count;
        var axisTags = 4 * 3;      // 4 position types * 3 axes
        var servoDiag = 2 * 3;     // temp + insulation per axis
        var fanTags = 3 * 4;       // 3 fan types (CNC, SP, SV) * 4 indexes
        var expectedMin = staticCount + axisTags + servoDiag + fanTags;

        defs.Should().HaveCount(expectedMin);
    }

    [Fact]
    public void BuildTagDefinitions_AxisTags_HaveCorrectValueType()
    {
        var axisNames = new List<string> { "X", "Y", "Z" };

        var defs = Focas2TagMap.BuildTagDefinitions(axisNames);

        var axisDefs = defs.Where(d => d.Name.StartsWith("axes/x/")).ToList();
        axisDefs.Should().HaveCount(4); // absolute, machine, relative, distance_to_go
        axisDefs.Should().OnlyContain(d => d.ValueType == CanonicalValueType.Double);
    }

    [Fact]
    public void ServoTemp_ReturnsCorrectPath()
    {
        var entry = Focas2TagMap.ServoTemp(2);

        entry.TagName.Should().Be("diagnostics/servo/2/temperature");
        entry.TagPath.Should().Be("Diagnostics/Servo/2/Temperature");
        entry.ValueType.Should().Be(CanonicalValueType.Integer);
        entry.Unit.Should().Be("C");
    }
}
