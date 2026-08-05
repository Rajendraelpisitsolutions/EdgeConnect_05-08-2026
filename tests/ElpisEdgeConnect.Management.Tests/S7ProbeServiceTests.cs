// ============================================================================
// Tests: S7ProbeService (M.2b.2) — the wizard's read-only Test Connection /
//        Test Read probes against a fake IS7Client. Pins: connect outcomes
//        (reachable / refused / timeout), read outcomes (read-ok / read-failed
//        / DB-access-denied), the read-only invariant (never writes), the
//        license gate, and that a malformed selected tag is reported as a
//        config error — never as a connection failure.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Sources.S7;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class S7ProbeServiceTests
{
    private static S7ProbeService Service(FakeS7Client client, bool licensed = true) =>
        new(
            clientFactory: () => client,
            isModuleEnabled: _ => licensed,
            loggerFactory: NullLoggerFactory.Instance,
            probeBudget: TimeSpan.FromSeconds(5));

    private static S7TestConnectionRequest Conn() =>
        new() { Host = "10.0.0.9", Port = 102, Rack = 0, Slot = 1, ConnectionType = "Basic" };

    private static S7TestReadRequest Read(string address = "DB1.DBW0", string? datatype = "Int") =>
        new() { Host = "10.0.0.9", Port = 102, Rack = 0, Slot = 1, Address = address, Datatype = datatype };

    // ── Test connection ──────────────────────────────────────────────────

    [Fact]
    public async Task TestConnection_Connected_IsReachable()
    {
        var client = new FakeS7Client { ConnectResult = S7OperationResult.Ok };

        var outcome = await Service(client).TestConnectionAsync(Conn(), CancellationToken.None);

        outcome.Status.Should().Be(S7ProbeStatus.Success);
        outcome.Result.Success.Should().BeTrue();
        outcome.Result.Outcome.Should().Be(S7ProbeOutcomes.Reachable);
        client.ConnectCalls.Should().Be(1);
    }

    [Fact]
    public async Task TestConnection_ConnectFails_IsRefused()
    {
        var client = new FakeS7Client { ConnectResult = S7OperationResult.Fail(-2, "connection refused") };

        var outcome = await Service(client).TestConnectionAsync(Conn(), CancellationToken.None);

        outcome.Status.Should().Be(S7ProbeStatus.Failure);
        outcome.Result.Success.Should().BeFalse();
        outcome.Result.Outcome.Should().Be(S7ProbeOutcomes.Refused);
    }

    [Fact]
    public async Task TestConnection_BudgetExpires_IsTimeout()
    {
        var client = new FakeS7Client { HangOnConnect = true };
        var service = new S7ProbeService(
            clientFactory: () => client,
            isModuleEnabled: _ => true,
            loggerFactory: NullLoggerFactory.Instance,
            probeBudget: TimeSpan.Zero); // fire the budget immediately

        var outcome = await service.TestConnectionAsync(Conn(), CancellationToken.None);

        outcome.Status.Should().Be(S7ProbeStatus.Failure);
        outcome.Result.Outcome.Should().Be(S7ProbeOutcomes.Timeout);
    }

    [Fact]
    public async Task TestConnection_LicenseDisabled_Returns403Status()
    {
        var client = new FakeS7Client { ConnectResult = S7OperationResult.Ok };

        var outcome = await Service(client, licensed: false).TestConnectionAsync(Conn(), CancellationToken.None);

        outcome.Status.Should().Be(S7ProbeStatus.LicenseDisabled);
        client.ConnectCalls.Should().Be(0, "the gate blocks before any network contact");
    }

    // ── Test read ──────────────────────────────────────────────────────

    [Fact]
    public async Task TestRead_ReadSucceeds_DecodesValue()
    {
        // Word 0x0005 big-endian → 5.
        var client = new FakeS7Client { ConnectResult = S7OperationResult.Ok, ReadResult = S7OperationResult.Ok, ReadBytes = new byte[] { 0x00, 0x05 } };

        var outcome = await Service(client).TestReadAsync(Read("DB1.DBW0", "Int"), CancellationToken.None);

        outcome.Status.Should().Be(S7ProbeStatus.Success);
        outcome.Result.Outcome.Should().Be(S7ProbeOutcomes.ReadOk);
        outcome.Result.Value.Should().Be("5");
        client.ReadCalls.Should().Be(1);
        client.WriteCalls.Should().Be(0, "the probe is strictly read-only");
    }

    [Fact]
    public async Task TestRead_ReadFails_IsReadFailed()
    {
        var client = new FakeS7Client { ConnectResult = S7OperationResult.Ok, ReadResult = S7OperationResult.Fail(1, "item not available") };

        var outcome = await Service(client).TestReadAsync(Read("DB1.DBW0", "Int"), CancellationToken.None);

        outcome.Result.Outcome.Should().Be(S7ProbeOutcomes.ReadFailed);
    }

    [Fact]
    public async Task TestRead_DbReadRefusedForAccess_IsDbAccessDenied()
    {
        var client = new FakeS7Client { ConnectResult = S7OperationResult.Ok, ReadResult = S7OperationResult.Fail(5, "CPU : Function refused — access denied") };

        var outcome = await Service(client).TestReadAsync(Read("DB1.DBW0", "Int"), CancellationToken.None);

        outcome.Result.Outcome.Should().Be(S7ProbeOutcomes.DbAccessDenied);
    }

    [Fact]
    public async Task TestRead_MalformedTag_IsConfigInvalid_NotConnectionFailure()
    {
        var client = new FakeS7Client { ConnectResult = S7OperationResult.Ok };

        var outcome = await Service(client).TestReadAsync(Read("DB1.DBZ0", "Int"), CancellationToken.None);

        outcome.Result.Outcome.Should().Be(S7ProbeOutcomes.ConfigInvalid);
        client.ConnectCalls.Should().Be(0, "a malformed tag must not look like a connection failure");
    }

    [Fact]
    public async Task TestRead_ConnectFails_IsConnectFailed_NotReadFailed()
    {
        var client = new FakeS7Client { ConnectResult = S7OperationResult.Fail(-2, "refused") };

        var outcome = await Service(client).TestReadAsync(Read("DB1.DBW0", "Int"), CancellationToken.None);

        outcome.Result.Outcome.Should().Be(S7ProbeOutcomes.ConnectFailed);
        client.ReadCalls.Should().Be(0);
    }

    // ── Fake transport ──────────────────────────────────────────────────

    private sealed class FakeS7Client : IS7Client
    {
        public S7OperationResult ConnectResult { get; init; } = S7OperationResult.Ok;
        public S7OperationResult ReadResult { get; init; } = S7OperationResult.Ok;
        public byte[]? ReadBytes { get; init; }
        public bool HangOnConnect { get; init; }

        public int ConnectCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }

        public bool IsConnected { get; private set; }

        public async Task<S7OperationResult> ConnectAsync(
            string host, int port, int rack, int slot, S7ConnectionType connectionType, TimeSpan timeout, CancellationToken ct)
        {
            ConnectCalls++;
            if (HangOnConnect)
            {
                // Wait until the probe budget cancels the linked token.
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }
            IsConnected = ConnectResult.Success;
            return ConnectResult;
        }

        public void Disconnect() => IsConnected = false;

        public Task<S7OperationResult> ReadAreaAsync(
            S7MemoryArea area, int dbNumber, int startByte, int byteCount, byte[] buffer, CancellationToken ct)
        {
            ReadCalls++;
            if (ReadResult.Success && ReadBytes is not null)
            {
                var n = Math.Min(ReadBytes.Length, buffer.Length);
                Array.Copy(ReadBytes, buffer, n);
            }
            return Task.FromResult(ReadResult);
        }

        public void Dispose() { }
    }
}
