// ============================================================================
// File: Collectors/MtLinkiCollector.cs
// Purpose: Collects ~40 MT-LINKi compatible diagnostic fields including CNC
//          state, PMC signals, fan statuses, battery, servo/spindle temps,
//          and insulation resistance.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2.Collectors;

/// <summary>
/// Collects MT-LINKi compatible diagnostic data: computed CNC state, PMC
/// signals, operator messages, fan statuses, battery status, servo/spindle
/// temperatures, and insulation resistance values.
/// </summary>
internal sealed class MtLinkiCollector(IFocas2Api api, ILogger logger)
{
    /// <summary>
    /// Collect MT-LINKi diagnostic data and append canonical points to <paramref name="points"/>.
    /// </summary>
    internal void Collect(
        ushort handle,
        string? runState,
        string? autoMode,
        bool? emergencyStop,
        int alarmCount,
        long? partsCount,
        string? mainProgram,
        string? runningProgram,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        CollectCncState(runState, emergencyStop, alarmCount, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectMode(autoMode, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectPartsNum(handle, partsCount, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectPmcSignals(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectCncWarning(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectCncFanStatus(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectBattery(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectServoTemps(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectServoInsulation(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectSpindleTemp(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectSpindleInsulation(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectSvFanStatus(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectSpFanStatus(handle, factory, points, deviceTimestamp, gatewayTimestamp);
    }

    private static void CollectCncState(
        string? runState,
        bool? emergencyStop,
        int alarmCount,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Compute MT-LINKi CNC state from run state + emergency + alarm count
        string cncState;
        if (emergencyStop == true)
            cncState = "EMERGENCY";
        else if (alarmCount > 0)
            cncState = "ALARM";
        else if (runState is "Running" or "MSTR")
            cncState = "OPERATE";
        else if (runState is "Hold")
            cncState = "SUSPEND";
        else
            cncState = "STOP";

        var tag = Focas2TagMap.MtLinkiCncState;
        points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, cncState,
            tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
    }

    private static void CollectMode(
        string? autoMode,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        var tag = Focas2TagMap.MtLinkiMode;
        points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, autoMode ?? "UNKNOWN",
            tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
    }

    private void CollectPartsNum(
        ushort handle,
        long? partsCount,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Try resettable parts counter (param 6711) first
        short ret = api.ReadParameter(handle, 6711, 0, 8, out var param);
        ThrowIfFatal(ret, nameof(api.ReadParameter));

        long value;
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            value = param.LData;
        }
        else
        {
            logger.LogDebug("ReadParameter(6711) returned {ErrorCode}, using fallback parts count", (Focas2ErrorCode)ret);
            value = partsCount ?? 0;
        }

        var tag = Focas2TagMap.MtLinkiPartsNum;
        points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, value,
            tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
    }

    private void CollectPmcSignals(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Read PMC F-table addresses 0-7 (8 bytes)
        // Buffer: 10 bytes header + 8 bytes data
        const ushort pmcLength = 18;
        var pmcData = new byte[pmcLength];

        short ret = api.ReadPmcRange(handle, Focas2Interop.PMC_ADDR_F, Focas2Interop.PMC_TYPE_BYTE,
            0, 7, pmcLength, pmcData);
        ThrowIfFatal(ret, nameof(api.ReadPmcRange));
        if (ret != (short)Focas2ErrorCode.EW_OK)
        {
            logger.LogDebug("ReadPmcRange(F0-7) returned {ErrorCode}, skipping PMC signals", (Focas2ErrorCode)ret);
            return;
        }

        // PMC data starts at offset 10 in the buffer (after header)
        const int dataOffset = 10;

        // F0 byte
        byte f0 = pmcData[dataOffset];
        // F2 byte
        byte f2 = pmcData[dataOffset + 2];

        // SigCUT: F2 bit 3
        bool sigCut = (f2 & 0x08) != 0;
        // SigFEED: F0 bit 5 (proxy)
        bool sigFeed = (f0 & 0x20) != 0;
        // SigM: F0 bit 0
        bool sigM = (f0 & 0x01) != 0;
        // SigS: F0 bit 1
        bool sigS = (f0 & 0x02) != 0;
        // SigT: F0 bit 2
        bool sigT = (f0 & 0x04) != 0;

        EmitBoolPoint(Focas2TagMap.MtLinkiSigCut, sigCut, factory, points, deviceTimestamp, gatewayTimestamp);
        EmitBoolPoint(Focas2TagMap.MtLinkiSigFeed, sigFeed, factory, points, deviceTimestamp, gatewayTimestamp);
        EmitBoolPoint(Focas2TagMap.MtLinkiSigM, sigM, factory, points, deviceTimestamp, gatewayTimestamp);
        EmitBoolPoint(Focas2TagMap.MtLinkiSigS, sigS, factory, points, deviceTimestamp, gatewayTimestamp);
        EmitBoolPoint(Focas2TagMap.MtLinkiSigT, sigT, factory, points, deviceTimestamp, gatewayTimestamp);
    }

    private static void EmitBoolPoint(
        TagMapEntry tag,
        bool value,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, value,
            tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
    }

    private void CollectCncWarning(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // ReadOperatorMessage type=0 (warning), buffer 256 bytes
        var msgData = new byte[256];
        short ret = api.ReadOperatorMessage(handle, 0, (short)msgData.Length, msgData);
        ThrowIfFatal(ret, nameof(api.ReadOperatorMessage));
        if (ret != (short)Focas2ErrorCode.EW_OK)
        {
            logger.LogDebug("ReadOperatorMessage returned {ErrorCode}, skipping CNC warning", (Focas2ErrorCode)ret);
            return;
        }

        // Message starts at offset 6 (2 bytes datano + 2 bytes type + 2 bytes char_num)
        string message = "";
        if (msgData.Length > 6)
        {
            short charNum = BitConverter.ToInt16(msgData, 4);
            if (charNum > 0 && charNum + 6 <= msgData.Length)
            {
                message = System.Text.Encoding.ASCII.GetString(msgData, 6, charNum).TrimEnd('\0');
            }
        }

        var tag = Focas2TagMap.MtLinkiCncWarning;
        points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, message,
            tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
    }

    private void CollectCncFanStatus(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // CNC fan diagnostics: 4571-4574
        for (int i = 0; i < 4; i++)
        {
            short diagNo = (short)(4571 + i);
            short ret = api.ReadDiagnosticData(handle, diagNo, 0, 8, out var diag);
            ThrowIfFatal(ret, nameof(api.ReadDiagnosticData));
            if (ret == (short)Focas2ErrorCode.EW_OK)
            {
                string status = MapFanStatus(diag.LData);
                var tag = Focas2TagMap.CncFanStatus(i + 1);
                points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, status,
                    tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
            }
            else
            {
                logger.LogDebug("ReadDiagnosticData({DiagNo}) returned {ErrorCode}", diagNo, (Focas2ErrorCode)ret);
            }
        }
    }

    private void CollectBattery(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // CNC battery: diag 300
        short ret = api.ReadDiagnosticData(handle, 300, 0, 8, out var diag300);
        ThrowIfFatal(ret, nameof(api.ReadDiagnosticData));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            string batteryStatus = diag300.LData == 0 ? "OK" : "Warning";
            var tag = Focas2TagMap.CncBattery;
            points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, batteryStatus,
                tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
        }
        else
        {
            logger.LogDebug("ReadDiagnosticData(300) returned {ErrorCode}", (Focas2ErrorCode)ret);
        }

        // APC/Servo battery: diag 301
        ret = api.ReadDiagnosticData(handle, 301, 0, 8, out var diag301);
        ThrowIfFatal(ret, nameof(api.ReadDiagnosticData));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            string batteryStatus = diag301.LData == 0 ? "OK" : "Warning";
            var tag = Focas2TagMap.ServoBattery;
            points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, batteryStatus,
                tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
        }
        else
        {
            logger.LogDebug("ReadDiagnosticData(301) returned {ErrorCode}", (Focas2ErrorCode)ret);
        }
    }

    private void CollectServoTemps(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Diag 308 per axis: servo temperature
        for (int i = 0; i < 2; i++)
        {
            short axisNo = (short)(i + 1);
            short ret = api.ReadDiagnosticData(handle, 308, axisNo, 8, out var diag);
            ThrowIfFatal(ret, nameof(api.ReadDiagnosticData));
            if (ret == (short)Focas2ErrorCode.EW_OK)
            {
                var tag = Focas2TagMap.ServoTemp(i);
                points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, diag.LData,
                    tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp,
                    unit: tag.Unit));
            }
            else
            {
                logger.LogDebug("ReadDiagnosticData(308, axis={Axis}) returned {ErrorCode}", axisNo, (Focas2ErrorCode)ret);
            }
        }
    }

    private void CollectServoInsulation(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Diag 413 per axis: servo insulation resistance
        for (int i = 0; i < 2; i++)
        {
            short axisNo = (short)(i + 1);
            short ret = api.ReadDiagnosticData(handle, 413, axisNo, 8, out var diag);
            ThrowIfFatal(ret, nameof(api.ReadDiagnosticData));
            if (ret == (short)Focas2ErrorCode.EW_OK)
            {
                var tag = Focas2TagMap.ServoInsulation(i);
                points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, diag.LData,
                    tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
            }
            else
            {
                logger.LogDebug("ReadDiagnosticData(413, axis={Axis}) returned {ErrorCode}", axisNo, (Focas2ErrorCode)ret);
            }
        }
    }

    private void CollectSpindleTemp(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Diag 403: spindle temperature
        short ret = api.ReadDiagnosticData(handle, 403, 0, 8, out var diag);
        ThrowIfFatal(ret, nameof(api.ReadDiagnosticData));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            var tag = Focas2TagMap.SpindleTemp;
            points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, diag.LData,
                tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp,
                unit: tag.Unit));
        }
        else
        {
            logger.LogDebug("ReadDiagnosticData(403) returned {ErrorCode}", (Focas2ErrorCode)ret);
        }
    }

    private void CollectSpindleInsulation(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Diag 410: spindle insulation resistance
        short ret = api.ReadDiagnosticData(handle, 410, 0, 8, out var diag);
        ThrowIfFatal(ret, nameof(api.ReadDiagnosticData));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            var tag = Focas2TagMap.SpindleInsulation;
            points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, diag.LData,
                tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
        }
        else
        {
            logger.LogDebug("ReadDiagnosticData(410) returned {ErrorCode}", (Focas2ErrorCode)ret);
        }
    }

    private void CollectSvFanStatus(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Servo amplifier fans: diag 4510-4517, report as 4 fans (every other)
        for (int i = 0; i < 4; i++)
        {
            short diagNo = (short)(4510 + i);
            short ret = api.ReadDiagnosticData(handle, diagNo, 0, 8, out var diag);
            ThrowIfFatal(ret, nameof(api.ReadDiagnosticData));
            if (ret == (short)Focas2ErrorCode.EW_OK)
            {
                string status = MapFanStatus(diag.LData);
                var tag = Focas2TagMap.SvFanStatus(i + 1);
                points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, status,
                    tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
            }
            else
            {
                logger.LogDebug("ReadDiagnosticData({DiagNo}) returned {ErrorCode}", diagNo, (Focas2ErrorCode)ret);
            }
        }
    }

    private void CollectSpFanStatus(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Spindle amplifier fans: diag 4520-4527, report as 4 fans
        for (int i = 0; i < 4; i++)
        {
            short diagNo = (short)(4520 + i);
            short ret = api.ReadDiagnosticData(handle, diagNo, 0, 8, out var diag);
            ThrowIfFatal(ret, nameof(api.ReadDiagnosticData));
            if (ret == (short)Focas2ErrorCode.EW_OK)
            {
                string status = MapFanStatus(diag.LData);
                var tag = Focas2TagMap.SpFanStatus(i + 1);
                points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, status,
                    tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
            }
            else
            {
                logger.LogDebug("ReadDiagnosticData({DiagNo}) returned {ErrorCode}", diagNo, (Focas2ErrorCode)ret);
            }
        }
    }

    private static string MapFanStatus(int value) => value switch
    {
        0 => "NORMAL",
        1 => "SLOW",
        2 => "STOP",
        _ => $"UNKNOWN({value})",
    };

    private static void ThrowIfFatal(short ret, string function)
    {
        var code = (Focas2ErrorCode)ret;
        if (code is Focas2ErrorCode.EW_SOCKET or Focas2ErrorCode.EW_HANDLE)
        {
            throw new Focas2FatalException(code, function);
        }
    }
}
