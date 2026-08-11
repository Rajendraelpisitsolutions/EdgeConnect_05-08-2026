// ============================================================================
// File: Routing/RoutingReplayDriverTests.cs
// Covers: K1.3 slice 3 — the ReplayRouteDriver phase machine in isolation, with a stub
//         buffer (controlled backlog) and a stub session-state provider (controlled H/C).
//         Proves: empty route births and enters Live without DATA; a backlog replays,
//         a batch crossing H is split (only the in-phase prefix published + acked), then
//         CatchUp to Live with correct PublishContext phase/cutoffs/sequences; and a
//         partial publish acks nothing and is retried to a full success (strict-ack).
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.md §R5 slice 3;
//            …-v3.2-amendment.md §B5.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class RoutingReplayDriverTests
{
    [Fact]
    public async Task Driver_Empty_Route_Births_And_Enters_Live_Without_Data()
    {
        var sink = new FakeReplayAwareSink("sp");
        var driver = BuildDriver(pointCount: 0, h: 0, cursor: 0, c: 0, sink, out _);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(() => sink.CompleteCatchUpCount == 1, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await AwaitDriver(run);

        sink.BeginCount.Should().Be(1);
        sink.CompleteCatchUpCount.Should().Be(1);
        sink.PublishContexts.Should().BeEmpty(); // birth → Live with no DATA
    }

    [Fact]
    public async Task Driver_Replays_Backlog_Splits_At_H_Then_CatchUp_To_Live()
    {
        var sink = new FakeReplayAwareSink("sp");
        // 3 points (seq 0,1,2). H=2 → Replay is [0,2) = {0,1}; C=3 → CatchUp is [2,3) = {2}.
        var driver = BuildDriver(pointCount: 3, h: 2, cursor: 0, c: 3, sink, out var buffer);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(
            () => sink.CompleteCatchUpCount == 1 && sink.ReplayPublishedPoints.Count == 3,
            TimeSpan.FromSeconds(5));
        cts.Cancel();
        await AwaitDriver(run);

        sink.BeginCount.Should().Be(1);
        var ctxs = sink.PublishContexts;
        ctxs.Should().HaveCount(2);

        // Replay subrange, split at H (the dequeued batch {0,1,2} straddles H=2 → only {0,1} published).
        ctxs[0].Phase.Should().Be(ReplayPhase.Replay);
        ctxs[0].BatchFirstSequence.Should().Be(0);
        ctxs[0].BatchLastSequence.Should().Be(1);
        ctxs[0].ReplayCutoffExclusive.Should().Be(2);

        // CatchUp subrange, split at C.
        ctxs[1].Phase.Should().Be(ReplayPhase.CatchUp);
        ctxs[1].BatchFirstSequence.Should().Be(2);
        ctxs[1].BatchLastSequence.Should().Be(2);
        ctxs[1].CatchUpCutoffExclusive.Should().Be(3);

        sink.CompleteCatchUpCount.Should().Be(1);
        sink.ReplayPublishedPoints.Should().HaveCount(3);
        buffer.AckCount.Should().Be(2); // exactly one ack per published phase subrange
    }

    [Fact]
    public async Task Driver_Partial_Publish_Acks_Nothing_Then_Retries_To_Full_Success()
    {
        var sink = new FakeReplayAwareSink("sp") { PartialNext = 1 };
        // 1 backlog point (seq 0). H=1 → Replay {0}; C=1.
        var driver = BuildDriver(pointCount: 1, h: 1, cursor: 0, c: 1, sink, out var buffer);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(() => sink.CompleteCatchUpCount == 1, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await AwaitDriver(run);

        sink.ReplayPublishCallCount.Should().Be(2);        // 1 partial + 1 success (same subrange retried)
        sink.ReplayPublishedPoints.Should().HaveCount(1);  // delivered once, on the successful retry
        buffer.AckCount.Should().Be(1);                    // strict-ack: the partial acked NOTHING
    }

    [Fact]
    public async Task Driver_Below_Barrier_With_No_Rows_And_No_ForwardProgress_Fails_Closed()
    {
        var sink = new FakeReplayAwareSink("sp");
        // Birth H=2, cursor=0, but the buffer has NO rows and the authoritative cursor is still 0.
        var driver = BuildDriver(pointCount: 0, h: 2, cursor: 0, c: 2, sink, out var buffer,
            boundary: new StubBoundaryProvider(firstPending: 0, cutoff: 2));

        using var cts = new CancellationTokenSource();
        var ex = await Record.ExceptionAsync(async () => await driver.RunAsync(cts.Token));

        ex.Should().BeOfType<BufferException>();
        ((BufferException)ex!).Error.Code.Should().Be(CoreErrors.BufferCursorInconsistent);
        buffer.AckCount.Should().Be(0); // never synthesized an ack to cross the barrier
    }

    [Fact]
    public async Task Driver_Retention_FastForward_Reconciles_Without_Synthetic_Ack()
    {
        var sink = new FakeReplayAwareSink("sp");
        // Birth H=3, cursor=0, but all backlog expired (buffer empty) and the authoritative cursor
        // fast-forwarded to 3 (== H). The driver must advance to H WITHOUT acking.
        var driver = BuildDriver(pointCount: 0, h: 3, cursor: 0, c: 3, sink, out var buffer,
            boundary: new StubBoundaryProvider(firstPending: 3, cutoff: 3));

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(() => sink.CompleteCatchUpCount == 1, TimeSpan.FromSeconds(5)); // progressed to Live
        cts.Cancel();
        await AwaitDriver(run);

        buffer.AckCount.Should().Be(0);          // reconciled via the authoritative cursor, no synthetic ack
        sink.PublishContexts.Should().BeEmpty(); // nothing to publish
    }

    [Fact]
    public async Task Driver_NonRetryable_Publish_Failure_Faults_And_Acks_Nothing()
    {
        var sink = new FakeReplayAwareSink("sp")
        {
            PublishResultOverride = PublishResult.Failed(
                new AdapterError { Code = "TEST.FATAL", Category = ErrorCategory.Internal, Message = "nope", Retryable = false },
                TimeSpan.Zero),
        };
        var driver = BuildDriver(pointCount: 1, h: 1, cursor: 0, c: 1, sink, out var buffer);

        using var cts = new CancellationTokenSource();
        var ex = await Record.ExceptionAsync(async () => await driver.RunAsync(cts.Token));

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain(CoreErrors.RouteDeliveryFailed);
        buffer.AckCount.Should().Be(0); // a non-retryable failure acked nothing
    }

    [Fact]
    public async Task Driver_Mints_A_New_Session_Id_Per_Start_From_The_Shared_Identity_Source()
    {
        var sink = new FakeReplayAwareSink("sp");
        // ONE shared identity source across two driver starts (and, in production, across a NEW Route
        // object after unregister/re-register) — models the engine-owned process-wide source.
        var identity = new ReplaySessionIdentitySource();

        var route1 = BuildRoute(pointCount: 0, h: 0, cursor: 0, c: 0, sink, new UnusedBoundaryProvider(), out _, out var dispatcher1);
        using (var cts1 = new CancellationTokenSource())
        {
            var run1 = new ReplayRouteDriver(route1, route1.Replay!, dispatcher1, identity).RunAsync(cts1.Token);
            await WaitForAsync(() => sink.BeginCount == 1, TimeSpan.FromSeconds(5));
            cts1.Cancel();
            await AwaitDriver(run1);
        }

        var s1 = sink.LastSessionId;

        // A NEW Route object (as a re-registration would create) — the session id must still advance.
        var route2 = BuildRoute(pointCount: 0, h: 0, cursor: 0, c: 0, sink, new UnusedBoundaryProvider(), out _, out var dispatcher2);
        using (var cts2 = new CancellationTokenSource())
        {
            var run2 = new ReplayRouteDriver(route2, route2.Replay!, dispatcher2, identity).RunAsync(cts2.Token);
            await WaitForAsync(() => sink.BeginCount == 2, TimeSpan.FromSeconds(5));
            cts2.Cancel();
            await AwaitDriver(run2);
        }

        var s2 = sink.LastSessionId;

        s1.Should().NotBeNull();
        s2.Should().NotBeNull();
        s2!.Value.Value.Should().BeGreaterThan(s1!.Value.Value); // unique + monotonic across Route replacement
    }

    [Fact]
    public async Task Driver_Batch_Ahead_Of_Cursor_With_Retention_FastForward_Publishes_From_The_Survivor()
    {
        var sink = new FakeReplayAwareSink("sp");
        // 10 rows exist but retention expired [0,5): the buffer's persisted cursor is 5. Birth cursor
        // is a stale 0. The driver must reconcile to 5 and publish [5,9] — never silently skip [0,5).
        var driver = BuildDriver(pointCount: 10, h: 10, cursor: 0, c: 10, sink, out var buffer,
            boundary: new StubBoundaryProvider(firstPending: 5, cutoff: 10), bufferStartCursor: 5);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(() => sink.CompleteCatchUpCount == 1, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await AwaitDriver(run);

        sink.PublishContexts.Should().ContainSingle();
        sink.PublishContexts[0].BatchFirstSequence.Should().Be(5); // reconciled to the survivor, not 0
        sink.PublishContexts[0].BatchLastSequence.Should().Be(9);
        buffer.AckCount.Should().Be(1); // one ack for the published [5,9] — no synthetic ack for [0,5)
    }

    [Fact]
    public async Task Driver_Batch_Ahead_Of_Cursor_Without_Persisted_Progress_Fails_Closed()
    {
        var sink = new FakeReplayAwareSink("sp");
        // The batch starts at 5 but the persisted cursor is still 0 — an unexplained gap → corruption.
        var driver = BuildDriver(pointCount: 10, h: 10, cursor: 0, c: 10, sink, out var buffer,
            boundary: new StubBoundaryProvider(firstPending: 0, cutoff: 10), bufferStartCursor: 5);

        using var cts = new CancellationTokenSource();
        var ex = await Record.ExceptionAsync(async () => await driver.RunAsync(cts.Token));

        ex.Should().BeOfType<BufferException>();
        ((BufferException)ex!).Error.Code.Should().Be(CoreErrors.BufferCursorInconsistent);
        sink.PublishContexts.Should().BeEmpty();
        buffer.AckCount.Should().Be(0);
    }

    [Fact]
    public async Task Driver_Batch_Below_Cursor_Fails_Closed()
    {
        var sink = new FakeReplayAwareSink("sp");
        // Birth cursor is 5 but the buffer serves from 0 (persisted/local views diverged) → republish
        // of already-acked data must fail closed.
        var driver = BuildDriver(pointCount: 10, h: 10, cursor: 5, c: 10, sink, out var buffer,
            boundary: new StubBoundaryProvider(firstPending: 0, cutoff: 10), bufferStartCursor: 0);

        using var cts = new CancellationTokenSource();
        var ex = await Record.ExceptionAsync(async () => await driver.RunAsync(cts.Token));

        ex.Should().BeOfType<BufferException>();
        ((BufferException)ex!).Error.Code.Should().Be(CoreErrors.BufferCursorInconsistent);
        buffer.AckCount.Should().Be(0);
    }

    [Fact]
    public async Task Driver_Retry_Budget_Exhausted_Faults_And_Acks_Nothing()
    {
        // Always-partial publish: retries exhaust MaxRetries (3) → fault, subrange never acked.
        var sink = new FakeReplayAwareSink("sp") { PartialNext = int.MaxValue };
        var driver = BuildDriver(pointCount: 1, h: 1, cursor: 0, c: 1, sink, out var buffer);

        using var cts = new CancellationTokenSource();
        var ex = await Record.ExceptionAsync(async () => await driver.RunAsync(cts.Token));

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain(CoreErrors.RouteDeliveryFailed);
        buffer.AckCount.Should().Be(0);
        sink.ReplayPublishCallCount.Should().Be(4); // initial + MaxRetries(3)
    }

    // ---- slice 4: rebirth ---------------------------------------------------

    [Fact]
    public async Task Driver_First_Observed_Metric_Forces_Rebirth_Before_The_Triggering_Subrange_Is_Acked()
    {
        // Live from birth (H=0,C=0); the buffer holds [0,1]. On the first Live publish the sink models a
        // first-observed metric: it awaits RequestRebirthAsync then returns PARTIAL. The driver must ack
        // NOTHING, process the rebirth (fresh birth at H=2 under epoch 1), then re-drive Replay and
        // publish [0,1] under the new epoch — the triggering subrange is acked ONLY after the rebirth.
        var sink = new FakeReplayAwareSink("sp") { RequestRebirthOnNextPublish = true };
        var provider = new RebirthSessionStateProvider((0, 0), (0, 2));
        var driver = BuildDriver(pointCount: 2, h: 0, cursor: 0, c: 0, sink, out var buffer, sessionState: provider);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(
            () => sink.RebirthCount == 1 && sink.ReplayPublishedPoints.Count == 2,
            TimeSpan.FromSeconds(5));
        cts.Cancel();
        await AwaitDriver(run);

        sink.RebirthCount.Should().Be(1);
        sink.LastRebirth!.Epoch.Value.Should().Be(1);          // candidate epoch promoted on success
        buffer.AckCount.Should().Be(1);                              // ONLY the epoch-1 republish acked
        sink.ReplayPublishedPoints.Should().HaveCount(2);

        var ctxs = sink.PublishContexts;
        ctxs.Should().HaveCountGreaterThanOrEqualTo(2);
        ctxs[0].Epoch.Value.Should().Be(0);                          // the triggering (partial) publish
        ctxs[^1].Epoch.Value.Should().Be(1);                         // the successful republish, new epoch
    }

    [Fact]
    public async Task Driver_Rebirth_Wakes_An_Empty_Live_Route()
    {
        // Birth straight to an EMPTY Live route (no buffer, H=C=0), so the driver is asleep on the
        // combined wait. An out-of-band rebirth request (as a Node-Control command would arrive) must
        // wake it and drive a re-birth — proving the rebirth wake participates in the empty-Live wait.
        var sink = new FakeReplayAwareSink("sp");
        var driver = BuildDriver(pointCount: 0, h: 0, cursor: 0, c: 0, sink, out _);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(() => sink.CompleteCatchUpCount == 1 && sink.LastHost is not null, TimeSpan.FromSeconds(5));

        await sink.LastHost!.RequestRebirthAsync(
            RebirthRequest.Create(sink.LastSessionId!.Value, ReplayEpochId.Create(0), RebirthReason.HostCommand),
            CancellationToken.None);

        await WaitForAsync(() => sink.RebirthCount == 1, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await AwaitDriver(run);

        sink.RebirthCount.Should().Be(1);
        sink.LastRebirth!.Epoch.Value.Should().Be(1);
    }

    [Fact]
    public async Task Driver_Failed_Rebirth_Keeps_The_Old_Epoch_And_Faults()
    {
        // A rebirth whose RebirthAsync throws must fault the route AND leave the authoritative epoch
        // unchanged (promotion happens only on success). The triggering subrange is never acked.
        var sink = new FakeReplayAwareSink("sp")
        {
            RequestRebirthOnNextPublish = true,
            RebirthThrows = new InvalidOperationException("rebirth boom"),
        };
        var provider = new RebirthSessionStateProvider((0, 0), (0, 2));
        var driver = BuildDriver(pointCount: 2, h: 0, cursor: 0, c: 0, sink, out var buffer, sessionState: provider);

        using var cts = new CancellationTokenSource();
        var ex = await Record.ExceptionAsync(async () => await driver.RunAsync(cts.Token));

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("rebirth boom");
        sink.RebirthCount.Should().Be(1);                            // attempted once
        buffer.AckCount.Should().Be(0);                              // triggering subrange never acked
        ((ReplaySessionRebirthHost)sink.LastHost!).CurrentEpoch.Value.Should().Be(0); // NOT promoted
    }

    // ---- slice 4 r1: out-of-band rebirth is a control-plane barrier on the BUSY path ----

    [Fact]
    public Task Driver_OutOfBand_Rebirth_During_Replay_Backlog_Precedes_The_Next_Subrange()
        // Replay [0,3) served one seq at a time. A request accepted while the first subrange is in
        // flight must be processed before the second subrange publishes.
        => AssertOutOfBandRebirthPrecedesNextSubrange(
            births: new (long, long)[] { (0, 3), (1, 3) }, cutoffs: new long[] { 3, 3 });

    [Fact]
    public Task Driver_OutOfBand_Rebirth_During_CatchUp_Precedes_The_Next_Subrange()
        // CatchUp [0,3) (H=0, C=3). Same ordering: finish the in-flight subrange, then rebirth before more.
        => AssertOutOfBandRebirthPrecedesNextSubrange(
            births: new (long, long)[] { (0, 0), (1, 1) }, cutoffs: new long[] { 3, 1 });

    [Fact]
    public Task Driver_OutOfBand_Rebirth_During_Continuous_Live_Does_Not_Wait_For_Drain()
        // Live from birth with a continuously-available backlog. The rebirth must fire while data
        // remains — not defer until the route drains.
        => AssertOutOfBandRebirthPrecedesNextSubrange(
            births: new (long, long)[] { (0, 0), (1, 1) }, cutoffs: new long[] { 0, 1 });

    /// <summary>
    /// Drive a 3-point backlog served one subrange at a time. Hold the FIRST publish in flight on a
    /// gate, inject an out-of-band rebirth (a Node-Control command) for the current session/epoch, then
    /// let everything flow. Asserts the first subrange is published (and acked) BEFORE the rebirth, and
    /// the second subrange is published only AFTER it — i.e. the pending request is a control barrier on
    /// the busy path, not deferred until the route drains.
    /// </summary>
    private async Task AssertOutOfBandRebirthPrecedesNextSubrange((long, long)[] births, long[] cutoffs)
    {
        var sink = new FakeReplayAwareSink("sp");
        using var gate = new SemaphoreSlim(0, 1);
        sink.ReplayPublishGate = gate;
        var provider = new SequencedSessionStateProvider(births, cutoffs);
        var driver = BuildDriver(pointCount: 3, h: 0, cursor: 0, c: 0, sink, out _, sessionState: provider, maxServe: 1);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);

        // The first subrange publish is now in flight (blocked on the gate). Inject an out-of-band
        // rebirth while the driver is BUSY, then release the gate and let the rest flow freely.
        await WaitForAsync(() => sink.PublishEnteredCount == 1, TimeSpan.FromSeconds(5));
        await sink.LastHost!.RequestRebirthAsync(
            RebirthRequest.Create(sink.LastSessionId!.Value, ReplayEpochId.Create(0), RebirthReason.HostCommand),
            CancellationToken.None);
        sink.ReplayPublishGate = null;
        gate.Release();

        await WaitForAsync(
            () => sink.RebirthCount == 1 && sink.ReplayEvents.Contains("pub:e1:1-1"),
            TimeSpan.FromSeconds(5));
        cts.Cancel();
        await AwaitDriver(run);

        var events = sink.ReplayEvents.ToList();
        var rebirthIdx = events.IndexOf("rebirth:e1");
        rebirthIdx.Should().BeGreaterThan(-1);
        events.IndexOf("pub:e0:0-0").Should().BeInRange(0, rebirthIdx - 1);   // 1st subrange (acked) BEFORE rebirth
        events.IndexOf("pub:e1:1-1").Should().BeGreaterThan(rebirthIdx);      // 2nd subrange only AFTER rebirth
    }

    [Fact]
    public async Task Driver_Rebirth_Accepted_During_Retry_Backoff_Preempts_The_Retry()
    {
        // A subrange publish returns not-full → the driver enters its retry backoff. An out-of-band
        // rebirth accepted during that window must be processed BEFORE any retry publish — never a DATA
        // retry ahead of an accepted control request. A long backoff makes the window deterministic
        // against the microsecond injection.
        var sink = new FakeReplayAwareSink("sp") { PartialNext = 1 };
        var provider = new SequencedSessionStateProvider(new (long, long)[] { (0, 1), (0, 1) }, new long[] { 1, 1 });
        var driver = BuildDriver(pointCount: 1, h: 0, cursor: 0, c: 0, sink, out _, sessionState: provider, initialBackoffMs: 300);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);

        // The partial publish has happened (entered == 1) and the driver is now backing off before a
        // retry. Inject the out-of-band rebirth into that window.
        await WaitForAsync(() => sink.PublishEnteredCount == 1, TimeSpan.FromSeconds(5));
        await sink.LastHost!.RequestRebirthAsync(
            RebirthRequest.Create(sink.LastSessionId!.Value, ReplayEpochId.Create(0), RebirthReason.HostCommand),
            CancellationToken.None);

        await WaitForAsync(() => sink.RebirthCount == 1, TimeSpan.FromSeconds(5));
        cts.Cancel();
        await AwaitDriver(run);

        // Exactly one publish had entered when the rebirth fired — the retry never ran ahead of it.
        sink.PublishEnteredAtLastRebirth.Should().Be(1);
        sink.RebirthCount.Should().Be(1);
    }

    // ---- slice 5: graceful session end --------------------------------------

    [Fact]
    public async Task Driver_Emits_EndSession_Once_On_Graceful_Stop()
    {
        var sink = new FakeReplayAwareSink("sp");
        var driver = BuildDriver(pointCount: 0, h: 0, cursor: 0, c: 0, sink, out _);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(() => sink.CompleteCatchUpCount == 1, TimeSpan.FromSeconds(5)); // begun + Live
        cts.Cancel();
        await AwaitDriver(run);

        sink.EndSessionCount.Should().Be(1);                        // exactly once for the begun session
        sink.LastEndReason.Should().Be(ReplaySessionEndReason.Stop); // default reason
    }

    [Fact]
    public async Task Driver_Does_Not_Emit_EndSession_When_Begin_Fails()
    {
        var sink = new FakeReplayAwareSink("sp") { BeginThrows = new InvalidOperationException("birth boom") };
        var driver = BuildDriver(pointCount: 0, h: 0, cursor: 0, c: 0, sink, out _);

        using var cts = new CancellationTokenSource();
        var ex = await Record.ExceptionAsync(async () => await driver.RunAsync(cts.Token));

        ex.Should().BeOfType<InvalidOperationException>();          // birth fault propagates
        sink.EndSessionCount.Should().Be(0);                        // no session was established → no end
    }

    [Fact]
    public async Task Driver_EndSession_Failure_Is_Swallowed_On_Shutdown()
    {
        // A throwing EndSessionAsync must be REPORTED but not prevent the driver from completing — the
        // worker must still return so the route reaches Stopped and the Host stops the sink.
        var sink = new FakeReplayAwareSink("sp") { EndThrows = new InvalidOperationException("death boom") };
        var driver = BuildDriver(pointCount: 0, h: 0, cursor: 0, c: 0, sink, out _);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(() => sink.CompleteCatchUpCount == 1, TimeSpan.FromSeconds(5));
        cts.Cancel();

        var ex = await Record.ExceptionAsync(async () => await run); // completes WITHOUT throwing the End failure
        ex.Should().BeNull();
        sink.EndSessionCount.Should().Be(1);                        // End was attempted
    }

    [Fact]
    public async Task Driver_Blocking_EndSession_Is_Bounded_So_Shutdown_Cannot_Wedge()
    {
        // [s5 r1 blocker 1] A non-cooperative EndSessionAsync (blocks until ITS token is cancelled) must
        // not wedge shutdown: End runs on a FRESH bounded token, so the driver still completes.
        var sink = new FakeReplayAwareSink("sp") { EndBlocksUntilCancelled = true };
        var driver = BuildDriver(pointCount: 0, h: 0, cursor: 0, c: 0, sink, out _);
        driver.EndSessionTimeout = TimeSpan.FromMilliseconds(200);

        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(cts.Token);
        await WaitForAsync(() => sink.CompleteCatchUpCount == 1, TimeSpan.FromSeconds(5));
        cts.Cancel();

        var ex = await Record.ExceptionAsync(async () => await run.WaitAsync(TimeSpan.FromSeconds(5)));
        ex.Should().BeNull();                                       // the driver completed within the bound
        sink.EndSessionCount.Should().Be(1);
        sink.EndTokenObservedCancellation.Should().BeTrue();       // End's OWN token was cancelled at the bound
    }

    [Theory]
    [InlineData(ReplaySessionEndReason.ConfigurationReplaced, ReplaySessionEndReason.Stop)]
    [InlineData(ReplaySessionEndReason.Stop, ReplaySessionEndReason.ConfigurationReplaced)]
    public void Route_TryBeginStop_Winner_Selects_The_Reason_A_Racing_Caller_Cannot_Overwrite_It(
        ReplaySessionEndReason winner, ReplaySessionEndReason loser)
    {
        // [s5 r1 blocker 2] Claiming shutdown + publishing its reason is ONE atomic route operation:
        // only the caller that wins the transition to Stopping selects the reason; a later caller loses
        // and MUST NOT overwrite it (so an ordinary Stop cannot clobber a ConfigurationReplaced in flight).
        var sink = new FakeReplayAwareSink("sp");
        var route = BuildRoute(0, 0, 0, 0, sink, new UnusedBoundaryProvider(), out _, out _);
        route.Lifecycle.TryTransitionTo(RouteState.Starting, "t");
        route.Lifecycle.TryTransitionTo(RouteState.Running, "t");

        route.TryBeginStop(winner).Should().BeTrue();
        route.PendingEndReason.Should().Be(winner);

        route.TryBeginStop(loser).Should().BeFalse();   // already Stopping → loses the claim
        route.PendingEndReason.Should().Be(winner);     // …and does NOT overwrite the winner's reason
    }

    // ---- helpers ------------------------------------------------------------

    private static ReplayRouteDriver BuildDriver(
        int pointCount, long h, long cursor, long c, FakeReplayAwareSink sink, out StubReplayBuffer buffer,
        IReplayBoundaryProvider? boundary = null, long bufferStartCursor = 0,
        IReplaySessionStateProvider? sessionState = null, int maxServe = int.MaxValue, int initialBackoffMs = 1)
    {
        var route = BuildRoute(pointCount, h, cursor, c, sink, boundary ?? new UnusedBoundaryProvider(), out buffer, out var dispatcher, bufferStartCursor, sessionState, maxServe, initialBackoffMs);
        return new ReplayRouteDriver(route, route.Replay!, dispatcher, new ReplaySessionIdentitySource());
    }

    private static Route BuildRoute(
        int pointCount, long h, long cursor, long c, FakeReplayAwareSink sink, IReplayBoundaryProvider boundary,
        out StubReplayBuffer buffer, out FanoutDispatcher dispatcher, long bufferStartCursor = 0,
        IReplaySessionStateProvider? sessionState = null, int maxServe = int.MaxValue, int initialBackoffMs = 1)
    {
        buffer = new StubReplayBuffer(pointCount, bufferStartCursor, maxServe);
        var provider = sessionState ?? new StubSessionStateProvider(h, cursor, c);
        var context = new ReplayRouteContext(
            new UnusedReplayRouteBuffer(), sink, "sp", RouteSchemaGeneration.Create(0), boundary, provider);

        var def = new RouteDefinition
        {
            RouteId = "route-a",
            GatewayId = RoutingTestData.GatewayId,
            Source = new FakeSourceIntake("src-1"),
            Sinks = new[] { (ISinkAdapter)sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = new BufferPolicy { Mode = BufferMode.StoreAndForward, MaxDepth = 64, DropPolicy = DropPolicy.Block, ReclaimInterval = TimeSpan.FromMilliseconds(50) },
            Delivery = new DeliveryPolicyConfig { Mode = DeliveryMode.AtLeastOnce, MaxRetries = 3, InitialBackoffMs = initialBackoffMs, MaxBackoffMs = Math.Max(initialBackoffMs, 5), BackoffMultiplier = 1.0, JitterPercent = 0 },
        };

        dispatcher = new FanoutDispatcher();
        dispatcher.RegisterSink("sp");
        var lifecycle = new RouteLifecycleManager("route-a", NullRoutingEngineDiagnostics.Instance);
        return new Route(def, buffer, dispatcher, lifecycle, context);
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(5).ConfigureAwait(false);
        }
        throw new TimeoutException("Predicate did not become true within the timeout.");
    }

    private static async Task AwaitDriver(Task run)
    {
        try { await run.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* driver stopped by cancellation */ }
    }

    // ---- stubs --------------------------------------------------------------

    /// <summary>An IMessageBuffer holding a fixed, contiguous point set (seq 0..N-1); Dequeue/Ack only.</summary>
    private sealed class StubReplayBuffer : IMessageBuffer
    {
        private readonly List<CanonicalDataPoint> _points;
        private readonly int _maxServe;
        private long _cursor;
        private int _ackCount;

        public StubReplayBuffer(int count, long startCursor = 0, int maxServe = int.MaxValue)
        {
            _points = Enumerable.Range(0, count).Select(i => RoutingTestData.MakePoint(i, tag: $"Spindle/T{i}")).ToList();
            // Simulate a persisted cursor that may sit ahead of 0 (e.g. after MaxAge retention
            // expired the earliest rows): DequeueBatch then returns only from startCursor onward.
            _cursor = startCursor;
            // Cap the per-dequeue span so a backlog is served as SEPARATE subranges (busy-path tests).
            _maxServe = maxServe;
        }

        public int AckCount => Volatile.Read(ref _ackCount);

        public string BufferId => "route-a";

        public ValueTask<BufferBatch> DequeueBatchAsync(string sinkId, int maxCount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = (int)Volatile.Read(ref _cursor);
            if (start >= _points.Count)
            {
                return new ValueTask<BufferBatch>(BufferBatch.Empty);
            }

            var end = Math.Min(start + Math.Min(maxCount, _maxServe), _points.Count);
            var slice = _points.GetRange(start, end - start);
            return new ValueTask<BufferBatch>(new BufferBatch
            {
                Points = slice,
                FirstSequence = start,
                LastSequence = end - 1,
            });
        }

        public ValueTask AckAsync(string sinkId, long upToSequence, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _ackCount);
            Volatile.Write(ref _cursor, upToSequence + 1);
            return ValueTask.CompletedTask;
        }

        public ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BufferStats> GetStatsAsync() => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubSessionStateProvider : IReplaySessionStateProvider
    {
        private readonly long _h;
        private readonly long _cursor;
        private readonly long _c;

        public StubSessionStateProvider(long h, long cursor, long c)
        {
            _h = h;
            _cursor = cursor;
            _c = c;
        }

        public ValueTask<ReplaySessionStartState> CaptureBirthStateAsync(string routeId, string sinkId, CancellationToken cancellationToken)
            => new(ReplaySessionStartState.Create(ReplayBoundary.Create(_cursor, _h), Empty()));

        public ValueTask<ReplaySessionCutoverState> CaptureCutoverAsync(string routeId, CancellationToken cancellationToken)
            => new(ReplaySessionCutoverState.Create(_c, Empty()));

        private static LatestValueSnapshot Empty() => LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(0));
    }

    /// <summary>
    /// A session-state provider that returns a DIFFERENT birth boundary per capture — so a rebirth
    /// re-captures a fresh, populated birth at a NEW H. CaptureCutover returns C == the most recent
    /// birth's H (so CatchUp is empty and the driver enters Live once the backlog below H is drained).
    /// The last configured birth repeats if capture is called more times than boundaries supplied.
    /// </summary>
    private sealed class RebirthSessionStateProvider : IReplaySessionStateProvider
    {
        private readonly Queue<(long Cursor, long H)> _births;
        private long _lastH;

        public RebirthSessionStateProvider(params (long Cursor, long H)[] births)
            => _births = new Queue<(long, long)>(births);

        public ValueTask<ReplaySessionStartState> CaptureBirthStateAsync(string routeId, string sinkId, CancellationToken cancellationToken)
        {
            var (cursor, h) = _births.Count > 1 ? _births.Dequeue() : _births.Peek();
            _lastH = h;
            return new(ReplaySessionStartState.Create(ReplayBoundary.Create(cursor, h), Empty()));
        }

        public ValueTask<ReplaySessionCutoverState> CaptureCutoverAsync(string routeId, CancellationToken cancellationToken)
            => new(ReplaySessionCutoverState.Create(_lastH, Empty()));

        private static LatestValueSnapshot Empty() => LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(0));
    }

    /// <summary>
    /// A session-state provider driven by explicit per-capture birth boundaries AND cutoffs — so an
    /// ordering test can place a rebirth in a chosen phase (Replay/CatchUp/Live) both before and after.
    /// The last configured value repeats if capture is called more times than supplied.
    /// </summary>
    private sealed class SequencedSessionStateProvider : IReplaySessionStateProvider
    {
        private readonly Queue<(long Cursor, long H)> _births;
        private readonly Queue<long> _cutoffs;

        public SequencedSessionStateProvider((long Cursor, long H)[] births, long[] cutoffs)
        {
            _births = new Queue<(long, long)>(births);
            _cutoffs = new Queue<long>(cutoffs);
        }

        public ValueTask<ReplaySessionStartState> CaptureBirthStateAsync(string routeId, string sinkId, CancellationToken cancellationToken)
        {
            var (cursor, h) = _births.Count > 1 ? _births.Dequeue() : _births.Peek();
            return new(ReplaySessionStartState.Create(ReplayBoundary.Create(cursor, h), Empty()));
        }

        public ValueTask<ReplaySessionCutoverState> CaptureCutoverAsync(string routeId, CancellationToken cancellationToken)
        {
            var c = _cutoffs.Count > 1 ? _cutoffs.Dequeue() : _cutoffs.Peek();
            return new(ReplaySessionCutoverState.Create(c, Empty()));
        }

        private static LatestValueSnapshot Empty() => LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(0));
    }

    private sealed class UnusedReplayRouteBuffer : IReplayRouteBuffer
    {
        public bool IsReplayTrackingEnabled => throw new NotSupportedException();
        public ValueTask<ReplayRouteActivation> ActivateReplayAsync(string routeId, string replaySinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<AssignedSequenceRange> AppendTrackedAsync(IReadOnlyList<CanonicalDataPoint> points, RouteSchemaGeneration expectedGeneration, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedBoundaryProvider : IReplayBoundaryProvider
    {
        public ValueTask<ReplayBoundary> CaptureReplayBoundaryAsync(string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubBoundaryProvider : IReplayBoundaryProvider
    {
        private readonly long _firstPending;
        private readonly long _cutoff;
        public StubBoundaryProvider(long firstPending, long cutoff) { _firstPending = firstPending; _cutoff = cutoff; }
        public ValueTask<ReplayBoundary> CaptureReplayBoundaryAsync(string sinkId, CancellationToken cancellationToken)
            => new(ReplayBoundary.Create(_firstPending, _cutoff));
    }
}
