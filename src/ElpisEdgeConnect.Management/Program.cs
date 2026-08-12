// ============================================================================
// File: Program.cs
// Purpose: PRIMARY EdgeConnect entry point — runs the full runtime
//          (sources, sinks, pipeline, routing, buffer, diagnostics)
//          AND the Connectivity Studio UI in the same process. This
//          is what 99% of operators run.
//
//          For headless deployments (no UI, e.g. fleet-managed
//          containers), src/ElpisEdgeConnect.Host is the alternative
//          entry point. Both share the locked composition sequence
//          via EdgeConnectComposition.ConfigureRuntimeAsync.
//
//          Wiring sequence:
//             1. Build WebApplicationBuilder (gives us Kestrel +
//                Blazor Server + Razor Components support).
//             2. Run shared composition — env-var parsing, eager
//                config + license load, source/sink adapter
//                registrations.
//             3. AddConnectivityStudio — license-gated on
//                `connectivity-studio` module. Skipped silently when
//                the module isn't licensed (matches the G.7 adapter
//                pattern: never cut customer data to enforce
//                licensing).
//             4. Build + map endpoints.
//
//          The runtime's IDiagnosticsService is now the live
//          RuntimeDiagnosticsCollector (registered by
//          AddElpisEdgeConnectHost), so the Studio's Overview /
//          Sources / Sinks pages see real data instead of the M.1a
//          empty-stub placeholder.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1b
// ============================================================================

using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Management;

var builder = WebApplication.CreateBuilder(args);

// Enable running as a Windows service (no-op when launched from a
// console / during development, so `dotnet run` and the packaged EXE
// both work). Integrates the host lifetime with the Windows Service
// Control Manager so the MSI can register this app as an auto-start
// service that serves the Studio UI on http://127.0.0.1:5080.
builder.Host.UseWindowsService();

// Shared composition step — same wiring sequence the headless
// Host.exe uses. Returns the eagerly-loaded license so we can pass
// it to AddConnectivityStudio for the module-licensing gate.
var composition = await EdgeConnectComposition.ConfigureRuntimeAsync(builder.Services);

// Wire Studio. License-gated; safe to call unconditionally (no-op
// when the connectivity-studio module is disabled).
builder.AddConnectivityStudio(license: composition.License);

var app = builder.Build();
app.MapConnectivityStudio();

await app.RunAsync();
