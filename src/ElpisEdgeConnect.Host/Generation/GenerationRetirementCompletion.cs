// ============================================================================
// File: Generation/GenerationRetirementCompletion.cs
// Purpose: The composite quiescence contract (review B3, C2-3). PumpTask
//          completion is NOT proof a native/socket worker stopped, so
//          replacement admission keys off the aggregate of the APPLICABLE
//          components (declared at construction) — pump + adapter-stop/dispose +
//          callback-drain — each Active / Unproven / Proven. An omitted/
//          not-applicable component is explicit (NotApplicable) and never
//          silently counts as proof; a never-proven applicable component stays
//          Active and so can never yield Proven.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §3.
// Slice 0 — commit 2 scaffolding (unused).
// ============================================================================

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>
/// Aggregates the applicable quiescence components of a retiring generation into
/// a single <see cref="QuiescenceEvidence"/>: <c>Active</c> if any component is
/// still active; otherwise <c>Unproven</c> if any applicable component cannot be
/// proven stopped; otherwise <c>Proven</c> (all applicable components proven,
/// the rest <see cref="QuiescenceComponentState.NotApplicable"/>).
/// </summary>
internal sealed class GenerationRetirementCompletion
{
    private readonly object _sync = new();
    private QuiescenceComponentState _pump;
    private QuiescenceComponentState _adapterStop;
    private QuiescenceComponentState _callbackDrain;

    /// <summary>Declare which components apply to this generation; the rest are <c>NotApplicable</c>.</summary>
    public GenerationRetirementCompletion(QuiescenceComponents applicable)
    {
        _pump = Initial(applicable, QuiescenceComponents.Pump);
        _adapterStop = Initial(applicable, QuiescenceComponents.AdapterStop);
        _callbackDrain = Initial(applicable, QuiescenceComponents.CallbackDrain);
    }

    /// <summary>Set the supervisor-pump completion component.</summary>
    public void SetPump(QuiescenceComponentState state)
    {
        lock (_sync) { Assign(ref _pump, state); }
    }

    /// <summary>Set the adapter stop/dispose completion component.</summary>
    public void SetAdapterStop(QuiescenceComponentState state)
    {
        lock (_sync) { Assign(ref _adapterStop, state); }
    }

    /// <summary>Set the callback/work-drain completion component.</summary>
    public void SetCallbackDrain(QuiescenceComponentState state)
    {
        lock (_sync) { Assign(ref _callbackDrain, state); }
    }

    /// <summary>The aggregate evidence across the applicable components.</summary>
    public QuiescenceEvidence Evidence
    {
        get
        {
            lock (_sync)
            {
                if (IsAny(QuiescenceComponentState.Active))
                {
                    return QuiescenceEvidence.Active;
                }
                if (IsAny(QuiescenceComponentState.Unproven))
                {
                    return QuiescenceEvidence.Unproven;
                }
                return QuiescenceEvidence.Proven; // all Proven or NotApplicable
            }
        }
    }

    private static QuiescenceComponentState Initial(QuiescenceComponents set, QuiescenceComponents component)
        => set.HasFlag(component) ? QuiescenceComponentState.Active : QuiescenceComponentState.NotApplicable;

    private static void Assign(ref QuiescenceComponentState field, QuiescenceComponentState state)
    {
        if (field == QuiescenceComponentState.NotApplicable)
        {
            throw new InvalidOperationException(
                "Cannot set a quiescence component that is not applicable to this generation.");
        }
        if (state == QuiescenceComponentState.NotApplicable)
        {
            throw new ArgumentException(
                "Applicability is fixed at construction; a component cannot be set to NotApplicable.",
                nameof(state));
        }
        field = state;
    }

    private bool IsAny(QuiescenceComponentState state)
        => _pump == state || _adapterStop == state || _callbackDrain == state;
}
