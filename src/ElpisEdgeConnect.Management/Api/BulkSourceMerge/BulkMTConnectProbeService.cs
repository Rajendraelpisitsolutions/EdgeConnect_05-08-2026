// ============================================================================
// File: Api/BulkSourceMerge/BulkMTConnectProbeService.cs
// Purpose: Optional operator-triggered per-row MTConnect /probe for the bulk-
//          import wizard. Returns per-row reachable + observation-count
//          informational results; never blocks submit.
//
//          v3.1 sec9 server-side hardening (locked):
//
//            URL handling:
//              * baseUrl validation lives in RowValidators (absolute Uri,
//                scheme http or https). The probe wrapper itself
//                constructs the probe Uri via 'new Uri(baseUri, "probe")',
//                never by string concatenation, so trailing slash + path-
//                mounted agents resolve correctly.
//
//            HTTP client:
//              * 5 s request timeout (hard cap).
//              * 1 MB response cap; reader aborts past that.
//              * No redirects.
//              * No credentials / cookies.
//              * Fixed User-Agent identifying Studio bulk-provision.
//              * Isolated HttpClient per import; not shared with Studio's
//                authenticated client.
//
//            XML parser:
//              * DtdProcessing.Prohibit.
//              * XmlResolver = null.
//              * XInclude disabled (default).
//
//            Parallelism:
//              * Max 5 concurrent probes via SemaphoreSlim. 50-row probe
//                completes in roughly 5-10 s wall-clock.
//
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md sec9
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace ElpisEdgeConnect.Management.Api.BulkSourceMerge;

/// <summary>Per-row probe outcome.</summary>
public enum BulkProbeOutcome
{
    /// <summary>Probe succeeded; <see cref="BulkProbeResult.ObservationCount"/> is meaningful.</summary>
    Reachable,

    /// <summary>HTTP request failed (DNS, refused, network unreachable).</summary>
    Unreachable,

    /// <summary>Request exceeded the 5 s probe budget.</summary>
    Timeout,

    /// <summary>Response exceeded the 1 MB cap and was aborted.</summary>
    ResponseTooLarge,

    /// <summary>Agent returned a non-success HTTP status.</summary>
    HttpError,

    /// <summary>Response body wasn't parseable as MTConnect Probe XML.</summary>
    InvalidXml,

    /// <summary>baseUrl resolved into a non-http/https scheme via redirect; rejected.</summary>
    RedirectSchemeChange,
}

/// <summary>Result of probing one CSV row's baseUrl.</summary>
public sealed record BulkProbeResult
{
    /// <summary>baseUrl from the CSV row, echoed back for UI correlation.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Probe disposition.</summary>
    public required BulkProbeOutcome Outcome { get; init; }

    /// <summary>
    /// Count of <c>&lt;DataItem&gt;</c> elements observed in the Probe XML
    /// response — informational. Null for non-reachable outcomes.
    /// </summary>
    public int? ObservationCount { get; init; }

    /// <summary>HTTP status code when applicable (non-success or redirect change).</summary>
    public int? StatusCode { get; init; }

    /// <summary>Operator-facing detail; populated for non-reachable outcomes.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Seam over the HTTP probe so the service is testable without a live agent.
/// Implementations honor the hardening contract in the file header.
/// </summary>
public interface IBulkMTConnectProbeFetcher
{
    /// <summary>
    /// Issue one hardened GET to <c>{baseUrl}/probe</c>. Returns
    /// (outcome, optional response body, optional status code).
    /// </summary>
    Task<(BulkProbeOutcome Outcome, string? Body, int? StatusCode)> FetchAsync(
        string baseUrl,
        CancellationToken cancellationToken);
}

/// <summary>
/// Service entry point. Probes a batch of rows in parallel (bounded), parses
/// each response under the XXE-safe XmlReaderSettings, returns results.
/// </summary>
public sealed class BulkMTConnectProbeService
{
    /// <summary>Per-probe budget per v3.1 sec9.</summary>
    public static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(5);

    /// <summary>Max response size per v3.1 sec9 (1 MB).</summary>
    public const int MaxResponseBytes = 1 * 1024 * 1024;

    /// <summary>Max concurrent probes per v3.1 sec9.</summary>
    public const int MaxConcurrentProbes = 5;

    /// <summary>User-Agent string operators see in MTConnect agent logs.</summary>
    public const string UserAgent = "EdgeConnect-Studio-BulkProvision/0.1";

    private readonly IBulkMTConnectProbeFetcher _fetcher;

    /// <summary>Construct with the real HTTP fetcher (default) or a test seam.</summary>
    public BulkMTConnectProbeService(IBulkMTConnectProbeFetcher? fetcher = null)
    {
        _fetcher = fetcher ?? new HttpBulkMTConnectProbeFetcher();
    }

    /// <summary>
    /// Probe each baseUrl with bounded parallelism. Probe failures DO NOT
    /// throw — every input produces a <see cref="BulkProbeResult"/> with
    /// the relevant <see cref="BulkProbeOutcome"/>.
    /// </summary>
    public async Task<IReadOnlyList<BulkProbeResult>> ProbeAsync(
        IReadOnlyList<string> baseUrls,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseUrls);
        if (baseUrls.Count == 0)
        {
            return Array.Empty<BulkProbeResult>();
        }

        using var semaphore = new SemaphoreSlim(MaxConcurrentProbes, MaxConcurrentProbes);
        var results = new BulkProbeResult[baseUrls.Count];
        var tasks = baseUrls
            .Select(async (url, idx) =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    results[idx] = await ProbeOneAsync(url, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            })
            .ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<BulkProbeResult> ProbeOneAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var (outcome, body, status) = await _fetcher.FetchAsync(baseUrl, cancellationToken).ConfigureAwait(false);
        if (outcome != BulkProbeOutcome.Reachable || body is null)
        {
            return new BulkProbeResult
            {
                BaseUrl = baseUrl,
                Outcome = outcome,
                StatusCode = status,
                Detail = outcome switch
                {
                    BulkProbeOutcome.Unreachable => "MTConnect agent did not respond. Confirm the URL + port and that the agent is running.",
                    BulkProbeOutcome.Timeout => "Probe exceeded the 5 s budget.",
                    BulkProbeOutcome.ResponseTooLarge => "Probe response exceeded the 1 MB cap.",
                    BulkProbeOutcome.HttpError => $"Agent returned HTTP {status} for /probe.",
                    BulkProbeOutcome.RedirectSchemeChange => "Probe followed a redirect that changed schemes; rejected.",
                    _ => null,
                },
            };
        }

        int count;
        try
        {
            count = CountDataItems(body);
        }
        catch (XmlException ex)
        {
            return new BulkProbeResult
            {
                BaseUrl = baseUrl,
                Outcome = BulkProbeOutcome.InvalidXml,
                StatusCode = status,
                Detail = $"Probe response is not valid MTConnect XML: {ex.Message}",
            };
        }

        return new BulkProbeResult
        {
            BaseUrl = baseUrl,
            Outcome = BulkProbeOutcome.Reachable,
            ObservationCount = count,
            StatusCode = status,
        };
    }

    /// <summary>
    /// Build the canonical probe URL from a baseUrl. Public for tests and
    /// for the real fetcher; uses <c>new Uri(base, "probe")</c> so trailing
    /// slash + path-mounted agents resolve correctly.
    /// </summary>
    public static Uri BuildProbeUri(string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        var withSlash = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        var baseUri = new Uri(withSlash, UriKind.Absolute);
        return new Uri(baseUri, "probe");
    }

    /// <summary>
    /// XmlReaderSettings used to parse Probe XML — DTD prohibited, no
    /// XmlResolver, XInclude disabled. Exposed for tests so they can assert
    /// the hardening matches v3.1 sec9.
    /// </summary>
    public static XmlReaderSettings BuildHardenedXmlReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
    };

    private static int CountDataItems(string body)
    {
        using var reader = XmlReader.Create(new StringReader(body), BuildHardenedXmlReaderSettings());
        var count = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && string.Equals(reader.LocalName, "DataItem", StringComparison.Ordinal))
            {
                count++;
            }
        }
        return count;
    }
}

/// <summary>
/// Real fetcher: builds an isolated <see cref="HttpClient"/> per call with
/// the v3.1 sec9 hardening — 5 s timeout, no redirects, no credentials, no
/// cookies, fixed User-Agent. Reads response into a 1 MB-capped buffer; aborts
/// past that. Detects scheme-change redirects via response headers when the
/// underlying handler emits one despite redirect-following being off.
/// </summary>
internal sealed class HttpBulkMTConnectProbeFetcher : IBulkMTConnectProbeFetcher
{
    public async Task<(BulkProbeOutcome Outcome, string? Body, int? StatusCode)> FetchAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        Uri uri;
        try
        {
            uri = BulkMTConnectProbeService.BuildProbeUri(baseUrl);
        }
        catch (UriFormatException)
        {
            return (BulkProbeOutcome.Unreachable, null, null);
        }

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
        };
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = BulkMTConnectProbeService.ProbeBudget,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(BulkMTConnectProbeService.UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/xml");

        try
        {
            using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            // Reject scheme-changing redirects (we don't follow at all, but
            // surface the situation to the operator).
            if ((int)resp.StatusCode >= 300 && (int)resp.StatusCode < 400 && resp.Headers.Location is not null)
            {
                var location = resp.Headers.Location.IsAbsoluteUri
                    ? resp.Headers.Location
                    : new Uri(uri, resp.Headers.Location);
                if (!string.Equals(location.Scheme, uri.Scheme, StringComparison.Ordinal))
                {
                    return (BulkProbeOutcome.RedirectSchemeChange, null, (int)resp.StatusCode);
                }
            }

            if (!resp.IsSuccessStatusCode)
            {
                return (BulkProbeOutcome.HttpError, null, (int)resp.StatusCode);
            }

            using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var temp = new byte[8192];
            var total = 0;
            int read;
            while ((read = await stream.ReadAsync(temp.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > BulkMTConnectProbeService.MaxResponseBytes)
                {
                    return (BulkProbeOutcome.ResponseTooLarge, null, (int)resp.StatusCode);
                }
                buffer.Write(temp, 0, read);
            }
            var body = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            return (BulkProbeOutcome.Reachable, body, (int)resp.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (BulkProbeOutcome.Timeout, null, null);
        }
        catch (HttpRequestException)
        {
            return (BulkProbeOutcome.Unreachable, null, null);
        }
    }
}
