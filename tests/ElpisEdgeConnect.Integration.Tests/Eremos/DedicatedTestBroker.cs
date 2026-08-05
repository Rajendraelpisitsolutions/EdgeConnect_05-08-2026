// ============================================================================
// File: Eremos/DedicatedTestBroker.cs
// Purpose: Owns a Mosquitto process per-test for the EREMOS V2 revalidation.
//          Resolves Q3 + Q2 from the v2 plan: the test owns the broker,
//          spawns on a random free TCP port, tears down cleanly. Gate 1
//          ("zero disconnect storms") would be violated if we shared the
//          default 1883 broker with other parallel MQTT tests.
//
//          Mosquitto must be installed at the standard Windows path
//          (C:\Program Files\mosquitto\mosquitto.exe). The fixture skips
//          gracefully if not — the [Collection] is decorated so the test
//          surfaces as "skipped" rather than "failed" on machines without
//          Mosquitto.
//
// Reference: docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §6.3
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Integration.Tests.Eremos;

/// <summary>
/// A Mosquitto broker process owned by a single test. Constructor spawns;
/// <see cref="DisposeAsync"/> terminates the process and cleans up the temp
/// config file. The broker listens on <see cref="Port"/> (a random free
/// port assigned at construction time) on 127.0.0.1 with anonymous access.
/// </summary>
/// <remarks>
/// Two-phase lifecycle:
///   1. Construction spawns the process and waits up to 5s for the port to
///      accept TCP connections (proxy for "broker is listening").
///   2. <see cref="StopAsync"/> + <see cref="StartAsync"/> allow mid-test
///      outage injection for Gate 5 (reconnect behaviour). The fixture
///      tracks state so DisposeAsync handles either a running or stopped
///      broker correctly.
/// </remarks>
public sealed class DedicatedTestBroker : IAsyncDisposable
{
    private const string DefaultMosquittoPath = @"C:\Program Files\mosquitto\mosquitto.exe";
    private const int StartupProbeTimeoutMs = 5000;
    private const int StartupProbeIntervalMs = 50;

    private readonly string _mosquittoPath;
    private readonly string _tempConfigPath;
    private Process? _process;

    /// <summary>The 127.0.0.1 port the broker is listening on.</summary>
    public int Port { get; }

    /// <summary>The broker URL — convenience for MQTT clients.</summary>
    public string BrokerUrl => $"tcp://127.0.0.1:{Port}";

    /// <summary>
    /// True if Mosquitto is available at the standard install path. Use
    /// this in tests to skip gracefully on machines without Mosquitto.
    /// </summary>
    public static bool IsAvailable(string? mosquittoPath = null)
    {
        var path = mosquittoPath ?? DefaultMosquittoPath;
        return File.Exists(path);
    }

    /// <summary>
    /// Construct and immediately start a dedicated Mosquitto broker on a
    /// random free port. Throws if Mosquitto isn't available — call
    /// <see cref="IsAvailable"/> first to skip gracefully.
    /// </summary>
    public DedicatedTestBroker(string? mosquittoPath = null)
    {
        _mosquittoPath = mosquittoPath ?? DefaultMosquittoPath;
        if (!File.Exists(_mosquittoPath))
        {
            throw new InvalidOperationException(
                $"Mosquitto not found at '{_mosquittoPath}'. " +
                "Install Mosquitto or call DedicatedTestBroker.IsAvailable() first to skip the test.");
        }

        Port = FindFreePort();
        _tempConfigPath = WriteTempConfig(Port);
        StartProcess();
    }

    /// <summary>Stop the broker process (for Gate 5 outage injection).</summary>
    public Task StopAsync()
    {
        if (_process is null || _process.HasExited)
        {
            return Task.CompletedTask;
        }

        try
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(2000);
        }
        catch (InvalidOperationException) { /* already exited */ }
        finally
        {
            _process.Dispose();
            _process = null;
        }
        return Task.CompletedTask;
    }

    /// <summary>Re-start the broker on the same port (Gate 5 recovery).</summary>
    public async Task StartAsync()
    {
        if (_process is not null && !_process.HasExited)
        {
            return; // already running
        }

        StartProcess();
        await WaitForBrokerListeningAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(_tempConfigPath))
            {
                File.Delete(_tempConfigPath);
            }
        }
        catch { /* best-effort cleanup */ }
    }

    // ─── internals ──────────────────────────────────────────────────────

    private void StartProcess()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _mosquittoPath,
            Arguments = $"-c \"{_tempConfigPath}\" -v",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = Process.Start(psi);
        if (_process is null)
        {
            throw new InvalidOperationException($"Failed to start Mosquitto process from '{_mosquittoPath}'.");
        }

        // Probe the port until the broker accepts connections (up to 5s).
        WaitForBrokerListeningAsync().GetAwaiter().GetResult();
    }

    private async Task WaitForBrokerListeningAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(StartupProbeTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await IsPortAcceptingAsync(Port).ConfigureAwait(false))
            {
                return;
            }
            await Task.Delay(StartupProbeIntervalMs).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Dedicated Mosquitto broker on port {Port} did not start within {StartupProbeTimeoutMs}ms.");
    }

    private static async Task<bool> IsPortAcceptingAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
            var winner = await Task.WhenAny(connectTask, Task.Delay(200)).ConfigureAwait(false);
            return winner == connectTask && client.Connected;
        }
        catch { return false; }
    }

    private static int FindFreePort()
    {
        // Bind to port 0 → OS assigns a free port → read + close. Standard
        // pattern; tiny race window between close and re-bind by Mosquitto
        // but acceptable for tests.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string WriteTempConfig(int port)
    {
        var path = Path.Combine(Path.GetTempPath(), $"eremos-revalidation-mosquitto-{Guid.NewGuid():N}.conf");
        File.WriteAllText(path,
            $"listener {port} 127.0.0.1\n" +
            "allow_anonymous true\n" +
            "persistence false\n" +
            // Quiet down logging — the test harness reads diagnostics from
            // gateway-side counters, not broker logs.
            "log_dest none\n");
        return path;
    }
}
