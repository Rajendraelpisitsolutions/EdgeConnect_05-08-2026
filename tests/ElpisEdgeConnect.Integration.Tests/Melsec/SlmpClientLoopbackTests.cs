// ============================================================================
// Tests: SlmpClient against the in-proc loopback SLMP server (real TCP on
// 127.0.0.1, no PLC). Covers success, protocol end code, malformed/truncated,
// partial chunks, timeout+drop, late-reply-after-reconnect, server disconnect,
// and cancellation. Frames are spec-derived (SH-080008), field-unverified.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Integration.Tests.Melsec;

[Trait("Category", "Integration")]
public class SlmpClientLoopbackTests
{
    private static MelsecSourceConfiguration Config(int port, int requestTimeoutMs = 500) => new()
    {
        InstanceId = "melsec-1",
        ProtocolName = "melsec",
        DeviceId = "dev1",
        Host = "127.0.0.1",
        Port = port,
        RequestTimeoutMs = requestTimeoutMs,
        MonitoringTimerMs = 250,
    };

    private static async Task<SlmpClient> ConnectAsync(int port, int requestTimeoutMs = 500)
    {
        var client = new SlmpClient(Config(port, requestTimeoutMs));
        var result = await client.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(2), default);
        result.IsSuccess.Should().BeTrue(result.Message);
        return client;
    }

    [Fact]
    public async Task Successful_batch_read_returns_expected_words()
    {
        await using var server = new LoopbackSlmpServer(_ => SlmpResponses.Send(SlmpResponses.SuccessFrame(0x1234, 0x5678)));
        using var client = await ConnectAsync(server.Port);

        var result = await client.ReadWordsAsync(MelsecDeviceCode.D, 100, 2, default);

        result.Status.Should().Be(MelsecClientStatus.Success);
        result.WordData.ToArray().Should().Equal(0x34, 0x12, 0x78, 0x56);
    }

    [Fact]
    public async Task Nonzero_end_code_becomes_protocol_failure_and_keeps_socket()
    {
        await using var server = new LoopbackSlmpServer(_ => SlmpResponses.Send(SlmpResponses.EndCodeFrame(0xC059)));
        using var client = await ConnectAsync(server.Port);

        var result = await client.ReadWordsAsync(MelsecDeviceCode.D, 100, 1, default);

        result.Status.Should().Be(MelsecClientStatus.ProtocolError);
        result.EndCode.Should().Be(0xC059);
        client.IsConnected.Should().BeTrue("a valid protocol response does not drop the connection");
    }

    [Fact]
    public async Task Malformed_response_becomes_malformed_and_drops_socket()
    {
        // Valid lengths but a wrong subheader (0x50 instead of 0xD0).
        var bad = SlmpResponses.SuccessFrame(0x1111);
        bad[0] = 0x50;
        await using var server = new LoopbackSlmpServer(_ => SlmpResponses.Send(bad));
        using var client = await ConnectAsync(server.Port);

        var result = await client.ReadWordsAsync(MelsecDeviceCode.D, 100, 1, default);

        result.Status.Should().Be(MelsecClientStatus.MalformedResponse);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Truncated_response_becomes_transport_failure_and_drops_socket()
    {
        // Declares a full 2-word frame but sends only part of it, then closes.
        var full = SlmpResponses.SuccessFrame(0x1111, 0x2222);
        await using var server = new LoopbackSlmpServer(_ => SlmpResponses.SendThenClose(full, full.Length - 2));
        using var client = await ConnectAsync(server.Port);

        var result = await client.ReadWordsAsync(MelsecDeviceCode.D, 100, 2, default);

        result.Status.Should().Be(MelsecClientStatus.TransportError);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Partial_tcp_chunks_are_reassembled()
    {
        var frame = SlmpResponses.SuccessFrame(0x1111, 0x2222);
        await using var server = new LoopbackSlmpServer(_ => SlmpResponses.SendChunked(frame, chunkSize: 3, delayMs: 10));
        using var client = await ConnectAsync(server.Port);

        var result = await client.ReadWordsAsync(MelsecDeviceCode.D, 100, 2, default);

        result.Status.Should().Be(MelsecClientStatus.Success);
        result.WordData.ToArray().Should().Equal(0x11, 0x11, 0x22, 0x22);
    }

    [Fact]
    public async Task No_response_times_out_and_drops_socket()
    {
        await using var server = new LoopbackSlmpServer(_ => SlmpResponses.NoReply());
        using var client = await ConnectAsync(server.Port, requestTimeoutMs: 300);

        var result = await client.ReadWordsAsync(MelsecDeviceCode.D, 100, 1, default);

        result.Status.Should().Be(MelsecClientStatus.TransportError);
        result.Message.Should().Contain("timed out");
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Late_reply_after_timeout_does_not_corrupt_next_read_after_reconnect()
    {
        // Connection 0 replies AFTER the client's 200 ms timeout (with a wrong
        // value); connection 1 replies correctly. The late reply lands on the
        // dropped socket 0 and must not corrupt read 2.
        await using var server = new LoopbackSlmpServer(index => index == 0
            ? SlmpResponses.DelayThenSend(SlmpResponses.SuccessFrame(0x9999), delayMs: 500)
            : SlmpResponses.Send(SlmpResponses.SuccessFrame(0x1234)));

        using var client = await ConnectAsync(server.Port, requestTimeoutMs: 200);

        var read1 = await client.ReadWordsAsync(MelsecDeviceCode.D, 100, 1, default);
        read1.Status.Should().Be(MelsecClientStatus.TransportError);
        client.IsConnected.Should().BeFalse();

        (await client.ConnectAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(2), default)).IsSuccess.Should().BeTrue();
        var read2 = await client.ReadWordsAsync(MelsecDeviceCode.D, 100, 1, default);

        read2.Status.Should().Be(MelsecClientStatus.Success);
        read2.WordData.ToArray().Should().Equal(0x34, 0x12);
    }

    [Fact]
    public async Task Server_disconnect_is_handled_deterministically()
    {
        await using var server = new LoopbackSlmpServer(_ => SlmpResponses.CloseImmediately());
        using var client = await ConnectAsync(server.Port);

        var result = await client.ReadWordsAsync(MelsecDeviceCode.D, 100, 1, default);

        result.Status.Should().Be(MelsecClientStatus.TransportError);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_does_not_deadlock_and_drops_socket()
    {
        await using var server = new LoopbackSlmpServer(_ => SlmpResponses.NoReply());
        using var client = await ConnectAsync(server.Port, requestTimeoutMs: 10_000);

        using var cts = new CancellationTokenSource();
        var readTask = client.ReadWordsAsync(MelsecDeviceCode.D, 100, 1, cts.Token);
        cts.Cancel();

        var act = async () => await readTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
        client.IsConnected.Should().BeFalse();
    }
}
