// ============================================================================
// Tests: ModbusProbeService — pins the Modbus probe ladder (M.2d.2 v2 §4.3).
//        Key invariants:
//
//          * Step 1 — TCP connect failures (refused / timeout) short-circuit.
//          * Step 2 — first configured tag drives FC + address.
//          * Step 3 — fallback uses operator overrides OR FC03 / addr 1 / qty 1
//            (NEVER addr 0 — many vendors reject it).
//          * License gate short-circuits before any transport call.
//          * Single-flight per IP:Port:UnitId (different unit ids on the same
//            host don't contend).
//          * Diagnostic fields populated for every outcome.
//          * Slave exception maps to MODBUS.PROBE_SLAVE_REJECTED, not TCP error.
//          * Read-only invariant — transport's ReadAsync only ever uses
//            FC01/02/03/04 (never FC05/06/15/16).
//          * Status code mapping per v2 §4.6.
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §4
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Api;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class ModbusProbeServiceTests
{
    // ─── Ladder step resolution (pure) ──────────────────────────────────

    [Fact]
    public void ResolveReadTarget_NoTagNoOverrides_UsesFallbackDefault()
    {
        // v2 §4.3 step 3 default: FC03 / addr 1 / qty 1. NEVER addr 0.
        var request = new ModbusProbeRequest { IpAddress = "10.0.0.1" };

        var (step, fc, addr, qty) = ModbusProbeService.ResolveReadTarget(request);

        step.Should().Be(ModbusProbeStep.FallbackTestAddress);
        fc.Should().Be(0x03, "default function code = FC03 Read Holding Registers");
        addr.Should().Be(1, "default address = 1, NEVER 0 (vendor compatibility)");
        qty.Should().Be(1);
    }

    [Fact]
    public void ResolveReadTarget_OverridesPresent_UsesOverrides()
    {
        var request = new ModbusProbeRequest
        {
            IpAddress = "10.0.0.1",
            Overrides = new ModbusProbeOverrides { FunctionCode = 0x04, Address = 500, Quantity = 2 },
        };

        var (step, fc, addr, qty) = ModbusProbeService.ResolveReadTarget(request);

        step.Should().Be(ModbusProbeStep.FallbackTestAddress);
        fc.Should().Be(0x04);
        addr.Should().Be(500);
        qty.Should().Be(2);
    }

    [Fact]
    public void ResolveReadTarget_FirstTagPresent_UsesTag_OverridesIgnored()
    {
        // Step 2 takes precedence — if the wizard already has tags, we
        // probe the first one. Overrides are step-3-only.
        var request = new ModbusProbeRequest
        {
            IpAddress = "10.0.0.1",
            FirstConfiguredTag = new ModbusProbeTagTarget(0x04, 1000, 1),
            Overrides = new ModbusProbeOverrides { FunctionCode = 0x03 },
        };

        var (step, fc, addr, qty) = ModbusProbeService.ResolveReadTarget(request);

        step.Should().Be(ModbusProbeStep.FirstConfiguredTag);
        fc.Should().Be(0x04);
        addr.Should().Be(1000);
        qty.Should().Be(1);
    }

    // ─── Happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_HappyPath_FallbackAddress_SucceedsWithDiagnostics()
    {
        var transport = new FakeTransport();
        var service = MakeService(() => transport);

        var outcome = await service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = "10.0.0.1", UnitId = 7 },
            CancellationToken.None);

        outcome.Status.Should().Be(ModbusProbeStatus.Success);
        outcome.Result.Success.Should().BeTrue();
        outcome.Result.FunctionCodeUsed.Should().Be(0x03);
        outcome.Result.AddressTested.Should().Be(1);
        outcome.Result.UnitIdTested.Should().Be(7);
        outcome.Result.ProbeStepReached.Should().Be(ModbusProbeStep.FallbackTestAddress);
        transport.ConnectCount.Should().Be(1);
        transport.ReadCount.Should().Be(1);
        transport.LastReadFunctionCode.Should().Be(0x03);
        transport.WriteFunctionsAttempted.Should().BeEmpty("read-only — never FC05/06/15/16");
    }

    [Fact]
    public async Task ProbeAsync_FirstConfiguredTag_PathHits_Step2()
    {
        var transport = new FakeTransport();
        var service = MakeService(() => transport);

        var outcome = await service.ProbeAsync(
            new ModbusProbeRequest
            {
                IpAddress = "10.0.0.1",
                UnitId = 1,
                FirstConfiguredTag = new ModbusProbeTagTarget(0x04, 2000, 1),
            },
            CancellationToken.None);

        outcome.Status.Should().Be(ModbusProbeStatus.Success);
        outcome.Result.FunctionCodeUsed.Should().Be(0x04);
        outcome.Result.AddressTested.Should().Be(2000);
        outcome.Result.ProbeStepReached.Should().Be(ModbusProbeStep.FirstConfiguredTag);
    }

    // ─── License gate ───────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_LicenseDisabled_DoesNotTouchTransport()
    {
        var transport = new FakeTransport();
        var service = MakeService(() => transport, isModuleEnabled: _ => false);

        var outcome = await service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = "10.0.0.1" },
            CancellationToken.None);

        outcome.Status.Should().Be(ModbusProbeStatus.LicenseDisabled);
        outcome.Result.ErrorCode.Should().Be("MODBUS.PROBE_LICENSE_DISABLED");
        transport.ConnectCount.Should().Be(0);
    }

    // ─── Single-flight (target-keyed IP:Port:UnitId) ─────────────────────

    [Fact]
    public async Task ProbeAsync_SecondProbeSameTarget_ReturnsBusy()
    {
        var slowGate = new TaskCompletionSource();
        var probeStarted = new TaskCompletionSource();
        var transport = new FakeTransport
        {
            ConnectHook = async _ =>
            {
                probeStarted.TrySetResult();
                await slowGate.Task;
            },
        };
        var service = MakeService(() => transport);

        var first = service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = "10.0.0.1", Port = 502, UnitId = 1 },
            CancellationToken.None);

        await probeStarted.Task;

        var secondOutcome = await service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = "10.0.0.1", Port = 502, UnitId = 1 },
            CancellationToken.None);

        secondOutcome.Status.Should().Be(ModbusProbeStatus.Busy);
        secondOutcome.Result.ErrorCode.Should().Be("MODBUS.PROBE_BUSY");

        slowGate.SetResult();
        await first;
    }

    [Fact]
    public async Task ProbeAsync_DifferentUnitIdSameHost_DoesNotContend()
    {
        // Target identity is IP:Port:UnitId — two different units on the
        // same gateway must probe independently (commissioning scenario).
        var slowGate = new TaskCompletionSource();
        var probeStarted = new TaskCompletionSource();

        // Queue of transports: slow first, fast second.
        var transports = new Queue<FakeTransport>();
        transports.Enqueue(new FakeTransport
        {
            ConnectHook = async _ =>
            {
                probeStarted.TrySetResult();
                await slowGate.Task;
            },
        });
        transports.Enqueue(new FakeTransport());

        var service = MakeService(() => transports.Dequeue());

        var slow = service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = "10.0.0.1", Port = 502, UnitId = 1 },
            CancellationToken.None);

        await probeStarted.Task;

        var fastOutcome = await service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = "10.0.0.1", Port = 502, UnitId = 2 },
            CancellationToken.None);

        fastOutcome.Status.Should().Be(ModbusProbeStatus.Success);

        slowGate.SetResult();
        await slow;
    }

    // ─── Failure modes ──────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_TcpConnectRefused_FailsAtStep1()
    {
        var transport = new FakeTransport
        {
            ConnectHook = _ => throw new SocketException((int)SocketError.ConnectionRefused),
        };
        var service = MakeService(() => transport);

        var outcome = await service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = "10.0.0.1" },
            CancellationToken.None);

        outcome.Status.Should().Be(ModbusProbeStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("MODBUS.PROBE_TCP_REFUSED");
        outcome.Result.ProbeStepReached.Should().Be(ModbusProbeStep.TcpConnect);
        outcome.Result.FunctionCodeUsed.Should().BeNull("never reached the read step");
        transport.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task ProbeAsync_SlaveException_MapsToSlaveRejected()
    {
        // PLC IS reachable, but rejected the address. Distinct from TCP
        // failure — wizard renders a different remediation hint.
        var transport = new FakeTransport
        {
            ReadHook = (_, _, _, _) => throw new ModbusProbeSlaveRejectedException("Illegal data address"),
        };
        var service = MakeService(() => transport);

        var outcome = await service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = "10.0.0.1", UnitId = 1 },
            CancellationToken.None);

        outcome.Status.Should().Be(ModbusProbeStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("MODBUS.PROBE_SLAVE_REJECTED");
        outcome.Result.ProbeStepReached.Should().Be(ModbusProbeStep.FallbackTestAddress);
        outcome.Result.FunctionCodeUsed.Should().Be(0x03);
        outcome.Result.AddressTested.Should().Be(1);
    }

    // ─── Config validation ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProbeAsync_MissingIp_FailsWithConfigInvalid(string? ip)
    {
        var service = MakeService(() => new FakeTransport());

        var outcome = await service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = ip! },
            CancellationToken.None);

        outcome.Status.Should().Be(ModbusProbeStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("MODBUS.PROBE_CONFIG_INVALID");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task ProbeAsync_InvalidPort_FailsWithConfigInvalid(int port)
    {
        var service = MakeService(() => new FakeTransport());

        var outcome = await service.ProbeAsync(
            new ModbusProbeRequest { IpAddress = "10.0.0.1", Port = port },
            CancellationToken.None);

        outcome.Status.Should().Be(ModbusProbeStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("MODBUS.PROBE_CONFIG_INVALID");
    }

    // ─── Status mapping (pure) ──────────────────────────────────────────

    [Theory]
    [InlineData(ModbusProbeStatus.Success, 200)]
    [InlineData(ModbusProbeStatus.Failure, 200)]   // §4.6 invariant — render inline
    [InlineData(ModbusProbeStatus.LicenseDisabled, 403)]
    [InlineData(ModbusProbeStatus.Busy, 409)]
    public void StatusCodeFor_MapsCorrectly(ModbusProbeStatus status, int expectedHttp)
    {
        ModbusProbeStatusMapping.StatusCodeFor(status).Should().Be(expectedHttp);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static ModbusProbeService MakeService(
        Func<IModbusProbeTransport> transportFactory,
        Func<string, bool>? isModuleEnabled = null,
        TimeSpan? probeBudget = null)
    {
        return new ModbusProbeService(
            transportFactory: transportFactory,
            isModuleEnabled: isModuleEnabled ?? (_ => true),
            loggerFactory: NullLoggerFactory.Instance,
            probeBudget: probeBudget ?? TimeSpan.FromSeconds(8));
    }

    private sealed class FakeTransport : IModbusProbeTransport
    {
        public int ConnectCount;
        public int ReadCount;
        public byte? LastReadFunctionCode;
        public ConcurrentBag<byte> WriteFunctionsAttempted { get; } = new();

        /// <summary>Optional hook for connect side-effects; default: succeed.</summary>
        public Func<CancellationToken, Task>? ConnectHook { get; set; }

        /// <summary>Optional hook for read side-effects; default: succeed.</summary>
        public Func<byte, byte, ushort, ushort, Task>? ReadHook { get; set; }

        public async Task ConnectAsync(string host, int port, TimeSpan connectTimeout, CancellationToken ct)
        {
            Interlocked.Increment(ref ConnectCount);
            if (ConnectHook is { } hook)
            {
                await hook(ct);
            }
        }

        public async Task ReadAsync(byte unitId, byte functionCode, ushort address, ushort quantity, CancellationToken ct)
        {
            Interlocked.Increment(ref ReadCount);
            LastReadFunctionCode = functionCode;

            // Defensive: assert the probe never asks us to write.
            if (functionCode is 0x05 or 0x06 or 0x0F or 0x10)
            {
                WriteFunctionsAttempted.Add(functionCode);
            }

            if (ReadHook is { } hook)
            {
                await hook(unitId, functionCode, address, quantity);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
