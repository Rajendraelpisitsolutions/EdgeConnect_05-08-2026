// ============================================================================
// File: HostHarness.cs
// Purpose: Test-side helper that boots a real EdgeConnect host for D phase 6
//          integration scenarios. Uses the real CompositionRoot, real
//          collector, real routing engine, and real supervisors; substitutes
//          IConfigurationManager (NSubstitute) so tests can inject arbitrary
//          GatewayConfiguration without touching on-disk files, and
//          substitutes ILicenseManager so license scenarios can drive state
//          transitions deterministically.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D3
// Milestone: D — phase 6.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Host.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace ElpisEdgeConnect.Integration.Tests;

/// <summary>
/// Builds and runs a full EdgeConnect host with mock adapters and
/// substituted config / license managers. Implements <see cref="IAsyncDisposable"/>
/// so tests can <c>await using</c> an instance.
/// </summary>
public sealed class HostHarness : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly bool _ownsTempDir;
    private bool _started;

    private HostHarness(IHost host, string tempDir, HostHarnessFixtures fixtures, bool ownsTempDir)
    {
        _host = host;
        TempDir = tempDir;
        Fixtures = fixtures;
        _ownsTempDir = ownsTempDir;
    }

    /// <summary>Temporary working directory; cleaned up on dispose.</summary>
    public string TempDir { get; }

    /// <summary>Mutable handles the tests use to drive behavior.</summary>
    public HostHarnessFixtures Fixtures { get; }

    /// <summary>Resolve any DI-registered service.</summary>
    public T GetRequiredService<T>() where T : notnull => _host.Services.GetRequiredService<T>();

    /// <summary>The underlying collector, exposed for snapshot queries.</summary>
    public RuntimeDiagnosticsCollector Collector => GetRequiredService<RuntimeDiagnosticsCollector>();

    /// <summary>The routing engine.</summary>
    public IRoutingEngine RoutingEngine => GetRequiredService<IRoutingEngine>();

    /// <summary>The readiness gate.</summary>
    public IHostReadinessGate Readiness => GetRequiredService<IHostReadinessGate>();

    /// <summary>The source supervisor.</summary>
    public SourceSupervisor SourceSupervisor => GetRequiredService<SourceSupervisor>();

    /// <summary>The sink supervisor.</summary>
    public SinkSupervisor SinkSupervisor => GetRequiredService<SinkSupervisor>();

    /// <summary>Start the host (walks the locked startup sequence).</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started) return;
        await _host.StartAsync(ct).ConfigureAwait(false);
        _started = true;
    }

    /// <summary>Stop the host (mirror shutdown).</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!_started) return;
        _started = false;
        await _host.StopAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        if (_host is IAsyncDisposable ad) { await ad.DisposeAsync().ConfigureAwait(false); }
        else _host.Dispose();
        if (_ownsTempDir)
        {
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Build a harness from the given source and sink registrations plus
    /// the route configuration. The config is wrapped in a substitute
    /// <see cref="IConfigurationManager"/> that returns it from
    /// <c>GetCurrentAsync</c>. The license manager is a permissive
    /// substitute (all modules enabled, status Active).
    /// <para>
    /// Allocates and cleans up a fresh per-test temp directory. For tests
    /// that need persistent storage across host restarts (e.g. the S&amp;F
    /// host-restart resumption test), use
    /// <see cref="BuildWithDataDir"/> instead.
    /// </para>
    /// </summary>
    public static HostHarness Build(
        IEnumerable<SourceRegistration> sources,
        IEnumerable<SinkRegistration> sinks,
        GatewayConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(config);

        var tempDir = Path.Combine(Path.GetTempPath(), "edgeconnect-int-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return BuildCore(sources, sinks, config, tempDir, ownsTempDir: true);
    }

    /// <summary>
    /// Build a harness rooted at <paramref name="dataDir"/>. The directory
    /// is NOT cleaned up on dispose, allowing a follow-up
    /// <see cref="HostHarness"/> to reopen the same SQLite buffer files
    /// for cross-restart durability tests. Caller is responsible for
    /// deleting the directory once all hosts that share it have been
    /// disposed.
    /// </summary>
    public static HostHarness BuildWithDataDir(
        IEnumerable<SourceRegistration> sources,
        IEnumerable<SinkRegistration> sinks,
        GatewayConfiguration config,
        string dataDir)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(dataDir);

        Directory.CreateDirectory(dataDir);
        return BuildCore(sources, sinks, config, dataDir, ownsTempDir: false);
    }

    private static HostHarness BuildCore(
        IEnumerable<SourceRegistration> sources,
        IEnumerable<SinkRegistration> sinks,
        GatewayConfiguration config,
        string tempDir,
        bool ownsTempDir)
    {

        var options = new ElpisEdgeConnect.Host.HostOptions
        {
            ConfigDirectory = Path.Combine(tempDir, "config"),
            LicensePath = Path.Combine(tempDir, "license.json"),
            GatewayIdentityPath = Path.Combine(tempDir, "identity"),
            EnableEndpointsServer = false, // tests don't need the HTTP surface
        };

        var configManager = Substitute.For<IConfigurationManager>();
        configManager.InitializeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        configManager.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<GatewayConfiguration>(config));

        var licenseManager = Substitute.For<ILicenseManager>();
        licenseManager.Status.Returns(LicenseStatus.Valid);
        licenseManager.IsModuleEnabled(Arg.Any<string>()).Returns(true);

        var sourceList = new List<SourceRegistration>(sources);
        var sinkList = new List<SinkRegistration>(sinks);

        var fixtures = new HostHarnessFixtures(configManager, licenseManager, sourceList, sinkList);

        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddElpisEdgeConnectHost(options);
                // Override DI entries the tests need to inject.
                services.AddSingleton(configManager);
                services.AddSingleton(licenseManager);
                services.AddSingleton<IEnumerable<SourceRegistration>>(sourceList);
                services.AddSingleton<IEnumerable<SinkRegistration>>(sinkList);
            });

        var host = hostBuilder.Build();
        return new HostHarness(host, tempDir, fixtures, ownsTempDir);
    }
}

/// <summary>
/// Mutable handles exposed to tests so they can drive the substituted
/// managers mid-run (e.g. induce a license expiration or apply a new
/// configuration).
/// </summary>
public sealed class HostHarnessFixtures
{
    internal HostHarnessFixtures(
        IConfigurationManager configManager,
        ILicenseManager licenseManager,
        List<SourceRegistration> sources,
        List<SinkRegistration> sinks)
    {
        ConfigManager = configManager;
        LicenseManager = licenseManager;
        SourceRegistrations = sources;
        SinkRegistrations = sinks;
    }

    /// <summary>The substituted configuration manager (NSubstitute).</summary>
    public IConfigurationManager ConfigManager { get; }

    /// <summary>The substituted license manager (NSubstitute).</summary>
    public ILicenseManager LicenseManager { get; }

    /// <summary>The source registrations passed to the host (mutable).</summary>
    public List<SourceRegistration> SourceRegistrations { get; }

    /// <summary>The sink registrations passed to the host (mutable).</summary>
    public List<SinkRegistration> SinkRegistrations { get; }
}
