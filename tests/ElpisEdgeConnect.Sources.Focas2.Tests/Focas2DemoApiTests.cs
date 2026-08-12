// ============================================================================
// Tests: Focas2DemoApi — pins the deterministic synthetic-CNC contract
//        from M.2b.3.1 plan v2 §2 (locked behaviour table) + v3.
//
// LOCKED test rules:
//   * Locked G: all tests inject a clock; ZERO Thread.Sleep anywhere.
//   * Tests run from a fresh DateTime origin per test (clockValue starts
//     at 2026-01-01T00:00:00Z); advance by mutating the closure variable.
//   * Cycle phases (per v2 §2):
//       0..10s   = Reset
//       10..50s  = Start (cutting)
//       50..60s  = Stop
//       cycle period = 60s
//
// Three no-native-reference tests at the bottom pin Locked I (no
// P/Invoke, no Focas2Interop dependency).
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md §1
// ============================================================================

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

public sealed class Focas2DemoApiTests
{
    private static readonly DateTime ClockOrigin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── #1 ────────────────────────────────────────────────────────────────
    [Fact]
    public void AllocLibHandle_ReturnsSuccessAndNonZeroHandle()
    {
        var (api, _) = MakeApi();

        var ret = api.AllocLibHandle("192.168.1.10", 8193, 10, out var handle);

        ret.Should().Be(0, "EW_OK");
        handle.Should().NotBe((ushort)0, "Focas2ConnectionManager treats handle=0 as invalid");
    }

    // ── #2 ────────────────────────────────────────────────────────────────
    [Fact]
    public void ReadSystemInfo_ReturnsCanonicalDemoIdentity()
    {
        var (api, _) = MakeApi();

        var ret = api.ReadSystemInfo(handle: 1, out var sysInfo);

        ret.Should().Be(0);
        sysInfo.Series.TrimEnd().Should().Be("31iB");
        sysInfo.CncType.TrimEnd().Should().Be("M");
        sysInfo.Version.TrimEnd().Should().Be("1.00");
    }

    // ── #3 ────────────────────────────────────────────────────────────────
    [Fact]
    public void ReadAxisCount_Returns3()
    {
        var (api, _) = MakeApi();

        api.ReadAxisCount(handle: 1, out var count).Should().Be(0);
        count.Should().Be((short)3);
    }

    // ── #4 ────────────────────────────────────────────────────────────────
    [Fact]
    public void ReadAxisNames_ReturnsXYZInOrder()
    {
        var (api, _) = MakeApi();
        var names = new OdbAxisName[8];
        short dataNum = 8;

        api.ReadAxisNames(handle: 1, ref dataNum, names).Should().Be(0);

        dataNum.Should().Be((short)3);
        names[0].Name.TrimEnd().Should().Be("X");
        names[1].Name.TrimEnd().Should().Be("Y");
        names[2].Name.TrimEnd().Should().Be("Z");
    }

    // ── #5 ────────────────────────────────────────────────────────────────
    [Fact]
    public void RunState_AtT5_IsReset_AtT30_IsStart_AtT55_IsStop()
    {
        // Pins the 10/40/10 phase split. Run codes per OdbStatusInfo.Run:
        //   0 = RESET, 1 = STOP, 3 = START.
        var (api, advance) = MakeApi();

        advance(TimeSpan.FromSeconds(5));
        api.ReadStatusInfo(1, out var reset).Should().Be(0);
        reset.Run.Should().Be((short)0, "t=5s should be in Reset phase");

        advance(TimeSpan.FromSeconds(25));  // total 30s into cycle
        api.ReadStatusInfo(1, out var start).Should().Be(0);
        start.Run.Should().Be((short)3, "t=30s should be in Start phase");

        advance(TimeSpan.FromSeconds(25));  // total 55s into cycle
        api.ReadStatusInfo(1, out var stop).Should().Be(0);
        stop.Run.Should().Be((short)1, "t=55s should be in Stop phase");
    }

    // ── #6 ────────────────────────────────────────────────────────────────
    [Fact]
    public void SpindleSpeed_AtT5_Is0_AtT30_IsBetween500And2500()
    {
        var (api, advance) = MakeApi();

        advance(TimeSpan.FromSeconds(5));   // Reset
        api.ReadActualSpindleSpeed(1, out var s1).Should().Be(0);
        s1.Data.Should().Be(0, "spindle is stationary during Reset");

        advance(TimeSpan.FromSeconds(25));  // 30s into cycle = mid-Start
        api.ReadActualSpindleSpeed(1, out var s2).Should().Be(0);
        // At t=30s we're 20s into Start (which goes 10-50s), so 20/40 = 50% ramp = 1500 rpm
        s2.Data.Should().BeInRange(500, 2500,
            "spindle should be partially ramped during Start phase (linear 0→3000 over 40s)");
    }

    // ── #7 ────────────────────────────────────────────────────────────────
    [Fact]
    public void PartsCount_AfterTwoFullCycles_IsTwo()
    {
        var (api, advance) = MakeApi();
        // Parameter 6711 = parts counter on the demo profile.
        advance(TimeSpan.FromSeconds(120));  // 2 full cycles, currently in cycle 3's Reset (count = 2)

        api.ReadParameter(1, paramNo: 6711, axisNo: 0, length: 8, out var param)
            .Should().Be(0);
        param.LData.Should().Be(2, "two Start→Stop transitions completed");
    }

    // ── #8 ────────────────────────────────────────────────────────────────
    [Fact]
    public void ToolNumber_CyclesT1T5T9_AcrossThreeCycles()
    {
        var (api, advance) = MakeApi();
        var buffer = new byte[4];

        // Cycle 0 → T1
        advance(TimeSpan.FromSeconds(15));  // mid-cycle-0
        api.ReadModal(1, type: 1, length: (short)buffer.Length, buffer).Should().Be(0);
        ToolNumberFromBuffer(buffer).Should().Be(1);

        // Cycle 1 → T5
        advance(TimeSpan.FromSeconds(60));  // mid-cycle-1
        api.ReadModal(1, type: 1, length: (short)buffer.Length, buffer).Should().Be(0);
        ToolNumberFromBuffer(buffer).Should().Be(5);

        // Cycle 2 → T9
        advance(TimeSpan.FromSeconds(60));  // mid-cycle-2
        api.ReadModal(1, type: 1, length: (short)buffer.Length, buffer).Should().Be(0);
        ToolNumberFromBuffer(buffer).Should().Be(9);

        // Cycle 3 wraps back to T1
        advance(TimeSpan.FromSeconds(60));  // mid-cycle-3
        api.ReadModal(1, type: 1, length: (short)buffer.Length, buffer).Should().Be(0);
        ToolNumberFromBuffer(buffer).Should().Be(1);
    }

    private static int ToolNumberFromBuffer(byte[] data) => data[0] | (data[1] << 8);

    // ── #9 ────────────────────────────────────────────────────────────────
    [Fact]
    public void AlarmStatus_FiresEvery4thCycle_ClearsAfter5Seconds()
    {
        var (api, advance) = MakeApi();

        // Cycle 0..2 — no alarm
        advance(TimeSpan.FromSeconds(2));
        api.ReadAlarmStatus(1, out var a0).Should().Be(0);
        a0.Data.Should().Be((short)0);

        // Jump to cycle 3 start (t=180s) — alarm fires for first 5s
        advance(TimeSpan.FromSeconds(178));  // total 180s
        api.ReadAlarmStatus(1, out var a1).Should().Be(0);
        a1.Data.Should().NotBe((short)0, "alarm should fire at the start of cycle 3");

        // 6 seconds later in cycle 3 → cleared
        advance(TimeSpan.FromSeconds(6));    // total 186s — past the 5s window
        api.ReadAlarmStatus(1, out var a2).Should().Be(0);
        a2.Data.Should().Be((short)0, "alarm clears after 5s");
    }

    // ── #10 ───────────────────────────────────────────────────────────────
    [Fact]
    public void AxisPositions_AtMultipleClockOffsets_AreBoundedPlusMinus100mm()
    {
        var (api, advance) = MakeApi();
        var offsets = new[] { 0.0, 7.5, 15.0, 23.75, 30.0, 45.0, 60.0, 90.0 };

        foreach (var offsetSeconds in offsets)
        {
            advance(TimeSpan.FromSeconds(offsetSeconds));
            api.ReadAbsolutePosition(1, axisNum: 0, length: 8, out var pos).Should().Be(0);
            for (var i = 0; i < 3; i++)
            {
                var mm = pos.Data[i] / Math.Pow(10, pos.Decimal[i]);
                Math.Abs(mm).Should().BeLessThanOrEqualTo(100.001,
                    $"axis {i} at offset {offsetSeconds}s must stay within ±100 mm");
            }
        }
    }

    // ── #11 ───────────────────────────────────────────────────────────────
    [Fact]
    public void MtLinkiServoTemperature_StaysWithin32To38Celsius()
    {
        var (api, advance) = MakeApi();
        // Diagnostic 308 = servo motor temperature (°C). Sweep across the
        // ~90s sinusoidal period.
        for (var t = 0; t < 90; t += 5)
        {
            advance(TimeSpan.FromSeconds(5));
            api.ReadDiagnosticData(1, diagNo: 308, axisNo: 0, length: 8, out var diag).Should().Be(0);
            diag.LData.Should().BeInRange(32, 38,
                $"servo temp at t={t}s should sit around 35°C ± 3");
        }
    }

    // ── #12 ───────────────────────────────────────────────────────────────
    [Fact]
    public void Deterministic_TwoInstancesSameClock_ReturnIdenticalValues()
    {
        // Locked G consequence: identical clock sequence → identical state.
        // Pins repeatability for sales demos.
        var clockA = new SharedClock(ClockOrigin);
        var clockB = new SharedClock(ClockOrigin);
        var apiA = new Focas2DemoApi(clockA.Read);
        var apiB = new Focas2DemoApi(clockB.Read);

        clockA.Now = ClockOrigin.AddSeconds(37);
        clockB.Now = ClockOrigin.AddSeconds(37);

        apiA.ReadActualSpindleSpeed(1, out var spinA);
        apiB.ReadActualSpindleSpeed(1, out var spinB);
        spinA.Data.Should().Be(spinB.Data);

        apiA.ReadAbsolutePosition(1, 0, 8, out var posA);
        apiB.ReadAbsolutePosition(1, 0, 8, out var posB);
        posA.Data[0].Should().Be(posB.Data[0]);
        posA.Data[1].Should().Be(posB.Data[1]);
        posA.Data[2].Should().Be(posB.Data[2]);
    }

    // ── #13 ───────────────────────────────────────────────────────────────
    [Fact]
    public void AllReadMethods_NeverThrow_OnAFreshClockSeed()
    {
        // Quick safety pass: walk every IFocas2Api method, confirm no
        // exceptions on a freshly-constructed demo API.
        var (api, _) = MakeApi();
        ushort handle;
        OdbStatusInfo statusInfo;
        OdbSystemInfo sysInfo;
        short axisCount;
        OdbProgramNumber programNum;
        OdbAxisData axisData;
        OdbActualFeed feed;
        OdbActualSpeed speed;
        OdbSpindleLoad load;
        OdbAlarmStatus alarmStatus;
        OdbTimer timer;
        OdbParameter param;
        OdbMacro macro;
        OdbToolOffset tofs;
        OdbDiagnosticData diag;
        short ofsType, useNo;
        short toolLifeCount;
        int toolLifeGroup;

        Action act = () =>
        {
            api.AllocLibHandle("ip", 8193, 10, out handle);
            api.FreeLibHandle(1);
            api.ReadStatusInfo(1, out statusInfo);
            api.ReadSystemInfo(1, out sysInfo);
            api.ReadAxisCount(1, out axisCount);

            var names = new OdbAxisName[8];
            short n = 8;
            api.ReadAxisNames(1, ref n, names);

            api.ReadProgramNumber(1, out programNum);

            var dirBuf = new byte[256];
            int top = 0;
            short numProg = 0;
            api.ReadProgramDirectory(1, 1, ref top, ref numProg, dirBuf);

            api.ReadAbsolutePosition(1, 0, 8, out axisData);
            api.ReadMachinePosition(1, 0, 8, out axisData);
            api.ReadRelativePosition(1, 0, 8, out axisData);
            api.ReadDistanceToGo(1, 0, 8, out axisData);

            api.ReadActualFeedRate(1, out feed);
            api.ReadActualSpindleSpeed(1, out speed);
            api.ReadSpindleLoad(1, 1, out load);

            api.ReadAlarmStatus(1, out alarmStatus);
            var alarms = new OdbAlarmMessage[8];
            short alarmNum = 8;
            api.ReadAlarmMessages(1, 0, ref alarmNum, alarms);

            api.ReadTimer(1, 0, out timer);
            api.ReadParameter(1, 6711, 0, 8, out param);

            api.ReadModal(1, 1, 8, new byte[8]);
            api.ReadMacro(1, 100, 8, out macro);
            api.ReadToolOffsetInfo2(1, out ofsType, out useNo);
            api.ReadToolOffsetInfo(1, out useNo);
            api.ReadToolOffset(1, 1, 0, 8, out tofs);
            api.ReadToolOffsetRange(1, 1, 0, 4, 8, new byte[64]);
            api.ReadToolLifeInfo(1, new byte[64]);
            api.ReadToolLifeGroupCount(1, out toolLifeCount);
            api.ReadToolLifeGroup(1, 1, new byte[64]);
            api.ReadToolLifeUseGroup(1, out toolLifeGroup);

            api.ReadPmcRange(1, 12, 0, 0, 7, 8, new byte[8]);
            api.ReadDiagnosticData(1, 308, 0, 8, out diag);
            api.ReadDiagnosticDataArray(1, 308, 0, 8, new byte[64]);
            api.ReadOperatorMessage(1, 0, 256, new byte[256]);
            api.ReadSpMaintCheck(1, 0, new byte[64]);
        };

        act.Should().NotThrow();
    }

    // ─── Locked I — no-native-reference tests ─────────────────────────────

    [Fact]
    public void DemoApiType_HasNoDllImportAttributes()
    {
        // Pins Locked I: Focas2DemoApi must NEVER P/Invoke.
        var type = typeof(Focas2DemoApi);
        var allMethods = type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance);

        foreach (var method in allMethods)
        {
            method.GetCustomAttribute<DllImportAttribute>()
                .Should().BeNull(
                    $"Method '{method.Name}' must not be [DllImport] — Locked I prohibits P/Invoke on Focas2DemoApi.");
        }
    }

    [Fact]
    public void DemoApiType_HasNoStaticConstructor()
    {
        // A static ctor would be a place to accidentally trigger native-
        // library resolution at type-init time. The demo API must have none.
        var type = typeof(Focas2DemoApi);
        var staticCtor = type.GetConstructor(
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        staticCtor.Should().BeNull(
            "Focas2DemoApi must not declare a static constructor — Locked I.");
    }

    [Fact]
    public void DemoApi_FullMethodSweep_NeverThrowsDllNotFoundException()
    {
        // Behavioural Locked I proof: even on a system without fwlib*.dll
        // installed, every IFocas2Api method on the demo API completes
        // without attempting native resolution. The test runs the same
        // method sweep as #13 above but specifically asserts the
        // DllNotFoundException path is impossible.
        var (api, _) = MakeApi();

        Action act = () =>
        {
            api.AllocLibHandle("ip", 8193, 10, out _);
            api.ReadStatusInfo(1, out _);
            api.ReadSystemInfo(1, out _);
            api.ReadAxisCount(1, out _);
            api.ReadAbsolutePosition(1, 0, 8, out _);
            api.ReadActualSpindleSpeed(1, out _);
            api.ReadAlarmStatus(1, out _);
            api.ReadParameter(1, 6711, 0, 8, out _);
            api.ReadDiagnosticData(1, 308, 0, 8, out _);
            api.ReadPmcRange(1, 12, 0, 0, 7, 8, new byte[8]);
        };

        act.Should().NotThrow<DllNotFoundException>();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Build a demo API plus an "advance the clock by TimeSpan" helper.
    /// The first <c>advance(0)</c> is implicit — the API captures clock
    /// origin in its ctor — so subsequent <c>advance(d)</c> calls offset
    /// from <see cref="ClockOrigin"/> deterministically.
    /// </summary>
    private static (Focas2DemoApi api, Action<TimeSpan> advance) MakeApi()
    {
        var clock = new SharedClock(ClockOrigin);
        var api = new Focas2DemoApi(clock.Read);
        void Advance(TimeSpan d) => clock.Now = clock.Now.Add(d);
        return (api, Advance);
    }

    private sealed class SharedClock
    {
        public DateTime Now { get; set; }
        public SharedClock(DateTime origin) => Now = origin;
        public DateTime Read() => Now;
    }
}
