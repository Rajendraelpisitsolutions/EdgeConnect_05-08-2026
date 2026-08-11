// ============================================================================
// File: Pipeline/Steps/TagMappingStep.cs
// Purpose: Rename source tags to canonical names. Points whose TagName is
//          not in the map pass through unchanged.
// Reference: ARCHITECTURE_BLUEPRINT.md §5, PHASE1_EXECUTION_PLAN.md C1
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Pipeline.Steps;

/// <summary>
/// First step in the blueprint-§5 pipeline. Renames
/// <see cref="CanonicalDataPoint.TagName"/> using a lookup table; the
/// original tag name is preserved in
/// <see cref="CanonicalDataPoint.OriginalTagName"/>. Unmapped tags pass
/// through byte-for-byte.
/// </summary>
public sealed class TagMappingStep : ITransformStep
{
    private readonly FrozenDictionary<string, string> _map;

    /// <summary>Create a step from an immutable source-name → canonical-name map.</summary>
    public TagMappingStep(IReadOnlyDictionary<string, string> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public string Name => "tag-mapping";

    /// <inheritdoc/>
    public TransformStepKind Kind => TransformStepKind.TagMapping;

    /// <inheritdoc/>
    public void Apply(
        IReadOnlyList<CanonicalDataPoint> input,
        List<CanonicalDataPoint> output,
        TransformContext context)
    {
        for (int i = 0; i < input.Count; i++)
        {
            var point = input[i];
            if (_map.TryGetValue(point.TagName, out var canonical))
            {
                // Preserve original name if the caller has not already set one.
                output.Add(point with
                {
                    TagName = canonical,
                    OriginalTagName = point.OriginalTagName ?? point.TagName,
                });
            }
            else
            {
                output.Add(point);
            }
        }
    }
}
