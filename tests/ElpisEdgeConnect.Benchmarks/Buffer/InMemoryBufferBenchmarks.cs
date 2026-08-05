// ============================================================================
// File: Buffer/InMemoryBufferBenchmarks.cs
// Reference: PHASE1_EXECUTION_PLAN.md C2a benchmark targets:
//   - InMemoryBuffer_Enqueue:        ≥ 100k pts/sec single-threaded
//   - InMemoryBuffer_EnqueueDequeue: ≥ 100k pts/sec round-trip
//   - Zero allocations on steady-state enqueue hot path
// ============================================================================

using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Benchmarks.Buffer;

[MemoryDiagnoser]
public class InMemoryBufferBenchmarks
{
    private const int BatchSize = 100;
    private const int Capacity = 65_536;

    private InMemoryBuffer _buffer = null!;
    private CanonicalDataPoint[] _batch = null!;

    [GlobalSetup]
    public void Setup()
    {
        var policy = new BufferPolicy
        {
            Mode = BufferMode.InMemory,
            MaxDepth = Capacity,
            DropPolicy = DropPolicy.DropOldest,
        };
        _buffer = new InMemoryBuffer("bench", policy);
        _buffer.RegisterSinkAsync("sink-1", default).AsTask().GetAwaiter().GetResult();

        _batch = new CanonicalDataPoint[BatchSize];
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < BatchSize; i++)
        {
            _batch[i] = new CanonicalDataPointBuilder()
                .WithGateway("GW")
                .WithSource("s", "mock")
                .WithDevice("d")
                .WithTag("t", "T/1")
                .WithValue((double)i, CanonicalValueType.Double)
                .WithGoodQuality(t0)
                .WithSequence(i)
                .Build();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _buffer.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Benchmark]
    public async ValueTask Enqueue_DropOldest_NoConsumer()
    {
        await _buffer.EnqueueAsync(_batch, default);
    }

    [Benchmark]
    public async ValueTask EnqueueDequeueAck_RoundTrip()
    {
        await _buffer.EnqueueAsync(_batch, default);
        var batch = await _buffer.DequeueBatchAsync("sink-1", BatchSize, default);
        if (!batch.IsEmpty)
        {
            await _buffer.AckAsync("sink-1", batch.LastSequence, default);
        }
    }
}
