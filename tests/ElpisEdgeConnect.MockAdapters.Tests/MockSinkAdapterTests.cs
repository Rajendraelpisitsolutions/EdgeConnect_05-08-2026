// ============================================================================
// File: MockSinkAdapterTests.cs
// Purpose: Phase 3 unit tests for MockSinkAdapter. Pin deterministic
//          success / fail / delay behavior, publish history capture,
//          contract adherence, and clean cancellation behavior.
// Milestone: D — phase 3.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.MockAdapters;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.MockAdapters.Tests;

public sealed class MockSinkAdapterTests
{
    private static MockSinkConfiguration Config(string instanceId = "sink-1") => new()
    {
        InstanceId = instanceId,
        ProtocolName = "mock",
    };

    private static async Task<MockSinkAdapter> StartedAsync(Action<MockSinkAdapter>? configure = null)
    {
        var sink = new MockSinkAdapter("sink-1");
        configure?.Invoke(sink);
        await sink.InitializeAsync(Config(), CancellationToken.None);
        await sink.StartAsync(CancellationToken.None);
        return sink;
    }

    private static IReadOnlyList<CanonicalDataPoint> MakeBatch(int count, int firstSeq = 1)
    {
        var factory = new CanonicalDataPointFactory(
            gatewayId: "test-gw",
            sourceInstanceId: "src-1",
            protocolName: "mock",
            deviceId: "dev-1");
        // Burn sequences up to firstSeq - 1 so the batch starts at firstSeq.
        for (var i = 1; i < firstSeq; i++)
        {
            factory.NextSequence();
        }
        var now = DateTime.UtcNow;
        var batch = new CanonicalDataPoint[count];
        for (var i = 0; i < count; i++)
        {
            batch[i] = factory.CreatePoint(
                tagName: "tag",
                tagPath: "dev-1/tag",
                value: 0L,
                valueType: CanonicalValueType.Long,
                quality: DataQuality.Good,
                deviceTimestamp: now,
                gatewayTimestamp: now);
        }
        return batch;
    }

    [Fact]
    public async Task LifecycleTransitions_FollowAdapterStateContract()
    {
        var sink = new MockSinkAdapter("sink-1");
        sink.State.Should().Be(AdapterState.Created);

        await sink.InitializeAsync(Config(), CancellationToken.None);
        sink.State.Should().Be(AdapterState.Initialized);

        await sink.StartAsync(CancellationToken.None);
        sink.State.Should().Be(AdapterState.Running);

        await sink.StopAsync(CancellationToken.None);
        sink.State.Should().Be(AdapterState.Stopped);

        await sink.DisposeAsync();
        sink.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task PublishAsync_HappyPath_CapturesPointsAndIncrementsCounters()
    {
        var sink = await StartedAsync();
        var batch = MakeBatch(5);

        var result = await sink.PublishAsync(batch, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AcceptedCount.Should().Be(5);
        sink.PublishedCount.Should().Be(5);
        sink.AttemptCount.Should().Be(1);
        sink.PublishedPoints.Should().HaveCount(5);
        sink.PublishedPoints.Select(p => p.SequenceNumber).Should().Equal(1L, 2L, 3L, 4L, 5L);
    }

    [Fact]
    public async Task PublishAsync_FailNextPublishes_FailsThenRecovers()
    {
        var sink = await StartedAsync(s => s.FailNextPublishes = 2);
        var batch = MakeBatch(2);

        var fail1 = await sink.PublishAsync(batch, CancellationToken.None);
        var fail2 = await sink.PublishAsync(batch, CancellationToken.None);
        var ok = await sink.PublishAsync(batch, CancellationToken.None);

        fail1.Success.Should().BeFalse();
        fail2.Success.Should().BeFalse();
        ok.Success.Should().BeTrue();
        sink.AttemptCount.Should().Be(3);
        sink.PublishedCount.Should().Be(2, "only the recovered publish stored points");
    }

    [Fact]
    public async Task PublishAsync_FailUntilSignaled_FailsDeterministicallyThenHeals()
    {
        var window = new TaskCompletionSource();
        var sink = await StartedAsync(s => s.FailUntilSignaled = window);
        var batch = MakeBatch(3);

        var fail1 = await sink.PublishAsync(batch, CancellationToken.None);
        var fail2 = await sink.PublishAsync(batch, CancellationToken.None);
        fail1.Success.Should().BeFalse();
        fail2.Success.Should().BeFalse();

        // Heal — every subsequent publish succeeds.
        window.SetResult();
        var ok = await sink.PublishAsync(batch, CancellationToken.None);
        ok.Success.Should().BeTrue();
        sink.PublishedCount.Should().Be(3);
    }

    [Fact]
    public async Task PublishGate_BlocksPublishUntilReleased()
    {
        using var gate = new SemaphoreSlim(initialCount: 0, maxCount: int.MaxValue);
        var sink = await StartedAsync(s => s.PublishGate = gate);
        var batch = MakeBatch(1);

        var pubTask = Task.Run(() => sink.PublishAsync(batch, CancellationToken.None));
        await Task.Delay(20);
        pubTask.IsCompleted.Should().BeFalse("publish should be parked on the gate");

        gate.Release();
        var result = await pubTask;
        result.Success.Should().BeTrue();
        sink.PublishedCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_RespectsCancellation_OnGateWait()
    {
        using var gate = new SemaphoreSlim(initialCount: 0, maxCount: int.MaxValue);
        var sink = await StartedAsync(s => s.PublishGate = gate);
        var batch = MakeBatch(1);

        using var cts = new CancellationTokenSource();
        var pubTask = Task.Run(() => sink.PublishAsync(batch, cts.Token));
        cts.Cancel();

        var act = async () => await pubTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CheckHealthAsync_ReportsLastErrorAfterFailure()
    {
        var sink = await StartedAsync(s => s.FailNextPublishes = 1);
        var batch = MakeBatch(1);

        await sink.PublishAsync(batch, CancellationToken.None);

        var health = await sink.CheckHealthAsync(CancellationToken.None);
        health.Level.Should().Be(HealthLevel.Degraded);
        health.LastError.Should().NotBeNull();
        health.LastError!.Code.Should().Be("MOCK.SINK_FAILED");
    }

    [Fact]
    public async Task CheckHealthAsync_ReportsHealthyAfterRecovery()
    {
        var sink = await StartedAsync(s => s.FailNextPublishes = 1);
        var batch = MakeBatch(1);
        await sink.PublishAsync(batch, CancellationToken.None); // fail
        await sink.PublishAsync(batch, CancellationToken.None); // ok

        var health = await sink.CheckHealthAsync(CancellationToken.None);
        health.Level.Should().Be(HealthLevel.Healthy);
        health.LastError.Should().BeNull();
        health.LastSuccessAt.Should().NotBeNull();
    }

    [Fact]
    public void Capabilities_DeclaresPushAndTestConnect()
    {
        var sink = new MockSinkAdapter("sink-1");
        sink.Capabilities.Should().HaveFlag(SinkCapabilities.Push);
        sink.Capabilities.Should().HaveFlag(SinkCapabilities.TestConnect);
        sink.Capabilities.Should().NotHaveFlag(SinkCapabilities.Pull);
    }
}
