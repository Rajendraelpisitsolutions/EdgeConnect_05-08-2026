// ============================================================================
// File: Collectors/AxisCollectorTests.cs
// Purpose: Unit tests for AxisCollector — axis position data collection.
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.Focas2.Collectors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests.Collectors;

public sealed class AxisCollectorTests
{
    private readonly FakeFocas2Api _api = new();
    private readonly AxisCollector _collector;
    private readonly CanonicalDataPointFactory _factory;

    public AxisCollectorTests()
    {
        _collector = new AxisCollector(_api, NullLogger.Instance);
        _factory = new CanonicalDataPointFactory("gw1", "focas-test", "focas2", "dev1");
    }

    [Fact]
    public void Collect_ThreeAxes_EmitsTwelvePoints()
    {
        var axisData = CreateAxisData(new[] { 100000, 200000, 300000 }, new short[] { 3, 3, 3 });
        _api.AbsolutePosition = axisData;
        _api.MachinePosition = axisData;
        _api.RelativePosition = axisData;
        _api.DistanceToGo = axisData;

        var axisNames = new List<string> { "X", "Y", "Z" };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, axisNames, _factory, points, now, now);

        // 4 position types * 3 axes = 12
        points.Should().HaveCount(12);
        points.Should().Contain(p => p.TagName == "axes/x/absolute");
        points.Should().Contain(p => p.TagName == "axes/y/machine");
        points.Should().Contain(p => p.TagName == "axes/z/relative");
        points.Should().Contain(p => p.TagName == "axes/x/distance_to_go");
    }

    [Fact]
    public void Collect_ScalesByDecimalPosition()
    {
        // Raw value 123456 with 3 decimal places = 123.456
        var axisData = CreateAxisData(new[] { 123456, 0, 0 }, new short[] { 3, 3, 3 });
        _api.AbsolutePosition = axisData;
        _api.MachinePosition = axisData;
        _api.RelativePosition = axisData;
        _api.DistanceToGo = axisData;

        var axisNames = new List<string> { "X" };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, axisNames, _factory, points, now, now);

        var absPoint = points.First(p => p.TagName == "axes/x/absolute");
        ((double)absPoint.Value!).Should().BeApproximately(123.456, 0.0001);
    }

    [Fact]
    public void Collect_FatalError_Throws()
    {
        _api.AbsolutePositionResult = (short)Focas2ErrorCode.EW_SOCKET;

        var axisNames = new List<string> { "X" };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        var act = () => _collector.Collect(1, axisNames, _factory, points, now, now);

        act.Should().Throw<Focas2FatalException>()
            .Which.ErrorCode.Should().Be(Focas2ErrorCode.EW_SOCKET);
    }

    [Fact]
    public void Collect_NonFatalError_SkipsAndContinues()
    {
        // Absolute fails with non-fatal, others succeed
        _api.AbsolutePositionResult = (short)Focas2ErrorCode.EW_FUNC;
        var axisData = CreateAxisData(new[] { 100000, 200000, 300000 }, new short[] { 3, 3, 3 });
        _api.MachinePosition = axisData;
        _api.RelativePosition = axisData;
        _api.DistanceToGo = axisData;

        var axisNames = new List<string> { "X", "Y", "Z" };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, axisNames, _factory, points, now, now);

        // Should still have 9 points (3 position types * 3 axes, skipping absolute)
        points.Should().HaveCount(9);
        points.Should().NotContain(p => p.TagName.Contains("absolute"));
    }

    // ---- Helpers ----

    private static OdbAxisData CreateAxisData(int[] values, short[] decimals)
    {
        var data = new int[Focas2Interop.MAX_AXIS];
        var dec = new short[Focas2Interop.MAX_AXIS];
        Array.Copy(values, data, values.Length);
        Array.Copy(decimals, dec, decimals.Length);
        return new OdbAxisData { Data = data, Decimal = dec };
    }
}
