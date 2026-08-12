// ---------------------------------------------------------------------------
// File:    Components/Shared/RouteHealth.cs
// Purpose: One verdict on "is this route actually delivering data?", shared by
//          the Overview route cards, the Overview health banner, and anything
//          else that summarises a gateway's state.
//
// Why this exists
// ---------------
// A route's own StateName describes the route PIPELINE, not its endpoints. A
// route whose source adapter is Stopped and whose MQTT sink is disconnected
// still reports StateName "Running" — the pipeline task is alive, it just has
// nothing to read and nowhere to publish. Rendering that as a green RUNNING
// chip told an operator everything was fine while no data moved at all.
//
// Endpoint health is therefore computed from the SOURCE and SINK adapter states
// as well as the route's, and every surface that answers "does anything need
// me?" routes through here so the card, the banner and the status footer can
// never contradict each other.
//
// The verdict AND its explanation come from one ordered walk (see Verdict): the
// condition that decides the level is the same one that supplies the sentence,
// so a route can never be judged broken by one condition and explained by
// another — or, as it could before, by nothing at all.
//
// Reference: platform principle P6 (operational product, not developer tool).
// ---------------------------------------------------------------------------

using ElpisEdgeConnect.Management.Contracts;

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>How much of an operator's attention a route currently deserves.</summary>
public enum RouteHealthLevel
{
    /// <summary>Source reading, every destination publishing.</summary>
    Healthy = 0,

    /// <summary>Delivering, but retrying or otherwise not at full health.</summary>
    Degraded = 1,

    /// <summary>An endpoint is down — data is not flowing end to end.</summary>
    Down = 2,
}

/// <summary>
/// A level and the reason that produced it, as one value.
/// <para>
/// The two travel together on purpose. When the level and the sentence were
/// computed by separate passes, a card could go red off one condition and print
/// the explanation of another — the operator then reconciles two answers to the
/// same question. One walk, one verdict, one sentence.
/// </para>
/// </summary>
/// <param name="Level">How much attention the route deserves.</param>
/// <param name="Reason">
/// Operator-facing sentence for the condition that set <paramref name="Level"/>.
/// Null only when the level is <see cref="RouteHealthLevel.Healthy"/> — a route
/// that is not healthy always carries a reason.
/// </param>
/// <param name="ErrorCode">
/// The adapter code behind <paramref name="Reason"/>, when the deciding
/// condition had one. Null for the conditions judged from counters or
/// configuration (a missing destination, a wedged buffer), which is exactly
/// when a caller must not print an empty code chip.
/// </param>
/// <param name="FromAdapterError">
/// True when an adapter's own reported error decided the level. The route card
/// already prints a fault panel for every reported error, so this is how it
/// knows not to say the same sentence twice — and, more importantly, which
/// verdicts NO fault panel can possibly state.
/// </param>
public sealed record RouteVerdict(
    RouteHealthLevel Level,
    string? Reason,
    string? ErrorCode,
    bool FromAdapterError = false);

/// <summary>
/// Endpoint-aware health verdict for a route. See the file header for why the
/// route's own <c>StateName</c> is not sufficient on its own.
/// </summary>
public static class RouteHealth
{
    /// <summary>
    /// The single adapter state that counts as healthy. Matches the test
    /// StatusFooter and the Overview banner apply, deliberately excluding
    /// Degraded — that is an adapter retrying something which is not currently
    /// answering, so counting it as healthy reports a machine as fine while its
    /// data has stopped.
    /// </summary>
    public const string RunningState = "Running";

    /// <summary>True when <paramref name="stateName"/> is the running state.</summary>
    public static bool IsRunning(string? stateName) =>
        string.Equals(stateName, RunningState, StringComparison.Ordinal);

    /// <summary>
    /// IS DATA REACHING ITS DESTINATION? Worst-of across the route and both its
    /// ends, PLUS the delivery signals — dropped readings, a wedged buffer, and
    /// any error recent enough to still describe the present.
    /// <para>
    /// Drives the Overview verdict banner, the status footer and broken-route
    /// ordering. For "is the machine connected?" — the question the route card's
    /// left accent asks — use <see cref="ConnectionLevel"/>.
    /// </para>
    /// <para>
    /// The evaluation order lives in <see cref="Verdict"/>, which also returns
    /// the sentence for whichever condition decided this level.
    /// </para>
    /// </summary>
    public static RouteHealthLevel Level(RouteSummaryDto? route) => Verdict(route).Level;

    /// <summary>
    /// The operator-facing sentence for whatever condition set
    /// <see cref="Level"/>, or null when the route is healthy.
    /// <para>
    /// A red card with no words on it is the failure this answers: the level and
    /// the explanation used to be derived separately — the level here, the
    /// sentence from a private ladder on Overview.razor — so a route could be
    /// judged broken by one condition and explained by another, or by none at
    /// all. Both now come from <see cref="Verdict"/>, so anything not healthy
    /// says why.
    /// </para>
    /// </summary>
    public static string? Explain(RouteSummaryDto? route) => Verdict(route).Reason;

    /// <summary>
    /// The whole verdict — level, reason and error code — from ONE ordered walk.
    /// </summary>
    /// <remarks>
    /// The order below is the definition of route health. It is fixed, and it is
    /// walked once: the first condition that decides the level is also the one
    /// that supplies the sentence, so the two can never describe different
    /// faults.
    /// <list type="number">
    /// <item>No source, or no destination — nothing it reads can be delivered.</item>
    /// <item>Delivery evidence — discarded readings, a wedged buffer, a live error.</item>
    /// <item>Worst of the endpoint and route states.</item>
    /// </list>
    /// Steps 1 and 2 come before the states because they are EVIDENCE about
    /// delivery rather than a report of intent: a route whose every adapter says
    /// "Running" while 3,027 rows pile up on disk is described by step 2 and by
    /// nothing in step 3. Ordering does not change the level — the level is the
    /// worst of everything found, and every step-1 and step-2 condition is
    /// already <see cref="RouteHealthLevel.Down"/> — it decides which true
    /// sentence the operator is shown first, so the most specific one wins.
    /// </remarks>
    public static RouteVerdict Verdict(RouteSummaryDto? route)
    {
        if (route is null)
        {
            return HealthyVerdict;
        }

        // ── 1. Nothing attached ────────────────────────────────────────────
        // Stated as configuration, not as failure. Neither case means a machine
        // is broken, and an operator who reads a red card as a device fault
        // walks to the wrong end of the factory.
        if (route.Source is null)
        {
            return new RouteVerdict(
                RouteHealthLevel.Down,
                "This route has no source attached, so it has nothing to read. "
                + "Nothing is wrong with any machine. The route will stay idle "
                + "until a source is attached to it.",
                ErrorCode: null);
        }

        if (route.Sinks.Count == 0)
        {
            return new RouteVerdict(
                RouteHealthLevel.Down,
                "This route has no destination attached, so nothing it reads can "
                + "be delivered. Nothing is wrong with the machine. The route "
                + "keeps reading, but the readings cannot leave the gateway "
                + "until a destination is attached to it.",
                ErrorCode: null);
        }

        // ── 2. Delivery evidence ───────────────────────────────────────────
        // DELIVERY, not just state. A route can report Running end to end —
        // source Running, sink Running, no error anywhere — and still never put
        // a single reading on the wire. Observed on this gateway: 774 enqueued,
        // 0 drained, 3,027 rows and 435 KB accumulating on disk, while every
        // card showed green. State and delivery are different questions, and
        // this method is the one that answers delivery.
        //
        // These signals previously lived as private helpers on Overview.razor,
        // so the banner counted a route as broken while the card beside it
        // stayed green — the two disagreed in front of the operator. They live
        // here now so the banner, the ordering and the footer all answer from
        // one definition.
        //
        // Losing data before wedged: a route can be both, and readings already
        // thrown away outrank readings still sitting on disk.
        if (IsLosingData(route))
        {
            return new RouteVerdict(
                RouteHealthLevel.Down,
                "Readings are being discarded — the buffer is full and the "
                + "destination is not draining it.",
                ErrorCode: null);
        }

        if (IsWedged(route))
        {
            return new RouteVerdict(
                RouteHealthLevel.Down,
                "Nothing has ever been delivered — readings are queuing on disk "
                + "and the destination has not accepted any of them.",
                ErrorCode: null);
        }

        // Source error first, then destination, then the route: the order an
        // operator asks the question in — the machine, then where its data goes,
        // then the gateway plumbing between them.
        if (IsLiveError(route.Source.LastErrorAtUtc))
        {
            return ErrorVerdict(route.Source.LastErrorCode, route.Source.LastErrorMessage);
        }

        foreach (var sink in route.Sinks)
        {
            if (IsLiveError(sink.LastErrorAtUtc))
            {
                return ErrorVerdict(sink.LastErrorCode, sink.LastErrorMessage);
            }
        }

        if (IsLiveError(route.LastErrorAtUtc))
        {
            return ErrorVerdict(route.LastErrorCode, route.LastErrorMessage);
        }

        // ── 3. Endpoint and route states ───────────────────────────────────
        // The SOURCE is judged harder than the route or a sink: anything other
        // than Running means no new values are arriving from the machine, so it
        // is Down even when the adapter calls itself Degraded. "Degraded" on a
        // source that has read 0 values is not a partial service — it is a
        // device we cannot reach, with retries in flight. Amber would say
        // "still working", which is the one thing it is not.
        if (!IsRunning(route.Source.StateName))
        {
            return new RouteVerdict(
                RouteHealthLevel.Down,
                $"The source {route.Source.SourceInstanceId} is {route.Source.StateName}, "
                + "so no new readings are arriving from the machine.",
                ErrorCode: null);
        }

        if (StateLevel(route.StateName) == RouteHealthLevel.Down)
        {
            return new RouteVerdict(
                RouteHealthLevel.Down,
                $"The route itself is {route.StateName}, so nothing is moving "
                + "through it even though the machine is being read.",
                ErrorCode: null);
        }

        foreach (var sink in route.Sinks)
        {
            // A null AdapterStateName means the host did not report one (older
            // builds). Absence of evidence is not a fault — leave it alone
            // rather than call every destination broken on an older gateway.
            if (sink.AdapterStateName is not null
                && StateLevel(sink.AdapterStateName) == RouteHealthLevel.Down)
            {
                return new RouteVerdict(
                    RouteHealthLevel.Down,
                    $"Destination {sink.SinkInstanceId} is {sink.AdapterStateName}, "
                    + "so readings have nowhere to go. Nothing is wrong with the "
                    + "machine — the route keeps reading, and readings queue in its "
                    + "buffer until the destination is back.",
                    ErrorCode: null);
            }
        }

        // Everything below is amber: data is still moving.
        foreach (var sink in route.Sinks)
        {
            // A sink IS allowed to be amber: degraded here means publishing but
            // retrying failures, and the store-and-forward buffer means nothing
            // is lost yet.
            if (sink.IsDegraded || StateLevel(sink.AdapterStateName) == RouteHealthLevel.Degraded)
            {
                return new RouteVerdict(
                    RouteHealthLevel.Degraded,
                    $"Destination {sink.SinkInstanceId} is retrying failed publishes. "
                    + "Readings are held in the buffer meanwhile, so nothing is lost yet.",
                    ErrorCode: null);
            }
        }

        if (StateLevel(route.StateName) == RouteHealthLevel.Degraded)
        {
            return new RouteVerdict(
                RouteHealthLevel.Degraded,
                "The route is running degraded — data is still moving, but the "
                + "pipeline is not at full health.",
                ErrorCode: null);
        }

        return HealthyVerdict;
    }

    /// <summary>
    /// IS THE MACHINE CONNECTED? Endpoint states only — route, source and every
    /// destination — with every delivery signal deliberately excluded.
    /// <para>
    /// Drives the route card's left accent. A gateway reading its device
    /// perfectly but unable to drain to the broker painted exactly the same red
    /// edge as one whose device was unplugged, so an operator could not tell
    /// "walk to the machine" from "the broker is down" without opening every
    /// card in turn.
    /// </para>
    /// <para>
    /// CONSEQUENCE, stated openly: a green edge no longer promises that data is
    /// flowing, and a route can be green-edged while the banner still counts it
    /// as broken. That is deliberate. It is NOT a return of the contradiction
    /// this file was written to kill — the delivery fault stays visible on the
    /// same card, in the fault panel and in the waiting / delivered counters.
    /// What remains forbidden is two surfaces answering the SAME question
    /// differently.
    /// </para>
    /// </summary>
    public static RouteHealthLevel ConnectionLevel(RouteSummaryDto? route)
    {
        if (route is null)
        {
            return RouteHealthLevel.Healthy;
        }

        var level = RouteHealthLevel.Healthy;

        // A route with no source or no destination cannot deliver anything,
        // whatever its pipeline reports.
        if (route.Source is null || route.Sinks.Count == 0)
        {
            return RouteHealthLevel.Down;
        }

        level = Worst(level, StateLevel(route.StateName));

        // The SOURCE is judged harder than the route or a sink: anything other
        // than Running means no new values are arriving from the machine, so it
        // is Down even when the adapter calls itself Degraded. "Degraded" on a
        // source that has read 0 values is not a partial service — it is a
        // device we cannot reach, with retries in flight. Amber would say
        // "still working", which is the one thing it is not.
        level = Worst(
            level,
            IsRunning(route.Source.StateName) ? RouteHealthLevel.Healthy : RouteHealthLevel.Down);

        // DELIVERY, not just state. A route can report Running end to end —
        // source Running, sink Running, no error anywhere — and still never put
        // a single reading on the wire. Observed on this gateway: 774 enqueued,
        // 0 drained, 3,027 rows and 435 KB accumulating on disk, while every
        // card showed green. State and delivery are different questions and only
        // delivery is the one the operator cares about.
        //
        // These three signals previously lived as private helpers on
        // Overview.razor, so the banner counted a route as broken while the card
        // beside it stayed green — the two disagreed in front of the operator.
        // They live here now so the cards, the verdict banner and the status
        // footer all answer from one definition.
        if (IsLosingData(route) || IsWedged(route)
            || IsLiveError(route.LastErrorAtUtc)
            || IsLiveError(route.Source?.LastErrorAtUtc)
            || route.Sinks.Any(s => IsLiveError(s.LastErrorAtUtc)))
        {
            level = RouteHealthLevel.Down;
        }

        foreach (var sink in route.Sinks)
        {
            // A sink IS allowed to be amber: degraded here means publishing but
            // retrying failures, and the store-and-forward buffer means nothing
            // is lost yet.
            if (sink.IsDegraded)
            {
                level = Worst(level, RouteHealthLevel.Degraded);
            }

            // A null AdapterStateName means the host did not report one (older
            // builds). Absence of evidence is not a fault — leave it alone
            // rather than paint every destination red on an older gateway.
            if (sink.AdapterStateName is not null)
            {
                level = Worst(level, StateLevel(sink.AdapterStateName));
            }
        }

        return level;
    }

    /// <summary>
    /// Is this route delivering, end to end? The predicate the status footer
    /// counts with, so "3/4 routes delivering" in the footer means the same
    /// thing as three green cards and one red one on the board. The footer used
    /// to count <c>StateName == "Running"</c> itself, which is the PIPELINE
    /// state — it reported a route as up while its card said "Not delivering".
    /// </summary>
    public static bool IsHealthy(RouteSummaryDto? route) =>
        Level(route) == RouteHealthLevel.Healthy;

    /// <summary>Convenience predicate — anything not <see cref="RouteHealthLevel.Healthy"/>.</summary>
    public static bool NeedsAttention(RouteSummaryDto? route) => !IsHealthy(route);

    /// <summary>
    /// Readings discarded. Two counters carry this and only one used to be read:
    /// <c>Buffer.TotalDropped</c> is what the buffer discarded itself, whereas
    /// <c>BackpressureDropCount</c> is what never got in because the buffer was
    /// already full. A wedged route shows the second while the first stays 0.
    /// </summary>
    public static bool IsLosingData(RouteSummaryDto r) =>
        (r.Buffer?.TotalDropped ?? 0) > 0 || r.BackpressureDropCount > 0;

    /// <summary>Something is queued and nothing has ever come out.</summary>
    public static bool IsWedged(RouteSummaryDto r) =>
        r.Buffer is { CurrentDepth: > 0, TotalDrained: 0 };

    /// <summary>
    /// An error recent enough to still describe the present. Anything older than
    /// the stale window is history and must not keep a route red for ever.
    /// </summary>
    public static bool IsLiveError(DateTime? utc) =>
        utc is not null && !OperatorFormat.IsStale(utc);

    /// <summary>
    /// The one healthy answer, shared so "healthy" is never accidentally
    /// constructed with a reason attached — a green route has nothing to say.
    /// </summary>
    private static readonly RouteVerdict HealthyVerdict =
        new(RouteHealthLevel.Healthy, Reason: null, ErrorCode: null);

    /// <summary>
    /// A verdict built from an adapter error. The sentence comes from
    /// <see cref="ErrorGuidance"/> — the same catalogue the card's fault panel
    /// reads — so the banner and the panel quote one wording, and an
    /// uncatalogued code still falls back to the adapter's own message rather
    /// than to silence.
    /// </summary>
    private static RouteVerdict ErrorVerdict(string? errorCode, string? message) =>
        new(RouteHealthLevel.Down,
            ErrorGuidance.Describe(errorCode, message),
            errorCode,
            FromAdapterError: true);

    private static RouteHealthLevel StateLevel(string? stateName) => stateName switch
    {
        RunningState => RouteHealthLevel.Healthy,
        "Degraded" => RouteHealthLevel.Degraded,
        // Created / Initializing / Initialized / Starting / Stopping are
        // transitional, but from the floor they still mean "not delivering".
        null => RouteHealthLevel.Healthy,
        _ => RouteHealthLevel.Down,
    };

    private static RouteHealthLevel Worst(RouteHealthLevel a, RouteHealthLevel b) =>
        a >= b ? a : b;
}
