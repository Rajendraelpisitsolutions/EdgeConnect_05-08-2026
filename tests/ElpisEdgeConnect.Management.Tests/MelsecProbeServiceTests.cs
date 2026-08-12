// ============================================================================
// Tests: MelsecProbeService + MelsecProbeApi — read-only probe over a fake
// IMelsecClient. Covers connect, selected/read-all via the planner, skip-invalid
// (never sent to PLC), planned blocks, protocol errors, license gate, raw
// off-by-default, and the probe-only (no-browse) route contract.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class MelsecProbeServiceTests
{
    private sealed class FakeMelsecClient : IMelsecClient
    {
        public bool IsConnected { get; private set; }
        public MelsecClientResult ConnectResult { get; set; } = MelsecClientResult.Connected;
        public Func<MelsecDeviceCode, int, int, MelsecClientResult> ReadBehavior { get; set; } =
            (_, _, _) => MelsecClientResult.Ok(ReadOnlyMemory<byte>.Empty);
        public List<(MelsecDeviceCode Device, int Head, int Points)> Reads { get; } = new();

        public Task<MelsecClientResult> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
        {
            if (ConnectResult.IsSuccess) IsConnected = true;
            return Task.FromResult(ConnectResult);
        }
        public void Disconnect() => IsConnected = false;
        public Task<MelsecClientResult> ReadWordsAsync(MelsecDeviceCode device, int head, int points, CancellationToken ct)
        {
            Reads.Add((device, head, points));
            return Task.FromResult(ReadBehavior(device, head, points));
        }
        public void Dispose() { }
    }

    private static MelsecProbeService Service(FakeMelsecClient fake, bool licensed = true) =>
        new(_ => fake, _ => licensed, NullLoggerFactory.Instance, TimeSpan.FromSeconds(30));

    private static MelsecProbeTagInput Tag(string name, string address, string? datatype = null) =>
        new() { Name = name, Address = address, Datatype = datatype };

    // ── Route contract (probe-only, no browse) ────────────────────────────

    [Fact]
    public void ProbeApi_BaseRoute_IsProbe_NotBrowse()
    {
        MelsecProbeApi.BaseRoute.Should().Be("/api/v1/sources/probe/melsec");
        MelsecProbeApi.BaseRoute.Should().NotContain("browse");
    }

    // ── Connection ────────────────────────────────────────────────────────

    [Fact]
    public async Task TestConnection_success_is_reachable()
    {
        var svc = Service(new FakeMelsecClient());
        var outcome = await svc.TestConnectionAsync(new MelsecTestConnectionRequest { Host = "127.0.0.1", Port = 6000 }, default);

        outcome.Status.Should().Be(MelsecProbeStatus.Success);
        outcome.Result.Outcome.Should().Be("reachable");
    }

    [Fact]
    public async Task TestConnection_refused_is_failure()
    {
        var svc = Service(new FakeMelsecClient { ConnectResult = MelsecClientResult.Transport("refused") });
        var outcome = await svc.TestConnectionAsync(new MelsecTestConnectionRequest { Host = "127.0.0.1", Port = 6000 }, default);

        outcome.Status.Should().Be(MelsecProbeStatus.Failure);
        outcome.Result.Outcome.Should().Be("refused");
    }

    [Fact]
    public async Task License_disabled_blocks_probe()
    {
        var svc = Service(new FakeMelsecClient(), licensed: false);
        var outcome = await svc.TestConnectionAsync(new MelsecTestConnectionRequest { Host = "127.0.0.1", Port = 6000 }, default);

        outcome.Status.Should().Be(MelsecProbeStatus.LicenseDisabled);
        outcome.Result.ErrorCode.Should().Be("MELSEC.PROBE_LICENSE_DISABLED");
    }

    // ── Read ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestRead_selected_tag_decodes_good()
    {
        var fake = new FakeMelsecClient { ReadBehavior = (_, _, _) => MelsecClientResult.Ok(new byte[] { 0x0A, 0x00 }) };
        var req = new MelsecTestReadRequest { Host = "127.0.0.1", Port = 6000, Tags = { Tag("a", "D100", "UInt16") } };

        var outcome = await Service(fake).TestReadAsync(req, default);

        outcome.Status.Should().Be(MelsecProbeStatus.Success);
        outcome.Result.TagResults.Should().ContainSingle();
        var a = outcome.Result.TagResults[0];
        a.Name.Should().Be("a"); a.Quality.Should().Be("Good"); a.Value.Should().Be("10");
        outcome.Result.Blocks.Should().ContainSingle(b => b.Device == "D" && b.HeadDeviceNumber == 100 && b.WordCount == 1);
        fake.Reads.Should().ContainSingle().Which.Should().Be((MelsecDeviceCode.D, 100, 1));
    }

    [Fact]
    public async Task TestRead_skips_invalid_tags_and_never_sends_them()
    {
        var fake = new FakeMelsecClient { ReadBehavior = (_, _, _) => MelsecClientResult.Ok(new byte[] { 0x05, 0x00 }) };
        var req = new MelsecTestReadRequest
        {
            Host = "127.0.0.1", Port = 6000,
            Tags = { Tag("good", "D100", "UInt16"), Tag("bad", "T5", "Bool") },
        };

        var outcome = await Service(fake).TestReadAsync(req, default);

        outcome.Result.Skipped.Should().ContainSingle(s => s.Name == "bad" && s.Code == "MELSEC.DEVICE_NOT_IMPLEMENTED");
        outcome.Result.TagResults.Select(t => t.Name).Should().Equal("good"); // no discovery, only requested-valid tags
        // The invalid tag was never planned, so it was never sent to the PLC.
        fake.Reads.Should().OnlyContain(r => r.Device == MelsecDeviceCode.D);
    }

    [Fact]
    public async Task TestRead_protocol_error_marks_tag_bad_with_end_code()
    {
        var fake = new FakeMelsecClient { ReadBehavior = (_, _, _) => MelsecClientResult.Protocol(0xC056) };
        var req = new MelsecTestReadRequest { Host = "127.0.0.1", Port = 6000, Tags = { Tag("a", "D100", "UInt16") } };

        var outcome = await Service(fake).TestReadAsync(req, default);

        var a = outcome.Result.TagResults.Should().ContainSingle().Subject;
        a.Quality.Should().Be("Bad");
        a.EndCode.Should().Be("0xC056");
    }

    [Fact]
    public async Task TestRead_all_valid_reads_every_block()
    {
        var fake = new FakeMelsecClient { ReadBehavior = (_, _, _) => MelsecClientResult.Ok(new byte[] { 0x01, 0x00 }) };
        var req = new MelsecTestReadRequest
        {
            Host = "127.0.0.1", Port = 6000,
            Tags = { Tag("d", "D100", "UInt16"), Tag("w", "W100", "UInt16") }, // different devices -> two blocks
        };

        var outcome = await Service(fake).TestReadAsync(req, default);

        outcome.Result.Blocks.Should().HaveCount(2);
        outcome.Result.TagResults.Should().HaveCount(2);
        fake.Reads.Should().HaveCount(2);
    }

    [Fact]
    public async Task TestRead_raw_payload_off_by_default_on_when_requested()
    {
        var fake = new FakeMelsecClient { ReadBehavior = (_, _, _) => MelsecClientResult.Ok(new byte[] { 0x0A, 0x00 }) };

        var off = await Service(fake).TestReadAsync(
            new MelsecTestReadRequest { Host = "127.0.0.1", Port = 6000, Tags = { Tag("a", "D100", "UInt16") } }, default);
        off.Result.TagResults[0].RawWordsHex.Should().BeNull();

        var on = await Service(fake).TestReadAsync(
            new MelsecTestReadRequest { Host = "127.0.0.1", Port = 6000, IncludeRaw = true, Tags = { Tag("a", "D100", "UInt16") } }, default);
        on.Result.TagResults[0].RawWordsHex.Should().Be("0A00");
    }
}
