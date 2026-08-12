// ============================================================================
// File: Wizards/RouteTransformsEditorModel.cs
// Purpose: In-memory state for the Transforms section of the Add-Route
//          wizard (M.2b.5). Three sub-blocks:
//
//            * Tag Mapping (rename canonical tags)
//            * Deadband     (suppress small changes — UNIFIED table per
//                            M.2b.5 v3 §1 Locked B + UX mockups §1.2)
//            * Rate Limit   (cap publish frequency)
//
//          EnrichmentTags is deliberately OMITTED — Core marks it DORMANT
//          ("values in this map have no runtime effect") until milestone
//          C1.5/C3. Surfacing an editor for a no-op field would mislead
//          operators.
//
//          KEY UX INVARIANT (M.2b.5 v3 plan §3):
//          A single tag can have either Absolute OR Percent deadband but
//          NOT both. The unified DeadbandRow model with Mode radio makes
//          this impossible to construct by accident. Cross-tag mutual
//          exclusion enforced at AddDeadbandRow time (no duplicate Tag).
//
//          KEY VALIDATION DISCIPLINE (M.2b.5 v3 plan Locked I/N):
//          Numeric range checks are enforced eagerly at row-add time
//          (cheap, syntactic). The full cross-record validation
//          (CrossRecordValidator) runs lazily via the
//          POST /api/v1/config/drafts round-trip — the wizard does not
//          re-implement Core's complex rules.
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §1, §5
//            docs/sessions/2026-05-18-mp2b5-mp2b6-ux-mockups.md §1 (Section 6 — Transforms)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Mode for a unified Deadband row — either an Absolute threshold on the
/// numeric value or a Percentage threshold on the relative change.
/// Mutually exclusive by construction: a row has exactly one Mode.
/// </summary>
public enum DeadbandMode
{
    /// <summary>Suppress points whose absolute change is below the threshold.</summary>
    Absolute,

    /// <summary>Suppress points whose proportional change is below the threshold (a fraction in (0, 1]).</summary>
    Percent,
}

/// <summary>
/// One row in the wizard's Tag Mapping table. Two-way bound by Razor.
/// </summary>
public sealed class TagMappingRow
{
    /// <summary>Source-side tag name as it arrives from the adapter.</summary>
    public string SourceTag { get; set; } = string.Empty;

    /// <summary>Canonical tag name emitted to downstream sinks.</summary>
    public string CanonicalTag { get; set; } = string.Empty;
}

/// <summary>
/// One row in the wizard's unified Deadband table. The <see cref="Mode"/>
/// radio enforces "Absolute OR Percent" mutual exclusion at the row level;
/// duplicate-tag prevention at the list level enforces "one mode per tag"
/// across the full editor state.
/// </summary>
public sealed class DeadbandRow
{
    /// <summary>Canonical tag name this rule applies to.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Threshold"/> is an absolute value or a fraction.</summary>
    public DeadbandMode Mode { get; set; } = DeadbandMode.Absolute;

    /// <summary>
    /// Absolute threshold (≥ 0) when <see cref="Mode"/> is
    /// <see cref="DeadbandMode.Absolute"/>; fraction in (0, 1] when
    /// <see cref="Mode"/> is <see cref="DeadbandMode.Percent"/>.
    /// </summary>
    public double Threshold { get; set; }
}

/// <summary>
/// One row in the wizard's Rate Limit table.
/// </summary>
public sealed class RateLimitRow
{
    /// <summary>Canonical tag name this rule applies to.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Minimum interval in milliseconds between published values (must be &gt; 0).</summary>
    public int MinIntervalMs { get; set; }
}

/// <summary>
/// Wizard state for the Transforms section of the Route wizard. Holds
/// three sub-block lists; <see cref="BuildTransformProfileConfig"/>
/// projects them to the canonical <see cref="TransformProfileConfig"/>
/// or returns <c>null</c> when every sub-block is empty (identity route).
/// </summary>
public sealed class RouteTransformsEditorModel
{
    /// <summary>Operator-added tag-mapping rows.</summary>
    public IList<TagMappingRow> TagMappings { get; } = new List<TagMappingRow>();

    /// <summary>Operator-added deadband rows (unified Absolute/Percent table).</summary>
    public IList<DeadbandRow> DeadbandRows { get; } = new List<DeadbandRow>();

    /// <summary>Operator-added rate-limit rows.</summary>
    public IList<RateLimitRow> RateLimitRows { get; } = new List<RateLimitRow>();

    // ─── Eager validation helpers ─────────────────────────────────────────

    /// <summary>
    /// Validate a deadband row at add-row time. Returns null on success,
    /// an operator-facing error message on failure.
    /// </summary>
    /// <remarks>
    /// Eager checks pinned by M.2b.5 v3 §5:
    /// <list type="bullet">
    /// <item>Tag must be non-empty.</item>
    /// <item>Absolute threshold must be finite and ≥ 0.</item>
    /// <item>Percent threshold must be in (0, 1].</item>
    /// <item>Tag must not already be configured (cross-row mutual exclusion).</item>
    /// </list>
    /// Note: the FOURTH check is the operational expression of P4's
    /// "one mode per tag" invariant — a tag can have one Mode, not two.
    /// </remarks>
    public string? ValidateDeadbandRow(DeadbandRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (string.IsNullOrWhiteSpace(row.Tag))
        {
            return "Tag is required.";
        }

        if (!double.IsFinite(row.Threshold))
        {
            return "Threshold must be a finite number.";
        }

        if (row.Mode == DeadbandMode.Absolute && row.Threshold < 0)
        {
            return "Absolute threshold must be ≥ 0.";
        }

        if (row.Mode == DeadbandMode.Percent && (row.Threshold <= 0 || row.Threshold > 1))
        {
            return "Percent threshold must be in (0, 1] (e.g. 0.05 = 5%).";
        }

        if (DeadbandRows.Any(existing => existing != row &&
            string.Equals(existing.Tag, row.Tag, StringComparison.Ordinal)))
        {
            return $"Tag '{row.Tag}' already has a deadband configured. " +
                   "A tag may use only one deadband mode (Absolute OR Percent).";
        }

        return null;
    }

    /// <summary>
    /// Validate a rate-limit row at add-row time.
    /// </summary>
    public string? ValidateRateLimitRow(RateLimitRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (string.IsNullOrWhiteSpace(row.Tag))
        {
            return "Tag is required.";
        }

        if (row.MinIntervalMs <= 0)
        {
            return "Min interval must be strictly positive (milliseconds).";
        }

        if (RateLimitRows.Any(existing => existing != row &&
            string.Equals(existing.Tag, row.Tag, StringComparison.Ordinal)))
        {
            return $"Tag '{row.Tag}' already has a rate limit configured.";
        }

        return null;
    }

    /// <summary>
    /// Validate a tag-mapping row at add-row time.
    /// </summary>
    public string? ValidateTagMappingRow(TagMappingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (string.IsNullOrWhiteSpace(row.SourceTag))
        {
            return "Source tag is required.";
        }

        if (string.IsNullOrWhiteSpace(row.CanonicalTag))
        {
            return "Canonical tag is required.";
        }

        if (TagMappings.Any(existing => existing != row &&
            string.Equals(existing.SourceTag, row.SourceTag, StringComparison.Ordinal)))
        {
            return $"Source tag '{row.SourceTag}' is already mapped.";
        }

        return null;
    }

    // ─── Projection ──────────────────────────────────────────────────────

    /// <summary>
    /// Project the editor state to the canonical
    /// <see cref="TransformProfileConfig"/>. Returns <c>null</c> when
    /// every sub-block is empty (identity route — Core's "no transforms"
    /// shape).
    /// </summary>
    /// <remarks>
    /// Each sub-block collapses to <c>null</c> on the record when its
    /// editor list is empty — matches the canonical "optional sub-block"
    /// semantics. Deadband rows split by <see cref="DeadbandMode"/> into
    /// the two separate dictionaries the Core record expects.
    /// </remarks>
    public TransformProfileConfig? BuildTransformProfileConfig()
    {
        IReadOnlyDictionary<string, string>? tagMapping = null;
        if (TagMappings.Count > 0)
        {
            tagMapping = TagMappings.ToDictionary(r => r.SourceTag, r => r.CanonicalTag, StringComparer.Ordinal);
        }

        IReadOnlyDictionary<string, double>? deadband = null;
        IReadOnlyDictionary<string, double>? deadbandPercent = null;
        var absoluteRows = DeadbandRows.Where(r => r.Mode == DeadbandMode.Absolute).ToList();
        var percentRows = DeadbandRows.Where(r => r.Mode == DeadbandMode.Percent).ToList();
        if (absoluteRows.Count > 0)
        {
            deadband = absoluteRows.ToDictionary(r => r.Tag, r => r.Threshold, StringComparer.Ordinal);
        }
        if (percentRows.Count > 0)
        {
            deadbandPercent = percentRows.ToDictionary(r => r.Tag, r => r.Threshold, StringComparer.Ordinal);
        }

        IReadOnlyDictionary<string, int>? rateLimit = null;
        if (RateLimitRows.Count > 0)
        {
            rateLimit = RateLimitRows.ToDictionary(r => r.Tag, r => r.MinIntervalMs, StringComparer.Ordinal);
        }

        // All sub-blocks empty → identity route (return null per Core's
        // "optional transform profile" shape).
        if (tagMapping is null && deadband is null && deadbandPercent is null && rateLimit is null)
        {
            return null;
        }

        return new TransformProfileConfig
        {
            TagMapping = tagMapping,
            Deadband = deadband,
            DeadbandPercent = deadbandPercent,
            RateLimitMs = rateLimit,
            // EnrichmentTags intentionally omitted (DORMANT in Core).
        };
    }
}
