// ============================================================================
// File: Buffer/SqliteBufferBenchmarks.cs
// Reference: docs/core/buffer-durability.md §9
//
// C2b throughput floors (informal — captured to phase1-baseline.md at gate):
//   SqliteBuffer_Enqueue_Single        ≥ 5  k tx/sec
//   SqliteBuffer_Enqueue_Batch_100     ≥ 100 k pts/sec
//   SqliteBuffer_Enqueue_Batch_1000    ≥ 500 k pts/sec
//   SqliteBuffer_DequeueAck_Roundtrip  ≥ 50 k pts/sec
//   SqliteBuffer_AckOnly               ≥ 10 k acks/sec
//   SqliteBuffer_Reclaim               ≥ 500 k rows/sec
// ============================================================================

using System;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Benchmarks.Buffer;

[MemoryDiagnoser]
public class SqliteBufferBenchmarks
{
    private string _dbPath = null!;
    private SqliteBuffer _buffer = null!;
    private CanonicalDataPoint[] _single = null!;
    private CanonicalDataPoint[] _batch100 = null!;
    private CanonicalDataPoint[] _batch1000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "edgeconnect-c2b-bench");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, $"sqlbuf-bench-{Guid.NewGuid():N}.db");

        var policy = new BufferPolicy
        {
            Mode = BufferMode.StoreAndForward,
            MaxDepth = 1_000_000,
            DropPolicy = DropPolicy.Block,
            ReclaimInterval = TimeSpan.FromMinutes(10),
            MaxBatchSize = 10_000,
        };
        _buffer = SqliteBuffer.OpenAsync("bench", _dbPath, policy).GetAwaiter().GetResult();
        _buffer.RegisterSinkAsync("sink-1", default).AsTask().GetAwaiter().GetResult();

        _single = MakeBatch(1);
        _batch100 = MakeBatch(100);
        _batch1000 = MakeBatch(1000);
    }

    private static CanonicalDataPoint[] MakeBatch(int count)
    {
        var arr = new CanonicalDataPoint[count];
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < count; i++)
        {
            arr[i] = new CanonicalDataPointBuilder()
                .WithGateway("GW")
                .WithSource("s", "mock")
                .WithDevice("d")
                .WithTag("t", "T/1")
                .WithValue((double)i, CanonicalValueType.Double)
                .WithGoodQuality(t0)
                .WithSequence(i)
                .Build();
        }
        return arr;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try { _buffer.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
    }

    [Benchmark]
    public async ValueTask Enqueue_Single()
    {
        await _buffer.EnqueueAsync(_single, default);
    }

    [Benchmark]
    public async ValueTask Enqueue_Batch_100()
    {
        await _buffer.EnqueueAsync(_batch100, default);
    }

    [Benchmark]
    public async ValueTask Enqueue_Batch_1000()
    {
        await _buffer.EnqueueAsync(_batch1000, default);
    }

    [Benchmark]
    public async ValueTask DequeueAck_Roundtrip()
    {
        await _buffer.EnqueueAsync(_batch100, default);
        var batch = await _buffer.DequeueBatchAsync("sink-1", 100, default);
        if (!batch.IsEmpty)
        {
            await _buffer.AckAsync("sink-1", batch.LastSequence, default);
        }
    }
}
