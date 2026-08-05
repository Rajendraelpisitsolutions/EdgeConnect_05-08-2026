// ============================================================================
// File: Routing/RoutingEngineReplaySlice1Tests.cs
// Covers: K1.3 slice 1 — RoutingEngine.RegisterRouteAsync replay-route wiring:
//         capability-gated detection of a replay-aware sink, activation at the
//         registration commit boundary, and the fail-closed guards (single-sink,
//         store-and-forward, non-capable buffer, no-automatic-downgrade), plus
//         activation-failure-does-not-publish and buffer-disposal-on-failure /
//         ownership-transfer-on-success.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.2-amendment.md
//            §B1 / §B2 (slice-1 tests 1, 5, 6, 7, 9, 10, 11, 12, 13).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class RoutingEngineReplaySlice1Tests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BufferPolicy SfPolicy() => new()
    {
        Mode = BufferMode.StoreAndForward,
        MaxDepth = 64,
        DropPolicy = DropPolicy.Block,
        ReclaimInterval = TimeSpan.FromMilliseconds(50),
    };

    private static BufferPolicy InMemoryPolicy() => new()
    {
        Mode = BufferMode.InMemory,
        MaxDepth = 1024,
        DropPolicy = DropPolicy.Block,
    };

    private static RouteDefinition Def(string routeId, BufferPolicy policy, params ISinkAdapter[] sinks) => new()
    {
        RouteId = routeId,
        GatewayId = RoutingTestData.GatewayId,
        Source = new FakeSourceIntake("src-1"),
        Sinks = sinks,
        Filter = TagFilter.AcceptAll,
        BufferPolicy = policy,
        Delivery = RoutingTestData.DefaultDeliveryPolicy(),
    };

    // ---- happy path: activation at the commit boundary ----------------------

    [Fact]
    public async Task Register_ReplayRoute_Activates_Store_At_Commit()
    {
        var dataPath = NewDataPath();
        try
        {
            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            await engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp")), Ct);

            engine.RegisteredRouteIds.Should().Contain("route-a");
            var db = BufferPath(dataPath, "route-a");
            ReadMeta(db, "replay_state_tracking").Should().Be("enabled");
            ReadMeta(db, "replay_sink_id").Should().Be("sp");
        }
        finally { TryDeleteDir(dataPath); }
    }

    // ---- fail-closed validation ---------------------------------------------

    [Fact]
    public async Task Register_ReplayRoute_NonCapableBuffer_Fails()
    {
        // The in-memory factory returns an InMemoryBuffer (not IReplayRouteBuffer) even for an SF policy.
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        var act = () => engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp")), Ct);

        (await act.Should().ThrowAsync<ReplayRouteConfigurationException>()).Which.Code
            .Should().Be(CoreErrors.ReplayRouteBufferNotCapable);
        engine.RegisteredRouteIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Register_ReplayRoute_With_A_Second_Sink_Fails()
    {
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        var act = () => engine.RegisterRouteAsync(
            Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp"), new FakeSinkAdapter("ordinary")), Ct);

        (await act.Should().ThrowAsync<ReplayRouteConfigurationException>()).Which.Code
            .Should().Be(CoreErrors.ReplayRouteRequiresSingleSink);
    }

    [Fact]
    public async Task Register_ReplayRoute_NonStoreAndForward_Fails()
    {
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        var act = () => engine.RegisterRouteAsync(Def("route-a", InMemoryPolicy(), new FakeReplayAwareSink("sp")), Ct);

        (await act.Should().ThrowAsync<ReplayRouteConfigurationException>()).Which.Code
            .Should().Be(CoreErrors.ReplayRouteRequiresStoreAndForward);
    }

    [Fact]
    public async Task Register_OrdinaryRoute_On_Enabled_Store_Fails_Downgrade()
    {
        var dataPath = NewDataPath();
        try
        {
            var db = BufferPath(dataPath, "route-a");
            await using (var pre = await SqliteBuffer.OpenAsync("route-a", db, SfPolicy()))
            {
                await ((IReplayRouteBuffer)pre).ActivateReplayAsync("route-a", "sp", Ct);
            }

            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            // Ordinary sink over an already replay-enabled store — must fail closed, not downgrade.
            var act = () => engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeSinkAdapter("ordinary")), Ct);

            (await act.Should().ThrowAsync<ReplayRouteConfigurationException>()).Which.Code
                .Should().Be(CoreErrors.ReplayRouteDowngradeNotAllowed);
            engine.RegisteredRouteIds.Should().BeEmpty();
        }
        finally { TryDeleteDir(dataPath); }
    }

    [Fact]
    public async Task Register_ReplayRoute_ActivationFailure_Does_Not_Publish_And_Leaves_Store_Disabled()
    {
        var dataPath = NewDataPath();
        try
        {
            var db = BufferPath(dataPath, "route-a");
            // Pre-seed an UN-drained backlog so the drained-store activation check fails.
            await using (var pre = await SqliteBuffer.OpenAsync("route-a", db, SfPolicy()))
            {
                await pre.RegisterSinkAsync("some-sink", Ct);
                await pre.EnqueueAsync(new[] { Point(0) }, Ct);
            }

            await using var engine = new RoutingEngine(new DefaultRouteBufferFactory(dataPath));

            var act = () => engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp")), Ct);

            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreReplayActivationBacklogPending);
            engine.RegisteredRouteIds.Should().BeEmpty();          // route not published
            ReadMeta(db, "replay_state_tracking").Should().NotBe("enabled"); // store left disabled
        }
        finally { TryDeleteDir(dataPath); }
    }

    // ---- buffer ownership on failure vs success -----------------------------

    [Fact]
    public async Task Register_Failure_Disposes_The_Created_Buffer()
    {
        var tracking = new DisposeTrackingBuffer("route-a");
        await using var engine = new RoutingEngine(new SingleBufferFactory(tracking));

        // Non-capable buffer + replay-aware sink → BufferNotReplayCapable, after the buffer was created.
        var act = () => engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp")), Ct);
        await act.Should().ThrowAsync<ReplayRouteConfigurationException>();

        tracking.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Register_Success_Transfers_Buffer_Ownership_To_The_Route()
    {
        var tracking = new DisposeTrackingBuffer("route-a");
        var engine = new RoutingEngine(new SingleBufferFactory(tracking));

        await engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeSinkAdapter("ordinary")), Ct);
        tracking.DisposeCount.Should().Be(0); // the Route now owns it

        await engine.DisposeAsync();
        tracking.DisposeCount.Should().Be(1); // disposed exactly once, via the route
    }

    [Fact]
    public async Task Register_Concurrent_SameRouteId_Activates_Exactly_Once()
    {
        var factory = new GatedCountingReplayFactory();
        await using var engine = new RoutingEngine(factory);

        // t1 reserves the id synchronously and parks at the factory gate; t2's reservation fails
        // BEFORE it can create a buffer or activate — so the one-way activation never races a
        // duplicate-registration loss.
        var t1 = engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp")), Ct);
        var t2 = engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp")), Ct);

        factory.CreateCount.Should().Be(1); // only the reservation winner reached the factory
        factory.Release();

        await t1;
        var ex = await Record.ExceptionAsync(async () => await t2);

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain(CoreErrors.RouteAlreadyRegistered);
        factory.ActivateCount.Should().Be(1);
        engine.RegisteredRouteIds.Should().ContainSingle().Which.Should().Be("route-a");
    }

    [Fact]
    public async Task Register_Failed_Registration_Holds_Reservation_Until_Cleanup_Completes()
    {
        var entered = new ManualResetEventSlim(false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new GatedDisposalFactory(entered, release);
        await using var engine = new RoutingEngine(factory);

        // t1: a replay route over a NON-replay-capable buffer → BufferNotReplayCapable → disposes the
        // buffer, whose DisposeAsync signals "entered" and then blocks. The reservation is still held.
        var t1 = engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp")), Ct);
        entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        // A concurrent same-id registration must be rejected AT THE RESERVATION (RouteAlreadyRegistered),
        // NOT reach the factory — if the reservation had been released before disposal it would instead
        // reach the factory and fail with a capability error.
        var mid = await Record.ExceptionAsync(async () =>
            await engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp")), Ct));
        mid.Should().BeOfType<InvalidOperationException>();
        mid!.Message.Should().Contain(CoreErrors.RouteAlreadyRegistered);
        factory.CreateCount.Should().Be(1); // the second attempt never reached the factory

        // Release the blocked disposal; t1 completes with its capability failure.
        release.SetResult();
        (await Record.ExceptionAsync(async () => await t1)).Should().BeOfType<ReplayRouteConfigurationException>();

        // The reservation is now free — a later attempt gets PAST the reservation to the factory.
        await Record.ExceptionAsync(async () =>
            await engine.RegisterRouteAsync(Def("route-a", SfPolicy(), new FakeReplayAwareSink("sp")), Ct));
        factory.CreateCount.Should().Be(2);
    }

    // ---- helpers ------------------------------------------------------------

    private static CanonicalDataPoint Point(long seq) =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice("dev-1")
            .WithTag("tag", "Spindle/Speed")
            .WithValue((double)seq, CanonicalValueType.Double)
            .WithGoodQuality(BaseUtc.AddSeconds(seq))
            .WithSequence(seq)
            .Build();

    private static string NewDataPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "edgeconnect-k13-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string BufferPath(string dataPath, string routeId) =>
        Path.Combine(dataPath, "buffer", routeId + ".db");

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private static string? ReadMeta(string dbPath, string key)
    {
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>An IRouteBufferFactory that returns one pre-supplied buffer (for ownership/disposal tests).</summary>
    private sealed class SingleBufferFactory : IRouteBufferFactory
    {
        private readonly IMessageBuffer _buffer;
        public SingleBufferFactory(IMessageBuffer buffer) => _buffer = buffer;
        public Task<IMessageBuffer> CreateAsync(string routeId, BufferPolicy policy, CancellationToken cancellationToken)
            => Task.FromResult(_buffer);
    }

    /// <summary>A no-op IMessageBuffer (NOT replay-capable) that counts DisposeAsync calls.</summary>
    private sealed class DisposeTrackingBuffer : IMessageBuffer
    {
        private int _disposeCount;
        public DisposeTrackingBuffer(string bufferId) => BufferId = bufferId;
        public string BufferId { get; }
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        // Slice-1 registration never exercises the data path — only DisposeAsync matters.
        public ValueTask EnqueueAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BufferBatch> DequeueBatchAsync(string sinkId, int maxCount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask AckAsync(string sinkId, long upToSequence, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BufferStats> GetStatsAsync() => throw new NotSupportedException();
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A replay-capable factory whose CreateAsync parks at a gate (counting create + activate),
    /// so a concurrent same-id registration can be observed to be rejected at the reservation
    /// before ever reaching the factory / activating.
    /// </summary>
    private sealed class GatedCountingReplayFactory : IRouteBufferFactory
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _createCount;
        private int _activateCount;

        public int CreateCount => Volatile.Read(ref _createCount);
        public int ActivateCount => Volatile.Read(ref _activateCount);
        public void Release() => _gate.TrySetResult();

        public async Task<IMessageBuffer> CreateAsync(string routeId, BufferPolicy policy, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            await _gate.Task.ConfigureAwait(false);
            return new CountingReplayBuffer(routeId, () => Interlocked.Increment(ref _activateCount));
        }
    }

    private sealed class CountingReplayBuffer : IMessageBuffer, IReplayRouteBuffer
    {
        private readonly Action _onActivate;
        public CountingReplayBuffer(string bufferId, Action onActivate)
        {
            BufferId = bufferId;
            _onActivate = onActivate;
        }

        public string BufferId { get; }

        public bool IsReplayTrackingEnabled => false;

        public ValueTask<ReplayRouteActivation> ActivateReplayAsync(string routeId, string replaySinkId, CancellationToken cancellationToken)
        {
            _onActivate();
            return new ValueTask<ReplayRouteActivation>(
                new ReplayRouteActivation(RouteSchemaGeneration.Create(0), new StubBoundaryProvider(), new StubSessionProvider()));
        }

        public ValueTask<AssignedSequenceRange> AppendTrackedAsync(IReadOnlyList<CanonicalDataPoint> points, RouteSchemaGeneration expectedGeneration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask EnqueueAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BufferBatch> DequeueBatchAsync(string sinkId, int maxCount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask AckAsync(string sinkId, long upToSequence, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BufferStats> GetStatsAsync() => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubBoundaryProvider : IReplayBoundaryProvider
    {
        public ValueTask<ReplayBoundary> CaptureReplayBoundaryAsync(string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubSessionProvider : IReplaySessionStateProvider
    {
        public ValueTask<ReplaySessionStartState> CaptureBirthStateAsync(string routeId, string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ReplaySessionCutoverState> CaptureCutoverAsync(string routeId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Factory that returns NON-replay-capable buffers whose DisposeAsync is gated, for the reservation-held-through-cleanup test.</summary>
    private sealed class GatedDisposalFactory : IRouteBufferFactory
    {
        private readonly ManualResetEventSlim _entered;
        private readonly TaskCompletionSource _release;
        private int _createCount;

        public GatedDisposalFactory(ManualResetEventSlim entered, TaskCompletionSource release)
        {
            _entered = entered;
            _release = release;
        }

        public int CreateCount => Volatile.Read(ref _createCount);

        public Task<IMessageBuffer> CreateAsync(string routeId, BufferPolicy policy, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            return Task.FromResult<IMessageBuffer>(new GatedDisposalBuffer(routeId, _entered, _release));
        }
    }

    /// <summary>A no-op, NON-replay-capable IMessageBuffer whose DisposeAsync signals then blocks on a gate.</summary>
    private sealed class GatedDisposalBuffer : IMessageBuffer
    {
        private readonly ManualResetEventSlim _entered;
        private readonly TaskCompletionSource _release;

        public GatedDisposalBuffer(string bufferId, ManualResetEventSlim entered, TaskCompletionSource release)
        {
            BufferId = bufferId;
            _entered = entered;
            _release = release;
        }

        public string BufferId { get; }

        public ValueTask EnqueueAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BufferBatch> DequeueBatchAsync(string sinkId, int maxCount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask AckAsync(string sinkId, long upToSequence, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BufferStats> GetStatsAsync() => throw new NotSupportedException();

        public async ValueTask DisposeAsync()
        {
            _entered.Set();
            await _release.Task.ConfigureAwait(false);
        }
    }
}
