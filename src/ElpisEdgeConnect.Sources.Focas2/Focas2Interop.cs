// ============================================================================
// File: Focas2Interop.cs
// Purpose: P/Invoke declarations for the Fanuc FOCAS2 native library.
//          Migrated from legacy ElpisEdgeConnect.DataSources.Focas2 for Phase 2.
//
// FOCAS2 LIBRARY:
//   - Windows 64-bit: Fwlib64.dll (or Fwlib32.dll for 32-bit)
//   - Linux:          libfwlib32.so
//   - Provided by Fanuc — NOT open-source. Must be obtained from Fanuc.
//
// THREAD SAFETY:
//   FOCAS2 functions are NOT thread-safe per handle. All calls for a given
//   handle must be serialized on the same thread. Focas2Thread enforces this.
//
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// P/Invoke declarations for Fanuc FOCAS2 native library functions.
/// All calls must be dispatched via the Focas2Thread to guarantee
/// thread affinity for handle-bound operations.
/// </summary>
#pragma warning disable CA1707 // Underscores in identifiers — FOCAS2/PMC naming convention
internal static class Focas2Interop
{
    // Library name in every [DllImport] attribute below. The actual file
    // name on disk varies by platform (Fwlib64.dll on Windows x64,
    // Fwlib32.dll on Windows x86, libfwlib32.so on Linux); a static
    // constructor installs a NativeLibrary.SetDllImportResolver that maps
    // this logical name to the right file at load time. Adding a new
    // platform = adding a case in the resolver, not changing any DllImport.
    internal const string FOCAS_DLL = "Fwlib64";

    /// <summary>
    /// Installs the per-assembly <see cref="DllImportResolver"/> that picks
    /// the correct FOCAS2 native-library file name for the current OS.
    /// Runs once when any member of this static class is first touched.
    /// </summary>
    static Focas2Interop()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(Focas2Interop).Assembly,
            ResolveFocasLibrary);
    }

    private static IntPtr ResolveFocasLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        // Pass through anything that isn't FOCAS — the Focas2 assembly
        // currently has only one native dependency, but the resolver is
        // scoped to the whole assembly so we must be defensive.
        if (!string.Equals(libraryName, FOCAS_DLL, StringComparison.Ordinal))
        {
            return NativeLibrary.Load(libraryName, assembly, searchPath);
        }

        // Preferred file name per OS + architecture. If the preferred name
        // isn't present, fall back to the alternates so deployments that
        // ship a differently-named file still resolve.
        var candidates = GetCandidateFileNames();
        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
            {
                return handle;
            }
        }

        // No candidate loaded. Throw a DllNotFoundException with a message
        // that tells operators exactly which file names were attempted,
        // so they can figure out which one Fanuc shipped them.
        throw new DllNotFoundException(
            $"Could not load the Fanuc FOCAS2 native library. Tried: " +
            $"{string.Join(", ", candidates)}. " +
            "Obtain the library from Fanuc (Windows: Fwlib64.dll / Fwlib32.dll; " +
            "Linux: libfwlib32.so) and place it beside the host binary or " +
            "on the platform's standard library search path.");
    }

    private static string[] GetCandidateFileNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 or Architecture.Arm64 => ["Fwlib64.dll", "Fwlib64"],
                Architecture.X86 => ["Fwlib32.dll", "Fwlib32"],
                _ => ["Fwlib64.dll", "Fwlib32.dll"],
            };
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Fanuc's Linux build historically ships as libfwlib32.so
            // regardless of architecture; prefer that, fall back to a .so.1.
            return ["libfwlib32.so", "libfwlib32.so.1", "fwlib32"];
        }
        // macOS and other platforms — FOCAS2 is not officially supported
        // there. Return a single logical name so the error message is
        // clean and consistent.
        return ["Fwlib64"];
    }

    // =========================================================================
    // CONNECTION MANAGEMENT
    // =========================================================================

    /// <summary>Open a connection to a CNC controller via Ethernet.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_allclibhndl3")]
    internal static extern short AllocLibHandle(
        [MarshalAs(UnmanagedType.LPStr)] string ipAddress,
        ushort port,
        int timeout,
        out ushort handle);

    /// <summary>Close a CNC connection and free the library handle.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_freelibhndl")]
    internal static extern short FreeLibHandle(ushort handle);

    /// <summary>
    /// Set the per-handle data-window time-out (in seconds) for subsequent
    /// reads on this handle. Operational mitigation for the silent-stall class
    /// (incident 2026-06-24): without it, an individual fwlib read on a
    /// black-holed/non-progressing connection has no upper time bound and can
    /// wedge the affine worker thread indefinitely. Native signature:
    /// <c>short cnc_setdtimeout(unsigned short FlibHndl, long time)</c> — C
    /// <c>long</c> is 32-bit on Windows, hence <see cref="int"/>. This bounds
    /// the library-level wait; it does NOT replace the per-generation deadline
    /// of slice-0 commit 3.1.
    /// </summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_setdtimeout")]
    internal static extern short SetDataTimeout(ushort handle, int time);

    // =========================================================================
    // PROGRAM INFORMATION
    // =========================================================================

    /// <summary>Read the current program number (main + running).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdprgnum")]
    internal static extern short ReadProgramNumber(ushort handle, out OdbProgramNumber programNum);

    /// <summary>Read CNC status information (run state, motion, alarm, etc.).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_statinfo")]
    internal static extern short ReadStatusInfo(ushort handle, out OdbStatusInfo statusInfo);

    // =========================================================================
    // AXIS POSITION DATA
    // =========================================================================

    /// <summary>Read absolute position of all axes.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_absolute")]
    internal static extern short ReadAbsolutePosition(
        ushort handle, short axisNum, short length, out OdbAxisData position);

    /// <summary>Read machine coordinate position of all axes.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_machine")]
    internal static extern short ReadMachinePosition(
        ushort handle, short axisNum, short length, out OdbAxisData position);

    /// <summary>Read relative position of all axes.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_relative")]
    internal static extern short ReadRelativePosition(
        ushort handle, short axisNum, short length, out OdbAxisData position);

    /// <summary>Read distance-to-go for all axes.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_distance")]
    internal static extern short ReadDistanceToGo(
        ushort handle, short axisNum, short length, out OdbAxisData position);

    // =========================================================================
    // FEED RATE & SPINDLE
    // =========================================================================

    /// <summary>Read actual feed rate (F actual = programmed x override%).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_actf")]
    internal static extern short ReadActualFeedRate(ushort handle, out OdbActualFeed feedRate);

    /// <summary>Read actual spindle speed (S actual).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_acts")]
    internal static extern short ReadActualSpindleSpeed(ushort handle, out OdbActualSpeed spindleSpeed);

    /// <summary>Read spindle load meter data.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdspload")]
    internal static extern short ReadSpindleLoad(ushort handle, short spindleNo, out OdbSpindleLoad load);

    // =========================================================================
    // ALARMS
    // =========================================================================

    /// <summary>Read active alarm messages (up to 10 alarms).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdalmmsg")]
    internal static extern short ReadAlarmMessages(
        ushort handle, short type, ref short num, OdbAlarmMessage[] alarms);

    /// <summary>Read alarm status summary (bitmask of active alarm types).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_alarm")]
    internal static extern short ReadAlarmStatus(ushort handle, out OdbAlarmStatus alarmStatus);

    // =========================================================================
    // PRODUCTION DATA
    // =========================================================================

    /// <summary>Read execution time (auto run time, cycle time, etc.).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtimer")]
    internal static extern short ReadTimer(ushort handle, short type, out OdbTimer timer);

    /// <summary>Read CNC parameter (parts counter, etc.).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdparam")]
    internal static extern short ReadParameter(
        ushort handle, short paramNo, short axisNo, short length, out OdbParameter param);

    // =========================================================================
    // SYSTEM INFORMATION
    // =========================================================================

    /// <summary>Read CNC system information (model, axes, max spindles, etc.).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_sysinfo")]
    internal static extern short ReadSystemInfo(ushort handle, out OdbSystemInfo sysInfo);

    /// <summary>Read number of controlled axes.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdaxisnum")]
    internal static extern short ReadAxisCount(ushort handle, out short axisCount);

    /// <summary>Read axis names (X, Y, Z, A, B, C, etc.).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdaxisname")]
    internal static extern short ReadAxisNames(
        ushort handle, ref short dataNum, OdbAxisName[] axisNames);

    // =========================================================================
    // TOOL DATA
    // =========================================================================

    /// <summary>Read CNC modal data (current G/M/T/S/B codes).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_modal")]
    internal static extern short ReadModal(
        ushort handle, short type, short length, byte[] data);

    /// <summary>Read a CNC macro variable (system variable or custom).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdmacro")]
    internal static extern short ReadMacro(
        ushort handle, short number, short length, out OdbMacro macro);

    /// <summary>Read tool offset information (number of offsets and memory type).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtofsinfo2")]
    internal static extern short ReadToolOffsetInfo2(
        ushort handle, out short ofsType, out short useNo);

    /// <summary>Fallback: read just the number of available tool offsets.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtofsinfo")]
    internal static extern short ReadToolOffsetInfo(ushort handle, out short useNo);

    /// <summary>Read a single tool offset value.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtofs")]
    internal static extern short ReadToolOffset(
        ushort handle, short number, short type, short length, out OdbToolOffset tofs);

    /// <summary>Read a range of tool offsets in one call.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtofsr")]
    internal static extern short ReadToolOffsetRange(
        ushort handle, short startNo, short type, short endNo, short length, byte[] data);

    /// <summary>Read tool life management configuration.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtlinfo")]
    internal static extern short ReadToolLifeInfo(ushort handle, byte[] data);

    /// <summary>Read the number of registered tool life groups.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdngrp")]
    internal static extern short ReadToolLifeGroupCount(ushort handle, out short count);

    /// <summary>Read tool life group data (tool number, life count, life limit).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtlgrp")]
    internal static extern short ReadToolLifeGroup(
        ushort handle, int groupNo, byte[] data);

    /// <summary>Read the currently used tool life group number.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdtlusegrp")]
    internal static extern short ReadToolLifeUseGroup(ushort handle, out int groupNo);

    // =========================================================================
    // PMC (Programmable Machine Controller) DATA
    // =========================================================================

    /// <summary>Read PMC data from a range of addresses.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "pmc_rdpmcrng")]
    internal static extern short ReadPmcRange(
        ushort handle, short addrType, short dataType,
        ushort startNo, ushort endNo, ushort length, byte[] pmcData);

    // =========================================================================
    // DIAGNOSTIC DATA
    // =========================================================================

    /// <summary>Read CNC diagnostic data (servo/spindle temps, insulation, etc.).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_diagnoss")]
    internal static extern short ReadDiagnosticData(
        ushort handle, short diagNo, short axisNo, short length, out OdbDiagnosticData diagData);

    /// <summary>Read CNC diagnostic data for all axes (array form).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_diagnoss")]
    internal static extern short ReadDiagnosticDataArray(
        ushort handle, short diagNo, short axisNo, short length, byte[] diagData);

    // =========================================================================
    // OPERATOR MESSAGES
    // =========================================================================

    /// <summary>Read CNC operator messages.</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdopmsg")]
    internal static extern short ReadOperatorMessage(
        ushort handle, short type, short length, byte[] opmsg);

    // =========================================================================
    // PROGRAM INFORMATION (extended)
    // =========================================================================

    /// <summary>Read program directory (program number, comment, size).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdprogdir3")]
    internal static extern short ReadProgramDirectory(
        ushort handle, short type, ref int topProg, ref short numProg, byte[] progDir);

    // =========================================================================
    // SPINDLE MAINTENANCE / MACHINE CHECK
    // =========================================================================

    /// <summary>Read maintenance check data (fan status, battery status, etc.).</summary>
    [DllImport(FOCAS_DLL, EntryPoint = "cnc_rdspmchk")]
    internal static extern short ReadSpMaintCheck(
        ushort handle, short type, byte[] data);

    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>All axes selector for position-reading functions.</summary>
    internal const short ALL_AXES = -1;

    /// <summary>Maximum number of controlled axes in FOCAS2 structures.</summary>
    internal const short MAX_AXIS = 32;

    // PMC address types
    internal const short PMC_ADDR_G = 0;
    internal const short PMC_ADDR_F = 1;
    internal const short PMC_ADDR_Y = 2;
    internal const short PMC_ADDR_X = 3;
    internal const short PMC_ADDR_A = 4;
    internal const short PMC_ADDR_R = 5;
    internal const short PMC_ADDR_T = 6;
    internal const short PMC_ADDR_K = 7;
    internal const short PMC_ADDR_C = 8;
    internal const short PMC_ADDR_D = 9;

    // PMC data types
    internal const short PMC_TYPE_BYTE = 0;
    internal const short PMC_TYPE_WORD = 1;
    internal const short PMC_TYPE_LONG = 2;
}
#pragma warning restore CA1707

// =============================================================================
// FOCAS2 STRUCTURE DEFINITIONS
// =============================================================================

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbProgramNumber
{
    public short Dummy1;
    public short Dummy2;
    public int RunningProgram;
    public int MainProgram;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbStatusInfo
{
    public short Dummy;
    public short Tmmode;
    public short Aut;        // AUTO mode (0=MDI, 1=MEM, 3=EDIT, 4=HANDLE, 5=JOG, 6=TJOG, 7=THND)
    public short Run;        // Run state (0=RESET, 1=STOP, 2=HOLD, 3=START, 4=MSTR)
    public short Motion;     // Axis motion (0=none, 1=motion, 2=dwell)
    public short Mstb;
    public short Emergency;  // Emergency stop (0=off, 1=on)
    public short Alarm;      // Alarm state (0=no alarm, 1=alarm, 2=battery)
    public short Edit;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbAxisData
{
    public short Dummy;
    public short Type;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Focas2Interop.MAX_AXIS)]
    public int[] Data;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = Focas2Interop.MAX_AXIS)]
    public short[] Decimal;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbActualFeed
{
    public short Dummy;
    public short Type;
    public int Data;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbActualSpeed
{
    public short Dummy;
    public short Type;
    public int Data;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbSpindleLoad
{
    public short DataNo;
    public short Type;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public short[] Data;
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
    public short Data;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbTimer
{
    public short Dummy;
    public short Type;
    public int Minute;
    public int Msec;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbParameter
{
    public short DataNo;
    public short Type;
    public int LData;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
internal struct OdbSystemInfo
{
    public short Dummy;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2)]
    public string MaxAxis;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2)]
    public string CncType;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string MtType;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string Series;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string Version;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2)]
    public string Axes;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 4)]
internal struct OdbAxisName
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string Name;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string Suffix;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbMacro
{
    public short DataNo;
    public short Dummy;
    public int McVal;
    public short McDig;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbToolOffset
{
    public short DataNo;
    public short Type;
    public int Data;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct OdbDiagnosticData
{
    public short DataNo;
    public short Type;
    public int LData;
}

/// <summary>
/// Common FOCAS2 error codes returned by all API functions.
/// </summary>
#pragma warning disable CA1707 // Identifiers should not contain underscores — FOCAS2 convention
internal enum Focas2ErrorCode : short
{
    /// <summary>Success.</summary>
    EW_OK = 0,
    /// <summary>Socket communication error.</summary>
    EW_SOCKET = -16,
    /// <summary>DLL not found.</summary>
    EW_NODLL = -15,
    /// <summary>Invalid handle.</summary>
    EW_HANDLE = -8,
    /// <summary>CNC/PMC version mismatch.</summary>
    EW_VERSION = -7,
    /// <summary>Unexpected error.</summary>
    EW_UNEXP = -6,
    /// <summary>System error.</summary>
    EW_SYSTEM = -5,
    /// <summary>Permission error.</summary>
    EW_PERM = -4,
    /// <summary>CNC is busy (retry later).</summary>
    EW_BUSY = -1,
    /// <summary>Function not available.</summary>
    EW_FUNC = 1,
    /// <summary>Data length error.</summary>
    EW_LENGTH = 2,
    /// <summary>Data number error.</summary>
    EW_NUMBER = 3,
    /// <summary>Data attribute error.</summary>
    EW_ATTRIB = 4,
    /// <summary>Data value error.</summary>
    EW_DATA = 5,
    /// <summary>Option not enabled.</summary>
    EW_NOOPT = 6,
    /// <summary>Write protection.</summary>
    EW_PROT = 7,
    /// <summary>Memory overflow.</summary>
    EW_OVRFLOW = 8,
    /// <summary>Parameter error.</summary>
    EW_PARAM = 9,
    /// <summary>Buffer full.</summary>
    EW_BUFFER = 10,
    /// <summary>Path error.</summary>
    EW_PATH = 11,
    /// <summary>CNC mode error.</summary>
    EW_MODE = 12,
    /// <summary>Execution rejected.</summary>
    EW_REJECT = 13,
    /// <summary>Alarm state.</summary>
    EW_ALARM = 15,
    /// <summary>CNC not running.</summary>
    EW_STOP = 16,
    /// <summary>Password error.</summary>
    EW_PASSWD = 17,
}
#pragma warning restore CA1707
