// ============================================================================
// File: MockSourceAdapterTests.cs
// Purpose: Phase 3 unit tests for MockSourceAdapter. Pin deterministic
//          sequence generation, pacing/burst/fail-after-N hooks, contract
//          adherence, and clean cancellation behavior.
// Milestone: D — phase 3.
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.MockAdapters;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.MockAdapters.Tests;

public sealed class MockSourceAdapterTests
{
    private static MockSourceConfiguration Config(string instanceId = "src-1") => new()
    {
        InstanceId = instanceId,
        ProtocolName = "mock",
        DeviceId = "dev-1",
    };

    private static async Task<MockSourceAdapter> StartedAsync(
        Action<MockSourceAdapter>? configure = null)
    {
        var src = new MockSourceAdapter("src-1");
        configure?.Invoke(src);
        await src.InitializeAsync(Config(), CancellationToken.None);
        await src.StartAsync(CancellationToken.None);
        return src;
    }

    [Fact]
    public async Task LifecycleTransitions_FollowAdapterStateContract()
    {
        var src = new MockSourceAdapter("src-1");
        src.State.Should().Be(AdapterState.Created);

        await src.InitializeAsync(Config(), CancellationToken.None);
        src.State.Should().Be(AdapterState.Initialized);

        await src.StartAsync(CancellationToken.None);
        src.State.Should().Be(AdapterState.Running);

        await src.StopAsync(CancellationToken.None);
        src.State.Should().Be(AdapterState.Stopped);

        await src.DisposeAsync();
        src.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task PollAsync_EmitsDeterministicMonotonicSequence()
    {
        var src = await StartedAsync(s => s.PointsPerPoll = 1);

        var batch1 = await src.PollAsync(CancellationToken.None);
        var batch2 = await src.PollAsync(CancellationToken.None);
        var batch3 = await src.PollAsync(CancellationToken.None);

        batch1.Should().HaveCount(1);
        batch2.Should().HaveCount(1);
        batch3.Should().HaveCount(1);
        batch1[0].SequenceNumber.Should().Be(1);
        batch2[0].SequenceNumber.Should().Be(2);
        batch3[0].SequenceNumber.Should().Be(3);
        src.EmittedCount.Should().Be(3);
        src.PollCount.Should().Be(3);
    }

    [Fact]
    public async Task PollAsync_BurstMode_ReturnsPointsPerPollPerCall()
    {
        var src = await StartedAsync(s => s.PointsPerPoll = 7);

        var batch = await src.PollAsync(CancellationToken.None);

        batch.Should().HaveCount(7);
        batch.Select(p => p.SequenceNumber).Should().Equal(1L, 2L, 3L, 4L, 5L, 6L, 7L);
        src.EmittedCount.Should().Be(7);
    }

    [Fact]
    public async Task PollAsync_StopAfterPoints_ReturnsEmptyBatchPastCap()
    {
        var src = await StartedAsync(s =>
        {
            s.PointsPerPoll = 3;
            s.StopAfterPoints = 5;
        });

        var first = await src.PollAsync(CancellationToken.None);
        var second = await src.PollAsync(CancellationToken.None); // would emit 3 more, capped to 2
        var third = await src.PollAsync(CancellationToken.None);  // empty

        first.Should().HaveCount(3);
        second.Should().HaveCount(2);
        third.Should().BeEmpty();
        src.EmittedCount.Should().Be(5);
    }

    [Fact]
    public async Task PollAsync_FailNextPolls_ThrowsAdapterExceptionAndDecrements()
    {
        var src = await StartedAsync(s => s.FailNextPolls = 2);

        var act1 = async () => await src.PollAsync(CancellationToken.None);
        var act2 = async () => await src.PollAsync(CancellationToken.None);
        await act1.Should().ThrowAsync<AdapterException>();
        await act2.Should().ThrowAsync<AdapterException>();

        // Third call succeeds.
        var batch = await src.PollAsync(CancellationToken.None);
        batch.Should().HaveCount(1);
        src.EmittedCount.Should().Be(1);
        src.PollCount.Should().Be(3);
    }

    [Fact]
    public async Task PollAsync_FailUntilSignaled_FailsThenHealsDeterministically()
    {
        var window = new TaskCompletionSource();
        var src = await StartedAsync(s => s.FailUntilSignaled = window);

        var act = async () => await src.PollAsync(CancellationToken.None);
        await act.Should().ThrowAsync<AdapterException>();
        await act.Should().ThrowAsync<AdapterException>();

        // Heal — every subsequent poll succeeds.
        window.SetResult();
        var batch = await src.PollAsync(CancellationToken.None);
        batch.Should().HaveCount(1);
    }

    [Fact]
    public async Task PollGate_BlocksPollUntilReleased()
    {
        using var gate = new SemaphoreSlim(initialCount: 0, maxCount: int.MaxValue);
        var src = await StartedAsync(s => s.PollGate = gate);

        // Kick off a poll on a background task; it must remain pending.
        var pollTask = Task.Run(() => src.PollAsync(CancellationToken.None));
        await Task.Delay(20); // tiny window for the poll to begin
        pollTask.IsCompleted.Should().BeFalse("the poll should be parked on the gate");

        // Release one permit; the poll completes deterministically.
        gate.Release();
        var batch = await pollTask;
        batch.Should().HaveCount(1);
    }

    [Fact]
    public async Task PollAsync_RespectsCancellation_OnGateWait()
    {
        using var gate = new SemaphoreSlim(initialCount: 0, maxCount: int.MaxValue);
        var src = await StartedAsync(s => s.PollGate = gate);

        using var cts = new CancellationTokenSource();
        var pollTask = Task.Run(() => src.PollAsync(cts.Token));
        cts.Cancel();

        var act = async () => await pollTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PollAsync_BeforeStart_Throws()
    {
        var src = new MockSourceAdapter("src-1");
        await src.InitializeAsync(Config(), CancellationToken.None);

        var act = async () => await src.PollAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CheckHealthAsync_ReportsHealthyAfterSuccess_DegradedAfterFailure()
    {
        var src = await StartedAsync(s => s.FailNextPolls = 1);

        var actFail = async () => await src.PollAsync(CancellationToken.None);
        await actFail.Should().ThrowAsync<AdapterException>();

        var degraded = await src.CheckHealthAsync(CancellationToken.None);
        degraded.Level.Should().Be(HealthLevel.Degraded);
        degraded.LastError.Should().NotBeNull();

        await src.PollAsync(CancellationToken.None);
        var healthy = await src.CheckHealthAsync(CancellationToken.None);
        healthy.Level.Should().Be(HealthLevel.Healthy);
        healthy.LastSuccessAt.Should().NotBeNull();
    }

    [Fact]
    public void Capabilities_DeclaresPollingAndTestConnect()
    {
        var src = new MockSourceAdapter("src-1");
        src.Capabilities.Should().HaveFlag(SourceCapabilities.Polling);
        src.Capabilities.Should().HaveFlag(SourceCapabilities.TestConnect);
        src.Capabilities.Should().NotHaveFlag(SourceCapabilities.Subscription);
        src.Capabilities.Should().NotHaveFlag(SourceCapabilities.Browse);
    }
}
