// ============================================================================
// File: CompositionRoot.cs
// Purpose: The DI registration extension method that wires every Phase 1
//          service into the host's IServiceCollection. Single file, single
//          method — anyone debugging the host's wiring opens THIS file
//          first. There is no DI scattered across other files.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D, ARCHITECTURE_BLUEPRINT.md §8
// Milestone: D — phase 2.
//
// LOCKED rules (per the D pre-implementation plan):
//   * No hidden side effects inside registrations. Constructors are
//     trivial; the locked startup sequence (HostStartup) does the
//     non-trivial work.
//   * Singletons for collector / engine / managers — there is exactly
//     ONE of each per gateway.
//   * The composition root takes a populated HostOptions. It does NOT
//     parse environment variables; that's the caller's job.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Core.Pipeline;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Host.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods that wire the EdgeConnect host. The single
/// composition entry point is <see cref="AddElpisEdgeConnectHost"/>.
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Register every Phase 1 service into <paramref name="services"/>.
    /// Idempotent: calling twice with the same options is a no-op for
    /// already-registered services.
    /// </summary>
    public static IServiceCollection AddElpisEdgeConnectHost(
        this IServiceCollection services,
        HostOptions options)
    {
        // ----- Construction-time options -----
        services.AddSingleton(options);

        // ----- Diagnostics (C4) -----
        // RuntimeDiagnosticsCollector implements IRoutingEngineDiagnostics,
        // ITransformMetricsRecorder, ISourceHealthSink, ISinkHealthSink, and
        // IDiagnosticsService against ONE state store. We register the
        // concrete type as singleton and bind every interface to it so DI
        // resolution returns the same instance for every typed seam.
        services.AddSingleton<RuntimeDiagnosticsCollector>();
        services.AddSingleton<IRoutingEngineDiagnostics>(sp => sp.GetRequiredService<RuntimeDiagnosticsCollector>());
        services.AddSingleton<ITransformMetricsRecorder>(sp => sp.GetRequiredService<RuntimeDiagnosticsCollector>());
        services.AddSingleton<ISourceHealthSink>(sp => sp.GetRequiredService<RuntimeDiagnosticsCollector>());
        services.AddSingleton<ISinkHealthSink>(sp => sp.GetRequiredService<RuntimeDiagnosticsCollector>());
        services.AddSingleton<IBufferHealthSink>(sp => sp.GetRequiredService<RuntimeDiagnosticsCollector>());
        services.AddSingleton<ISinkSessionHealthSink>(sp => sp.GetRequiredService<RuntimeDiagnosticsCollector>());
        services.AddSingleton<IDiagnosticsService>(sp => sp.GetRequiredService<RuntimeDiagnosticsCollector>());

        // ----- Configuration faults (M.P2.1) -----
        // Live in-memory registry of cross-record validation failures
        // observed at gateway startup (and at M.P2.2 hot-reload). Read-only
        // from the Studio / management API; populated by the protocol
        // registration extensions and RouteDefinitionFactory.Build during
        // composition, drained to the audit chain by HostStartup once
        // IConfigurationManager.InitializeAsync completes.
        services.AddSingleton<IConfigurationFaultRegistry, ConfigurationFaultRegistry>();

        // ----- Configuration (B2) -----
        // ConfigurationStorageLayout is rooted at the gateway DATA
        // directory (the parent of config/). Passing ConfigDirectory
        // directly produced a doubled-config-dir path bug —
        // resolved-data-root keeps the layout's invariant
        // intact regardless of whether the caller supplied DataRoot
        // explicitly or only ConfigDirectory.
        services.AddSingleton<IConfigurationStore>(_ =>
            new FileSystemConfigurationStore(
                new ConfigurationStorageLayout(options.ResolvedDataRoot)));
        services.AddSingleton<IConfigurationManager>(sp =>
            new ConfigurationManager(sp.GetRequiredService<IConfigurationStore>()));
        // The same ConfigurationManager instance also owns the gateway audit chain
        // for non-config events (ADR-0020 Rule 5 — BUNDLE.GENERATED).
        services.AddSingleton<IGatewayAuditWriter>(sp =>
            (IGatewayAuditWriter)sp.GetRequiredService<IConfigurationManager>());

        // ----- Licensing (B3) -----
        // Bound to this gateway's identity (ADR-0036): a license issued for a
        // different gateway fails to load. EdgeConnectComposition replaces this
        // with an eagerly-loaded instance sharing the same binding provider.
        services.AddSingleton<ILicenseManager>(_ => new LicenseManager(
            () => FileSystemGatewayIdentity.TryReadPersisted(options.GatewayIdentityPath)));

        // ----- Gateway identity (locked decision #19) -----
        // Singleton backing the IGatewayIdentity contract. Construction is
        // trivial; the real read/create-on-first-start work runs inside
        // HostStartup during the LoadGatewayIdentity phase (phase 3).
        services.AddSingleton<FileSystemGatewayIdentity>(_ =>
            new FileSystemGatewayIdentity(options.GatewayIdentityPath));
        services.AddSingleton<IGatewayIdentity>(sp =>
            sp.GetRequiredService<FileSystemGatewayIdentity>());

        // ----- Buffer factory (C2/C3) -----
        // Chip 4 (Bug 1 P3) fix: the factory takes the DATA root, not the
        // config directory. Pre-fix, buffer files landed at
        // {dataRoot}/config/buffer/{routeId}.db instead of the canonical
        // {dataRoot}/buffer/{routeId}.db. The factory's migration shim
        // moves any pre-existing .db + .db-shm + .db-wal triplet from the
        // legacy path to the canonical path on first open after upgrade.
        // The factory is given the diagnostics seam so each StoreAndForward
        // buffer can report points it cannot serialize as a structured
        // RoutePointQuarantinedEvent (quarantine-and-continue) rather than
        // letting one bad point silently strand the route.
        services.AddSingleton<IRouteBufferFactory>(sp =>
            new DefaultRouteBufferFactory(
                options.ResolvedDataRoot,
                sp.GetRequiredService<IRoutingEngineDiagnostics>()));

        // ----- Live Data Tap (ADR-0018, demand-driven per ADR-0017) -----
        // Strictly observational capture service for the Stream/Compare/Inspect
        // tap. Captured values are masked at capture time against a LIVE
        // sensitive-tag policy (ADR-0018A) — the policy is rebuilt on every
        // config reload so a privacy control can never go stale.
        services.AddSingleton<IRouteTap>(sp =>
        {
            var cfgMgr = sp.GetRequiredService<IConfigurationManager>();
            var provider = new SensitiveTagPolicyProvider();
            try
            {
                var cfg = cfgMgr.GetCurrentAsync(default).AsTask().GetAwaiter().GetResult();
                provider.Set(cfg.Gateway.SensitiveTags);
            }
            catch
            {
                // Config not loaded yet — CurrentChanged populates it before
                // routes start capturing. Default (empty) masks nothing.
            }
            cfgMgr.CurrentChanged += (_, e) => provider.Set(e.NewConfiguration.Gateway.SensitiveTags);

            var masker = new TapValueMasker(() => provider.Current);
            return new RouteTap(masker: masker.Mask);
        });

        // ----- Routing engine (C3) -----
        services.AddSingleton<IRoutingEngine>(sp =>
            new RoutingEngine(
                sp.GetRequiredService<IRouteBufferFactory>(),
                sp.GetRequiredService<IRoutingEngineDiagnostics>(),
                sp.GetRequiredService<IBufferHealthSink>(),
                sp.GetRequiredService<IRouteTap>()));

        // ----- Route definitions (populated by D phase 6 factory) -----
        // Phases 2-4: empty enumerable so HostStartup.RegisterRoutes is a no-op.
        // Phase 6 will replace this with a real factory that materializes
        // RouteDefinitions from the loaded configuration + the supervised
        // source intakes.
        services.AddSingleton<IEnumerable<RouteDefinition>>(_ => new List<RouteDefinition>());

        // ----- Adapter supervisors (D phase 4) -----
        // Empty default registrations; tests/phase 6 inject real adapters.
        services.AddSingleton<IEnumerable<SourceRegistration>>(_ => new List<SourceRegistration>());
        services.AddSingleton<IEnumerable<SinkRegistration>>(_ => new List<SinkRegistration>());
        services.AddSingleton<SourceSupervisor>();
        services.AddSingleton<ISupervisedSourceRegistry>(sp => sp.GetRequiredService<SourceSupervisor>());
        services.AddSingleton<SinkSupervisor>();
        services.AddSingleton<RouteDefinitionFactory>();

        // ----- Diagnostic-bundle redaction rules (ADR-0020 M-B, B5) -----
        // Registered UNCONDITIONALLY (not license-gated): the redactor must be
        // able to redact any protocol's connection block found in gateway.json,
        // even a configured-but-unlicensed one. The Management redaction registry
        // composes these over the shared baseline.
        services.AddBundleRedactionRules();

        // ----- M.P2.2 phase 2.b: per-instance registration factory -----
        // Stateless dispatcher consulting per-protocol IsXxxProtocol
        // helpers + BuildSource/BuildSink. Used by the coordinator
        // (phase 2.c) below; not exercised at boot.
        services.AddSingleton<IRegistrationFactory, RegistrationFactory>();

        // ----- M.P2.2 phase 3: reconcile-outcome correlation channel -----
        // Bounded in-memory registry (capacity 64) that the coordinator
        // publishes ReloadOutcome records to and the Management apply
        // endpoint awaits on. Guardrails K-N (phase 3 plan v2 §2):
        // bounded, non-blocking, observation-only, process-lifetime.
        // Must be registered before the coordinator so DI hands it in.
        services.AddSingleton<IReloadOutcomeRegistry, ReloadOutcomeRegistry>();

        // ----- M.P2.2 phase 2.c: hot-reload coordinator -----
        // Subscribes to IConfigurationManager.CurrentChanged after
        // MarkReady (driven by HostStartup). Classifies the diff via
        // RuntimeReloadClassifier, drives supervisors + routing engine
        // in the locked stop/start order, registers/clears faults.
        // Locked threading: the CurrentChanged handler hops off the
        // firing thread via Task.Run; reconciliation runs on its own
        // SemaphoreSlim(1,1), NEVER blocking the apply mutex.
        services.AddSingleton<RuntimeReloadCoordinator>();

        // ----- Diagnostics meters (C4 phase 5) -----
        // One DiagnosticsMeters per gateway. It owns a single
        // System.Diagnostics.Metrics.Meter against the C4 constants,
        // registers observable instruments that read live from the
        // collector, and exposes its Meter so the endpoints server can
        // scrape it.
        services.AddSingleton<DiagnosticsMeters>(sp =>
            new DiagnosticsMeters(sp.GetRequiredService<IDiagnosticsService>()));

        // ----- Health / readiness / metrics endpoints (D phase 5) -----
        services.AddSingleton<HostEndpointsServer>(sp => new HostEndpointsServer(
            sp.GetRequiredService<IHostReadinessGate>(),
            sp.GetRequiredService<DiagnosticsMeters>().Meter,
            options.EndpointsListenUrl,
            sp.GetRequiredService<ILogger<HostEndpointsServer>>()));

        // ----- Host-only services -----
        services.AddSingleton<IHostReadinessGate, HostReadinessGate>();
        services.AddSingleton<IStartupSequenceObserver>(NullStartupSequenceObserver.Instance);

        // ----- The hosted service that walks the locked startup sequence -----
        services.AddHostedService<HostStartup>();

        // ----- H.2: SinkSessionPoller -----
        // Periodically reads every ISessionTrackingSink and pushes the
        // active-session snapshot to the diagnostics surface. Lives in the
        // host (not Core) so Core stays protocol-agnostic. Idempotent at
        // zero sinks (just sits in an infinite wait until shutdown).
        services.AddHostedService<SinkSessionPoller>();

        // ----- Unlicensed demo cutoff (ADR-0035) -----
        // OVERRIDES locked decision #7: when the license status is not Valid
        // (NotLoaded / InGracePeriod / Expired / Invalid) for longer than the
        // trial window (default 2h, EDGECONNECT_LICENSE_TRIAL_MINUTES), STOP data
        // collection (sources + sinks) — the host/UI keeps running so an operator
        // can activate a license. LicenseTrialState exposes the countdown to the
        // UI banner. Registered here so it applies to both the headless Host and
        // the Management/Studio service (both build this composition).
        services.AddSingleton<LicenseTrialState>();
        services.AddHostedService(sp => new LicenseTrialEnforcer(
            sp.GetRequiredService<ILicenseManager>(),
            sp.GetRequiredService<SourceSupervisor>(),
            sp.GetRequiredService<SinkSupervisor>(),
            sp.GetRequiredService<LicenseTrialState>(),
            sp.GetRequiredService<IDiagnosticsService>(),
            sp.GetRequiredService<ILogger<LicenseTrialEnforcer>>(),
            // Watched so that deleting / tampering with the license file reverts
            // the runtime to demo mode immediately, not just at the next restart.
            licensePath: options.LicensePath,
            // Monotonic clock anchor — defeats "wind the date back to un-expire
            // the license" by refusing to honour it when the clock moves backwards.
            clockAnchors: new ClockAnchorStore(
                options.ResolvedDataRoot,
                sp.GetRequiredService<ILogger<LicenseTrialEnforcer>>()),
            trialDuration: LicenseTrialEnforcer.ResolveTrialDuration()));

        return services;
    }
}
