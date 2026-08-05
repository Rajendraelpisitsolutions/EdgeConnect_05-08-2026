// ============================================================================
// File: Contracts/BulkSourceMerge/BulkSourceMergeDtos.cs
// Purpose: Wire-shape DTOs for the bulk-source-merge preview + submit flow.
//          Per v3.1 §1: submit re-sends the original CSV bytes (server
//          re-parses), so request DTOs carry the raw CSV blob, not the
//          preview's generated source objects.
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md §1
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;

/// <summary>Operator-facing severity for a per-row validation finding.</summary>
public enum BulkSourceMergeSeverity
{
    /// <summary>Informational only (no UI surface in Phase 1).</summary>
    Info = 0,

    /// <summary>Warning — does NOT block submit.</summary>
    Warning = 1,

    /// <summary>Blocker — submit disabled until fixed.</summary>
    Error = 2,
}

/// <summary>
/// One finding (block or warn) from CSV parsing, validation, or merge.
/// </summary>
public sealed record BulkSourceMergeFinding
{
    /// <summary>Stable error code; see <see cref="BulkSourceMergeErrorCode"/>.</summary>
    public required string Code { get; init; }

    /// <summary>Operator-facing message.</summary>
    public required string Message { get; init; }

    /// <summary>Severity classification.</summary>
    public required BulkSourceMergeSeverity Severity { get; init; }

    /// <summary>1-based CSV row index when row-scoped; null when batch-level.</summary>
    public int? CsvRow { get; init; }

    /// <summary>Identifies the offending value when applicable (deviceId, route id, etc.).</summary>
    public string? Subject { get; init; }
}

/// <summary>
/// Preview request — operator uploads CSV + sidecar-equivalent inputs.
/// Per v3.1 §1: <c>SelectedSinkInstanceId</c> is required when 2+ enabled
/// MQTT sinks exist on the gateway; null when there is exactly one
/// (auto-select).
/// </summary>
public sealed record BulkSourceMergePreviewRequest
{
    /// <summary>Protocol picked by operator (state 3 of the wizard).</summary>
    public required BulkSourceMergeProtocol Protocol { get; init; }

    /// <summary>Raw CSV bytes. UTF-8 expected. Size limit enforced by handler.</summary>
    public required byte[] CsvBytes { get; init; }

    /// <summary>
    /// MQTT sink to route new sources to. Required when gateway has 2+ enabled MQTT
    /// sinks. Null when exactly one (service auto-selects).
    /// </summary>
    public string? SelectedSinkInstanceId { get; init; }

    /// <summary>Optional operator-supplied audit label for the batch.</summary>
    public string? ImportLabel { get; init; }
}

/// <summary>Preview response — informational; does not contain generated objects per v3.1 §1.</summary>
public sealed record BulkSourceMergePreviewResponse
{
    /// <summary>
    /// SHA-256 hex of the canonical-JSON serialization of the current applied
    /// gateway config. Submit replays this; mismatch on submit blocks per v3.1 §6.
    /// </summary>
    public required string BaseConfigHash { get; init; }

    /// <summary>The sink that routes will target.</summary>
    public required string ChosenSinkInstanceId { get; init; }

    /// <summary>Number of well-formed CSV rows (after structural parse).</summary>
    public required int ParsedRowCount { get; init; }

    /// <summary>Findings (blockers + warnings). Empty when fully clean.</summary>
    public required IReadOnlyList<BulkSourceMergeFinding> Findings { get; init; }

    /// <summary>True when no <see cref="BulkSourceMergeSeverity.Error"/> findings exist.</summary>
    public required bool CanSubmit { get; init; }
}

/// <summary>
/// Submit request — operator commits. Per v3.1 §1: re-sends original
/// <c>CsvBytes</c> so the server re-parses and re-merges from scratch.
/// Server does NOT trust client-supplied generated source/route objects.
/// </summary>
public sealed record BulkSourceMergeSubmitRequest
{
    /// <summary>Same as <see cref="BulkSourceMergePreviewRequest.Protocol"/>.</summary>
    public required BulkSourceMergeProtocol Protocol { get; init; }

    /// <summary>Same CSV bytes the preview saw.</summary>
    public required byte[] CsvBytes { get; init; }

    /// <summary>Same selection (or null if one MQTT sink).</summary>
    public string? SelectedSinkInstanceId { get; init; }

    /// <summary>Operator-supplied audit label.</summary>
    public string? ImportLabel { get; init; }

    /// <summary>Captured during preview; server rejects on mismatch.</summary>
    public required string BaseConfigHash { get; init; }
}

/// <summary>Submit response — the created draft id, or findings on failure.</summary>
public sealed record BulkSourceMergeSubmitResponse
{
    /// <summary>Draft id created via <c>IConfigurationManager.CreateDraftAsync</c>; null on failure.</summary>
    public string? DraftId { get; init; }

    /// <summary>Findings explaining a refusal; empty on success.</summary>
    public required IReadOnlyList<BulkSourceMergeFinding> Findings { get; init; }

    /// <summary>True when <see cref="DraftId"/> is non-null.</summary>
    public bool Succeeded => DraftId is not null;
}
