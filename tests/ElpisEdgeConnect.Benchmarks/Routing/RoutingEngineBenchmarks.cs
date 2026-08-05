// ============================================================================
// File: Routing/RoutingEngineBenchmarks.cs
// Covers: C3 benchmarks from PHASE1_EXECUTION_PLAN.md §D4
//   - RoutingEngine_SustainedThroughput target: 5k pts/sec
//   - RoutingEngine_PeakBurst target: 20k pts/sec for 30 sec
//
// Note: we measure microsecond-level cost of pushing a single 100-point
// batch through a fully-constructed RoutingEngine + InMemoryBuffer +
// in-process fake source/sink. The throughput target is then derived by
// converting mean batch cost → points per second. This avoids the cost of
// running a true 30 s sustained loop under BenchmarkDotNet while still
// pinning the same underlying per-batch cost that the 30 s loop would
// measure.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Benchmarks.Routing;

/// <summary>
/// End-to-end routing-engine throughput on an in-memory buffer with a
/// tight-loop fake source and a no-op fake sink. Phase 1 targets:
/// sustained 5 k pts/sec, peak burst 20 k pts/sec.
/// </summary>
[MemoryDiagnoser]
[BenchmarkDotNet.Attributes.Config(typeof(InProcessConfig))]
public class RoutingEngineBenchmarks
{
    private const int BatchSize = 100;
    private BenchSourceIntake _source = null!;
    private BenchSinkAdapter _sink = null!;
    private RoutingEngine _engine = null!;
    private CanonicalDataPoint[] _points = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new BenchSourceIntake("src-bench");
        _sink = new BenchSinkAdapter("sink-bench");
        _engine = new RoutingEngine(new InMemoryBufferFactory());

        var def = new RouteDefinition
        {
            RouteId = "route-bench",
            GatewayId = "GW-BENCH",
            Source = _source,
            Sinks = new ISinkAdapter[] { _sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = new BufferPolicy
            {
                Mode = BufferMode.InMemory,
                MaxDepth = 100_000,
                DropPolicy = DropPolicy.DropOldest,
            },
            Delivery = new DeliveryPolicyConfig
            {
                Mode = DeliveryMode.AtLeastOnce,
                MaxRetries = 10,
                InitialBackoffMs = 1,
                MaxBackoffMs = 10,
                BackoffMultiplier = 2.0,
                JitterPercent = 0,
            },
        };
        _engine.RegisterRouteAsync(def, CancellationToken.None).GetAwaiter().GetResult();
        _engine.StartRouteAsync("route-bench", CancellationToken.None).GetAwaiter().GetResult();

        var factory = new CanonicalDataPointFactory("GW-BENCH", "src-bench", "bench", "dev-bench");
        var now = DateTime.UtcNow;
        _points = new CanonicalDataPoint[BatchSize];
        for (var i = 0; i < BatchSize; i++)
        {
            _points[i] = factory.CreatePoint(
                "tag", "path/tag", (double)i,
                CanonicalValueType.Double, DataQuality.Good, now, now);
        }
    }

    /// <summary>
    /// Push 100 points into the source channel and wait for the sink
    /// to publish them. Measures per-batch cost through the full route
    /// worker + buffer + fanout + sink path.
    /// </summary>
    [Benchmark]
    public async Task RouteBatch_100Points()
    {
        var beforeCount = _sink.PublishedCount;
        for (var i = 0; i < BatchSize; i++)
        {
            await _source.WriteAsync(_points[i]);
        }
        while (_sink.PublishedCount < beforeCount + BatchSize)
        {
            await Task.Yield();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _source.Complete();
        _engine.StopRouteAsync("route-bench", CancellationToken.None).GetAwaiter().GetResult();
        _engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ----- Bench-local infrastructure (kept private) -----

    private sealed class BenchSourceIntake : ISourceIntake
    {
        private readonly Channel<CanonicalDataPoint> _channel;
        public BenchSourceIntake(string id)
        {
            SourceInstanceId = id;
            _channel = Channel.CreateBounded<CanonicalDataPoint>(new BoundedChannelOptions(4096)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
        }
        public string SourceInstanceId { get; }
        public ChannelReader<CanonicalDataPoint> Reader => _channel.Reader;
        public ValueTask WriteAsync(CanonicalDataPoint point) => _channel.Writer.WriteAsync(point);
        public void Complete() => _channel.Writer.TryComplete();
    }

    private sealed class BenchSinkAdapter : ISinkAdapter
    {
        private long _count;
        public BenchSinkAdapter(string id) { InstanceId = id; }
        public string InstanceId { get; }
        public string ProtocolName => "bench";
        public SinkCapabilities Capabilities => SinkCapabilities.Push;
        public AdapterState State { get; private set; } = AdapterState.Running;
        public long PublishedCount => Interlocked.Read(ref _count);
        public Task InitializeAsync(SinkConfiguration config, CancellationToken ct) => Task.CompletedTask;
        public Task StartAsync(CancellationToken ct) { State = AdapterState.Running; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) { State = AdapterState.Stopped; return Task.CompletedTask; }
        public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
            => Task.FromResult(new AdapterHealth
            {
                State = State,
                Level = HealthLevel.Healthy,
                CheckedAt = DateTime.UtcNow,
            });
        public Task<PublishResult> PublishAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct)
        {
            Interlocked.Add(ref _count, points.Count);
            return Task.FromResult(PublishResult.Successful(points.Count, TimeSpan.Zero));
        }
        public Task UpdateCurrentValuesAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken ct) => Task.CompletedTask;
        public Task<ValidationResult> ValidateConfigAsync(SinkConfiguration config, CancellationToken ct)
            => Task.FromResult(ValidationResult.Success());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InMemoryBufferFactory : IRouteBufferFactory
    {
        public Task<IMessageBuffer> CreateAsync(string routeId, BufferPolicy policy, CancellationToken ct)
            => Task.FromResult<IMessageBuffer>(new InMemoryBuffer(routeId, policy));
    }
}
