// ============================================================================
// File: DataSources/IMachineDataSource.cs
// Purpose: Core abstraction for CNC machine data acquisition.
//          All data source implementations (MT-LINKi REST API, FOCAS2 DLL,
//          HTTP REST simulator) implement this interface.
//
// STRATEGY PATTERN:
//   Each machine in appsettings.json specifies a DataSourceType (MtLinki,
//   Focas2Dll, or HttpRest). The DataSourceFactory creates the correct
//   implementation, and MachinePollerService uses it through this interface.
//
//   ┌──────────────────┐     ┌───────────────────────┐
//   │ MachinePoller     │────▶│ IMachineDataSource     │
//   │ Service           │     │   .CollectDataAsync()  │
//   └──────────────────┘     └───────────┬───────────┘
//                                        │
//              ┌────────────────┬────────┼────────┬────────────────┐
//              ▼                ▼        ▼        ▼                ▼
//     ┌──────────────┐ ┌────────────┐ ┌────────┐ ┌──────────────────┐
//     │ MtLinki REST  │ │ Focas2 DLL │ │  HTTP  │ │   MTConnect      │
//     │ DataSource    │ │ DataSource │ │  REST  │ │   DataSource     │
//     │ (Fanuc only)  │ │ (P/Invoke) │ │ (Sim)  │ │ (Open Standard)  │
//     └──────────────┘ └────────────┘ └────────┘ └──────────────────┘
// ============================================================================

using ElpisEdgeConnect.Models;

namespace ElpisEdgeConnect.DataSources;

/// <summary>
/// Interface for acquiring CNC machine data from any source.
/// 
/// Each implementation encapsulates a different data acquisition method:
///   - MtLinkiRestDataSource:   Reads from Fanuc MT-LINKi's REST API
///   - Focas2DllDataSource:     Calls FOCAS2 native DLL via P/Invoke
///   - HttpRestDataSource:      HTTP REST calls (simulator/custom wrapper)
/// 
/// LIFECYCLE:
///   1. Created by DataSourceFactory.Create() based on MachineConfig.DataSourceType
///   2. Used by MachinePollerService.PollOnceAsync() on every poll cycle
///   3. Disposed when the machine is removed or service shuts down
/// </summary>
public interface IMachineDataSource : IDisposable
{
    /// <summary>Machine ID — used for logging and identification.</summary>
    string MachineId { get; }

    /// <summary>Machine name — human-readable, for logging.</summary>
    string MachineName { get; }

    /// <summary>
    /// The data source type this implementation represents.
    /// Used for diagnostics and logging.
    /// </summary>
    DataSourceType SourceType { get; }

    /// <summary>
    /// Collect all configured data points from this machine in a single snapshot.
    /// 
    /// Each implementation handles its own:
    ///   - Connection management (HTTP client, FOCAS2 handle, HTTP client)
    ///   - Error handling and status reporting
    ///   - Data point mapping to the common CncMachineData model
    /// 
    /// The returned CncMachineData.Status reflects the collection outcome:
    ///   - Online:  Data collected successfully
    ///   - Offline: Source unreachable (circuit breaker, connection refused)
    ///   - Error:   Partial failure (some data points failed)
    ///   - Alarm:   Machine has active alarms
    /// </summary>
    /// <param name="ct">Cancellation token from the poller's stopping signal</param>
    /// <returns>CncMachineData snapshot with all collected values</returns>
    Task<CncMachineData> CollectDataAsync(CancellationToken ct);

    /// <summary>
    /// Tests connectivity to the data source.
    /// Returns true if the source is reachable, false otherwise.
    /// Used during startup health checks.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct);
}

/// <summary>
/// Data acquisition method for a CNC machine.
/// Each machine in appsettings.json specifies one of these as DataSourceType.
/// </summary>
public enum DataSourceType
{
    /// <summary>
    /// Read CNC data from Fanuc MT-LINKi's REST API (port 3000).
    /// MT-LINKi Collector PC acquires data from CNCs via FOCAS and exposes
    /// it through a REST API. This is the recommended production approach
    /// for factories already running MT-LINKi.
    /// 
    /// API pattern: {BaseUrl}/api/v1/equipment/CNC/monitorings
    /// Requires: MtLinki connection settings in MachineConfig.
    /// </summary>
    MtLinki,

    /// <summary>
    /// Call Fanuc FOCAS2 native library (Fwlib32.dll / libfwlib32.so) directly
    /// via P/Invoke. Direct Ethernet connection to the CNC controller.
    /// 
    /// Best for: Small setups without MT-LINKi, or when real-time data
    /// is needed with minimal latency.
    /// 
    /// Requires: FOCAS2 DLL installed on the machine running the bridge.
    /// Connection settings: IP address and port in MachineConfig.
    /// </summary>
    Focas2Dll,

    /// <summary>
    /// HTTP REST API calls to a web service endpoint.
    /// Used for: Custom FOCAS REST wrappers, the CNC Simulator, or
    /// any HTTP-based data source.
    ///
    /// This is the original data acquisition method from the bridge's
    /// initial implementation, preserved for backward compatibility
    /// and simulator testing.
    /// </summary>
    HttpRest,

    /// <summary>
    /// MTConnect open standard protocol (HTTP/XML).
    /// Free, royalty-free standard for CNC data acquisition.
    ///
    /// Supported brands: Brother (Speedio), Mazak, Okuma, DMG Mori,
    /// Haas, Doosan, Makino, and any CNC with an MTConnect adapter.
    ///
    /// No proprietary DLLs or licenses needed — just HTTP to the
    /// CNC's built-in MTConnect agent (typically port 5000 or 7878).
    ///
    /// Requires: MTConnect connection settings in MachineConfig.
    /// </summary>
    MTConnect,

    /// <summary>
    /// Brother CNC built-in HTTP web monitoring interface.
    /// Reads machine data from Brother Speedio (and other models) via
    /// their embedded web server on port 80. No licenses needed.
    ///
    /// Endpoints: /HTTPD_MCNINFO, /MNTP_WKCNTR, /MNTP_CYCLETIME,
    /// /ATC_TOOLS, /ALARM_CURALMLIST, /MNTP_MAINTNOTICE
    ///
    /// Supported: Brother Speedio S/R/M/W series, any Brother CNC
    /// with the built-in web monitoring interface.
    ///
    /// Requires: BrotherHttp connection settings in MachineConfig.
    /// </summary>
    BrotherHttp
}
