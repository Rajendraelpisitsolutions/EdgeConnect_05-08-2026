// ============================================================================
// File: PythonModbusSimulatorFixture.cs
// Purpose: xUnit async fixture that spawns the pymodbus simulator
//          (ModbusSimulator/server.py) directly via the host's Python
//          interpreter — no Docker required. Complements
//          ModbusTcpSimulatorFixture which uses Docker; this variant
//          lets the Modbus→OPC UA end-to-end scenario run on dev
//          laptops where Docker isn't installed but Python +
//          pymodbus are.
//
//          IsAvailable=false when no Python interpreter with pymodbus
//          can be located; the consuming test class branches on the
//          flag and skips.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H (E2E validation)
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ElpisEdgeConnect.Integration.Tests;

/// <summary>
/// Manages the lifecycle of the pymodbus simulator (server.py) spawned
/// directly under a host Python interpreter. Used by the Modbus →
/// OPC UA end-to-end test.
/// </summary>
public sealed class PythonModbusSimulatorFixture : IAsyncLifetime
{
    private Process? _proc;

    /// <summary>TCP host the simulator listens on (always 127.0.0.1).</summary>
    public string Host => "127.0.0.1";

    /// <summary>Host-side TCP port the simulator is listening on.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// False when no Python interpreter with pymodbus is available, or
    /// the simulator failed to start. Consumers should branch on this
    /// and skip the test.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Human-readable reason the fixture is unavailable, if any.</summary>
    public string? UnavailableReason { get; private set; }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        var python = ResolvePythonExecutable();
        if (python is null)
        {
            UnavailableReason = "no Python interpreter (python / python3 / py) found on PATH";
            return;
        }

        if (!await PymodbusAvailableAsync(python).ConfigureAwait(false))
        {
            UnavailableReason = $"{python} found but pymodbus is not installed (try: pip install pymodbus)";
            return;
        }

        var serverPath = LocateServerScript();
        if (serverPath is null)
        {
            UnavailableReason = "ModbusSimulator/server.py not found alongside the test binary";
            return;
        }

        Port = FindFreeTcpPort();
        try
        {
            _proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = $"\"{serverPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Environment =
                    {
                        ["MODBUS_PORT"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        // Deterministic timing — no jitter, no slow-slave episodes.
                        ["MODBUS_SIM_JITTER_MS"] = "0",
                        ["MODBUS_SIM_SLOW_AFTER"] = "0",
                        // Force unbuffered stdout so we can see logs if a future test reads them.
                        ["PYTHONUNBUFFERED"] = "1",
                    },
                },
                EnableRaisingEvents = true,
            };

            _proc.Start();

            // Drain stdout/stderr to /dev/null so the process doesn't
            // block on a full pipe.
            _ = _proc.StandardOutput.ReadToEndAsync();
            _ = _proc.StandardError.ReadToEndAsync();

            if (!await WaitForTcpReadyAsync(Host, Port, TimeSpan.FromSeconds(15)).ConfigureAwait(false))
            {
                UnavailableReason =
                    $"pymodbus simulator did not accept TCP connections on {Host}:{Port} within 15s";
                await StopAsync().ConfigureAwait(false);
                return;
            }

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"unexpected fixture failure: {ex.Message}";
            await StopAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task DisposeAsync() => StopAsync();

    private async Task StopAsync()
    {
        if (_proc is null) return;
        try
        {
            if (!_proc.HasExited)
            {
                _proc.Kill(entireProcessTree: true);
            }
            await _proc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
        _proc.Dispose();
        _proc = null;
    }

    private static string? ResolvePythonExecutable()
    {
        // Probe likely candidates in order. Windows ships 'py' (the
        // launcher) and 'python'; Linux/macOS tend to ship 'python3'.
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "py", "python", "python3" }
            : new[] { "python3", "python" };

        foreach (var candidate in candidates)
        {
            if (ProbeExecutable(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static bool ProbeExecutable(string exe)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return false;
            if (!p.WaitForExit(2000))
            {
                try { p.Kill(); } catch { /* ignore */ }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> PymodbusAvailableAsync(string python)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = python,
                Arguments = "-c \"import pymodbus; print(pymodbus.__version__)\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return false;
            await p.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token)
                .ConfigureAwait(false);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? LocateServerScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ModbusSimulator", "server.py"),
            Path.Combine(Directory.GetCurrentDirectory(), "ModbusSimulator", "server.py"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "ModbusSimulator", "server.py");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static int FindFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> WaitForTcpReadyAsync(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
                if (tcp.Connected) return true;
            }
            catch
            {
                // not ready yet
            }
            await Task.Delay(200).ConfigureAwait(false);
        }
        return false;
    }
}
