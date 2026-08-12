// ============================================================================
// File: HostStartup.cs
// Purpose: The IHostedService that walks the locked startup sequence and
//          its mirror-image shutdown. ANY change to the order requires a
//          blueprint revision plus an update to StartupOrderingTests.
//
//          Phase 2 scope: every locked phase is walked and observed via
//          IStartupSequenceObserver. The deferred phases (source supervisor,
//          metrics endpoint) record their phase entry via the observer so
//          the ordering contract is stable; the actual work lives in later
//          D phases.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D
// Milestone: D — phase 2.
//
// LOCKED contract:
//   * The sequence executes in StartupPhase numeric order on StartAsync
//     and the strict reverse on StopAsync.
//   * Each phase fires OnStartupPhase/OnShutdownPhase IMMEDIATELY BEFORE
//     doing the work for that phase. Failures inside a phase short-circuit
//     the rest of startup; the host fails fast with a typed error.
//   * No hidden side effects inside DI registration. The composition root
//     wires services; HostStartup is the only place that calls .InitializeAsync,
//     .LoadFromFileAsync, .RegisterRouteAsync, .StartAllAsync.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Host.Endpoints;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// The IHostedService that owns the locked startup/shutdown sequence.
/// Resolved exactly once per host instance.
/// </summary>
public sealed class HostStartup : IHostedService
{
    private readonly HostOptions _options;
    private readonly IConfigurationManager _configManager;
    private readonly ILicenseManager _licenseManager;
    private readonly FileSystemGatewayIdentity _gatewayIdentity;
    private readonly RuntimeDiagnosticsCollector _diagnostics;
    private readonly IRouteBufferFactory _bufferFactory;
    private readonly IRoutingEngine _routingEngine;
    private readonly IEnumerable<RouteDefinition> _routeDefinitions;
    private readonly SourceSupervisor _sourceSupervisor;
    private readonly SinkSupervisor _sinkSupervisor;
    private readonly RouteDefinitionFactory _routeDefinitionFactory;
    private readonly HostEndpointsServer _endpoints;
    private readonly IStartupSequenceObserver _observer;
    private readonly IHostReadinessGate _readiness;
    private readonly IConfigurationFaultRegistry? _faultRegistry;
    private readonly RuntimeReloadCoordinator? _reloadCoordinator;
    private readonly ILogger<HostStartup> _logger;

    private bool _started;

    /// <summary>Construct the startup orchestrator with all dependencies resolved by DI.</summary>
    public HostStartup(
        HostOptions options,
        IConfigurationManager configManager,
        ILicenseManager licenseManager,
        FileSystemGatewayIdentity gatewayIdentity,
        RuntimeDiagnosticsCollector diagnostics,
        IRouteBufferFactory bufferFactory,
        IRoutingEngine routingEngine,
        IEnumerable<RouteDefinition> routeDefinitions,
        SourceSupervisor sourceSupervisor,
        SinkSupervisor sinkSupervisor,
        RouteDefinitionFactory routeDefinitionFactory,
        HostEndpointsServer endpoints,
        IStartupSequenceObserver observer,
        IHostReadinessGate readiness,
        ILogger<HostStartup> logger,
        IConfigurationFaultRegistry? faultRegistry = null,
        RuntimeReloadCoordinator? reloadCoordinator = null)
    {
        _options = options;
        _configManager = configManager;
        _licenseManager = licenseManager;
        _gatewayIdentity = gatewayIdentity;
        _diagnostics = diagnostics;
        _bufferFactory = bufferFactory;
        _routingEngine = routingEngine;
        _routeDefinitions = routeDefinitions;
        _sourceSupervisor = sourceSupervisor;
        _sinkSupervisor = sinkSupervisor;
        _routeDefinitionFactory = routeDefinitionFactory;
        _endpoints = endpoints;
        _observer = observer;
        _readiness = readiness;
        _faultRegistry = faultRegistry;
        _reloadCoordinator = reloadCoordinator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Phases 1 and 2 happened OUTSIDE this hosted service (env parse +
        // container build). We still observe them so the contract is
        // testable end-to-end.
        _observer.OnStartupPhase(StartupPhase.ParseEnvironment);
        _observer.OnStartupPhase(StartupPhase.BuildContainer);

        _observer.OnStartupPhase(StartupPhase.LoadGatewayIdentity);
        await _gatewayIdentity.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Gateway identity resolved: {GatewayId}", _gatewayIdentity.GatewayId);

        _observer.OnStartupPhase(StartupPhase.LoadConfiguration);
        await _configManager.InitializeAsync(cancellationToken).ConfigureAwait(false);

        // M.2b.6.2 §3.B — surface which current.json the gateway just
        // loaded so operators can confirm at a glance whether
        // EDGECONNECT_DATA_ROOT is overriding the default path. This
        // mirrors the Studio Config page caption (Config.razor) so the
        // log and the UI agree.
        var layout = new ConfigurationStorageLayout(_options.ResolvedDataRoot);
        var configPathSource = Environment.GetEnvironmentVariable("EDGECONNECT_DATA_ROOT") is not null
            ? "env:EDGECONNECT_DATA_ROOT"
            : "default";
        _logger.LogInformation(
            "Configuration loaded from: {Path} (source: {Source})", layout.CurrentConfigPath, configPathSource);

        // Follow-up to ADR-0020 G5: probe that the data root is actually writable
        // by this process identity. An unwritable audit.log / current.json — e.g.
        // Windows ProgramData files created under an elevated context, leaving a
        // non-admin runtime user read-only — otherwise fails deep inside the first
        // audit-writing operation (config apply/rollback, diagnostic-bundle
        // generation) with a raw UnauthorizedAccessException. Fail-soft per
        // ADR-0003: the data pipeline keeps running; we surface a Gateway-level
        // fault + a prominent log so the operator can fix permissions. Detection
        // only — the runtime never repairs ACLs (that's installer/ops territory).
        var writability = DataRootWritabilityProbe.Probe(layout);
        if (!writability.IsWritable)
        {
            var detail = string.Join("; ", writability.Issues);
            _logger.LogError(
                "Gateway data root is not writable ({Code}): {Detail}. The data pipeline " +
                "keeps running, but config apply/rollback and diagnostic-bundle generation " +
                "will fail until this is fixed. See docs/ops-runbook.md § data-dir permissions.",
                CoreErrors.ConfigDataNotWritable, detail);
            _faultRegistry?.Register(new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Gateway,
                InstanceId = "data-root",
                ErrorCode = CoreErrors.ConfigDataNotWritable,
                Message = $"Data root not writable: {detail}",
                ObservedAtUtc = DateTime.UtcNow,
            });
        }

        // M.P2.1: drain any cross-record configuration faults observed at
        // DI-registration time (in EdgeConnectComposition's
        // Add*SourcesFromGatewayConfig calls) into the audit chain. The
        // registry was populated synchronously during composition; we can
        // only write the audit-chain entries now that IConfigurationManager
        // is initialized. The registry stays populated after drain — the
        // audit chain is the durable record, the registry is the live view.
        if (_faultRegistry is not null)
        {
            foreach (var fault in _faultRegistry.GetFaults())
            {
                // Gateway-level faults (e.g. data-root not writable) are
                // environmental, not config-content faults, and must not be
                // audit-drained — when the fault IS "audit log not writable" the
                // append would fail anyway. They stay in the registry for the
                // Studio and were already logged at Error above.
                if (fault.Kind == ConfigurationFaultKind.Gateway)
                {
                    continue;
                }

                _logger.LogWarning(
                    "Configuration fault on boot: {Kind} '{InstanceId}' — {Code}: {Message}",
                    fault.Kind, fault.InstanceId, fault.ErrorCode, fault.Message);
                try
                {
                    await _configManager.AppendRuntimeFaultAsync(fault, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Never let an audit-chain write failure escalate into a
                    // gateway crash — the fault is still in the in-memory
                    // registry and the operator will see it in the Studio.
                    _logger.LogError(ex,
                        "Failed to append RuntimeConfigurationFault audit entry for {Kind} '{InstanceId}'.",
                        fault.Kind, fault.InstanceId);
                }
            }
        }

        _observer.OnStartupPhase(StartupPhase.LoadLicense);
        if (File.Exists(_options.LicensePath))
        {
            await _licenseManager.LoadFromFileAsync(_options.LicensePath, cancellationToken)
                .ConfigureAwait(false);
        }

        _observer.OnStartupPhase(StartupPhase.ConstructDiagnostics);
        // Diagnostics is already constructed by DI; this phase exists so
        // operators can see it in logs and tests can pin it in the order.
        // Pre-register every route id from config so the snapshot surface
        // returns a non-null entry even before any event arrives.
        var current = await _configManager.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        foreach (var route in current.Routes)
        {
            _diagnostics.EnsureRoute(route.RouteId);
        }

        // Advisory (not a fault — the sources still come up): warn when two or
        // more enabled FOCAS2 sources target the SAME CNC endpoint. FANUC
        // controllers allow only a few simultaneous FOCAS clients, so co-located
        // sources can exhaust that limit and cause SOCKET_ERROR drops.
        foreach (var shared in Focas2RegistrationExtensions.FindSharedCncEndpoints(current))
        {
            _logger.LogWarning(
                "FOCAS2 connection-limit advisory: {Count} sources target the same CNC endpoint {Endpoint} ({SourceIds}). " +
                "FANUC controllers allow only a few simultaneous FOCAS clients; sharing one can cause SOCKET_ERROR drops. " +
                "Consider consolidating these sources or verifying the controller's connection limit.",
                shared.SourceIds.Count, shared.Endpoint, string.Join(", ", shared.SourceIds));
        }

        _observer.OnStartupPhase(StartupPhase.ConstructBufferFactory);
        // Buffer factory is constructed by DI; phase exists for ordering.
        _ = _bufferFactory;

        _observer.OnStartupPhase(StartupPhase.RegisterRoutes);
        // Phase 6: correlate config + supervised intakes + sink registry
        // via the factory. The legacy IEnumerable<RouteDefinition> path
        // is kept as a fallback for tests that pre-build their routes.
        var sinkLookup = new Dictionary<string, ISinkAdapter>(StringComparer.Ordinal);
        foreach (var reg in _sinkSupervisor.Registrations)
        {
            sinkLookup[reg.Adapter.InstanceId] = reg.Adapter;
        }
        var factoryRoutes = _routeDefinitionFactory.Build(current, _sourceSupervisor, sinkLookup, _faultRegistry);

        // M.P2.1: drain any route-level faults the factory just produced
        // (missing-source / missing-sink). These were observed against the
        // current version too, so they belong in the audit chain.
        if (_faultRegistry is not null)
        {
            foreach (var fault in _faultRegistry.GetFaultsFor(ConfigurationFaultKind.Route))
            {
                _logger.LogWarning(
                    "Route fault on boot: '{RouteId}' — {Code}: {Message}",
                    fault.InstanceId, fault.ErrorCode, fault.Message);
                try
                {
                    await _configManager.AppendRuntimeFaultAsync(fault, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to append RuntimeConfigurationFault audit entry for route '{RouteId}'.",
                        fault.InstanceId);
                }
            }
        }
        foreach (var def in factoryRoutes)
        {
            await _routingEngine.RegisterRouteAsync(def, cancellationToken).ConfigureAwait(false);
        }
        // Preserve the legacy path so tests that inject explicit definitions
        // still work. Phase 6 scenarios use the factory path only.
        foreach (var def in _routeDefinitions)
        {
            await _routingEngine.RegisterRouteAsync(def, cancellationToken).ConfigureAwait(false);
        }

        // Phase 9 (StartSourceSupervisor): the locked enum value is named
        // for the source supervisor but the phase covers BOTH the sink
        // supervisor and the source supervisor. Sinks come up first so
        // they're ready to receive when the source pumps start.
        _observer.OnStartupPhase(StartupPhase.StartSourceSupervisor);
        await _sinkSupervisor.StartAsync(cancellationToken).ConfigureAwait(false);
        await _sourceSupervisor.StartAsync(cancellationToken).ConfigureAwait(false);

        _observer.OnStartupPhase(StartupPhase.StartRoutingEngine);
        await _routingEngine.StartAllAsync(cancellationToken).ConfigureAwait(false);

        _observer.OnStartupPhase(StartupPhase.MarkReady);
        _readiness.MarkReady();

        // M.P2.2 phase 2.c: subscribe the hot-reload coordinator AFTER
        // MarkReady so the gateway doesn't react to a CurrentChanged
        // event mid-boot. (The IConfigurationManager contract says
        // CurrentChanged doesn't fire during InitializeAsync; this
        // ordering is belt-and-braces.) Subscribe is idempotent;
        // unsubscribe runs in the mirror-image position on shutdown.
        _reloadCoordinator?.Subscribe();

        _observer.OnStartupPhase(StartupPhase.StartMetricsEndpoint);
        if (_options.EnableEndpointsServer)
        {
            await _endpoints.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        _started = true;
        _logger.LogInformation("Host startup complete; readiness gate open.");
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return;
        }
        _started = false;
        _logger.LogInformation("Host shutdown beginning; readiness gate closing.");

        // Strict reverse of the startup sequence. Each phase fires its
        // observer hook BEFORE the teardown work, mirroring startup.
        _observer.OnShutdownPhase(StartupPhase.StartMetricsEndpoint);
        if (_options.EnableEndpointsServer)
        {
            await _endpoints.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _observer.OnShutdownPhase(StartupPhase.MarkReady);
        // M.P2.2 phase 2.c: unsubscribe BEFORE MarkNotReady. Any
        // CurrentChanged event after this point is silently ignored;
        // the coordinator's DisposeAsync (driven by DI) drains
        // in-flight reconciliation with a 5s ceiling.
        _reloadCoordinator?.Unsubscribe();
        _readiness.MarkNotReady();

        _observer.OnShutdownPhase(StartupPhase.StartRoutingEngine);
        await _routingEngine.StopAllAsync(cancellationToken).ConfigureAwait(false);

        // Mirror startup: stop sources first (so the channels close before
        // the sinks tear down), then sinks.
        _observer.OnShutdownPhase(StartupPhase.StartSourceSupervisor);
        await _sourceSupervisor.StopAsync(cancellationToken).ConfigureAwait(false);
        await _sinkSupervisor.StopAsync(cancellationToken).ConfigureAwait(false);

        _observer.OnShutdownPhase(StartupPhase.RegisterRoutes);
        // Routes are owned by the engine and disposed when the engine is
        // disposed by the DI container. Nothing extra to do here.

        _observer.OnShutdownPhase(StartupPhase.ConstructBufferFactory);
        // Buffer disposal is also handled by the engine via Route.DisposeAsync.

        _observer.OnShutdownPhase(StartupPhase.ConstructDiagnostics);
        // Diagnostics collector is disposed by the DI container.

        _observer.OnShutdownPhase(StartupPhase.LoadLicense);
        // License manager is disposed by the DI container.

        _observer.OnShutdownPhase(StartupPhase.LoadConfiguration);
        // Configuration manager is disposed by the DI container.

        _observer.OnShutdownPhase(StartupPhase.LoadGatewayIdentity);
        _observer.OnShutdownPhase(StartupPhase.BuildContainer);
        _observer.OnShutdownPhase(StartupPhase.ParseEnvironment);
    }

}
