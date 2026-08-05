// ============================================================================
// File: Wizards/EnableDisablePlanner.cs
// Purpose: Pure planner for Enable/Disable inline toggle (M.2b.6.1).
//          Produces an EnableDisablePlanResult from current configuration
//          + (entity kind, instance id, desired Enabled state).
//
//          LAYER DISCIPLINE (Locked, v3 review):
//             * planner = deterministic config reasoning. No I/O, no
//                         metrics, no UI copy, no runtime-state reads,
//                         no side effects, no logging.
//             * API     = orchestration + ordering + telemetry.
//             * model   = operator interaction state.
//             * telemetry = boundary concern at API layer.
//          If you find yourself wanting the planner to read runtime
//          state, format user-facing copy, or emit a metric — STOP.
//          That work belongs in the API or model layer.
//
//          ATOMICITY (Locked A.1 clarification): the word "atomic" in
//          the milestone plan refers to the OPERATOR INTERACTION MODEL
//          (one click → one outcome), not distributed transactional
//          atomicity. The planner is pure config reasoning; the API
//          orchestrates the existing draft → validate → apply pipeline
//          sequentially. No rollback hook if reload misbehaves;
//          remediation is the existing audit-history page.
//
// Reference: docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md
//            docs/sessions/2026-05-19-mp2b61-implementation-kickoff.md §5
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// The three kinds of configuration entity the inline toggle operates on.
/// Mirrors the three list pages (Sources / Sinks / Routes).
/// </summary>
public enum ConfigEntityKind
{
    /// <summary>A <see cref="SourceInstanceConfig"/>.</summary>
    Source,

    /// <summary>A <see cref="SinkInstanceConfig"/>.</summary>
    Sink,

    /// <summary>A <see cref="RouteConfig"/>.</summary>
    Route,
}

/// <summary>
/// Outcome of the planner's reasoning. The API layer dispatches on this.
/// </summary>
public enum EnableDisablePlanOutcome
{
    /// <summary>Plan produced a new draft to apply.</summary>
    Apply,

    /// <summary>Current state already equals desired state. No draft. (Locked F.)</summary>
    NoOp,

    /// <summary>Plan rejected by cross-record validation; <see cref="EnableDisablePlanResult.Blockers"/> lists the dependents. (Locked C.)</summary>
    CrossRecordRefused,
}

/// <summary>
/// Identifies a configuration entity that blocks (or is blocked by) the
/// requested toggle. Surfaced as a dependent list in the cross-record
/// refusal response.
/// </summary>
/// <param name="Kind">Entity kind — drives the deep-link target (per Locked C.1, <c>/{kind}s?focus=&lt;id&gt;</c>).</param>
/// <param name="Id">Instance id.</param>
/// <param name="Name">Optional human-readable name; falls back to <paramref name="Id"/> in UI.</param>
public sealed record DependencyRef(ConfigEntityKind Kind, string Id, string? Name);

/// <summary>
/// Impact metadata produced alongside every plan result — populated for
/// Apply outcomes so the confirm drawer can render the diff + impact
/// warning without a second planner call. (v2 §6 simplification.)
/// </summary>
/// <param name="DiffSummary">One-line config-diff string (e.g. <c>"Enabled: false → true"</c>).</param>
/// <param name="ImpactWarning">
/// Operator-facing warning when the toggle has a non-trivial runtime
/// effect — populated when disabling a route (data flow stops) per
/// v1 Locked B. <c>null</c> otherwise.
/// </param>
/// <param name="Dependents">
/// Related entities the operator should know about. For Apply outcomes
/// this is informational (e.g. routes that reference a sink being
/// enabled). For <see cref="EnableDisablePlanOutcome.CrossRecordRefused"/>
/// this overlaps with <see cref="EnableDisablePlanResult.Blockers"/>;
/// the API layer surfaces them via the appropriate envelope.
/// </param>
public sealed record ImpactSummary(
    string DiffSummary,
    string? ImpactWarning,
    IReadOnlyList<DependencyRef> Dependents);

/// <summary>
/// Result of <see cref="EnableDisablePlanner.Plan"/>. The shape carries
/// every piece of information the API layer needs to dispatch (outcome),
/// the new draft for the Apply path, the blocker list for the
/// CrossRecordRefused path, and the impact metadata for the drawer UI.
/// </summary>
public sealed record EnableDisablePlanResult(
    EnableDisablePlanOutcome Outcome,
    GatewayConfiguration? Draft,
    IReadOnlyList<DependencyRef> Blockers,
    ImpactSummary Impact);

/// <summary>
/// Pure planner that produces an <see cref="EnableDisablePlanResult"/>
/// from a current <see cref="GatewayConfiguration"/> plus a target
/// (kind, id, desired Enabled state).
/// </summary>
/// <remarks>
/// Mirrors the <see cref="WizardConfigMerger"/> purity discipline: no
/// I/O, no side effects, no mutation of inputs. The API layer is
/// responsible for orchestrating the draft/validate/apply pipeline,
/// emitting telemetry, and mapping outcomes to HTTP status codes.
/// </remarks>
public static class EnableDisablePlanner
{
    /// <summary>
    /// Produce a plan for toggling <paramref name="instanceId"/> of
    /// <paramref name="kind"/> to <paramref name="desiredEnabled"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="current"/> or <paramref name="instanceId"/> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="instanceId"/> is empty/whitespace.</exception>
    /// <exception cref="KeyNotFoundException">
    /// When no entity of <paramref name="kind"/> with id
    /// <paramref name="instanceId"/> exists in <paramref name="current"/>.
    /// The API layer maps this to HTTP 404.
    /// </exception>
    public static EnableDisablePlanResult Plan(
        GatewayConfiguration current,
        ConfigEntityKind kind,
        string instanceId,
        bool desiredEnabled)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(instanceId);
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("Instance id must be non-empty.", nameof(instanceId));
        }

        return kind switch
        {
            ConfigEntityKind.Source => PlanSource(current, instanceId, desiredEnabled),
            ConfigEntityKind.Sink => PlanSink(current, instanceId, desiredEnabled),
            ConfigEntityKind.Route => PlanRoute(current, instanceId, desiredEnabled),
            _ => throw new ArgumentException($"Unknown ConfigEntityKind '{kind}'.", nameof(kind)),
        };
    }

    // ─── Source ─────────────────────────────────────────────────────────

    private static EnableDisablePlanResult PlanSource(
        GatewayConfiguration current, string sourceId, bool desiredEnabled)
    {
        var source = FindSource(current, sourceId);

        if (source.Enabled == desiredEnabled)
        {
            return NoOp(ConfigEntityKind.Source, source.Enabled, desiredEnabled);
        }

        // Cross-record: disabling a source with enabled routes referencing
        // it would leave those routes incoherent (Core's startup invariant).
        if (!desiredEnabled)
        {
            var blockers = current.Routes
                .Where(r => r.Enabled
                            && string.Equals(r.SourceInstanceId, sourceId, StringComparison.Ordinal))
                .Select(r => new DependencyRef(ConfigEntityKind.Route, r.RouteId, r.Name))
                .ToList();
            if (blockers.Count > 0)
            {
                return CrossRecordRefused(
                    diff: $"Enabled: {Format(source.Enabled)} → {Format(desiredEnabled)}",
                    blockers: blockers);
            }
        }

        // Apply: produce a new draft with the source's Enabled flipped.
        var newSources = current.Sources
            .Select(s => string.Equals(s.InstanceId, sourceId, StringComparison.Ordinal)
                ? s with { Enabled = desiredEnabled }
                : s)
            .ToList();

        var draft = current with { Sources = newSources };

        // Informational dependents: enabled routes that reference this source.
        // Useful context for the operator even when Apply (e.g. enabling a
        // source they had wired into a now-disabled route).
        var dependents = current.Routes
            .Where(r => string.Equals(r.SourceInstanceId, sourceId, StringComparison.Ordinal))
            .Select(r => new DependencyRef(ConfigEntityKind.Route, r.RouteId, r.Name))
            .ToList();

        return new EnableDisablePlanResult(
            Outcome: EnableDisablePlanOutcome.Apply,
            Draft: draft,
            Blockers: Array.Empty<DependencyRef>(),
            Impact: new ImpactSummary(
                DiffSummary: $"Enabled: {Format(source.Enabled)} → {Format(desiredEnabled)}",
                ImpactWarning: null,  // Source toggles don't have direct data-flow impact (routes do).
                Dependents: dependents));
    }

    // ─── Sink ───────────────────────────────────────────────────────────

    private static EnableDisablePlanResult PlanSink(
        GatewayConfiguration current, string sinkId, bool desiredEnabled)
    {
        var sink = FindSink(current, sinkId);

        if (sink.Enabled == desiredEnabled)
        {
            return NoOp(ConfigEntityKind.Sink, sink.Enabled, desiredEnabled);
        }

        if (!desiredEnabled)
        {
            var blockers = current.Routes
                .Where(r => r.Enabled
                            && r.SinkInstanceIds.Any(id => string.Equals(id, sinkId, StringComparison.Ordinal)))
                .Select(r => new DependencyRef(ConfigEntityKind.Route, r.RouteId, r.Name))
                .ToList();
            if (blockers.Count > 0)
            {
                return CrossRecordRefused(
                    diff: $"Enabled: {Format(sink.Enabled)} → {Format(desiredEnabled)}",
                    blockers: blockers);
            }
        }

        var newSinks = current.Sinks
            .Select(s => string.Equals(s.InstanceId, sinkId, StringComparison.Ordinal)
                ? s with { Enabled = desiredEnabled }
                : s)
            .ToList();

        var draft = current with { Sinks = newSinks };

        var dependents = current.Routes
            .Where(r => r.SinkInstanceIds.Any(id => string.Equals(id, sinkId, StringComparison.Ordinal)))
            .Select(r => new DependencyRef(ConfigEntityKind.Route, r.RouteId, r.Name))
            .ToList();

        return new EnableDisablePlanResult(
            Outcome: EnableDisablePlanOutcome.Apply,
            Draft: draft,
            Blockers: Array.Empty<DependencyRef>(),
            Impact: new ImpactSummary(
                DiffSummary: $"Enabled: {Format(sink.Enabled)} → {Format(desiredEnabled)}",
                ImpactWarning: null,
                Dependents: dependents));
    }

    // ─── Route ──────────────────────────────────────────────────────────

    private static EnableDisablePlanResult PlanRoute(
        GatewayConfiguration current, string routeId, bool desiredEnabled)
    {
        var route = FindRoute(current, routeId);

        if (route.Enabled == desiredEnabled)
        {
            return NoOp(ConfigEntityKind.Route, route.Enabled, desiredEnabled);
        }

        // Cross-record: enabling a route is blocked if its source is
        // disabled OR any of its sinks is disabled.
        if (desiredEnabled)
        {
            var blockers = new List<DependencyRef>();

            var source = current.Sources
                .FirstOrDefault(s => string.Equals(s.InstanceId, route.SourceInstanceId, StringComparison.Ordinal));
            if (source is null)
            {
                // Phantom source — surface as a blocker so the operator
                // sees what's wrong. The merger / validator would also
                // catch this; we mirror for defence in depth.
                blockers.Add(new DependencyRef(
                    ConfigEntityKind.Source, route.SourceInstanceId, Name: null));
            }
            else if (!source.Enabled)
            {
                blockers.Add(new DependencyRef(
                    ConfigEntityKind.Source, source.InstanceId, source.DeviceName));
            }

            foreach (var sinkId in route.SinkInstanceIds)
            {
                var sink = current.Sinks
                    .FirstOrDefault(s => string.Equals(s.InstanceId, sinkId, StringComparison.Ordinal));
                if (sink is null)
                {
                    blockers.Add(new DependencyRef(
                        ConfigEntityKind.Sink, sinkId, Name: null));
                }
                else if (!sink.Enabled)
                {
                    blockers.Add(new DependencyRef(
                        ConfigEntityKind.Sink, sink.InstanceId, Name: null));
                }
            }

            if (blockers.Count > 0)
            {
                return CrossRecordRefused(
                    diff: $"Enabled: {Format(route.Enabled)} → {Format(desiredEnabled)}",
                    blockers: blockers);
            }
        }

        // Apply: flip Enabled on the route.
        var newRoutes = current.Routes
            .Select(r => string.Equals(r.RouteId, routeId, StringComparison.Ordinal)
                ? r with { Enabled = desiredEnabled }
                : r)
            .ToList();

        var draft = current with { Routes = newRoutes };

        // Impact warning: disabling a route stops data flow until re-enabled.
        var sinkList = string.Join(", ", route.SinkInstanceIds.Select(id => $"'{id}'"));
        string? impactWarning = !desiredEnabled
            ? $"Data flow from '{route.SourceInstanceId}' to {sinkList} will stop until the route is enabled again."
            : null;

        var dependents = new List<DependencyRef>
        {
            new(ConfigEntityKind.Source, route.SourceInstanceId, Name: null),
        };
        foreach (var sinkId in route.SinkInstanceIds)
        {
            dependents.Add(new DependencyRef(ConfigEntityKind.Sink, sinkId, Name: null));
        }

        return new EnableDisablePlanResult(
            Outcome: EnableDisablePlanOutcome.Apply,
            Draft: draft,
            Blockers: Array.Empty<DependencyRef>(),
            Impact: new ImpactSummary(
                DiffSummary: $"Enabled: {Format(route.Enabled)} → {Format(desiredEnabled)}",
                ImpactWarning: impactWarning,
                Dependents: dependents));
    }

    // ─── Lookups ────────────────────────────────────────────────────────

    private static SourceInstanceConfig FindSource(GatewayConfiguration current, string id)
    {
        foreach (var s in current.Sources)
        {
            if (string.Equals(s.InstanceId, id, StringComparison.Ordinal)) return s;
        }
        throw new KeyNotFoundException($"Source '{id}' not found in current configuration.");
    }

    private static SinkInstanceConfig FindSink(GatewayConfiguration current, string id)
    {
        foreach (var s in current.Sinks)
        {
            if (string.Equals(s.InstanceId, id, StringComparison.Ordinal)) return s;
        }
        throw new KeyNotFoundException($"Sink '{id}' not found in current configuration.");
    }

    private static RouteConfig FindRoute(GatewayConfiguration current, string id)
    {
        foreach (var r in current.Routes)
        {
            if (string.Equals(r.RouteId, id, StringComparison.Ordinal)) return r;
        }
        throw new KeyNotFoundException($"Route '{id}' not found in current configuration.");
    }

    // ─── Result helpers ─────────────────────────────────────────────────

    private static EnableDisablePlanResult NoOp(ConfigEntityKind kind, bool current, bool desired)
    {
        // Diff is meaningless for NoOp; encode it as the identity transform
        // so callers that render the diff line still produce something
        // coherent ("Enabled: true → true").
        _ = kind;  // Reserved for future use (e.g. logging context at API layer).
        return new EnableDisablePlanResult(
            Outcome: EnableDisablePlanOutcome.NoOp,
            Draft: null,
            Blockers: Array.Empty<DependencyRef>(),
            Impact: new ImpactSummary(
                DiffSummary: $"Enabled: {Format(current)} → {Format(desired)}",
                ImpactWarning: null,
                Dependents: Array.Empty<DependencyRef>()));
    }

    private static EnableDisablePlanResult CrossRecordRefused(
        string diff, IReadOnlyList<DependencyRef> blockers) =>
        new(
            Outcome: EnableDisablePlanOutcome.CrossRecordRefused,
            Draft: null,
            Blockers: blockers,
            Impact: new ImpactSummary(
                DiffSummary: diff,
                ImpactWarning: null,
                Dependents: blockers));

    private static string Format(bool enabled) => enabled ? "true" : "false";
}
