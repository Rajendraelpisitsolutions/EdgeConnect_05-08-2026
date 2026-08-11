// ============================================================================
// File: Retirement/ModbusWireIdleRetirementTests.cs
// Purpose: M2 + non-blocking-close proof at the connection-manager level — the
//          wire-idle indicator cannot resolve while a read holds the wire (so it
//          is equivalent to worker exit), and Disconnect() is lock-free (returns
//          promptly even while a wedged read holds the wire; it must NOT acquire
//          the wire lock to close).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4;
//            commit-3.0 Modbus pattern review M2.
// ============================================================================

using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Retirement;

public sealed class ModbusWireIdleRetirementTests
{
    private static ModbusTcpSourceConfiguration Config() => new()
    {
        InstanceId = "cm",
        ProtocolName = "modbustcp",
        DeviceId = "plc",
        Host = "127.0.0.1",
        Port = 502,
        ConnectTimeoutMs = 500,
        RequestTimeoutMs = 500,
        InitialBackoffMs = 1000,
        MaxBackoffMs = 60_000,
        BackoffMultiplier = 2.0,
        CircuitBreakerThreshold = 3,
        CircuitBreakerResetMs = 5_000,
    };

    [Fact]
    public async Task WaitForWireIdle_CannotResolveWhileWireHeld_AndCloseIsLockFree()
    {
        var mgr = new ModbusConnectionManager(new FakeModbusClient(), Config(), "cm", NullLogger.Instance);

        // Simulate an in-flight read holding the wire for the full synchronous call.
        var inFlightRead = await mgr.AcquireWireLockAsync(default);

        // Lock-free close must return PROMPTLY even while the read holds the wire —
        // it must NOT acquire the wire lock to disconnect (else it would deadlock).
        mgr.Disconnect();

        // The wire-idle indicator is equivalent to worker exit: it cannot resolve
        // while the read still holds the wire.
        var idle = mgr.WaitForWireIdleAsync();
        idle.IsCompleted.Should().BeFalse();

        // The read worker exits → releases the wire → the indicator resolves.
        inFlightRead.Dispose();
        await idle;
        idle.IsCompletedSuccessfully.Should().BeTrue();
    }
}
