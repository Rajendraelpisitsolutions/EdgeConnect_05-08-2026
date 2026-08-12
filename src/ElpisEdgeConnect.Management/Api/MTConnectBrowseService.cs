// ============================================================================
// File: Api/MTConnectBrowseService.cs
// Purpose: Drives POST /api/v1/sources/browse/mtconnect (M.2b.4). One HTTP GET
//          of the agent's /probe (fixed 10 s budget, independent of the runtime
//          poll timeout — plan v2 QC-4), parsed by the adapter-owned
//          MTConnectProbeParser into a FOCAS2-style semantic-availability result.
//          License-gated on `source-mtconnect`. The HTTP fetch is behind a seam
//          so the status/parse logic is unit-tested without a live agent.
// Reference: docs/sessions/2026-05-31-mtconnect-source-wizard-plan-v2.md §3/§5.
// ============================================================================

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.MTConnect;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>What happened at the HTTP layer when fetching /probe.</summary>
internal enum ProbeFetchOutcome { Ok, Unreachable, Timeout, Unauthorized, HttpError }

/// <summary>The raw outcome of one /probe fetch.</summary>
internal sealed record ProbeFetch(ProbeFetchOutcome Outcome, string? Body = null, int? StatusCode = null);

/// <summary>Seam over the HTTP /probe GET so the service is testable without a live agent.</summary>
internal interface IMTConnectProbeFetcher
{
    Task<ProbeFetch> FetchProbeAsync(string agentBaseUrl, TimeSpan budget, CancellationToken ct);
}

/// <summary>Real HTTP /probe fetcher (one short-lived HttpClient per probe).</summary>
internal sealed class HttpMTConnectProbeFetcher : IMTConnectProbeFetcher
{
    public async Task<ProbeFetch> FetchProbeAsync(string agentBaseUrl, TimeSpan budget, CancellationToken ct)
    {
        Uri uri;
        try
        {
            var baseUrl = agentBaseUrl.EndsWith('/') ? agentBaseUrl : agentBaseUrl + "/";
            uri = new Uri(new Uri(baseUrl, UriKind.Absolute), "probe");
        }
        catch (UriFormatException)
        {
            return new ProbeFetch(ProbeFetchOutcome.Unreachable);
        }

        using var http = new HttpClient { Timeout = budget };
        http.DefaultRequestHeaders.Add("Accept", "application/xml");
        try
        {
            using var resp = await http.GetAsync(uri, ct).ConfigureAwait(false);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new ProbeFetch(ProbeFetchOutcome.Unauthorized, StatusCode: (int)resp.StatusCode);
            }
            if (!resp.IsSuccessStatusCode)
            {
                return new ProbeFetch(ProbeFetchOutcome.HttpError, StatusCode: (int)resp.StatusCode);
            }
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new ProbeFetch(ProbeFetchOutcome.Ok, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsed (not a caller cancellation).
            return new ProbeFetch(ProbeFetchOutcome.Timeout);
        }
        catch (HttpRequestException)
        {
            return new ProbeFetch(ProbeFetchOutcome.Unreachable);
        }
        catch (NotSupportedException)
        {
            // HttpClient rejects a URI whose scheme it cannot speak (a
            // schemeless "host:port" parses as scheme "host"). Callers
            // normalize first, so this is now unreachable in practice — but
            // BrowseAsync promises every outcome is a status, and letting this
            // escape turned an operator typo into a 500.
            return new ProbeFetch(ProbeFetchOutcome.Unreachable);
        }
    }
}

/// <summary>Probes an MTConnect agent's /probe and returns semantic-tag availability + axes.</summary>
public sealed class MTConnectBrowseService
{
    /// <summary>License module gating the browse (and the adapter).</summary>
    public const string LicenseModuleKey = MTConnectSourceConfiguration.LicenseModuleKey;

    /// <summary>Fixed probe budget — an operator interaction, independent of runtime <c>TimeoutSeconds</c> (QC-4).</summary>
    public static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(10);

    private readonly IMTConnectProbeFetcher _fetcher;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<MTConnectBrowseService> _logger;

    /// <summary>DI constructor — real HTTP fetcher, license gate permissive when no license loaded.</summary>
    public MTConnectBrowseService(ILoggerFactory loggerFactory, ILicenseManager? license = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _fetcher = new HttpMTConnectProbeFetcher();
        _isModuleEnabled = module => license is null || license.Current is null || license.IsModuleEnabled(module);
        _logger = loggerFactory.CreateLogger<MTConnectBrowseService>();
    }

    /// <summary>Test constructor — inject a fake fetcher + license gate.</summary>
    internal MTConnectBrowseService(
        IMTConnectProbeFetcher fetcher,
        Func<string, bool> isModuleEnabled,
        ILogger<MTConnectBrowseService> logger)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _isModuleEnabled = isModuleEnabled ?? throw new ArgumentNullException(nameof(isModuleEnabled));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Run the probe. Never throws for expected failures — every outcome is a status.</summary>
    public async Task<MTConnectBrowseResultDto> BrowseAsync(MTConnectBrowseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sw = Stopwatch.StartNew();

        if (!_isModuleEnabled(LicenseModuleKey))
        {
            return Fail(MTConnectBrowseStatus.LicenseDisabled, sw,
                $"License module '{LicenseModuleKey}' is not enabled. The MTConnect source feature is not licensed.");
        }

        // Uri.TryCreate alone is not a scheme check: "agent.local:5000" parses
        // as an absolute URI with scheme "agent.local", so it used to pass here
        // and then throw NotSupportedException inside HttpClient. Normalizing
        // both accepts the bare host an operator actually types and rejects
        // what is genuinely unreachable, with the same sentence the adapter uses.
        var agentBaseUrl = MTConnectSourceConfiguration.TryNormalizeAgentBaseUrl(request.AgentBaseUrl);
        if (agentBaseUrl is null)
        {
            return Fail(MTConnectBrowseStatus.Unreachable, sw,
                MTConnectSourceConfiguration.InvalidAgentBaseUrlMessage(request.AgentBaseUrl));
        }

        var fetch = await _fetcher.FetchProbeAsync(agentBaseUrl, ProbeBudget, ct).ConfigureAwait(false);
        switch (fetch.Outcome)
        {
            case ProbeFetchOutcome.Unreachable:
                return Fail(MTConnectBrowseStatus.Unreachable, sw, "Could not reach the agent. Check the URL, port, and that the MTConnect agent is running.");
            case ProbeFetchOutcome.Timeout:
                return Fail(MTConnectBrowseStatus.Timeout, sw, $"No response within {ProbeBudget.TotalSeconds:0} s.");
            case ProbeFetchOutcome.Unauthorized:
                return Fail(MTConnectBrowseStatus.Unauthorized, sw, "Agent requires authentication (401/403).");
            case ProbeFetchOutcome.HttpError:
                return Fail(MTConnectBrowseStatus.Unreachable, sw, $"Agent returned HTTP {fetch.StatusCode}.");
        }

        MTConnectProbeResult parsed;
        try
        {
            parsed = MTConnectProbeParser.Parse(fetch.Body!, request.AgentDeviceName);
        }
        catch (Exception ex) when (ex is XmlException or MTConnectProbeFormatException)
        {
            _logger.LogDebug(ex, "MTConnect /probe was not a valid document for {Url}", request.AgentBaseUrl);
            return Fail(MTConnectBrowseStatus.InvalidProbeDocument, sw,
                "The response was not a valid MTConnect /probe document.");
        }

        if (parsed.DeviceNames.Count == 0)
        {
            return Fail(MTConnectBrowseStatus.UnsupportedAgent, sw, "The agent returned a /probe with no devices.");
        }

        var status = parsed.HasRecognisedTags
            ? MTConnectBrowseStatus.ReachableWithRecognisedTags
            : MTConnectBrowseStatus.ReachableNoRecognisedTags;

        return new MTConnectBrowseResultDto
        {
            Status = status,
            DeviceName = parsed.TargetDeviceName,
            DeviceUuid = parsed.TargetDeviceUuid,
            Manufacturer = parsed.Manufacturer,
            AvailableDevices = parsed.DeviceNames,
            Tags = parsed.Tags.Select(t => new MTConnectSemanticTagAvailability
            {
                CanonicalTag = t.CanonicalTag,
                Available = t.Available,
                Reason = t.Reason,
                SourceDataItemType = t.SourceDataItemType,
                SourceDataItemId = t.SourceDataItemId,
            }).ToList(),
            Axes = parsed.Axes,
            ElapsedMs = sw.ElapsedMilliseconds,
            Message = status == MTConnectBrowseStatus.ReachableNoRecognisedTags
                ? "Agent reachable, but none of EdgeConnect's standard CNC tags are present — this source would produce no data."
                : null,
        };
    }

    private static MTConnectBrowseResultDto Fail(MTConnectBrowseStatus status, Stopwatch sw, string message) =>
        new() { Status = status, Message = message, ElapsedMs = sw.ElapsedMilliseconds };
}
