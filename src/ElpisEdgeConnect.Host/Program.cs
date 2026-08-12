// ============================================================================
// File: Program.cs
// Purpose: Headless Worker entry point for EdgeConnect. Runs the
//          runtime (sources, sinks, pipeline, routing, buffer,
//          diagnostics) without the Connectivity Studio UI.
//
//          Operators who want the UI use the Management entry point
//          (src/ElpisEdgeConnect.Management) instead — same runtime,
//          plus the Blazor management host. Both entry points share
//          the locked composition sequence via
//          EdgeConnectComposition.ConfigureRuntimeAsync.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D
//            PHASE4_EXECUTION_PLAN.md Milestone M.1b
// Milestone: D — phase 2 (composition root).
// ============================================================================

using Microsoft.Extensions.Hosting;

var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
_ = await ElpisEdgeConnect.Host.EdgeConnectComposition.ConfigureRuntimeAsync(builder.Services);

// HostStartup walks phases 3-12 inside StartAsync, and the reverse on StopAsync.
await builder.Build().RunAsync();
