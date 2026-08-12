// ============================================================================
// File: Wizards/OnboardingRouteWiring.cs
// Purpose: The Connect-a-device flow's cross-entity wiring rule, stated once,
//          in a form the UI can ask BEFORE the apply instead of discovering at
//          apply time.
//
//          WizardConfigMerger.BuildBundledOnboardingDraft throws when the route
//          does not reference the source and sink created in the same ceremony.
//          That throw is correct and stays — it is the last line of defence —
//          but it lands after the operator has filled in every step, and it
//          names a parameter ('newRoute'), not a step. This helper mirrors the
//          merger's two reference invariants exactly so the flow can gate the
//          route step on them and say which endpoint disagrees.
//
//          Pure: no I/O, no injected services, no framework types. That is what
//          lets both OnboardingFlow and ReviewAndConnect share ONE definition
//          of the rule rather than each carrying a copy that can drift.
// Reference: docs/decisions/0016-onboarding-meta-wizard.md Rule 6
//            WizardConfigMerger.BuildBundledOnboardingDraft (the rule mirrored)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Answers whether an onboarding route actually points at the source and
/// destination configured in the same ceremony, and describes the disagreement
/// in the operator's terms when it does not.
/// </summary>
/// <remarks>
/// <para>
/// The rule mirrored here is the merger's, verbatim:
/// </para>
/// <list type="bullet">
///   <item><c>route.SourceInstanceId</c> equals the source's <c>InstanceId</c>
///     (ordinal — instance ids are case-sensitive everywhere else in config).</item>
///   <item><c>route.SinkInstanceIds</c> <em>contains</em> the sink's
///     <c>InstanceId</c>. Contains, not equals: a route may fan out to several
///     destinations (blueprint §3), and the merger only requires that the
///     ceremony's own destination is among them. A fan-out route that still
///     includes it is correct and must not be blocked.</item>
/// </list>
/// <para>
/// The defect this exists to close: the route wizard captured the source and
/// destination ids once per mount, and onboarding steps stay mounted across
/// Back/Next (ADR-0016 Rule 2), so renaming an endpoint after the route step
/// had been visited left the route holding the old id. Nothing asked the
/// question again until the apply refused it. Everything here is therefore
/// computed from the arguments on every call — there is deliberately no state
/// to cache, because a cached answer is precisely what caused the defect.
/// </para>
/// </remarks>
public static class OnboardingRouteWiring
{
    /// <summary>
    /// True when <paramref name="route"/> satisfies both of the bundled draft's
    /// cross-entity reference invariants against <paramref name="source"/> and
    /// <paramref name="sink"/> — i.e. when the apply will not reject it.
    /// </summary>
    /// <remarks>
    /// A null argument returns <c>false</c>. The question this answers is "is
    /// this safe to POST?", and that cannot be true of a ceremony missing one of
    /// its three entities. Note the deliberate asymmetry with
    /// <see cref="DescribeMismatch"/>, which returns <c>null</c> for the same
    /// input: an endpoint that does not exist yet is not a <em>disagreement</em>
    /// to explain, it is a step the operator has not finished, already gated
    /// elsewhere and better left un-nagged.
    /// </remarks>
    /// <param name="route">The route the route step built, or null if it isn't valid yet.</param>
    /// <param name="source">The source the source step built, or null.</param>
    /// <param name="sink">The destination the destination step built, or null.</param>
    public static bool IsWiredTo(RouteConfig? route, SourceInstanceConfig? source, SinkInstanceConfig? sink)
    {
        if (route is null || source is null || sink is null)
        {
            return false;
        }

        return ReferencesSource(route, source) && ReferencesSink(route, sink);
    }

    /// <summary>
    /// One operator-facing sentence naming what the route fails to reference, or
    /// <c>null</c> when the three agree (or when there is nothing yet to compare
    /// — see <see cref="IsWiredTo"/> for why those two collapse to the same
    /// answer here).
    /// </summary>
    /// <remarks>
    /// Every branch names the id the route holds AND the id the operator
    /// configured, then points at the step that can fix it. The merger's own
    /// message is accurate but reads as
    /// <c>Parameter 'newRoute'</c> — no help to someone deciding which of six
    /// steps to go back to.
    /// <para>
    /// The destination branch says "tick" rather than "pick": the likeliest way
    /// to get here with a valid-looking route is un-ticking the ceremony's
    /// destination inside the route wizard's fan-out list. That is an operator
    /// choice, so it is reported, never silently re-ticked.
    /// </para>
    /// </remarks>
    /// <param name="route">The route the route step built, or null if it isn't valid yet.</param>
    /// <param name="source">The source the source step built, or null.</param>
    /// <param name="sink">The destination the destination step built, or null.</param>
    public static string? DescribeMismatch(RouteConfig? route, SourceInstanceConfig? source, SinkInstanceConfig? sink)
    {
        if (route is null || source is null || sink is null)
        {
            return null;
        }

        var sourceOk = ReferencesSource(route, source);
        var sinkOk = ReferencesSink(route, sink);

        return (sourceOk, sinkOk) switch
        {
            (true, true) => null,
            (false, true) =>
                $"It reads from '{route.SourceInstanceId}', but the source you configured is "
                    + $"'{source.InstanceId}'. Go back to the route step and pick it again.",
            (true, false) =>
                $"It delivers to {DescribeSinks(route)}, but the destination you configured is "
                    + $"'{sink.InstanceId}'. Go back to the route step and tick '{sink.InstanceId}' "
                    + "in the destinations list.",
            _ =>
                $"It reads from '{route.SourceInstanceId}' and delivers to {DescribeSinks(route)}, but "
                    + $"the pair you configured is '{source.InstanceId}' → '{sink.InstanceId}'. Go back "
                    + "to the route step and pick them again.",
        };
    }

    /// <summary>Ordinal id match — the merger compares these the same way.</summary>
    private static bool ReferencesSource(RouteConfig route, SourceInstanceConfig source) =>
        string.Equals(route.SourceInstanceId, source.InstanceId, StringComparison.Ordinal);

    /// <summary>
    /// Membership, not equality — a fan-out route carrying other destinations is
    /// legitimate as long as the ceremony's own one is among them.
    /// </summary>
    private static bool ReferencesSink(RouteConfig route, SinkInstanceConfig sink) =>
        SinkIds(route).Contains(sink.InstanceId, StringComparer.Ordinal);

    /// <summary>
    /// Renders the route's destination list for the message. An empty list gets
    /// words rather than an empty pair of quotes, which reads as a rendering bug.
    /// </summary>
    private static string DescribeSinks(RouteConfig route)
    {
        var ids = SinkIds(route);
        return ids.Count == 0
            ? "no destination at all"
            : "'" + string.Join("', '", ids) + "'";
    }

    /// <summary>
    /// The route's destination ids, never null. <c>SinkInstanceIds</c> is a
    /// non-nullable required member, but a route can also arrive here straight
    /// off a JSON deserialiser that happily leaves it null — and this helper's
    /// whole job is to answer rather than throw.
    /// </summary>
    private static IReadOnlyList<string> SinkIds(RouteConfig route)
    {
        IReadOnlyList<string>? ids = route.SinkInstanceIds;
        return ids ?? Array.Empty<string>();
    }
}
