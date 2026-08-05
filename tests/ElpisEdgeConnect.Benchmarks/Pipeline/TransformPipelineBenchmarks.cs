// ============================================================================
// File: Pipeline/TransformPipelineBenchmarks.cs
// Covers: C1 benchmark from PHASE1_EXECUTION_PLAN.md §D4
//   - TransformPipeline_4Steps_SingleThread target: 10k points/sec
// ============================================================================

using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Pipeline;

namespace ElpisEdgeConnect.Benchmarks.Pipeline;

/// <summary>
/// Measures the cost of pushing a batch through the transform pipeline
/// with four steps stacked. Phase 1 target is 10 k points/sec; the
/// single-thread floor here is a sanity check.
/// </summary>
[MemoryDiagnoser]
[BenchmarkDotNet.Attributes.Config(typeof(InProcessConfig))]
public class TransformPipelineBenchmarks
{
    private const int BatchSize = 100;
    private List<CanonicalDataPoint> _batch = null!;
    private TransformPipeline _pipeline = null!;
    private TransformContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        var factory = new CanonicalDataPointFactory(
            gatewayId: "GW-BENCH",
            sourceInstanceId: "src-bench",
            protocolName: "bench",
            deviceId: "dev-bench");
        var now = DateTime.UtcNow;
        _batch = new List<CanonicalDataPoint>(BatchSize);
        for (var i = 0; i < BatchSize; i++)
        {
            _batch.Add(factory.CreatePoint(
                tagName: "tag" + (i % 8),
                tagPath: "path/tag" + (i % 8),
                value: (double)i,
                valueType: CanonicalValueType.Double,
                quality: DataQuality.Good,
                deviceTimestamp: now,
                gatewayTimestamp: now));
        }

        // Build a 4-step pipeline of identity transforms. The point of this
        // benchmark is the pipeline machinery cost, not any specific step.
        _pipeline = new TransformPipeline(new ITransformStep[]
        {
            new IdentityStep("step-1"),
            new IdentityStep("step-2"),
            new IdentityStep("step-3"),
            new IdentityStep("step-4"),
        });

        _context = new TransformContext
        {
            RouteId = "route-bench",
            GatewayId = "GW-BENCH",
            UtcNow = now,
            Metrics = NullTransformMetricsRecorder.Instance,
        };
    }

    /// <summary>
    /// Push one 100-point batch through the 4-step pipeline.
    /// 10 k pts/sec target → one batch must complete in ≤ 10 ms.
    /// </summary>
    [Benchmark]
    public int FourStep_100PointBatch()
    {
        var result = _pipeline.Execute(_batch, _context);
        return result.Count;
    }

    private sealed class IdentityStep : ITransformStep
    {
        public IdentityStep(string name) { Name = name; }
        public string Name { get; }
        public TransformStepKind Kind => TransformStepKind.Filter;

        public void Apply(
            IReadOnlyList<CanonicalDataPoint> input,
            List<CanonicalDataPoint> output,
            TransformContext context)
        {
            foreach (var p in input)
            {
                output.Add(p);
            }
        }
    }
}
