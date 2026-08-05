// ============================================================================
// File: Licensing/LicenseEditionCatalog.cs
// Purpose: Central policy for which SOURCE / SINK protocols each license
//          edition is allowed to OFFER (i.e. which protocol tiles the operator
//          may even pick). This is the single source of truth shared by the
//          Studio protocol pickers and the License Generator so both agree.
//
//          Offering matrix (Unknown = dev / no license loaded = everything):
//
//            Edition       Sources                 Destinations
//            ----------    --------------------    ----------------------
//            Starter       Modbus TCP only         MQTT only
//            Professional  all                     MQTT only
//            Enterprise    all                     MQTT + OPC UA Server
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

    /// <summary>The single SINK module Starter and Professional offer.</summary>
    public const string StarterSinkModuleKey = LicenseModuleKeys.SinkMqtt;

    /// <summary>
    /// True when the edition restricts which SOURCE protocols may be offered.
    /// Only Starter restricts sources (Modbus TCP only); Professional / Enterprise
    /// / Unknown offer the full source catalogue.
    /// </summary>
    public static bool RestrictsSources(LicenseEdition edition) =>
        edition == LicenseEdition.Starter;

    /// <summary>
    /// True when the edition restricts which DESTINATION protocols may be offered.
    /// Every named edition restricts sinks (Starter / Professional → MQTT only;
    /// Enterprise → MQTT + OPC UA Server); only Unknown (dev) offers everything.
    /// </summary>
    public static bool RestrictsSinks(LicenseEdition edition) =>
        edition != LicenseEdition.Unknown;

    /// <summary>
    /// True when the given SOURCE module key may be offered under the edition.
    /// Starter offers only Modbus TCP; every other edition offers everything.
    /// </summary>
    public static bool IsSourceModuleOffered(LicenseEdition edition, string moduleKey) =>
        !RestrictsSources(edition)
        || string.Equals(moduleKey, StarterSourceModuleKey, StringComparison.Ordinal);

    /// <summary>
    /// True when the given SINK module key may be offered under the edition:
    /// Starter / Professional → MQTT only; Enterprise → MQTT + OPC UA Server;
    /// Unknown (dev) → everything.
    /// </summary>
    public static bool IsSinkModuleOffered(LicenseEdition edition, string moduleKey) => edition switch
    {
        LicenseEdition.Enterprise =>
            string.Equals(moduleKey, LicenseModuleKeys.SinkMqtt, StringComparison.Ordinal)
            || string.Equals(moduleKey, LicenseModuleKeys.SinkOpcUaServer, StringComparison.Ordinal),
        LicenseEdition.Professional or LicenseEdition.Starter =>
            string.Equals(moduleKey, LicenseModuleKeys.SinkMqtt, StringComparison.Ordinal),
        _ => true, // Unknown / dev — offer everything
    };
}
