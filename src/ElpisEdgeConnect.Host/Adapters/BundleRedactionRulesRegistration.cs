// ============================================================================
// File: Adapters/BundleRedactionRulesRegistration.cs
// Purpose: Registers every per-protocol IBundleRedactionRules into DI so the
//          Management-side BundleRedactionRulesRegistry can compose them.
//
//          UNCONDITIONAL by design: unlike adapter registration (which is
//          license-gated and config-driven), redaction rules are pure metadata
//          and MUST always be available. The diagnostic-bundle / backup redactor
//          has to know how to redact ANY protocol's connection block it finds in
//          gateway.json — even for a protocol that is configured but unlicensed,
//          or disabled. Gating these on license/config would let a configured-
//          but-unlicensed protocol's secrets fall through to baseline-only
//          classification (with a missing-rules warning) instead of its precise
//          per-protocol rules.
//
//          Lives in Host because Host is the one project that references every
//          adapter module. The rules themselves carry no Management reference
//          (metadata-only; see ADR-0020 M-B plan v2 §3a).
// Reference: docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md §3, §6 (B5).
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sinks.Mqtt;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using ElpisEdgeConnect.Sources.BrotherHttp;
using ElpisEdgeConnect.Sources.EthernetIp;
using ElpisEdgeConnect.Sources.Focas2;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.ModbusTcp;
using ElpisEdgeConnect.Sources.MTConnect;
using ElpisEdgeConnect.Sources.OpcUaClient;
using ElpisEdgeConnect.Sources.S7;
using Microsoft.Extensions.DependencyInjection;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Registers the per-protocol <see cref="IBundleRedactionRules"/> implementations
/// unconditionally. The Management-side redaction registry resolves
/// <c>IEnumerable&lt;IBundleRedactionRules&gt;</c> and composes them over the
/// shared baseline.
/// </summary>
public static class BundleRedactionRulesRegistration
{
    /// <summary>
    /// Append every protocol's redaction rules as <see cref="IBundleRedactionRules"/>
    /// singletons. Always called from the composition root, independent of
    /// license state or configured instances.
    /// </summary>
    public static IServiceCollection AddBundleRedactionRules(this IServiceCollection services)
    {
        System.ArgumentNullException.ThrowIfNull(services);

        // Sinks
        services.AddSingleton<IBundleRedactionRules, MqttBundleRedactionRules>();
        services.AddSingleton<IBundleRedactionRules, OpcUaServerBundleRedactionRules>();

        // Sources
        services.AddSingleton<IBundleRedactionRules, OpcUaClientBundleRedactionRules>();
        services.AddSingleton<IBundleRedactionRules, Focas2BundleRedactionRules>();
        services.AddSingleton<IBundleRedactionRules, MTConnectBundleRedactionRules>();
        services.AddSingleton<IBundleRedactionRules, BrotherHttpBundleRedactionRules>();
        services.AddSingleton<IBundleRedactionRules, ModbusTcpBundleRedactionRules>();
        services.AddSingleton<IBundleRedactionRules, S7BundleRedactionRules>();
        services.AddSingleton<IBundleRedactionRules, EthernetIpBundleRedactionRules>();
        services.AddSingleton<IBundleRedactionRules, MelsecBundleRedactionRules>();

        return services;
    }
}
