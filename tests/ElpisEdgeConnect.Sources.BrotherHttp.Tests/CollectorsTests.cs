// ============================================================================
// Tests: Per-endpoint collectors. Each collector parses the legacy wire
//        format into BrotherPollSession. Tests use canned response strings
//        modelled on legacy BrotherHttpDataSource's documented examples.
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Sources.BrotherHttp.Collectors;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

public sealed class MachineInfoCollectorTests
{
    [Fact]
    public void Collect_HappyPath_ExtractsHostnameModelStatusCode()
    {
        var session = new BrotherPollSession();

        MachineInfoCollector.Collect("BRN68E74A6608EA,SXd1,3,01,0,1", session);

        session.Hostname.Should().Be("BRN68E74A6608EA");
        session.Model.Should().Be("SXd1");
        session.StatusCode.Should().Be(3);
        session.MachineInfoEndpointFailed.Should().BeFalse();
    }

    [Fact]
    public void Collect_NullResponse_SetsEndpointFailedFlag()
    {
        var session = new BrotherPollSession();

        MachineInfoCollector.Collect(response: null, session);

        session.MachineInfoEndpointFailed.Should().BeTrue();
        session.Hostname.Should().BeNull();
    }

    [Fact]
    public void Collect_EmptyResponse_DoesNotFail_ButLeavesValuesNull()
    {
        var session = new BrotherPollSession();

        MachineInfoCollector.Collect("   ", session);

        session.MachineInfoEndpointFailed.Should().BeFalse();
        session.Hostname.Should().BeNull();
    }

    [Fact]
    public void Collect_MalformedResponse_LeavesValuesNull()
    {
        var session = new BrotherPollSession();

        MachineInfoCollector.Collect("only,two", session);

        session.Hostname.Should().BeNull();
        session.Model.Should().BeNull();
        session.StatusCode.Should().BeNull();
    }
}

public sealed class CycleTimeCollectorTests
{
    private const string SampleResponse =
        "Program,(,0926,)\n" +
        "Cycle time,0000:04:56.2\n" +
        "Cutting time,0000:04:43.5\n" +
        "Non cutting time,0000:00:12.7\n" +
        "Cutting time / cycle time, 95 %\n" +
        "Operation time,3462:45:28\n" +
        "Power on time,2496:17:52\n" +
        "Operation end counter,0994";

    [Fact]
    public void Collect_HappyPath_ParsesProgramAndTimes()
    {
        var session = new BrotherPollSession();

        CycleTimeCollector.Collect(SampleResponse, session);

        session.Program.Should().Be("O0926");
        session.CycleSeconds.Should().BeApproximately(296.2, 0.01);
        session.CuttingSeconds.Should().BeApproximately(283.5, 0.01);
        session.OperationHours.Should().BeApproximately(3462.8, 0.1);
        session.PowerOnHours.Should().BeApproximately(2496.3, 0.1);
        session.EndCounter.Should().Be(994);
        session.CuttingRatioPercent.Should().Be(95);
    }

    [Fact]
    public void Collect_NullResponse_SetsEndpointFailedFlag()
    {
        var session = new BrotherPollSession();

        CycleTimeCollector.Collect(null, session);

        session.CycleTimeEndpointFailed.Should().BeTrue();
    }

    [Fact]
    public void Collect_DistinguishesCuttingTime_FromCuttingRatio()
    {
        // The "Cutting time," prefix matches BOTH "Cutting time,X" and
        // "Cutting time / cycle time,X". Pin: only the first one writes
        // CuttingSeconds; the second writes CuttingRatioPercent.
        var session = new BrotherPollSession();

        CycleTimeCollector.Collect(
            "Cutting time,0000:04:43.5\nCutting time / cycle time, 95 %",
            session);

        session.CuttingSeconds.Should().BeApproximately(283.5, 0.01);
        session.CuttingRatioPercent.Should().Be(95);
    }
}

public sealed class WorkCounterCollectorTests
{
    [Fact]
    public void Collect_HappyPath_FirstCounterIsPartsCount()
    {
        var session = new BrotherPollSession();

        WorkCounterCollector.Collect("999,     0,     0,     0,,0\n0,0,0,0,,0", session);

        session.PartsCount.Should().Be(999);
        session.Counters.Should().ContainKey(1);
        session.Counters[1].Count.Should().Be(999);
    }

    [Fact]
    public void Collect_ZeroValues_AreNotEmitted()
    {
        // Sparse: when count and target are both zero, the counter is omitted.
        var session = new BrotherPollSession();

        WorkCounterCollector.Collect("0,0,0,0,,0\n0,0,0,0,,0", session);

        session.PartsCount.Should().Be(0);
        // First counter has all zeros — should be absent from dict.
        session.Counters.Should().NotContainKey(1);
    }

    [Fact]
    public void Collect_PopulatedCounter_StoresCountAndTarget()
    {
        var session = new BrotherPollSession();

        // Counter 2 has count=15, target=50
        WorkCounterCollector.Collect(
            "100,0,0,0,,0\n15,0,50,0,,0\n0,0,0,0,,0\n0,0,0,0,,0",
            session);

        session.Counters.Should().ContainKey(2);
        session.Counters[2].Count.Should().Be(15);
        session.Counters[2].Target.Should().Be(50);
    }

    [Fact]
    public void Collect_NullResponse_SetsEndpointFailedFlag()
    {
        var session = new BrotherPollSession();

        WorkCounterCollector.Collect(null, session);

        session.WorkCountersEndpointFailed.Should().BeTrue();
    }
}

public sealed class AtcToolsCollectorTests
{
    [Fact]
    public void Collect_ProgramHeader_IsSkipped()
    {
        var session = new BrotherPollSession();

        AtcToolsCollector.Collect(
            "Program(0926)\n01,,,001,,,              ,,, 448.254x0.000,,,,,,********,,,STD tool,,,,#a0a0a4,#a0a0a4,0",
            session);

        session.Magazine.Should().HaveCount(1);
        session.Magazine[0].Slot.Should().Be(1);
    }

    [Fact]
    public void Collect_ActiveToolHighlight_IsDetected()
    {
        // Active tool has both color columns starting with '#' (e.g. #000000, #ff8000).
        var session = new BrotherPollSession();

        AtcToolsCollector.Collect(
            "01,,,001,,,              ,,, 448.254x0.000,,,,,,********,,,STD tool,,,,#a0a0a4,#a0a0a4,0\n" +
            "11,#000000,#ff8000,011,,,DRILL6.35     ,,, 537.900x10.100,,,,,,********,,,STD tool,,,,#a0a0a4,#a0a0a4,0",
            session);

        session.ActiveToolNumber.Should().Be(11);
        session.Magazine.Should().HaveCount(2);
        session.Magazine[0].IsActive.Should().BeFalse();
        session.Magazine[1].IsActive.Should().BeTrue();
        session.Magazine[1].Name.Should().Be("DRILL6.35");
        session.Magazine[1].Length.Should().BeApproximately(537.900, 0.001);
        session.Magazine[1].Radius.Should().BeApproximately(10.100, 0.001);
        session.Magazine[1].Type.Should().Be("STD tool");
        session.Magazine[1].Life.Should().BeNull();   // "********" → null
    }

    [Fact]
    public void Collect_EmptySlotName_StoredAsNull()
    {
        var session = new BrotherPollSession();

        AtcToolsCollector.Collect(
            "01,,,001,,,              ,,, 448.254x0.000,,,,,,********,,,STD tool,,,,#a0a0a4,#a0a0a4,0",
            session);

        session.Magazine[0].Name.Should().BeNull();
    }

    [Fact]
    public void Collect_NullResponse_SetsEndpointFailedFlag()
    {
        var session = new BrotherPollSession();

        AtcToolsCollector.Collect(null, session);

        session.AtcToolsEndpointFailed.Should().BeTrue();
    }
}

public sealed class AlarmCollectorTests
{
    [Fact]
    public void Collect_EmptyBody_LeavesNoActiveAlarms()
    {
        var session = new BrotherPollSession();

        AlarmCollector.Collect("", session);

        session.ActiveAlarms.Should().BeEmpty();
        session.InformationalAlarmMessage.Should().BeNull();
        session.MaintenanceAlarmMessages.Should().BeEmpty();
        session.AlarmsEndpointFailed.Should().BeFalse();
    }

    [Fact]
    public void Collect_NullResponse_SetsEndpointFailedFlag()
    {
        var session = new BrotherPollSession();

        AlarmCollector.Collect(null, session);

        session.AlarmsEndpointFailed.Should().BeTrue();
    }

    [Fact]
    public void Collect_StandbyAlarm501_GoesToInformational_NotActive()
    {
        var session = new BrotherPollSession();

        AlarmCollector.Collect("0501, *Standby mode,0926,001186,3", session);

        session.ActiveAlarms.Should().BeEmpty();
        session.InformationalAlarmMessage.Should().Be("*Standby mode");
    }

    [Theory]
    [InlineData("GREASING XYZ AXIS")]
    [InlineData("FILTER replacement due")]
    [InlineData("LUBRICATION required")]
    public void Collect_MaintenanceKeyword_GoesToMaintenance_NotActive(string message)
    {
        var session = new BrotherPollSession();

        AlarmCollector.Collect($"0800,{message},0926,001186,4", session);

        session.ActiveAlarms.Should().BeEmpty();
        session.MaintenanceAlarmMessages.Should().ContainSingle().Which.Should().Be(message);
    }

    [Fact]
    public void Collect_RealAlarm_GoesToActiveAlarms()
    {
        var session = new BrotherPollSession();

        AlarmCollector.Collect("0512, *Servo overheat,0926,001186,4", session);

        session.ActiveAlarms.Should().ContainSingle();
        session.ActiveAlarms[0].Number.Should().Be(512);
        session.ActiveAlarms[0].Message.Should().Be("*Servo overheat");
    }

    [Fact]
    public void Collect_MultipleAlarms_AllParsed()
    {
        var session = new BrotherPollSession();

        AlarmCollector.Collect(
            "0512, *Servo overheat,0926,001186,4\n" +
            "0501, *Standby mode,0926,001186,3\n" +
            "0800, *GREASING XYZ AXIS,0926,001186,4",
            session);

        session.ActiveAlarms.Should().ContainSingle()
            .Which.Number.Should().Be(512);
        session.InformationalAlarmMessage.Should().Be("*Standby mode");
        session.MaintenanceAlarmMessages.Should().ContainSingle()
            .Which.Should().Contain("GREASING");
    }
}

public sealed class MaintenanceCollectorTests
{
    [Fact]
    public void Collect_HappyPath_ParsesNoticeAndDuePercent()
    {
        var session = new BrotherPollSession();

        MaintenanceCollector.Collect(
            "Z-axis travel distance),GREASING XYZ AXIS,Valid,87000,100000,Notified,Warning,#ff8000",
            session);

        session.Notices.Should().ContainSingle();
        var notice = session.Notices[0];
        notice.Description.Should().Be("GREASING XYZ AXIS");
        notice.Condition.Should().Be("Z-axis travel distance)");
        notice.Status.Should().Be("Valid");
        notice.Notified.Should().Be("Notified");
        notice.State.Should().Be("Warning");
        notice.DuePercent.Should().Be(87);   // 87000 / 100000 * 100 = 87
    }

    [Fact]
    public void Collect_NoneLine_ProducesNoNotices()
    {
        var session = new BrotherPollSession();

        MaintenanceCollector.Collect("None", session);

        session.Notices.Should().BeEmpty();
        session.MaintenanceNoticesEndpointFailed.Should().BeFalse();
    }

    [Fact]
    public void Collect_ZeroLimit_LeavesDuePercentNull()
    {
        var session = new BrotherPollSession();

        MaintenanceCollector.Collect(
            "Condition,Description,Valid,0,0,Notified,Normal,#00ff00",
            session);

        session.Notices.Should().ContainSingle();
        session.Notices[0].DuePercent.Should().BeNull();
    }

    [Fact]
    public void Collect_NullResponse_SetsEndpointFailedFlag()
    {
        var session = new BrotherPollSession();

        MaintenanceCollector.Collect(null, session);

        session.MaintenanceNoticesEndpointFailed.Should().BeTrue();
    }
}
