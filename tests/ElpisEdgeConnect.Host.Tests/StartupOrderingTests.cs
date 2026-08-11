// ============================================================================
// File: StartupOrderingTests.cs
// Purpose: Pin the locked host startup/shutdown sequence. The single test
//          here drives a real generic host built from the real
//          CompositionRoot, swaps in stub IConfigurationManager and
//          ILicenseManager (so the test does not depend on disk state),
//          captures every StartupPhase emission via a recording observer,
//          starts and stops the host, and asserts the recorded sequence
//          equals StartupPhase.{1..12} on startup and the strict reverse
//          on shutdown.
//
//          ANY change to the locked startup ordering must update both
//          HostStartup AND this test together — they are the contract.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D — startup ordering
// Milestone: D — phase 2.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class StartupOrderingTests
{
    /// <summary>
    /// Recording observer the test injects to capture every phase emission
    /// in the order it was fired.
    /// </summary>
    private sealed class RecordingObserver : IStartupSequenceObserver
    {
        private readonly object _gate = new();
        private readonly List<(StartupPhase Phase, string Direction)> _events = new();

        public IReadOnlyList<(StartupPhase Phase, string Direction)> Events
        {
            get { lock (_gate) { return _events.ToArray(); } }
        }

        public void OnStartupPhase(StartupPhase phase)
        {
            lock (_gate) { _events.Add((phase, "startup")); }
        }

        public void OnShutdownPhase(StartupPhase phase)
        {
            lock (_gate) { _events.Add((phase, "shutdown")); }
        }
    }

    private static GatewayConfiguration EmptyConfig() => new()
    {
        Gateway = new GatewaySettings
        {
            GatewayId = "test-gateway",
            GatewayName = "Test Gateway",
        },
    };

    private static HostOptions OptionsForTempDir(out string tempDir)
    {
        tempDir = Path.Combine(Path.GetTempPath(), "edgeconnect-host-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return new HostOptions
        {
            ConfigDirectory = Path.Combine(tempDir, "config"),
            LicensePath = Path.Combine(tempDir, "license.json"),
            GatewayIdentityPath = Path.Combine(tempDir, "identity"),
            RecordDeferredPhases = true,
            // Don't bind a port in the ordering-pin test; the observer
            // fires regardless of whether the server is enabled. Dedicated
            // endpoint tests exercise the server with a real bound port.
            EnableEndpointsServer = false,
        };
    }

    [Fact]
    public async Task HostStartup_WalksLockedPhasesInOrder_AndReverseOnShutdown()
    {
        var options = OptionsForTempDir(out var tempDir);
        try
        {
            var observer = new RecordingObserver();

            // Stub config manager that returns an empty configuration. We do
            // NOT want this test to depend on real on-disk config files; the
            // test's job is to pin ORDERING, not to exercise the file store.
            var configManager = Substitute.For<IConfigurationManager>();
            configManager.InitializeAsync(Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            configManager.GetCurrentAsync(Arg.Any<CancellationToken>())
                .Returns(new ValueTask<GatewayConfiguration>(EmptyConfig()));

            // Stub license manager — InitializeAsync isn't on the interface;
            // the host calls LoadFromFileAsync only when the file exists.
            // We leave the file absent so the LoadLicense phase is observed
            // but the manager is not actually invoked.
            var licenseManager = Substitute.For<ILicenseManager>();

            // Build the host with the real composition root and OVERRIDE
            // the substitutable services.
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Services.AddElpisEdgeConnectHost(options);
            // Override after AddElpisEdgeConnectHost so our stubs win.
            builder.Services.AddSingleton(configManager);
            builder.Services.AddSingleton(licenseManager);
            builder.Services.AddSingleton<IStartupSequenceObserver>(observer);

            using var host = builder.Build();

            await host.StartAsync(CancellationToken.None);

            // Snapshot the startup events BEFORE shutdown.
            var startupEvents = observer.Events
                .Where(e => e.Direction == "startup")
                .Select(e => e.Phase)
                .ToArray();

            // Locked startup ordering: every phase 1..12 in numeric order.
            var expectedStartup = new[]
            {
                StartupPhase.ParseEnvironment,
                StartupPhase.BuildContainer,
                StartupPhase.LoadGatewayIdentity,
                StartupPhase.LoadConfiguration,
                StartupPhase.LoadLicense,
                StartupPhase.ConstructDiagnostics,
                StartupPhase.ConstructBufferFactory,
                StartupPhase.RegisterRoutes,
                StartupPhase.StartSourceSupervisor,
                StartupPhase.StartRoutingEngine,
                StartupPhase.MarkReady,
                StartupPhase.StartMetricsEndpoint,
            };
            startupEvents.Should().Equal(expectedStartup);

            // Readiness gate must be open at this point.
            var gate = host.Services.GetRequiredService<IHostReadinessGate>();
            gate.IsReady.Should().BeTrue("MarkReady ran during startup");

            // Now stop the host and verify the strict reverse.
            await host.StopAsync(CancellationToken.None);

            var shutdownEvents = observer.Events
                .Where(e => e.Direction == "shutdown")
                .Select(e => e.Phase)
                .ToArray();
            shutdownEvents.Should().Equal(expectedStartup.Reverse());

            // Readiness gate must be closed.
            gate.IsReady.Should().BeFalse("MarkNotReady ran during shutdown");

            // The locked InitializeAsync call MUST have happened on the
            // configuration manager (this pins that LoadConfiguration is
            // not just an observer event but actually does work).
            await configManager.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* test cleanup */ }
        }
    }

    [Fact]
    public async Task HostStartup_FailureInLoadConfiguration_FailsFast()
    {
        var options = OptionsForTempDir(out var tempDir);
        try
        {
            var observer = new RecordingObserver();
            var configManager = Substitute.For<IConfigurationManager>();
            configManager.InitializeAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("simulated bad config")));

            var licenseManager = Substitute.For<ILicenseManager>();

            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Services.AddElpisEdgeConnectHost(options);
            builder.Services.AddSingleton(configManager);
            builder.Services.AddSingleton(licenseManager);
            builder.Services.AddSingleton<IStartupSequenceObserver>(observer);

            using var host = builder.Build();

            // Startup MUST throw — fail-fast contract.
            var act = async () => await host.StartAsync(CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*simulated bad config*");

            // Phases AFTER LoadConfiguration must NOT have been observed.
            var observedStartup = observer.Events
                .Where(e => e.Direction == "startup")
                .Select(e => e.Phase)
                .ToArray();
            observedStartup.Should().Contain(StartupPhase.LoadConfiguration);
            observedStartup.Should().NotContain(StartupPhase.LoadLicense);
            observedStartup.Should().NotContain(StartupPhase.MarkReady);

            // Readiness gate must NOT be open.
            host.Services.GetRequiredService<IHostReadinessGate>().IsReady.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* test cleanup */ }
        }
    }

    // Regression for the ADR-0020 G5 live finding: a read-only audit.log under
    // the data root (e.g. admin-owned ProgramData files, non-admin runtime user)
    // must NOT crash startup — the data pipeline keeps running (fail-soft) and a
    // gateway-level CORE.CONFIG_DATA_NOT_WRITABLE fault is registered for the
    // Studio instead of failing deep inside the first audit-writing operation.
    [Fact]
    public async Task HostStartup_UnwritableDataRoot_FailsSoft_AndRegistersGatewayFault()
    {
        var options = OptionsForTempDir(out var tempDir);
        var layout = new ConfigurationStorageLayout(tempDir);
        layout.EnsureDirectoriesExist();
        File.WriteAllText(layout.AuditLogPath, "{}\n");
        File.SetAttributes(layout.AuditLogPath, FileAttributes.ReadOnly);
        try
        {
            // Guard: if this identity can append despite the ReadOnly attribute
            // (e.g. running as root on Linux), the probe legitimately can't
            // detect a problem — skip rather than assert a false negative.
            if (CanAppend(layout.AuditLogPath))
            {
                return;
            }

            var observer = new RecordingObserver();
            var configManager = Substitute.For<IConfigurationManager>();
            configManager.InitializeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            configManager.GetCurrentAsync(Arg.Any<CancellationToken>())
                .Returns(new ValueTask<GatewayConfiguration>(EmptyConfig()));
            var licenseManager = Substitute.For<ILicenseManager>();

            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Services.AddElpisEdgeConnectHost(options);
            builder.Services.AddSingleton(configManager);
            builder.Services.AddSingleton(licenseManager);
            builder.Services.AddSingleton<IStartupSequenceObserver>(observer);

            using var host = builder.Build();

            // Fail-soft: startup completes and the readiness gate opens.
            await host.StartAsync(CancellationToken.None);
            host.Services.GetRequiredService<IHostReadinessGate>().IsReady.Should().BeTrue();

            // The gateway-level fault is registered for the Studio.
            var registry = host.Services.GetRequiredService<IConfigurationFaultRegistry>();
            var fault = registry.GetFaultsFor(ConfigurationFaultKind.Gateway).Should().ContainSingle().Subject;
            fault.ErrorCode.Should().Be(CoreErrors.ConfigDataNotWritable);
            fault.InstanceId.Should().Be("data-root");
            fault.Message.Should().Contain("audit.log");

            await host.StopAsync(CancellationToken.None);
        }
        finally
        {
            try { File.SetAttributes(layout.AuditLogPath, FileAttributes.Normal); } catch { /* cleanup */ }
            try { Directory.Delete(tempDir, recursive: true); } catch { /* test cleanup */ }
        }
    }

    private static bool CanAppend(string path)
    {
        try
        {
            using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
            }
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
