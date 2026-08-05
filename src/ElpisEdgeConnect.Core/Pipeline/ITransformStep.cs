// ============================================================================
// File: Pipeline/ITransformStep.cs
// Purpose: Contract for a single transform pipeline step.
// Reference: ARCHITECTURE_BLUEPRINT.md §5, PHASE1_EXECUTION_PLAN.md C1
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Pipeline;

/// <summary>
/// One stage of a route's transform pipeline. Implementations may carry
/// per-tag state (e.g., deadband last-value, rate-limit last-emitted
/// timestamp) and are guaranteed to be invoked from a single worker thread
/// per route — implementations do not need internal synchronization.
/// </summary>
/// <remarks>
/// <para>
/// <b>Batch contract.</b> <c>Apply</c> takes the current batch as the
/// <c>input</c> argument and writes surviving (possibly transformed)
/// points into the caller-owned <c>output</c> list, which is cleared
/// before the call.
/// </para>
/// <para>
/// <b>Immutability.</b> Steps must not mutate entries in the input list.
/// To change a point's field, produce a new record via a <c>with</c>
/// expression.
/// </para>
/// <para>
/// <b>Allocation.</b> Pipelines double-buffer the input/output lists
/// between steps, so a step should not allocate a new list per call; it
/// should write into the output list directly.
/// </para>
/// </remarks>
public interface ITransformStep
{
    /// <summary>Stable human-readable name (e.g. <c>"tag-mapping"</c>). Used in metrics tags.</summary>
    string Name { get; }

    /// <summary>Built-in kind identifier.</summary>
    TransformStepKind Kind { get; }

    /// <summary>
    /// Apply the step to <paramref name="input"/>, writing kept points
    /// (possibly transformed) into <paramref name="output"/>.
    /// </summary>
    void Apply(
        IReadOnlyList<CanonicalDataPoint> input,
        List<CanonicalDataPoint> output,
        TransformContext context);
}
