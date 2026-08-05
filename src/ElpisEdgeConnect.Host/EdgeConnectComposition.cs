// ============================================================================
// File: EdgeConnectComposition.cs
// Purpose: Shared composition root for the EdgeConnect runtime —
//          identical service registration sequence whether the
//          entry point is:
//             * Host.exe (headless worker; src/ElpisEdgeConnect.Host)
//             * Management.exe (Studio + runtime; src/ElpisEdgeConnect.Management)
//
//          Refactored out of Host's Program.cs in M.1b.1 so the
//          Management project's WebApplicationBuilder can wire the
//          same runtime without duplicating the env-var parsing,
//          eager config + license loading, and protocol adapter
//          registrations.
//
//          Locked rules preserved:
//             * Eager config/license pre-load before container build
//               (so adapter G.7 license gates run with real state).
//             * Per-adapter isolation (one broken module never gates
//               another module's registration).
//             * Permissive fallback when no license file is on disk
//               (dev / sim / soak runs).
// Reference: docs/PHASE1_EXECUTION_PLAN.md Milestone D
//            docs/PHASE4_EXECUTION_PLAN.md Milestone M.1b
// ============================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.Focas2;
using ElpisEdgeConnect.Sources.S7;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Result of the EdgeConnect composition step — the resolved host
/// options plus the eagerly-loaded license manager and (optional)
/// gateway configuration. Caller passes these to downstream
/// wire-ups (e.g. <c>AddConnectivityStudio(license: result.License)</c>).
/// </summary>
public sealed record EdgeConnectCompositionResult(
    HostOptions Options,
    LicenseManager License,
    GatewayConfiguration? PreloadedConfig);

/// <summary>
/// Shared service registration for the EdgeConnect runtime. Called
/// by both the worker-mode (<c>Host.exe</c>) and web-mode
/// (<c>Management.exe</c>) entry points.
/// </summary>
public static class EdgeConnectComposition
{
    /// <summary>
    /// Wire the full runtime into <paramref name="services"/>. The
    /// returned <see cref="EdgeConnectCompositionResult"/> exposes the
    /// loaded license + config so the caller can:
    ///  * pass <c>result.License</c> to <c>AddConnectivityStudio</c> for
    ///    the M.1a license gate;
    ///  * gate additional downstream extensions on <c>result.PreloadedConfig</c>.
    /// </summary>
    public static async Task<EdgeConnectCompositionResult> ConfigureRuntimeAsync(
        IServiceCollection services,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ----- Pre-Phase 0: catch any unobserved Task exception in the
        // whole process. Bug 2 (P0) follow-up: the RoutingEngine now
        // wraps its worker task in a try/catch + Failed transition
        // (RoutingEngine.ObserveWorkerFault), so worker-task death is no
        // longer silent. This handler is broader-class belt-and-braces
        // — any unobserved Task fault anywhere in the process (sink
        // supervisors, hot-reload coordinator, license loader, etc.)
        // surfaces on stderr instead of being swallowed by the GC
        // finalizer. Idempotent: subscribing the same handler twice is
        // a no-op (the event auto-deduplicates by delegate identity).
        EnsureUnobservedTaskExceptionHandler();

        // ----- Phase 1: ParseEnvironment ------------------------------------
        var dataRoot = Environment.GetEnvironmentVariable("EDGECONNECT_DATA_ROOT")
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EdgeConnect")
                : "/var/lib/edgeconnect");

        // Endpoints server (Prometheus /metrics + /health/*) — operator
        // overrides:
        //   EDGECONNECT_ENDPOINTS_URL=http://localhost:9101/  (alternate port)
        //   EDGECONNECT_ENDPOINTS_DISABLED=true               (skip the server entirely)
        // Useful when port 9100 is occupied by another process (e.g.
        // Prometheus node_exporter shares that default) or by a
        // leftover HTTP.SYS URL ACL from a prior crashed run.
        var endpointsUrl = Environment.GetEnvironmentVariable("EDGECONNECT_ENDPOINTS_URL");
        var endpointsDisabledRaw = Environment.GetEnvironmentVariable("EDGECONNECT_ENDPOINTS_DISABLED");
        var endpointsDisabled = !string.IsNullOrEmpty(endpointsDisabledRaw)
            && (string.Equals(endpointsDisabledRaw, "true", StringComparison.OrdinalIgnoreCase)
                || endpointsDisabledRaw == "1");

        // Chip 5: EDGECONNECT_CONFIG_DIR was inert before this commit (read
        // into HostOptions.ConfigDirectory but never consumed for path
        // resolution — ConfigurationStorageLayout is constructed from
        // ResolvedDataRoot, which always falls back to DataRoot). Removed
        // for clarity. If an operator still sets it, log a startup
        // deprecation warning so they know the env var is now unrecognised.
        var legacyConfigDir = Environment.GetEnvironmentVariable("EDGECONNECT_CONFIG_DIR");
        if (!string.IsNullOrEmpty(legacyConfigDir))
        {
            Console.Error.WriteLine(
                "[startup] EDGECONNECT_CONFIG_DIR is no longer recognised (removed for " +
                "clarity — it was inert prior to this build). Configuration files live " +
                "under {dataRoot}/config/ where dataRoot is set via EDGECONNECT_DATA_ROOT. " +
                $"Your setting EDGECONNECT_CONFIG_DIR='{legacyConfigDir}' is ignored.");
        }

        var hostOptions = new HostOptions
        {
            ConfigDirectory = Path.Combine(dataRoot, "config"),
            LicensePath = Environment.GetEnvironmentVariable("EDGECONNECT_LICENSE_PATH")
                ?? Path.Combine(dataRoot, "edgelicense.json"),
            GatewayIdentityPath = Environment.GetEnvironmentVariable("EDGECONNECT_IDENTITY_PATH")
                ?? Path.Combine(dataRoot, "identity"),
            DataRoot = dataRoot,
            EndpointsListenUrl = endpointsUrl ?? "http://localhost:9100/",
            EnableEndpointsServer = !endpointsDisabled,
            RecordDeferredPhases = true,
        };

        // ----- Pre-phase: eagerly load config so protocol adapters can be
        // registered into DI before the container is built.
        // ConfigurationStorageLayout is rooted at the gateway data
        // directory — using ResolvedDataRoot keeps this consistent with
        // CompositionRoot (both resolve current.json to the same path).
        var currentConfigPath = new ConfigurationStorageLayout(hostOptions.ResolvedDataRoot).CurrentConfigPath;
        GatewayConfiguration? preloadedConfig = null;
        if (File.Exists(currentConfigPath))
        {
            var json = await File.ReadAllTextAsync(currentConfigPath, ct).ConfigureAwait(false);
            preloadedConfig = JsonSerializer.Deserialize<GatewayConfiguration>(json, PreloadJsonOptions);
        }

        // ----- Pre-phase: eagerly load license so the adapter registration
        // extensions below can gate per-protocol modules.
        // Bound to this gateway's identity (ADR-0036): a license issued for a
        // different gateway fails to load and is treated as unlicensed. The
        // provider reads the persisted identity lazily — null on first start
        // (before the identity exists), which disables the check until then.
        var eagerLicense = new LicenseManager(
            () => FileSystemGatewayIdentity.TryReadPersisted(hostOptions.GatewayIdentityPath));
        if (File.Exists(hostOptions.LicensePath))
        {
            try
            {
                await eagerLicense.LoadFromFileAsync(hostOptions.LicensePath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Soft-fail: log to stderr and continue with no license loaded.
                // HostStartup's LoadLicense phase will surface the issue formally
                // via the diagnostics surface.
                Console.Error.WriteLine($"[startup] License pre-load failed; continuing without enforcement: {ex.Message}");
            }
        }

        // ----- Phase 2: BuildContainer --------------------------------------
        services.AddElpisEdgeConnectHost(hostOptions);

        // Register the pre-loaded license manager so HostStartup's
        // LoadLicense phase sees the same instance.
        services.RemoveAll<ILicenseManager>();
        services.AddSingleton<ILicenseManager>(eagerLicense);

        // Register protocol adapters from the pre-loaded config.
        //
        // M.P2.1 fail-soft: construct the IConfigurationFaultRegistry now,
        // register it in DI as the singleton (overriding the placeholder
        // CompositionRoot.AddElpisEdgeConnectHost adds), and pass it to
        // every protocol extension so cross-record validation failures
        // become fault entries rather than gateway-killing throws. The
        // registry is drained to the audit chain by HostStartup once
        // IConfigurationManager.InitializeAsync completes.
        if (preloadedConfig is not null)
        {
            var faultRegistry = new ConfigurationFaultRegistry();
            services.RemoveAll<IConfigurationFaultRegistry>();
            services.AddSingleton<IConfigurationFaultRegistry>(faultRegistry);

            services.AddFocas2SourcesFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
            services.AddMTConnectSourcesFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
            services.AddModbusTcpSourcesFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
            services.AddS7SourcesFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
            services.AddBrotherHttpSourcesFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
            services.AddOpcUaClientSourcesFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
            services.AddEthernetIpSourcesFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
            services.AddMelsecSourcesFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
            services.AddMqttSinksFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
            services.AddOpcUaServerSinksFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
        }

        // M.2b.3.1 — FOCAS2 demo mode wiring. Three independent signals
        // so accidental activation in production is loudly visible AND
        // the gauge is always scrape-friendly (value=0 in production,
        // value=1 in demo).
        var startupEventStore = new GatewayStartupEventStore();
        services.AddSingleton<IGatewayStartupEventStore>(startupEventStore);
        services.AddSingleton<Focas2FakeModeMeter>();

        if (Focas2DemoModeOptions.IsEnabled)
        {
            // (1) Loud, distinct, grep-friendly stderr line. The phrase
            //     "FOCAS2 FAKE MODE ACTIVE" is the documented marker for
            //     log monitoring to pattern-match — NOT a system failure.
            Console.Error.WriteLine($"[startup][CRITICAL] {Focas2DemoModeOptions.StartupCriticalMessage}");

            // (2) Boot-time signal in the Diagnostics surface (Q9).
            startupEventStore.Append(new GatewayStartupEvent
            {
                EventCode = "focas2.fake-mode.activated",
                Severity = "Critical",
                Message = Focas2DemoModeOptions.StartupCriticalMessage,
                EmittedAtUtc = DateTime.UtcNow,
            });

            // (3) Prometheus gauge — the Focas2FakeModeMeter singleton above
            //     publishes edgeconnect_focas2_fake_mode_enabled. Force its
            //     resolution at startup so the gauge is registered with the
            //     meter listener even when no caller has yet asked for the
            //     instance. The .NET hosted-app convention is to materialise
            //     such singletons at startup time so observable instruments
            //     are scrapeable from the first /metrics hit.
            services.AddHostedService<Focas2FakeModeMeterMaterializer>();
        }

        // M.2b.2 follow-up — Siemens S7 demo mode wiring. Mirrors the FOCAS2
        // block above: gauge always registered (0 in production, 1 in demo);
        // stderr + diagnostics signals only when active.
        services.AddSingleton<S7FakeModeMeter>();

        if (S7DemoModeOptions.IsEnabled)
        {
            Console.Error.WriteLine($"[startup][CRITICAL] {S7DemoModeOptions.StartupCriticalMessage}");

            startupEventStore.Append(new GatewayStartupEvent
            {
                EventCode = "s7.fake-mode.activated",
                Severity = "Critical",
                Message = S7DemoModeOptions.StartupCriticalMessage,
                EmittedAtUtc = DateTime.UtcNow,
            });

            services.AddHostedService<S7FakeModeMeterMaterializer>();
        }

        if (WindowsServiceHelpers.IsWindowsService())
        {
            services.AddWindowsService(o => o.ServiceName = "Elpis EdgeConnect");
        }

        return new EdgeConnectCompositionResult(hostOptions, eagerLicense, preloadedConfig);
    }

    /// <summary>
    /// Tiny hosted service whose only job is to force-resolve the
    /// <see cref="Focas2FakeModeMeter"/> singleton at startup so the
    /// underlying observable gauge is registered against the meter
    /// listener before the first Prometheus scrape arrives.
    /// </summary>
    private sealed class Focas2FakeModeMeterMaterializer : IHostedService
    {
        private readonly Focas2FakeModeMeter _meter;

        public Focas2FakeModeMeterMaterializer(Focas2FakeModeMeter meter)
        {
            _meter = meter;  // resolution alone is enough — the ctor side-effect publishes the gauge.
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = _meter;  // touch the field to suppress any "unused" analyser.
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// S7 counterpart of <see cref="Focas2FakeModeMeterMaterializer"/> — forces
    /// resolution of the <see cref="S7FakeModeMeter"/> singleton at startup so
    /// its observable gauge is registered before the first Prometheus scrape.
    /// </summary>
    private sealed class S7FakeModeMeterMaterializer : IHostedService
    {
        private readonly S7FakeModeMeter _meter;

        public S7FakeModeMeterMaterializer(S7FakeModeMeter meter)
        {
            _meter = meter;  // resolution alone publishes the gauge.
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = _meter;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Cached JsonSerializerOptions used for the pre-phase config read.</summary>
    private static readonly JsonSerializerOptions PreloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 0 = handler not attached; 1 = attached. Set via
    /// <see cref="Interlocked.CompareExchange(ref int, int, int)"/> so the
    /// handler subscribes exactly once per process even when
    /// <see cref="ConfigureRuntimeAsync"/> runs multiple times (test
    /// harness lifetimes).
    /// </summary>
    private static int s_unobservedHandlerAttached;

    private static void EnsureUnobservedTaskExceptionHandler()
    {
        if (Interlocked.CompareExchange(ref s_unobservedHandlerAttached, 1, 0) != 0)
        {
            return;
        }
        TaskScheduler.UnobservedTaskException += static (sender, args) =>
        {
            // Mark observed so the default policy (ignore on .NET 8) is
            // explicit and the exception is logged regardless of the
            // process-wide setting. The fault still surfaced here is a
            // genuine bug — any production-grade handler MUST log it,
            // not swallow it.
            args.SetObserved();
            Console.Error.WriteLine(
                $"[edgeconnect] Unobserved task exception: {args.Exception}");
        };
    }
}
