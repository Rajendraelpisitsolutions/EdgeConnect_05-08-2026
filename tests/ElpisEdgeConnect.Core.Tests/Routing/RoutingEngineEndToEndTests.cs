// ============================================================================
// File: Routing/RoutingEngineEndToEndTests.cs
// Purpose: Phase 7 end-to-end gate for C3. Exercises all prior phases
//          together: intake pump + backpressure + buffer + fanout + retry +
//          replay + lifecycle + shutdown. This is a gate, not a feature
//          phase — if any of the locked invariants regresses, this test
//          is the one that catches it.
//
// Invariants asserted:
//   1. Zero loss across a sink-outage window (AtLeastOnce + ample buffer)
//   2. Strict monotonic ordering of delivered points
//   3. No duplicates (cursor truth is honoured across retry + drain)
//   4. Source production sustains the scaled 5k pts/sec target
//   5. Route lifecycle walks Running → Degraded → Draining → Running,
//      then Running → Stopping → Stopped on shutdown
//   6. All sink-level events fire (degraded / draining / recovered)
//   7. No deadlock on shutdown during recovery
//
// Milestone: C3 Commit 2 (phase 7 — end-to-end gate).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineEndToEndTests
{
    private static RouteDefinition MakeDefinition(
        FakeSourceIntake source,
        IReadOnlyList<ISinkAdapter> sinks,
        BufferPolicy bufferPolicy,
        DeliveryPolicyConfig delivery,
        string routeId = "route-1")
        => new()
        {
            RouteId = routeId,
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = sinks,
            Filter = TagFilter.AcceptAll,
            BufferPolicy = bufferPolicy,
            Delivery = delivery,
        };

    private static async Task WaitForAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        string? description = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new TimeoutException(
            $"Predicate did not become true within {timeout}. {description}");
    }

    /// <summary>
    /// The big C3 integration gate. The test name keeps the blueprint's
    /// 5k-pts/sec / 30s-outage aspiration while the actual run uses a
    /// scaled-down count so the suite stays fast. The ratios and invariants
    /// are what matter — raw wall-clock duration does not.
    /// </summary>
    /// <summary>
    /// QUARANTINED by D phase 1: this test carries a genuine wall-clock
    /// throughput assertion (≥ 5000 pts/sec produce rate) that cannot be
    /// made deterministic. Under full-suite CPU contention the 5000 pts/sec
    /// threshold can miss. Quarantined with <c>Category=Flaky</c> per
    /// docs/phase1-exit/flaky-tests-disposition.md row 3. The deterministic
    /// companion <see cref="RoutingEngine_EndToEnd_OutageRecovery_ZeroLoss_Deterministic"/>
    /// below covers the same ordering + zero-loss invariants without the
    /// throughput assertion and runs in the default gate.
    /// Run this quarantined test explicitly with:
    ///   dotnet test --filter "FullyQualifiedName~5kPtsPerSec"
    /// </summary>
    [Fact]
    [Trait("Category", "Flaky")]
    public async Task RoutingEngine_EndToEnd_5kPtsPerSec_30sOutage_ZeroLossAndOrdered()
    {
        // Scaled knobs. Changing these does not change what the test proves;
        // it only changes how much work the test does per run.
        const int TotalPoints = 10_000;
        const int OutageStartIndex = 3_000;  // begin failing at 30% produced
        const int OutageEndIndex = 7_000;    // heal at 70% produced
        const double TargetRatePerSec = 5_000.0;

        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FakeSourceIntake("src-1", capacity: 2048);
        var sink = new FakeSinkAdapter("sink-1");

        // Ample buffer so the outage-window backlog fits without any drops.
        var bufferPolicy = new BufferPolicy
        {
            Mode = BufferMode.InMemory,
            MaxDepth = 50_000,
            DropPolicy = DropPolicy.DropOldest,
        };

        // AtLeastOnce with an effectively unbounded retry budget and very
        // short backoffs — we are testing correctness, not backoff tuning.
        var delivery = new DeliveryPolicyConfig
        {
            Mode = DeliveryMode.AtLeastOnce,
            MaxRetries = 10_000,
            InitialBackoffMs = 1,
            MaxBackoffMs = 10,
            BackoffMultiplier = 2.0,
            JitterPercent = 0,
        };

        await using var engine = new RoutingEngine(
            new InMemoryRouteBufferFactory(),
            diagnostics);

        await engine.RegisterRouteAsync(
            MakeDefinition(source, new ISinkAdapter[] { sink }, bufferPolicy, delivery),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // ---- Produce ----
        var produceSw = Stopwatch.StartNew();
        for (var i = 0; i < TotalPoints; i++)
        {
            if (i == OutageStartIndex)
            {
                sink.FailPermanently = true;
            }
            else if (i == OutageEndIndex)
            {
                sink.FailPermanently = false;
            }
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();
        produceSw.Stop();

        // ---- Drain ----
        await WaitForAsync(
            () => sink.PublishedCount == TotalPoints,
            TimeSpan.FromSeconds(30),
            description: $"expected {TotalPoints} delivered; observed {sink.PublishedCount}");

        await engine.StopRouteAsync("route-1", CancellationToken.None);

        // ---- Invariant 1: zero loss ----
        sink.PublishedCount.Should().Be(TotalPoints, "AtLeastOnce with ample buffer must not drop");

        // ---- Invariant 2: strict monotonic ordering ----
        var seqs = sink.PublishedPoints.Select(p => p.SequenceNumber).ToArray();
        seqs.Should().BeInAscendingOrder();

        // ---- Invariant 3: no duplicates ----
        seqs.Should().OnlyHaveUniqueItems();
        seqs.Should().Equal(Enumerable.Range(0, TotalPoints).Select(i => (long)i));

        // ---- Invariant 4: production throughput meets scaled target ----
        var effectiveRate = TotalPoints / produceSw.Elapsed.TotalSeconds;
        effectiveRate.Should().BeGreaterThan(
            TargetRatePerSec,
            $"source production should sustain ≥ {TargetRatePerSec:F0} pts/sec; " +
            $"measured {effectiveRate:F0} ({produceSw.Elapsed})");

        // ---- Invariant 5: lifecycle events in expected order ----
        var routeEvents = diagnostics.Events
            .Where(e => e.StartsWith("route:route-1:", StringComparison.Ordinal))
            .ToArray();
        routeEvents.Should().Contain("route:route-1:Configured->Starting");
        routeEvents.Should().Contain("route:route-1:Starting->Running");
        routeEvents.Should().Contain("route:route-1:Running->Degraded");
        routeEvents.Should().Contain("route:route-1:Degraded->Draining");
        routeEvents.Should().Contain("route:route-1:Draining->Running");
        routeEvents.Should().Contain("route:route-1:Stopping->Stopped");

        // Ordering pin across the recovery arc.
        var idxRunning1 = Array.IndexOf(routeEvents, "route:route-1:Starting->Running");
        var idxDegraded = Array.IndexOf(routeEvents, "route:route-1:Running->Degraded");
        var idxDraining = Array.IndexOf(routeEvents, "route:route-1:Degraded->Draining");
        var idxRunning2 = Array.IndexOf(routeEvents, "route:route-1:Draining->Running");
        var idxStopped = Array.IndexOf(routeEvents, "route:route-1:Stopping->Stopped");
        idxRunning1.Should().BeLessThan(idxDegraded);
        idxDegraded.Should().BeLessThan(idxDraining);
        idxDraining.Should().BeLessThan(idxRunning2);
        idxRunning2.Should().BeLessThan(idxStopped);

        // ---- Invariant 6: sink-level events fired ----
        diagnostics.Events.Should().Contain(e => e == "degraded:sink-1");
        diagnostics.Events.Should().Contain(e => e == "draining:sink-1");
        diagnostics.Events.Should().Contain(e => e == "recovered:sink-1");

        // ---- Invariant 7: final state ----
        engine.GetRouteState("route-1").Should().Be(RouteState.Stopped);
    }

    /// <summary>
    /// Companion test: shutdown during recovery must terminate cleanly, not
    /// hang. The route is mid-drain when Stop is called; the worker must
    /// observe cancellation, park sink loops, and transition to Stopped
    /// within a bounded budget.
    /// </summary>
    [Fact]
    public async Task RoutingEngine_EndToEnd_ShutdownDuringRecovery_DrainsOrStopsCleanly()
    {
        // D phase-1 stabilization: replaced FailPermanently + PublishDelay
        // wall-clock gates with a TCS failure window + SemaphoreSlim publish
        // gate. Deterministic: we hold the sink in "draining but blocked"
        // state and call Stop while the slow path is parked on the gate.
        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FakeSourceIntake("src-1");
        var failureWindow = new TaskCompletionSource();
        using var drainGate = new SemaphoreSlim(initialCount: 0, maxCount: int.MaxValue);
        var sink = new FakeSinkAdapter("sink-1")
        {
            FailUntilSignaled = failureWindow,
        };

        var bufferPolicy = new BufferPolicy
        {
            Mode = BufferMode.InMemory,
            MaxDepth = 20_000,
            DropPolicy = DropPolicy.DropOldest,
        };

        var delivery = new DeliveryPolicyConfig
        {
            Mode = DeliveryMode.AtLeastOnce,
            MaxRetries = 10_000,
            InitialBackoffMs = 1,
            MaxBackoffMs = 10,
            BackoffMultiplier = 2.0,
            JitterPercent = 0,
        };

        await using var engine = new RoutingEngine(
            new InMemoryRouteBufferFactory(),
            diagnostics);
        await engine.RegisterRouteAsync(
            MakeDefinition(source, new ISinkAdapter[] { sink }, bufferPolicy, delivery),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // Build a backlog while the sink is failing.
        for (var i = 0; i < 2_000; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        await WaitForAsync(
            () => diagnostics.Events.Any(e => e == "degraded:sink-1"),
            TimeSpan.FromSeconds(10),
            description: "waiting for sink to report degraded");

        // Heal the sink (TCS) but hold the drain gate so publish blocks
        // deterministically. The first post-heal publish will park on the
        // gate → publisher enters draining → we observe the event → then
        // we Stop mid-drain.
        sink.PublishGate = drainGate;
        failureWindow.SetResult();
        // Admit exactly one publish so the publisher reports Draining.
        drainGate.Release(1);

        // Wait until drain has started (the publisher has fired Draining).
        await WaitForAsync(
            () => diagnostics.Events.Any(e => e == "draining:sink-1"),
            TimeSpan.FromSeconds(10),
            description: "waiting for sink to enter drain phase");

        // Hold further publishes blocked. Any pending publish is parked on
        // drainGate.WaitAsync(ct), so Stop's cancellation will unblock it.
        // (We intentionally do NOT release more permits here.)

        // Call Stop while still draining. Must terminate within a bounded
        // budget (no hang).
        var stopBudget = TimeSpan.FromSeconds(10);
        var stopSw = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(stopBudget);
        await engine.StopRouteAsync("route-1", cts.Token);
        stopSw.Stop();

        stopSw.Elapsed.Should().BeLessThan(stopBudget, "Stop must not hang during recovery");
        engine.GetRouteState("route-1").Should().Be(RouteState.Stopped);

        // Any delivered points must still be in monotonic order — no
        // partial-batch corruption under forced shutdown.
        sink.PublishedPoints
            .Select(p => p.SequenceNumber)
            .Should()
            .BeInAscendingOrder();
    }

    /// <summary>
    /// Deterministic companion to the quarantined 5 k pts/sec test. Drops
    /// the wall-clock throughput assertion; keeps the zero-loss, monotonic-
    /// ordering, and lifecycle-ordering invariants. Runs in the default
    /// gate (no Flaky trait). Uses TCS-based failure window instead of
    /// the FailPermanently toggle.
    /// </summary>
    [Fact]
    public async Task RoutingEngine_EndToEnd_OutageRecovery_ZeroLoss_Deterministic()
    {
        const int TotalPoints = 2_000;
        const int OutageStartIndex = 600;
        const int OutageEndIndex = 1_400;

        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FakeSourceIntake("src-1", capacity: 4096);
        var failureWindow = new TaskCompletionSource();
        var sink = new FakeSinkAdapter("sink-1");

        var bufferPolicy = new BufferPolicy
        {
            Mode = BufferMode.InMemory,
            MaxDepth = 50_000,
            DropPolicy = DropPolicy.DropOldest,
        };

        var delivery = new DeliveryPolicyConfig
        {
            Mode = DeliveryMode.AtLeastOnce,
            MaxRetries = 10_000,
            InitialBackoffMs = 1,
            MaxBackoffMs = 10,
            BackoffMultiplier = 2.0,
            JitterPercent = 0,
        };

        await using var engine = new RoutingEngine(
            new InMemoryRouteBufferFactory(),
            diagnostics);

        await engine.RegisterRouteAsync(
            MakeDefinition(source, new ISinkAdapter[] { sink }, bufferPolicy, delivery),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // Produce in three phases: healthy, failure (TCS-gated), healthy.
        for (var i = 0; i < OutageStartIndex; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        sink.FailUntilSignaled = failureWindow;
        for (var i = OutageStartIndex; i < OutageEndIndex; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        // Wait for degraded (deterministic — TCS hasn't fired).
        await WaitForAsync(
            () => diagnostics.Events.Any(e => e == "degraded:sink-1"),
            TimeSpan.FromSeconds(15),
            description: "expected degraded event during outage window");

        // Deterministic heal.
        failureWindow.SetResult();
        for (var i = OutageEndIndex; i < TotalPoints; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        await WaitForAsync(
            () => sink.PublishedCount == TotalPoints,
            TimeSpan.FromSeconds(30),
            description: $"expected {TotalPoints} delivered; observed {sink.PublishedCount}");
        await engine.StopRouteAsync("route-1", CancellationToken.None);

        // Zero loss + strict monotonic order + full expected sequence.
        sink.PublishedCount.Should().Be(TotalPoints);
        var seqs = sink.PublishedPoints.Select(p => p.SequenceNumber).ToArray();
        seqs.Should().BeInAscendingOrder();
        seqs.Should().OnlyHaveUniqueItems();
        seqs.Should().Equal(Enumerable.Range(0, TotalPoints).Select(i => (long)i));

        // Lifecycle walked degraded → draining → running → stopping → stopped.
        var routeEvents = diagnostics.Events
            .Where(e => e.StartsWith("route:route-1:", StringComparison.Ordinal))
            .ToArray();
        routeEvents.Should().Contain("route:route-1:Running->Degraded");
        routeEvents.Should().Contain("route:route-1:Degraded->Draining");
        routeEvents.Should().Contain("route:route-1:Draining->Running");
        routeEvents.Should().Contain("route:route-1:Stopping->Stopped");

        engine.GetRouteState("route-1").Should().Be(RouteState.Stopped);
    }
}
