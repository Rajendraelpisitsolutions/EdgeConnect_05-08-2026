// ============================================================================
// File: Collectors/AlarmCollectorTests.cs
// Purpose: Unit tests for AlarmCollector — alarm status and messages.
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.Focas2.Collectors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests.Collectors;

public sealed class AlarmCollectorTests
{
    private readonly FakeFocas2Api _api = new();
    private readonly AlarmCollector _collector;
    private readonly CanonicalDataPointFactory _factory;

    public AlarmCollectorTests()
    {
        _collector = new AlarmCollector(_api, NullLogger.Instance);
        _factory = new CanonicalDataPointFactory("gw1", "focas-test", "focas2", "dev1");
    }

    [Fact]
    public void Collect_NoAlarms_EmitsCountZero()
    {
        _api.AlarmStatusData = new OdbAlarmStatus { Data = 0 };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, null, _factory, points, now, now);

        points.Should().ContainSingle();
        var countPoint = points[0];
        countPoint.TagName.Should().Be("alarms/count");
        ((int)countPoint.Value!).Should().Be(0);
    }

    [Fact]
    public void Collect_TwoAlarms_EmitsObjectAndCount()
    {
        _api.AlarmStatusData = new OdbAlarmStatus { Data = 1 };
        _api.AlarmMessageCount = 2;
        _api.AlarmMessages =
        [
            new OdbAlarmMessage { AlarmNo = 100, Type = 0, Axis = 1, AlarmMessage = "PS ALARM 100" },
            new OdbAlarmMessage { AlarmNo = 200, Type = 3, Axis = 0, AlarmMessage = "SV ALARM 200" },
            default, default, default, default, default, default, default, default,
        ];
        var axisNames = new List<string> { "X", "Y", "Z" };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, axisNames, _factory, points, now, now);

        points.Should().HaveCount(2);

        var activePoint = points.First(p => p.TagName == "alarms/active");
        activePoint.Value.Should().BeAssignableTo<List<Dictionary<string, object>>>();
        var alarms = (List<Dictionary<string, object>>)activePoint.Value!;
        alarms.Should().HaveCount(2);
        alarms[0]["alarmNumber"].Should().Be(100);
        alarms[0]["type"].Should().Be("PS");
        alarms[1]["type"].Should().Be("SV");

        var countPoint = points.First(p => p.TagName == "alarms/count");
        ((short)countPoint.Value!).Should().Be(2);
    }

    [Theory]
    [InlineData((short)0, "PS")]
    [InlineData((short)1, "OT")]
    [InlineData((short)2, "OH")]
    [InlineData((short)3, "SV")]
    [InlineData((short)4, "SR")]
    [InlineData((short)5, "MC")]
    [InlineData((short)6, "SP")]
    [InlineData((short)7, "DS")]
    [InlineData((short)8, "IE")]
    [InlineData((short)9, "EX")]
    [InlineData((short)10, "PC")]
    public void Collect_AlarmTypeMapping_CorrectNames(short typeValue, string expected)
    {
        _api.AlarmStatusData = new OdbAlarmStatus { Data = 1 };
        _api.AlarmMessageCount = 1;
        _api.AlarmMessages =
        [
            new OdbAlarmMessage { AlarmNo = 42, Type = typeValue, Axis = 0, AlarmMessage = "test" },
            default, default, default, default, default, default, default, default, default,
        ];
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, null, _factory, points, now, now);

        var activePoint = points.First(p => p.TagName == "alarms/active");
        var alarms = (List<Dictionary<string, object>>)activePoint.Value!;
        alarms[0]["type"].Should().Be(expected);
    }

    [Fact]
    public void Collect_AxisLabel_FromNames()
    {
        _api.AlarmStatusData = new OdbAlarmStatus { Data = 1 };
        _api.AlarmMessageCount = 1;
        _api.AlarmMessages =
        [
            new OdbAlarmMessage { AlarmNo = 1, Type = 0, Axis = 2, AlarmMessage = "test" },
            default, default, default, default, default, default, default, default, default,
        ];
        var axisNames = new List<string> { "X", "Y", "Z" };
        var points = new List<CanonicalDataPoint>();
        var now = DateTime.UtcNow;

        _collector.Collect(1, axisNames, _factory, points, now, now);

        var activePoint = points.First(p => p.TagName == "alarms/active");
        var alarms = (List<Dictionary<string, object>>)activePoint.Value!;
        alarms[0]["axis"].Should().Be("Y"); // Axis=2 is 1-based, so index 1 = "Y"
    }
}
