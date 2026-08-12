// ============================================================================
// File: Wizards/WizardConfigMerger.cs
// Purpose: Pure, deterministic transformations that produce a new
//          GatewayConfiguration draft from the current config + a new
//          source (or route, or sink) + an explicit routing decision.
//          Protocol-agnostic — consumed by every source / route / sink
//          wizard without modification.
//
//          Method symmetry (Locked G in M.2b.5 v2):
//             * BuildNewSourceDraft  — appends a source + optional new
//                                      route, mirrors M.2b.1's original
//                                      shape (renamed from BuildNewDraft
//                                      at M.2b.5 lock).
//             * BuildNewRouteDraft   — appends a route to an existing
//                                      configuration (M.2b.5).
//             * BuildNewSinkDraft    — appends a sink + optional new
//                                      route (M.2b.6, future).
//
//          Architectural invariants enforced here (NOT just in UI):
//             * Source / route / sink instance ids are unique within a
//               configuration.
//             * No silent route-source replacement. The 'AddToExisting'
//               variant is deliberately absent — RouteConfig.SourceInstanceId
//               is required by Core's contract, so all valid routes
//               already have sources, and overwriting them via a wizard
//               would be operationally dangerous (silent disconnection
//               of a live PLC). See ChatGPT M.2b.1 safety review.
//             * Routes reference real sources and real sinks. Sources
//               referenced by a route must be enabled. (Enforced eagerly
//               here for defence in depth; the management API's
//               CrossRecordValidator enforces the same set lazily at
//               draft-create time.)
//
//          Pure: no HTTP, no DI, no async, no mutation of inputs.
//          Inputs are records — record-with works structurally. The
//          purity is what lets every wizard reuse this merger.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2b.1
//            docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5 (Locked G)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// How a newly-added source should be wired into routing.
/// </summary>
public abstract record RouteWiring
{
    /// <summary>
    /// Operator chose to add the source without wiring it. The source
    /// is created and persisted; routing decisions are deferred to a
    /// future Configuration-page edit. Default for the wizard.
    /// </summary>
    public sealed record NotWired : RouteWiring;

    /// <summary>
    /// Operator chose to create a new route that uses the new source
    /// (when this wiring is supplied to <c>BuildNewSourceDraft</c>) or
    /// the new sink (when supplied to <c>BuildNewSinkDraft</c>, along
    /// with an explicit <see cref="SourceInstanceId"/> picked from
    /// existing sources).
    /// </summary>
    /// <param name="RouteId">Unique route identifier; regex-validated by Core.</param>
    /// <param name="Name">Operator-readable route name.</param>
    /// <param name="Buffer">Per-route buffer policy.</param>
    /// <param name="SinkInstanceIds">
    /// Sinks this route fans out to; must be non-empty. For the sink-
    /// wizard caller, this list MUST include the new sink id (the wizard's
    /// UI enforces it by pre-checking the new sink's row).
    /// </param>
    /// <param name="SourceInstanceId">
    /// Set by sink-wizard callers — the existing source the new route
    /// pulls from. <c>null</c> for source-wizard callers (the merger
    /// supplies the new source's id automatically).
    /// </param>
    public sealed record NewRoute(
        string RouteId,
        string Name,
        BufferPolicyConfig Buffer,
        IReadOnlyList<string> SinkInstanceIds,
        string? SourceInstanceId = null) : RouteWiring;

    /// <summary>Singleton instance of the no-wire choice.</summary>
    public static readonly RouteWiring None = new NotWired();
}

/// <summary>
/// Pure transformations that produce a new draft
/// <see cref="GatewayConfiguration"/> from the current config plus a new
/// source / route / sink. Method names follow the symmetry locked in
/// M.2b.5 v2 (Locked G): <c>BuildNewSourceDraft</c> /
/// <c>BuildNewRouteDraft</c> / <c>BuildNewSinkDraft</c>.
/// </summary>
public static class WizardConfigMerger
{
    /// <summary>
    /// Build a new draft configuration by appending the new source to
    /// the current config and applying the routing decision.
    /// </summary>
    /// <remarks>
    /// Renamed from <c>BuildNewDraft</c> at M.2b.5 lock for symmetry with
    /// <see cref="BuildNewRouteDraft"/> and the future
    /// <c>BuildNewSinkDraft</c> (M.2b.6). Behaviour is identical to the
    /// pre-rename method; only the name changed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">When required arguments are null.</exception>
    /// <exception cref="ArgumentException">
    /// When the new source's instance id collides with an existing source,
    /// or when the routing decision creates a duplicate route id.
    /// </exception>
    public static GatewayConfiguration BuildNewSourceDraft(
        GatewayConfiguration currentConfig,
        SourceInstanceConfig newSource,
        RouteWiring wiring)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(wiring);

        // ── Invariant: source instance id is unique ───────────────────
        foreach (var existing in currentConfig.Sources)
        {
            if (string.Equals(existing.InstanceId, newSource.InstanceId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Source instance id '{newSource.InstanceId}' already exists. " +
                    "Choose a unique instance id.",
                    nameof(newSource));
            }
        }

        // ── Invariant: enabled source must have a route ──────────────
        // Core's startup-time registration (ModbusTcpRegistrationExtensions
        // and analogous registrations for S7 / FOCAS2 / MTConnect) refuses
        // to start the gateway when an enabled source has no referencing
        // enabled route. The merger enforces this in the transformation
        // layer too — defence in depth — so any future wizard / API
        // misuse fails at draft-build time rather than at gateway startup.
        if (wiring is RouteWiring.NotWired && newSource.Enabled)
        {
            throw new ArgumentException(
                $"Source '{newSource.InstanceId}' is enabled but has no route wired. " +
                "Core's startup validator requires every enabled source to be referenced " +
                "by an enabled route. Either set Enabled = false before calling BuildNewSourceDraft, " +
                "or supply a RouteWiring.NewRoute.",
                nameof(newSource));
        }

        // ── Sources: append new source ────────────────────────────────
        var newSources = new List<SourceInstanceConfig>(currentConfig.Sources.Count + 1);
        newSources.AddRange(currentConfig.Sources);
        newSources.Add(newSource);

        // ── Routes: apply wiring decision ─────────────────────────────
        var newRoutes = wiring switch
        {
            RouteWiring.NotWired => (IReadOnlyList<RouteConfig>)currentConfig.Routes,
            RouteWiring.NewRoute cn => CreateNewRoute(currentConfig.Routes, cn, newSource.InstanceId),
            _ => throw new ArgumentException($"Unsupported RouteWiring variant '{wiring.GetType().Name}'.", nameof(wiring)),
        };

        return currentConfig with
        {
            Sources = newSources,
            Routes = newRoutes,
        };
    }

    private static List<RouteConfig> CreateNewRoute(
        IReadOnlyList<RouteConfig> existing,
        RouteWiring.NewRoute wiring,
        string newSourceId)
    {
        // ── Invariant: route id is unique ─────────────────────────────
        foreach (var route in existing)
        {
            if (string.Equals(route.RouteId, wiring.RouteId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Route id '{wiring.RouteId}' already exists. Choose a unique route id.",
                    nameof(wiring));
            }
        }

        if (wiring.SinkInstanceIds.Count == 0)
        {
            throw new ArgumentException(
                "CreateNew route must include at least one sink. " +
                "If no sink should receive the data, choose 'Do not wire yet' instead.",
                nameof(wiring));
        }

        var newRoute = new RouteConfig
        {
            RouteId = wiring.RouteId,
            Name = wiring.Name,
            SourceInstanceId = newSourceId,
            SinkInstanceIds = wiring.SinkInstanceIds,
            Buffer = wiring.Buffer,
            // Filter / Transforms / Delivery / Enabled all use Core's defaults.
        };

        var newList = new List<RouteConfig>(existing.Count + 1);
        newList.AddRange(existing);
        newList.Add(newRoute);
        return newList;
    }

    /// <summary>
    /// Build an updated-source draft by replacing the existing
    /// <see cref="SourceInstanceConfig"/> whose <see cref="SourceInstanceConfig.InstanceId"/>
    /// matches <paramref name="updatedSource"/>'s. Used by the M.2d.2
    /// Edit-mode source wizards. Routes are preserved byte-identical
    /// (see §5.5 of the M.2d.2 v2 plan — Edit mode NEVER modifies routes);
    /// sinks are preserved byte-identical.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pure transformation — optimistic-concurrency (<see cref="EditModeContext.BaseVersionId"/>)
    /// is enforced at the API layer (the caller passes the BaseVersionId
    /// in the wire DTO and the endpoint checks it against
    /// <see cref="ElpisEdgeConnect.Core.Configuration.IConfigurationManager.CurrentVersionId"/>
    /// before calling this merger). Keeping the merger pure is
    /// deliberate — it stays testable without DI.
    /// </para>
    /// <para>
    /// Immutability invariants (M.2d.2 v2 §5.4 mutability table):
    /// <list type="bullet">
    /// <item><see cref="SourceInstanceConfig.InstanceId"/> — must match
    /// an existing source; rename = delete + add, never an Edit.</item>
    /// <item><see cref="SourceInstanceConfig.ProtocolName"/> — cannot
    /// change; switching protocols requires delete + re-add.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Route preservation (M.2d.2 v2 §5.5 locked invariant): the returned
    /// configuration has the same <see cref="GatewayConfiguration.Routes"/>
    /// reference as the input (byte-identical). This holds even when
    /// the source is disabled, the connection changes, the device class
    /// shifts, or the tag list mutates — route changes flow exclusively
    /// through the Route wizard, never as a side effect of Edit on a
    /// source.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">When required arguments are null.</exception>
    /// <exception cref="ArgumentException">
    /// When no source matches <paramref name="updatedSource"/>'s
    /// <see cref="SourceInstanceConfig.InstanceId"/> in the current config,
    /// or when <see cref="SourceInstanceConfig.ProtocolName"/> differs
    /// from the existing source's protocol.
    /// </exception>
    public static GatewayConfiguration BuildUpdatedSourceDraft(
        GatewayConfiguration currentConfig,
        SourceInstanceConfig updatedSource)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        ArgumentNullException.ThrowIfNull(updatedSource);

        // ── Invariant: matching source must exist ─────────────────────
        var index = -1;
        for (var i = 0; i < currentConfig.Sources.Count; i++)
        {
            if (string.Equals(currentConfig.Sources[i].InstanceId, updatedSource.InstanceId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            throw new ArgumentException(
                $"Cannot update source '{updatedSource.InstanceId}' — no source with that " +
                "instance id exists in the current configuration. Edit mode requires the " +
                "source to be present; rename / re-add flows go through Add mode.",
                nameof(updatedSource));
        }

        // ── Invariant: ProtocolName immutable in Edit ─────────────────
        var existing = currentConfig.Sources[index];
        if (!string.Equals(existing.ProtocolName, updatedSource.ProtocolName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Cannot change ProtocolName of source '{updatedSource.InstanceId}' from " +
                $"'{existing.ProtocolName}' to '{updatedSource.ProtocolName}'. Protocol is " +
                "immutable in Edit mode (M.2d.2 v2 §5.4). Delete the source and re-add it " +
                "as the new protocol instead.",
                nameof(updatedSource));
        }

        // ── Replace source at index, preserve everything else ─────────
        var newSources = new List<SourceInstanceConfig>(currentConfig.Sources.Count);
        for (var i = 0; i < currentConfig.Sources.Count; i++)
        {
            newSources.Add(i == index ? updatedSource : currentConfig.Sources[i]);
        }

        // Routes and Sinks intentionally passed through by reference —
        // the byte-identical invariant from §5.5. `with` on the record
        // makes a new GatewayConfiguration but shares the same
        // IReadOnlyList<RouteConfig> / IReadOnlyList<SinkInstanceConfig>.
        return currentConfig with
        {
            Sources = newSources,
        };
    }

    /// <summary>
    /// Build a new draft configuration by appending the new route to the
    /// current config. Sources and sinks remain untouched — the new
    /// route is expected to reference instances that already exist in
    /// <paramref name="currentConfig"/>.
    /// </summary>
    /// <remarks>
    /// Eager defence-in-depth checks (matched lazily by the management
    /// API's <c>CrossRecordValidator</c> at draft-create time):
    /// <list type="bullet">
    /// <item>Route id is unique within the configuration.</item>
    /// <item><see cref="RouteConfig.SourceInstanceId"/> resolves to an existing source.</item>
    /// <item>The referenced source is <see cref="SourceInstanceConfig.Enabled"/>
    /// when the route itself is enabled (matches Core's startup invariant).</item>
    /// <item>Every entry in <see cref="RouteConfig.SinkInstanceIds"/> resolves
    /// to an existing sink — including the case where one of several sinks
    /// is phantom.</item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">When required arguments are null.</exception>
    /// <exception cref="ArgumentException">
    /// When any of the above invariants is violated.
    /// </exception>
    public static GatewayConfiguration BuildNewRouteDraft(
        GatewayConfiguration currentConfig,
        RouteConfig newRoute)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        ArgumentNullException.ThrowIfNull(newRoute);

        // ── Invariant: route id is unique ─────────────────────────────
        foreach (var route in currentConfig.Routes)
        {
            if (string.Equals(route.RouteId, newRoute.RouteId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Route id '{newRoute.RouteId}' already exists. Choose a unique route id.",
                    nameof(newRoute));
            }
        }

        // ── Invariant: source must exist ──────────────────────────────
        SourceInstanceConfig? referencedSource = null;
        foreach (var source in currentConfig.Sources)
        {
            if (string.Equals(source.InstanceId, newRoute.SourceInstanceId, StringComparison.Ordinal))
            {
                referencedSource = source;
                break;
            }
        }
        if (referencedSource is null)
        {
            throw new ArgumentException(
                $"Route '{newRoute.RouteId}' references source '{newRoute.SourceInstanceId}', " +
                "which does not exist in the current configuration.",
                nameof(newRoute));
        }

        // ── Invariant: source must be enabled when the route is enabled ──
        // Mirrors Core's startup invariant — an enabled route pointing at
        // a disabled source would fail registration. We surface the error
        // here so the draft never reaches the runtime.
        if (newRoute.Enabled && !referencedSource.Enabled)
        {
            throw new ArgumentException(
                $"Route '{newRoute.RouteId}' is enabled but its source " +
                $"'{newRoute.SourceInstanceId}' is disabled. Enable the source first " +
                "or create the route as disabled.",
                nameof(newRoute));
        }

        // ── Invariant: every referenced sink must exist ───────────────
        // Fanout integrity: if any sink id is phantom, the whole route is
        // invalid. Verify each id explicitly — not just "at least one".
        var existingSinkIds = new HashSet<string>(
            currentConfig.Sinks.Select(s => s.InstanceId),
            StringComparer.Ordinal);
        foreach (var sinkId in newRoute.SinkInstanceIds)
        {
            if (!existingSinkIds.Contains(sinkId))
            {
                throw new ArgumentException(
                    $"Route '{newRoute.RouteId}' references sink '{sinkId}', " +
                    "which does not exist in the current configuration.",
                    nameof(newRoute));
            }
        }

        // ── Routes: append ────────────────────────────────────────────
        var newRoutes = new List<RouteConfig>(currentConfig.Routes.Count + 1);
        newRoutes.AddRange(currentConfig.Routes);
        newRoutes.Add(newRoute);

        return currentConfig with
        {
            Routes = newRoutes,
        };
    }

    /// <summary>
    /// Build a new draft configuration by appending the new sink to the
    /// current config and applying the routing decision.
    /// </summary>
    /// <remarks>
    /// Symmetric with <see cref="BuildNewSourceDraft"/> (Locked G). The
    /// wiring semantic mirrors the source-wizard's:
    /// <list type="bullet">
    /// <item><see cref="RouteWiring.NotWired"/> forces the new sink to
    /// be created with <see cref="SinkInstanceConfig.Enabled"/> = <c>false</c>
    /// (Locked D-G — Core's startup validator complains about enabled
    /// sinks that no route references).</item>
    /// <item><see cref="RouteWiring.NewRoute"/> appends a new route that
    /// pulls from an existing source (<see cref="RouteWiring.NewRoute.SourceInstanceId"/>
    /// MUST be non-null and resolve to a real source in the config).</item>
    /// </list>
    /// Defence-in-depth invariants enforced eagerly here, mirrored by
    /// the management API's <c>CrossRecordValidator</c> at draft-create time.
    /// </remarks>
    /// <exception cref="ArgumentNullException">When required arguments are null.</exception>
    /// <exception cref="ArgumentException">When any invariant is violated.</exception>
    public static GatewayConfiguration BuildNewSinkDraft(
        GatewayConfiguration currentConfig,
        SinkInstanceConfig newSink,
        RouteWiring wiring)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        ArgumentNullException.ThrowIfNull(newSink);
        ArgumentNullException.ThrowIfNull(wiring);

        // ── Invariant: sink instance id is unique ─────────────────────
        foreach (var existing in currentConfig.Sinks)
        {
            if (string.Equals(existing.InstanceId, newSink.InstanceId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Sink instance id '{newSink.InstanceId}' already exists. " +
                    "Choose a unique instance id.",
                    nameof(newSink));
            }
        }

        // ── Invariant: NotWired forces Enabled=false ──────────────────
        // Defence in depth — Core's startup validator rejects enabled
        // sinks that no route references. The wizard UI is supposed to
        // flip Enabled to false in this branch, but the merger enforces
        // it too so any future caller (API, automation) can't construct
        // an invalid draft.
        if (wiring is RouteWiring.NotWired && newSink.Enabled)
        {
            throw new ArgumentException(
                $"Sink '{newSink.InstanceId}' is enabled but has no route wired. " +
                "Core's startup validator requires every enabled sink to be referenced " +
                "by an enabled route. Either set Enabled = false before calling " +
                "BuildNewSinkDraft, or supply a RouteWiring.NewRoute.",
                nameof(newSink));
        }

        // ── Sinks: append new sink ────────────────────────────────────
        var newSinks = new List<SinkInstanceConfig>(currentConfig.Sinks.Count + 1);
        newSinks.AddRange(currentConfig.Sinks);
        newSinks.Add(newSink);

        // ── Routes: apply wiring decision ─────────────────────────────
        var newRoutes = wiring switch
        {
            RouteWiring.NotWired => (IReadOnlyList<RouteConfig>)currentConfig.Routes,
            RouteWiring.NewRoute cn => CreateNewRouteForSink(currentConfig, cn, newSink.InstanceId),
            _ => throw new ArgumentException($"Unsupported RouteWiring variant '{wiring.GetType().Name}'.", nameof(wiring)),
        };

        return currentConfig with
        {
            Sinks = newSinks,
            Routes = newRoutes,
        };
    }

    /// <summary>
    /// Sink-wizard variant of <see cref="CreateNewRoute"/>. The new
    /// route's source comes from <see cref="RouteWiring.NewRoute.SourceInstanceId"/>
    /// (which MUST be non-null and resolve to an existing source); the
    /// sink ids come from <see cref="RouteWiring.NewRoute.SinkInstanceIds"/>
    /// (which MUST include the new sink the wizard is creating).
    /// </summary>
    /// <summary>
    /// Build an updated-sink draft by replacing the existing
    /// <see cref="SinkInstanceConfig"/> whose <see cref="SinkInstanceConfig.InstanceId"/>
    /// matches <paramref name="updatedSink"/>'s. Used by the M.2d.3
    /// Edit-mode sink wizards. Sources and routes are preserved byte-identically.
    /// </summary>
    /// <remarks>
    /// Immutability invariants (M.2d.3 v2 §3.1):
    /// <list type="bullet">
    /// <item><see cref="SinkInstanceConfig.InstanceId"/> — must match an existing
    /// sink; rename = delete + add, never an Edit.</item>
    /// <item><see cref="SinkInstanceConfig.ProtocolName"/> — cannot change;
    /// switching protocols requires delete + re-add.</item>
    /// </list>
    /// Route preservation: the returned configuration has the same
    /// <see cref="GatewayConfiguration.Routes"/> reference as the input.
    /// Sink edits never modify routing — route changes flow exclusively
    /// through the Route wizard.
    /// </remarks>
    /// <exception cref="ArgumentNullException">When required arguments are null.</exception>
    /// <exception cref="ArgumentException">
    /// When no sink matches <paramref name="updatedSink"/>'s InstanceId, or when
    /// ProtocolName differs from the existing sink's protocol.
    /// </exception>
    public static GatewayConfiguration BuildEditedSinkDraft(
        GatewayConfiguration currentConfig,
        SinkInstanceConfig updatedSink)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        ArgumentNullException.ThrowIfNull(updatedSink);

        // ── Invariant: matching sink must exist ───────────────────────
        var index = -1;
        for (var i = 0; i < currentConfig.Sinks.Count; i++)
        {
            if (string.Equals(currentConfig.Sinks[i].InstanceId, updatedSink.InstanceId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            throw new ArgumentException(
                $"Cannot update sink '{updatedSink.InstanceId}' — no sink with that " +
                "instance id exists in the current configuration. Edit mode requires the " +
                "sink to be present; rename / re-add flows go through Add mode.",
                nameof(updatedSink));
        }

        // ── Invariant: ProtocolName immutable in Edit ─────────────────
        var existing = currentConfig.Sinks[index];
        if (!string.Equals(existing.ProtocolName, updatedSink.ProtocolName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Cannot change ProtocolName of sink '{updatedSink.InstanceId}' from " +
                $"'{existing.ProtocolName}' to '{updatedSink.ProtocolName}'. ProtocolName is " +
                "immutable in Edit mode (M.2d.3 v2 §3.1). Delete the sink and re-add it " +
                "as the new protocol instead.",
                nameof(updatedSink));
        }

        // ── Replace sink at index, preserve sources + routes ─────────
        var newSinks = new List<SinkInstanceConfig>(currentConfig.Sinks.Count);
        for (var i = 0; i < currentConfig.Sinks.Count; i++)
        {
            newSinks.Add(i == index ? updatedSink : currentConfig.Sinks[i]);
        }

        return currentConfig with
        {
            Sinks = newSinks,
        };
    }

    /// <summary>
    /// Build an updated-route draft by replacing the existing
    /// <see cref="RouteConfig"/> whose <see cref="RouteConfig.RouteId"/>
    /// matches <paramref name="updatedRoute"/>'s. Used by the M.2d.3
    /// Edit-mode route wizard. Sources and sinks are preserved byte-identically.
    /// </summary>
    /// <remarks>
    /// Immutability invariants (M.2d.3 v2 §3.1):
    /// <list type="bullet">
    /// <item><see cref="RouteConfig.RouteId"/> — must match an existing route; rename
    /// is not supported (would break buffer paths and diagnostics history).</item>
    /// </list>
    /// Defence-in-depth checks (mirrored by CrossRecordValidator at draft-create time):
    /// <list type="bullet">
    /// <item>Referenced source must exist in <paramref name="currentConfig"/>.</item>
    /// <item>Every referenced sink must exist in <paramref name="currentConfig"/>.</item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">When required arguments are null.</exception>
    /// <exception cref="ArgumentException">
    /// When no route matches <paramref name="updatedRoute"/>'s RouteId, or when any
    /// referenced source or sink does not exist in the current configuration.
    /// </exception>
    public static GatewayConfiguration BuildEditedRouteDraft(
        GatewayConfiguration currentConfig,
        RouteConfig updatedRoute)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        ArgumentNullException.ThrowIfNull(updatedRoute);

        // ── Invariant: matching route must exist ──────────────────────
        var index = -1;
        for (var i = 0; i < currentConfig.Routes.Count; i++)
        {
            if (string.Equals(currentConfig.Routes[i].RouteId, updatedRoute.RouteId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            throw new ArgumentException(
                $"Cannot update route '{updatedRoute.RouteId}' — no route with that id " +
                "exists in the current configuration. Edit mode requires the route to be " +
                "present; rename / re-add flows go through Add mode.",
                nameof(updatedRoute));
        }

        // ── Invariant: referenced source must exist ───────────────────
        var sourceFound = false;
        foreach (var source in currentConfig.Sources)
        {
            if (string.Equals(source.InstanceId, updatedRoute.SourceInstanceId, StringComparison.Ordinal))
            {
                sourceFound = true;
                break;
            }
        }
        if (!sourceFound)
        {
            throw new ArgumentException(
                $"Route '{updatedRoute.RouteId}' references source '{updatedRoute.SourceInstanceId}', " +
                "which does not exist in the current configuration.",
                nameof(updatedRoute));
        }

        // ── Invariant: every referenced sink must exist ───────────────
        var existingSinkIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sink in currentConfig.Sinks)
        {
            existingSinkIds.Add(sink.InstanceId);
        }
        foreach (var sinkId in updatedRoute.SinkInstanceIds)
        {
            if (!existingSinkIds.Contains(sinkId))
            {
                throw new ArgumentException(
                    $"Route '{updatedRoute.RouteId}' references sink '{sinkId}', " +
                    "which does not exist in the current configuration.",
                    nameof(updatedRoute));
            }
        }

        // ── Replace route at index, preserve sources + sinks ─────────
        var newRoutes = new List<RouteConfig>(currentConfig.Routes.Count);
        for (var i = 0; i < currentConfig.Routes.Count; i++)
        {
            newRoutes.Add(i == index ? updatedRoute : currentConfig.Routes[i]);
        }

        return currentConfig with
        {
            Routes = newRoutes,
        };
    }

    private static List<RouteConfig> CreateNewRouteForSink(
        GatewayConfiguration currentConfig,
        RouteWiring.NewRoute wiring,
        string newSinkId)
    {
        // ── Invariant: SourceInstanceId is required and must exist ────
        if (string.IsNullOrWhiteSpace(wiring.SourceInstanceId))
        {
            throw new ArgumentException(
                "RouteWiring.NewRoute used with BuildNewSinkDraft must specify " +
                "SourceInstanceId — the existing source the new route pulls from.",
                nameof(wiring));
        }

        SourceInstanceConfig? referencedSource = null;
        foreach (var src in currentConfig.Sources)
        {
            if (string.Equals(src.InstanceId, wiring.SourceInstanceId, StringComparison.Ordinal))
            {
                referencedSource = src;
                break;
            }
        }
        if (referencedSource is null)
        {
            throw new ArgumentException(
                $"Route '{wiring.RouteId}' references source '{wiring.SourceInstanceId}', " +
                "which does not exist in the current configuration.",
                nameof(wiring));
        }
        if (!referencedSource.Enabled)
        {
            throw new ArgumentException(
                $"Route '{wiring.RouteId}' references source '{wiring.SourceInstanceId}', " +
                "which is disabled. Enable the source first or create the new sink " +
                "without wiring (RouteWiring.NotWired).",
                nameof(wiring));
        }

        // ── Invariant: route id is unique ─────────────────────────────
        foreach (var route in currentConfig.Routes)
        {
            if (string.Equals(route.RouteId, wiring.RouteId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Route id '{wiring.RouteId}' already exists. Choose a unique route id.",
                    nameof(wiring));
            }
        }

        if (wiring.SinkInstanceIds.Count == 0)
        {
            throw new ArgumentException(
                "CreateNew route must include at least one sink.",
                nameof(wiring));
        }

        // ── Invariant: the new sink must appear in the sink list ──────
        // The wizard UI pre-checks the new sink's row, but we enforce it
        // here so the merger can't produce a route that doesn't actually
        // include the sink the operator just created.
        var includesNewSink = false;
        foreach (var sinkId in wiring.SinkInstanceIds)
        {
            if (string.Equals(sinkId, newSinkId, StringComparison.Ordinal))
            {
                includesNewSink = true;
                break;
            }
        }
        if (!includesNewSink)
        {
            throw new ArgumentException(
                $"Route '{wiring.RouteId}' does not include the new sink '{newSinkId}' " +
                "in its SinkInstanceIds. The new route must consume the sink that was " +
                "just added; otherwise the sink would be orphaned.",
                nameof(wiring));
        }

        // ── Invariant: every other referenced sink must already exist ──
        // The new sink hasn't been appended to currentConfig.Sinks yet
        // (that happens after this helper returns), so we exclude it
        // from the "must already exist" check.
        var existingSinkIds = new HashSet<string>(
            currentConfig.Sinks.Select(s => s.InstanceId),
            StringComparer.Ordinal);
        foreach (var sinkId in wiring.SinkInstanceIds)
        {
            if (string.Equals(sinkId, newSinkId, StringComparison.Ordinal)) continue;
            if (!existingSinkIds.Contains(sinkId))
            {
                throw new ArgumentException(
                    $"Route '{wiring.RouteId}' references sink '{sinkId}', " +
                    "which does not exist in the current configuration.",
                    nameof(wiring));
            }
        }

        var newRoute = new RouteConfig
        {
            RouteId = wiring.RouteId,
            Name = wiring.Name,
            SourceInstanceId = wiring.SourceInstanceId!,
            SinkInstanceIds = wiring.SinkInstanceIds,
            Buffer = wiring.Buffer,
        };

        var newList = new List<RouteConfig>(currentConfig.Routes.Count + 1);
        newList.AddRange(currentConfig.Routes);
        newList.Add(newRoute);
        return newList;
    }

    /// <summary>
    /// Build the bundled draft for the Connect-a-device onboarding flow
    /// (ADR-0016 Rule 6). Composes a new source + sink + route into a
    /// single <see cref="GatewayConfiguration"/> with optional gateway-
    /// identity override (from the Welcome step).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Invariants enforced (defence-in-depth — the embedded wizards each
    /// run their own validation, but the merger is the single chokepoint
    /// that sees all three entities together):
    /// </para>
    /// <list type="bullet">
    ///   <item>Source instance id is unique across <c>currentConfig.Sources</c>,
    ///     unless <paramref name="replaceSourceInPlace"/> says this ceremony
    ///     already created it with Save.</item>
    ///   <item>Sink instance id is unique across <c>currentConfig.Sinks</c>,
    ///     unless <paramref name="replaceSinkInPlace"/> says the same.</item>
    ///   <item>Route id is unique across <c>currentConfig.Routes</c>.</item>
    ///   <item>Route's <c>SourceInstanceId</c> matches the new source's <c>InstanceId</c>.</item>
    ///   <item>Route's <c>SinkInstanceIds</c> contains the new sink's <c>InstanceId</c>.</item>
    /// </list>
    /// <para>
    /// Gateway identity override is applied only when supplied and
    /// different from current — the Welcome step in OnboardingFlow may
    /// pass the unchanged values back if the operator didn't edit them.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Any invariant violated.</exception>
    public static GatewayConfiguration BuildBundledOnboardingDraft(
        GatewayConfiguration currentConfig,
        SourceInstanceConfig newSource,
        SinkInstanceConfig newSink,
        RouteConfig newRoute,
        string? gatewayIdOverride = null,
        string? gatewayNameOverride = null,
        bool replaceSourceInPlace = false,
        bool replaceSinkInPlace = false)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(newSink);
        ArgumentNullException.ThrowIfNull(newRoute);

        // ── Cross-entity reference invariants ─────────────────────────
        if (!string.Equals(newRoute.SourceInstanceId, newSource.InstanceId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Route '{newRoute.RouteId}'.SourceInstanceId='{newRoute.SourceInstanceId}' does not match " +
                $"the new source's InstanceId='{newSource.InstanceId}'. The bundled draft requires the route " +
                "to reference the source created in the same ceremony.",
                nameof(newRoute));
        }
        var routeReferencesSink = false;
        foreach (var sinkId in newRoute.SinkInstanceIds)
        {
            if (string.Equals(sinkId, newSink.InstanceId, StringComparison.Ordinal))
            {
                routeReferencesSink = true;
                break;
            }
        }
        if (!routeReferencesSink)
        {
            throw new ArgumentException(
                $"Route '{newRoute.RouteId}'.SinkInstanceIds does not include the new sink's " +
                $"InstanceId='{newSink.InstanceId}'. The bundled draft requires the route to reference " +
                "the sink created in the same ceremony.",
                nameof(newRoute));
        }

        // ── Uniqueness invariants ─────────────────────────────────────
        // "Already exists" means a DIFFERENT entity owns the id. An entity the
        // operator pre-created with Save earlier in this same ceremony is the
        // same entity arriving in its final form, so it is replaced rather than
        // rejected — otherwise pressing Save would make Connect impossible.
        foreach (var existing in currentConfig.Sources)
        {
            if (string.Equals(existing.InstanceId, newSource.InstanceId, StringComparison.Ordinal)
                && !replaceSourceInPlace)
            {
                throw new ArgumentException(
                    $"Source instance id '{newSource.InstanceId}' already exists. Choose a unique id.",
                    nameof(newSource));
            }
        }
        foreach (var existing in currentConfig.Sinks)
        {
            if (string.Equals(existing.InstanceId, newSink.InstanceId, StringComparison.Ordinal)
                && !replaceSinkInPlace)
            {
                throw new ArgumentException(
                    $"Sink instance id '{newSink.InstanceId}' already exists. Choose a unique id.",
                    nameof(newSink));
            }
        }
        foreach (var existing in currentConfig.Routes)
        {
            if (string.Equals(existing.RouteId, newRoute.RouteId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Route id '{newRoute.RouteId}' already exists. Choose a unique id.",
                    nameof(newRoute));
            }
        }

        // ── Optional Welcome-step gateway identity override ──────────
        var gateway = currentConfig.Gateway;
        if (!string.IsNullOrWhiteSpace(gatewayIdOverride)
            && !string.Equals(gatewayIdOverride, gateway.GatewayId, StringComparison.Ordinal))
        {
            gateway = gateway with { GatewayId = gatewayIdOverride };
        }
        if (!string.IsNullOrWhiteSpace(gatewayNameOverride)
            && !string.Equals(gatewayNameOverride, gateway.GatewayName, StringComparison.Ordinal))
        {
            gateway = gateway with { GatewayName = gatewayNameOverride };
        }

        // ── Append (or replace) entities ─────────────────────────────
        // Replacing preserves list ORDER, so a source pre-created by Save keeps
        // its position in the operator's list instead of jumping to the end
        // when Connect commits its final form.
        var sources = new List<SourceInstanceConfig>(currentConfig.Sources.Count + 1);
        sources.AddRange(currentConfig.Sources);
        var sourceSlot = replaceSourceInPlace
            ? sources.FindIndex(s => string.Equals(s.InstanceId, newSource.InstanceId, StringComparison.Ordinal))
            : -1;
        if (sourceSlot >= 0)
        {
            sources[sourceSlot] = newSource;
        }
        else
        {
            sources.Add(newSource);
        }

        var sinks = new List<SinkInstanceConfig>(currentConfig.Sinks.Count + 1);
        sinks.AddRange(currentConfig.Sinks);
        var sinkSlot = replaceSinkInPlace
            ? sinks.FindIndex(s => string.Equals(s.InstanceId, newSink.InstanceId, StringComparison.Ordinal))
            : -1;
        if (sinkSlot >= 0)
        {
            sinks[sinkSlot] = newSink;
        }
        else
        {
            sinks.Add(newSink);
        }

        var routes = new List<RouteConfig>(currentConfig.Routes.Count + 1);
        routes.AddRange(currentConfig.Routes);
        routes.Add(newRoute);

        return currentConfig with
        {
            Gateway = gateway,
            Sources = sources,
            Sinks = sinks,
            Routes = routes,
        };
    }
}
