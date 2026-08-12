// ============================================================================
// Tests: MelsecSourceAdapter — lifecycle, validation, poll/decode, partial-block
//        isolation, reconnect/backoff, and retirement — all against a fake
//        IMelsecClient. No real network.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Melsec.Tests;

public class MelsecSourceAdapterTests
{
    // ---- Test doubles ------------------------------------------------------

    private sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class FakeMelsecClient : IMelsecClient
    {
        public bool IsConnected { get; private set; }
        public int ConnectCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public (MelsecDeviceCode Device, int Head, int Points)? LastRead { get; private set; }

        public Func<int, MelsecClientResult>? ConnectBehavior { get; set; }
        public Func<MelsecDeviceCode, int, int, MelsecClientResult>? ReadBehavior { get; set; }
        public TaskCompletionSource<MelsecClientResult>? ReadGate { get; set; }

        public Task<MelsecClientResult> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
        {
            ConnectCalls++;
            var result = ConnectBehavior?.Invoke(ConnectCalls) ?? MelsecClientResult.Connected;
            if (result.IsSuccess) IsConnected = true;
            return Task.FromResult(result);
        }

        public void Disconnect()
        {
            DisconnectCalls++;
            IsConnected = false;
            ReadGate?.TrySetResult(MelsecClientResult.Transport("socket closed"));
        }

        public async Task<MelsecClientResult> ReadWordsAsync(MelsecDeviceCode device, int headDeviceNumber, int points, CancellationToken ct)
        {
            ReadCalls++;
            LastRead = (device, headDeviceNumber, points);
            if (ReadGate is not null)
            {
                using var reg = ct.Register(() => ReadGate.TrySetCanceled(ct));
                return await ReadGate.Task.ConfigureAwait(false);
            }
            return ReadBehavior?.Invoke(device, headDeviceNumber, points) ?? MelsecClientResult.Ok(ReadOnlyMemory<byte>.Empty);
        }

        public void Dispose() { }
    }

    // ---- Helpers -----------------------------------------------------------

    private static MelsecTagDefinition Tag(string name, string address, string? datatype = null, int scanRateMs = 1000) =>
        new() { Name = name, Address = address, Datatype = datatype, ScanRateMs = scanRateMs };

    private static MelsecSourceConfiguration Config(params MelsecTagDefinition[] tags) => new()
    {
        InstanceId = "melsec-1",
        ProtocolName = "melsec",
        DeviceId = "dev1",
        DeviceClass = "plc",
        Host = "127.0.0.1",
        Port = 6000,
        TagDefinitions = tags,
    };

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<MelsecSourceAdapter> StartAsync(MelsecSourceConfiguration cfg, FakeMelsecClient client, TimeProvider? time = null)
    {
        var adapter = new MelsecSourceAdapter(cfg.InstanceId, client, NullLogger.Instance, gatewayIdentity: null, time ?? new TestTimeProvider(Start));
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);
        return adapter;
    }

    // ---- Lifecycle ---------------------------------------------------------

    [Fact]
    public async Task Initialize_start_stop_happy_path()
    {
        var client = new FakeMelsecClient();
        var adapter = await StartAsync(Config(Tag("t", "D100")), client);

        adapter.State.Should().Be(AdapterState.Running);
        client.ConnectCalls.Should().Be(0); // TL-139: the connect belongs to the first poll

        await adapter.StopAsync(default);
        adapter.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task Start_does_not_dial_the_device()
    {
        // TL-139 regression: StartAsync is awaited by SourceSupervisor.AddAsync and
        // therefore by the config-apply reload, so dialling a PLC that is not wired
        // up yet stalled the operator's Save for the whole connect budget.
        var client = new FakeMelsecClient
        {
            ReadBehavior = (_, _, _) => MelsecClientResult.Ok(new byte[] { 0x01, 0x00 }),
        };
        var adapter = await StartAsync(Config(Tag("t", "D100", "int16")), client);

        client.ConnectCalls.Should().Be(0);
        adapter.State.Should().Be(AdapterState.Running);

        // ...and the deferral costs nothing: the first poll connects and reads.
        var points = await adapter.PollAsync(default);

        client.ConnectCalls.Should().Be(1);
        points.Should().ContainSingle().Which.Quality.Should().Be(DataQuality.Good);
    }

    [Fact]
    public async Task Start_before_initialize_is_still_a_genuine_start_failure()
    {
        // A silent device is not a start failure, but lifecycle misuse is: the
        // Failed transition and MELSEC.START_FAILED stay reachable through it.
        var adapter = new MelsecSourceAdapter("melsec-1", new FakeMelsecClient(), NullLogger.Instance);

        var act = async () => await adapter.StartAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        adapter.State.Should().Be(AdapterState.Failed);
        var health = await adapter.CheckHealthAsync(default);
        health.LastError!.Code.Should().Be("MELSEC.START_FAILED");
    }

    // ---- Validation --------------------------------------------------------

    private static async Task<ValidationResult> Validate(MelsecSourceConfiguration cfg)
    {
        var adapter = new MelsecSourceAdapter("melsec-1", new FakeMelsecClient(), NullLogger.Instance);
        return await adapter.ValidateConfigAsync(cfg, default);
    }

    [Fact]
    public async Task Validation_rejects_non_tcp_transport()
    {
        var result = await Validate(Config(Tag("t", "D100")) with { TransportProtocol = MelsecTransportProtocol.Udp });
        result.IsValid.Should().BeFalse();
        result.Errors[0].Code.Should().Be("MELSEC.CONFIG_MODE_NOT_IMPLEMENTED");
    }

    [Fact]
    public async Task Validation_rejects_non_3e_binary_frame()
    {
        var result = await Validate(Config(Tag("t", "D100")) with { FrameMode = MelsecFrameMode.Mc4EBinary });
        result.Errors[0].Code.Should().Be("MELSEC.CONFIG_MODE_NOT_IMPLEMENTED");
    }

    [Fact]
    public async Task Validation_rejects_unsupported_profile()
    {
        var result = await Validate(Config(Tag("t", "D100")) with { DeviceProfile = MelsecDeviceProfile.QnA });
        result.Errors[0].Code.Should().Be("MELSEC.CONFIG_PROFILE_NOT_IMPLEMENTED");
    }

    [Fact]
    public async Task Validation_rejects_missing_host_and_bad_port()
    {
        (await Validate(Config(Tag("t", "D100")) with { Host = "" })).Errors[0].Code.Should().Be("MELSEC.CONFIG_MISSING_HOST");
        (await Validate(Config(Tag("t", "D100")) with { Port = 0 })).Errors[0].Code.Should().Be("MELSEC.CONFIG_INVALID_PORT");
    }

    [Fact]
    public async Task Validation_rejects_points_cap_over_960()
    {
        var result = await Validate(Config(Tag("t", "D100")) with { MaxPointsPerRequest = 961 });
        result.Errors[0].Code.Should().Be("MELSEC.CONFIG_POINTS_CAP");
    }

    [Fact]
    public async Task Validation_rejects_incoherent_request_timeout()
    {
        // RequestTimeoutMs (1000) < monitoring timer (4000).
        var result = await Validate(Config(Tag("t", "D100")) with { RequestTimeoutMs = 1000, MonitoringTimerMs = 4000 });
        result.Errors[0].Code.Should().Be("MELSEC.CONFIG_TIMEOUT_INCOHERENT");
    }

    [Fact]
    public async Task Validation_rejects_unsupported_device_tag()
    {
        var result = await Validate(Config(Tag("t", "T5")));
        result.IsValid.Should().BeFalse();
        result.Errors[0].Code.Should().Be("MELSEC.DEVICE_NOT_IMPLEMENTED");
    }

    [Fact]
    public async Task Validation_rejects_malformed_tag()
    {
        var result = await Validate(Config(Tag("t", "Q1")));
        result.Errors[0].Code.Should().Be("MELSEC.CONFIG_INVALID_ADDRESS");
    }

    [Fact]
    public async Task Validation_rejects_zero_scan_rate()
    {
        var result = await Validate(Config(Tag("t", "D100", scanRateMs: 0)));
        result.Errors[0].Code.Should().Be("MELSEC.CONFIG_INVALID_SCANRATE");
    }

    // ---- Poll / decode -----------------------------------------------------

    [Fact]
    public async Task Poll_decodes_multiple_tags_from_one_block()
    {
        var client = new FakeMelsecClient
        {
            // D100/D101 coalesce into head 100, count 2: words 10 and 20 (LE).
            ReadBehavior = (_, _, _) => MelsecClientResult.Ok(new byte[] { 0x0A, 0x00, 0x14, 0x00 }),
        };
        var adapter = await StartAsync(Config(Tag("a", "D100", "int16"), Tag("b", "D101", "int16")), client);

        var points = await adapter.PollAsync(default);

        points.Should().HaveCount(2);
        points.Should().OnlyContain(p => p.Quality == DataQuality.Good);
        points.Should().ContainSingle(p => p.TagName == "a" && Equals(p.Value, (short)10));
        points.Should().ContainSingle(p => p.TagName == "b" && Equals(p.Value, (short)20));
        client.LastRead!.Value.Should().Be((MelsecDeviceCode.D, 100, 2));
    }

    [Fact]
    public async Task Poll_partial_block_failure_affects_only_mapped_tags()
    {
        var client = new FakeMelsecClient
        {
            ReadBehavior = (device, _, _) => device == MelsecDeviceCode.D
                ? MelsecClientResult.Ok(new byte[] { 0x05, 0x00 })
                : MelsecClientResult.Protocol(0xC059),
        };
        // D100 and W100 are different devices -> two independent blocks.
        var adapter = await StartAsync(Config(Tag("d", "D100", "int16"), Tag("w", "W100", "int16")), client);

        var points = await adapter.PollAsync(default);

        points.Should().ContainSingle(p => p.TagName == "d")
            .Which.Quality.Should().Be(DataQuality.Good);
        var w = points.Should().ContainSingle(p => p.TagName == "w").Subject;
        w.Quality.Should().Be(DataQuality.Bad);
        w.QualityReason.Should().Contain("protocol");
    }

    [Fact]
    public async Task Poll_transport_failure_marks_tags_bad_communication()
    {
        var client = new FakeMelsecClient { ReadBehavior = (_, _, _) => MelsecClientResult.Transport("reset") };
        var adapter = await StartAsync(Config(Tag("t", "D100", "int16")), client);

        var points = await adapter.PollAsync(default);

        points.Should().ContainSingle().Which.Quality.Should().Be(DataQuality.Bad);
        points[0].QualityReason.Should().Contain("communication");
    }

    // ---- Reconnect / backoff ----------------------------------------------

    [Fact]
    public async Task Reconnect_backoff_recovers_after_transient_connect_failures()
    {
        var client = new FakeMelsecClient
        {
            ConnectBehavior = call => call <= 5 ? MelsecClientResult.Transport("refused") : MelsecClientResult.Connected,
            ReadBehavior = (_, _, _) => MelsecClientResult.Ok(new byte[] { 0x01, 0x00 }),
        };
        var time = new TestTimeProvider(Start);
        var adapter = new MelsecSourceAdapter("melsec-1", client, NullLogger.Instance, null, time);
        await adapter.InitializeAsync(Config(Tag("t", "D100", "int16")), default);
        await adapter.StartAsync(default); // no connect here — the poll loop owns it

        IReadOnlyList<CanonicalDataPoint> points = Array.Empty<CanonicalDataPoint>();
        for (var i = 0; i < 8 && points.Count == 0; i++)
        {
            time.Advance(TimeSpan.FromMinutes(2)); // clear backoff + breaker reset window
            points = await adapter.PollAsync(default);
        }

        points.Should().ContainSingle();
        client.ConnectCalls.Should().BeGreaterThan(5);
    }

    // ---- Retirement --------------------------------------------------------

    [Fact]
    public async Task Retirement_is_idempotent_and_proves_wire_idle()
    {
        var client = new FakeMelsecClient();
        var adapter = await StartAsync(Config(Tag("t", "D100")), client);

        var context = new AdapterRetirementContext { ObservationToken = default };
        var op1 = adapter.BeginRetirement(context);
        var op2 = adapter.BeginRetirement(context);

        op2.Should().BeSameAs(op1); // idempotent — no second cleanup
        client.DisconnectCalls.Should().BeGreaterThan(0); // revoke/detach (close) before cancel

        var attestation = await op1.Completion; // wire idle (no in-flight read)
        attestation.Worker.Should().Be(AdapterSurfaceState.Proven);
    }

    [Fact]
    public async Task Cancel_during_poll_does_not_deadlock()
    {
        var gate = new TaskCompletionSource<MelsecClientResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeMelsecClient { ReadGate = gate };
        var adapter = await StartAsync(Config(Tag("t", "D100", "int16")), client);

        using var cts = new CancellationTokenSource();
        var pollTask = adapter.PollAsync(cts.Token); // blocks on the gate (in-flight read)
        cts.Cancel();

        var act = async () => await pollTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Retirement_interrupts_in_flight_read_and_reaches_wire_idle()
    {
        var gate = new TaskCompletionSource<MelsecClientResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeMelsecClient { ReadGate = gate };
        var adapter = await StartAsync(Config(Tag("t", "D100", "int16")), client);

        var pollTask = adapter.PollAsync(default); // begins an in-flight read on the gate

        // Retirement closes the transport, which unblocks the wedged read (no late
        // wire activity survives) and lets the durable attestation resolve.
        var op = adapter.BeginRetirement(new AdapterRetirementContext { ObservationToken = default });

        await pollTask; // completes (transport error -> bad points), no deadlock
        var attestation = await op.Completion;

        attestation.Worker.Should().Be(AdapterSurfaceState.Proven);
        client.IsConnected.Should().BeFalse();
    }

    // ─── A-2O profile acceptance rule ───

    [Fact]
    public async Task Validation_accepts_explicit_Modern_profile()
    {
        var result = await Validate(Config(Tag("t", "D100")) with { DeviceProfile = MelsecDeviceProfile.Modern });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task IqF_config_initializes_with_octal_XY_and_no_ZR()
    {
        // A-2O acceptance: an iQ-F config with an octal X label initializes and
        // plans head 8; a ZR tag fails validation with a typed error.
        var cfg = Config(Tag("input_a", "X10", "Bool")) with { DeviceProfile = MelsecDeviceProfile.IqF };
        var client = new FakeMelsecClient();
        var adapter = new MelsecSourceAdapter(cfg.InstanceId, client, NullLogger.Instance);

        await adapter.InitializeAsync(cfg, CancellationToken.None);

        adapter.State.Should().Be(AdapterState.Initialized);

        var zrResult = await Validate(Config(Tag("recipe", "ZR100")) with { DeviceProfile = MelsecDeviceProfile.IqF });
        zrResult.IsValid.Should().BeFalse();
        zrResult.Errors[0].Code.Should().Be(MelsecAddressParser.DeviceNotImplemented);
    }

    [Fact]
    public async Task Validation_of_IqF_profile_follows_the_operator_selectable_gate()
    {
        // Self-adjusting: while Gate A-2O's flip has not landed, IqF is rejected;
        // once IqF.IsOperatorSelectable flips true, it must validate cleanly with
        // an iQ-F-legal tag set. Either way the rule is registry-driven.
        var result = await Validate(Config(Tag("t", "D100")) with { DeviceProfile = MelsecDeviceProfile.IqF });

        if (ElpisEdgeConnect.Sources.Melsec.Profiles.MelsecProfiles.IqF.IsOperatorSelectable)
        {
            result.IsValid.Should().BeTrue();
        }
        else
        {
            result.Errors[0].Code.Should().Be("MELSEC.CONFIG_PROFILE_NOT_IMPLEMENTED");
        }
    }
}
