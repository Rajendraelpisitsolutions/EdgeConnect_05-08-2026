// ============================================================================
// File: Licensing/LicenseEditionCatalog.cs
// Purpose: Central policy for which SOURCE / SINK protocols each license
//          edition is allowed to OFFER (i.e. which protocol tiles the operator
//          may even pick). This is the single source of truth shared by the
//          Studio protocol pickers and the License Generator so both agree.
//
//          Offering matrix (Unknown = dev / no license loaded = everything):
//
//            Edition       Sources                                Destinations
//            ----------    -----------------------------------    ------------------
//            Unknown(dev)  all                                    all
//            Starter       Modbus TCP only                        MQTT only
//            Professional  all EXCEPT OPC UA Client               MQTT only
//            CncPro        FOCAS2 + Brother HTTP + MTConnect only  MQTT only
//            Enterprise    all                                    MQTT + OPC UA Server
//            TrialPeriod   all                                    all
//
//          NOTE: this governs what is *offered/selectable*. The authoritative
//          runtime entitlement is still LicenseInfo.Modules checked by
//          LicenseManager.IsModuleEnabled — a restricted-edition license simply
//          never has the other modules written into it (the generator honours
//          this policy).
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Edition-to-offered-protocol policy. Pure and side-effect free. See the
/// offering matrix in the file header.
/// </summary>
public static class LicenseEditionCatalog
{
    /// <summary>The single SOURCE module Starter offers.</summary>
    public const string StarterSourceModuleKey = LicenseModuleKeys.SourceModbusTcp;

    /// <summary>The single SINK module Starter, Professional, and CNC Pro offer.</summary>
    public const string StarterSinkModuleKey = LicenseModuleKeys.SinkMqtt;

    // OPC UA Client's REAL runtime module key. NOTE: the Core constant
    // LicenseModuleKeys.SourceOpcUaClient is "source-opc-ua-client" (different
    // hyphenation), but the OPC UA Client adapter, the issued licenses, and the
    // Studio pickers all use "source-opcua-client" (see
    // OpcUaClientSourceConfiguration.LicenseModuleKey). Compare against this
    // literal when excluding OPC UA Client — do NOT swap in the mismatched constant.
    private const string OpcUaClientSourceModuleKey = "source-opcua-client";

    /// <summary>
    /// True when the edition restricts which SOURCE protocols may be offered.
    /// Starter (Modbus TCP only) and CNC Pro (FOCAS2 / Brother HTTP / MTConnect)
    /// limit the source set; Enterprise / TrialPeriod / Unknown offer the full
    /// source catalogue.
    /// <para>
    /// Professional also drops OPC UA Client, yet this method returns
    /// <c>false</c> for it on purpose: the License Generator uses this flag as a
    /// "Modbus-TCP-only" gate, so returning <c>true</c> would wrongly collapse
    /// Professional to Modbus. Professional's OPC UA Client exclusion is enforced
    /// via <see cref="IsSourceModuleOffered"/> instead (the Studio source picker
    /// and the generator both consult that method per protocol).
    /// </para>
    /// </summary>
    public static bool RestrictsSources(LicenseEdition edition) =>
        edition is LicenseEdition.Starter or LicenseEdition.CncPro;

    /// <summary>
    /// True when the edition restricts which DESTINATION protocols may be offered.
    /// Every named edition restricts sinks (Starter / Professional / CNC Pro →
    /// MQTT only; Enterprise → MQTT + OPC UA Server); only TrialPeriod and Unknown
    /// (dev) offer everything.
    /// </summary>
    public static bool RestrictsSinks(LicenseEdition edition) =>
        edition != LicenseEdition.Unknown && edition != LicenseEdition.TrialPeriod;

    /// <summary>
    /// True when the given SOURCE module key may be offered under the edition:
    /// Starter → Modbus TCP only; CNC Pro → FOCAS2 / Brother HTTP / MTConnect;
    /// Professional → everything except OPC UA Client; Enterprise / TrialPeriod /
    /// Unknown (dev) → everything.
    /// </summary>
    public static bool IsSourceModuleOffered(LicenseEdition edition, string moduleKey) => edition switch
    {
        LicenseEdition.Starter =>
            string.Equals(moduleKey, StarterSourceModuleKey, StringComparison.Ordinal),
        LicenseEdition.CncPro =>
            string.Equals(moduleKey, LicenseModuleKeys.SourceFocas2, StringComparison.Ordinal)
            || string.Equals(moduleKey, LicenseModuleKeys.SourceBrotherHttp, StringComparison.Ordinal)
            || string.Equals(moduleKey, LicenseModuleKeys.SourceMtconnect, StringComparison.Ordinal),
        LicenseEdition.Professional =>
            !string.Equals(moduleKey, OpcUaClientSourceModuleKey, StringComparison.Ordinal),
        _ => true, // Enterprise / TrialPeriod / Unknown (dev) — offer everything
    };

    /// <summary>
    /// True when the given SINK module key may be offered under the edition:
    /// Starter / Professional / CNC Pro → MQTT only; Enterprise → MQTT + OPC UA
    /// Server; TrialPeriod / Unknown (dev) → everything.
    /// </summary>
    public static bool IsSinkModuleOffered(LicenseEdition edition, string moduleKey) => edition switch
    {
        LicenseEdition.Enterprise =>
            string.Equals(moduleKey, LicenseModuleKeys.SinkMqtt, StringComparison.Ordinal)
            || string.Equals(moduleKey, LicenseModuleKeys.SinkOpcUaServer, StringComparison.Ordinal),
        LicenseEdition.Professional or LicenseEdition.Starter or LicenseEdition.CncPro =>
            string.Equals(moduleKey, LicenseModuleKeys.SinkMqtt, StringComparison.Ordinal),
        _ => true, // TrialPeriod / Unknown (dev) — offer everything
    };
}
