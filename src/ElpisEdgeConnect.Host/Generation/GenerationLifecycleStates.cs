// ============================================================================
// File: Generation/GenerationLifecycleStates.cs
// Purpose: The two orthogonal generation state axes (review §8): publish
//          authority vs retirement/cleanup outcome — kept separate rather than
//          collapsed into one enum. Plus the quiescence vocabulary (review B3).
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §3, §8.
// Slice 0 — commit 2 scaffolding (unused).
// ============================================================================

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>Whether a generation may affect current runtime state.</summary>
internal enum AuthorityState
{
    Authorized = 0,
    Retired = 1,
}

/// <summary>The cleanup/retirement outcome of a generation, orthogonal to <see cref="AuthorityState"/>.</summary>
internal enum RetirementState
{
    /// <summary>Not retired, or retired with no outstanding cleanup.</summary>
    None = 0,

    /// <summary>Retired and isolated while bounded cleanup is attempted.</summary>
    Quarantined = 1,

    /// <summary>Quarantined work still physically active past the cleanup deadline.</summary>
    Orphaned = 2,

    /// <summary>Cleanup proven complete.</summary>
    Completed = 3,
}

/// <summary>State of one quiescence component (e.g. pump, adapter-stop, callback-drain).</summary>
internal enum QuiescenceComponentState
{
    Active = 0,
    Proven = 1,
    Unproven = 2,

    /// <summary>The component does not apply to this generation (e.g. callback-drain for a poll adapter).</summary>
    NotApplicable = 3,
}

/// <summary>Which quiescence components apply to a generation; declared at construction (review C2-3).</summary>
[System.Flags]
internal enum QuiescenceComponents
{
    None = 0,
    Pump = 1,
    AdapterStop = 2,
    CallbackDrain = 4,
}

/// <summary>Aggregate evidence that a retired generation has stopped doing work.</summary>
internal enum QuiescenceEvidence
{
    /// <summary>At least one component is still active.</summary>
    Active = 0,

    /// <summary>No component is active, but at least one cannot be proven stopped.</summary>
    Unproven = 1,

    /// <summary>Every component is proven stopped.</summary>
    Proven = 2,
}
