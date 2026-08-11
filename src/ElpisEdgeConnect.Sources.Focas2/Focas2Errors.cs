// ============================================================================
// File: Focas2Errors.cs
// Purpose: FOCAS2-specific error code constants following the locked convention
//          {PROTOCOL}.{CATEGORY_SUBCATEGORY}.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 13.3, Errors/AdapterError.cs
// ============================================================================

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Stable error codes for the FOCAS2 source adapter. All codes follow the
/// locked convention <c>FOCAS2.{CATEGORY_SUBCATEGORY}</c>.
/// </summary>
internal static class Focas2Errors
{
    // ---- Network ----

    /// <summary>cnc_allclibhndl3 failed after all retries.</summary>
    internal const string ConnectFailed = "FOCAS2.CONNECT_FAILED";

    /// <summary>Handle went invalid mid-session (EW_HANDLE).</summary>
    internal const string HandleInvalid = "FOCAS2.HANDLE_INVALID";

    /// <summary>Socket communication lost (EW_SOCKET).</summary>
    internal const string SocketError = "FOCAS2.SOCKET_ERROR";

    // ---- ResourceExhausted ----

    /// <summary>Skipping connect due to exponential backoff.</summary>
    internal const string ConnectBackoff = "FOCAS2.CONNECT_BACKOFF";

    /// <summary>CNC has no available handle slots.</summary>
    internal const string HandleExhausted = "FOCAS2.HANDLE_EXHAUSTED";

    // ---- Configuration ----

    /// <summary>Fwlib64.dll / libfwlib32.so not found.</summary>
    internal const string DllNotFound = "FOCAS2.DLL_NOT_FOUND";

    /// <summary>Bad configuration (missing IP, bad port, etc.).</summary>
    internal const string ConfigInvalid = "FOCAS2.CONFIG_INVALID";

    /// <summary>Config is not Focas2SourceConfiguration.</summary>
    internal const string ConfigWrongType = "FOCAS2.CONFIG_WRONG_TYPE";

    // ---- Protocol ----

    /// <summary>Function not supported on this CNC model (EW_FUNC).</summary>
    internal const string FuncNotAvailable = "FOCAS2.FUNC_NOT_AVAILABLE";

    /// <summary>Non-fatal error during data collection.</summary>
    internal const string CollectError = "FOCAS2.COLLECT_ERROR";

    /// <summary>Handle validation via cnc_statinfo failed after allocation.</summary>
    internal const string StatinfoFailed = "FOCAS2.STATINFO_FAILED";

    // ---- DeviceState ----

    /// <summary>CNC is busy, retry later (EW_BUSY).</summary>
    internal const string CncBusy = "FOCAS2.CNC_BUSY";

    // ---- Authentication ----

    /// <summary>FOCAS2 permission denied (EW_PERM).</summary>
    internal const string PermissionError = "FOCAS2.PERMISSION_ERROR";
}
