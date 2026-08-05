// ============================================================================
// Tests: MTConnectProbeParser (M.2b.4 M2). Fixture-driven over realistic /probe
//        documents — proves the wizard's availability/axis discovery matches the
//        adapter's fixed semantic map, and exercises every edge state plan v2 §7
//        calls for (subset, none recognised, no axes, axis cap, multi-device,
//        malformed, not-MTConnect, conditions present/absent).
// ============================================================================

using System;
using System.Linq;
using System.Text;
using System.Xml;
using ElpisEdgeConnect.Sources.MTConnect;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.MTConnect.Tests;

public class MTConnectProbeParserTests
{
    // A full VMC: X/Y/Z linear + C rotary axis (all with Position), a spindle
    // Rotary (RotaryVelocity + Load, no Position), the standard controller
    // events/samples, and a Condition. → every standard tag available, 4 axes.
    private const string FullProbe = """
        <?xml version="1.0" encoding="UTF-8"?>
        <MTConnectDevices xmlns="urn:mtconnect.org:MTConnectDevices:1.7">
          <Header creationTime="2026-06-01T00:00:00Z" sender="agent" instanceId="1" version="1.7.0" bufferSize="131072"/>
          <Devices>
            <Device id="d1" name="VCN-530C" uuid="urn:mtc:vcn-530c">
              <Description manufacturer="Mazak" model="VCN-530C"/>
              <DataItems>
                <DataItem id="avail" type="AVAILABILITY" category="EVENT"/>
                <DataItem id="estop" type="EMERGENCY_STOP" category="EVENT"/>
              </DataItems>
              <Components>
                <Controller id="cn" name="controller">
                  <Components>
                    <Path id="path" name="path">
                      <DataItems>
                        <DataItem id="exec" type="EXECUTION" category="EVENT"/>
                        <DataItem id="mode" type="CONTROLLER_MODE" category="EVENT"/>
                        <DataItem id="prog" type="PROGRAM" category="EVENT"/>
                        <DataItem id="feed" type="PATH_FEEDRATE" category="SAMPLE" units="MILLIMETER/SECOND"/>
                        <DataItem id="pc" type="PART_COUNT" category="EVENT"/>
                        <DataItem id="ptimer" type="PROCESS_TIMER" category="SAMPLE" units="SECOND"/>
                      </DataItems>
                    </Path>
                  </Components>
                </Controller>
                <Axes id="ax" name="axes">
                  <Components>
                    <Linear id="x" name="X"><DataItems>
                      <DataItem id="xa" type="POSITION" subType="ACTUAL" category="SAMPLE" units="MILLIMETER"/>
                      <DataItem id="xm" type="POSITION" subType="MACHINE" category="SAMPLE" units="MILLIMETER"/>
                    </DataItems></Linear>
                    <Linear id="y" name="Y"><DataItems>
                      <DataItem id="ya" type="POSITION" subType="ACTUAL" category="SAMPLE" units="MILLIMETER"/>
                    </DataItems></Linear>
                    <Linear id="z" name="Z"><DataItems>
                      <DataItem id="za" type="POSITION" subType="ACTUAL" category="SAMPLE" units="MILLIMETER"/>
                    </DataItems></Linear>
                    <Rotary id="c" name="C"><DataItems>
                      <DataItem id="ca" type="POSITION" subType="ACTUAL" category="SAMPLE" units="DEGREE"/>
                    </DataItems></Rotary>
                    <Rotary id="spindle" name="S"><DataItems>
                      <DataItem id="srpm" type="ROTARY_VELOCITY" category="SAMPLE" units="REVOLUTION/MINUTE"/>
                      <DataItem id="sload" type="LOAD" category="SAMPLE" units="PERCENT"/>
                    </DataItems></Rotary>
                  </Components>
                </Axes>
                <Systems id="sys" name="systems">
                  <Components>
                    <Electric id="el" name="electric"><DataItems>
                      <DataItem id="sysc" type="SYSTEM" category="CONDITION"/>
                    </DataItems></Electric>
                  </Components>
                </Systems>
              </Components>
            </Device>
          </Devices>
        </MTConnectDevices>
        """;

    [Fact]
    public void Parse_FullProbe_AllStandardTagsAvailable_AndFourAxes()
    {
        var result = MTConnectProbeParser.Parse(FullProbe);

        result.TargetDeviceName.Should().Be("VCN-530C");
        result.Manufacturer.Should().Be("Mazak");
        result.HasRecognisedTags.Should().BeTrue();
        result.Tags.Should().OnlyContain(t => t.Available, "the full probe exposes every standard tag's source");
        result.Axes.Should().Equal("X", "Y", "Z", "C"); // spindle 'S' has no Position → not an axis
    }

    [Fact]
    public void Parse_FullProbe_RecordsSourceTypeAndId()
    {
        var result = MTConnectProbeParser.Parse(FullProbe);

        var spindle = result.Tags.Single(t => t.CanonicalTag == "spindle/speed");
        spindle.Available.Should().BeTrue();
        spindle.SourceDataItemType.Should().Be("ROTARY_VELOCITY");
        spindle.SourceDataItemId.Should().Be("srpm");
    }

    [Fact]
    public void Parse_Subset_SpindleLoadAndCycleTimeUnavailable_WithReason()
    {
        // Drop LOAD and PROCESS_TIMER and the Condition.
        var subset = FullProbe
            .Replace("<DataItem id=\"sload\" type=\"LOAD\" category=\"SAMPLE\" units=\"PERCENT\"/>", "")
            .Replace("<DataItem id=\"ptimer\" type=\"PROCESS_TIMER\" category=\"SAMPLE\" units=\"SECOND\"/>", "")
            .Replace("<DataItem id=\"sysc\" type=\"SYSTEM\" category=\"CONDITION\"/>", "");

        var result = MTConnectProbeParser.Parse(subset);

        result.Tags.Single(t => t.CanonicalTag == "spindle/load").Available.Should().BeFalse();
        result.Tags.Single(t => t.CanonicalTag == "spindle/load").Reason.Should().Contain("LOAD");
        result.Tags.Single(t => t.CanonicalTag == "production/cycle_time").Available.Should().BeFalse();
        result.Tags.Single(t => t.CanonicalTag == "alarms/count").Available.Should().BeFalse();
        // Spindle speed (ROTARY_VELOCITY) and others remain.
        result.Tags.Single(t => t.CanonicalTag == "spindle/speed").Available.Should().BeTrue();
        result.HasRecognisedTags.Should().BeTrue();
    }

    [Fact]
    public void Parse_NoRecognisedDataItems_HasRecognisedTagsFalse()
    {
        const string bare = """
            <MTConnectDevices xmlns="urn:mtconnect.org:MTConnectDevices:1.7">
              <Devices><Device id="d1" name="Mystery" uuid="u1">
                <DataItems><DataItem id="avail" type="AVAILABILITY" category="EVENT"/></DataItems>
              </Device></Devices>
            </MTConnectDevices>
            """;

        var result = MTConnectProbeParser.Parse(bare);

        result.HasRecognisedTags.Should().BeFalse();
        result.Tags.Should().OnlyContain(t => !t.Available);
        result.Axes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoAxisComponents_AxesEmpty()
    {
        const string noAxes = """
            <MTConnectDevices xmlns="urn:mtconnect.org:MTConnectDevices:1.7">
              <Devices><Device id="d1" name="Lathe" uuid="u1">
                <Components><Controller id="cn"><Components><Path id="p">
                  <DataItems><DataItem id="exec" type="EXECUTION" category="EVENT"/></DataItems>
                </Path></Components></Controller></Components>
              </Device></Devices>
            </MTConnectDevices>
            """;

        var result = MTConnectProbeParser.Parse(noAxes);

        result.Axes.Should().BeEmpty();
        result.Tags.Single(t => t.CanonicalTag == "status/run_state").Available.Should().BeTrue();
    }

    [Fact]
    public void Parse_ManyAxes_CappedAtMax()
    {
        var sb = new StringBuilder();
        sb.Append("<MTConnectDevices xmlns=\"urn:mtconnect.org:MTConnectDevices:1.7\"><Devices><Device id=\"d1\" name=\"Big\" uuid=\"u1\"><Components><Axes id=\"ax\">");
        for (var i = 0; i < 20; i++)
        {
            sb.Append($"<Linear id=\"a{i}\" name=\"AX{i}\"><DataItems><DataItem id=\"p{i}\" type=\"POSITION\" category=\"SAMPLE\"/></DataItems></Linear>");
        }
        sb.Append("</Axes></Components></Device></Devices></MTConnectDevices>");

        var result = MTConnectProbeParser.Parse(sb.ToString());

        result.Axes.Should().HaveCount(MTConnectProbeParser.MaxAxes);
    }

    [Fact]
    public void Parse_MultiDevice_ListsAll_AndSelectsNamedTarget()
    {
        const string multi = """
            <MTConnectDevices xmlns="urn:mtconnect.org:MTConnectDevices:1.7">
              <Devices>
                <Device id="d1" name="Mill-1" uuid="u1"><DataItems><DataItem id="e1" type="EXECUTION" category="EVENT"/></DataItems></Device>
                <Device id="d2" name="Lathe-2" uuid="u2"><DataItems><DataItem id="e2" type="EMERGENCY_STOP" category="EVENT"/></DataItems></Device>
              </Devices>
            </MTConnectDevices>
            """;

        MTConnectProbeParser.Parse(multi).DeviceNames.Should().Equal("Mill-1", "Lathe-2");
        MTConnectProbeParser.Parse(multi, "Lathe-2").TargetDeviceName.Should().Be("Lathe-2");
        // Unknown target → falls back to the first device.
        MTConnectProbeParser.Parse(multi, "Nope").TargetDeviceName.Should().Be("Mill-1");
    }

    [Fact]
    public void Parse_MalformedXml_ThrowsXmlException()
    {
        var act = () => MTConnectProbeParser.Parse("<MTConnectDevices><Devices><Device></Devices>");
        act.Should().Throw<XmlException>();
    }

    [Fact]
    public void Parse_NotMTConnectDevices_ThrowsFormatException()
    {
        var act = () => MTConnectProbeParser.Parse("<html><body>Not MTConnect</body></html>");
        act.Should().Throw<MTConnectProbeFormatException>();
    }

    [Fact]
    public void Parse_ValidEnvelopeNoDevices_ReturnsEmptyDevices()
    {
        var result = MTConnectProbeParser.Parse(
            "<MTConnectDevices xmlns=\"urn:mtconnect.org:MTConnectDevices:1.7\"><Devices/></MTConnectDevices>");

        result.DeviceNames.Should().BeEmpty();
        result.Tags.Should().BeEmpty();
    }

    // Drift guard: a probe containing every scalar mapping's FIRST probe type must
    // mark all scalar tags available — proves the shared map's probe types are the
    // ones the parser recognises (companion to the stream-parser tests on the
    // /current side, which use the same map's StreamElementNames).
    [Fact]
    public void Parse_ProbeWithEveryScalarType_MarksAllScalarTagsAvailable()
    {
        var sb = new StringBuilder();
        sb.Append("<MTConnectDevices xmlns=\"urn:mtconnect.org:MTConnectDevices:1.7\"><Devices><Device id=\"d1\" name=\"All\" uuid=\"u1\"><DataItems>");
        var i = 0;
        foreach (var mapping in MTConnectSemanticMap.Scalar)
        {
            sb.Append($"<DataItem id=\"i{i++}\" type=\"{mapping.ProbeDataItemTypes[0]}\" category=\"EVENT\"/>");
        }
        sb.Append("</DataItems></Device></Devices></MTConnectDevices>");

        var result = MTConnectProbeParser.Parse(sb.ToString());

        // Every scalar canonical tag should be available.
        result.Tags.Where(t => t.CanonicalTag is not "alarms/count" and not "alarms/first_fault")
            .Should().OnlyContain(t => t.Available);
    }
}
