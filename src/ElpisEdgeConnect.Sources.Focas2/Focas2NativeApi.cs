// ============================================================================
// File: Focas2NativeApi.cs
// Purpose: Production IFocas2Api implementation that delegates to the static
//          Focas2Interop P/Invoke declarations. Used by the adapter at runtime.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Production <see cref="IFocas2Api"/> that delegates every call to the
/// static <see cref="Focas2Interop"/> P/Invoke layer.
/// </summary>
internal sealed class Focas2NativeApi : IFocas2Api
{
    public short AllocLibHandle(string ipAddress, ushort port, int timeout, out ushort handle)
        => Focas2Interop.AllocLibHandle(ipAddress, port, timeout, out handle);

    public short FreeLibHandle(ushort handle)
        => Focas2Interop.FreeLibHandle(handle);

    public short SetDataTimeout(ushort handle, int seconds)
        => Focas2Interop.SetDataTimeout(handle, seconds);

    public short ReadStatusInfo(ushort handle, out OdbStatusInfo statusInfo)
        => Focas2Interop.ReadStatusInfo(handle, out statusInfo);

    public short ReadSystemInfo(ushort handle, out OdbSystemInfo sysInfo)
        => Focas2Interop.ReadSystemInfo(handle, out sysInfo);

    public short ReadAxisCount(ushort handle, out short axisCount)
        => Focas2Interop.ReadAxisCount(handle, out axisCount);

    public short ReadAxisNames(ushort handle, ref short dataNum, OdbAxisName[] axisNames)
        => Focas2Interop.ReadAxisNames(handle, ref dataNum, axisNames);

    public short ReadProgramNumber(ushort handle, out OdbProgramNumber programNum)
        => Focas2Interop.ReadProgramNumber(handle, out programNum);

    public short ReadProgramDirectory(ushort handle, short type, ref int topProg, ref short numProg, byte[] progDir)
        => Focas2Interop.ReadProgramDirectory(handle, type, ref topProg, ref numProg, progDir);

    public short ReadAbsolutePosition(ushort handle, short axisNum, short length, out OdbAxisData position)
        => Focas2Interop.ReadAbsolutePosition(handle, axisNum, length, out position);

    public short ReadMachinePosition(ushort handle, short axisNum, short length, out OdbAxisData position)
        => Focas2Interop.ReadMachinePosition(handle, axisNum, length, out position);

    public short ReadRelativePosition(ushort handle, short axisNum, short length, out OdbAxisData position)
        => Focas2Interop.ReadRelativePosition(handle, axisNum, length, out position);

    public short ReadDistanceToGo(ushort handle, short axisNum, short length, out OdbAxisData position)
        => Focas2Interop.ReadDistanceToGo(handle, axisNum, length, out position);

    public short ReadActualFeedRate(ushort handle, out OdbActualFeed feedRate)
        => Focas2Interop.ReadActualFeedRate(handle, out feedRate);

    public short ReadActualSpindleSpeed(ushort handle, out OdbActualSpeed spindleSpeed)
        => Focas2Interop.ReadActualSpindleSpeed(handle, out spindleSpeed);

    public short ReadSpindleLoad(ushort handle, short spindleNo, out OdbSpindleLoad load)
        => Focas2Interop.ReadSpindleLoad(handle, spindleNo, out load);

    public short ReadAlarmStatus(ushort handle, out OdbAlarmStatus alarmStatus)
        => Focas2Interop.ReadAlarmStatus(handle, out alarmStatus);

    public short ReadAlarmMessages(ushort handle, short type, ref short num, OdbAlarmMessage[] alarms)
        => Focas2Interop.ReadAlarmMessages(handle, type, ref num, alarms);

    public short ReadTimer(ushort handle, short type, out OdbTimer timer)
        => Focas2Interop.ReadTimer(handle, type, out timer);

    public short ReadParameter(ushort handle, short paramNo, short axisNo, short length, out OdbParameter param)
        => Focas2Interop.ReadParameter(handle, paramNo, axisNo, length, out param);

    public short ReadModal(ushort handle, short type, short length, byte[] data)
        => Focas2Interop.ReadModal(handle, type, length, data);

    public short ReadMacro(ushort handle, short number, short length, out OdbMacro macro)
        => Focas2Interop.ReadMacro(handle, number, length, out macro);

    public short ReadToolOffsetInfo2(ushort handle, out short ofsType, out short useNo)
        => Focas2Interop.ReadToolOffsetInfo2(handle, out ofsType, out useNo);

    public short ReadToolOffsetInfo(ushort handle, out short useNo)
        => Focas2Interop.ReadToolOffsetInfo(handle, out useNo);

    public short ReadToolOffset(ushort handle, short number, short type, short length, out OdbToolOffset tofs)
        => Focas2Interop.ReadToolOffset(handle, number, type, length, out tofs);

    public short ReadToolOffsetRange(ushort handle, short startNo, short type, short endNo, short length, byte[] data)
        => Focas2Interop.ReadToolOffsetRange(handle, startNo, type, endNo, length, data);

    public short ReadToolLifeInfo(ushort handle, byte[] data)
        => Focas2Interop.ReadToolLifeInfo(handle, data);

    public short ReadToolLifeGroupCount(ushort handle, out short count)
        => Focas2Interop.ReadToolLifeGroupCount(handle, out count);

    public short ReadToolLifeGroup(ushort handle, int groupNo, byte[] data)
        => Focas2Interop.ReadToolLifeGroup(handle, groupNo, data);

    public short ReadToolLifeUseGroup(ushort handle, out int groupNo)
        => Focas2Interop.ReadToolLifeUseGroup(handle, out groupNo);

    public short ReadPmcRange(ushort handle, short addrType, short dataType,
        ushort startNo, ushort endNo, ushort length, byte[] pmcData)
        => Focas2Interop.ReadPmcRange(handle, addrType, dataType, startNo, endNo, length, pmcData);

    public short ReadDiagnosticData(ushort handle, short diagNo, short axisNo, short length, out OdbDiagnosticData diagData)
        => Focas2Interop.ReadDiagnosticData(handle, diagNo, axisNo, length, out diagData);

    public short ReadDiagnosticDataArray(ushort handle, short diagNo, short axisNo, short length, byte[] diagData)
        => Focas2Interop.ReadDiagnosticDataArray(handle, diagNo, axisNo, length, diagData);

    public short ReadOperatorMessage(ushort handle, short type, short length, byte[] opmsg)
        => Focas2Interop.ReadOperatorMessage(handle, type, length, opmsg);

    public short ReadSpMaintCheck(ushort handle, short type, byte[] data)
        => Focas2Interop.ReadSpMaintCheck(handle, type, data);
}
