// ============================================================================
// File: Adapters/IReplayAwareSinkAdapter.cs
// Purpose: Optional sink capability adding the replay-session lifecycle on top of
//          ISinkAdapter. A sink that does NOT implement this interface is published
//          through the base ISinkAdapter.PublishAsync path, completely unaffected.
//          Core drives the lifecycle; the sink reacts to the phase/context and
//          lifecycle Core supplies — it does not derive or advance buffer phase.
// Reference: docs/decisions/0036-*.md Rule 6/7; plan v2.3 §1/§2; PR #180 review.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// A sink that participates in the Core-driven replay session. The lifecycle lets
/// the sink birth even for an empty route and always before DATA. Ownership of
/// buffer replay phase, the H/C boundaries, batch splitting, and cursor advancement
/// stays in Core; this sink only reacts to what Core supplies.
/// <para>
/// <b>Method composition (Core calls in this order):</b>
/// </para>
/// <list type="number">
/// <item><description>
/// <see cref="ISinkAdapter.StartAsync"/> — starts adapter-local resources (clients,
/// buffers, timers). It does NOT establish the external replay session and does NOT
/// birth.
/// </description></item>
/// <item><description>
/// <see cref="BeginReplaySessionAsync"/> — establishes the external session
/// (connect) and emits the birth from the start snapshot. Always called after
/// <see cref="ISinkAdapter.StartAsync"/> and before any phase-tagged publish.
/// </description></item>
/// <item><description>
/// <see cref="PublishAsync(IReadOnlyList{CanonicalDataPoint}, PublishContext, CancellationToken)"/>
/// — phase-tagged batches; then <see cref="CompleteCatchUpAsync"/> to enter Live.
/// </description></item>
/// <item><description>
/// <see cref="RebirthAsync"/> — same-session re-announce (retains the session; resets
/// protocol sequence; re-births). Distinct from beginning a new session.
/// </description></item>
/// <item><description>
/// <see cref="EndSessionAsync"/> — graceful protocol shutdown (e.g. emit a death
/// certificate). Called before <see cref="ISinkAdapter.StopAsync"/> on a clean stop.
/// </description></item>
/// <item><description>
/// <see cref="ISinkAdapter.StopAsync"/> — final idempotent resource cleanup. It must
/// NOT emit a second death when <see cref="EndSessionAsync"/> already ran.
/// </description></item>
/// </list>
/// <para>
/// When Core selects the replay-aware path for a sink, the base context-free
/// <see cref="ISinkAdapter.PublishAsync(IReadOnlyList{CanonicalDataPoint}, CancellationToken)"/>
/// is NEVER called for that route — every publish carries a <see cref="PublishContext"/>.
/// </para>
/// <para>
/// <b>Epoch gating (locked):</b> a same-session rebirth retains the
/// <see cref="ReplaySessionId"/> but each successful NBIRTH — the initial
/// <see cref="BeginReplaySessionAsync"/> and every <see cref="RebirthAsync"/> —
/// establishes a NEW <see cref="ReplayEpochId"/>. The sink records the epoch of its
/// <em>most recent successful</em> birth as authoritative and MUST reject or ignore any
/// <see cref="PublishAsync(IReadOnlyList{CanonicalDataPoint}, PublishContext, CancellationToken)"/>,
/// <see cref="CompleteCatchUpAsync"/>, or rebirth-request input whose session or epoch
/// does not match it — so a delayed pre-rebirth batch or cutover cannot corrupt the
/// current birth generation. A NBIRTH that FAILS must NOT promote the candidate epoch;
/// the previous authoritative epoch stands.
/// </para>
/// </summary>
public interface IReplayAwareSinkAdapter : ISinkAdapter
{
    /// <summary>
    /// Begin a NEW replay session: establish the external transport/session and birth
    /// from <see cref="ReplaySessionStart.State"/>'s snapshot — even when the buffer is
    /// empty (no DATA follows). Records the session identity for later lifecycle calls.
    /// </summary>
    /// <param name="start">The session start inputs (session id, boundary, birth snapshot, host).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the birth is emitted.</returns>
    Task BeginReplaySessionAsync(ReplaySessionStart start, CancellationToken cancellationToken);

    /// <summary>
    /// Same-session rebirth: RETAIN the transport session and birth/death sequence,
    /// RESET protocol sequence state, emit a fresh birth from
    /// <see cref="ReplaySessionRebirth.State"/>'s snapshot, replace the birth-generation
    /// baseline, and resume. Distinct from <see cref="BeginReplaySessionAsync"/>, which
    /// establishes a NEW session. A rebirth for a superseded session must be ignored.
    /// </summary>
    /// <param name="rebirth">The rebirth inputs (session id, fresh boundary, fresh snapshot).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the re-birth is emitted.</returns>
    Task RebirthAsync(ReplaySessionRebirth rebirth, CancellationToken cancellationToken);

    /// <summary>
    /// Publish a single-phase batch with its replay context. The batch is guaranteed
    /// to be entirely within one <see cref="PublishContext.Phase"/> (Core splits
    /// boundary-straddling batches before calling).
    /// </summary>
    /// <param name="points">The batch points, in strict sequence order.</param>
    /// <param name="context">The replay context for this batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The publish result driving cursor advancement.</returns>
    Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        PublishContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Complete the catch-up cutover: emit the single non-historical latest-value
    /// update (compared against the current birth generation) and enter Live.
    /// </summary>
    /// <param name="cutover">The catch-up cutoff and coherent snapshot as of it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when Live has been entered.</returns>
    Task CompleteCatchUpAsync(ReplaySessionCutover cutover, CancellationToken cancellationToken);

    /// <summary>
    /// End the session gracefully (e.g. emit a death certificate before
    /// disconnecting) on stop or configuration replacement. On a clean stop this runs
    /// before <see cref="ISinkAdapter.StopAsync"/>, which must not emit a second death.
    /// </summary>
    /// <param name="sessionEnd">The session end inputs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the session has ended.</returns>
    Task EndSessionAsync(ReplaySessionEnd sessionEnd, CancellationToken cancellationToken);
}
