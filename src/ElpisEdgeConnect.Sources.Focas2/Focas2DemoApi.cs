// ============================================================================
// File: Focas2DemoApi.cs
// Purpose: Synthetic CNC emulator implementing IFocas2Api. Used when
//          EDGECONNECT_FOCAS2_FAKE_MODE=true so Studio / dashboards /
//          MQTT pipelines can be demoed without a real Fanuc CNC or the
//          fwlib*.dll native library installed.
//
// LOCKED behaviour (M.2b.3.1 plan v2 §2 + v3):
//   * Locked G: time-driven state machine via injectable Func<DateTime>.
//   * Locked I: NO P/Invoke, NO reference to Focas2Interop. Only refs to
//     Odb* DTO structs and the Focas2ErrorCode enum (which are separate
//     CLR types declared in Focas2Interop.cs but NOT inside the static
//     class).
//
// Canonical demo profile:
//   * 3-axis machining centre, series 31i-B5, type M, version 1.00
//   * 60s cycle: Reset (10s) → Start (40s, cutting) → Stop (10s)
//   * Axes: 100 mm * sin(2π t/30) with per-axis 120° phase offset
//   * Spindle: 0 in Reset/Stop, ramps 0→3000 rpm linearly during Start
//   * Parts counter: increments at each Start→Stop transition
//   * Tool: cycles T1 / T5 / T9 every 3 cycles
//   * Alarm SV0432: fires for ~5s every 4th cycle
//   * MtLinki diagnostics: static healthy values (servo temps ~35°C,
//     fans OK, batteries OK)
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md §1
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Synthetic FOCAS2 controller emulator. Pure managed C# — no native-DLL
/// dependency. Constructed by <c>Focas2SourceAdapter</c>'s production ctor
/// when <c>Focas2DemoModeOptions.IsEnabled</c> is true.
/// </summary>
internal sealed class Focas2DemoApi : IFocas2Api
{
    // ── Cycle constants (seconds) ─────────────────────────────────────────
    private const double ResetSeconds = 10.0;
    private const double StartSeconds = 40.0;
    private const double StopSeconds = 10.0;
    private const double CycleSeconds = ResetSeconds + StartSeconds + StopSeconds;  // 60s

    // ── Demo profile ──────────────────────────────────────────────────────
    private const int DemoAxisCount = 3;
    private const double MaxSpindleRpm = 3000.0;
    private const int CuttingFeedRateMmPerMin = 1500;
    private const short DemoAlarmNumber = 432;     // SV0432
    private const int PartsCounterParameterNumber = 6711;
    private const double ServoTempCelsius = 35.0;
    private const double ServoTempAmplitude = 3.0;
    private const double ServoTempPeriodSeconds = 90.0;

    // OdbAxisData.Data/Decimal arrays carry MarshalAs(SizeConst = MAX_AXIS).
    // We never P/Invoke through these structs (Locked I), but for symmetry
    // with the real FOCAS2 layout we size the arrays to the same value as
    // Focas2Interop.MAX_AXIS — kept in sync manually because referencing
    // that constant would touch the static class and risk DllImportResolver
    // installation (violating Locked I).
    private const int OdbAxisStructSize = 32;

    private readonly Func<DateTime> _clock;
    private readonly DateTime _startedAt;
    private ushort _nextHandle;

    /// <summary>Production constructor — uses <c>DateTime.UtcNow</c> as the clock.</summary>
    public Focas2DemoApi()
        : this(() => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Test constructor — injectable clock so unit tests can advance state
    /// deterministically without <c>Thread.Sleep</c> (Locked G).
    /// </summary>
    internal Focas2DemoApi(Func<DateTime> clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _startedAt = _clock();
        _nextHandle = 1;
    }

    // ─── Time helpers ─────────────────────────────────────────────────────

    private TimeSpan Elapsed => _clock() - _startedAt;

    private int CurrentCycle => (int)(Elapsed.TotalSeconds / CycleSeconds);

    private double PhaseSeconds => Elapsed.TotalSeconds % CycleSeconds;

    private DemoPhase CurrentPhase
    {
        get
        {
            var p = PhaseSeconds;
            if (p < ResetSeconds) return DemoPhase.Reset;
            if (p < ResetSeconds + StartSeconds) return DemoPhase.Start;
            return DemoPhase.Stop;
        }
    }

    private enum DemoPhase { Reset, Start, Stop }

    private bool AlarmActive
    {
        // Fires for the first ~5s of every 4th cycle.
        get => CurrentCycle % 4 == 3 && PhaseSeconds < 5.0;
    }

    private int CurrentSpindleRpm
    {
        get
        {
            if (CurrentPhase != DemoPhase.Start) return 0;
            var startElapsed = PhaseSeconds - ResetSeconds;
            var ratio = Math.Clamp(startElapsed / StartSeconds, 0.0, 1.0);
            return (int)(MaxSpindleRpm * ratio);
        }
    }

    private int CurrentFeedRate => CurrentPhase == DemoPhase.Start ? CuttingFeedRateMmPerMin : 0;

    private int CurrentToolNumber
    {
        get
        {
            // T1 / T5 / T9 cycle every 3 cycles.
            return (CurrentCycle % 3) switch
            {
                0 => 1,
                1 => 5,
                _ => 9,
            };
        }
    }

    private int CurrentPartsCount
    {
        get
        {
            // Increment at the Start→Stop transition; once in Stop phase we
            // claim N+1 parts produced from cycle N.
            return CurrentPhase == DemoPhase.Stop ? CurrentCycle + 1 : CurrentCycle;
        }
    }

    private double AxisPositionMm(int axisIndex)
    {
        var t = Elapsed.TotalSeconds;
        var phaseOffset = axisIndex * 2.0 * Math.PI / 3.0;
        return 100.0 * Math.Sin(2.0 * Math.PI * t / 30.0 + phaseOffset);
    }

    // ─── Connection ───────────────────────────────────────────────────────

    public short AllocLibHandle(string ipAddress, ushort port, int timeout, out ushort handle)
    {
        handle = unchecked(++_nextHandle);
        if (handle == 0) handle = 1;  // never return 0 (Focas2ConnectionManager treats 0 as invalid)
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short FreeLibHandle(ushort handle) => (short)Focas2ErrorCode.EW_OK;

    /// <summary>Demo mode never blocks, so the data time-out is a no-op.</summary>
    public short SetDataTimeout(ushort handle, int seconds) => (short)Focas2ErrorCode.EW_OK;

    // ─── Status ───────────────────────────────────────────────────────────

    public short ReadStatusInfo(ushort handle, out OdbStatusInfo statusInfo)
    {
        var phase = CurrentPhase;
        statusInfo = new OdbStatusInfo
        {
            Aut = 1,                                       // 1 = MEM
            Run = phase switch
            {
                DemoPhase.Reset => (short)0,               // RESET
                DemoPhase.Start => (short)3,               // START
                _ => (short)1,                             // STOP
            },
            Motion = phase == DemoPhase.Start ? (short)1 : (short)0,
            Emergency = 0,
            Alarm = AlarmActive ? (short)1 : (short)0,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── System info ──────────────────────────────────────────────────────

    public short ReadSystemInfo(ushort handle, out OdbSystemInfo sysInfo)
    {
        sysInfo = new OdbSystemInfo
        {
            // Field MarshalAs sizes are fixed in OdbSystemInfo; we just
            // populate values matching the demo profile (truncated/padded
            // as needed at the managed layer — no native marshalling).
            CncType = "M ",
            MtType = "M   ",
            Series = "31iB",
            Version = "1.00",
            MaxAxis = "32",
            Axes = "3 ",
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadAxisCount(ushort handle, out short axisCount)
    {
        axisCount = DemoAxisCount;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadAxisNames(ushort handle, ref short dataNum, OdbAxisName[] axisNames)
    {
        var names = new[] { "X", "Y", "Z" };
        var written = Math.Min(axisNames.Length, names.Length);
        for (var i = 0; i < written; i++)
        {
            axisNames[i] = new OdbAxisName { Name = names[i], Suffix = "1" };
        }
        dataNum = (short)written;
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── Program ──────────────────────────────────────────────────────────

    public short ReadProgramNumber(ushort handle, out OdbProgramNumber programNum)
    {
        programNum = new OdbProgramNumber
        {
            MainProgram = 1234,
            RunningProgram = 1234,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadProgramDirectory(ushort handle, short type, ref int topProg, ref short numProg, byte[] progDir)
    {
        numProg = 0;
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── Position ─────────────────────────────────────────────────────────

    public short ReadAbsolutePosition(ushort handle, short axisNum, short length, out OdbAxisData position) =>
        FillAxisData(out position);

    public short ReadMachinePosition(ushort handle, short axisNum, short length, out OdbAxisData position) =>
        FillAxisData(out position);

    public short ReadRelativePosition(ushort handle, short axisNum, short length, out OdbAxisData position) =>
        FillAxisData(out position);

    public short ReadDistanceToGo(ushort handle, short axisNum, short length, out OdbAxisData position)
    {
        // Distance-to-go is conceptually different but for the demo we just
        // return half the absolute position — visually distinct enough.
        position = new OdbAxisData
        {
            Type = 0,
            Data = new int[OdbAxisStructSize],
            Decimal = new short[OdbAxisStructSize],
        };
        for (var i = 0; i < DemoAxisCount; i++)
        {
            position.Data[i] = (int)(AxisPositionMm(i) * 500.0);  // half scale, micrometers
            position.Decimal[i] = 3;
        }
        return (short)Focas2ErrorCode.EW_OK;
    }

    private short FillAxisData(out OdbAxisData position)
    {
        position = new OdbAxisData
        {
            Type = 0,
            Data = new int[OdbAxisStructSize],
            Decimal = new short[OdbAxisStructSize],
        };
        for (var i = 0; i < DemoAxisCount; i++)
        {
            // Convert mm → integer micrometers (decimal = 3 means /1000).
            position.Data[i] = (int)(AxisPositionMm(i) * 1000.0);
            position.Decimal[i] = 3;
        }
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── Feed / spindle ───────────────────────────────────────────────────

    public short ReadActualFeedRate(ushort handle, out OdbActualFeed feedRate)
    {
        feedRate = new OdbActualFeed
        {
            Type = 0,
            Data = CurrentFeedRate,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadActualSpindleSpeed(ushort handle, out OdbActualSpeed spindleSpeed)
    {
        spindleSpeed = new OdbActualSpeed
        {
            Type = 0,
            Data = CurrentSpindleRpm,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadSpindleLoad(ushort handle, short spindleNo, out OdbSpindleLoad load)
    {
        var data = new short[4];
        if (CurrentPhase == DemoPhase.Start)
        {
            data[0] = 65;  // 65% spindle load during cutting
        }
        load = new OdbSpindleLoad
        {
            Type = 0,
            Data = data,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── Alarms ───────────────────────────────────────────────────────────

    public short ReadAlarmStatus(ushort handle, out OdbAlarmStatus alarmStatus)
    {
        alarmStatus = new OdbAlarmStatus
        {
            Data = AlarmActive ? (short)0x0080 : (short)0,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadAlarmMessages(ushort handle, short type, ref short num, OdbAlarmMessage[] alarms)
    {
        if (!AlarmActive)
        {
            num = 0;
            return (short)Focas2ErrorCode.EW_OK;
        }
        var written = Math.Min(alarms.Length, 1);
        if (written > 0)
        {
            alarms[0] = new OdbAlarmMessage
            {
                AlarmNo = DemoAlarmNumber,
                Type = 1,    // 1 = SV (servo)
                Axis = 1,    // X axis
                MsgLength = 0,
                AlarmMessage = "SV0432 N CONVERTER:CONTROL POWER",
            };
        }
        num = (short)written;
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── Production ───────────────────────────────────────────────────────

    public short ReadTimer(ushort handle, short type, out OdbTimer timer)
    {
        // Cycle time = time elapsed in the current Start phase.
        var cycleTimeSec = CurrentPhase switch
        {
            DemoPhase.Start => PhaseSeconds - ResetSeconds,
            DemoPhase.Stop => StartSeconds,  // pinned at the end value through Stop
            _ => 0.0,
        };
        var totalMs = (long)(cycleTimeSec * 1000);
        timer = new OdbTimer
        {
            Type = type,
            Minute = (int)(totalMs / 60_000),
            Msec = (int)(totalMs % 60_000),
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadParameter(ushort handle, short paramNo, short axisNo, short length, out OdbParameter param)
    {
        param = new OdbParameter
        {
            DataNo = paramNo,
            Type = 0,
            LData = paramNo == PartsCounterParameterNumber ? CurrentPartsCount : 0,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── Tool ─────────────────────────────────────────────────────────────

    public short ReadModal(ushort handle, short type, short length, byte[] data)
    {
        // Pack the current tool number into the first 4 bytes (little-endian).
        // The collectors that read this will reverse-engineer the T-code from
        // the modal buffer.
        var tool = CurrentToolNumber;
        if (data.Length >= 2)
        {
            data[0] = (byte)(tool & 0xFF);
            data[1] = (byte)((tool >> 8) & 0xFF);
        }
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadMacro(ushort handle, short number, short length, out OdbMacro macro)
    {
        macro = new OdbMacro
        {
            DataNo = number,
            McVal = 0,
            McDig = 0,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolOffsetInfo2(ushort handle, out short ofsType, out short useNo)
    {
        ofsType = 0;   // type A
        useNo = 32;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolOffsetInfo(ushort handle, out short useNo)
    {
        useNo = 32;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolOffset(ushort handle, short number, short type, short length, out OdbToolOffset tofs)
    {
        tofs = new OdbToolOffset
        {
            DataNo = number,
            Type = type,
            Data = 0,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolOffsetRange(ushort handle, short startNo, short type, short endNo, short length, byte[] data) =>
        (short)Focas2ErrorCode.EW_OK;

    public short ReadToolLifeInfo(ushort handle, byte[] data) => (short)Focas2ErrorCode.EW_OK;

    public short ReadToolLifeGroupCount(ushort handle, out short count)
    {
        count = 0;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolLifeGroup(ushort handle, int groupNo, byte[] data) => (short)Focas2ErrorCode.EW_OK;

    public short ReadToolLifeUseGroup(ushort handle, out int groupNo)
    {
        groupNo = 0;
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── PMC (MtLinki signals) ────────────────────────────────────────────

    public short ReadPmcRange(ushort handle, short addrType, short dataType,
        ushort startNo, ushort endNo, ushort length, byte[] pmcData)
    {
        Array.Clear(pmcData, 0, pmcData.Length);
        // Set F4.0 (cutting) and F8.0 (feed) during Start phase so the
        // MtLinkiCollector observes "machine cutting" signals during cycles.
        if (CurrentPhase == DemoPhase.Start && pmcData.Length >= 9)
        {
            pmcData[4] = 0x01;  // F4 bit 0: SP cycle / cutting
            pmcData[8] = 0x01;  // F8 bit 0: feed
        }
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── Diagnostics (MtLinki temps, fans, batteries) ─────────────────────

    public short ReadDiagnosticData(ushort handle, short diagNo, short axisNo, short length, out OdbDiagnosticData diagData)
    {
        // 300 = CNC battery, 301 = servo battery (0 = OK)
        // 308 = servo temperature (°C, integer)
        // 413 = servo insulation resistance (MΩ * 10)
        // Other diag numbers return 0 (healthy default).
        int value = diagNo switch
        {
            300 => 0,  // CNC battery OK
            301 => 0,  // servo battery OK
            308 => (int)Math.Round(ServoTempCelsius + ServoTempAmplitude *
                                    Math.Sin(2.0 * Math.PI * Elapsed.TotalSeconds / ServoTempPeriodSeconds)),
            413 => 1000,  // 100.0 MΩ insulation — healthy
            _ => 0,
        };
        diagData = new OdbDiagnosticData
        {
            DataNo = diagNo,
            Type = 0,
            LData = value,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadDiagnosticDataArray(ushort handle, short diagNo, short axisNo, short length, byte[] diagData)
    {
        Array.Clear(diagData, 0, diagData.Length);
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── Operator messages ────────────────────────────────────────────────

    public short ReadOperatorMessage(ushort handle, short type, short length, byte[] opmsg)
    {
        Array.Clear(opmsg, 0, opmsg.Length);  // no warnings
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ─── Maintenance ──────────────────────────────────────────────────────

    public short ReadSpMaintCheck(ushort handle, short type, byte[] data)
    {
        Array.Clear(data, 0, data.Length);
        return (short)Focas2ErrorCode.EW_OK;
    }
}
