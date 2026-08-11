// ============================================================================
// File: Checklist/IChecklistEvaluator.cs
// Purpose: Single seam for evaluating the commissioning checklist.
//          Composes existing M.1c.* surfaces (diagnostics service +
//          event aggregator + config manager + license manager) into
//          a roll-up of per-check pass/fail/pending/N-A states.
//          Pure composition — no new Core dependencies.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1d
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Contracts;

namespace ElpisEdgeConnect.Management.Checklist;

/// <summary>
/// Evaluates every check in the commissioning catalog and returns a
/// roll-up envelope. Implementations are stateless beyond their
/// dependencies and may be registered as singletons.
/// </summary>
public interface IChecklistEvaluator
{
    /// <summary>
    /// Run every check in the catalog and return the consolidated
    /// response. Never throws for individual check failures — a check
    /// that throws becomes a <c>Fail</c> with the exception message in
    /// <c>Detail</c>, so a single broken evaluator method doesn't
    /// 500 the entire checklist endpoint.
    /// </summary>
    Task<ChecklistResponse> EvaluateAsync(CancellationToken ct);
}
