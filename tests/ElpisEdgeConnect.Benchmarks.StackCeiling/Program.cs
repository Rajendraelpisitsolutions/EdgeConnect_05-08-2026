// ============================================================================
// File: Program.cs
// Purpose: BenchmarkDotNet entry point for the stack-ceiling harness.
//          Designed to be invoked once on the dedicated benchmark host
//          after configuration:
//            1. Launch UA Sample Server (see README.md §1)
//            2. Set OPCUA_BENCHMARK_ENDPOINT if non-default
//            3. dotnet run -c Release -- --filter '*StackCeiling*'
// ============================================================================

using BenchmarkDotNet.Running;
using ElpisEdgeConnect.Benchmarks.StackCeiling;

BenchmarkRunner.Run<StackCeilingBenchmarks>(args: args);
