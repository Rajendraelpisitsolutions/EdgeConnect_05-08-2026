// ============================================================================
// File: IBrotherHttpApi.cs
// Purpose: Abstraction over the six Brother HTTP web-monitoring endpoint calls.
//          Production uses BrotherHttpHttpApi (via IHttpClientFactory per
//          v3 §4 Pattern C); tests inject BrotherHttpDemoApi (synthetic) or
//          a stub. Each method returns the raw response text on success or
//          null on transient failure — matches the legacy null-on-failure
//          convention so the collectors and the parity oracle apply
//          identical "endpoint produced no data" semantics.
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §3 (ISourceAdapter
//            contract), v3 §4 (IHttpClientFactory pattern),
//            docs/sessions/2026-05-21-mp24-brother-http-plan-v3.1.md §B.1 (atomic batch)
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Internal abstraction over the six Brother HTTP web-monitoring endpoints.
/// Implementations: <see cref="BrotherHttpHttpApi"/> (real, via
/// <c>IHttpClientFactory</c>) and <see cref="BrotherHttpDemoApi"/>
/// (synthetic, used when <see cref="BrotherHttpDemoModeOptions.IsEnabled"/>
/// is true at adapter construction). Test code injects stubs.
/// </summary>
/// <remarks>
/// All methods return <see langword="null"/> on transient failure
/// (HTTP error, timeout, parse failure) — the adapter's per-endpoint
/// failure metric (<c>elpis_edgeconnect_brother_endpoint_failures_total</c>
/// per v3.1 §B.5) and the precedence chain (v3 §6.2) consume the
/// <see langword="null"/> sentinel rather than exceptions. Cancellation
/// (via <see cref="CancellationToken"/>) MUST propagate as
/// <see cref="System.OperationCanceledException"/>.
/// </remarks>
internal interface IBrotherHttpApi
{
    /// <summary>GET <c>/HTTPD_MCNINFO</c> — machine info (hostname, model, status code).</summary>
    Task<string?> GetMachineInfoAsync(CancellationToken ct);

    /// <summary>GET <c>/MNTP_CYCLETIME</c> — cycle time, program, operation hours.</summary>
    Task<string?> GetCycleTimeAsync(CancellationToken ct);

    /// <summary>GET <c>/MNTP_WKCNTR</c> — work counters (four counters, parts/target).</summary>
    Task<string?> GetWorkCountersAsync(CancellationToken ct);

    /// <summary>GET <c>/ATC_TOOLS</c> — ATC magazine tool list.</summary>
    Task<string?> GetAtcToolsAsync(CancellationToken ct);

    /// <summary>GET <c>/ALARM_CURALMLIST</c> — currently active alarms.</summary>
    Task<string?> GetAlarmsAsync(CancellationToken ct);

    /// <summary>GET <c>/MNTP_MAINTNOTICE</c> — maintenance notices.</summary>
    Task<string?> GetMaintenanceNoticesAsync(CancellationToken ct);
}
