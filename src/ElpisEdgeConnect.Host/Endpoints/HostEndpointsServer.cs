// ============================================================================
// File: Endpoints/HostEndpointsServer.cs
// Purpose: Minimal HttpListener-based HTTP server exposing three routes:
//            GET /health/live   — always 200 while the process is up
//            GET /health/ready  — 200 when IHostReadinessGate.IsReady, else 503
//            GET /metrics       — Prometheus text format for DiagnosticsMeters
//
//          Driven explicitly by HostStartup during the StartMetricsEndpoint
//          phase. No ASP.NET Core dependency; zero NuGet packages beyond
//          the base runtime.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D — health/metrics endpoints
// Milestone: D — phase 5.
//
// LOCKED design rules:
//   * Runs on a single background Task that accepts HttpListener requests.
//     Per-request handling is synchronous and non-blocking — responses
//     are tiny (a few bytes for health, a few KB for metrics).
//   * No external exporter dependency. PrometheusTextEmitter drives a
//     MeterListener each time /metrics is hit; this is O(instruments * routes)
//     per scrape and acceptable at the 15 s scrape cadence.
//   * Failures inside request handlers are logged and turned into 500s
//     — never crash the server loop.
//   * Production binding is localhost-only. Anything else requires an
//     operator to change HostOptions.EndpointsListenUrl and configure
//     Windows URL reservations (netsh http add urlacl).
// ============================================================================

using System;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host.Endpoints;

/// <summary>
/// Tiny HTTP server exposing liveness, readiness, and Prometheus metrics
/// endpoints. Lifecycle is driven explicitly by <see cref="HostStartup"/>.
/// </summary>
public sealed class HostEndpointsServer : IAsyncDisposable
{
    private readonly IHostReadinessGate _readiness;
    private readonly Meter _meter;
    private readonly ILogger<HostEndpointsServer> _logger;
    private readonly string _listenUrl;
    private HttpListener? _listener;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    /// <summary>Construct the endpoints server.</summary>
    public HostEndpointsServer(
        IHostReadinessGate readiness,
        Meter meter,
        string listenUrl,
        ILogger<HostEndpointsServer> logger)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentException.ThrowIfNullOrEmpty(listenUrl);
        ArgumentNullException.ThrowIfNull(logger);
        _readiness = readiness;
        _meter = meter;
        _listenUrl = listenUrl.EndsWith('/') ? listenUrl : listenUrl + "/";
        _logger = logger;
    }

    /// <summary>The URL the server is listening on.</summary>
    public string ListenUrl => _listenUrl;

    /// <summary>
    /// Start accepting requests. Returns once the listener is open.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_listener is not null)
        {
            return Task.CompletedTask;
        }
        _listener = new HttpListener();
        _listener.Prefixes.Add(_listenUrl);
        _listener.Start();
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => AcceptLoopAsync(_loopCts.Token), _loopCts.Token);
        _logger.LogInformation("HostEndpointsServer listening on {Url}", _listenUrl);
        return Task.CompletedTask;
    }

    /// <summary>Stop accepting new requests and wait for the accept loop to exit.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            return;
        }

        try { _loopCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { _listener.Stop(); } catch (ObjectDisposedException) { }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected */ }
        }

        try { _listener.Close(); } catch (ObjectDisposedException) { }
        _listener = null;
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
        _logger.LogInformation("HostEndpointsServer stopped.");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener!;
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                // Listener was stopped; normal shutdown.
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                HandleRequest(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in endpoints request handler.");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { /* best-effort */ }
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var method = context.Request.HttpMethod;

        if (!string.Equals(method, "GET", StringComparison.Ordinal))
        {
            WriteText(context, 405, "method not allowed\n", "text/plain");
            return;
        }

        switch (path)
        {
            case "/health/live":
                WriteText(context, 200, "live\n", "text/plain");
                return;

            case "/health/ready":
                if (_readiness.IsReady)
                {
                    WriteText(context, 200, "ready\n", "text/plain");
                }
                else
                {
                    WriteText(context, 503, "not ready\n", "text/plain");
                }
                return;

            case "/metrics":
                var body = PrometheusTextEmitter.Render(_meter);
                // Prometheus text format content-type per spec.
                WriteText(context, 200, body, "text/plain; version=0.0.4; charset=utf-8");
                return;

            default:
                WriteText(context, 404, "not found\n", "text/plain");
                return;
        }
    }

    private static void WriteText(HttpListenerContext context, int status, string body, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }
}
