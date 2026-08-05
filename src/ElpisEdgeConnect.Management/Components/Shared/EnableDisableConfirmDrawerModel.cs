// ============================================================================
// File: Components/Shared/EnableDisableConfirmDrawerModel.cs
// Purpose: POCO interaction state machine for the inline Enable/Disable
//          confirm drawer (M.2b.6.1). Razor shell over this model per
//          P2's shared-primitive discipline — the model is unit-testable
//          without bUnit.
//
//          LAYER DISCIPLINE (Locked, v3 review):
//            * planner = pure config reasoning.
//            * API     = orchestration + ordering + telemetry.
//            * model (THIS FILE) = operator interaction state machine.
//                                  Owns: snackbar text, stale-view banner
//                                  pivot, loading-lock state, drain-state
//                                  guard, focus-query deep-link rendering.
//            * telemetry = boundary concern at API layer.
//
//          Locked O — loading discipline:
//            * Primary button enters loading state immediately on Apply
//            * Cancel disables during the request
//            * Drawer cannot close until request resolves
//            * Duplicate submissions ignored (single-fire pattern)
//            * Timeout surfaces a recoverable banner
//
//          Locked G — stale-view pivot:
//            * When the API returns 409 CONFIG.STALE_VIEW, the drawer
//              pivots: primary button text becomes "Refresh"; clicking
//              Refresh re-polls the page and closes the drawer.
//
//          Locked N — operator-facing copy. The model exposes the
//          locked strings via properties; the Razor renders them
//          verbatim.
//
// Reference: docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md §6 (Locked O)
//            docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md §2 (Locked G)
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Wizards;

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>
/// Lifecycle state of the drawer model. Each state has a distinct
/// rendering branch in the Razor shell. Transitions are driven by
/// <see cref="EnableDisableConfirmDrawerModel.Open"/>,
/// <see cref="EnableDisableConfirmDrawerModel.BeginApply"/>,
/// <see cref="EnableDisableConfirmDrawerModel.RecordApplied"/>, etc.
/// </summary>
public enum DrawerState
{
    /// <summary>Drawer is not displayed.</summary>
    Closed,

    /// <summary>Drawer is open; primary action enabled; awaiting operator confirmation.</summary>
    Ready,

    /// <summary>Apply request is in flight. Primary shows spinner; Cancel disabled; close suppressed.</summary>
    Applying,

    /// <summary>Stale-view 409 received; primary text pivoted to "Refresh"; Cancel re-enabled.</summary>
    StaleView,

    /// <summary>Cross-record refusal — drawer shows the blocker list inline.</summary>
    CrossRecord,

    /// <summary>Generic validation error from the apply pipeline; recoverable.</summary>
    ValidationError,

    /// <summary>Request timed out; banner offers Refresh to confirm post-hoc state.</summary>
    Timeout,
}

/// <summary>
/// POCO state machine for the confirm drawer. Razor consumes via
/// two-way state observation; tests drive via the public transitions.
/// </summary>
public sealed class EnableDisableConfirmDrawerModel
{
    // ─── Target context (set on Open) ────────────────────────────────────

    /// <summary>The entity being toggled. Set by <see cref="Open"/>.</summary>
    public ConfigEntityKind Kind { get; private set; }

    /// <summary>Instance id of the target.</summary>
    public string InstanceId { get; private set; } = string.Empty;

    /// <summary>Optional operator-facing display name; falls back to <see cref="InstanceId"/>.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>The desired Enabled value (target of the toggle).</summary>
    public bool DesiredEnabled { get; private set; }

    /// <summary>One-line diff string from the planner (e.g. <c>"Enabled: false → true"</c>).</summary>
    public string DiffSummary { get; private set; } = string.Empty;

    /// <summary>Operator-facing impact warning; non-null when disabling a route.</summary>
    public string? ImpactWarning { get; private set; }

    /// <summary>The configuration version the page observed at open time; used for the stale-view request body.</summary>
    public string? ExpectedConfigurationVersion { get; private set; }

    // ─── State ──────────────────────────────────────────────────────────

    /// <summary>Current state of the drawer state machine.</summary>
    public DrawerState State { get; private set; } = DrawerState.Closed;

    /// <summary>True when the drawer is rendered (any state other than <see cref="DrawerState.Closed"/>).</summary>
    public bool IsOpen => State != DrawerState.Closed;

    /// <summary>True while the apply request is in flight (Locked O).</summary>
    public bool IsApplying => State == DrawerState.Applying;

    /// <summary>True when Cancel is enabled (disabled during Applying per Locked O).</summary>
    public bool IsCancelEnabled => State != DrawerState.Applying;

    /// <summary>True when the primary action button is enabled.</summary>
    public bool IsPrimaryEnabled => State is DrawerState.Ready
                                          or DrawerState.StaleView
                                          or DrawerState.CrossRecord
                                          or DrawerState.ValidationError
                                          or DrawerState.Timeout;

    /// <summary>True when the drawer cannot be dismissed (Locked O — close suppressed during Applying).</summary>
    public bool IsDismissalSuppressed => State == DrawerState.Applying;

    // ─── Result-bearing fields ──────────────────────────────────────────

    /// <summary>Cross-record blocker list — populated when <see cref="State"/> is <see cref="DrawerState.CrossRecord"/>.</summary>
    public IReadOnlyList<DependencyRef> Blockers { get; private set; } = Array.Empty<DependencyRef>();

    /// <summary>Stale-view current version — populated when <see cref="State"/> is <see cref="DrawerState.StaleView"/>.</summary>
    public string? CurrentVersion { get; private set; }

    /// <summary>Generic error message — populated for ValidationError / Timeout.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Audit-record id from the most recent successful Apply (snackbar uses this).</summary>
    public string? LastAuditRecordId { get; private set; }

    // ─── Locked-N operator copy (single source of truth) ────────────────

    /// <summary>Locked-N drawer header text.</summary>
    public string Header => DesiredEnabled
        ? $"Confirm: Enable {Kind.ToString().ToLowerInvariant()} '{InstanceId}'"
        : $"Confirm: Disable {Kind.ToString().ToLowerInvariant()} '{InstanceId}'";

    /// <summary>Primary action button label; pivots to "Refresh" on stale-view per Locked O × Locked G.</summary>
    public string PrimaryLabel
    {
        get
        {
            if (State == DrawerState.StaleView) return "Refresh";
            return DesiredEnabled ? $"Enable {Kind.ToString().ToLowerInvariant()}" : $"Disable {Kind.ToString().ToLowerInvariant()}";
        }
    }

    /// <summary>Snackbar text for the successful-apply outcome (Locked N).</summary>
    public string AppliedSnackbarText => DesiredEnabled
        ? $"{Capitalise(Kind)} '{InstanceId}' enabled."
        : $"{Capitalise(Kind)} '{InstanceId}' disabled.";

    /// <summary>Snackbar text for the no-op outcome — fires WITHOUT opening the drawer (Locked F × Locked N).</summary>
    public string NoOpSnackbarText => DesiredEnabled
        ? $"{Capitalise(Kind)} '{InstanceId}' is already enabled."
        : $"{Capitalise(Kind)} '{InstanceId}' is already disabled.";

    /// <summary>Stale-view banner copy (Locked N).</summary>
    public const string StaleViewBanner = "Configuration changed. Refresh and try again.";

    /// <summary>Timeout banner copy (Locked O).</summary>
    public const string TimeoutBanner = "Request timed out. The change may or may not have applied. Refresh to confirm.";

    // ─── Transitions ────────────────────────────────────────────────────

    /// <summary>
    /// Open the drawer with a target context. Pre-population of impact
    /// metadata comes from the planner result the caller passes in;
    /// the drawer does NOT call the API on open (that happens on the
    /// primary-action click).
    /// </summary>
    public void Open(
        ConfigEntityKind kind,
        string instanceId,
        string? displayName,
        bool desiredEnabled,
        ImpactSummary impact,
        string? expectedConfigurationVersion)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentNullException.ThrowIfNull(impact);

        Kind = kind;
        InstanceId = instanceId;
        DisplayName = displayName;
        DesiredEnabled = desiredEnabled;
        DiffSummary = impact.DiffSummary;
        ImpactWarning = impact.ImpactWarning;
        ExpectedConfigurationVersion = expectedConfigurationVersion;

        Blockers = Array.Empty<DependencyRef>();
        CurrentVersion = null;
        ErrorMessage = null;
        LastAuditRecordId = null;

        State = DrawerState.Ready;
    }

    /// <summary>
    /// Begin the apply request. Locked O: locks Cancel, locks dismissal,
    /// idempotent (re-entrant calls while Applying are ignored).
    /// </summary>
    /// <returns><c>true</c> when the transition fired; <c>false</c> when ignored as a duplicate.</returns>
    public bool BeginApply()
    {
        // Locked O — duplicate-click guard. If we're already applying,
        // the click is a no-op; the button's disabled state is the first
        // line of defence and this is defence-in-depth.
        if (State == DrawerState.Applying) return false;
        if (!IsPrimaryEnabled) return false;
        State = DrawerState.Applying;
        return true;
    }

    /// <summary>Apply succeeded — capture audit id, transition to Closed so the snackbar fires.</summary>
    public void RecordApplied(string auditRecordId)
    {
        LastAuditRecordId = auditRecordId;
        State = DrawerState.Closed;
    }

    /// <summary>Apply returned 409 CONFIG.STALE_VIEW — pivot to refresh prompt.</summary>
    public void RecordStaleView(string? currentVersion)
    {
        CurrentVersion = currentVersion;
        State = DrawerState.StaleView;
    }

    /// <summary>Apply returned 409 CONFIG.CROSS_RECORD_REFUSED — show blocker list.</summary>
    public void RecordCrossRecord(IReadOnlyList<DependencyRef> blockers)
    {
        ArgumentNullException.ThrowIfNull(blockers);
        Blockers = blockers;
        State = DrawerState.CrossRecord;
    }

    /// <summary>Apply returned a validation error — recoverable; primary re-enables.</summary>
    public void RecordValidationError(string message)
    {
        ErrorMessage = message;
        State = DrawerState.ValidationError;
    }

    /// <summary>Apply timed out (Locked O recovery path).</summary>
    public void RecordTimeout()
    {
        ErrorMessage = TimeoutBanner;
        State = DrawerState.Timeout;
    }

    /// <summary>
    /// Operator dismissed the drawer (Cancel button or Esc). Suppressed
    /// during Applying per Locked O.
    /// </summary>
    public bool TryDismiss()
    {
        if (IsDismissalSuppressed) return false;
        State = DrawerState.Closed;
        return true;
    }

    // ─── Deep-link rendering (Locked C.1) ───────────────────────────────

    /// <summary>
    /// Render a focus-query deep link for a dependent. Format is
    /// <c>/{kind}s?focus={id}</c> per Locked C.1 — the list page reads
    /// the query parameter, scrolls to the row, and pulses a highlight.
    /// </summary>
    public static string DependencyDeepLink(DependencyRef dep)
    {
        ArgumentNullException.ThrowIfNull(dep);
        var page = dep.Kind switch
        {
            ConfigEntityKind.Source => "/sources",
            ConfigEntityKind.Sink => "/sinks",
            ConfigEntityKind.Route => "/routes",
            _ => "/",
        };
        return $"{page}?focus={Uri.EscapeDataString(dep.Id)}";
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static string Capitalise(ConfigEntityKind kind) => kind switch
    {
        ConfigEntityKind.Source => "Source",
        ConfigEntityKind.Sink => "Destination",  // Locked N: operator-facing word is "Destination"
        ConfigEntityKind.Route => "Route",
        _ => kind.ToString(),
    };
}
