// ============================================================================
// File: Program.cs
// Purpose: Entry point for BenchmarkDotNet runner.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D4
// ============================================================================

using BenchmarkDotNet.Running;

namespace ElpisEdgeConnect.Benchmarks;

public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
