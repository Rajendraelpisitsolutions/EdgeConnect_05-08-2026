// ============================================================================
// File: StartupPhase.cs
// Purpose: Locked enumeration of host startup/shutdown phases. Part of the
//          startup ordering CONTRACT — phases are tested explicitly via the
//          IStartupSequenceObserver seam, not asserted as an implementation
//          detail. Adding/removing/reordering values is a blueprint change.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D — startup ordering
// Milestone: D — phase 2.
//
// LOCKED ordering (per the D pre-implementation plan):
//   Startup:
//     1.  ParseEnvironment
//     2.  BuildContainer
//     3.  LoadGatewayIdentity
//     4.  LoadConfiguration
//     5.  LoadLicense
//     6.  ConstructDiagnostics
//     7.  ConstructBufferFactory
//     8.  RegisterRoutes
//     9.  StartSourceSupervisor    (deferred to D phase 4 — placeholder phase
//                                   recorded so the contract is stable)
//    10.  StartRoutingEngine
//    11.  MarkReady
//    12.  StartMetricsEndpoint     (deferred to D phase 5 — placeholder phase)
//   Shutdown is the strict reverse, with the same enum values.
// ============================================================================

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Locked phases of the host startup and shutdown sequence. The numeric
/// values are stable and tested directly so reordering requires an explicit
/// blueprint update.
/// </summary>
public enum StartupPhase
{
    /// <summary>Parse environment variables and command-line arguments.</summary>
    ParseEnvironment = 1,

    /// <summary>Build the generic host and DI container.</summary>
    BuildContainer = 2,

    /// <summary>Load (or create on first run) the gateway identity file.</summary>
    LoadGatewayIdentity = 3,

    /// <summary>Load and validate gateway configuration via <c>IConfigurationManager</c>.</summary>
    LoadConfiguration = 4,

    /// <summary>Load and verify the signed license via <c>ILicenseManager</c>.</summary>
    LoadLicense = 5,

    /// <summary>Construct the <c>RuntimeDiagnosticsCollector</c> + meters bindings.</summary>
    ConstructDiagnostics = 6,

    /// <summary>Construct the per-route buffer factory.</summary>
    ConstructBufferFactory = 7,

    /// <summary>Register routes with the routing engine from the loaded configuration.</summary>
    RegisterRoutes = 8,

    /// <summary>Start the source supervisor (deferred to D phase 4; placeholder for ordering).</summary>
    StartSourceSupervisor = 9,

    /// <summary>Call <c>IRoutingEngine.StartAllAsync</c>.</summary>
    StartRoutingEngine = 10,

    /// <summary>Flip readiness probe to "ready".</summary>
    MarkReady = 11,

    /// <summary>Start the Prometheus scrape endpoint (deferred to D phase 5; placeholder).</summary>
    StartMetricsEndpoint = 12,
}
