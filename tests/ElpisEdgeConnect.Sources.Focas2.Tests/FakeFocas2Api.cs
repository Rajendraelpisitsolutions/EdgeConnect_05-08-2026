// ============================================================================
// File: FakeFocas2Api.cs
// Purpose: Configurable fake implementing IFocas2Api for unit testing without
//          requiring the native Fwlib64.dll.
// ============================================================================

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

/// <summary>
/// Configurable fake implementing <see cref="IFocas2Api"/> for unit testing.
/// Default behaviour: all methods return EW_OK (0) with default out values.
/// Override properties or Func fields to configure specific scenarios.
/// </summary>
internal sealed class FakeFocas2Api : IFocas2Api
{
    // ---- Configurable return codes ----
    public short AllocResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short FreeResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short StatusInfoResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short SystemInfoResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short AxisCountResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short AxisNamesResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short ProgramNumberResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short AbsolutePositionResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short MachinePositionResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short RelativePositionResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short DistanceToGoResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short FeedRateResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short SpindleSpeedResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short SpindleLoadResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short AlarmStatusResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short AlarmMessagesResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short TimerResult { get; set; } = (short)Focas2ErrorCode.EW_OK;
    public short ParameterResult { get; set; } = (short)Focas2ErrorCode.EW_OK;

    // ---- Configurable out data ----
    public OdbStatusInfo StatusInfo { get; set; }
    public OdbSystemInfo SystemInfo { get; set; }
    public short AxisCount { get; set; } = 3;
    public OdbAxisName[] AxisNames { get; set; } = CreateDefaultAxisNames();
    public OdbProgramNumber ProgramNumber { get; set; }
    public OdbAxisData AbsolutePosition { get; set; } = CreateDefaultAxisData();
    public OdbAxisData MachinePosition { get; set; } = CreateDefaultAxisData();
    public OdbAxisData RelativePosition { get; set; } = CreateDefaultAxisData();
    public OdbAxisData DistanceToGo { get; set; } = CreateDefaultAxisData();
    public OdbActualFeed ActualFeed { get; set; }
    public OdbActualSpeed ActualSpeed { get; set; }
    public OdbSpindleLoad SpindleLoadData { get; set; } = new() { Data = new short[4] };
    public OdbAlarmStatus AlarmStatusData { get; set; }
    public OdbAlarmMessage[] AlarmMessages { get; set; } = new OdbAlarmMessage[10];
    public short AlarmMessageCount { get; set; }
    public OdbTimer TimerData { get; set; }
    public OdbParameter ParameterData { get; set; }

    // ---- Func overrides for custom behavior ----
    public Func<ushort, (short ret, OdbStatusInfo info)>? ReadStatusInfoFunc { get; set; }
    public Func<string, ushort, int, (short ret, ushort handle)>? AllocLibHandleFunc { get; set; }

    // ---- Call tracking ----
    public int AllocCallCount { get; private set; }
    public int FreeCallCount { get; private set; }
    public int StatusCallCount { get; private set; }
    public ushort LastFreedHandle { get; private set; }
    public int SetDataTimeoutCallCount { get; private set; }
    public int LastDataTimeoutSeconds { get; private set; } = -1;

    /// <summary>Return code for <see cref="SetDataTimeout"/>; defaults to EW_OK.</summary>
    public short SetDataTimeoutResult { get; set; } = (short)Focas2ErrorCode.EW_OK;

    /// <summary>When set, <see cref="SetDataTimeout"/> throws this (simulates a
    /// P/Invoke failure such as a missing native entry point).</summary>
    public System.Exception? SetDataTimeoutException { get; set; }

    // ---- Handle counter ----
    private ushort _nextHandle = 1;

    // ---- IFocas2Api implementation ----

    public short AllocLibHandle(string ipAddress, ushort port, int timeout, out ushort handle)
    {
        AllocCallCount++;

        if (AllocLibHandleFunc != null)
        {
            var (ret, h) = AllocLibHandleFunc(ipAddress, port, timeout);
            handle = h;
            return ret;
        }

        handle = _nextHandle++;
        return AllocResult;
    }

    public short FreeLibHandle(ushort handle)
    {
        FreeCallCount++;
        LastFreedHandle = handle;
        return FreeResult;
    }

    public short SetDataTimeout(ushort handle, int seconds)
    {
        SetDataTimeoutCallCount++;
        LastDataTimeoutSeconds = seconds;
        if (SetDataTimeoutException != null)
        {
            throw SetDataTimeoutException;
        }
        return SetDataTimeoutResult;
    }

    public short ReadStatusInfo(ushort handle, out OdbStatusInfo statusInfo)
    {
        StatusCallCount++;

        if (ReadStatusInfoFunc != null)
        {
            var (ret, info) = ReadStatusInfoFunc(handle);
            statusInfo = info;
            return ret;
        }

        statusInfo = StatusInfo;
        return StatusInfoResult;
    }

    public short ReadSystemInfo(ushort handle, out OdbSystemInfo sysInfo)
    {
        sysInfo = SystemInfo;
        return SystemInfoResult;
    }

    public short ReadAxisCount(ushort handle, out short axisCount)
    {
        axisCount = AxisCount;
        return AxisCountResult;
    }

    public short ReadAxisNames(ushort handle, ref short dataNum, OdbAxisName[] axisNames)
    {
        var count = Math.Min(dataNum, (short)AxisNames.Length);
        for (int i = 0; i < count; i++)
        {
            axisNames[i] = AxisNames[i];
        }
        dataNum = count;
        return AxisNamesResult;
    }

    public short ReadProgramNumber(ushort handle, out OdbProgramNumber programNum)
    {
        programNum = ProgramNumber;
        return ProgramNumberResult;
    }

    public short ReadProgramDirectory(ushort handle, short type, ref int topProg, ref short numProg, byte[] progDir)
    {
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadAbsolutePosition(ushort handle, short axisNum, short length, out OdbAxisData position)
    {
        position = AbsolutePosition;
        return AbsolutePositionResult;
    }

    public short ReadMachinePosition(ushort handle, short axisNum, short length, out OdbAxisData position)
    {
        position = MachinePosition;
        return MachinePositionResult;
    }

    public short ReadRelativePosition(ushort handle, short axisNum, short length, out OdbAxisData position)
    {
        position = RelativePosition;
        return RelativePositionResult;
    }

    public short ReadDistanceToGo(ushort handle, short axisNum, short length, out OdbAxisData position)
    {
        position = DistanceToGo;
        return DistanceToGoResult;
    }

    public short ReadActualFeedRate(ushort handle, out OdbActualFeed feedRate)
    {
        feedRate = ActualFeed;
        return FeedRateResult;
    }

    public short ReadActualSpindleSpeed(ushort handle, out OdbActualSpeed spindleSpeed)
    {
        spindleSpeed = ActualSpeed;
        return SpindleSpeedResult;
    }

    public short ReadSpindleLoad(ushort handle, short spindleNo, out OdbSpindleLoad load)
    {
        load = SpindleLoadData;
        return SpindleLoadResult;
    }

    public short ReadAlarmStatus(ushort handle, out OdbAlarmStatus alarmStatus)
    {
        alarmStatus = AlarmStatusData;
        return AlarmStatusResult;
    }

    public short ReadAlarmMessages(ushort handle, short type, ref short num, OdbAlarmMessage[] alarms)
    {
        var count = Math.Min(num, AlarmMessageCount);
        for (int i = 0; i < count; i++)
        {
            alarms[i] = AlarmMessages[i];
        }
        num = count;
        return AlarmMessagesResult;
    }

    public short ReadTimer(ushort handle, short type, out OdbTimer timer)
    {
        timer = TimerData;
        return TimerResult;
    }

    public short ReadParameter(ushort handle, short paramNo, short axisNo, short length, out OdbParameter param)
    {
        param = ParameterData;
        return ParameterResult;
    }

    // ---- Configurable tool data (defaults preserve prior empty behaviour) ----

    /// <summary>Active T-code returned via ReadModal (packed at byte offset 4). 0 = none.</summary>
    public int CurrentToolNumber { get; set; }

    /// <summary>Offset memory type: 0=A, 1=B, 2=C.</summary>
    public short ToolOffsetType { get; set; }

    /// <summary>Number of tool offset sets reported by ReadToolOffsetInfo2/Info.</summary>
    public short ToolOffsetCount { get; set; }

    /// <summary>
    /// Raw offset values keyed by (toolNumber, focasType). focasType matches the
    /// cnc_rdtofs sub-type index (A:0; B:0,1; C:0,1,2,3). Served via ReadToolOffsetRange.
    /// </summary>
    public Dictionary<(int tool, short type), int> ToolOffsetValues { get; } = new();

    /// <summary>When true, ReadToolOffsetRange fails so the collector falls back to individual reads.</summary>
    public bool FailOffsetRange { get; set; }

    /// <summary>Tool-life info: max groups (&gt;0 enables) and count type (1=Minutes else Count).</summary>
    public short ToolLifeMaxGroups { get; set; }
    public short ToolLifeCountType { get; set; }
    public int ToolLifeActiveGroup { get; set; }

    /// <summary>Per-group life data keyed by group number: (toolNo, lifeCount, lifeLimit).</summary>
    public Dictionary<int, (int toolNo, int count, int limit)> ToolLifeGroups { get; } = new();

    public short ReadModal(ushort handle, short type, short length, byte[] data)
    {
        if (type == 108 && data.Length >= 8 && CurrentToolNumber > 0)
        {
            var bytes = BitConverter.GetBytes(CurrentToolNumber);
            Array.Copy(bytes, 0, data, 4, 4);
        }
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadMacro(ushort handle, short number, short length, out OdbMacro macro)
    {
        macro = default;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolOffsetInfo2(ushort handle, out short ofsType, out short useNo)
    {
        ofsType = ToolOffsetType;
        useNo = ToolOffsetCount;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolOffsetInfo(ushort handle, out short useNo)
    {
        useNo = ToolOffsetCount;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolOffset(ushort handle, short number, short type, short length, out OdbToolOffset tofs)
    {
        tofs = new OdbToolOffset
        {
            DataNo = number,
            Type = type,
            Data = ToolOffsetValues.TryGetValue((number, type), out var v) ? v : 0,
        };
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolOffsetRange(ushort handle, short startNo, short type, short endNo, short length, byte[] data)
    {
        if (FailOffsetRange)
            return (short)Focas2ErrorCode.EW_FUNC;

        // ODBTOFS range layout: 6-byte header then a 4-byte long per value.
        for (int tool = startNo; tool <= endNo; tool++)
        {
            int idx = 6 + (tool - startNo) * 4;
            if (idx + 4 > data.Length) break;
            int raw = ToolOffsetValues.TryGetValue((tool, type), out var v) ? v : 0;
            Array.Copy(BitConverter.GetBytes(raw), 0, data, idx, 4);
        }
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolLifeInfo(ushort handle, byte[] data)
    {
        if (data.Length >= 4)
        {
            Array.Copy(BitConverter.GetBytes(ToolLifeMaxGroups), 0, data, 0, 2);
            Array.Copy(BitConverter.GetBytes(ToolLifeCountType), 0, data, 2, 2);
        }
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolLifeGroupCount(ushort handle, out short count)
    {
        count = (short)ToolLifeGroups.Count;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolLifeGroup(ushort handle, int groupNo, byte[] data)
    {
        if (ToolLifeGroups.TryGetValue(groupNo, out var g) && data.Length >= 16)
        {
            Array.Copy(BitConverter.GetBytes(groupNo), 0, data, 0, 4);
            Array.Copy(BitConverter.GetBytes(g.toolNo), 0, data, 4, 4);
            Array.Copy(BitConverter.GetBytes(g.count), 0, data, 8, 4);
            Array.Copy(BitConverter.GetBytes(g.limit), 0, data, 12, 4);
        }
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadToolLifeUseGroup(ushort handle, out int groupNo)
    {
        groupNo = ToolLifeActiveGroup;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadPmcRange(ushort handle, short addrType, short dataType,
        ushort startNo, ushort endNo, ushort length, byte[] pmcData)
    {
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadDiagnosticData(ushort handle, short diagNo, short axisNo, short length, out OdbDiagnosticData diagData)
    {
        diagData = default;
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadDiagnosticDataArray(ushort handle, short diagNo, short axisNo, short length, byte[] diagData)
    {
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadOperatorMessage(ushort handle, short type, short length, byte[] opmsg)
    {
        return (short)Focas2ErrorCode.EW_OK;
    }

    public short ReadSpMaintCheck(ushort handle, short type, byte[] data)
    {
        return (short)Focas2ErrorCode.EW_OK;
    }

    // ---- Helpers ----

    private static OdbAxisName[] CreateDefaultAxisNames() =>
    [
        new OdbAxisName { Name = "X", Suffix = "" },
        new OdbAxisName { Name = "Y", Suffix = "" },
        new OdbAxisName { Name = "Z", Suffix = "" },
    ];

    private static OdbAxisData CreateDefaultAxisData() => new()
    {
        Data = new int[Focas2Interop.MAX_AXIS],
        Decimal = new short[Focas2Interop.MAX_AXIS],
    };
}
