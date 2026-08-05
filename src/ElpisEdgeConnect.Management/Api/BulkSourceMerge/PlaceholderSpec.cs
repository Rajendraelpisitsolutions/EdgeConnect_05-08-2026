// ============================================================================
// File: Api/BulkSourceMerge/PlaceholderSpec.cs
// Purpose: Per-template placeholder registry entry. Per v3.1 §2 lock,
//          substitution validates EXPECTED occurrence counts and rejects on
//          mismatch -- this catches broken templates and partial substitution.
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md §2
// ============================================================================

namespace ElpisEdgeConnect.Management.Api.BulkSourceMerge;

/// <summary>
/// One registry entry describing a placeholder the substitution engine
/// expects to see in a given template.
/// </summary>
public sealed record PlaceholderSpec
{
    /// <summary>Placeholder name as it appears between <c>{{ }}</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Position kind in the template (drives escape rules).</summary>
    public required PlaceholderPosition Position { get; init; }

    /// <summary>
    /// Number of times <c>{{ Name }}</c> is expected to appear in the
    /// template body. Mismatch on substitution throws
    /// <c>TemplateSubstitutionCountMismatch</c>.
    /// </summary>
    public required int ExpectedOccurrences { get; init; }
}

/// <summary>Where a placeholder lives in the template JSON text.</summary>
public enum PlaceholderPosition
{
    /// <summary>
    /// Inside a JSON string value (surrounded by <c>"</c>). Operator value gets
    /// JSON-escaped AND brace-encoded before insertion so it can never bleed
    /// into the surrounding JSON structure. Named <c>StringValue</c> (not
    /// <c>String</c>) to avoid CA1720 type-name collision.
    /// </summary>
    StringValue,

    /// <summary>
    /// At a raw token position. Value must validate to a JSON literal
    /// (true, false, integer). Used for <c>"Enabled": {{ enabled }}</c>.
    /// </summary>
    RawBoolean,
}
