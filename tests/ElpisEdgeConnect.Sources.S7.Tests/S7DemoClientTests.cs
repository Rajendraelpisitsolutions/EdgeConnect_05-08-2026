// ============================================================================
// Tests: S7DemoClient — the synthetic IS7Client used in demo mode. Pins:
//        connect always succeeds, reads fill the buffer deterministically for
//        a fixed clock, values vary over time and decode in-range, cancellation
//        is honored, and there is no network/native dependency.
// ============================================================================

using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.S7;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests;

public class S7DemoClientTests
{
    private static S7DemoClient At(DateTime fixedNow) => new(() => fixedNow);

    [Fact]
    public async Task ConnectAsync_AlwaysSucceeds_AndSetsConnected()
    {
        var client = At(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var result = await client.ConnectAsync("anything", 102, 0, 1, S7ConnectionType.Basic, TimeSpan.FromSeconds(1), CancellationToken.None);

        result.Success.Should().BeTrue();
        client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAreaAsync_IsDeterministic_ForAFixedClock()
    {
        var client = At(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var a = new byte[8];
        var b = new byte[8];

        await client.ReadAreaAsync(S7MemoryArea.DataBlock, 1, 0, 8, a, CancellationToken.None);
        await client.ReadAreaAsync(S7MemoryArea.DataBlock, 1, 0, 8, b, CancellationToken.None);

        a.Should().Equal(b);
    }

    [Fact]
    public async Task ReadAreaAsync_DecodesToInRangeWord()
    {
        var client = At(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var buf = new byte[2];

        var r = await client.ReadAreaAsync(S7MemoryArea.DataBlock, 1, 0, 2, buf, CancellationToken.None);

        r.Success.Should().BeTrue();
        var word = BinaryPrimitives.ReadUInt16BigEndian(buf);
        word.Should().BeInRange((ushort)100, (ushort)900);
    }

    [Fact]
    public async Task ReadAreaAsync_VariesOverTime()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = t0;
        var client = new S7DemoClient(() => now);

        var early = new byte[2];
        await client.ReadAreaAsync(S7MemoryArea.DataBlock, 1, 0, 2, early, CancellationToken.None);

        now = t0.AddSeconds(15); // half the 30s sine period — guaranteed to differ
        var later = new byte[2];
        await client.ReadAreaAsync(S7MemoryArea.DataBlock, 1, 0, 2, later, CancellationToken.None);

        early.Should().NotEqual(later);
    }

    [Fact]
    public async Task ReadAreaAsync_HonorsCancellation()
    {
        var client = At(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.ReadAreaAsync(S7MemoryArea.Marker, 0, 0, 4, new byte[4], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Dispose_MarksDisconnected()
    {
        var client = At(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        client.Dispose();
        client.IsConnected.Should().BeFalse();
    }
}
