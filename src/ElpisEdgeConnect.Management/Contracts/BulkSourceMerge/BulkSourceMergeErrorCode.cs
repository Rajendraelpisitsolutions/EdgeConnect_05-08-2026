// ============================================================================
// File: Contracts/BulkSourceMerge/BulkSourceMergeErrorCode.cs
// Purpose: Stable error code identifiers for blockers and warnings.
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md
//            §10 (PR I-1 test list pinning each error code).
// ============================================================================

namespace ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;

/// <summary>
/// Stable identifier strings for blockers and warnings produced by
/// <c>BulkSourceMergeService</c>. Tests assert against these codes; the
/// operator-facing message wording is allowed to evolve, but the code is
/// the contract.
/// </summary>
public static class BulkSourceMergeErrorCode
{
    // ── CSV-level blockers ───────────────────────────────────────────────
    /// <summary>CSV header doesn't match the protocol's required column shape.</summary>
    public const string CsvHeaderShapeInvalid = "BulkProvision.CsvHeaderShapeInvalid";

    /// <summary>Two CSV rows share the same deviceId.</summary>
    public const string CsvDuplicateDeviceId = "BulkProvision.CsvDuplicateDeviceId";

    /// <summary>CSV exceeds the row count limit (v3.1 §8).</summary>
    public const string CsvTooManyRows = "BulkProvision.CsvTooManyRows";

    /// <summary>CSV body cannot be parsed as UTF-8 / RFC 4180.</summary>
    public const string CsvParseFailed = "BulkProvision.CsvParseFailed";

    // ── Per-row blockers ─────────────────────────────────────────────────
    /// <summary>deviceId fails the validation regex.</summary>
    public const string DeviceIdFormatInvalid = "BulkProvision.DeviceIdFormatInvalid";

    /// <summary>Required CSV cell is empty.</summary>
    public const string RowMissingValue = "BulkProvision.RowMissingValue";

    /// <summary>The enabled cell isn't exact "true" or "false".</summary>
    public const string EnabledValueInvalid = "BulkProvision.EnabledValueInvalid";

    /// <summary>MTConnect baseUrl isn't a valid http/https URL.</summary>
    public const string MtConnectBaseUrlInvalid = "BulkProvision.MtConnectBaseUrlInvalid";

    // ── Merge-collision blockers ─────────────────────────────────────────
    /// <summary>Generated Source.InstanceId already exists on gateway.</summary>
    public const string SourceInstanceIdCollision = "BulkProvision.SourceInstanceIdCollision";

    /// <summary>Generated Source.DeviceId already exists on gateway.</summary>
    public const string SourceDeviceIdCollision = "BulkProvision.SourceDeviceIdCollision";

    /// <summary>Generated RouteId already exists on gateway.</summary>
    public const string RouteIdCollision = "BulkProvision.RouteIdCollision";

    // ── Gateway-state blockers ───────────────────────────────────────────
    /// <summary>Gateway has no enabled MQTT sink to route to.</summary>
    public const string NoMqttSink = "BulkProvision.NoMqttSink";

    /// <summary>Multiple enabled MQTT sinks; operator must select one.</summary>
    public const string SinkSelectionRequired = "BulkProvision.SinkSelectionRequired";

    /// <summary>Submitted base config hash doesn't match current applied config (stale preview).</summary>
    public const string BaseConfigHashMismatch = "BulkProvision.BaseConfigHashMismatch";

    // ── Template / substitution / schema blockers ────────────────────────
    /// <summary>Template registry expected count mismatch.</summary>
    public const string TemplateSubstitutionCountMismatch = "BulkProvision.TemplateSubstitutionCountMismatch";

    /// <summary>Unknown {{...}} marker remains after substitution.</summary>
    public const string TemplateResidualMarker = "BulkProvision.TemplateResidualMarker";

    /// <summary>Final merged GatewayConfiguration fails schema validation.</summary>
    public const string MergedConfigSchemaViolation = "BulkProvision.MergedConfigSchemaViolation";

    // ── Warnings (do not block) ──────────────────────────────────────────
    /// <summary>Two rows or row+existing source share the same deviceName.</summary>
    public const string DuplicateDeviceName = "BulkProvision.DuplicateDeviceName";

    /// <summary>Generated Route Name matches an existing route's Name.</summary>
    public const string DuplicateRouteName = "BulkProvision.DuplicateRouteName";

    /// <summary>An unapplied draft already exists for this gateway.</summary>
    public const string UnappliedDraftExists = "BulkProvision.UnappliedDraftExists";
}
