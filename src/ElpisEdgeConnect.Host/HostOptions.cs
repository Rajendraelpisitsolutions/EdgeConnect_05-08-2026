// ============================================================================
// File: HostOptions.cs
// Purpose: Construction-time options for the EdgeConnect host. Carries the
//          paths and toggles needed by the startup sequence: config root,
//          license file, gateway identity file. Read once at startup; never
//          mutated thereafter.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D
// Milestone: D — phase 2.
// ============================================================================

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Host construction options. All fields are read once during startup and
/// never mutated. Production callers populate from environment variables
/// and command-line arguments; tests populate directly.
/// </summary>
public sealed record HostOptions
{
    /// <summary>Root directory containing the gateway configuration files.</summary>
    public required string ConfigDirectory { get; init; }

    /// <summary>
    /// Gateway data root — the parent directory of <c>config/</c>,
    /// <c>buffer/</c>, and the gateway identity file. Optional; when
    /// null the host derives it from <see cref="ConfigDirectory"/>'s
    /// parent (the historical convention where <see cref="ConfigDirectory"/>
    /// is <c>$dataRoot/config</c>).
    /// <para>
    /// <c>ConfigurationStorageLayout</c> is documented as "rooted at
    /// the gateway data directory" — passing
    /// <see cref="ConfigDirectory"/> instead would double the
    /// <c>config/</c> segment and produce paths like
    /// <c>$dataRoot/config/config/current.json</c>. New callers SHOULD
    /// set <see cref="DataRoot"/> explicitly; legacy callers that
    /// only set <see cref="ConfigDirectory"/> get the right behavior
    /// via <see cref="ResolvedDataRoot"/>.
    /// </para>
    /// </summary>
    public string? DataRoot { get; init; }

    /// <summary>
    /// Resolved gateway data root — <see cref="DataRoot"/> when set,
    /// otherwise derived from <see cref="ConfigDirectory"/>'s parent.
    /// Used by the composition root to construct the
    /// <c>ConfigurationStorageLayout</c>.
    /// </summary>
    public string ResolvedDataRoot =>
        DataRoot
        ?? System.IO.Path.GetDirectoryName(
            ConfigDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
        ?? ConfigDirectory;

    /// <summary>Absolute path to the signed license file.</summary>
    public required string LicensePath { get; init; }

    /// <summary>
    /// Absolute path to the gateway identity file. The file is created on
    /// first run if it does not exist; on subsequent runs the existing
    /// identity is loaded.
    /// </summary>
    public required string GatewayIdentityPath { get; init; }

    /// <summary>
    /// When true, the startup sequence STILL records the deferred phases
    /// (StartSourceSupervisor, StartMetricsEndpoint) via the observer so
    /// the ordering contract is stable across phase 2-5 development. The
    /// actual work for those phases lands in later D phases.
    /// </summary>
    public bool RecordDeferredPhases { get; init; } = true;

    /// <summary>
    /// URL that the host's <c>HostEndpointsServer</c> binds to. Must be a
    /// fully-qualified HttpListener prefix with a trailing slash, e.g.
    /// <c>"http://localhost:9100/"</c>. Localhost binding does not require
    /// Windows URL reservations; any other binding does.
    /// </summary>
    public string EndpointsListenUrl { get; init; } = "http://localhost:9100/";

    /// <summary>
    /// When false, <see cref="HostStartup"/> skips starting the
    /// <c>HostEndpointsServer</c>. Tests that don't need the HTTP surface
    /// set this to false to avoid binding a port. Default: true.
    /// </summary>
    public bool EnableEndpointsServer { get; init; } = true;

    /// <summary>
    /// Wait-window milliseconds for the Management apply endpoint to
    /// surface the reconcile outcome (M.P2.2 phase 3). The endpoint
    /// awaits the <c>IReloadOutcomeRegistry</c> for at most this long
    /// before returning <c>Status = InProgress</c> and letting the
    /// operator poll <c>/diagnostics/configuration-faults</c>. Default
    /// 10 000ms (Q4 verdict, phase 3 plan v2): targets ~90-95% of
    /// healthy reconciles resolving inside the window.
    /// </summary>
    public int ReloadOutcomeWaitMs { get; init; } = 10_000;
}
