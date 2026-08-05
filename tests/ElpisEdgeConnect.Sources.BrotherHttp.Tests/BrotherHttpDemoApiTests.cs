// ============================================================================
// Tests: BrotherHttpDemoApi — synthetic-CNC scenario rotation + state
//        evolution per v3.1 §C.2. Uses an injectable clock so cycle
//        progression is deterministic and Thread.Sleep is never needed.
// Reference: src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpDemoApi.cs
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

public sealed class BrotherHttpDemoApiTests
{
    private static readonly DateTime _t0 = new(2026, 5, 21, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, DemoScenario.Running)]
    [InlineData(11, DemoScenario.Running)]
    [InlineData(12, DemoScenario.Idle)]
    [InlineData(24, DemoScenario.Alarm)]
    [InlineData(36, DemoScenario.Standby)]
    [InlineData(48, DemoScenario.Maintenance)]
    [InlineData(60, DemoScenario.Running)]    // wraps after 60s full cycle
    [InlineData(72, DemoScenario.Idle)]
    public void ScenarioRotation_FollowsPhasedSchedule(int elapsedSeconds, DemoScenario expected)
    {
        var clock = MakeClock(elapsedSeconds);
        var api = new BrotherHttpDemoApi(clock);

        api.CurrentScenarioForTesting.Should().Be(expected);
    }

    [Fact]
    public async Task AllSixEndpoints_AlwaysReturnNonNull_AcrossAllScenarios()
    {
        // The IBrotherHttpApi contract permits null on transient failure,
        // but the demo backend never has transient failures — every endpoint
        // must always yield a response. This pins the "demo always available"
        // invariant.
        foreach (DemoScenario scenario in Enum.GetValues<DemoScenario>())
        {
            var phaseSeconds = (int)scenario * 12;
            var api = new BrotherHttpDemoApi(MakeClock(phaseSeconds));

            (await api.GetMachineInfoAsync(CancellationToken.None))
                .Should().NotBeNull($"MachineInfo must be present in {scenario}");
            (await api.GetCycleTimeAsync(CancellationToken.None))
                .Should().NotBeNull($"CycleTime must be present in {scenario}");
            (await api.GetWorkCountersAsync(CancellationToken.None))
                .Should().NotBeNull($"WorkCounters must be present in {scenario}");
            (await api.GetAtcToolsAsync(CancellationToken.None))
                .Should().NotBeNull($"AtcTools must be present in {scenario}");
            (await api.GetAlarmsAsync(CancellationToken.None))
                .Should().NotBeNull($"Alarms response must be present (empty body is still non-null) in {scenario}");
            (await api.GetMaintenanceNoticesAsync(CancellationToken.None))
                .Should().NotBeNull($"MaintenanceNotices must be present in {scenario}");
        }
    }

    [Fact]
    public async Task MachineInfo_StatusCode_MatchesScenarioMapping()
    {
        var matrix = new (DemoScenario Scenario, int Seconds, string ExpectedStatusCode)[]
        {
            (DemoScenario.Running, 0, "3"),
            (DemoScenario.Idle, 12, "1"),
            (DemoScenario.Alarm, 24, "4"),
            (DemoScenario.Standby, 36, "5"),
            (DemoScenario.Maintenance, 48, "1"),
        };

        foreach (var (scenario, seconds, expectedCode) in matrix)
        {
            var api = new BrotherHttpDemoApi(MakeClock(seconds));
            var response = await api.GetMachineInfoAsync(CancellationToken.None);

            response.Should().NotBeNull();
            var parts = response!.Split(',');
            parts.Length.Should().BeGreaterOrEqualTo(3, $"machine info must have at least 3 CSV fields in {scenario}");
            parts[2].Should().Be(expectedCode, $"status code mismatch for {scenario}");
        }
    }

    [Fact]
    public async Task Alarms_OnlyEmittedInAlarmOrStandbyScenarios()
    {
        var emptyBodyScenarios = new[] { DemoScenario.Running, DemoScenario.Idle, DemoScenario.Maintenance };
        foreach (var scenario in emptyBodyScenarios)
        {
            var api = new BrotherHttpDemoApi(MakeClock((int)scenario * 12));
            var alarms = await api.GetAlarmsAsync(CancellationToken.None);
            alarms.Should().BeEmpty($"{scenario} scenario must emit no active alarms");
        }

        var alarmApi = new BrotherHttpDemoApi(MakeClock(24));   // Alarm phase
        (await alarmApi.GetAlarmsAsync(CancellationToken.None))
            .Should().Contain("0512").And.Contain("Servo overheat");

        var standbyApi = new BrotherHttpDemoApi(MakeClock(36)); // Standby phase
        (await standbyApi.GetAlarmsAsync(CancellationToken.None))
            .Should().Contain("0501").And.Contain("Standby mode");
    }

    [Fact]
    public async Task MaintenanceNotices_OnlyEmittedInMaintenanceScenario()
    {
        var nonMaintenance = new[] { DemoScenario.Running, DemoScenario.Idle, DemoScenario.Alarm, DemoScenario.Standby };
        foreach (var scenario in nonMaintenance)
        {
            var api = new BrotherHttpDemoApi(MakeClock((int)scenario * 12));
            var notices = await api.GetMaintenanceNoticesAsync(CancellationToken.None);
            notices.Should().Be("None", $"{scenario} must report no maintenance notices");
        }

        var maintApi = new BrotherHttpDemoApi(MakeClock(48));
        var maintResp = await maintApi.GetMaintenanceNoticesAsync(CancellationToken.None);
        maintResp.Should()
            .Contain("GREASING XYZ AXIS")
            .And.Contain("Notified");
    }

    [Fact]
    public async Task PartsCount_IsMonotonicAcrossCycles()
    {
        // Pins v3.1 §C.2: parts counter advances cycle over cycle so the
        // demo doesn't look frozen during prolonged UAT.
        var earlyApi = new BrotherHttpDemoApi(MakeClock(0));
        var earlyResp = await earlyApi.GetWorkCountersAsync(CancellationToken.None);
        var earlyCount = ParseFirstCounter(earlyResp!);

        var lateApi = new BrotherHttpDemoApi(MakeClock(60 * 5)); // 5 full cycles
        var lateResp = await lateApi.GetWorkCountersAsync(CancellationToken.None);
        var lateCount = ParseFirstCounter(lateResp!);

        lateCount.Should().BeGreaterThan(earlyCount, "parts counter must drift up across cycles");
    }

    [Fact]
    public async Task MaintenanceDuePercent_DriftsUpwardAcrossCycles()
    {
        // Demo always shows maintenance notice in the Maintenance phase (48s).
        // Pin: the DuePercent rises over time per v3.1 §C.2.
        var earlyApi = new BrotherHttpDemoApi(MakeClock(48)); // 0th cycle
        var earlyResp = await earlyApi.GetMaintenanceNoticesAsync(CancellationToken.None);

        var lateApi = new BrotherHttpDemoApi(MakeClock(48 + 60 * 10)); // 10 cycles later
        var lateResp = await lateApi.GetMaintenanceNoticesAsync(CancellationToken.None);

        // Parse the "current" field (column index 3) — drift shows up there.
        var earlyCurrent = ParseMaintenanceCurrent(earlyResp!);
        var lateCurrent = ParseMaintenanceCurrent(lateResp!);

        lateCurrent.Should().BeGreaterThan(earlyCurrent, "DuePercent drift must be visible in the 'current' field over 10 cycles");
    }

    [Fact]
    public async Task CycleTime_JittersDeterministicallyAcrossCycles()
    {
        // Pin v3.1 §C.2 — jitter is reproducible (same cycle index → same
        // value) so test assertions remain stable.
        var resp1 = await new BrotherHttpDemoApi(MakeClock(0))
            .GetCycleTimeAsync(CancellationToken.None);
        var resp2 = await new BrotherHttpDemoApi(MakeClock(0))
            .GetCycleTimeAsync(CancellationToken.None);

        resp1.Should().Be(resp2, "same cycle index must yield identical jittered cycle time");
    }

    [Fact]
    public void Constructor_NullClock_Throws()
    {
        Action act = () => _ = new BrotherHttpDemoApi(clock: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static Func<DateTime> MakeClock(int elapsedSeconds)
    {
        // Returns a clock function that:
        //   * On the first call (when the ctor reads _startedAt), returns _t0
        //   * On subsequent calls, returns _t0 + elapsed
        // This is consistent with FOCAS2's deterministic-clock pattern.
        var calls = 0;
        return () =>
        {
            var t = calls == 0 ? _t0 : _t0.AddSeconds(elapsedSeconds);
            calls++;
            return t;
        };
    }

    private static long ParseFirstCounter(string workCountersResponse)
    {
        var firstLine = workCountersResponse.Split('\n')[0];
        var parts = firstLine.Split(',');
        return long.Parse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long ParseMaintenanceCurrent(string maintenanceResponse)
    {
        var firstLine = maintenanceResponse.Split('\n')[0];
        var parts = firstLine.Split(',');
        // Column index 3 is "current" per legacy format
        return long.Parse(parts[3].Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
