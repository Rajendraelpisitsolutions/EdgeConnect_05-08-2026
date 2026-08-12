// ============================================================================
// File: Buffer/SerializerBenchmarks.cs
// Reference: PHASE1_EXECUTION_PLAN.md C2a — Serializer_Roundtrip ≥ 500k pts/sec
// ============================================================================

using System;
using BenchmarkDotNet.Attributes;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Benchmarks.Buffer;

[MemoryDiagnoser]
public class SerializerBenchmarks
{
    private CanonicalDataPoint _point = null!;
    private byte[] _binaryBytes = null!;
    private byte[] _msgPackBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _point = new CanonicalDataPointBuilder()
            .WithGateway("GW-001")
            .WithSource("focas-jyoti17", "focas2")
            .WithDevice("Jyoti17", "Jyoti 17 CNC")
            .WithTag("spindle.speed", "Spindle/Speed")
            .WithValue(3500.0, CanonicalValueType.Double)
            .WithUnit("rpm")
            .WithGoodQuality(new DateTime(2026, 4, 7, 12, 30, 0, DateTimeKind.Utc))
            .WithSequence(42)
            .Build();

        _binaryBytes = BinaryWriterFormat.Instance.Serialize(_point);
        _msgPackBytes = MessagePackFormat.Instance.Serialize(_point);
    }

    [Benchmark(Baseline = true)]
    public CanonicalDataPoint Binary_RoundTrip()
    {
        var bytes = BinaryWriterFormat.Instance.Serialize(_point);
        return BinaryWriterFormat.Instance.Deserialize(bytes);
    }

    [Benchmark]
    public CanonicalDataPoint MessagePack_RoundTrip()
    {
        var bytes = MessagePackFormat.Instance.Serialize(_point);
        return MessagePackFormat.Instance.Deserialize(bytes);
    }

    [Benchmark]
    public byte[] Binary_Serialize_Only() => BinaryWriterFormat.Instance.Serialize(_point);

    [Benchmark]
    public byte[] MessagePack_Serialize_Only() => MessagePackFormat.Instance.Serialize(_point);

    [Benchmark]
    public CanonicalDataPoint Binary_Deserialize_Only() => BinaryWriterFormat.Instance.Deserialize(_binaryBytes);

    [Benchmark]
    public CanonicalDataPoint MessagePack_Deserialize_Only() => MessagePackFormat.Instance.Deserialize(_msgPackBytes);
}
