// ============================================================================
// File: Generation/GenerationScopedIntakeWriterTests.cs
// Purpose: Pin the §2 generation-fenced write algorithm: commit under the lease
//          fence, reject after retirement/detach, capacity wait then commit,
//          committed-before-retire drains, channel-closed terminal, idempotent
//          detach that does not complete the channel.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §2, §10.
// ============================================================================

using System.Threading.Channels;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Generation;
using ElpisEdgeConnect.Host.Generation;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Generation;

public sealed class GenerationScopedIntakeWriterTests
{
    private static (SourceSlotGate Gate, GenerationLease Lease) AuthorizedGate()
    {
        var gate = new SourceSlotGate(RuntimeInstanceId.New(), "src-1", new SourceGenerationAllocator());
        var lease = gate.IssueLease().Lease!;
        gate.TryAuthorize(lease).Should().Be(GenerationAuthorizationOutcome.Ok);
        return (gate, lease);
    }

    [Fact]
    public async Task WriteAsync_Authorized_CommitsToChannel()
    {
        var (gate, lease) = AuthorizedGate();
        var channel = Channel.CreateBounded<int>(4);
        var writer = new GenerationScopedIntakeWriter<int>(gate, lease, channel.Writer);

        (await writer.WriteAsync(7, default)).Should().Be(IntakeWriteOutcome.Committed);
        channel.Reader.TryRead(out var value).Should().BeTrue();
        value.Should().Be(7);
    }

    [Fact]
    public async Task WriteAsync_AfterRetirement_IsRejected_AndNotEnqueued()
    {
        var (gate, lease) = AuthorizedGate();
        var channel = Channel.CreateBounded<int>(4);
        var writer = new GenerationScopedIntakeWriter<int>(gate, lease, channel.Writer);
        gate.TryRetire(lease, RetirementReason.Stop);

        (await writer.WriteAsync(7, default)).Should().Be(IntakeWriteOutcome.RejectedRetired);
        channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_AfterDetach_IsRejected()
    {
        var (gate, lease) = AuthorizedGate();
        var channel = Channel.CreateBounded<int>(4);
        var writer = new GenerationScopedIntakeWriter<int>(gate, lease, channel.Writer);
        writer.Detach();

        (await writer.WriteAsync(7, default)).Should().Be(IntakeWriteOutcome.RejectedRetired);
    }

    [Fact]
    public void Detach_IsIdempotent_AndDoesNotCompleteChannel()
    {
        var (gate, lease) = AuthorizedGate();
        var channel = Channel.CreateBounded<int>(4);
        var writer = new GenerationScopedIntakeWriter<int>(gate, lease, channel.Writer);

        writer.Detach();
        writer.Detach(); // no throw

        // Detach must NOT complete the channel — we can still complete it ourselves.
        channel.Writer.TryComplete().Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_CommittedBeforeRetire_DrainsAfterRetirement()
    {
        var (gate, lease) = AuthorizedGate();
        var channel = Channel.CreateBounded<int>(4);
        var writer = new GenerationScopedIntakeWriter<int>(gate, lease, channel.Writer);

        (await writer.WriteAsync(7, default)).Should().Be(IntakeWriteOutcome.Committed);
        gate.TryRetire(lease, RetirementReason.Reconfigure);
        writer.Detach();

        channel.Reader.TryRead(out var value).Should().BeTrue();
        value.Should().Be(7);
    }

    [Fact]
    public async Task WriteAsync_ChannelClosed_ReturnsChannelClosed()
    {
        var (gate, lease) = AuthorizedGate();
        var channel = Channel.CreateBounded<int>(4);
        var writer = new GenerationScopedIntakeWriter<int>(gate, lease, channel.Writer);
        channel.Writer.Complete();

        (await writer.WriteAsync(7, default)).Should().Be(IntakeWriteOutcome.ChannelClosed);
    }

    [Fact]
    public async Task WriteAsync_WaitsForCapacity_ThenCommits()
    {
        var (gate, lease) = AuthorizedGate();
        var channel = Channel.CreateBounded<int>(1);
        channel.Writer.TryWrite(99).Should().BeTrue(); // fill to capacity
        var writer = new GenerationScopedIntakeWriter<int>(gate, lease, channel.Writer);

        var pending = writer.WriteAsync(7, default).AsTask();
        pending.IsCompleted.Should().BeFalse(); // blocked on capacity

        channel.Reader.TryRead(out var first).Should().BeTrue();
        first.Should().Be(99);

        (await pending).Should().Be(IntakeWriteOutcome.Committed);
        channel.Reader.TryRead(out var second).Should().BeTrue();
        second.Should().Be(7);
    }
}
