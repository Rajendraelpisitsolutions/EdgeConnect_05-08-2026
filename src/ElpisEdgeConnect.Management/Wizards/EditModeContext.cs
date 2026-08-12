// ============================================================================
// File: Wizards/EditModeContext.cs
// Purpose: Discriminates Add vs Edit mode for a wizard and looks up the
//          existing source / sink / route from a loaded GatewayConfiguration
//          when in Edit mode. M.2d.1 deliverable per v2 plan §5.6.
//
//          Stays under Wizards/ (sibling to Focas2SourceWizardModel etc.)
//          per the Q2 verdict and roadmap v2 §3.7.1 locked placement.
//
//          Pure type: no DI, no async, no mutation. Tested with standard
//          xUnit (no bUnit needed).
//
//          M.2d.2/.3 wire this into each wizard page so an "Edit" entry
//          point can resolve the existing entity and prefill the wizard
//          model. M.2d.1 itself touches no wizard.
// Reference: docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md §5.6
// ============================================================================

using System;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>Add vs Edit discrimination for a wizard session.</summary>
public enum WizardMode
{
    /// <summary>Adding a new source / sink / route.</summary>
    Add,

    /// <summary>Editing an existing source / sink / route identified by <see cref="EditModeContext.ExistingInstanceId"/>.</summary>
    Edit,
}

/// <summary>
/// A wizard's mode context. <see cref="Mode"/> is Add (default) or Edit;
/// when Edit, <see cref="ExistingInstanceId"/> identifies the existing
/// entity by its <c>InstanceId</c> (sources/sinks) or <c>RouteId</c>
/// (routes). Pure value type — no behaviour beyond the three Find lookups
/// against a loaded <see cref="GatewayConfiguration"/>.
/// </summary>
/// <param name="Mode">Add or Edit.</param>
/// <param name="ExistingInstanceId">The existing entity's id when <see cref="Mode"/> is Edit; null otherwise.</param>
/// <param name="BaseVersionId">
/// Optimistic-concurrency token captured at Edit hydration time — the value
/// of <c>IConfigurationManager.CurrentVersionId</c> when the wizard loaded
/// the entity. M.2d.2 §5.5: round-trips through the save request so the
/// server can reject stale saves (409 Conflict + <c>ConfigVersionMismatchDto</c>)
/// when another session has applied a newer config in between. Null in
/// Add mode and when not in use.
/// </param>
public sealed record EditModeContext(WizardMode Mode, string? ExistingInstanceId, string? BaseVersionId = null)
{
    /// <summary>Construct an Add-mode context. <see cref="ExistingInstanceId"/> and <see cref="BaseVersionId"/> are null.</summary>
    public static EditModeContext Add() => new(WizardMode.Add, null, null);

    /// <summary>
    /// Construct an Edit-mode context for the entity identified by
    /// <paramref name="existingInstanceId"/>. The id must be non-null and
    /// non-whitespace. <see cref="BaseVersionId"/> defaults to null; use
    /// <see cref="Edit(string, string)"/> to capture the optimistic-concurrency
    /// token at hydration time.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="existingInstanceId"/> is null, empty, or whitespace.</exception>
    public static EditModeContext Edit(string existingInstanceId)
    {
        if (string.IsNullOrWhiteSpace(existingInstanceId))
        {
            throw new ArgumentException(
                "Edit-mode context requires a non-empty existing instance id.",
                nameof(existingInstanceId));
        }
        return new EditModeContext(WizardMode.Edit, existingInstanceId, null);
    }

    /// <summary>
    /// Construct an Edit-mode context with an optimistic-concurrency token.
    /// Used by <c>SourceEditRouter</c> (M.2d.2 §5.5) when loading an
    /// existing entity for editing: captures <c>IConfigurationManager.CurrentVersionId</c>
    /// so the eventual save can detect stale edits.
    /// </summary>
    /// <param name="existingInstanceId">The existing entity's id; must be non-null and non-whitespace.</param>
    /// <param name="baseVersionId">The current config version id at hydration time. May be null only if the manager has no version yet.</param>
    /// <exception cref="ArgumentException">When <paramref name="existingInstanceId"/> is null, empty, or whitespace.</exception>
    public static EditModeContext Edit(string existingInstanceId, string baseVersionId)
    {
        if (string.IsNullOrWhiteSpace(existingInstanceId))
        {
            throw new ArgumentException(
                "Edit-mode context requires a non-empty existing instance id.",
                nameof(existingInstanceId));
        }
        return new EditModeContext(WizardMode.Edit, existingInstanceId, baseVersionId);
    }

    /// <summary>
    /// True when the context is in Edit mode AND has an existing instance
    /// id. Convenience for wizards.
    /// </summary>
    public bool IsEdit => Mode == WizardMode.Edit && !string.IsNullOrEmpty(ExistingInstanceId);

    /// <summary>
    /// Find the source whose <see cref="SourceInstanceConfig.InstanceId"/>
    /// matches <see cref="ExistingInstanceId"/> in <paramref name="config"/>.
    /// Returns null in Add mode OR when the source isn't present.
    /// </summary>
    public SourceInstanceConfig? FindSource(GatewayConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return IsEdit
            ? config.Sources.FirstOrDefault(s => string.Equals(s.InstanceId, ExistingInstanceId, StringComparison.Ordinal))
            : null;
    }

    /// <summary>
    /// Find the sink whose <see cref="SinkInstanceConfig.InstanceId"/>
    /// matches <see cref="ExistingInstanceId"/> in <paramref name="config"/>.
    /// Returns null in Add mode OR when the sink isn't present.
    /// </summary>
    public SinkInstanceConfig? FindSink(GatewayConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return IsEdit
            ? config.Sinks.FirstOrDefault(s => string.Equals(s.InstanceId, ExistingInstanceId, StringComparison.Ordinal))
            : null;
    }

    /// <summary>
    /// Find the route whose <see cref="RouteConfig.RouteId"/> matches
    /// <see cref="ExistingInstanceId"/> in <paramref name="config"/>.
    /// Returns null in Add mode OR when the route isn't present.
    /// </summary>
    /// <remarks>
    /// Routes use <c>RouteId</c> rather than <c>InstanceId</c>;
    /// <see cref="ExistingInstanceId"/> carries the route id in this
    /// context. The field name reflects the Add/Edit-context concept,
    /// not the route data shape.
    /// </remarks>
    public RouteConfig? FindRoute(GatewayConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return IsEdit
            ? config.Routes.FirstOrDefault(r => string.Equals(r.RouteId, ExistingInstanceId, StringComparison.Ordinal))
            : null;
    }
}
