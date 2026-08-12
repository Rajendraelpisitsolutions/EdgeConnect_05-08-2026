// ============================================================================
// File: Diagnostics/DiagnosticsCollectorBenchmarks.cs
// Covers: C4 benchmark from PHASE1_EXECUTION_PLAN.md §D4
//   - DiagnosticsCollector_CounterUpdates target: 1M updates/sec concurrent
// ============================================================================

using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Benchmarks.Diagnostics;

/// <summary>
/// Measures the collector's hot-path write cost. The 1M updates/sec
/// target is the steady-state cost of OnBackpressureDropped, which is
/// one of the most-called hot-path methods.
/// </summary>
[MemoryDiagnoser]
[BenchmarkDotNet.Attributes.Config(typeof(InProcessConfig))]
public class DiagnosticsCollectorBenchmarks
{
    private RuntimeDiagnosticsCollector _collector = null!;
    private BackpressureDroppedEvent _bpEvt = null!;
    private RouteStateChangedEvent _stateEvt = null!;

    [GlobalSetup]
    public void Setup()
    {
        _collector = new RuntimeDiagnosticsCollector();
        _collector.EnsureRoute("route-bench");
        var now = DateTime.UtcNow;
        _bpEvt = new BackpressureDroppedEvent
        {
            RouteId = "route-bench",
            DroppedCount = 1,
            Reason = "bench",
            ObservedAtUtc = now,
        };
        _stateEvt = new RouteStateChangedEvent
        {
            RouteId = "route-bench",
            From = RouteState.Configured,
            To = RouteState.Running,
            ObservedAtUtc = now,
        };
    }

    [Benchmark]
    public void OnBackpressureDropped_Single() => _collector.OnBackpressureDropped(_bpEvt);

    [Benchmark]
    public void OnRouteStateChanged_Single() => _collector.OnRouteStateChanged(_stateEvt);

    [Benchmark]
    public void RecordStep_Single() =>
        _collector.RecordStep("route-bench", "filter", 100, 100, 0, TimeSpan.Zero);
}
