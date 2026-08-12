// ============================================================================
// File: Licensing/LicenseModuleKeys.cs
// Purpose: Canonical license module-key constants. Single source of truth
//          for the strings passed to ILicenseManager.IsModuleEnabled.
//          See docs/licensing/module-catalog.md for the authoritative
//          catalog and tiering guidance.
//
// LOCKED: these constants are stable identifiers used in already-issued
// license files. Renaming any of them is a breaking change; add new
// constants for new modules instead.
// Reference: docs/licensing/module-catalog.md
//            docs/PHASE4_EXECUTION_PLAN.md Milestone G.7
// ============================================================================

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Canonical license module keys. Adapters, sinks, and packaging code
/// reference these constants instead of embedding string literals.
/// See <c>docs/licensing/module-catalog.md</c> for the authoritative
/// catalog and tiering guidance.
/// </summary>
public static class LicenseModuleKeys
{
    // ----- Core -----

    /// <summary>Base runtime. Always required for any data flow.</summary>
    public const string CoreRuntime = "core-runtime";

    // ----- Sinks -----

    /// <summary>MQTT publish sink (PerTag + Batch modes).</summary>
    public const string SinkMqtt = "sink-mqtt";

    /// <summary>OPC UA Server endpoint. Phase 4 Milestone H.</summary>
    public const string SinkOpcUaServer = "sink-opc-ua-server";

    /// <summary>HTTP push sink. Reserved — Phase 5.</summary>
    public const string SinkHttp = "sink-http";

    /// <summary>TCP push sink. Reserved — Phase 5.</summary>
    public const string SinkTcp = "sink-tcp";

    // ----- Sources -----

    /// <summary>Modbus TCP source adapter.</summary>
    public const string SourceModbusTcp = "source-modbus-tcp";

    /// <summary>
    /// Fanuc FOCAS2 source adapter. Customer must also have a Fanuc DLL
    /// license (distinct from this license module — see
    /// <c>docs/adapter-sdk/focas2-adapter.md</c>).
    /// </summary>
    public const string SourceFocas2 = "source-focas2";

    /// <summary>MTConnect source adapter.</summary>
    public const string SourceMtconnect = "source-mtconnect";

    /// <summary>
    /// Brother HTTP source adapter (Brother Speedio and other Brother CNCs
    /// via the built-in port-80 web-monitoring interface). No proprietary
    /// licenses required from Brother — this gates the module within the
    /// EdgeConnect license.
    /// </summary>
    public const string SourceBrotherHttp = "source-brother-http";

    /// <summary>Siemens S7 source adapter via Sharp7. Phase 4 Milestone I.</summary>
    public const string SourceS7 = "source-s7";

    /// <summary>OPC UA Client source adapter. Phase 4 Milestone J fork.</summary>
    public const string SourceOpcUaClient = "source-opc-ua-client";

    /// <summary>
    /// Allen-Bradley EtherNet/IP source adapter (ControlLogix / CompactLogix /
    /// GuardLogix / MicroLogix / Micro800) via libplctag.
    /// </summary>
    public const string SourceEthernetIp = "source-ethernet-ip";

    /// <summary>
    /// Mitsubishi MELSEC source adapter (SLMP / MC 3E binary over TCP).
    /// Hand-rolled wire layer, no third-party dependency (ADR-0033).
    /// </summary>
    public const string SourceMelsec = "source-melsec";

    // ----- Features / UI -----

    /// <summary>
    /// Connectivity Studio (management REST API + Blazor UI).
    /// Phase 4 Milestone M. Without this module the host runs headless.
    /// </summary>
    public const string ConnectivityStudio = "connectivity-studio";

    /// <summary>Future EREMOS V2 historian-direct integration. Reserved — Phase 5.</summary>
    public const string HistorianBridge = "historian-bridge";
}
