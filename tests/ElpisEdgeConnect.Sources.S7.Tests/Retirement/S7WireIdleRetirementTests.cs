// ============================================================================
// File: Retirement/S7WireIdleRetirementTests.cs
// Purpose: M2 + non-blocking-close proof at the connection-manager level — the
//          wire-idle indicator cannot resolve while a read holds the wire (so it
//          is equivalent to worker exit), and Disconnect() is lock-free (returns
//          promptly even while a wedged read holds the wire).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4;
//            commit-3.0 Modbus pattern review M2 (replicated to S7).
// ============================================================================

using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests.Retirement;

public sealed class S7WireIdleRetirementTests
{
    private static S7SourceConfiguration Config() => new()
    {
        InstanceId = "cm",
        ProtocolName = "s7",
        DeviceId = "plc",
        Host = "127.0.0.1",
    };

    [Fact]
    public async Task WaitForWireIdle_CannotResolveWhileWireHeld_AndCloseIsLockFree()
    {
        var mgr = new S7ConnectionManager(new S7DemoClient(), Config(), NullLogger.Instance);

        // Simulate an in-flight read holding the wire for the full synchronous call.
        var inFlightRead = await mgr.AcquireWireLockAsync();

        // Lock-free close must return PROMPTLY even while the read holds the wire.
        mgr.Disconnect();

        // The wire-idle indicator cannot resolve while the read still holds the wire.
        var idle = mgr.WaitForWireIdleAsync();
        idle.IsCompleted.Should().BeFalse();

        // The read worker exits → releases the wire → the indicator resolves.
        inFlightRead.Dispose();
        await idle;
        idle.IsCompletedSuccessfully.Should().BeTrue();
    }
}
