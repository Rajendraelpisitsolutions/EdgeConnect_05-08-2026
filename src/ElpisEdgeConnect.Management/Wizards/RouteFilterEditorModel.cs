// ============================================================================
// File: Wizards/RouteFilterEditorModel.cs
// Purpose: In-memory state for the Filter section of the Add-Route wizard
//          (M.2b.5). Two glob-pattern lists — Include + optional Exclude.
//
//          KEY DISCIPLINE (M.2b.5 v3 plan §1 Locked I + §5):
//          Glob-pattern validation calls Core's GlobMatcher.Compile() —
//          the wizard never re-implements glob syntax rules. Composition,
//          not duplication.
//
//          Default Include is ["*"] per blueprint §4.4 and the runtime
//          TagFilterConfig default. Operators can remove the wildcard
//          to make the filter restrictive.
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
//            docs/sessions/2026-05-18-mp2b5-mp2b6-ux-mockups.md §1 (Section 5 — Filter)
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Pipeline.Steps;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Wizard state for the Filter section of the Route wizard. Razor binds
/// two-way to <see cref="Include"/> and <see cref="Exclude"/>; the operator
/// adds and removes glob patterns. <see cref="BuildTagFilterConfig"/>
/// projects to the canonical <see cref="TagFilterConfig"/>.
/// </summary>
public sealed class RouteFilterEditorModel
{
    /// <summary>
    /// Default Include list — matches the Core default (every tag passes
    /// through unless explicitly excluded).
    /// </summary>
    public static IReadOnlyList<string> DefaultInclude { get; } = new[] { "*" };

    /// <summary>
    /// Glob patterns of tags to include. Defaults to <c>["*"]</c> matching
    /// the runtime <see cref="TagFilterConfig"/>. Operators can remove
    /// patterns (including <c>"*"</c>) to make the route restrictive.
    /// </summary>
    public IList<string> Include { get; } = new List<string> { "*" };

    /// <summary>
    /// Glob patterns of tags to exclude. Empty by default. Applied AFTER
    /// <see cref="Include"/>; null is projected to "no exclusions" rather
    /// than an empty list to match the canonical record shape.
    /// </summary>
    public IList<string> Exclude { get; } = new List<string>();

    /// <summary>
    /// Validate a glob pattern at add-row time using Core's
    /// <see cref="GlobMatcher.Compile"/>. Returns null on success, an
    /// operator-facing error message on failure. Composes Core's
    /// validation per M.2b.5 v3 plan Locked I; never re-implements
    /// glob syntax rules.
    /// </summary>
    public static string? ValidatePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "Pattern cannot be empty.";
        }

        try
        {
            // GlobMatcher.Compile throws ArgumentException on empty/whitespace;
            // any other syntax issue surfaces as the same exception type, so
            // wrapping the message gives the operator a consistent surface.
            _ = GlobMatcher.Compile(pattern);
            return null;
        }
        catch (ArgumentException ex)
        {
            return $"Invalid glob pattern: {ex.Message}";
        }
    }

    /// <summary>
    /// Project to the canonical <see cref="TagFilterConfig"/>.
    /// </summary>
    /// <remarks>
    /// Empty Include collapses to the runtime default <c>["*"]</c> — the
    /// wizard never emits a logically-empty Include list, since that
    /// would block every tag from flowing through the route (almost
    /// certainly not what the operator wants). The defensive collapse
    /// mirrors `TagFilterConfig`'s own default.
    ///
    /// Empty Exclude projects to <c>null</c> on the record (matches the
    /// canonical "no exclusions" shape, distinct from an empty list).
    /// </remarks>
    public TagFilterConfig BuildTagFilterConfig()
    {
        var include = Include.Count > 0
            ? new List<string>(Include)
            : new List<string>(DefaultInclude);

        // Exclude: null when empty, list otherwise.
        IReadOnlyList<string>? exclude = Exclude.Count > 0
            ? new List<string>(Exclude)
            : null;

        return new TagFilterConfig
        {
            Include = include,
            Exclude = exclude,
        };
    }
}
