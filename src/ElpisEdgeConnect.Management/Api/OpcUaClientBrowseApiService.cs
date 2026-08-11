// ============================================================================
// File: Api/OpcUaClientBrowseApiService.cs
// Purpose: Drives the OPC UA Client Browse probe behind
//          POST /api/v1/sources/browse/opcua-client. Thin wrapper around
//          the protocol-side OpcUaClientBrowseService (Sources.OpcUaClient,
//          PR 5) that adds: license gate, single-flight lease per endpoint,
//          15s probe budget, correlation ProbeId.
//
//          Named *ApiService* (not *Service*) to avoid colliding with
//          Sources.OpcUaClient.OpcUaClientBrowseService — that's the
//          protocol-side implementation of ITagBrowseService and is what
//          this service ultimately delegates to. The naming makes the
//          relationship clear at every callsite.
//
// LOCKED behaviour pinned by tests (PR 7c-2 plan + amendments,
// user lock 2026-05-29):
//
//   1. License-gated by `source-opcua-client`
//   2. Single-flight per endpoint URL (extracted from the wizard's
//      SourceConfigJson — protects the OPC server from a "click Browse
//      5 times" hammer)
//   3. Fixed 15s probe budget (matches the FOCAS2 Browse + TestConnect
//      budgets)
//   4. Correlation ProbeId on every log line + in the DTO
//   5. Lazy expansion is the wizard's responsibility — this service
//      faithfully passes StartingNodeId + MaxDepth + MaxNodes through to
//      the protocol-side service
//
// Reference: PR 7c-2 plan + amendments (user lock 2026-05-29)
//            docs/decisions/0015-wizard-contract.md Rule 9
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Browse;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.OpcUaClient;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Management-side service that wraps the protocol-side
/// <see cref="OpcUaClientBrowseService"/> (from <c>Sources.OpcUaClient</c>)
/// with license gating, single-flight, probe budget, and DTO shaping.
/// </summary>
public sealed class OpcUaClientBrowseApiService
{
    private const string LicenseModuleKey = OpcUaClientSourceConfiguration.LicenseModuleKey;

    /// <summary>Fixed probe budget — matches FOCAS2 Browse + TestConnect services.</summary>
    internal static readonly TimeSpan DefaultProbeBudget = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions ConfigParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Func<ITagBrowseService> _browseServiceFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<OpcUaClientBrowseApiService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _probeBudget;

    /// <summary>
    /// Production constructor — delegates to a fresh
    /// <see cref="OpcUaClientBrowseService"/> per probe, gated by the
    /// supplied <see cref="ILicenseManager"/>.
    /// </summary>
    public OpcUaClientBrowseApiService(
        ILoggerFactory loggerFactory,
        ILicenseManager? license = null)
        : this(
            browseServiceFactory: () => new OpcUaClientBrowseService(
                loggerFactory.CreateLogger<OpcUaClientBrowseService>()),
            isModuleEnabled: BuildLicenseGate(license),
            loggerFactory: loggerFactory,
            probeBudget: DefaultProbeBudget)
    {
    }

    /// <summary>
    /// Test constructor — injects a custom browse-service factory + license
    /// gate + probe budget so unit tests can pin the contract without
    /// standing up a real OPC stack endpoint.
    /// </summary>
    internal OpcUaClientBrowseApiService(
        Func<ITagBrowseService> browseServiceFactory,
        Func<string, bool> isModuleEnabled,
        ILoggerFactory loggerFactory,
        TimeSpan probeBudget)
    {
        ArgumentNullException.ThrowIfNull(browseServiceFactory);
        ArgumentNullException.ThrowIfNull(isModuleEnabled);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _browseServiceFactory = browseServiceFactory;
        _isModuleEnabled = isModuleEnabled;
        _logger = loggerFactory.CreateLogger<OpcUaClientBrowseApiService>();
        _probeBudget = probeBudget;
    }

    /// <summary>License-gate delegate mirroring the dev-mode permissive semantic.</summary>
    internal static Func<string, bool> BuildLicenseGate(ILicenseManager? license) =>
        moduleKey =>
            license is null
            || license.Current is null
            || license.IsModuleEnabled(moduleKey);

    /// <summary>
    /// Run a browse probe. Returns a populated DTO; never throws for
    /// expected probe failures (license, busy, malformed config,
    /// timeout, browse-side errors).
    /// </summary>
    public async Task<OpcUaClientBrowseProbeOutcome> BrowseAsync(
        OpcUaClientBrowseRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var probeId = GenerateProbeId();
        var sw = Stopwatch.StartNew();

        // ── License gate ──────────────────────────────────────────────
        if (!_isModuleEnabled(LicenseModuleKey))
        {
            _logger.LogInformation(
                "OPC UA browse {ProbeId} blocked: license module '{Module}' disabled.",
                probeId, LicenseModuleKey);
            return Outcome(
                OpcUaClientBrowseStatus.LicenseDisabled,
                FailDto(probeId, sw,
                    "OPCUA.PROBE_LICENSE_DISABLED",
                    "OPC UA Client source module is not licensed for this gateway.",
                    "Contact your administrator to enable the source-opcua-client license module."));
        }

        // ── Config parse — required to derive the single-flight lease key ─
        string endpointUrl;
        try
        {
            endpointUrl = ExtractEndpointUrl(request.SourceConfigJson);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            _logger.LogInformation(
                "OPC UA browse {ProbeId} config-invalid: {Message}",
                probeId, ex.Message);
            return Outcome(
                OpcUaClientBrowseStatus.ConfigInvalid,
                FailDto(probeId, sw,
                    "OPCUA.BROWSE_CONFIG_INVALID",
                    $"SourceConfigJson is malformed: {ex.Message}",
                    "Verify the wizard's draft is well-formed before retrying the browse."));
        }

        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return Outcome(
                OpcUaClientBrowseStatus.ConfigInvalid,
                FailDto(probeId, sw,
                    "OPCUA.BROWSE_CONFIG_INCOMPLETE",
                    "EndpointUrl is missing from the supplied config.",
                    "Fill in the OPC UA endpoint URL on the Connection step before browsing."));
        }

        // ── Single-flight per endpoint URL ────────────────────────────
        var lease = _leases.GetOrAdd(endpointUrl, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "OPC UA browse {ProbeId} busy: another probe is in flight against {Endpoint}.",
                probeId, endpointUrl);
            return Outcome(
                OpcUaClientBrowseStatus.Busy,
                FailDto(probeId, sw,
                    "OPCUA.BROWSE_IN_FLIGHT",
                    $"Another browse is in flight against {endpointUrl}.",
                    "Wait for the active browse to finish before retrying."));
        }

        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);

            var coreRequest = new BrowseRequest
            {
                SourceConfigJson = request.SourceConfigJson,
                StartingNodeId = request.StartingNodeId,
                MaxDepth = request.MaxDepth,
                MaxNodes = request.MaxNodes,
            };

            _logger.LogInformation(
                "OPC UA browse {ProbeId} → BrowseAsync(endpoint={Endpoint}, startingNode={StartingNode}, maxDepth={Depth}, maxNodes={MaxNodes}).",
                probeId, endpointUrl, request.StartingNodeId ?? "<root>", request.MaxDepth, request.MaxNodes);

            BrowseResult coreResult;
            try
            {
                var browseService = _browseServiceFactory();
                coreResult = await browseService.BrowseAsync(coreRequest, probeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (probeCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return Outcome(
                    OpcUaClientBrowseStatus.Failure,
                    FailDto(probeId, sw,
                        "OPCUA.BROWSE_TIMEOUT",
                        $"Browse against {endpointUrl} did not complete within {_probeBudget.TotalSeconds:F0}s.",
                        "Verify the OPC UA server is reachable and the requested depth/node count is reasonable for the address space."));
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("OPCUA.", StringComparison.Ordinal))
            {
                var code = ExtractErrorCode(ex.Message);
                return Outcome(
                    OpcUaClientBrowseStatus.Failure,
                    FailDto(probeId, sw, code, ex.Message,
                        RemediationHintFor(code)));
            }

            _logger.LogInformation(
                "OPC UA browse {ProbeId} succeeded in {Elapsed}ms (truncated={Truncated}).",
                probeId, sw.ElapsedMilliseconds, coreResult.Truncated);

            return Outcome(
                OpcUaClientBrowseStatus.Success,
                new OpcUaClientBrowseResultDto
                {
                    ProbeId = probeId,
                    Success = true,
                    Result = coreResult,
                    ElapsedMs = sw.ElapsedMilliseconds,
                });
        }
        finally
        {
            lease.Release();
        }
    }

    /// <summary>
    /// Extract the endpoint URL from the wizard's SourceConfigJson. Used
    /// for both lease keying and the "endpoint missing" early-return.
    /// </summary>
    private static string ExtractEndpointUrl(string? sourceConfigJson)
    {
        if (string.IsNullOrWhiteSpace(sourceConfigJson))
        {
            throw new ArgumentException("SourceConfigJson is empty.");
        }
        using var doc = JsonDocument.Parse(sourceConfigJson);
        if (!doc.RootElement.TryGetProperty("endpointUrl", out var elem)
            && !doc.RootElement.TryGetProperty("EndpointUrl", out elem))
        {
            return string.Empty;
        }
        return elem.GetString() ?? string.Empty;
    }

    private static string ExtractErrorCode(string message)
    {
        var colon = message.IndexOf(':');
        if (colon <= 0) return "OPCUA.BROWSE_FAILED";
        var prefix = message[..colon];
        return prefix.StartsWith("OPCUA.", StringComparison.Ordinal) ? prefix : "OPCUA.BROWSE_FAILED";
    }

    private static string RemediationHintFor(string errorCode) => errorCode switch
    {
        "OPCUA.BROWSE_CONFIG_INCOMPLETE" =>
            "Fill in the missing fields on the Connection step (typically EndpointUrl or ApplicationUri) before browsing.",
        "OPCUA.BROWSE_CONFIG_INVALID" =>
            "The wizard's draft is malformed; cancel out and start a fresh draft if the issue persists.",
        _ =>
            "Check that the OPC UA server is reachable and the security/auth fields on the Connection step are correct.",
    };

    private static OpcUaClientBrowseResultDto FailDto(
        string probeId, Stopwatch sw, string errorCode, string message, string hint) => new()
    {
        ProbeId = probeId,
        Success = false,
        ElapsedMs = sw.ElapsedMilliseconds,
        ErrorCode = errorCode,
        ErrorMessage = message,
        RemediationHint = hint,
    };

    private static OpcUaClientBrowseProbeOutcome Outcome(
        OpcUaClientBrowseStatus status, OpcUaClientBrowseResultDto result) =>
        new(status, result);

    private static string GenerateProbeId() =>
        string.Concat("probe-", Guid.NewGuid().ToString("N").AsSpan(0, 8));
}

/// <summary>Service-level status driving the HTTP status code in the API layer.</summary>
public enum OpcUaClientBrowseStatus
{
    /// <summary>Browse succeeded — HTTP 200.</summary>
    Success,

    /// <summary>Browse ran but failed (server-side / timeout / etc.) — HTTP 200 (Success=false body).</summary>
    Failure,

    /// <summary>Malformed or incomplete request — HTTP 400.</summary>
    ConfigInvalid,

    /// <summary>License gate refused — HTTP 403.</summary>
    LicenseDisabled,

    /// <summary>Single-flight lease busy — HTTP 409.</summary>
    Busy,
}

/// <summary>Service-layer outcome — DTO plus the status enum used for HTTP mapping.</summary>
public sealed record OpcUaClientBrowseProbeOutcome(
    OpcUaClientBrowseStatus Status,
    OpcUaClientBrowseResultDto Result);

/// <summary>
/// Input parameters for an OPC UA browse probe. Mirrors
/// <see cref="BrowseRequest"/> but lives in the Management.Api namespace
/// for OpenAPI serialisation independence — Core records can evolve
/// without re-shaping the wire contract.
/// </summary>
public sealed record OpcUaClientBrowseRequest
{
    /// <summary>
    /// Serialised <see cref="OpcUaClientSourceConfiguration"/> from the
    /// wizard's in-progress draft. The protocol-side browse service
    /// deserialises and uses it for session creation.
    /// </summary>
    public required string SourceConfigJson { get; init; }

    /// <summary>
    /// Starting node id (e.g. <c>"ns=2;s=Simulated"</c>). Null/empty →
    /// the protocol's natural root (Objects folder for OPC UA).
    /// </summary>
    public string? StartingNodeId { get; init; }

    /// <summary>Maximum recursion depth. Default 1 (one level of children).</summary>
    public int MaxDepth { get; init; } = 1;

    /// <summary>Maximum nodes returned. Default 1000.</summary>
    public int MaxNodes { get; init; } = 1_000;
}
