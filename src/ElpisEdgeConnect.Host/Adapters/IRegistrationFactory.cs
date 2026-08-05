// ============================================================================
// File: Adapters/IRegistrationFactory.cs
// Purpose: Stateless per-instance registration builder used by the M.P2.2
//          hot-reload coordinator (phase 2.c) to construct a
//          SourceRegistration / SinkRegistration for ONE configuration
//          instance at apply-time, without going through DI.
//
//          The DI extension methods (Add{Protocol}SourcesFromGatewayConfig
//          / Add{Protocol}SinksFromGatewayConfig) continue to drive boot-
//          time registration; this interface gives the coordinator a
//          parallel path for runtime reconciliation that shares the same
//          per-protocol construction helpers.
//
// LOCKED design rules (per docs/sessions/2026-05-16-mp22-phase2b-plan.md):
//   * Stateless. No mutable state. No service-locator behaviour. All
//     dependencies flow in via method parameters.
//   * The factory ASSUMES the caller has filtered out disabled instances.
//     Disabled is operator intent, not a fault — the factory must never
//     observe Enabled == false.
//   * null return is the unified "this instance will not be supervised"
//     signal: license-disabled (no fault), no-route (fault registered),
//     or unrecognised protocol (logged warning). Exceptions from adapter
//     construction propagate — the coordinator's TryWithFaultAsync
//     wraps the call site.
// Reference: docs/sessions/2026-05-16-mp22-phase2b-plan.md
//            docs/decisions/0009-runtime-hot-reload-instance-granularity.md
// Milestone: M.P2.2 phase 2.b
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Stateless per-instance registration builder. The hot-reload
/// coordinator (M.P2.2 phase 2.c) calls this to construct a
/// <see cref="SourceRegistration"/> / <see cref="SinkRegistration"/>
/// for ONE configuration instance at apply-time, without going through
/// DI.
/// </summary>
public interface IRegistrationFactory
{
    /// <summary>
    /// Build a <see cref="SourceRegistration"/> for the given source
    /// instance.
    /// </summary>
    /// <param name="src">
    /// Enabled source instance config. The factory ASSUMES the caller
    /// has filtered disabled instances — disabled is operator intent,
    /// not a fault. Violating this assumption is a contract bug.
    /// </param>
    /// <param name="gateway">Gateway-level settings.</param>
    /// <param name="routeIdSelector">
    /// Resolves a source instance id to its referencing route id.
    /// Returns null when no enabled route references the source — in
    /// which case the factory registers
    /// <c>CONFIG.SOURCE_WITHOUT_ROUTE</c> in <paramref name="faultRegistry"/>
    /// and returns null.
    /// </param>
    /// <param name="license">License manager (optional in dev/test).</param>
    /// <param name="faultRegistry">Where to register cross-record validation faults.</param>
    /// <param name="serviceProvider">For resolving <c>ILoggerFactory</c> + <c>IGatewayIdentity</c>.</param>
    /// <returns>
    /// A <see cref="SourceRegistration"/> ready to hand to
    /// <c>SourceSupervisor.AddAsync</c>, or null when the protocol is
    /// unrecognised, the license module is disabled, OR a cross-record
    /// fault was registered.
    /// </returns>
    SourceRegistration? BuildSource(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider serviceProvider);

    /// <summary>
    /// Build a <see cref="SinkRegistration"/> for the given sink
    /// instance. Same semantics as <see cref="BuildSource"/>.
    /// </summary>
    SinkRegistration? BuildSink(
        SinkInstanceConfig sink,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider serviceProvider);
}
