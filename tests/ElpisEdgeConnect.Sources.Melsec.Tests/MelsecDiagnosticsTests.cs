// ============================================================================
// Tests: MelsecSourceAdapter observational diagnostics (IMelsecDiagnosticsProvider).
// Proves the snapshot reflects route/scan-blocks/end-code/affected-tags/per-tag
// quality/latency, is copy-on-read, does NOT mutate adapter state, and is safe
// (no throw / no deadlock) before init and after stop/retirement.
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Diagnostics;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Melsec.Tests;

public sealed class MelsecDiagnosticsTests
{
    private sealed class FakeMelsecClient : IMelsecClient
    {
        public bool IsConnected { get; private set; }
        public int ReadCalls { get; private set; }
        public Func<MelsecDeviceCode, int, int, MelsecClientResult> ReadBehavior { get; set; } =
            (_, _, _) => MelsecClientResult.Ok(ReadOnlyMemory<byte>.Empty);
        public Task<MelsecClientResult> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
        { IsConnected = true; return Task.FromResult(MelsecClientResult.Connected); }
        public void Disconnect() => IsConnected = false;
        public Task<MelsecClientResult> ReadWordsAsync(MelsecDeviceCode device, int head, int points, CancellationToken ct)
        { ReadCalls++; return Task.FromResult(ReadBehavior(device, head, points)); }
        public void Dispose() { }
    }

    private sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static MelsecSourceConfiguration Config() => new()
    {
        InstanceId = "melsec-1",
        ProtocolName = "melsec",
        DeviceId = "dev1",
        Host = "127.0.0.1",
        Port = 6000,
        NetworkNo = 1,
        PcNo = 0x7E,
        RequestDestModuleIoNo = 0x03FF,
        RequestDestModuleStationNo = 2,
        TagDefinitions = new[]
        {
            new MelsecTagDefinition { Name = "a", Address = "D100", Datatype = "UInt16", ScanRateMs = 1000 },
            new MelsecTagDefinition { Name = "b", Address = "D101", Datatype = "UInt16", ScanRateMs = 1000 },
        },
    };

    private static async Task<(MelsecSourceAdapter Adapter, FakeMelsecClient Client)> StartAsync(FakeMelsecClient client)
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var adapter = new MelsecSourceAdapter("melsec-1", client, NullLogger.Instance, gatewayIdentity: null, time);
        await adapter.InitializeAsync(Config(), default);
        await adapter.StartAsync(default);
        return (adapter, client);
    }

    private static MelsecDiagnosticsSnapshot Snapshot(MelsecSourceAdapter adapter) =>
        ((IMelsecDiagnosticsProvider)adapter).GetDiagnosticsSnapshot();

    [Fact]
    public void Snapshot_before_init_is_safe_and_empty()
    {
        var adapter = new MelsecSourceAdapter("melsec-1", new FakeMelsecClient(), NullLogger.Instance);
        var snap = Snapshot(adapter);

        snap.InstanceId.Should().Be("melsec-1");
        snap.ScanBlocks.Should().BeEmpty();
        snap.TagQuality.Should().BeEmpty();
        snap.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task Snapshot_reflects_route_and_scan_blocks_before_first_poll()
    {
        var (adapter, _) = await StartAsync(new FakeMelsecClient());
        var snap = Snapshot(adapter);

        snap.Route.Should().Be(new MelsecRouteDiagnostics(1, 0x7E, 0x03FF, 2));
        snap.ScanBlocks.Should().ContainSingle();
        var block = snap.ScanBlocks[0];
        block.DeviceSymbol.Should().Be("D");
        block.HeadDeviceNumber.Should().Be(100);
        block.WordCount.Should().Be(2);
        block.LastResult.Should().Be(MelsecBlockResult.NotYetPolled);
        snap.TagQuality.Should().OnlyContain(t => t.Quality == "Unknown");
        snap.LastRequestLatencyMs.Should().BeNull();
    }

    [Fact]
    public async Task Snapshot_after_success_poll_is_good_with_latency()
    {
        var client = new FakeMelsecClient { ReadBehavior = (_, _, _) => MelsecClientResult.Ok(new byte[] { 0x0A, 0x00, 0x14, 0x00 }) };
        var (adapter, _) = await StartAsync(client);
        await adapter.PollAsync(default);

        var snap = Snapshot(adapter);
        snap.ScanBlocks[0].LastResult.Should().Be(MelsecBlockResult.Good);
        snap.TagQuality.Should().OnlyContain(t => t.Quality == "Good");
        snap.AffectedTags.Should().BeEmpty();
        snap.LastRequestLatencyMs.Should().NotBeNull();
        snap.LastEndCode.Should().BeNull();
    }

    [Fact]
    public async Task Snapshot_after_protocol_error_sets_end_code_and_affected_tags()
    {
        var client = new FakeMelsecClient { ReadBehavior = (_, _, _) => MelsecClientResult.Protocol(0xC056) };
        var (adapter, _) = await StartAsync(client);
        await adapter.PollAsync(default);

        var snap = Snapshot(adapter);
        snap.ScanBlocks[0].LastResult.Should().Be(MelsecBlockResult.ProtocolError);
        snap.ScanBlocks[0].LastEndCode.Should().Be(0xC056);
        snap.LastEndCode.Should().Be(0xC056);
        snap.LastEndCodeDescription.Should().NotBeNullOrEmpty();
        snap.AffectedTags.Should().BeEquivalentTo(new[] { "a", "b" });
        snap.TagQuality.Should().OnlyContain(t => t.Quality == "Bad");
    }

    [Fact]
    public async Task Snapshot_is_observational_and_does_not_mutate_state()
    {
        var client = new FakeMelsecClient { ReadBehavior = (_, _, _) => MelsecClientResult.Ok(new byte[] { 0x01, 0x00, 0x02, 0x00 }) };
        var (adapter, _) = await StartAsync(client);
        await adapter.PollAsync(default);

        var healthBefore = await adapter.CheckHealthAsync(default);
        var readsBefore = client.ReadCalls;

        for (var i = 0; i < 10; i++) _ = Snapshot(adapter);

        var healthAfter = await adapter.CheckHealthAsync(default);
        client.ReadCalls.Should().Be(readsBefore, "reading diagnostics must not touch the wire");
        var before = healthBefore.Metrics!;
        var after = healthAfter.Metrics!;
        after["pollAttempts"].Should().Be(before["pollAttempts"]);
        after["readsExecuted"].Should().Be(before["readsExecuted"]);
        adapter.State.Should().Be(healthBefore.State);
    }

    [Fact]
    public async Task Snapshot_is_safe_after_stop()
    {
        var (adapter, _) = await StartAsync(new FakeMelsecClient());
        await adapter.StopAsync(default);

        var act = () => Snapshot(adapter);
        act.Should().NotThrow();
        Snapshot(adapter).Connected.Should().BeFalse();
    }

    [Fact]
    public async Task Snapshot_is_safe_after_retirement()
    {
        var (adapter, _) = await StartAsync(new FakeMelsecClient());
        _ = adapter.BeginRetirement(new AdapterRetirementContext { ObservationToken = default });

        var act = () => Snapshot(adapter);
        act.Should().NotThrow();
    }
}
