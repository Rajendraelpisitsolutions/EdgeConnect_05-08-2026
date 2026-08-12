// ============================================================================
// File: Routing/RoutingEngineWorkerFaultTests.cs
// Purpose: Bug 2 (P0) regression cover — the worker task that drives a
//          route's intake pump + sink loops is started fire-and-forget by
//          RoutingEngine.StartRouteAsync. Without explicit fault
//          observation an exception that escapes RouteWorker.RunAsync
//          would leave the route in Running while no data flows — the
//          load-bearing invariant the smoke bug violated.
//
//          These tests pin: a worker task that crashes outside cooperative
//          cancellation transitions the route to Failed and the diagnostics
//          surface emits a Running→Failed (or Starting→Failed) event.
//
// Reference: docs/sessions/2026-05-20-followup-chips.md (Chip 1)
//            docs/sessions/2026-05-20-100-cnc-deployment-readiness.md §5
//            ARCHITECTURE_BLUEPRINT.md §19.9 (Lifecycle)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineWorkerFaultTests
{
    private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
        return predicate();
    }

    [Fact]
    public async Task StartRoute_WorkerThrows_RouteTransitionsToFailed()
    {
        // The route's buffer throws on RegisterSinkAsync — the very first
        // thing RouteWorker.RunAsync does. Before the fix, the route stayed
        // Running while the worker task was dead; after the fix, the
        // worker's continuation observes the exception and transitions
        // the route to Failed.
        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");

        await using var engine = new RoutingEngine(
            new ThrowingBufferFactory(throwOnRegisterSink: true),
            diagnostics);

        await engine.RegisterRouteAsync(new RouteDefinition
        {
            RouteId = "route-1",
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new[] { (Core.Adapters.ISinkAdapter)sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = RoutingTestData.DefaultDeliveryPolicy(),
        }, CancellationToken.None);

        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // The worker continuation must observe the throw and transition
        // the route off Running.
        var reachedFailed = await WaitForAsync(
            () => engine.GetRouteState("route-1") == RouteState.Failed,
            TimeSpan.FromSeconds(5));

        reachedFailed.Should().BeTrue(
            "Bug 2 invariant: a worker task that throws outside cooperative "
            + "cancellation must transition the route to Failed. "
            + $"Observed state: {engine.GetRouteState("route-1")}.");

        // The diagnostics surface must have seen the Running → Failed
        // transition (the route reached Running before the worker task's
        // scheduler-thread async continuation actually executed RunAsync's
        // pre-loop work).
        diagnostics.Events.Should().Contain(e => e.Contains("->Failed"));
    }

    [Fact]
    public async Task StartRoute_WorkerCancelled_DoesNotTransitionToFailed()
    {
        // Counter-test for the cancellation path. When the route is
        // stopped via cancellation, the worker's OperationCanceledException
        // must NOT be treated as a fault — Stopped is the destination, not
        // Failed.
        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");

        await using var engine = new RoutingEngine(
            new InMemoryRouteBufferFactory(),
            diagnostics);

        await engine.RegisterRouteAsync(new RouteDefinition
        {
            RouteId = "route-1",
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new[] { (Core.Adapters.ISinkAdapter)sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = RoutingTestData.DefaultDeliveryPolicy(),
        }, CancellationToken.None);

        await engine.StartRouteAsync("route-1", CancellationToken.None);
        engine.GetRouteState("route-1").Should().Be(RouteState.Running);

        await engine.StopRouteAsync("route-1", CancellationToken.None);
        engine.GetRouteState("route-1").Should().Be(RouteState.Stopped,
            "cooperative cancellation must reach Stopped, never Failed");

        diagnostics.Events.Should().NotContain(e => e.Contains("->Failed"),
            "no Failed transition should fire under cooperative shutdown");
    }

    [Fact]
    public async Task StopRoute_WorkerFaultsDuringShutdown_ReachesStoppedWithoutThrowing()
    {
        // Regression: a route whose worker FAULTS during shutdown lands in
        // Failed (Stopping → Failed is legal). StopRouteAsync must still reach
        // Stopped via the legal Failed → Stopping → Stopped path. The pre-fix
        // code forced an illegal Failed → Stopped, which threw
        // [CORE.ROUTE_INVALID_LIFECYCLE_TRANSITION] and crashed the host.
        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FaultOnCancelSource("src-1");
        var sink = new FakeSinkAdapter("sink-1");

        await using var engine = new RoutingEngine(
            new InMemoryRouteBufferFactory(), diagnostics);

        await engine.RegisterRouteAsync(new RouteDefinition
        {
            RouteId = "route-1",
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new[] { (Core.Adapters.ISinkAdapter)sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = RoutingTestData.DefaultDeliveryPolicy(),
        }, CancellationToken.None);

        await engine.StartRouteAsync("route-1", CancellationToken.None);
        engine.GetRouteState("route-1").Should().Be(RouteState.Running);

        // Must NOT throw, even though the worker faults to Failed mid-stop.
        var stop = async () => await engine.StopRouteAsync("route-1", CancellationToken.None);
        await stop.Should().NotThrowAsync();

        engine.GetRouteState("route-1").Should().Be(RouteState.Stopped);
        diagnostics.Events.Should().Contain(e => e.Contains("->Failed"),
            "the worker faulted during shutdown (this is the regression scenario)");
    }

    [Fact]
    [Trait("Category", "Flaky")]
    public async Task FaultedRoute_SelfHeals_WhenTransientConditionClears()
    {
        // The buffer throws on the FIRST RegisterSinkAsync (worker faults →
        // route Failed), then succeeds. With auto-restart enabled the supervisor
        // restarts the worker on a short backoff and the route recovers to Running
        // with no operator action — the fix for a transient fault (a momentary
        // sink/publish or buffer-I/O error) wedging a route permanently.
        //
        // Flaky-tagged: the restart runs on a timed background task whose scheduling
        // is starved under the full parallel suite. Reliable in isolation
        // (`--filter FullyQualifiedName~FaultedRoute_SelfHeals`); the deterministic
        // mechanism/off-switch is pinned by FaultedRoute_StaysFailed_WhenAutoRestartDisabled.
        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");

        await using var engine = new RoutingEngine(
            new TransientRegisterFaultBufferFactory(failCount: 1),
            diagnostics,
            autoRestartFaultedRoutes: true,
            routeRestartBaseDelay: TimeSpan.FromMilliseconds(30),
            routeRestartMaxDelay: TimeSpan.FromMilliseconds(100));

        await engine.RegisterRouteAsync(new RouteDefinition
        {
            RouteId = "route-1",
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new[] { (Core.Adapters.ISinkAdapter)sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = RoutingTestData.DefaultDeliveryPolicy(),
        }, CancellationToken.None);

        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // First worker start faults to Failed; the self-heal supervisor then
        // restarts it and, the transient condition cleared, it reaches Running.
        var recovered = await WaitForAsync(
            () => engine.GetRouteState("route-1") == RouteState.Running,
            TimeSpan.FromSeconds(10));

        recovered.Should().BeTrue(
            "the self-heal supervisor must restart a faulted route once the transient "
            + $"condition clears. Observed state: {engine.GetRouteState("route-1")}.");
        diagnostics.Events.Should().Contain(e => e.Contains("->Failed"),
            "the route must have faulted before it self-healed");
    }

    [Fact]
    public async Task FaultedRoute_StaysFailed_WhenAutoRestartDisabled()
    {
        // Deterministic counter-test: with the supervisor OFF a faulted route is
        // NOT restarted — preserves the pre-fix behaviour for callers that opt out.
        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");

        await using var engine = new RoutingEngine(
            new TransientRegisterFaultBufferFactory(failCount: 1),
            diagnostics,
            autoRestartFaultedRoutes: false);

        await engine.RegisterRouteAsync(new RouteDefinition
        {
            RouteId = "route-1",
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new[] { (Core.Adapters.ISinkAdapter)sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = RoutingTestData.DefaultDeliveryPolicy(),
        }, CancellationToken.None);

        await engine.StartRouteAsync("route-1", CancellationToken.None);

        var failed = await WaitForAsync(
            () => engine.GetRouteState("route-1") == RouteState.Failed,
            TimeSpan.FromSeconds(5));
        failed.Should().BeTrue();

        // Give a (disabled) supervisor ample time to NOT act, then confirm the
        // route was left dead and the mechanism recovers it on an explicit restart.
        await Task.Delay(200);
        engine.GetRouteState("route-1").Should().Be(RouteState.Failed,
            "with auto-restart disabled a faulted route must not self-heal");

        await engine.TryRestartFaultedRouteAsync("route-1");
        engine.GetRouteState("route-1").Should().Be(RouteState.Running,
            "an explicit restart of a transiently-faulted route must bring it back to Running");
    }

    /// <summary>
    /// A source intake whose reader turns the shutdown cancellation into a
    /// NON-cancellation fault (InvalidOperationException). The route worker's
    /// intake pump reads the source directly (unlike sink loops, source faults
    /// DO fault the route), so this drives Stopping → Failed during
    /// StopRouteAsync — reproducing the "faulted during shutdown" race.
    /// </summary>
    private sealed class FaultOnCancelSource : ISourceIntake
    {
        private readonly Channel<CanonicalDataPoint> _channel =
            Channel.CreateUnbounded<CanonicalDataPoint>();

        public FaultOnCancelSource(string id)
        {
            SourceInstanceId = id;
            Reader = new FaultOnCancelReader(_channel.Reader);
        }

        public string SourceInstanceId { get; }
        public ChannelReader<CanonicalDataPoint> Reader { get; }
    }

    private sealed class FaultOnCancelReader : ChannelReader<CanonicalDataPoint>
    {
        private readonly ChannelReader<CanonicalDataPoint> _inner;
        public FaultOnCancelReader(ChannelReader<CanonicalDataPoint> inner) => _inner = inner;

        public override bool TryRead(
            [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out CanonicalDataPoint item)
            => _inner.TryRead(out item);

        public override async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("simulated source fault during shutdown");
            }
        }

        public override async ValueTask<CanonicalDataPoint> ReadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("simulated source fault during shutdown");
            }
        }
    }

    /// <summary>
    /// Buffer factory whose buffer throws on the first
    /// <see cref="IMessageBuffer.RegisterSinkAsync"/> call — RouteWorker
    /// hits this before scheduling sink loops or running the intake pump,
    /// so it isolates the "worker dies before doing useful work" failure
    /// mode that motivated Bug 2.
    /// </summary>
    private sealed class ThrowingBufferFactory : IRouteBufferFactory
    {
        private readonly bool _throwOnRegisterSink;
        public ThrowingBufferFactory(bool throwOnRegisterSink)
        {
            _throwOnRegisterSink = throwOnRegisterSink;
        }

        public Task<IMessageBuffer> CreateAsync(
            string routeId,
            BufferPolicy policy,
            CancellationToken cancellationToken)
            => Task.FromResult<IMessageBuffer>(
                new ThrowingBuffer(routeId, _throwOnRegisterSink));
    }

    private sealed class ThrowingBuffer : IMessageBuffer
    {
        private readonly bool _throwOnRegisterSink;

        public ThrowingBuffer(string bufferId, bool throwOnRegisterSink)
        {
            BufferId = bufferId;
            _throwOnRegisterSink = throwOnRegisterSink;
        }

        public string BufferId { get; }

        public ValueTask EnqueueAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask<BufferBatch> DequeueBatchAsync(string sinkId, int maxCount, CancellationToken cancellationToken)
            => new(BufferBatch.Empty);

        public ValueTask AckAsync(string sinkId, long upToSequence, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken)
        {
            if (_throwOnRegisterSink)
            {
                throw new InvalidOperationException(
                    $"simulated buffer fault on RegisterSinkAsync for '{sinkId}'");
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask<BufferStats> GetStatsAsync()
            => new(new BufferStats
            {
                CurrentDepth = 0,
                TotalEnqueued = 0,
                TotalDrained = 0,
                TotalDropped = 0,
                RegisteredSinks = 0,
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Buffer factory that hands out ONE shared buffer whose
    /// <see cref="IMessageBuffer.RegisterSinkAsync"/> throws the first
    /// <c>failCount</c> times then succeeds. Sharing the instance across
    /// <see cref="CreateAsync"/> keeps the fault count intact across a restart
    /// (the engine re-uses the route's buffer on restart).
    /// </summary>
    private sealed class TransientRegisterFaultBufferFactory : IRouteBufferFactory
    {
        private readonly TransientRegisterFaultBuffer _buffer;
        public TransientRegisterFaultBufferFactory(int failCount)
            => _buffer = new TransientRegisterFaultBuffer(failCount);

        public Task<IMessageBuffer> CreateAsync(
            string routeId, BufferPolicy policy, CancellationToken cancellationToken)
            => Task.FromResult<IMessageBuffer>(_buffer);
    }

    private sealed class TransientRegisterFaultBuffer : IMessageBuffer
    {
        private int _failsRemaining;
        public TransientRegisterFaultBuffer(int failCount)
        {
            _failsRemaining = failCount;
            BufferId = "transient-fault";
        }

        public string BufferId { get; }

        public ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _failsRemaining) >= 0)
            {
                throw new InvalidOperationException(
                    $"simulated transient buffer fault on RegisterSinkAsync for '{sinkId}'");
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask EnqueueAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask<BufferBatch> DequeueBatchAsync(string sinkId, int maxCount, CancellationToken cancellationToken)
            => new(BufferBatch.Empty);

        public ValueTask AckAsync(string sinkId, long upToSequence, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask<BufferStats> GetStatsAsync()
            => new(new BufferStats
            {
                CurrentDepth = 0,
                TotalEnqueued = 0,
                TotalDrained = 0,
                TotalDropped = 0,
                RegisteredSinks = 0,
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
