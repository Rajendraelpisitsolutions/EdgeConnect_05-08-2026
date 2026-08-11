// ============================================================================
// File: Collectors/StatusCollectorTests.cs
// Purpose: Unit tests for StatusCollector — CNC status to canonical points.
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.Focas2.Collectors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests.Collectors;

public sealed class StatusCollectorTests
{
    private readonly FakeFocas2Api _api = new();
    private readonly StatusCollector _collector;
    private readonly CanonicalDataPointFactory _factory;

    public StatusCollectorTests()
    {
        _collector = new StatusCollector(_api, NullLogger.Instance);
        _factory = new CanonicalDataPointFactory("gw1", "focas-test", "focas2", "dev1");
    }

    [Fact]
    public void Collect_WithCachedStatInfo_EmitsFivePoints()
    {
        var cached = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 0, Motion = 1, Alarm = 0 };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, cached, _factory, points, now, now);

        points.Should().HaveCount(5);
        points.Should().Contain(p => p.TagName == "status/run_state" && (string)p.Value! == "Running");
        points.Should().Contain(p => p.TagName == "status/auto_mode" && (string)p.Value! == "MEM");
        points.Should().Contain(p => p.TagName == "status/emergency_stop" && (bool)p.Value! == false);
        points.Should().Contain(p => p.TagName == "status/motion" && (string)p.Value! == "Motion");
        points.Should().Contain(p => p.TagName == "status/alarm_flag" && (int)p.Value! == 0);
    }

    [Fact]
    public void Collect_WithApiCall_EmitsFivePoints()
    {
        _api.StatusInfo = new OdbStatusInfo { Run = 1, Aut = 0, Emergency = 0, Motion = 0, Alarm = 0 };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, null, _factory, points, now, now);

        points.Should().HaveCount(5);
        points.Should().Contain(p => p.TagName == "status/run_state" && (string)p.Value! == "Stop");
        points.Should().Contain(p => p.TagName == "status/auto_mode" && (string)p.Value! == "MDI");
    }

    [Fact]
    public void Collect_EmergencyActive_OverridesRunState()
    {
        var cached = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 1, Motion = 0, Alarm = 0 };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, cached, _factory, points, now, now);

        points.Should().Contain(p => p.TagName == "status/run_state" && (string)p.Value! == "Emergency");
        points.Should().Contain(p => p.TagName == "status/emergency_stop" && (bool)p.Value! == true);
    }

    [Theory]
    [InlineData((short)0, "Reset")]
    [InlineData((short)1, "Stop")]
    [InlineData((short)2, "Hold")]
    [InlineData((short)3, "Running")]
    [InlineData((short)4, "MSTR")]
    public void Collect_RunStateMapping_CorrectStrings(short runValue, string expected)
    {
        var cached = new OdbStatusInfo { Run = runValue, Aut = 0, Emergency = 0, Motion = 0, Alarm = 0 };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, cached, _factory, points, now, now);

        var runStatePoint = points.First(p => p.TagName == "status/run_state");
        ((string)runStatePoint.Value!).Should().Be(expected);
    }

    [Fact]
    public void Collect_FatalError_ThrowsFocas2FatalException()
    {
        _api.StatusInfoResult = (short)Focas2ErrorCode.EW_SOCKET;
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        var act = () => _collector.Collect(1, null, _factory, points, now, now);

        act.Should().Throw<Focas2FatalException>()
            .Which.ErrorCode.Should().Be(Focas2ErrorCode.EW_SOCKET);
    }
}
