// ============================================================================
// File: InProcessConfig.cs
// Purpose: BenchmarkDotNet config used by the D phase 7 benchmarks that
//          run in-process. The dev box has only the .NET 10 SDK installed;
//          the default BenchmarkDotNet child-process toolchain refuses to
//          launch because the project targets net8.0. Running in-process
//          sidesteps that while still producing stable measurements for
//          consolidation purposes.
// Reference: PHASE1_EXECUTION_PLAN.md §D4, docs/benchmarks/phase1-baseline.md
// Milestone: D — phase 7.
// ============================================================================

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace ElpisEdgeConnect.Benchmarks;

/// <summary>
/// Minimal in-process config: one short job, in-process emit toolchain.
/// Used by the D7 consolidation benchmarks.
/// </summary>
public sealed class InProcessConfig : ManualConfig
{
    /// <summary>Construct the in-process config.</summary>
    public InProcessConfig()
    {
        AddJob(Job.ShortRun
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithWarmupCount(2)
            .WithIterationCount(3)
            .WithLaunchCount(1));
    }
}
