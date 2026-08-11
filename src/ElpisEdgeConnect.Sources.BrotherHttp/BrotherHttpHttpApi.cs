// ============================================================================
// File: BrotherHttpHttpApi.cs
// Purpose: Real IBrotherHttpApi implementation. Issues GET requests to the
//          six Brother HTTP web-monitoring endpoints using IHttpClientFactory
//          per v3 §4 Pattern C — factory is injected at construction, an
//          HttpClient is created per call with the instance's BaseAddress
//          and Timeout, the underlying HttpMessageHandler is pooled by the
//          factory for 100-CNC-scale socket reuse.
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §4 (Pattern C),
//            v3.1 §B.5 (per-endpoint failure metric — increments are wired
//            in step 7 when the adapter consumes this API)
// ============================================================================

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Production <see cref="IBrotherHttpApi"/> backed by a real HTTP client
/// against a Brother CNC's built-in web-monitoring interface. Bound to a
/// single CNC at construction (BaseAddress + Timeout are immutable per
/// instance).
/// </summary>
internal sealed class BrotherHttpHttpApi : IBrotherHttpApi
{
    private readonly IHttpClientFactory _factory;
    private readonly Uri _baseAddress;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;
    private readonly string _instanceId;

    /// <summary>
    /// Construct an HTTP API bound to one Brother CNC.
    /// </summary>
    /// <param name="factory">Injected <see cref="IHttpClientFactory"/> — pools the underlying handler.</param>
    /// <param name="baseUrl">Address of the Brother CNC's HTTP interface, e.g. <c>http://192.168.2.110</c> or the bare <c>192.168.2.110</c> (<c>http://</c> is implied). Trailing slash optional.</param>
    /// <param name="timeoutSeconds">Per-request HTTP timeout in seconds.</param>
    /// <param name="instanceId">Source instance id, used in log scoping only.</param>
    /// <param name="logger">Logger for debug-level endpoint-failure traces.</param>
    /// <exception cref="ArgumentNullException">Any reference parameter is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseUrl"/> is empty, whitespace, or cannot be normalised into an absolute http(s) URL; or <paramref name="timeoutSeconds"/> is non-positive.</exception>
    public BrotherHttpHttpApi(
        IHttpClientFactory factory,
        string baseUrl,
        int timeoutSeconds,
        string instanceId,
        ILogger logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("BaseUrl cannot be null, empty, or whitespace.", nameof(baseUrl));
        }
        // Shared normalisation (bare host/IP → http://host) lives on the
        // configuration record so every entry path lands on identical
        // behaviour — see BrotherHttpSourceConfiguration.TryNormalizeBaseUrl.
        var normalized = BrotherHttpSourceConfiguration.TryNormalizeBaseUrl(baseUrl);
        if (normalized is null)
        {
            throw new ArgumentException(
                BrotherHttpSourceConfiguration.InvalidBaseUrlMessage(baseUrl), nameof(baseUrl));
        }
        // Trailing slash is mandatory for HttpClient.BaseAddress: without it
        // the last path segment is replaced rather than appended.
        _baseAddress = new Uri(normalized + "/", UriKind.Absolute);

        if (timeoutSeconds <= 0)
        {
            throw new ArgumentException($"TimeoutSeconds must be > 0; got {timeoutSeconds}.", nameof(timeoutSeconds));
        }
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);

        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<string?> GetMachineInfoAsync(CancellationToken ct) =>
        FetchAsync("HTTPD_MCNINFO", ct);

    /// <inheritdoc/>
    public Task<string?> GetCycleTimeAsync(CancellationToken ct) =>
        FetchAsync("MNTP_CYCLETIME", ct);

    /// <inheritdoc/>
    public Task<string?> GetWorkCountersAsync(CancellationToken ct) =>
        FetchAsync("MNTP_WKCNTR", ct);

    /// <inheritdoc/>
    public Task<string?> GetAtcToolsAsync(CancellationToken ct) =>
        FetchAsync("ATC_TOOLS", ct);

    /// <inheritdoc/>
    public Task<string?> GetAlarmsAsync(CancellationToken ct) =>
        FetchAsync("ALARM_CURALMLIST", ct);

    /// <inheritdoc/>
    public Task<string?> GetMaintenanceNoticesAsync(CancellationToken ct) =>
        FetchAsync("MNTP_MAINTNOTICE", ct);

    /// <summary>
    /// Helper for testing — exposes the resolved BaseAddress so URL formation
    /// can be inspected in unit tests without dispatching a real request.
    /// </summary>
    internal Uri BaseAddressForTesting => _baseAddress;

    /// <summary>
    /// Issue one GET against a Brother endpoint relative path. Returns
    /// <see langword="null"/> on transient HTTP failure or timeout;
    /// rethrows on cooperative cancellation so the adapter can honour
    /// the supervisor's stop signal.
    /// </summary>
    private async Task<string?> FetchAsync(string relativePath, CancellationToken ct)
    {
        var client = _factory.CreateClient();
        client.BaseAddress = _baseAddress;
        client.Timeout = _timeout;

        try
        {
            return await client.GetStringAsync(relativePath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cooperative cancellation — let the adapter's StopAsync see it.
            throw;
        }
        catch (TaskCanceledException ex)
        {
            // Timeout (TaskCanceledException without cooperative ct cancel)
            // — treat as transient endpoint failure, return null.
            _logger.LogDebug(
                "Brother HTTP endpoint '{Path}' timed out for source '{InstanceId}': {Message}",
                relativePath, _instanceId, ex.Message);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(
                "Brother HTTP endpoint '{Path}' failed for source '{InstanceId}': {Message}",
                relativePath, _instanceId, ex.Message);
            return null;
        }
    }
}
