// ============================================================================
// File: Collectors/ProductionCollectorTests.cs
// Purpose: Unit tests for ProductionCollector — cycle time and parts count.
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.Focas2.Collectors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests.Collectors;

public sealed class ProductionCollectorTests
{
    private readonly FakeFocas2Api _api = new();
    private readonly ProductionCollector _collector;
    private readonly CanonicalDataPointFactory _factory;

    public ProductionCollectorTests()
    {
        _collector = new ProductionCollector(_api, NullLogger.Instance);
        _factory = new CanonicalDataPointFactory("gw1", "focas-test", "focas2", "dev1");
    }

    [Fact]
    public void Collect_EmitsCycleTimeAndPartsCount()
    {
        _api.TimerData = new OdbTimer { Minute = 2, Msec = 30500 }; // 2 min + 30.5 sec = 150.5s
        _api.ParameterData = new OdbParameter { LData = 1234 };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, _factory, points, now, now);

        points.Should().HaveCount(2);

        var cycleTime = points.First(p => p.TagName == "production/cycle_time");
        ((double)cycleTime.Value!).Should().BeApproximately(150.5, 0.01);

        var partsCount = points.First(p => p.TagName == "production/parts_count");
        ((long)partsCount.Value!).Should().Be(1234);
    }

    [Fact]
    public void Collect_TimerFails_SkipsCycleTime()
    {
        _api.TimerResult = (short)Focas2ErrorCode.EW_FUNC;
        _api.ParameterData = new OdbParameter { LData = 42 };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, _factory, points, now, now);

        points.Should().ContainSingle();
        points[0].TagName.Should().Be("production/parts_count");
    }

    [Fact]
    public void Collect_ParamFails_SkipsPartsCount()
    {
        _api.TimerData = new OdbTimer { Minute = 1, Msec = 0 };
        _api.ParameterResult = (short)Focas2ErrorCode.EW_FUNC;
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, _factory, points, now, now);

        points.Should().ContainSingle();
        points[0].TagName.Should().Be("production/cycle_time");
    }
}
