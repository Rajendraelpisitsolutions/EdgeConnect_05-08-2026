// ============================================================================
// File: IntegrationTestCollection.cs
// Purpose: xUnit collection definition that serializes the routing- and
//          diagnostics-engine integration tests. These tests drive real
//          RoutingEngine/Task schedulers/buffer background loops and
//          compete for the thread pool. Running them in parallel with
//          each other causes CPU starvation flakes (5+ tests observed
//          flaking under full-suite load during D phase 1 stabilization).
//
//          This collection does NOT disable parallelism globally — unit
//          tests in other collections still run in parallel. Only the
//          integration-heavy classes serialize.
// Reference: docs/phase1-exit/flaky-tests-disposition.md
// Milestone: D — phase 1.
// ============================================================================

using Xunit;

namespace ElpisEdgeConnect.Core.Tests;

/// <summary>
/// Marker collection that forces sequential execution across the routing
/// and diagnostics integration test classes.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RoutingIntegrationCollection
{
    public const string Name = "Routing integration (serialized)";
}
