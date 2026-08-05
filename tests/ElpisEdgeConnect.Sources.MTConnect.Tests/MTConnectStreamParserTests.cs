// ============================================================================
// File: MTConnectStreamParserTests.cs
// Purpose: Exercise the XML → canonical-point parser against fixture XML
//          files. Fixtures live under TestData/ and are copied to the
//          test output directory by the csproj.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.MTConnect.Tests;

public sealed class MTConnectStreamParserTests
{
    private static CanonicalDataPointFactory NewFactory() => new(
        gatewayId: "gw-parser-test",
        sourceInstanceId: "mtc-src",
        protocolName: "mtconnect",
        deviceId: "cnc-1");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine("TestData", name));

    [Fact]
    public void ParseCurrent_HappyPath_EmitsAllExpectedTags()
    {
        var xml = Fixture("sample-current.xml");
        var points = new List<CanonicalDataPoint>();
        var factory = NewFactory();

        var parsed = MTConnectStreamParser.ParseCurrent(
            xml, factory, points, DateTime.UtcNow, DateTime.UtcNow);

        parsed.Should().BeTrue();
        var names = points.ConvertAll(p => p.TagName);
        names.Should().Contain("status/run_state");
        names.Should().Contain("status/controller_mode");
        names.Should().Contain("status/emergency_stop");
        names.Should().Contain("program/main_program");
        names.Should().Contain("program/running_program");
        names.Should().Contain("spindle/speed");
        names.Should().Contain("spindle/load");
        names.Should().Contain("axes/feed_rate");
        names.Should().Contain("production/parts_count");
        names.Should().Contain("production/cycle_time");
        names.Should().Contain("alarms/count");
        names.Should().Contain("alarms/first_fault");
        names.Should().Contain("axes/x/absolute");
        names.Should().Contain("axes/x/machine");
        names.Should().Contain("axes/y/absolute");
        names.Should().Contain("axes/z/absolute");
    }

    [Fact]
    public void ParseCurrent_HappyPath_MapsExecutionToCanonicalVocabulary()
    {
        var xml = Fixture("sample-current.xml");
        var points = new List<CanonicalDataPoint>();
        MTConnectStreamParser.ParseCurrent(xml, NewFactory(), points,
            DateTime.UtcNow, DateTime.UtcNow);

        var runState = points.Find(p => p.TagName == "status/run_state");
        runState.Should().NotBeNull();
        runState!.Value.Should().Be("Running", "ACTIVE maps to Running");

        var mode = points.Find(p => p.TagName == "status/controller_mode");
        mode!.Value.Should().Be("MEM", "AUTOMATIC maps to MEM");

        var estop = points.Find(p => p.TagName == "status/emergency_stop");
        estop!.Value.Should().Be(false, "ARMED => not triggered");
    }

    [Fact]
    public void ParseCurrent_NumericTags_ParseWithInvariantCulture()
    {
        var xml = Fixture("sample-current.xml");
        var points = new List<CanonicalDataPoint>();
        MTConnectStreamParser.ParseCurrent(xml, NewFactory(), points,
            DateTime.UtcNow, DateTime.UtcNow);

        points.Find(p => p.TagName == "spindle/speed")!.Value.Should().Be(1200.0);
        points.Find(p => p.TagName == "spindle/load")!.Value.Should().Be(45.0);
        points.Find(p => p.TagName == "axes/feed_rate")!.Value.Should().Be(500.5);
        points.Find(p => p.TagName == "production/parts_count")!.Value.Should().Be(42L);
        points.Find(p => p.TagName == "production/cycle_time")!.Value.Should().Be(125.3);
        points.Find(p => p.TagName == "axes/x/absolute")!.Value.Should().Be(123.456);
        points.Find(p => p.TagName == "axes/x/machine")!.Value.Should().Be(120.0);
    }

    [Fact]
    public void ParseCurrent_FaultConditions_EmitActiveCountAndFirstMessage()
    {
        var xml = Fixture("sample-current-with-fault.xml");
        var points = new List<CanonicalDataPoint>();
        MTConnectStreamParser.ParseCurrent(xml, NewFactory(), points,
            DateTime.UtcNow, DateTime.UtcNow);

        var alarmCount = points.Find(p => p.TagName == "alarms/count");
        alarmCount!.Value.Should().Be(2, "two Fault elements in the fixture");

        var first = points.Find(p => p.TagName == "alarms/first_fault");
        first!.Value.Should().Be("Spindle overtemperature");

        // EmergencyStop=TRIGGERED should surface as boolean true.
        points.Find(p => p.TagName == "status/emergency_stop")!.Value.Should().Be(true);
    }

    [Fact]
    public void ParseCurrent_AllUnavailable_ReturnsTrueButAlarmsStillEmitted()
    {
        var xml = Fixture("sample-current-unavailable.xml");
        var points = new List<CanonicalDataPoint>();

        // Even when every Events / Samples value is UNAVAILABLE, the parser
        // still emits the two alarm tags (count=0, first="") because those
        // summarize the Condition section which is independent of
        // UNAVAILABLE markers on data items.
        var parsed = MTConnectStreamParser.ParseCurrent(
            xml, NewFactory(), points, DateTime.UtcNow, DateTime.UtcNow);

        parsed.Should().BeTrue();
        points.Should().OnlyContain(p =>
            p.TagName == "alarms/count" || p.TagName == "alarms/first_fault");
        points.Find(p => p.TagName == "alarms/count")!.Value.Should().Be(0);
        points.Find(p => p.TagName == "alarms/first_fault")!.Value.Should().Be(string.Empty);
    }

    [Fact]
    public void ParseCurrent_NoDeviceStream_ReturnsFalse()
    {
        const string xml =
            "<MTConnectStreams xmlns=\"urn:mtconnect.org:MTConnectStreams:1.7\"><Streams/></MTConnectStreams>";
        var points = new List<CanonicalDataPoint>();

        var parsed = MTConnectStreamParser.ParseCurrent(xml, NewFactory(), points,
            DateTime.UtcNow, DateTime.UtcNow);

        parsed.Should().BeFalse();
        points.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ACTIVE", "Running")]
    [InlineData("INTERRUPTED", "Hold")]
    [InlineData("FEED_HOLD", "Hold")]
    [InlineData("OPTIONAL_STOP", "Hold")]
    [InlineData("PROGRAM_STOPPED", "Stop")]
    [InlineData("STOPPED", "Stop")]
    [InlineData("READY", "Reset")]
    [InlineData("MYSTERY", "Unknown(MYSTERY)")]
    public void MapExecutionToRunState_Cases(string input, string expected)
    {
        MTConnectStreamParser.MapExecutionToRunState(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("AUTOMATIC", "MEM")]
    [InlineData("MANUAL", "JOG")]
    [InlineData("MANUAL_DATA_INPUT", "MDI")]
    [InlineData("SEMI_AUTOMATIC", "HANDLE")]
    [InlineData("EDIT", "EDIT")]
    public void MapControllerMode_Cases(string input, string expected)
    {
        MTConnectStreamParser.MapControllerMode(input).Should().Be(expected);
    }
}
