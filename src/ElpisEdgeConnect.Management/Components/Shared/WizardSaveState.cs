// ============================================================================
// File: WizardSaveState.cs
// Purpose: Save-state passthrough enum for WizardShell. M.2d.1 deliverable —
//          exposes the state through the parameter surface; the M.2d.2/.3
//          milestones decide whether to render a chip / toast / nothing
//          based on it. M.2d.1 itself has no visual side-effect per the Q4
//          verdict in the v2 plan.
// Reference: docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md §5.1
// ============================================================================

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>
/// Save-state passthrough for <see cref="WizardShell"/>. The shell does NOT
/// render any state-specific visual; consumers may bind to this and render
/// their own chip, toast, or banner as the wizard milestone decides.
/// </summary>
public enum WizardSaveState
{
    /// <summary>Default — user is editing; no save in flight.</summary>
    Editing,

    /// <summary>A save POST is in flight.</summary>
    Saving,

    /// <summary>Last save completed successfully.</summary>
    Saved,

    /// <summary>Last save failed.</summary>
    Failed,
}
