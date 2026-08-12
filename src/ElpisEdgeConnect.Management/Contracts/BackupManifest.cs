// ============================================================================
// File: Contracts/BackupManifest.cs
// Purpose: Wire-shape of manifest.json — the operator- and tooling-
//          facing index of what a backup archive contains. Pairs with
//          the redacted gateway.json, audit.log, and history/* files
//          shipped inside the zip.
//
//          Schema rules:
//             * SchemaVersion locks the format. Importers reject
//               unknown values to avoid loose interpretation of future
//               extensions.
//             * BackupType narrows what's included
//               (configuration-only today; configuration+certificates
//               and full-secure-export in future schema versions).
//             * Redactions are keyed by zip-relative filename so restore
//               tooling can prompt for missing secrets file-by-file
//               rather than re-scanning every entry.
//             * Checksums use the OCI-style "<algorithm>:<hex>" prefix
//               convention (e.g. "sha256:e3b0c4…") so future
//               algorithm migration is wire-compatible without changing
//               field shape.
//             * Capabilities are deliberately NOT shipped in schema
//               version 1 — schemaVersion + backupType already cover
//               compatibility. Future versions may add a capabilities
//               object; consumers should treat its absence as "no
//               advanced capabilities advertised."
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.3
//            ARCHITECTURE_BLUEPRINT.md §17.7 (Audit integrity)
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Operator- and tooling-facing index of an EdgeConnect backup archive.
/// Serialized as <c>manifest.json</c> at the root of the zip.
/// </summary>
public sealed record BackupManifest
{
    /// <summary>
    /// Manifest format version. Importers reject unknown values to avoid
    /// loose interpretation of future extensions. Bump on breaking change.
    /// </summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// What kind of backup this is. Values:
    ///   <c>"configuration-only"</c>     — current shape (gateway.json + audit + history, secrets redacted, no certs).
    /// Future values (NOT emitted today):
    ///   <c>"configuration+certificates"</c> — adds encrypted certificate material.
    ///   <c>"full-secure-export"</c>          — full encrypted archive.
    /// </summary>
    public required string BackupType { get; init; }

    /// <summary>
    /// Version of the redaction engine (<c>ConfigRedactionEngine</c>) that
    /// produced this artifact's redactions, per ADR-0020 R-3. Lets a support
    /// engineer reason about why a field was masked or stripped independently
    /// of <see cref="SchemaVersion"/> — tier outcomes can change between engine
    /// revisions without a manifest-schema change.
    /// </summary>
    public required int RedactionEngineVersion { get; init; }

    /// <summary>EdgeConnect runtime version emitting the backup. From assembly.</summary>
    public required string ProductVersion { get; init; }

    /// <summary>Gateway identity at export time (same convention as DiagnosticsEventDto.GatewayId).</summary>
    public required string GatewayId { get; init; }

    /// <summary>Operator-readable gateway name; null if unset in gateway config.</summary>
    public string? GatewayName { get; init; }

    /// <summary>UTC timestamp the zip was assembled.</summary>
    public required DateTime CreatedUtc { get; init; }

    /// <summary>
    /// Actor identity. <c>"system"</c> today (auth context not wired yet);
    /// populated from <c>HttpContext.User</c> once auth lands.
    /// </summary>
    public required string ExportedBy { get; init; }

    /// <summary>
    /// Why the backup was created. Lets support tooling and compliance
    /// reviewers distinguish a planned export from a panic-button capture.
    /// </summary>
    /// <remarks>
    /// Conventional values, no enum to avoid lock-in:
    ///   <c>"manual"</c>          — operator clicked Download in the Studio (today).
    ///   <c>"scheduled"</c>       — backup scheduler ran (future).
    ///   <c>"pre-upgrade"</c>     — captured by the installer before a runtime upgrade (future).
    ///   <c>"support-request"</c> — captured at a support engineer's request (future).
    ///   <c>"automated"</c>       — captured by an external orchestration tool (future).
    /// Producers should prefer existing values; consumers should treat
    /// unknown values as opaque strings.
    /// </remarks>
    public required string ExportReason { get; init; }

    /// <summary>What's in the bundle. Lets importers pre-check capability.</summary>
    public required BackupComponents Components { get; init; }

    /// <summary>
    /// Map of zip-relative filename → list of JSONPath-ish field paths
    /// within that file whose values were replaced with
    /// <c>"&lt;REDACTED&gt;"</c>. Provenance preserved so restore
    /// tooling can prompt for missing secrets per-file rather than
    /// re-scanning every entry.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Redactions { get; init; }

    /// <summary>
    /// Map of zip-relative filename → content hash in the OCI-style
    /// <c>"&lt;algorithm&gt;:&lt;hex&gt;"</c> prefix format (e.g.
    /// <c>"sha256:e3b0c4…"</c>). Importers parse the prefix to identify
    /// the algorithm; unknown algorithms should be treated as
    /// "checksum not verifiable" rather than as corruption. A future
    /// schemaVersion may promote this to a structured object if richer
    /// metadata (truncation, HMAC mode, etc.) becomes necessary;
    /// consumers should accept either shape.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Checksums { get; init; }

    /// <summary>Number of historical configuration versions included in <c>history/*.json</c>.</summary>
    public int HistoryCount { get; init; }

    /// <summary>Number of entries shipped in <c>audit.log</c>.</summary>
    public long AuditEntryCount { get; init; }

    /// <summary>
    /// Non-fatal issues during export (e.g. <c>"audit log truncated at
    /// entry 12 — chain broken"</c>, <c>"history file v3 missing on
    /// disk"</c>). Null when the export completed cleanly. Operator
    /// surfaces these in the support bundle.
    /// </summary>
    public IReadOnlyList<string>? Warnings { get; init; }
}

/// <summary>
/// Describes what components are present in a backup archive.
/// Boolean flags so future capability extensions don't require a schema
/// version bump (a new flag added with default false is wire-compatible).
/// </summary>
public sealed record BackupComponents
{
    /// <summary>True when <c>gateway.json</c> is included (redacted).</summary>
    public required bool Configuration { get; init; }

    /// <summary>True when <c>audit.log</c> is included.</summary>
    public required bool Audit { get; init; }

    /// <summary>True when <c>history/*.json</c> files are included (redacted).</summary>
    public required bool History { get; init; }

    /// <summary>
    /// True when certificate material is included. Always false in
    /// schemaVersion=1 (BackupType "configuration-only" excludes certs).
    /// Reserved for future BackupType "configuration+certificates".
    /// </summary>
    public required bool Certificates { get; init; }
}
