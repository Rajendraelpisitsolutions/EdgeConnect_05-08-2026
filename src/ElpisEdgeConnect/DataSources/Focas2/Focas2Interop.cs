// ============================================================================
// File: DataSources/Focas2/Focas2Interop.cs
// Purpose: P/Invoke declarations for the Fanuc FOCAS2 native library.
//          This provides direct access to CNC controller data via Ethernet.
//
// FOCAS2 LIBRARY:
//   - Windows 64-bit: Fwlib64.dll (or Fwlib32.dll for 32-bit)
//   - Linux:          libfwlib32.so
//   - Provided by Fanuc — NOT open-source. Must be obtained from Fanuc.
//
// FUNCTION NAMING CONVENTION:
//   All FOCAS2 functions follow the pattern: cnc_{function_name}
//   - cnc_allclibhndl3: Open connection (allocate library handle)
//   - cnc_freelibhndl:  Close connection (free library handle)
//   - cnc_rdprgnum:     Read program number
//   - cnc_statinfo:     Read CNC status
//   - cnc_absolute:     Read absolute position
//   - cnc_acts:         Read actual spindle speed
//   etc.
//
// ERROR HANDLING:
//   All functions return a short (Int16) error code:
//     0 = EW_OK (success)
//   < 0 = Severe error (connection lost, invalid handle)
//   > 0 = Non-fatal error (wrong mode, parameter error)
//   See Focas2ErrorCode enum for common codes.
//
// HANDLE MANAGEMENT:
//   cnc_allclibhndl3() returns a handle (ushort) that must be passed to
//   every subsequent call. Call cnc_freelibhndl() to release the handle.
//   Max simultaneous handles depends on CNC model (typically 10-16).
// ============================================================================

using System.Runtime.InteropServices;
using System.Text;

namespace ElpisEdgeConnect.DataSources.Focas2;

/// <summary>
/// P/Invoke declarations for Fanuc FOCAS2 native library functions.
/// 
/// PLATFORM DETECTION:
///   The DLL name is resolved at runtime based on the operating system.
///   On Windows, it loads "Fwlib64.dll" (or the path from Focas2Settings.DllPath).
///   On Linux, it loads "libfwlib32.so".
/// 
/// THREAD SAFETY:
///   FOCAS2 functions are NOT thread-safe per handle. Each thread must use
///   its own handle, or calls must be serialized with a lock.
///   Since each machine has its own Focas2DllDataSource, this is handled naturally.
/// </summary>
internal static class Focas2Interop
{
    // DLL name constant — resolved by the runtime's native library loader.
    // On Windows: searches Fwlib64.dll in app dir, PATH, system32
    // On Linux: searches libfwlib32.so in LD_LIBRARY_PATH, /usr/lib, etc.
    internal const string FOCAS_DLL = "Fwlib64";

    // =========================================================================
    // CONNECTION MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Open a connection to a CNC controller via Ethernet.
    /// 
    /// Parameters:
    ///   ipAddress — CNC controller IP (e.g., "192.168.1.101")
    ///   port      — FOCAS2 port (typically 8193)
    ///   timeout   — Connection timeout in seconds
    ///   handle    — [out] Connection handle for subsequent API calls
    /// 
    /// Returns: 0 (EW_OK) on success, error code on failure.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_allclibhndl3")]
    internal static extern short AllocLibHandle(
        [MarshalAs(UnmanagedType.LPStr)] string ipAddress,
        ushort port,
        int timeout,
        out ushort handle);

    /// <summary>
    /// Close a CNC connection and free the library handle.
    /// Must be called for every successful AllocLibHandle call.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_freelibhndl")]
    internal static extern short FreeLibHandle(ushort handle);

    // =========================================================================
    // PROGRAM INFORMATION
    // =========================================================================

    /// <summary>
    /// Read the current program number (main program and running program).
    /// 
    /// Output structure (ODBPRO):
    ///   dummy [0-1]  — Not used
    ///   data  [0]    — Running program number (O number)
    ///   mdata [0]    — Main program number
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdprgnum")]
    internal static extern short ReadProgramNumber(ushort handle, out OdbProgramNumber programNum);

    /// <summary>
    /// Read CNC status information (run state, motion, alarm, etc.).
    /// Returns a comprehensive status structure.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_statinfo")]
    internal static extern short ReadStatusInfo(ushort handle, out OdbStatusInfo statusInfo);

    // =========================================================================
    // AXIS POSITION DATA
    // =========================================================================

    /// <summary>
    /// Read absolute position of all axes.
    /// 
    /// Parameters:
    ///   handle   — Connection handle
    ///   axisNum  — Axis number (1-based) or ALL_AXES (-1) for all axes
    ///   length   — Size of output buffer
    ///   position — [out] Position data
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_absolute")]
    internal static extern short ReadAbsolutePosition(
        ushort handle, short axisNum, short length, out OdbAxisData position);

    /// <summary>
    /// Read machine coordinate position of all axes.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_machine")]
    internal static extern short ReadMachinePosition(
        ushort handle, short axisNum, short length, out OdbAxisData position);

    /// <summary>
    /// Read relative position of all axes.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_relative")]
    internal static extern short ReadRelativePosition(
        ushort handle, short axisNum, short length, out OdbAxisData position);

    /// <summary>
    /// Read distance-to-go for all axes.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_distance")]
    internal static extern short ReadDistanceToGo(
        ushort handle, short axisNum, short length, out OdbAxisData position);

    // =========================================================================
    // FEED RATE & SPINDLE
    // =========================================================================

    /// <summary>
    /// Read actual feed rate (F actual = programmed × override%).
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_actf")]
    internal static extern short ReadActualFeedRate(ushort handle, out OdbActualFeed feedRate);

    /// <summary>
    /// Read actual spindle speed (S actual).
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_acts")]
    internal static extern short ReadActualSpindleSpeed(ushort handle, out OdbActualSpeed spindleSpeed);

    /// <summary>
    /// Read spindle load meter data.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdspload")]
    internal static extern short ReadSpindleLoad(ushort handle, short spindleNo, out OdbSpindleLoad load);

    // =========================================================================
    // ALARMS
    // =========================================================================

    /// <summary>
    /// Read active alarm messages (up to 10 alarms).
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdalmmsg")]
    internal static extern short ReadAlarmMessages(
        ushort handle, short type, ref short num, OdbAlarmMessage[] alarms);

    /// <summary>
    /// Read alarm status summary (bitmask of active alarm types).
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_alarm")]
    internal static extern short ReadAlarmStatus(ushort handle, out OdbAlarmStatus alarmStatus);

    // =========================================================================
    // PRODUCTION DATA
    // =========================================================================

    /// <summary>
    /// Read execution time (auto run time, cycle time, etc.).
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtimer")]
    internal static extern short ReadTimer(ushort handle, short type, out OdbTimer timer);

    /// <summary>
    /// Read parts counter (current count and required count).
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdparam")]
    internal static extern short ReadParameter(
        ushort handle, short paramNo, short axisNo, short length, out OdbParameter param);

    // =========================================================================
    // SYSTEM INFORMATION
    // =========================================================================

    /// <summary>
    /// Read CNC system information (model, axes, max spindles, etc.).
    /// Useful for auto-detecting machine capabilities.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_sysinfo")]
    internal static extern short ReadSystemInfo(ushort handle, out OdbSystemInfo sysInfo);

    /// <summary>
    /// Read number of controlled axes.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdaxisnum")]
    internal static extern short ReadAxisCount(ushort handle, out short axisCount);

    /// <summary>
    /// Read axis names (X, Y, Z, A, B, C, etc.).
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdaxisname")]
    internal static extern short ReadAxisNames(
        ushort handle, ref short dataNum, OdbAxisName[] axisNames);

    // =========================================================================
    // TOOL DATA
    // =========================================================================

    /// <summary>
    /// Read CNC modal data (current G/M/T/S/B codes).
    /// Used to read the active T-code (tool number).
    ///
    /// Parameters:
    ///   handle — Connection handle
    ///   type   — Modal type: 0-12=G-code groups, 107=M, 108=T, 109=S, 110=B
    ///   length — Buffer size (typically 40 bytes for ODBMDL)
    ///   data   — [out] Modal data buffer
    ///
    /// For T-code (type=108): the tool number is at offset 4 as a 4-byte int.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_modal")]
    internal static extern short ReadModal(
        ushort handle, short type, short length, byte[] data);

    /// <summary>
    /// Read a CNC macro variable (custom variable or system variable).
    /// Used as fallback for reading T-code (#4120), M-code (#4130), etc.
    ///
    /// Parameters:
    ///   handle  — Connection handle
    ///   number  — Macro variable number (#4120 = current T-code on path 1)
    ///   length  — Buffer size (10 for ODBM structure)
    ///   macro   — [out] Macro variable data
    ///
    /// Common system variables:
    ///   #4120 — Current T-code (path 1)
    ///   #4130 — Current M-code (path 1)
    ///   #3901 — Parts counter (some controllers)
    ///   #3902 — Parts required
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdmacro")]
    internal static extern short ReadMacro(
        ushort handle, short number, short length, out OdbMacro macro);

    /// <summary>
    /// Read tool offset information (number of offsets and memory type).
    ///
    /// Parameters:
    ///   handle — Connection handle
    ///   ofsType — [out] Memory type: 0=A, 1=B, 2=C
    ///   useNo   — [out] Number of available tool offset sets
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtofsinfo2")]
    internal static extern short ReadToolOffsetInfo2(
        ushort handle, out short ofsType, out short useNo);

    /// <summary>
    /// Fallback: read just the number of available tool offsets.
    /// Use when cnc_rdtofsinfo2 is not supported.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtofsinfo")]
    internal static extern short ReadToolOffsetInfo(ushort handle, out short useNo);

    /// <summary>
    /// Read a single tool offset value.
    ///
    /// Parameters:
    ///   handle — Connection handle
    ///   number — Tool offset number (1-based)
    ///   type   — Offset type:
    ///            Memory A: 0=offset value
    ///            Memory B: 0=wear, 1=geometry
    ///            Memory C: 0=wear length, 1=wear radius, 2=geometry length, 3=geometry radius
    ///   length — Size of output buffer (8 bytes for ODBTOFS)
    ///   tofs   — [out] Tool offset data
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtofs")]
    internal static extern short ReadToolOffset(
        ushort handle, short number, short type, short length, out OdbToolOffset tofs);

    /// <summary>
    /// Read a range of tool offsets in one call (more efficient than individual reads).
    ///
    /// Parameters:
    ///   handle  — Connection handle
    ///   startNo — Start offset number (1-based)
    ///   type    — Offset type (same as cnc_rdtofs)
    ///   endNo   — End offset number
    ///   length  — Buffer size: 6 + 4*(endNo-startNo+1)
    ///   data    — [out] Buffer: [2 startNo][2 type][2 endNo][4*N values]
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtofsr")]
    internal static extern short ReadToolOffsetRange(
        ushort handle, short startNo, short type, short endNo, short length, byte[] data);

    /// <summary>
    /// Read tool life management configuration.
    ///
    /// Returns:
    ///   Buffer layout: [2 T_grp_num][2 T_life_count_type][2 T_grp_zero_skip]
    ///   T_grp_num: max number of tool life groups
    ///   T_life_count_type: 0=count, 1=minutes
    ///   T_grp_zero_skip: whether to skip groups with zero life
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtlinfo")]
    internal static extern short ReadToolLifeInfo(ushort handle, byte[] data);

    /// <summary>
    /// Read the number of registered tool life groups.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdngrp")]
    internal static extern short ReadToolLifeGroupCount(ushort handle, out short count);

    /// <summary>
    /// Read tool life group data (tool number, life count, life limit).
    ///
    /// Parameters:
    ///   handle  — Connection handle
    ///   groupNo — Group number (1-based)
    ///   data    — [out] Group data buffer
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtlgrp")]
    internal static extern short ReadToolLifeGroup(
        ushort handle, int groupNo, byte[] data);

    /// <summary>
    /// Read the currently used tool life group number.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtlusegrp")]
    internal static extern short ReadToolLifeUseGroup(ushort handle, out int groupNo);

    // =========================================================================
    // PMC (Programmable Machine Controller) DATA
    // =========================================================================

    /// <summary>
    /// Read PMC data from a range of addresses.
    /// Used to read G/F/Y/X/A/R/T/K/C/D table signals.
    ///
    /// Parameters:
    ///   handle   — Connection handle
    ///   addrType — PMC address type (0=G, 1=F, 2=Y, 3=X, 4=A, 5=R, 6=T, 7=K, 8=C, 9=D)
    ///   dataType — Data type (0=byte, 1=word, 2=long, 4=bit with byte size, 5=bit with word size)
    ///   startNo  — Start byte address number
    ///   endNo    — End byte address number
    ///   length   — Size of output buffer
    ///   pmcData  — [out] PMC data buffer (byte array, size = length)
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "pmc_rdpmcrng")]
    internal static extern short ReadPmcRange(
        ushort handle, short addrType, short dataType,
        ushort startNo, ushort endNo, ushort length, byte[] pmcData);

    // =========================================================================
    // DIAGNOSTIC DATA
    // =========================================================================

    /// <summary>
    /// Read CNC diagnostic data (servo/spindle temperatures, insulation data, etc.).
    ///
    /// Parameters:
    ///   handle   — Connection handle
    ///   diagNo   — Diagnostic number
    ///   axisNo   — Axis number (0=all, 1-n=specific axis, -1=all for some functions)
    ///   length   — Size of output buffer
    ///   diagData — [out] Diagnostic data
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_diagnoss")]
    internal static extern short ReadDiagnosticData(
        ushort handle, short diagNo, short axisNo, short length, out OdbDiagnosticData diagData);

    /// <summary>
    /// Read CNC diagnostic data for all axes (array form).
    /// Used when diagNo returns per-axis data and we want all axes at once.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_diagnoss")]
    internal static extern short ReadDiagnosticDataArray(
        ushort handle, short diagNo, short axisNo, short length, byte[] diagData);

    // =========================================================================
    // OPERATOR MESSAGES
    // =========================================================================

    /// <summary>
    /// Read CNC operator messages (warnings, operator messages on screen).
    ///
    /// Parameters:
    ///   handle — Connection handle
    ///   type   — Message type (-1=all, 0=operator msg 1-4, 4=macro msg, 5=external msg)
    ///   length — Size of output buffer (short)
    ///   opmsg  — [out] Operator message data buffer
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdopmsg")]
    internal static extern short ReadOperatorMessage(
        ushort handle, short type, short length, byte[] opmsg);

    // =========================================================================
    // PROGRAM INFORMATION (extended)
    // =========================================================================

    /// <summary>
    /// Read program directory (program number, comment, size).
    /// Used to get program comments for MainProgram and ActProgram.
    ///
    /// Parameters:
    ///   handle  — Connection handle
    ///   type    — Type of info (0=reg, 1=comment, 2=date+time)
    ///   topProg — Starting program number
    ///   numProg — [in/out] Number of programs to read / actually read
    ///   progDir — [out] Program directory data buffer
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdprogdir3")]
    internal static extern short ReadProgramDirectory(
        ushort handle, short type, ref int topProg, ref short numProg, byte[] progDir);

    // =========================================================================
    // SPINDLE MAINTENANCE / MACHINE CHECK
    // =========================================================================

    /// <summary>
    /// Read maintenance check data (fan status, battery status, etc.).
    /// This function reads SP maintenance information.
    ///
    /// Parameters:
    ///   handle — Connection handle
    ///   type   — Maintenance type
    ///   data   — [out] Maintenance data buffer
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdspmchk")]
    internal static extern short ReadSpMaintCheck(
        ushort handle, short type, byte[] data);

    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>All axes selector for position-reading functions.</summary>
    internal const short ALL_AXES = -1;

    /// <summary>Compute buffer length for axis data: 4 + 4 + 4*MAX_AXIS.</summary>
    internal const short MAX_AXIS = 32;

    // PMC address types for ReadPmcRange
    internal const short PMC_ADDR_G = 0;  // G: Output from CNC to PMC
    internal const short PMC_ADDR_F = 1;  // F: Output from PMC to CNC
    internal const short PMC_ADDR_Y = 2;  // Y: Output from PMC to machine
    internal const short PMC_ADDR_X = 3;  // X: Input from machine to PMC
    internal const short PMC_ADDR_A = 4;  // A: Message demand
    internal const short PMC_ADDR_R = 5;  // R: Internal relay
    internal const short PMC_ADDR_T = 6;  // T: Timer
    internal const short PMC_ADDR_K = 7;  // K: Keep relay
    internal const short PMC_ADDR_C = 8;  // C: Counter
    internal const short PMC_ADDR_D = 9;  // D: Data table

    // PMC data types for ReadPmcRange
    internal const short PMC_TYPE_BYTE = 0;
    internal const short PMC_TYPE_WORD = 1;
    internal const short PMC_TYPE_LONG = 2;
}

// =============================================================================
// FOCAS2 STRUCTURE DEFINITIONS
// =============================================================================
// These structures mirror the C structs defined in the FOCAS2 header files.
// They are used as [out] parameters in the P/Invoke declarations above.
// LayoutKind.Sequential ensures the memory layout matches the C structures.

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbProgramNumber
{
    public short Dummy1;
    public short Dummy2;
    public int RunningProgram;  // Currently running O number
    public int MainProgram;     // Main program O number
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbStatusInfo
{
    public short Dummy;      // Not used
    public short Tmmode;     // T/M mode selection (Series 15 only, but field always present in struct)
    public short Aut;        // AUTO mode (0=MDI, 1=MEM, 3=EDIT, 4=HANDLE, 5=JOG, 6=TJOG, 7=THND)
    public short Run;        // Run state (0=RESET, 1=STOP, 2=HOLD, 3=START, 4=MSTR)
    public short Motion;     // Axis motion (0=none, 1=motion, 2=dwell)
    public short Mstb;       // M/S/T/B status
    public short Emergency;  // Emergency stop (0=off, 1=on)
    public short Alarm;      // Alarm state (0=no alarm, 1=alarm, 2=battery)
    public short Edit;       // Edit mode
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbAxisData
{
    public short Dummy;          // Not used
    public short Type;           // Axis number (1=first axis)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Focas2Interop.MAX_AXIS)]
    public int[] Data;           // Position data (value × 10^decimal)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Focas2Interop.MAX_AXIS)]
    public short[] Decimal;      // Decimal point position for each axis
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbActualFeed
{
    public short Dummy;
    public short Type;
    public int Data;     // Feed rate value
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbActualSpeed
{
    public short Dummy;
    public short Type;
    public int Data;     // Spindle speed in RPM
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbSpindleLoad
{
    public short DataNo;
    public short Type;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public short[] Data;     // Spindle load percentage × 10
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
internal struct OdbAlarmMessage
{
    public int AlarmNo;
    public short Type;
    public short Axis;
    public short Dummy;
    public short MsgLength;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string AlarmMessage;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbAlarmStatus
{
    public short Dummy;
    public short Data;   // Bitmask: bit0=PS, bit1=OT, bit2=SV, etc.
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbTimer
{
    public short Dummy;
    public short Type;
    public int Minute;
    public int Msec;     // Milliseconds portion
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbParameter
{
    public short DataNo;
    public short Type;
    public int LData;    // Long parameter value
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
internal struct OdbSystemInfo
{
    public short Dummy;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2)]
    public string MaxAxis;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2)]
    public string CncType;     // " M" = Machining center, " T" = Lathe
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string MtType;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string Series;      // CNC series (e.g., "0iD ", "30i ")
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string Version;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2)]
    public string Axes;        // Number of controlled axes
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
internal struct OdbAxisName
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string Name;       // Axis name (e.g., "X", "Y", "Z")
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string Suffix;     // Axis suffix
}

/// <summary>
/// Macro variable data returned by cnc_rdmacro.
/// Value = McVal × 10^(-McDig)
/// Example: McVal=12345, McDig=-3 → actual value = 12.345
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbMacro
{
    public short DataNo;   // Macro variable number
    public short Dummy;    // Not used
    public int McVal;      // Macro value (integer part)
    public short McDig;    // Decimal digits (negative = digits after decimal point)
}

/// <summary>
/// Tool offset data returned by cnc_rdtofs.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbToolOffset
{
    public short DataNo;   // Offset number
    public short Type;     // Offset type
    public int Data;       // Offset value (raw — divide by 1000 for mm IS-B, 10000 for IS-C)
}

/// <summary>
/// Diagnostic data returned by cnc_diagnoss.
/// Layout matches ODBDGN structure for a single axis / non-axis diagnostic.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbDiagnosticData
{
    public short DataNo;   // Diagnostic number
    public short Type;     // Data type / axis number
    public int LData;      // Long data value (most diagnostics use this)
}

/// <summary>
/// Common FOCAS2 error codes returned by all API functions.
/// </summary>
public enum Focas2Error : short
{
    EW_OK = 0,           // Success
    EW_SOCKET = -16,     // Socket communication error
    EW_NODLL = -15,      // DLL not found
    EW_HANDLE = -8,      // Invalid handle
    EW_VERSION = -7,     // CNC/PMC version mismatch
    EW_UNEXP = -6,       // Unexpected error
    EW_SYSTEM = -5,      // System error
    EW_PERM = -4,        // Permission error
    EW_BUSY = -1,        // CNC is busy (retry later)
    EW_FUNC = 1,         // Function not available
    EW_LENGTH = 2,       // Data length error
    EW_NUMBER = 3,       // Data number error
    EW_ATTRIB = 4,       // Data attribute error
    EW_DATA = 5,         // Data value error
    EW_NOOPT = 6,        // Option not enabled
    EW_PROT = 7,         // Write protection
    EW_OVRFLOW = 8,      // Memory overflow
    EW_PARAM = 9,        // Parameter error
    EW_BUFFER = 10,      // Buffer full
    EW_PATH = 11,        // Path error
    EW_MODE = 12,        // CNC mode error
    EW_REJECT = 13,      // Execution rejected
    EW_ALARM = 15,       // Alarm state
    EW_STOP = 16,        // CNC not running
    EW_PASSWD = 17       // Password error
}
