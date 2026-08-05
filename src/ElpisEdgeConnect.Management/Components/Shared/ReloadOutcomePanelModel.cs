// ============================================================================
// File: Components/Shared/ReloadOutcomePanelModel.cs
// Purpose: Pure view-model for the M.P2.2 phase 3 reload-outcome panel.
//          The Razor component is a thin binding shell over this model
//          so the panel's logic (status branches, chip rows, diagnostics
//          links, banner text) is unit-testable without bUnit.
//
//          Observation only (phase 3 guardrail M): the view-model
//          carries DATA from the DTO; it never invokes orchestration
//          actions. Adding "retry" or "rollback" affordances on the
//          panel would break the architectural pin in §1 of the
//          phase 3 plan v2.
// Reference: docs/sessions/2026-05-16-mp22-phase3-plan.md (v2 locked)
// Milestone: M.P2.2 phase 3
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Management.Contracts.Config;

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>
/// View-model for the reload-outcome panel. Wraps a
/// <see cref="ReloadOutcomeDto"/> (or null) and exposes the computed
/// properties the Razor template binds to.
/// </summary>
/// <remarks>
/// A <c>null</c> <see cref="Outcome"/> is the wire signal that
/// runtime-reload observation is unavailable — the panel hides itself
/// entirely. <see cref="Visible"/> captures this so the Razor template
/// has a single check at the root.
/// </remarks>
public sealed class ReloadOutcomePanelModel
{
    /// <summary>The DTO this model wraps. May be null.</summary>
    public ReloadOutcomeDto? Outcome { get; }

    /// <summary>Construct a model from an outcome DTO (may be null).</summary>
    public ReloadOutcomePanelModel(ReloadOutcomeDto? outcome)
    {
        Outcome = outcome;
    }

    /// <summary>True when the panel should render at all (DTO is not null).</summary>
    public bool Visible => Outcome is not null;

    /// <summary>Wire status string (<c>"Completed"</c> / <c>"InProgress"</c> / <c>"Skipped"</c>), or null when hidden.</summary>
    public string? Status => Outcome?.Status;

    /// <summary>True when <see cref="Status"/> is <c>"Completed"</c>.</summary>
    public bool IsCompleted =>
        string.Equals(Outcome?.Status, "Completed", StringComparison.Ordinal);

    /// <summary>True when <see cref="Status"/> is <c>"InProgress"</c> — reconcile still in flight.</summary>
    public bool IsInProgress =>
        string.Equals(Outcome?.Status, "InProgress", StringComparison.Ordinal);

    /// <summary>True when <see cref="Status"/> is <c>"Skipped"</c> — superseded by a newer apply.</summary>
    public bool IsSkipped =>
        string.Equals(Outcome?.Status, "Skipped", StringComparison.Ordinal);

    /// <summary>Instances credited as freshly Applied (Add action). Empty for non-Completed.</summary>
    public IReadOnlyList<string> AppliedInstances =>
        Outcome?.AppliedInstances ?? Array.Empty<string>();

    /// <summary>Instances credited as Restarted (Modified action). Empty for non-Completed.</summary>
    public IReadOnlyList<string> RestartedInstances =>
        Outcome?.RestartedInstances ?? Array.Empty<string>();

    /// <summary>Per-instance faulted entries with their error codes.</summary>
    public IReadOnlyList<FaultedReloadEntryDto> FaultedInstances =>
        Outcome?.FaultedInstances ?? Array.Empty<FaultedReloadEntryDto>();

    /// <summary>For <see cref="IsSkipped"/>, the id of the version that superseded this one.</summary>
    public string? SupersededByVersion => Outcome?.SupersededBy;

    /// <summary>Reconcile elapsed milliseconds, or null when no outcome.</summary>
    public long? ElapsedMs => Outcome?.ElapsedMs;

    /// <summary>Operator-facing elapsed-time label (e.g. <c>"137 ms"</c>), empty when no outcome.</summary>
    public string ElapsedMsDisplay =>
        Outcome is null ? string.Empty : $"{Outcome.ElapsedMs} ms";

    /// <summary>
    /// Banner text shown when <see cref="IsInProgress"/> — directs the
    /// operator at the durable diagnostics surface for the terminal
    /// state.
    /// </summary>
    public const string InProgressMessage = "Reconcile still in flight — see Diagnostics for the terminal status.";

    /// <summary>
    /// Banner text shown when <see cref="IsSkipped"/>. Carries the
    /// <c>SupersededBy</c> id so the operator can correlate it to the
    /// version-history surface.
    /// </summary>
    public string SkippedMessage =>
        $"Superseded by version {SupersededByVersion ?? "?"} before reconcile ran.";

    /// <summary>
    /// Anchor link to the diagnostics page focused on the given
    /// instance id. Used by faulted-chip click handlers in the
    /// Razor template.
    /// </summary>
    public static string DiagnosticsHrefFor(string instanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        return $"/diagnostics#{instanceId}";
    }
}
