// ============================================================================
// File: Api/BulkSourceMerge/RowValidators.cs
// Purpose: Per-cell semantic validators that emit row-scoped Findings.
//
//          BulkSourceMergeCsvParser handles structural CSV concerns. These
//          validators run after parsing on each per-row cell value:
//
//            * deviceId       — v3.1 section 5 regex (^[A-Za-z0-9_-]+$) + length 1..64
//            * MtConnect URL  — v3.1 section 9 (http/https agent address; a
//                               bare host or host:port implies http://)
//            * enabled        — v3.1 section 2 strict casing ("true" / "false" exact)
//            * required cell  — v3.1 section 8 strict non-empty for required columns
//
//          Each Validate method returns either null (pass) or a Finding with
//          the matching stable error code from BulkSourceMergeErrorCode. The
//          service composes them per row.
//
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md
//            sections 2, 5, 8, 9
// ============================================================================

using System;
using System.Text.RegularExpressions;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;
using ElpisEdgeConnect.Sources.MTConnect;

namespace ElpisEdgeConnect.Management.Api.BulkSourceMerge;

/// <summary>
/// Per-cell semantic validators. Stateless — each call returns either null
/// (pass) or a row-scoped <see cref="BulkSourceMergeFinding"/>.
/// </summary>
public static class RowValidators
{
    /// <summary>Maximum deviceId length per v3.1 §5.</summary>
    public const int DeviceIdMaxLength = 64;

    private static readonly Regex DeviceIdRegex = new(
        @"^[A-Za-z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Validate a deviceId cell against v3.1 §5: regex
    /// <c>^[A-Za-z0-9_-]+$</c>, length 1..64. Rejects spaces, dots,
    /// slashes, Unicode. No auto-normalization — operators fix the CSV.
    /// </summary>
    public static BulkSourceMergeFinding? ValidateDeviceId(string deviceId, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        if (deviceId.Length == 0)
        {
            return new BulkSourceMergeFinding
            {
                Code = BulkSourceMergeErrorCode.DeviceIdFormatInvalid,
                Severity = BulkSourceMergeSeverity.Error,
                Message = "deviceId is empty. Provide a non-empty identifier matching the [A-Za-z0-9_-] pattern.",
                CsvRow = lineNumber,
                Subject = deviceId,
            };
        }
        if (deviceId.Length > DeviceIdMaxLength)
        {
            return new BulkSourceMergeFinding
            {
                Code = BulkSourceMergeErrorCode.DeviceIdFormatInvalid,
                Severity = BulkSourceMergeSeverity.Error,
                Message = $"deviceId '{Truncate(deviceId)}' is {deviceId.Length} chars; the limit is {DeviceIdMaxLength}.",
                CsvRow = lineNumber,
                Subject = deviceId,
            };
        }
        if (!DeviceIdRegex.IsMatch(deviceId))
        {
            return new BulkSourceMergeFinding
            {
                Code = BulkSourceMergeErrorCode.DeviceIdFormatInvalid,
                Severity = BulkSourceMergeSeverity.Error,
                Message = $"deviceId '{deviceId}' contains characters outside [A-Za-z0-9_-]. Spaces, dots, slashes, and Unicode are not allowed.",
                CsvRow = lineNumber,
                Subject = deviceId,
            };
        }
        return null;
    }

    /// <summary>
    /// Validate an MTConnect baseUrl cell per v3.1 §9: must resolve to an
    /// http/https agent address. A bare host or <c>host:port</c> is accepted and
    /// gets <c>http://</c> implied, matching the adapter, the browse probe and
    /// the wizard. Other schemes (<c>file://</c>, <c>javascript:</c>,
    /// <c>ftp://</c>, etc.) reject.
    /// </summary>
    public static BulkSourceMergeFinding? ValidateMtConnectBaseUrl(string baseUrl, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        if (baseUrl.Length == 0)
        {
            return new BulkSourceMergeFinding
            {
                Code = BulkSourceMergeErrorCode.MtConnectBaseUrlInvalid,
                Severity = BulkSourceMergeSeverity.Error,
                Message = "baseUrl is empty. Provide the MTConnect Agent base URL (e.g. http://192.168.10.51:5000/).",
                CsvRow = lineNumber,
                Subject = baseUrl,
            };
        }
        // Delegates to the same normaliser the adapter, the browse probe and the
        // wizard use, so a CSV accepts exactly what those accept — notably a bare
        // host or host:port, which an operator filling a spreadsheet is far more
        // likely to type than a full URL. Raw Uri.TryCreate was wrong in BOTH
        // directions here: it rejected "192.168.10.51:5000" outright, while
        // "agent.local:5000" sailed through as an absolute URI whose scheme is
        // "agent.local", and the row then failed later against the real agent.
        var normalized = MTConnectSourceConfiguration.TryNormalizeAgentBaseUrl(baseUrl);
        if (normalized is null)
        {
            return new BulkSourceMergeFinding
            {
                Code = BulkSourceMergeErrorCode.MtConnectBaseUrlInvalid,
                Severity = BulkSourceMergeSeverity.Error,
                Message = MTConnectSourceConfiguration.InvalidAgentBaseUrlMessage(baseUrl),
                CsvRow = lineNumber,
                Subject = baseUrl,
            };
        }
        return null;
    }

    /// <summary>
    /// Validate an enabled cell per v3.1 §2: must be exactly <c>"true"</c>
    /// or <c>"false"</c> (case-sensitive). Empty cell, "TRUE", "1", etc.,
    /// all reject.
    /// </summary>
    public static BulkSourceMergeFinding? ValidateEnabledValue(string enabledValue, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(enabledValue);

        if (enabledValue is "true" or "false")
        {
            return null;
        }
        return new BulkSourceMergeFinding
        {
            Code = BulkSourceMergeErrorCode.EnabledValueInvalid,
            Severity = BulkSourceMergeSeverity.Error,
            Message = $"enabled '{Truncate(enabledValue)}' is not exact 'true' or 'false'. Casing matters; no other values accepted.",
            CsvRow = lineNumber,
            Subject = enabledValue,
        };
    }

    /// <summary>
    /// Validate that a required cell (by name) carries a non-empty trimmed
    /// value. Used for deviceName + the address column where emptiness
    /// blocks but a custom semantic check doesn't apply.
    /// </summary>
    public static BulkSourceMergeFinding? ValidateRequiredCellNonEmpty(string cellName, string value, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(cellName);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > 0 && value.Trim().Length > 0)
        {
            return null;
        }
        return new BulkSourceMergeFinding
        {
            Code = BulkSourceMergeErrorCode.RowMissingValue,
            Severity = BulkSourceMergeSeverity.Error,
            Message = $"Required cell '{cellName}' is empty.",
            CsvRow = lineNumber,
            Subject = cellName,
        };
    }

    private static string Truncate(string value, int max = 80) =>
        value.Length <= max ? value : value[..max] + "...";
}
