// ============================================================================
// File: Adapters/ReplaySessionContracts.cs
// Purpose: Protocol-neutral parameter/callback types for the replay-session
//          lifecycle (IReplayAwareSinkAdapter). Every record carries a
//          ReplaySessionId so a delayed cutover/end/rebirth from a superseded
//          session can be rejected, and carries the atomic ReplaySessionStartState /
//          ReplaySessionCutoverState INTACT — the coherent (boundary, snapshot) pair
//          is never re-decomposed into independently-constructible fields. Same-
//          session rebirth is an explicit, separate operation from beginning a new
//          session. All records have private constructors + validated Create
//          factories. No protocol-specific fields (no bdSeq/seq/payload).
// Reference: docs/decisions/0036-*.md Rule 4/6; plan v2.3 §2; PR #180 review rounds 1-2.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Inputs handed to a replay-aware sink at session start: the session identity, the
/// atomic birth state (boundary + birth snapshot, captured coherently), and the Core
/// host the sink uses to request a rebirth. Supplied even when the buffer is empty, so
/// the sink can establish its session and birth with no DATA. Built via
/// <see cref="Create"/>; the primitive constructor is private.
/// </summary>
public sealed record ReplaySessionStart
{
    private ReplaySessionStart(ReplaySessionId sessionId, ReplayEpochId epoch, string routeId, ReplaySessionStartState state, IReplaySessionHost host)
    {
        SessionId = sessionId;
        Epoch = epoch;
        RouteId = routeId;
        State = state;
        Host = host;
    }

    /// <summary>The session identity (retained across a same-session rebirth).</summary>
    public ReplaySessionId SessionId { get; }

    /// <summary>The epoch this NBIRTH establishes (the initial begin epoch).</summary>
    public ReplayEpochId Epoch { get; }

    /// <summary>The route the session belongs to.</summary>
    public string RouteId { get; }

    /// <summary>The atomic birth state (boundary + birth snapshot).</summary>
    public ReplaySessionStartState State { get; }

    /// <summary>The Core host used to signal a rebirth request (reverse handshake).</summary>
    public IReplaySessionHost Host { get; }

    /// <summary>Create validated session-start inputs.</summary>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="epoch">The epoch this begin establishes.</param>
    /// <param name="routeId">The route (non-empty).</param>
    /// <param name="state">The atomic birth state.</param>
    /// <param name="host">The rebirth host.</param>
    /// <returns>The inputs.</returns>
    /// <exception cref="ArgumentException">Thrown when the route is empty.</exception>
    public static ReplaySessionStart Create(ReplaySessionId sessionId, ReplayEpochId epoch, string routeId, ReplaySessionStartState state, IReplaySessionHost host)
    {
        RequireRoute(routeId);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(host);
        return new ReplaySessionStart(sessionId, epoch, routeId, state, host);
    }

    internal static void RequireRoute(string routeId)
    {
        if (string.IsNullOrEmpty(routeId))
        {
            throw new ArgumentException("RouteId must be non-empty.", nameof(routeId));
        }
    }
}

/// <summary>
/// Inputs for a same-session rebirth: the sink RETAINS its transport session and
/// protocol birth/death sequence (e.g. bdSeq), RESETS protocol sequence state, emits
/// a fresh birth from <see cref="State"/>, replaces the current birth-generation
/// baseline, and resumes. Distinct from <see cref="ReplaySessionStart"/>, which
/// establishes a NEW session. Built via <see cref="Create"/>.
/// </summary>
public sealed record ReplaySessionRebirth
{
    private ReplaySessionRebirth(ReplaySessionId sessionId, ReplayEpochId epoch, ReplaySessionStartState state)
    {
        SessionId = sessionId;
        Epoch = epoch;
        State = state;
    }

    /// <summary>The (unchanged) session identity being reborn.</summary>
    public ReplaySessionId SessionId { get; }

    /// <summary>The NEW epoch this rebirth establishes (strictly greater than the prior epoch).</summary>
    public ReplayEpochId Epoch { get; }

    /// <summary>The freshly captured atomic birth state.</summary>
    public ReplaySessionStartState State { get; }

    /// <summary>Create validated rebirth inputs.</summary>
    /// <param name="sessionId">The session identity being reborn.</param>
    /// <param name="epoch">The new epoch this rebirth establishes.</param>
    /// <param name="state">The fresh atomic birth state.</param>
    /// <returns>The inputs.</returns>
    public static ReplaySessionRebirth Create(ReplaySessionId sessionId, ReplayEpochId epoch, ReplaySessionStartState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ReplaySessionRebirth(sessionId, epoch, state);
    }
}

/// <summary>
/// Inputs for the catch-up cutover: the atomic catch-up state (cutoff + snapshot as of
/// it) from which the sink emits the final non-historical update (compared against the
/// current birth generation) and enters Live. Built via <see cref="Create"/>.
/// </summary>
public sealed record ReplaySessionCutover
{
    private ReplaySessionCutover(ReplaySessionId sessionId, ReplayEpochId epoch, ReplaySessionCutoverState state)
    {
        SessionId = sessionId;
        Epoch = epoch;
        State = state;
    }

    /// <summary>The session identity.</summary>
    public ReplaySessionId SessionId { get; }

    /// <summary>The epoch this cutover belongs to (must match the sink's current birth epoch to be honored).</summary>
    public ReplayEpochId Epoch { get; }

    /// <summary>The atomic catch-up state (cutoff + snapshot).</summary>
    public ReplaySessionCutoverState State { get; }

    /// <summary>Create validated cutover inputs.</summary>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="epoch">The epoch this cutover belongs to.</param>
    /// <param name="state">The atomic catch-up state.</param>
    /// <returns>The inputs.</returns>
    public static ReplaySessionCutover Create(ReplaySessionId sessionId, ReplayEpochId epoch, ReplaySessionCutoverState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ReplaySessionCutover(sessionId, epoch, state);
    }
}

/// <summary>Why a replay session is ending.</summary>
public enum ReplaySessionEndReason
{
    /// <summary>The route/sink is stopping (graceful shutdown).</summary>
    Stop = 0,

    /// <summary>The configuration was replaced (the session is torn down for a new one).</summary>
    ConfigurationReplaced = 1,
}

/// <summary>Inputs for a graceful session end (e.g. a sink emits a death certificate before disconnecting). Built via <see cref="Create"/>.</summary>
public sealed record ReplaySessionEnd
{
    private ReplaySessionEnd(ReplaySessionId sessionId, string routeId, ReplaySessionEndReason reason)
    {
        SessionId = sessionId;
        RouteId = routeId;
        Reason = reason;
    }

    /// <summary>The session identity.</summary>
    public ReplaySessionId SessionId { get; }

    /// <summary>The route the session belonged to.</summary>
    public string RouteId { get; }

    /// <summary>Why the session is ending.</summary>
    public ReplaySessionEndReason Reason { get; }

    /// <summary>Create validated session-end inputs.</summary>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="routeId">The route (non-empty).</param>
    /// <param name="reason">Why the session is ending (must be a defined value).</param>
    /// <returns>The inputs.</returns>
    /// <exception cref="ArgumentException">Thrown when the route is empty or the reason undefined.</exception>
    public static ReplaySessionEnd Create(ReplaySessionId sessionId, string routeId, ReplaySessionEndReason reason)
    {
        ReplaySessionStart.RequireRoute(routeId);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentException($"Unknown ReplaySessionEndReason '{(int)reason}'.", nameof(reason));
        }

        return new ReplaySessionEnd(sessionId, routeId, reason);
    }
}

/// <summary>Why a sink is asking Core to rebirth.</summary>
public enum RebirthReason
{
    /// <summary>The destination host commanded a rebirth (e.g. a Node-Control rebirth).</summary>
    HostCommand = 0,

    /// <summary>A metric schema change was detected.</summary>
    SchemaChange = 1,

    /// <summary>Another sink-specific reason (see the detail).</summary>
    Other = 2,
}

/// <summary>A sink's request to Core to re-announce (rebirth) the session with a fresh coherent snapshot. Built via <see cref="Create"/>.</summary>
public sealed record RebirthRequest
{
    private RebirthRequest(ReplaySessionId sessionId, ReplayEpochId epoch, RebirthReason reason, string? detail)
    {
        SessionId = sessionId;
        Epoch = epoch;
        Reason = reason;
        Detail = detail;
    }

    /// <summary>The session the request applies to (Core rejects it if superseded).</summary>
    public ReplaySessionId SessionId { get; }

    /// <summary>
    /// The epoch the sink believed authoritative when it raised the request. Core
    /// coalesces/ignores a request whose epoch is no longer current (e.g. a rebirth
    /// already advanced the epoch), so a stale queued request cannot trigger a
    /// redundant rebirth.
    /// </summary>
    public ReplayEpochId Epoch { get; }

    /// <summary>The typed reason.</summary>
    public RebirthReason Reason { get; }

    /// <summary>Optional human-readable detail (diagnostics only).</summary>
    public string? Detail { get; }

    /// <summary>Create a validated rebirth request.</summary>
    /// <param name="sessionId">The session the request applies to.</param>
    /// <param name="epoch">The epoch the sink believed authoritative when raising the request.</param>
    /// <param name="reason">The typed reason (must be a defined value).</param>
    /// <param name="detail">Optional detail.</param>
    /// <returns>The request.</returns>
    /// <exception cref="ArgumentException">Thrown when the reason is undefined.</exception>
    public static RebirthRequest Create(ReplaySessionId sessionId, ReplayEpochId epoch, RebirthReason reason, string? detail = null)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentException($"Unknown RebirthReason '{(int)reason}'.", nameof(reason));
        }

        return new RebirthRequest(sessionId, epoch, reason, detail);
    }
}

/// <summary>
/// Core-provided host handed to a replay-aware sink for the reverse (sink → Core)
/// rebirth signal. The request MUST be treated as <b>asynchronous, queued, and
/// non-reentrant</b> — the sink must not synchronously block on Core while Core is
/// awaiting a sink publish call. The returned task completes when the request has
/// been accepted (queued), not when the rebirth finishes.
/// </summary>
public interface IReplaySessionHost
{
    /// <summary>
    /// Request a rebirth. Core (asynchronously, non-reentrantly) pauses the
    /// replay-aware route path, captures a fresh coherent birth state, and drives a
    /// same-session rebirth on the sink via
    /// <see cref="IReplayAwareSinkAdapter.RebirthAsync"/>; buffer cursor ownership is
    /// unchanged. A request for a superseded session is ignored.
    /// </summary>
    /// <param name="request">The rebirth request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request has been ACCEPTED (queued).</returns>
    ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken);
}
