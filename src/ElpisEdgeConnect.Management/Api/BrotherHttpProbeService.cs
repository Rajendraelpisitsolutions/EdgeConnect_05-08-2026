// ============================================================================
// File: Api/BrotherHttpProbeService.cs
// Purpose: Drives the Brother HTTP Test Connection probe behind
//          POST /api/v1/sources/browse/brother-http — performs a single
//          throwaway GET {BaseUrl}/HTTPD_MCNINFO against the operator-
//          supplied Brother CNC config and surfaces the result with
//          diagnostic fields (EndpointTested, HttpStatusObserved).
//
//          Locked invariants (M.2d.2 v2 §4.2, §4.7):
//            * No state mutation — probe is read-only, never writes.
//            * License-gated by source-brother-http module key.
//            * Single-flight per normalised BaseUrl (matches Focas2 +
//              MQTT target-keyed lease pattern; v2 §4.2).
//            * 8s probe budget (matches Focas2 default).
//            * No full 6-endpoint sweep — just HTTPD_MCNINFO health
//              authority (M.P2.4 v3.1 Q4 lock).
//
//          Closes the M.P2.4 Q12 deferral.
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §4
//            docs/sessions/2026-05-21-mp24-handoff.md §6 (Q12 Test Connection)
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.BrotherHttp;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Executes a one-shot Brother HTTP Test Connection probe — used by the
/// Brother source wizard's "Test Connection" button to verify CNC
/// reachability before the operator commits a draft.
/// </summary>
/// <remarks>
/// Pattern mirrors <see cref="MqttTestConnectionService"/> and
/// <see cref="Focas2BrowseService"/> deliberately: target-keyed lease,
/// license gate, fixed probe budget, structured result DTO. Diagnostic
/// fields (<c>EndpointTested</c>, <c>HttpStatusObserved</c>) added per
/// M.2d.2 v2 §4.4 for support-troubleshooting value.
/// </remarks>
public sealed class BrotherHttpProbeService
{
    private const string LicenseModuleKey = BrotherHttpSourceConfiguration.LicenseModuleKey;

    /// <summary>Fixed probe budget — 8s matches the Focas2 + Modbus probe ladder budgets.</summary>
    internal static readonly TimeSpan DefaultProbeBudget = TimeSpan.FromSeconds(8);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<BrotherHttpProbeService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _probeBudget;

    /// <summary>
    /// Production constructor — pulls <see cref="IHttpClientFactory"/>
    /// from DI and uses the supplied <see cref="ILicenseManager"/> for
    /// module gating. License manager null OR no license loaded =
    /// permissive (matches MQTT + Focas2 dev-mode semantic).
    /// </summary>
    public BrotherHttpProbeService(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILicenseManager? license = null)
        : this(
            httpClientFactory: httpClientFactory,
            isModuleEnabled: MqttTestConnectionService.BuildLicenseGate(license),
            loggerFactory: loggerFactory,
            probeBudget: DefaultProbeBudget)
    {
    }

    /// <summary>
    /// Test-only constructor — injects a custom license gate + probe
    /// budget so unit tests can exercise both license-disabled and
    /// timeout paths deterministically.
    /// </summary>
    internal BrotherHttpProbeService(
        IHttpClientFactory httpClientFactory,
        Func<string, bool> isModuleEnabled,
        ILoggerFactory loggerFactory,
        TimeSpan probeBudget)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(isModuleEnabled);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _httpClientFactory = httpClientFactory;
        _isModuleEnabled = isModuleEnabled;
        _logger = loggerFactory.CreateLogger<BrotherHttpProbeService>();
        _probeBudget = probeBudget;
    }

    /// <summary>
    /// Run the probe. Returns a <see cref="BrotherHttpProbeOutcome"/>
    /// describing success or the categorised failure.
    /// </summary>
    public async Task<BrotherHttpProbeOutcome> ProbeAsync(
        BrotherHttpProbeRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var probeId = GenerateProbeId();
        var sw = Stopwatch.StartNew();

        // ── License gate ──────────────────────────────────────────────
        if (!_isModuleEnabled(LicenseModuleKey))
        {
            _logger.LogInformation("Brother probe {ProbeId} blocked: license module '{Module}' disabled.",
                probeId, LicenseModuleKey);
            return new BrotherHttpProbeOutcome(
                Status: BrotherHttpProbeStatus.LicenseDisabled,
                Result: new BrotherHttpProbeResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ErrorCode = "BROTHER.PROBE_LICENSE_DISABLED",
                    ErrorMessage = "Brother HTTP source module is not licensed for this gateway.",
                });
        }

        // ── BaseUrl normalisation (per v2 §4.2) ───────────────────────
        // Lowercase host, trailing-slash stripped — so two probes
        // targeting "http://192.168.2.110/" and "http://192.168.2.110"
        // contend on the same lease.
        var normalisedBaseUrl = NormaliseBaseUrl(request.BaseUrl);
        if (normalisedBaseUrl is null)
        {
            return new BrotherHttpProbeOutcome(
                Status: BrotherHttpProbeStatus.Failure,
                Result: new BrotherHttpProbeResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ErrorCode = "BROTHER.PROBE_CONFIG_INVALID",
                    ErrorMessage = $"BaseUrl '{request.BaseUrl}' is not a valid absolute URI.",
                });
        }

        // ── Single-flight per normalised BaseUrl ──────────────────────
        var lease = _leases.GetOrAdd(normalisedBaseUrl, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("Brother probe {ProbeId} busy: {Key} has another probe in flight.",
                probeId, normalisedBaseUrl);
            return new BrotherHttpProbeOutcome(
                Status: BrotherHttpProbeStatus.Busy,
                Result: new BrotherHttpProbeResultDto
                {
                    ProbeId = probeId,
                    Success = false,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    ErrorCode = "BROTHER.PROBE_BUSY",
                    ErrorMessage = $"Another Test Connection probe is in flight against {normalisedBaseUrl}.",
                });
        }

        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);

            var endpoint = $"{normalisedBaseUrl}/HTTPD_MCNINFO";

            _logger.LogInformation(
                "Brother probe {ProbeId} → GET {Endpoint} (timeout={Budget}s).",
                probeId, endpoint, _probeBudget.TotalSeconds);

            var httpClient = _httpClientFactory.CreateClient(nameof(BrotherHttpProbeService));
            HttpResponseMessage? response = null;
            int? observedStatus = null;
            try
            {
                response = await httpClient
                    .GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, probeCts.Token)
                    .ConfigureAwait(false);
                observedStatus = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    return Fail(probeId, sw, endpoint, observedStatus,
                        "BROTHER.PROBE_HTTP_STATUS",
                        $"Brother CNC at {endpoint} returned HTTP {observedStatus} {response.ReasonPhrase}.");
                }

                // Verify body is non-empty — HTTPD_MCNINFO returns plain-text
                // key=value pairs; empty body is a strong signal something
                // is wrong even at HTTP 200 (some appliances return 200 +
                // empty body when the path doesn't exist).
                var body = await response.Content.ReadAsStringAsync(probeCts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(body))
                {
                    return Fail(probeId, sw, endpoint, observedStatus,
                        "BROTHER.PROBE_EMPTY_BODY",
                        $"Brother CNC at {endpoint} returned HTTP 200 with an empty body. " +
                        "This typically indicates the HTTPD_MCNINFO endpoint is not available — " +
                        "verify this is actually a Brother CNC and HTTPD is enabled.");
                }
            }
            catch (TaskCanceledException) when (probeCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return Fail(probeId, sw, endpoint, observedStatus,
                    "BROTHER.PROBE_TIMEOUT",
                    $"Brother CNC at {endpoint} did not respond within {_probeBudget.TotalSeconds:F0}s.");
            }
            catch (HttpRequestException ex)
            {
                return Fail(probeId, sw, endpoint, observedStatus,
                    "BROTHER.PROBE_UNREACHABLE",
                    $"Brother CNC at {endpoint} is unreachable: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Fail(probeId, sw, endpoint, observedStatus,
                    "BROTHER.PROBE_FAILED",
                    $"Brother CNC at {endpoint} probe failed: {ex.Message}");
            }
            finally
            {
                response?.Dispose();
            }

            _logger.LogInformation("Brother probe {ProbeId} succeeded in {Elapsed}ms (HTTP {Status}).",
                probeId, sw.ElapsedMilliseconds, observedStatus);

            return new BrotherHttpProbeOutcome(
                Status: BrotherHttpProbeStatus.Success,
                Result: new BrotherHttpProbeResultDto
                {
                    ProbeId = probeId,
                    Success = true,
                    ElapsedMs = (int)sw.ElapsedMilliseconds,
                    EndpointTested = endpoint,
                    HttpStatusObserved = observedStatus,
                });
        }
        finally
        {
            lease.Release();
        }
    }

    /// <summary>
    /// Normalise a BaseUrl per v2 §4.2 lease-key rules: lowercase host,
    /// trailing-slash stripped, scheme preserved. Returns null when the
    /// input isn't a parseable absolute URI.
    /// </summary>
    internal static string? NormaliseBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }
        // Strip trailing slash; lowercase host. Port preserved verbatim
        // (default ports already canonicalised by Uri).
        var trimmed = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        // Force lowercase scheme + host while preserving path casing
        // (HTTPD_MCNINFO is appended later — paths are case-sensitive
        // on most Brother CNCs, so leave the user-supplied path alone).
        var lowerScheme = uri.Scheme.ToLowerInvariant();
        var lowerHost = uri.Host.ToLowerInvariant();
        var portSegment = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{lowerScheme}://{lowerHost}{portSegment}{path}";
    }

    private BrotherHttpProbeOutcome Fail(
        string probeId,
        Stopwatch sw,
        string endpoint,
        int? httpStatus,
        string errorCode,
        string errorMessage)
    {
        _logger.LogWarning("Brother probe {ProbeId} failed: {ErrorCode} — {Message}",
            probeId, errorCode, errorMessage);
        return new BrotherHttpProbeOutcome(
            Status: BrotherHttpProbeStatus.Failure,
            Result: new BrotherHttpProbeResultDto
            {
                ProbeId = probeId,
                Success = false,
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                EndpointTested = endpoint,
                HttpStatusObserved = httpStatus,
            });
    }

    private static string GenerateProbeId() =>
        string.Concat("probe-", Guid.NewGuid().ToString("N").AsSpan(0, 8));
}

/// <summary>Service-level status that drives the HTTP status code in the API layer.</summary>
public enum BrotherHttpProbeStatus
{
    /// <summary>Probe succeeded — HTTP 200.</summary>
    Success,

    /// <summary>Probe ran but the CNC rejected, timed out, or returned non-success — HTTP 200 with Success=false.</summary>
    Failure,

    /// <summary>License gate refused the probe — HTTP 403.</summary>
    LicenseDisabled,

    /// <summary>Single-flight lease busy — HTTP 409.</summary>
    Busy,
}

/// <summary>Service-layer outcome — DTO plus the status enum that drives HTTP status mapping.</summary>
public sealed record BrotherHttpProbeOutcome(
    BrotherHttpProbeStatus Status,
    BrotherHttpProbeResultDto Result);

/// <summary>
/// Input parameters for the Brother HTTP probe. The wire DTO (deserialised
/// by the API endpoint) projects into this record; tests instantiate it
/// directly.
/// </summary>
public sealed record BrotherHttpProbeRequest
{
    /// <summary>Brother CNC HTTP base URL, e.g. <c>"http://192.168.2.110"</c>.</summary>
    public required string BaseUrl { get; init; }
}
