// ============================================================================
// File: ModbusTcpSimulatorFixture.cs
// Purpose: xUnit async fixture that builds and runs the pymodbus-based Modbus
//          TCP simulator (see ModbusSimulator/Dockerfile) for the duration
//          of an integration-test class.
//
//          If docker isn't available the fixture sets IsAvailable=false
//          instead of failing — the test class checks the flag and Skips.
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
/// Manages the lifecycle of the pymodbus Docker simulator used by the
/// Phase 3 F1 integration tests. Designed to be used as an
/// <see cref="IAsyncLifetime"/> fixture.
/// </summary>
public sealed class ModbusTcpSimulatorFixture : IAsyncLifetime
{
    private const string ImageTag = "elpis-modbus-sim:test";
    private const int InternalPort = 5020;

    private string? _containerName;

    /// <summary>TCP host the simulator is listening on (always 127.0.0.1).</summary>
    public string Host => "127.0.0.1";

    /// <summary>Host-side TCP port mapped to the container's 5020 port.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// False when the fixture was unable to start the simulator (docker
    /// missing, daemon not running, build failed). Tests should call
    /// <see cref="Assert.SkipUnless(bool, string)"/> or branch on this flag.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Human-readable reason the fixture is unavailable, if any.</summary>
    public string? UnavailableReason { get; private set; }

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (!await IsDockerAvailableAsync().ConfigureAwait(false))
        {
            UnavailableReason = "docker CLI not found or daemon unreachable";
            return;
        }

        var simulatorDir = LocateSimulatorDirectory();
        if (simulatorDir is null)
        {
            UnavailableReason = "ModbusSimulator/Dockerfile not located on disk";
            return;
        }

        try
        {
            var buildResult = await RunProcessAsync(
                "docker",
                $"build -q -t {ImageTag} \"{simulatorDir}\"",
                TimeSpan.FromMinutes(5)).ConfigureAwait(false);
            if (buildResult.ExitCode != 0)
            {
                UnavailableReason = $"docker build failed: {buildResult.Stderr.Trim()}";
                return;
            }

            _containerName = $"elpis-modbus-sim-{Guid.NewGuid():N}";
            Port = FindFreeTcpPort();

            var runResult = await RunProcessAsync(
                "docker",
                $"run -d --rm --name {_containerName} -p 127.0.0.1:{Port}:{InternalPort} {ImageTag}",
                TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            if (runResult.ExitCode != 0)
            {
                UnavailableReason = $"docker run failed: {runResult.Stderr.Trim()}";
                _containerName = null;
                return;
            }

            if (!await WaitForTcpReadyAsync(Host, Port, TimeSpan.FromSeconds(30)).ConfigureAwait(false))
            {
                await StopContainerAsync().ConfigureAwait(false);
                UnavailableReason = $"simulator did not accept TCP connections on {Host}:{Port} within 30s";
                return;
            }

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"unexpected fixture failure: {ex.Message}";
            IsAvailable = false;
            await StopContainerAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task DisposeAsync() => StopContainerAsync();

    private async Task StopContainerAsync()
    {
        if (_containerName is null)
        {
            return;
        }
        try
        {
            await RunProcessAsync("docker", $"stop --time 2 {_containerName}",
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
        _containerName = null;
    }

    private static async Task<bool> IsDockerAvailableAsync()
    {
        try
        {
            var result = await RunProcessAsync("docker", "version --format {{.Server.Version}}",
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? LocateSimulatorDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ModbusSimulator"),
            // When running `dotnet test` the cwd is the test project dir.
            Path.Combine(Directory.GetCurrentDirectory(), "ModbusSimulator"),
        };
        foreach (var candidate in candidates)
        {
            var dockerfile = Path.Combine(candidate, "Dockerfile");
            if (File.Exists(dockerfile))
            {
                return candidate;
            }
        }

        // Walk upward from BaseDirectory looking for the Integration.Tests
        // project folder — handles test hosts that rebase cwd.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "ModbusSimulator");
            if (File.Exists(Path.Combine(candidate, "Dockerfile")))
            {
                return candidate;
            }
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
                if (tcp.Connected)
                {
                    return true;
                }
            }
            catch
            {
                // not ready yet
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
        return false;
    }

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);

    private static async Task<ProcessResult> RunProcessAsync(string file, string args, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        process.Start();

        using var cts = new CancellationTokenSource(timeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            return new ProcessResult(-1, string.Empty, $"process timed out after {timeout}");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }
}
