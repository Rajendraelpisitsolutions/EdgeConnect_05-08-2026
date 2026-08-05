// ============================================================================
// File: ModbusTcpF1IntegrationTests.cs
// Purpose: Integration tests for the Phase 3 F1 Modbus TCP adapter against a
//          real pymodbus simulator running in a Docker container (see
//          ModbusTcpSimulatorFixture). Verifies FC01/02/03/04 reads return
//          the seeded values and that fatal transport errors surface
//          correctly when the peer is unreachable.
//
// The pymodbus address map (see ModbusSimulator/server.py):
//   FC01 coils 0..9:       [T,F,T,F,T,F,T,F,T,F]
//   FC02 discrete 0..7:    [F,F,T,T,F,F,T,T]
//   FC03 holding 0..4:     [0x1111, 0x2222, 0x3333, 0x4444, 0x5555]
//   FC03 holding 10..14:   [100, 200, 300, 400, 500]
//   FC04 input 0..2:       [0xDEAD, 0xBEEF, 0xCAFE]
// ============================================================================

using System;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.ModbusTcp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ElpisEdgeConnect.Integration.Tests;

/// <summary>
/// End-to-end integration tests for the F1 Modbus TCP adapter. Skips if
/// Docker is unavailable on the test host.
/// </summary>
[Trait("Category", "RequiresDocker")]
public sealed class ModbusTcpF1IntegrationTests : IClassFixture<ModbusTcpSimulatorFixture>
{
    private readonly ModbusTcpSimulatorFixture _sim;
    private readonly ITestOutputHelper _output;

    public ModbusTcpF1IntegrationTests(ModbusTcpSimulatorFixture sim, ITestOutputHelper output)
    {
        _sim = sim;
        _output = output;
    }

    private ModbusTcpSourceConfiguration ConfigForSim() => new()
    {
        InstanceId = "modbus-sim",
        ProtocolName = "modbustcp",
        DeviceId = "plc-sim",
        Host = _sim.Host,
        Port = (ushort)_sim.Port,
        DefaultUnitId = 1,
        ConnectTimeoutMs = 3000,
        RequestTimeoutMs = 2000,
        MaxTransactionRetries = 1,
        InitialBackoffMs = 50,
        MaxBackoffMs = 1000,
        CircuitBreakerThreshold = 100,
        CircuitBreakerResetMs = 1000,
    };

    [Fact]
    public async Task ReadsHoldingRegisters_FromSimulator()
    {
        if (SkipIfUnavailable()) return;
        _output.WriteLine($"Simulator running on {_sim.Host}:{_sim.Port}");

        await using var adapter = new ModbusTcpSourceAdapter(
            "modbus-sim",
            new FluentModbusClient(),
            NullLogger.Instance);
        await adapter.InitializeAsync(ConfigForSim(), default);
        await adapter.StartAsync(default);

        var result = await adapter.ExecuteAsyncInternal(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 5),
            default);

        result.IsSuccess.Should().BeTrue(
            "simulator seeds holding registers 0..4 with 0x1111..0x5555; got error: {0}",
            result.Error?.Message);
        result.RegisterPayload.Should().Equal(new ushort[] { 0x1111, 0x2222, 0x3333, 0x4444, 0x5555 });
    }

    [Fact]
    public async Task ReadsHoldingRegisters_OffsetRange()
    {
        if (SkipIfUnavailable()) return;

        await using var adapter = new ModbusTcpSourceAdapter(
            "modbus-sim",
            new FluentModbusClient(),
            NullLogger.Instance);
        await adapter.InitializeAsync(ConfigForSim(), default);
        await adapter.StartAsync(default);

        var result = await adapter.ExecuteAsyncInternal(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 10, 5),
            default);

        result.IsSuccess.Should().BeTrue();
        result.RegisterPayload.Should().Equal(new ushort[] { 100, 200, 300, 400, 500 });
    }

    [Fact]
    public async Task ReadsInputRegisters_FromSimulator()
    {
        if (SkipIfUnavailable()) return;

        await using var adapter = new ModbusTcpSourceAdapter(
            "modbus-sim",
            new FluentModbusClient(),
            NullLogger.Instance);
        await adapter.InitializeAsync(ConfigForSim(), default);
        await adapter.StartAsync(default);

        var result = await adapter.ExecuteAsyncInternal(
            new ModbusReadRequest(1, ModbusRegisterClass.InputRegister, 0, 3),
            default);

        result.IsSuccess.Should().BeTrue();
        result.RegisterPayload.Should().Equal(new ushort[] { 0xDEAD, 0xBEEF, 0xCAFE });
    }

    [Fact]
    public async Task ReadsCoils_FromSimulator()
    {
        if (SkipIfUnavailable()) return;

        await using var adapter = new ModbusTcpSourceAdapter(
            "modbus-sim",
            new FluentModbusClient(),
            NullLogger.Instance);
        await adapter.InitializeAsync(ConfigForSim(), default);
        await adapter.StartAsync(default);

        var result = await adapter.ExecuteAsyncInternal(
            new ModbusReadRequest(1, ModbusRegisterClass.Coil, 0, 10),
            default);

        result.IsSuccess.Should().BeTrue();
        result.BitPayload.Should().Equal(
            new bool[] { true, false, true, false, true, false, true, false, true, false });
    }

    [Fact]
    public async Task ReadsDiscreteInputs_FromSimulator()
    {
        if (SkipIfUnavailable()) return;

        await using var adapter = new ModbusTcpSourceAdapter(
            "modbus-sim",
            new FluentModbusClient(),
            NullLogger.Instance);
        await adapter.InitializeAsync(ConfigForSim(), default);
        await adapter.StartAsync(default);

        var result = await adapter.ExecuteAsyncInternal(
            new ModbusReadRequest(1, ModbusRegisterClass.DiscreteInput, 0, 8),
            default);

        result.IsSuccess.Should().BeTrue();
        result.BitPayload.Should().Equal(
            new bool[] { false, false, true, true, false, false, true, true });
    }

    [Fact]
    public async Task InvalidAddress_ReturnsSlaveException_WithoutDroppingConnection()
    {
        if (SkipIfUnavailable()) return;

        await using var adapter = new ModbusTcpSourceAdapter(
            "modbus-sim",
            new FluentModbusClient(),
            NullLogger.Instance);
        await adapter.InitializeAsync(ConfigForSim(), default);
        await adapter.StartAsync(default);

        // Addr 60000 is far outside the seeded block — pymodbus returns
        // Illegal Data Address (0x02). Non-fatal: the next read should still
        // succeed on the same connection.
        var bad = await adapter.ExecuteAsyncInternal(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 60000, 1),
            default);
        bad.IsSuccess.Should().BeFalse();
        bad.Error!.Code.Should().Be(ModbusErrors.SlaveException);
        bad.SlaveExceptionCode.Should().Be((byte)0x02);

        var good = await adapter.ExecuteAsyncInternal(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 1),
            default);
        good.IsSuccess.Should().BeTrue(
            "a slave exception must not invalidate the connection; got: {0}",
            good.Error?.Message);
    }

    [Fact]
    public async Task ConnectToUnreachablePort_FailsWithConnectFailedErrorCode()
    {
        if (SkipIfUnavailable()) return;

        var cfg = ConfigForSim() with
        {
            // Same host but a port no one is listening on. Keep the
            // connect timeout tight so the test wraps up quickly.
            Port = 5999,
            ConnectTimeoutMs = 500,
            RequestTimeoutMs = 500,
            MaxTransactionRetries = 0,
            CircuitBreakerThreshold = 100,
        };

        await using var adapter = new ModbusTcpSourceAdapter(
            "modbus-sim-bad",
            new FluentModbusClient(),
            NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);

        var result = await adapter.ExecuteAsyncInternal(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 1),
            default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().BeOneOf(ModbusErrors.ConnectFailed, ModbusErrors.SocketError);
    }

    private bool SkipIfUnavailable()
    {
        // xUnit 2.x has no native Skip — CI pipelines filter out the
        // Category=RequiresDocker trait on hosts without Docker. For dev
        // laptops that lack Docker, emit a clear message into test output
        // so the run isn't confused with a real pass/fail.
        if (!_sim.IsAvailable)
        {
            _output.WriteLine($"[SKIPPED] Modbus simulator not available: {_sim.UnavailableReason}");
            return true;
        }
        return false;
    }
}
