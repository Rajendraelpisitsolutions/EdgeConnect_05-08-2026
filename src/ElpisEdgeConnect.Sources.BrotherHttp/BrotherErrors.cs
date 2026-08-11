// ============================================================================
// File: BrotherErrors.cs
// Purpose: Canonical Brother HTTP error-code catalog. Codes follow the
//          MODULE.CATEGORY_SUBCATEGORY convention (per CLAUDE.md §7).
//          Adapters, collectors, and the config validator surface these
//          stable identifiers; never raw strings.
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §10 step 9
// ============================================================================

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Stable Brother HTTP error identifiers. The catalog is append-only —
/// renaming a code is a breaking change for any caller that pattern-matches
/// on the string.
/// </summary>
public static class BrotherErrors
{
    /// <summary>
    /// <c>HTTPD_MCNINFO</c> (the health-authority endpoint per Q4) returned
    /// an error or timed out. After
    /// <see cref="BrotherHttpSourceConfiguration.FaultThresholdConsecutiveFailures"/>
    /// consecutive occurrences, the adapter transitions to
    /// <see cref="Core.Adapters.AdapterState.Failed"/>.
    /// </summary>
    public const string HttpUnreachable = "BROTHER.HTTP_UNREACHABLE";

    /// <summary>
    /// One of the non-health endpoints (cycle time / counters / tools /
    /// alarms / maintenance) returned an error. Adapter stays
    /// <see cref="Core.Adapters.AdapterState.Running"/> per Q4 — partial
    /// data emission only; tracked via
    /// <c>elpis_edgeconnect_brother_endpoint_failures_total</c>.
    /// </summary>
    public const string EndpointUnreachable = "BROTHER.ENDPOINT_UNREACHABLE";

    /// <summary>
    /// Endpoint responded with non-empty bytes that failed to parse into
    /// the expected wire shape (typically a Brother firmware change or a
    /// non-Brother device behind the URL).
    /// </summary>
    public const string EndpointParseFailed = "BROTHER.ENDPOINT_PARSE_FAILED";

    /// <summary>
    /// Validation rejection per Q10: the configured
    /// <see cref="Core.Configuration.PollingSettings.IntervalMs"/> is below
    /// <see cref="BrotherHttpSourceConfiguration.PollIntervalHardMinimumMs"/>.
    /// </summary>
    public const string PollTooFast = "BROTHER.POLL_TOO_FAST";

    /// <summary>
    /// Validation warning per Q10: the configured polling interval is
    /// between the hard minimum and
    /// <see cref="BrotherHttpSourceConfiguration.PollIntervalSoftWarningMs"/>.
    /// Accepted but flagged.
    /// </summary>
    public const string PollTooFastWarning = "BROTHER.POLL_TOO_FAST_WARNING";

    /// <summary>
    /// A <c>DataPoints</c> entry doesn't match any catalog leaf or prefix
    /// per <see cref="BrotherTagMap.IsKnownPathOrPrefix"/>.
    /// </summary>
    public const string UnknownDataPoint = "BROTHER.UNKNOWN_DATA_POINT";

    /// <summary>
    /// Validation rejection: configuration is missing a required field
    /// (e.g. <see cref="BrotherHttpSourceConfiguration.BaseUrl"/>).
    /// </summary>
    public const string ConfigMissingField = "BROTHER.CONFIG_MISSING_FIELD";

    /// <summary>
    /// Validation rejection: a configuration value is outside the allowed
    /// range (e.g. negative timeout, negative fault threshold).
    /// </summary>
    public const string ConfigOutOfRange = "BROTHER.CONFIG_OUT_OF_RANGE";
}
