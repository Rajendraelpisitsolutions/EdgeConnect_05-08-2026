// ============================================================================
// File: Buffer/CompressionBenchmarks.cs
// Reference: PHASE1_EXECUTION_PLAN.md C2a
//   - Compression_Ratio:      ≥ 5x on realistic CNC payloads
//   - Compression_Throughput: ≥ 200 MB/sec compressed write
// ============================================================================

using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Benchmarks.Buffer;

[MemoryDiagnoser]
public class CompressionBenchmarks
{
    private byte[] _payload = null!;
    private byte[] _compressed = null!;

    [Params(500, 2000)]
    public int PointCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var ms = new MemoryStream();
        var rng = new Random(42);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < PointCount; i++)
        {
            var p = new CanonicalDataPointBuilder()
                .WithGateway("GW-001")
                .WithSource("focas-jyoti17", "focas2")
                .WithDevice("Jyoti17")
                .WithTag("spindle.speed", "Spindle/Speed")
                .WithValue(3000.0 + (rng.NextDouble() - 0.5) * 0.5, CanonicalValueType.Double)
                .WithGoodQuality(t0.AddMilliseconds(i * 100))
                .WithSequence(i)
                .Build();
            var b = BinaryWriterFormat.Instance.Serialize(p);
            ms.Write(b, 0, b.Length);
        }
        _payload = ms.ToArray();
        _compressed = CompressionCodec.Compress(_payload);
    }

    [Benchmark]
    public byte[] Compress() => CompressionCodec.Compress(_payload);

    [Benchmark]
    public byte[] Decompress() => CompressionCodec.Decompress(_compressed);

    /// <summary>
    /// Reports compression ratio. Not a perf benchmark; the report includes
    /// it so the gate-review numbers are visible from a single run.
    /// </summary>
    [Benchmark]
    public double RatioObserved()
    {
        var c = CompressionCodec.Compress(_payload);
        return (double)_payload.Length / c.Length;
    }
}
