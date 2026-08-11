// ============================================================================
// File: Bundle/BundleManifest.cs
// Purpose: Wire shapes for the two top-level metadata files a diagnostic bundle
//          carries: manifest.json (per-entry checksums + redaction provenance,
//          grouped by tier) and bundle-info.json (bundle-level provenance +
//          contributor list). Mirrors the convention established by the backup
//          BackupManifest but is bundle-specific.
// Reference: docs/sessions/2026-05-31-adr0020-bundle-generation-plan-v2.md §5;
//            docs/decisions/0020-diagnostic-bundle-redaction-spec.md Rules 3/4.
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Bundle;

/// <summary>
/// Per-entry index of a bundle archive (<c>manifest.json</c>): which entries were
/// included, their checksums, the redaction paths within each, the EXCLUDE notes,
/// and any non-blocking warnings (missing protocol rules, secret-shape detector
/// hits). The composer builds this; contributors never touch it.
/// </summary>
public sealed record BundleManifest
{
    /// <summary>Zip-relative entry name → content hash (<c>"sha256:&lt;hex&gt;"</c>).</summary>
    public required IReadOnlyDictionary<string, string> Checksums { get; init; }

    /// <summary>Zip-relative entry name → JSONPath-ish paths redacted within it.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Redactions { get; init; }

    /// <summary>Surfaces deliberately excluded (Rule 1), as <c>"&lt;surface&gt;: &lt;reason&gt;"</c>.</summary>
    public required IReadOnlyList<string> Excluded { get; init; }

    /// <summary>
    /// Non-blocking provenance warnings (missing protocol rules; secret-shape
    /// detector hits), prefixed with the entry they came from.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Bundle-level provenance header (<c>bundle-info.json</c>, Rule 4). Distinct
/// from <see cref="BundleManifest"/> (per-entry): this is the "what is this
/// bundle" summary a support engineer reads first.
/// </summary>
public sealed record BundleInfo
{
    /// <summary>Version of the bundle format. Older bundles surface as "older format" rather than crash a loader.</summary>
    public required int BundleSpecVersion { get; init; }

    /// <summary>Redaction-engine revision that produced the redactions (R-3).</summary>
    public required int RedactionEngineVersion { get; init; }

    /// <summary>Gateway identity at generation time.</summary>
    public required string GatewayId { get; init; }

    /// <summary>UTC generation timestamp (ISO 8601).</summary>
    public required DateTime BundleGeneratedAtUtc { get; init; }

    /// <summary>EdgeConnect build that generated the bundle.</summary>
    public required string GeneratorVersion { get; init; }

    /// <summary>Operator/session identity that initiated generation (matches the audit trail).</summary>
    public required string BundlerInvokedBy { get; init; }

    /// <summary>Optional operator-supplied reason for generating (e.g. a support ticket id). Null when not given.</summary>
    public string? Reason { get; init; }

    /// <summary>Counts of fields included / masked / stripped, and surfaces excluded.</summary>
    public required RedactionSummary RedactionSummary { get; init; }

    /// <summary>The contributors that ran (name + capability), so support sees exactly what is inside.</summary>
    public required IReadOnlyList<BundleContributorInfo> Contributors { get; init; }
}

/// <summary>Aggregate redaction counts for the whole bundle.</summary>
public sealed record RedactionSummary
{
    /// <summary>Entries included verbatim or with INCLUDE-tier fields.</summary>
    public required int IncludedEntries { get; init; }

    /// <summary>Fields masked across the bundle.</summary>
    public required int MaskedFields { get; init; }

    /// <summary>Fields stripped across the bundle.</summary>
    public required int StrippedFields { get; init; }

    /// <summary>Surfaces excluded from the bundle (Rule 1).</summary>
    public required int ExcludedSurfaces { get; init; }
}

/// <summary>A contributor that ran, as recorded in <see cref="BundleInfo.Contributors"/>.</summary>
public sealed record BundleContributorInfo
{
    /// <summary>Contributor id (e.g. <c>"config"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Capability category (string form of <see cref="BundleCapability"/>).</summary>
    public required string Capability { get; init; }
}

/// <summary>
/// A structured secret-shape detector hit surfaced on the bundle preview so the
/// Studio can name the exact field the operator must acknowledge (ADR-0020 R-1).
/// The value is INCLUDE'd verbatim — the detector only flags it for review.
/// </summary>
public sealed record BundleSecretShapeWarning
{
    /// <summary>Zip-relative entry the flagged value lives in (e.g. <c>gateway.json</c>).</summary>
    public required string File { get; init; }

    /// <summary>JSONPath-ish path of the flagged value within that entry.</summary>
    public required string Path { get; init; }

    /// <summary>Human-readable detector verdict (e.g. "looks like a JWT").</summary>
    public required string Detail { get; init; }
}
