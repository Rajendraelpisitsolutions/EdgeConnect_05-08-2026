// ============================================================================
// File: ModbusTransactionExecutorTests.cs
// Purpose: Unit tests for ModbusTransactionExecutor — FC01–FC04 dispatch,
//          per-transaction retry budget, slave-exception handling, and
//          request validation (FC-limit, address overflow).
// ============================================================================

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusTransactionExecutorTests
{
    private static ModbusTcpSourceConfiguration Config(int maxRetries = 2) => new()
    {
        InstanceId = "tx-test",
        ProtocolName = "modbustcp",
        DeviceId = "plc",
        Host = "127.0.0.1",
        Port = 502,
        MaxTransactionRetries = maxRetries,
        // Tiny backoff — we are testing the retry budget, not the backoff
        // gate. Small values keep the test wall-clock deterministic: the
        // ComputeRetryDelay inside the executor (100ms for attempt 0) already
        // clears any realistic nextRetryAt.
        InitialBackoffMs = 1,
        MaxBackoffMs = 100,
        BackoffMultiplier = 2.0,
        CircuitBreakerThreshold = 100, // never trip during these tests
        CircuitBreakerResetMs = 1000,
        ConnectTimeoutMs = 100,
        RequestTimeoutMs = 100,
    };

    private static async Task<(FakeModbusClient client, ModbusConnectionManager mgr, ModbusTransactionExecutor exec)>
        SetupAsync(ModbusTcpSourceConfiguration? cfg = null)
    {
        cfg ??= Config();
        var client = new FakeModbusClient();
        var mgr = new ModbusConnectionManager(client, cfg, "tx-test", NullLogger.Instance);
        await mgr.EnsureConnectedAsync(default);
        var exec = new ModbusTransactionExecutor(client, mgr, cfg, "tx-test", NullLogger.Instance);
        return (client, mgr, exec);
    }

    [Fact]
    public async Task Execute_Fc03_Success_ReturnsRegisters()
    {
        var (client, _, exec) = await SetupAsync();
        client.HoldingRegisterResults.Enqueue(() => [100, 200, 300]);

        var result = await exec.ExecuteAsync(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 3),
            default);

        result.IsSuccess.Should().BeTrue();
        result.RegisterPayload.Should().Equal(new ushort[] { 100, 200, 300 });
        result.BitPayload.Should().BeNull();
        result.RetryCount.Should().Be(0);
        client.Calls.Should().ContainSingle(c =>
            c.FunctionCode == 0x03 && c.UnitId == 1 && c.StartAddress == 0 && c.Quantity == 3);
    }

    [Theory]
    [InlineData(ModbusRegisterClass.Coil, (byte)0x01)]
    [InlineData(ModbusRegisterClass.DiscreteInput, (byte)0x02)]
    [InlineData(ModbusRegisterClass.HoldingRegister, (byte)0x03)]
    [InlineData(ModbusRegisterClass.InputRegister, (byte)0x04)]
    public async Task Execute_DispatchesCorrectFunctionCode(ModbusRegisterClass rc, byte expectedFc)
    {
        var (client, _, exec) = await SetupAsync();

        await exec.ExecuteAsync(new ModbusReadRequest(2, rc, 10, 4), default);

        client.Calls.Should().ContainSingle(c => c.FunctionCode == expectedFc && c.UnitId == 2);
    }

    [Fact]
    public async Task Execute_FatalError_RetriesUpToBudget()
    {
        var cfg = Config(maxRetries: 3);
        var (client, mgr, exec) = await SetupAsync(cfg);
        client.ReadException = new ModbusFatalException(ModbusErrors.SocketError, "peer reset");

        var result = await exec.ExecuteAsync(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 1),
            default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ModbusErrors.SocketError);
        result.RetryCount.Should().Be(3);
        // One call per attempt (maxRetries + the initial attempt).
        client.Calls.Count.Should().Be(1 + 3);
        mgr.ConsecutiveFailures.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Execute_SlaveException_DoesNotRetry()
    {
        var (client, _, exec) = await SetupAsync(Config(maxRetries: 5));
        client.ReadException = new ModbusSlaveException(0x03, 0x02, "Illegal Data Address");

        var result = await exec.ExecuteAsync(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 1),
            default);

        result.IsSuccess.Should().BeFalse();
        result.SlaveExceptionCode.Should().Be(0x02);
        result.Error!.Code.Should().Be(ModbusErrors.SlaveException);
        result.RetryCount.Should().Be(0);
        client.Calls.Should().ContainSingle(); // no retries
    }

    [Fact]
    public async Task Execute_ZeroQuantity_FailsValidationWithoutCallingClient()
    {
        var (client, _, exec) = await SetupAsync();

        var result = await exec.ExecuteAsync(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 0),
            default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ModbusErrors.ConfigOutOfRange);
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_ExceedsFcLimit_FailsValidationWithoutCallingClient()
    {
        var (client, _, exec) = await SetupAsync();

        // FC03 hard limit is 125 registers.
        var result = await exec.ExecuteAsync(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 126),
            default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ModbusErrors.RequestTooLarge);
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_ExceedsFcLimit_Coils_FailsValidation()
    {
        var (client, _, exec) = await SetupAsync();

        // FC01 hard limit is 2000 bits.
        var result = await exec.ExecuteAsync(
            new ModbusReadRequest(1, ModbusRegisterClass.Coil, 0, 2001),
            default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ModbusErrors.RequestTooLarge);
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_AddressOverflow_FailsValidation()
    {
        var (_, _, exec) = await SetupAsync();

        var result = await exec.ExecuteAsync(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 65_530, 10),
            default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ModbusErrors.ConfigOutOfRange);
    }

    [Fact]
    public async Task Execute_NotConnected_NoConfigError_TriesConnectAndFailsGracefully()
    {
        var cfg = Config(maxRetries: 0);
        var client = new FakeModbusClient
        {
            ConnectException = new ModbusFatalException(ModbusErrors.ConnectFailed, "offline"),
        };
        var mgr = new ModbusConnectionManager(client, cfg, "tx-test", NullLogger.Instance);
        var exec = new ModbusTransactionExecutor(client, mgr, cfg, "tx-test", NullLogger.Instance);

        var result = await exec.ExecuteAsync(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 1),
            default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ModbusErrors.SocketError);
    }

    [Fact]
    public async Task Execute_SuccessAfterOneFatalRetry_ReportsRetryCountOne()
    {
        var (client, _, exec) = await SetupAsync(Config(maxRetries: 2));
        var firstAttempt = true;
        client.HoldingRegisterResults.Enqueue(() =>
        {
            if (firstAttempt)
            {
                firstAttempt = false;
                throw new ModbusFatalException(ModbusErrors.SocketError, "transient reset");
            }
            return [42];
        });
        client.HoldingRegisterResults.Enqueue(() => [42]);

        var result = await exec.ExecuteAsync(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 1),
            default);

        result.IsSuccess.Should().BeTrue();
        result.RegisterPayload.Should().Equal(new ushort[] { 42 });
        result.RetryCount.Should().Be(1);
    }
}
