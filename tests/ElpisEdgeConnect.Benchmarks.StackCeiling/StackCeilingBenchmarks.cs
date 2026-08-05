// ============================================================================
// File: StackCeilingBenchmarks.cs
// Purpose: The 4 stack-ceiling phase benchmarks per v2.1 §6 Q9 locked
//          sequence. Each [Benchmark] method calls into StackCeilingHarness
//          with phase-specific parameters; BenchmarkDotNet's monitoring run
//          strategy produces sustained throughput numbers over the
//          configured duration.
//
// Phase sequence (LOCKED, run in this exact order):
//   1. Warmup_15K_5min       — 5-min warmup at 15K items (avoids cold-JIT
//                              noise as "steady-state")
//   2. Sustained_30K_30min   — 30-min sustained at 30K items
//                              (PRIMARY TARGET; must pass 7 gates)
//   3. Stretch_50K_30min     — 30-min stretch attempt at 50K items
//                              (informational unless gates green)
//   4. Exploratory_75K_30min — 30-min exploratory ceiling at 75K items
//                              (informational ONLY; NEVER customer-facing)
//
// Endpoint configured via environment variable
//   OPCUA_BENCHMARK_ENDPOINT (default: opc.tcp://localhost:62541)
// so the same code runs against UA Sample Server (lab) and any other
// reference server (calibration).
//
// LOCKED: results from this harness ONLY update phase2-multi-protocol-
//         baseline.md §1-§5 when produced on the dedicated benchmark host
//         (v2.1 §6 Q10 governance). PR-smoke / shared-runner runs are
//         informational.
// ============================================================================

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace ElpisEdgeConnect.Benchmarks.StackCeiling;

/// <summary>
/// The 4 locked stack-ceiling phases. Run in order via the BenchmarkDotNet
/// monitoring strategy (1 invocation per iteration, no warmup-of-warmup).
/// </summary>
[SimpleJob(RunStrategy.Monitoring, iterationCount: 1, warmupCount: 0, invocationCount: 1)]
[MemoryDiagnoser]
public class StackCeilingBenchmarks
{
    /// <summary>
    /// OPC UA endpoint to subscribe against. Defaults to the OPC Foundation
    /// UA Reference Server's standard port (62541) which is what the
    /// instructions in README.md launch.
    /// </summary>
    private static string Endpoint =>
        System.Environment.GetEnvironmentVariable("OPCUA_BENCHMARK_ENDPOINT")
        ?? "opc.tcp://localhost:62541";

    /// <summary>15K monitored items × 5 minutes — JIT + allocator warmup.</summary>
    [Benchmark]
    public async System.Threading.Tasks.Task<StackCeilingHarness.PhaseResult> Warmup_15K_5min()
    {
        var harness = new StackCeilingHarness(Endpoint, 15_000, System.TimeSpan.FromMinutes(5));
        return await harness.RunAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 30K monitored items × 30 minutes — PRIMARY TARGET. All 7 gates from
    /// workload-profiles.md §2 must hold simultaneously for this run to
    /// count toward the locked baseline.
    /// </summary>
    [Benchmark]
    public async System.Threading.Tasks.Task<StackCeilingHarness.PhaseResult> Sustained_30K_30min()
    {
        var harness = new StackCeilingHarness(Endpoint, 30_000, System.TimeSpan.FromMinutes(30));
        return await harness.RunAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 50K monitored items × 30 minutes — stretch target attempt.
    /// Informational unless all 7 gates green; in which case the measured
    /// number becomes the locked stretch baseline.
    /// </summary>
    [Benchmark]
    public async System.Threading.Tasks.Task<StackCeilingHarness.PhaseResult> Stretch_50K_30min()
    {
        var harness = new StackCeilingHarness(Endpoint, 50_000, System.TimeSpan.FromMinutes(30));
        return await harness.RunAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 75K monitored items × 30 minutes — exploratory ceiling. Documents
    /// where the OPC Foundation .NET stack actually falls over on
    /// pilot-class hardware. NEVER a customer-facing claim; goes into
    /// phase2-multi-protocol-baseline.md §6 "Informational" only.
    /// </summary>
    [Benchmark]
    public async System.Threading.Tasks.Task<StackCeilingHarness.PhaseResult> Exploratory_75K_30min()
    {
        var harness = new StackCeilingHarness(Endpoint, 75_000, System.TimeSpan.FromMinutes(30));
        return await harness.RunAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
    }
}
