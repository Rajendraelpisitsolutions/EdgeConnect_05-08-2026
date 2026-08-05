// ============================================================================
// Tests: ReloadOutcomeRegistry — in-memory correlation channel between the
//        runtime-reload coordinator (producer) and the Management apply
//        endpoint (consumer). These tests pin the load-bearing
//        invariants of the M.P2.2 phase 3 observation surface:
//
//          * Wait-then-enqueue and enqueue-then-wait both deliver.
//          * Multiple concurrent waiters on the same version all
//            receive the outcome.
//          * Idempotent re-enqueue (first wins).
//          * Bounded cache with FIFO eviction (guardrail K, capacity 64).
//          * Cancellation / timeout returns null, not throws.
//          * EnqueueSkipped builds the right shape for the stale-skip
//            branch.
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class ReloadOutcomeRegistryTests
{
    [Fact]
    public async Task EnqueueCompleted_ThenWaitFor_ReturnsImmediately()
    {
        var queue = new ReloadOutcomeRegistry();
        var versionId = ConfigurationVersionId.NewId();
        var outcome = MakeCompletedOutcome(versionId);

        queue.EnqueueCompleted(outcome);
        var result = await queue.WaitForAsync(versionId, TimeSpan.FromSeconds(5), CancellationToken.None);

        result.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task WaitFor_BeforeEnqueue_ReturnsWhenEnqueued()
    {
        var queue = new ReloadOutcomeRegistry();
        var versionId = ConfigurationVersionId.NewId();
        var outcome = MakeCompletedOutcome(versionId);

        var waiter = queue.WaitForAsync(versionId, TimeSpan.FromSeconds(5), CancellationToken.None);
        queue.EnqueueCompleted(outcome);
        var result = await waiter;

        result.Should().BeSameAs(outcome);
    }

    [Fact]
    public async Task WaitFor_TimesOut_ReturnsNull()
    {
        var queue = new ReloadOutcomeRegistry();
        var versionId = ConfigurationVersionId.NewId();

        var result = await queue.WaitForAsync(versionId, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task MultipleWaiters_SameVersion_AllReceiveOutcome()
    {
        var queue = new ReloadOutcomeRegistry();
        var versionId = ConfigurationVersionId.NewId();
        var outcome = MakeCompletedOutcome(versionId);

        var waiterA = queue.WaitForAsync(versionId, TimeSpan.FromSeconds(5), CancellationToken.None);
        var waiterB = queue.WaitForAsync(versionId, TimeSpan.FromSeconds(5), CancellationToken.None);
        var waiterC = queue.WaitForAsync(versionId, TimeSpan.FromSeconds(5), CancellationToken.None);

        queue.EnqueueCompleted(outcome);
        var results = await Task.WhenAll(waiterA, waiterB, waiterC);

        results.Should().AllSatisfy(r => r.Should().BeSameAs(outcome));
    }

    [Fact]
    public async Task EnqueueSkipped_PopulatesSupersededBy()
    {
        var queue = new ReloadOutcomeRegistry();
        var staleVersion = ConfigurationVersionId.NewId();
        var winnerVersion = ConfigurationVersionId.NewId();

        queue.EnqueueSkipped(staleVersion, winnerVersion);
        var result = await queue.WaitForAsync(staleVersion, TimeSpan.FromSeconds(5), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(ReloadStatus.Skipped);
        result.NewVersionId.Should().Be(staleVersion);
        result.SupersededBy.Should().Be(winnerVersion);
        result.AppliedInstances.Should().BeEmpty();
        result.RestartedInstances.Should().BeEmpty();
        result.FaultedInstances.Should().BeEmpty();
        result.ElapsedMs.Should().Be(0);
    }

    [Fact]
    public async Task LruEviction_OldEntriesDroppedAfterCapacity()
    {
        // Guardrail K — the queue is bounded at Capacity. Insertion order
        // is FIFO: once the cache exceeds Capacity, the oldest entry is
        // evicted. A WaitFor on the evicted version no longer hits the
        // cache and returns null after the timeout.
        var queue = new ReloadOutcomeRegistry();
        var versions = Enumerable
            .Range(0, ReloadOutcomeRegistry.Capacity + 1)
            .Select(_ => ConfigurationVersionId.NewId())
            .ToList();

        foreach (var v in versions)
        {
            queue.EnqueueCompleted(MakeCompletedOutcome(v));
        }

        // The oldest entry (versions[0]) should have been evicted by the
        // (Capacity + 1)-th enqueue. WaitFor on it returns null.
        var evicted = await queue.WaitForAsync(versions[0], TimeSpan.FromMilliseconds(50), CancellationToken.None);
        evicted.Should().BeNull();

        // The newest entry is still cached and resolves immediately.
        var newest = await queue.WaitForAsync(versions[^1], TimeSpan.FromSeconds(5), CancellationToken.None);
        newest.Should().NotBeNull();
        newest!.NewVersionId.Should().Be(versions[^1]);
    }

    [Fact]
    public async Task DoubleEnqueue_SameVersion_IsIdempotent()
    {
        // Idempotency: the first outcome wins; a second enqueue for the
        // same version is a no-op. This protects against accidental
        // double-publication (e.g. a future coordinator change that
        // sends both a Completed and a Skipped path for the same
        // version).
        var queue = new ReloadOutcomeRegistry();
        var versionId = ConfigurationVersionId.NewId();
        var first = MakeCompletedOutcome(versionId, applied: new[] { "first-source" });
        var second = MakeCompletedOutcome(versionId, applied: new[] { "second-source" });

        queue.EnqueueCompleted(first);
        queue.EnqueueCompleted(second);

        var result = await queue.WaitForAsync(versionId, TimeSpan.FromSeconds(5), CancellationToken.None);
        result.Should().BeSameAs(first);
    }

    [Fact]
    public async Task WaitFor_CanceledViaCt_ReturnsNull()
    {
        var queue = new ReloadOutcomeRegistry();
        var versionId = ConfigurationVersionId.NewId();
        using var cts = new CancellationTokenSource();

        var waiter = queue.WaitForAsync(versionId, TimeSpan.FromSeconds(30), cts.Token);
        cts.Cancel();
        var result = await waiter;

        result.Should().BeNull();
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static ReloadOutcome MakeCompletedOutcome(
        ConfigurationVersionId versionId,
        string[]? applied = null,
        string[]? restarted = null,
        FaultedReloadEntry[]? faulted = null,
        long elapsedMs = 42)
    {
        return new ReloadOutcome
        {
            Status = ReloadStatus.Completed,
            NewVersionId = versionId,
            AppliedInstances = applied ?? Array.Empty<string>(),
            RestartedInstances = restarted ?? Array.Empty<string>(),
            FaultedInstances = faulted ?? Array.Empty<FaultedReloadEntry>(),
            ElapsedMs = elapsedMs,
        };
    }
}
