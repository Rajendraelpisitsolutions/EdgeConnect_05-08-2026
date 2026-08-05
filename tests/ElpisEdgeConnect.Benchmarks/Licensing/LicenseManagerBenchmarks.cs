// ============================================================================
// File: Licensing/LicenseManagerBenchmarks.cs
// Covers: B3 benchmark from PHASE1_EXECUTION_PLAN.md §D4
//   - LicenseManager_IsModuleEnabled target: < 100 ns
// ============================================================================

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ElpisEdgeConnect.Core.Licensing;

namespace ElpisEdgeConnect.Benchmarks.Licensing;

/// <summary>
/// Measures the cost of the license manager's fast-path module check.
/// This call sits on every route-registration and adapter-lifecycle
/// path, so the target is sub-100 ns.
/// </summary>
[MemoryDiagnoser]
[BenchmarkDotNet.Attributes.Config(typeof(InProcessConfig))]
public class LicenseManagerBenchmarks
{
    private LicenseManager _manager = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Unloaded manager: IsModuleEnabled returns false without
        // consulting a loaded license. The fast-path cost is a single
        // field read + comparison — exactly what we want to measure as
        // the floor. A "loaded" variant can be added later if needed.
        _manager = new LicenseManager();
    }

    [Benchmark]
    public bool IsModuleEnabled_Unloaded() => _manager.IsModuleEnabled("focas2");

    [Benchmark]
    public bool IsFeatureEnabled_Unloaded() => _manager.IsFeatureEnabled("transforms");
}
