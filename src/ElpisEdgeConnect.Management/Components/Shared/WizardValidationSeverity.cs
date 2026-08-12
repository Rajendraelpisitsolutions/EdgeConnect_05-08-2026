// ============================================================================
// File: WizardValidationSeverity.cs
// Purpose: Severity enum carried by WizardValidationBanner. M.2d.1
//          deliverable per v2 plan §5.3. Mapped internally to MudBlazor
//          Severity; this wrapper lets future wizards consume the
//          primitive without taking a hard dependency on MudBlazor types
//          in their POCO models.
// Reference: docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md §5.3
// ============================================================================

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>
/// Severity classification for messages rendered by
/// <see cref="WizardValidationBanner"/>.
/// </summary>
public enum WizardValidationSeverity
{
    /// <summary>Informational message (light blue banner).</summary>
    Info,

    /// <summary>Warning — non-blocking issue the operator should review (amber banner).</summary>
    Warning,

    /// <summary>Error — blocks save (red banner). Default.</summary>
    Error,
}
