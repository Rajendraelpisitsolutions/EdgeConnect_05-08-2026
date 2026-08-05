// ============================================================================
// File: ToolCollectorTests.cs
// Purpose: Verify the FOCAS2 ToolCollector emits FLAT per-tool / per-group tags
//          using the exact names the EREMOS V2 ToolLifeIngestionService consumes
//          (Tool.CurrentNo, Tool.{n}.GeomRad, ToolLife.{g}.Counter, ...).
//          These are locked-contract tests: the names must not regress.
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.Focas2.Collectors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

public sealed class ToolCollectorTests
{
    private const ushort Handle = 1;

    private static CanonicalDataPointFactory Factory() =>
        new("gw-1", "focas-1", "FOCAS2", "dev-1");

    private static (List<CanonicalDataPoint> points, FakeFocas2Api api) Run(Action<FakeFocas2Api> configure)
    {
        var api = new FakeFocas2Api();
        configure(api);
        var collector = new ToolCollector(api, NullLogger.Instance);
        var points = new List<CanonicalDataPoint>();
        var now = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc);

        collector.Collect(Handle, Factory(), points, now, now);
        return (points, api);
    }

    private static CanonicalDataPoint? Tag(List<CanonicalDataPoint> points, string tagName) =>
        points.FirstOrDefault(p => p.TagName == tagName);

    [Fact]
    public void Collect_EmitsCurrentToolNumber_WithDottedName()
    {
        var (points, _) = Run(api => api.CurrentToolNumber = 13);

        var tag = Tag(points, "Tool.CurrentNo");
        tag.Should().NotBeNull();
        tag!.Value.Should().Be(13);
        tag.TagPath.Should().Be("Tool/CurrentNo");
    }

    [Fact]
    public void Collect_TypeC_EmitsFlatPerToolOffsetTags_NonZeroOnly()
    {
        var (points, _) = Run(api =>
        {
            api.ToolOffsetType = 2;   // Memory C
            api.ToolOffsetCount = 3;
            // Tool 1: geometry radius only. Tool 2: wear radius only. Tool 3: all zero.
            api.ToolOffsetValues[(1, 3)] = -332530; // GeomRad raw -> -332.53 mm
            api.ToolOffsetValues[(2, 1)] = 6000;    // WearRad raw -> 6.0 mm
        });

        Tag(points, "Tool.1.GeomRad")!.Value.Should().Be(-332.53);
        Tag(points, "Tool.1.GeomRad")!.TagPath.Should().Be("Tool/1/GeomRad");
        Tag(points, "Tool.2.WearRad")!.Value.Should().Be(6.0);

        // Zero values are not emitted.
        Tag(points, "Tool.1.WearLen").Should().BeNull();
        Tag(points, "Tool.3.GeomRad").Should().BeNull();

        // Offset type label emitted.
        Tag(points, "Tool.OffsetType")!.Value.Should().Be("C");
    }

    [Fact]
    public void Collect_TypeC_FallsBackToIndividualReads_WhenRangeFails()
    {
        var (points, _) = Run(api =>
        {
            api.ToolOffsetType = 2;
            api.ToolOffsetCount = 2;
            api.FailOffsetRange = true;            // force individual cnc_rdtofs path
            api.ToolOffsetValues[(2, 2)] = -800;   // GeomLen raw -> -0.8 mm
        });

        Tag(points, "Tool.2.GeomLen")!.Value.Should().Be(-0.8);
    }

    [Fact]
    public void Collect_MarksActiveToolOffset()
    {
        var (points, _) = Run(api =>
        {
            api.CurrentToolNumber = 5;
            api.ToolOffsetType = 0;   // Memory A
            api.ToolOffsetCount = 10;
        });

        var active = Tag(points, "Tool.5.IsActive");
        active.Should().NotBeNull();
        active!.Value.Should().Be(true);
    }

    [Fact]
    public void Collect_ToolLifeDisabled_EmitsLifeEnabledFalse_AndNoGroups()
    {
        var (points, _) = Run(api => api.ToolLifeMaxGroups = 0);

        Tag(points, "Tool.LifeEnabled")!.Value.Should().Be(false);
        points.Should().NotContain(p => p.TagName.StartsWith("ToolLife."));
    }

    [Fact]
    public void Collect_ToolLifeEnabled_EmitsPerGroupTags_WithEremosFieldNames()
    {
        var (points, _) = Run(api =>
        {
            api.ToolLifeMaxGroups = 4;
            api.ToolLifeCountType = 1;       // Minutes
            api.ToolLifeActiveGroup = 1;
            api.ToolLifeGroups[1] = (toolNo: 3, count: 40, limit: 100);
        });

        Tag(points, "Tool.LifeEnabled")!.Value.Should().Be(true);
        Tag(points, "ToolLife.1.ToolNo")!.Value.Should().Be(3);
        Tag(points, "ToolLife.1.Counter")!.Value.Should().Be(40);
        Tag(points, "ToolLife.1.Limit")!.Value.Should().Be(100);
        Tag(points, "ToolLife.1.CountType")!.Value.Should().Be("Minutes");
        Tag(points, "ToolLife.1.Status")!.Value.Should().Be("Active");
        // 40/100 used -> 60% remaining
        Tag(points, "ToolLife.1.LifeRemain")!.Value.Should().Be(60.0);
    }

    [Fact]
    public void Collect_AllOffsetTopics_ContainToolMarker_ForEremosDetection()
    {
        // EREMOS detects tool topics via the literal "Tool." / "ToolLife." marker.
        var (points, _) = Run(api =>
        {
            api.CurrentToolNumber = 2;
            api.ToolOffsetType = 2;
            api.ToolOffsetCount = 2;
            api.ToolOffsetValues[(1, 0)] = 1000;
            api.ToolLifeMaxGroups = 1;
            api.ToolLifeGroups[1] = (toolNo: 1, count: 0, limit: 0);
        });

        points.Where(p => p.TagPath.StartsWith("Tool/") || p.TagPath.StartsWith("ToolLife/"))
              .Should().OnlyContain(p =>
                  p.TagName.StartsWith("Tool.") || p.TagName.StartsWith("ToolLife."));
    }
}
