// ============================================================================
// File: Pipeline/Steps/FilterStep.cs
// Purpose: Include / exclude points by glob matching against TagPath.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4, §5, PHASE1_EXECUTION_PLAN.md C1
//
// D5 decision: matching target is CanonicalDataPoint.TagPath (not TagName).
// Filter runs AFTER TagMapping so renames are already applied; matching on
// TagPath gives stable identity regardless of rename.
// D6 decision: supported glob tokens are *, ?, ** only. No regex.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Pipeline.Steps;

/// <summary>
/// Drop points whose <see cref="CanonicalDataPoint.TagPath"/> does not
/// match the include list, or matches the exclude list.
/// </summary>
/// <remarks>
/// <para>
/// Matching is performed against <c>TagPath</c> rather than <c>TagName</c>
/// (per C1 D5 decision) so filtering is stable after tag mapping renames
/// a point.
/// </para>
/// <para>
/// A point with null or whitespace <c>TagPath</c> is a contract violation
/// upstream and causes a loud <see cref="LicenseException"/>-style failure;
/// see <see cref="Apply"/>.
/// </para>
/// </remarks>
public sealed class FilterStep : ITransformStep
{
    private readonly GlobMatcher[] _include;
    private readonly GlobMatcher[] _exclude;

    /// <summary>Build a filter step from raw include / exclude pattern lists.</summary>
    public FilterStep(IReadOnlyList<string> include, IReadOnlyList<string>? exclude)
    {
        ArgumentNullException.ThrowIfNull(include);
        _include = include.Count == 0
            ? new[] { GlobMatcher.MatchAll }
            : CompileAll(include);
        _exclude = exclude is null || exclude.Count == 0
            ? Array.Empty<GlobMatcher>()
            : CompileAll(exclude);
    }

    /// <inheritdoc/>
    public string Name => "filter";

    /// <inheritdoc/>
    public TransformStepKind Kind => TransformStepKind.Filter;

    /// <summary>True when this step is the identity filter (<c>*</c> include, no exclude).</summary>
    public bool IsIdentity =>
        _exclude.Length == 0
        && _include.Length == 1
        && ReferenceEquals(_include[0], GlobMatcher.MatchAll);

    /// <inheritdoc/>
    public void Apply(
        IReadOnlyList<CanonicalDataPoint> input,
        List<CanonicalDataPoint> output,
        TransformContext context)
    {
        for (int i = 0; i < input.Count; i++)
        {
            var point = input[i];

            // Fail loudly on contract violation — blueprint A1 mandates
            // TagPath is always populated.
            if (string.IsNullOrWhiteSpace(point.TagPath))
            {
                throw new ConfigurationException(
                    CoreErrors.PipelineInvalidFilterPattern,
                    $"FilterStep received a point with null/empty TagPath for tag '{point.TagName}' on route '{context.RouteId}'. " +
                    "TagPath is required by the canonical data model contract (A1).");
            }

            if (!MatchesAny(_include, point.TagPath))
            {
                continue;
            }
            if (MatchesAny(_exclude, point.TagPath))
            {
                continue;
            }
            output.Add(point);
        }
    }

    private static bool MatchesAny(GlobMatcher[] patterns, string path)
    {
        for (int i = 0; i < patterns.Length; i++)
        {
            if (patterns[i].IsMatch(path))
            {
                return true;
            }
        }
        return false;
    }

    private static GlobMatcher[] CompileAll(IReadOnlyList<string> patterns)
    {
        var compiled = new GlobMatcher[patterns.Count];
        for (int i = 0; i < patterns.Count; i++)
        {
            compiled[i] = GlobMatcher.Compile(patterns[i]);
        }
        return compiled;
    }
}
