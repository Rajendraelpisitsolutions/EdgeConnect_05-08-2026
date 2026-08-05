// ============================================================================
// File: Model/CanonicalDataPointBenchmarks.cs
// Covers: A1 benchmarks from PHASE1_EXECUTION_PLAN.md
//   - CanonicalDataPoint_Construction target: 2M points/sec via builder
// Memory diagnostics enabled on every benchmark so regressions are visible.
// ============================================================================

using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Benchmarks.Model;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class CanonicalDataPointBenchmarks
{
    private CanonicalDataPointFactory _factory = null!;
    private DateTime _now;

    [GlobalSetup]
    public void Setup()
    {
        _factory = new CanonicalDataPointFactory(
            gatewayId: "GW-BENCH-001",
            sourceInstanceId: "focas-bench",
            protocolName: "focas2",
            deviceId: "BenchDevice",
            deviceName: "Bench Device");
        _now = DateTime.UtcNow;
    }

    /// <summary>
    /// Direct record construction — the absolute floor for allocation cost.
    /// </summary>
    [Benchmark(Baseline = true)]
    public CanonicalDataPoint DirectRecordConstruction()
    {
        return new CanonicalDataPoint
        {
            GatewayId = "GW-BENCH-001",
            SourceInstanceId = "focas-bench",
            ProtocolName = "focas2",
            DeviceId = "BenchDevice",
            DeviceName = "Bench Device",
            TagName = "spindle.speed",
            TagPath = "Spindle/Speed",
            OriginalTagName = null,
            Value = 3500.0,
            ValueType = CanonicalValueType.Double,
            Unit = "rpm",
            Quality = DataQuality.Good,
            QualityReason = null,
            DeviceTimestamp = _now,
            GatewayTimestamp = _now,
            Metadata = null,
            SequenceNumber = 1,
        };
    }

    /// <summary>
    /// Factory fast path via CreatePoint — the expected production path for
    /// adapters in tight loops.
    /// </summary>
    [Benchmark]
    public CanonicalDataPoint FactoryCreatePoint()
    {
        return _factory.CreatePoint(
            tagName: "spindle.speed",
            tagPath: "Spindle/Speed",
            value: 3500.0,
            valueType: CanonicalValueType.Double,
            quality: DataQuality.Good,
            deviceTimestamp: _now,
            gatewayTimestamp: _now,
            unit: "rpm");
    }

    /// <summary>
    /// Builder path — more ergonomic but with a builder allocation per point.
    /// Target: 2M points/sec per Phase 1 plan.
    /// </summary>
    [Benchmark]
    public CanonicalDataPoint BuilderPath()
    {
        return _factory.NewBuilder()
            .WithTag("spindle.speed", "Spindle/Speed")
            .WithValue(3500.0, CanonicalValueType.Double)
            .WithUnit("rpm")
            .WithGoodQuality(_now)
            .Build();
    }

    /// <summary>
    /// Sequence generation in isolation — should be under 20ns with Interlocked.
    /// </summary>
    [Benchmark]
    public long NextSequenceOnly()
    {
        return _factory.NextSequence();
    }
}
