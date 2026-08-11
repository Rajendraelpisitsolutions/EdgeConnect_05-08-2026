// ============================================================================
// Tests: End-to-end Modbus RTU-over-TCP against the standalone simulator
//        (tools/ModbusRtuSimulator). Drives the REAL FluentModbusRtuClient over
//        a TCP socket (TcpModbusRtuSerialPort) against a genuine RTU slave,
//        proving the RTU framing + CRC works on the wire — not just against
//        canned frames. Skips gracefully if the simulator can't start.
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.ModbusTcp;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusRtuSimulatorFixture : IAsyncLifetime
{
    private Process? _process;

    public string Host => "127.0.0.1";
    public int Port { get; private set; }
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        var dll = LocateSimulatorDll();
        if (dll is null)
        {
            UnavailableReason = "ModbusRtuSimulator.dll not found (build the tools project)";
            return;
        }

        Port = FindFreeTcpPort();
        try
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{dll}\" --port {Port} --bind 127.0.0.1",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            _process.Start();

            if (!await WaitForTcpReadyAsync(Host, Port, TimeSpan.FromSeconds(15)).ConfigureAwait(false))
            {
                UnavailableReason = $"simulator did not accept TCP on {Host}:{Port} within 15s";
                await StopAsync().ConfigureAwait(false);
                return;
            }
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"failed to start simulator: {ex.Message}";
            await StopAsync().ConfigureAwait(false);
        }
    }

    public Task DisposeAsync() => StopAsync();

    private async Task StopAsync()
    {
        if (_process is null) { return; }
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch { /* best-effort */ }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private static string? LocateSimulatorDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ElpisEdgeConnect.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null) { return null; }

        var simBin = Path.Combine(dir.FullName, "tools", "ModbusRtuSimulator", "bin");
        if (!Directory.Exists(simBin)) { return null; }

        string? newest = null;
        var newestWrite = DateTime.MinValue;
        foreach (var path in Directory.EnumerateFiles(simBin, "ModbusRtuSimulator.dll", SearchOption.AllDirectories))
        {
            var w = File.GetLastWriteTimeUtc(path);
            if (w > newestWrite) { newestWrite = w; newest = path; }
        }
        return newest;
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
                if (tcp.Connected) { return true; }
            }
            catch { /* not ready */ }
            await Task.Delay(200).ConfigureAwait(false);
        }
        return false;
    }
}

public sealed class ModbusRtuOverTcpIntegrationTests : IClassFixture<ModbusRtuSimulatorFixture>
{
    private readonly ModbusRtuSimulatorFixture _sim;

    public ModbusRtuOverTcpIntegrationTests(ModbusRtuSimulatorFixture sim) => _sim = sim;

    private ModbusTcpSourceConfiguration Config() => new()
    {
        InstanceId = "rtu-otcp",
        ProtocolName = "modbusrtu",
        DeviceId = "rtu1",
        DeviceClass = "plc",
        Host = _sim.Host,
        Port = (ushort)_sim.Port,
        Encapsulation = ModbusEncapsulation.RtuOverTcp,
    };

    private async Task<FluentModbusRtuClient> ConnectAsync()
    {
        var client = new FluentModbusRtuClient(RtuTransportMode.Tcp, Config());
        await client.ConnectAsync(
            _sim.Host, _sim.Port, ModbusEncapsulation.RtuOverTcp,
            connectTimeout: TimeSpan.FromSeconds(3), readTimeout: TimeSpan.FromSeconds(3), CancellationToken.None);
        return client;
    }

    [Fact]
    public async Task RealClient_ReadsHoldingRegisters_OverRtuOverTcp()
    {
        if (!_sim.IsAvailable) { return; } // skip — see UnavailableReason

        await using var client = await ConnectAsync();

        var regs = await client.ReadHoldingRegistersAsync(unitId: 1, startAddress: 0, quantity: 3, CancellationToken.None);

        // Simulator returns register[addr] = 1000 + addr.
        regs.Should().Equal((ushort)1000, (ushort)1001, (ushort)1002);
    }

    [Fact]
    public async Task RealClient_ReadsInputRegisters_OverRtuOverTcp()
    {
        if (!_sim.IsAvailable) { return; }

        await using var client = await ConnectAsync();

        var regs = await client.ReadInputRegistersAsync(unitId: 1, startAddress: 5, quantity: 2, CancellationToken.None);

        regs.Should().Equal((ushort)1005, (ushort)1006);
    }

    [Fact]
    public async Task RealClient_ReadsCoils_OverRtuOverTcp()
    {
        if (!_sim.IsAvailable) { return; }

        await using var client = await ConnectAsync();

        var coils = await client.ReadCoilsAsync(unitId: 1, startAddress: 0, quantity: 4, CancellationToken.None);

        // Simulator: even addresses true, odd false.
        coils.Should().Equal(true, false, true, false);
    }
}
