// ============================================================================
// File: Configuration/MachineConfig.cs
// Purpose: Defines the configuration model for a single Fanuc CNC machine.
//          Supports three data source types configurable per machine:
//          MT-LINKi REST API, FOCAS2 DLL (P/Invoke), and HTTP REST (simulator).
// ============================================================================

using System.ComponentModel.DataAnnotations;
using ElpisEdgeConnect.DataSources;

namespace ElpisEdgeConnect.Configuration;

/// <summary>
/// Configuration for a single Fanuc CNC machine.
/// Supports hot-reload — add/remove machines in appsettings.json without restarting.
/// </summary>
public sealed class MachineConfig
{
    [Required, MinLength(1)]
    public string MachineId { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    // ── MQTT Payload Identity ──

    /// <summary>
    /// Device ID sent in the MQTT payload's "DeviceId" field.
    /// Defaults to MachineId if not set.
    /// Example: "DeviceID_CNC", "CNC-001", "FACTORY_A_LATHE_01"
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Device name used as the prefix in each Data[] entry: "DeviceName.TagName,..."
    /// Defaults to MachineName (with spaces/special chars removed) if not set.
    /// Example: "LatheBay1", "MillCenter02", "CNC_Lathe_01"
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    // ── Data Source Selection ──

    /// <summary>
    /// Which data acquisition method to use for this machine.
    ///   MtLinki   — Read from Fanuc MT-LINKi REST API (port 3000)
    ///   Focas2Dll — Direct FOCAS2 native DLL via P/Invoke
    ///   HttpRest  — HTTP REST API (simulator, custom REST wrappers)
    /// </summary>
    public DataSourceType DataSourceType { get; set; } = DataSourceType.MtLinki;

    /// <summary>MT-LINKi REST API connection settings. Required when DataSourceType = MtLinki.</summary>
    public MtLinkiSettings? MtLinki { get; set; }

    /// <summary>Direct FOCAS2 DLL connection settings. Required when DataSourceType = Focas2Dll.</summary>
    public Focas2Settings? Focas2 { get; set; }

    /// <summary>MTConnect connection settings. Required when DataSourceType = MTConnect.</summary>
    public MTConnectSettings? MTConnect { get; set; }

    /// <summary>Brother HTTP web monitoring settings. Required when DataSourceType = BrotherHttp.</summary>
    public BrotherHttpSettings? BrotherHttp { get; set; }

    // ── HTTP REST Settings (DataSourceType = HttpRest) ──

    /// <summary>Base URL for HTTP REST data source (simulator or custom wrapper).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password. Formats: "env:VAR_NAME", "enc:IV:CT", or plaintext.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    public AuthType AuthType { get; set; } = AuthType.Basic;

    [Range(500, 30000)]
    public int TimeoutMs { get; set; } = 5000;

    public string CertificateThumbprint { get; set; } = string.Empty;

    // ── Common Settings (all data source types) ──

    [Range(100, 60000)]
    public int PollIntervalMs { get; set; } = 1000;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum consecutive poll errors before the poller auto-disables itself.
    /// Protects CNC controllers from runaway connection attempts (e.g., exhausting
    /// FOCAS2 connection slots). Set to 0 to disable auto-disable (poll forever).
    /// Default: 3 — after 3 consecutive failures, the poller stops and logs a CRITICAL warning.
    /// The machine can be re-enabled by restarting the bridge or editing config (hot-reload).
    /// </summary>
    [Range(0, 1000)]
    public int MaxConsecutiveErrors { get; set; } = 3;

    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// FOCAS data points to collect. Used by all data source types for
    /// determining which categories of data to read.
    /// </summary>
    public List<string> DataPoints { get; set; } = [];
}

/// <summary>
/// MT-LINKi REST API connection configuration.
/// MT-LINKi exposes CNC data through a REST API (typically on port 3000).
/// The bridge connects to this API instead of polling CNC controllers directly.
/// 
/// API pattern: {BaseUrl}/api/{ApiVersion}/equipment/{EquipmentType}/monitorings
/// Example:     http://192.168.2.199:3000/api/v1/equipment/CNC/monitorings
/// </summary>
public sealed class MtLinkiSettings
{
    /// <summary>
    /// MT-LINKi server base URL including port.
    /// Examples:
    ///   "http://192.168.2.199:3000"
    ///   "http://mtlinki-server:3000"
    ///   "https://mtlinki.factory.local:3000"
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>API version prefix (default: "v1"). Used in URL: /api/{ApiVersion}/...</summary>
    public string ApiVersion { get; set; } = "v1";

    /// <summary>Equipment type in MT-LINKi (default: "CNC"). Used in URL: /equipment/{EquipmentType}/...</summary>
    public string EquipmentType { get; set; } = "CNC";

    /// <summary>
    /// Machine identifier in MT-LINKi. Used to match this machine in API responses
    /// that return data for multiple machines. Leave empty to use MachineId.
    /// </summary>
    public string MachineIdentifier { get; set; } = string.Empty;

    /// <summary>Optional username for MT-LINKi API authentication.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Optional password for MT-LINKi API authentication.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Whether to also fetch from /monitorings/ALARM/logs endpoint.</summary>
    public bool FetchAlarmLogs { get; set; } = true;

    /// <summary>Whether to also fetch from /monitorings/PRODUCTION/logs endpoint.</summary>
    public bool FetchProductionLogs { get; set; } = false;
}

/// <summary>
/// Direct FOCAS2 DLL connection configuration.
/// Requires Fwlib64.dll (Windows) or libfwlib32.so (Linux) installed.
/// </summary>
public sealed class Focas2Settings
{
    /// <summary>CNC controller IP address on the factory network.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>FOCAS2 Ethernet port (default: 8193).</summary>
    public ushort Port { get; set; } = 8193;

    /// <summary>Connection timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Path to the FOCAS2 DLL. Leave empty for default search order.
    /// Windows: Fwlib64.dll in app directory or PATH
    /// Linux:   libfwlib32.so in LD_LIBRARY_PATH
    /// </summary>
    public string DllPath { get; set; } = string.Empty;

    /// <summary>
    /// Keep FOCAS2 connection open between polls (true = faster, uses a slot).
    /// </summary>
    public bool KeepAlive { get; set; } = true;
}

/// <summary>
/// MTConnect agent connection configuration.
/// MTConnect is an open, royalty-free standard for CNC machine monitoring.
/// Supported by: Brother, Mazak, Okuma, DMG Mori, Haas, and many others.
///
/// Endpoints:
///   {BaseUrl}/probe   → Device description (data items available)
///   {BaseUrl}/current → Current values of all data items
///   {BaseUrl}/sample  → Historical data with sequence numbers
/// </summary>
public sealed class MTConnectSettings
{
    /// <summary>
    /// MTConnect agent base URL including port.
    /// The port varies by manufacturer:
    ///   Brother Speedio: typically 5000 or 7878
    ///   Mazak:           typically 5000
    ///   Generic:         typically 5000 or 7878
    /// Examples:
    ///   "http://192.168.2.110:5000"
    ///   "http://192.168.2.110:7878"
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>
    /// Device name filter for multi-device agents.
    /// If set, requests are scoped to: {BaseUrl}/{DeviceName}/current
    /// Leave empty for single-device agents (most common).
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Brother CNC built-in HTTP web monitoring connection configuration.
/// Brother Speedio and other Brother CNC machines expose a web monitoring
/// interface on port 80 with CSV/text endpoints for real-time machine data.
///
/// Endpoints:
///   {BaseUrl}/HTTPD_MCNINFO   -> Machine info (model, status)
///   {BaseUrl}/MNTP_WKCNTR     -> Work counters (parts count)
///   {BaseUrl}/MNTP_CYCLETIME  -> Program, cycle times, operation hours
///   {BaseUrl}/ATC_TOOLS       -> ATC magazine tools
///   {BaseUrl}/ALARM_CURALMLIST -> Current alarms
/// </summary>
public sealed class BrotherHttpSettings
{
    /// <summary>
    /// Brother CNC web server base URL.
    /// Brother machines typically run their web interface on port 80.
    /// Examples:
    ///   "http://192.168.2.110"
    ///   "http://192.168.2.110:80"
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost";

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Authentication method for HTTP REST data sources.
/// </summary>
public enum AuthType
{
    None,
    Basic,
    Bearer
}
