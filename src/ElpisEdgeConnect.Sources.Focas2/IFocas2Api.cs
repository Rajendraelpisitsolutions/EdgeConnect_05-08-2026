// ============================================================================
// File: IFocas2Api.cs
// Purpose: Abstraction over all FOCAS2 P/Invoke calls. Enables unit testing
//          with FakeFocas2Api without requiring the native Fwlib64.dll.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Interface wrapping every FOCAS2 P/Invoke call used by the adapter.
/// Production code uses <see cref="Focas2NativeApi"/>; tests inject a fake.
/// All methods return the FOCAS2 error code as a <see langword="short"/>.
/// </summary>
internal interface IFocas2Api
{
    // ---- Connection ----

    /// <summary>Open a connection to a CNC controller.</summary>
    short AllocLibHandle(string ipAddress, ushort port, int timeout, out ushort handle);

    /// <summary>Close a CNC connection and free the handle.</summary>
    short FreeLibHandle(ushort handle);

    /// <summary>
    /// Set the per-handle data-window read time-out, in seconds, bounding how
    /// long an individual read on this handle may wait. Operational mitigation
    /// for the silent-stall class (incident 2026-06-24).
    /// </summary>
    short SetDataTimeout(ushort handle, int seconds);

    // ---- Status ----

    /// <summary>Read CNC status information.</summary>
    short ReadStatusInfo(ushort handle, out OdbStatusInfo statusInfo);

    // ---- System ----

    /// <summary>Read CNC system information.</summary>
    short ReadSystemInfo(ushort handle, out OdbSystemInfo sysInfo);

    /// <summary>Read number of controlled axes.</summary>
    short ReadAxisCount(ushort handle, out short axisCount);

    /// <summary>Read axis names.</summary>
    short ReadAxisNames(ushort handle, ref short dataNum, OdbAxisName[] axisNames);

    // ---- Program ----

    /// <summary>Read current program number (main + running).</summary>
    short ReadProgramNumber(ushort handle, out OdbProgramNumber programNum);

    /// <summary>Read program directory (number, comment, size).</summary>
    short ReadProgramDirectory(ushort handle, short type, ref int topProg, ref short numProg, byte[] progDir);

    // ---- Position ----

    /// <summary>Read absolute position of axes.</summary>
    short ReadAbsolutePosition(ushort handle, short axisNum, short length, out OdbAxisData position);

    /// <summary>Read machine coordinate position of axes.</summary>
    short ReadMachinePosition(ushort handle, short axisNum, short length, out OdbAxisData position);

    /// <summary>Read relative position of axes.</summary>
    short ReadRelativePosition(ushort handle, short axisNum, short length, out OdbAxisData position);

    /// <summary>Read distance-to-go for axes.</summary>
    short ReadDistanceToGo(ushort handle, short axisNum, short length, out OdbAxisData position);

    // ---- Feed / Spindle ----

    /// <summary>Read actual feed rate.</summary>
    short ReadActualFeedRate(ushort handle, out OdbActualFeed feedRate);

    /// <summary>Read actual spindle speed.</summary>
    short ReadActualSpindleSpeed(ushort handle, out OdbActualSpeed spindleSpeed);

    /// <summary>Read spindle load.</summary>
    short ReadSpindleLoad(ushort handle, short spindleNo, out OdbSpindleLoad load);

    // ---- Alarms ----

    /// <summary>Read alarm status summary bitmask.</summary>
    short ReadAlarmStatus(ushort handle, out OdbAlarmStatus alarmStatus);

    /// <summary>Read active alarm messages.</summary>
    short ReadAlarmMessages(ushort handle, short type, ref short num, OdbAlarmMessage[] alarms);

    // ---- Production ----

    /// <summary>Read timer data (cycle time, etc.).</summary>
    short ReadTimer(ushort handle, short type, out OdbTimer timer);

    /// <summary>Read CNC parameter (parts counter, etc.).</summary>
    short ReadParameter(ushort handle, short paramNo, short axisNo, short length, out OdbParameter param);

    // ---- Tool ----

    /// <summary>Read CNC modal data (G/M/T/S/B codes).</summary>
    short ReadModal(ushort handle, short type, short length, byte[] data);

    /// <summary>Read a CNC macro variable.</summary>
    short ReadMacro(ushort handle, short number, short length, out OdbMacro macro);

    /// <summary>Read tool offset info (memory type + count).</summary>
    short ReadToolOffsetInfo2(ushort handle, out short ofsType, out short useNo);

    /// <summary>Read tool offset info (count only, fallback).</summary>
    short ReadToolOffsetInfo(ushort handle, out short useNo);

    /// <summary>Read a single tool offset value.</summary>
    short ReadToolOffset(ushort handle, short number, short type, short length, out OdbToolOffset tofs);

    /// <summary>Read a range of tool offsets.</summary>
    short ReadToolOffsetRange(ushort handle, short startNo, short type, short endNo, short length, byte[] data);

    /// <summary>Read tool life management configuration.</summary>
    short ReadToolLifeInfo(ushort handle, byte[] data);

    /// <summary>Read number of registered tool life groups.</summary>
    short ReadToolLifeGroupCount(ushort handle, out short count);

    /// <summary>Read tool life group data.</summary>
    short ReadToolLifeGroup(ushort handle, int groupNo, byte[] data);

    /// <summary>Read currently used tool life group number.</summary>
    short ReadToolLifeUseGroup(ushort handle, out int groupNo);

    // ---- PMC ----

    /// <summary>Read PMC data from a range of addresses.</summary>
    short ReadPmcRange(ushort handle, short addrType, short dataType,
        ushort startNo, ushort endNo, ushort length, byte[] pmcData);

    // ---- Diagnostics ----

    /// <summary>Read CNC diagnostic data (single axis/non-axis).</summary>
    short ReadDiagnosticData(ushort handle, short diagNo, short axisNo, short length, out OdbDiagnosticData diagData);

    /// <summary>Read CNC diagnostic data for all axes (array form).</summary>
    short ReadDiagnosticDataArray(ushort handle, short diagNo, short axisNo, short length, byte[] diagData);

    // ---- Operator Messages ----

    /// <summary>Read CNC operator messages.</summary>
    short ReadOperatorMessage(ushort handle, short type, short length, byte[] opmsg);

    // ---- Maintenance ----

    /// <summary>Read SP maintenance check data.</summary>
    short ReadSpMaintCheck(ushort handle, short type, byte[] data);
}
