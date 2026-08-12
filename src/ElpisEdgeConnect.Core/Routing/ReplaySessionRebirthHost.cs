// ============================================================================
// File: Routing/ReplaySessionRebirthHost.cs
// Purpose: The real coalescing, epoch-gated rebirth host for a replay route (K1.3
//          slice 4). Handed to the replay-aware sink at BeginReplaySessionAsync (and
//          carried across each rebirth); the sink calls RequestRebirthAsync on it to
//          ask Core (asynchronously, non-reentrantly) to re-announce the session with a
//          fresh coherent snapshot. Replaces the slice-3 fail-fast placeholder.
//
// Locked semantics (v3 §R5.4 / v3.1 §A2 / v3.2 §C3):
//   - Epoch gating: accept ONLY a request whose session AND epoch match the CURRENT
//     authoritative pair. A different session is superseded; a lower epoch was already
//     reborn past; a higher epoch cannot be honoured because Core — not the sink — mints
//     epochs (a rebirth promotes the epoch only AFTER a successful RebirthAsync). Every
//     non-matching request is ignored DETERMINISTICALLY (no queue, no wake).
//   - Coalescing: at most one pending request per current epoch; duplicates collapse.
//   - The request is asynchronous + queued: RequestRebirthAsync returns as soon as the
//     request is accepted-or-ignored and NEVER blocks on Core (no reentrancy while Core
//     is awaiting a sink publish).
//   - An independent wake (a latched, coalescing binary signal — same discipline as
//     FanoutDispatcher) lets an idle Live driver be pulled out of its buffer-signal wait
//     by an accepted rebirth. The pending request — not the wake — is the authoritative
//     state; the wake only unblocks the combined wait.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.md §R5.4;
//            …-v3.1-amendment.md §A2; …-v3.2-amendment.md §C3; ADR-0036 (replay → rebirth).
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// The Core-owned <see cref="IReplaySessionHost"/> for one running replay driver. Stateful and
/// thread-safe: the sink calls <see cref="RequestRebirthAsync"/> from its publish path while the
/// driver reads/advances the authoritative session+epoch on its own task. Accepts only a request
/// for the current session+epoch, coalesces duplicates, and ignores everything else deterministically.
/// </summary>
internal sealed class ReplaySessionRebirthHost : IReplaySessionHost, IDisposable
{
    private readonly object _gate = new();

    // Latched, coalescing binary wake (initialCount 0, maxCount 1) — mirrors FanoutDispatcher: a
    // release that lands before the driver waits is not lost; the next wait consumes it immediately.
    private readonly SemaphoreSlim _wake = new(0, 1);

    private ReplaySessionId _session;
    private ReplayEpochId _epoch;
    private RebirthRequest? _pending;
    private bool _disposed;

    /// <summary>Create the host authoritative for <paramref name="session"/> at <paramref name="epoch"/>.</summary>
    /// <param name="session">The session this host governs.</param>
    /// <param name="epoch">The initial authoritative epoch (advanced only by <see cref="Promote"/>).</param>
    public ReplaySessionRebirthHost(ReplaySessionId session, ReplayEpochId epoch)
    {
        _session = session;
        _epoch = epoch;
    }

    /// <summary>The current authoritative session.</summary>
    public ReplaySessionId CurrentSession
    {
        get { lock (_gate) { return _session; } }
    }

    /// <summary>The current authoritative epoch (a failed rebirth leaves this unchanged).</summary>
    public ReplayEpochId CurrentEpoch
    {
        get { lock (_gate) { return _epoch; } }
    }

    /// <inheritdoc/>
    public ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            // Accept ONLY the current session AND epoch; coalesce duplicates (one pending per epoch).
            // Any other request (superseded session, an already-passed lower epoch, or a not-yet-minted
            // higher epoch) is ignored deterministically — Core owns epoch advancement.
            if (request.SessionId == _session && request.Epoch == _epoch && _pending is null)
            {
                _pending = request;
                ReleaseWake();
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Driver-side: take the pending rebirth request (if any) for processing. Clears it so a single
    /// request drives a single rebirth. Returns false when nothing is pending.
    /// </summary>
    /// <param name="request">The taken request, when this returns true.</param>
    /// <returns>True if a request was pending and taken.</returns>
    public bool TryTakePending(out RebirthRequest request)
    {
        lock (_gate)
        {
            if (_pending is { } p)
            {
                _pending = null;
                request = p;
                return true;
            }
        }

        request = null!;
        return false;
    }

    /// <summary>
    /// Driver-side: promote to the new authoritative session+epoch AFTER a successful re-birth. Any
    /// request still pending for the OLD epoch is now obsolete (the fresh birth already reflects it)
    /// and is cleared, so a stale queued request cannot trigger a redundant rebirth.
    /// </summary>
    /// <param name="session">The (retained) session.</param>
    /// <param name="epoch">The new authoritative epoch.</param>
    public void Promote(ReplaySessionId session, ReplayEpochId epoch)
    {
        lock (_gate)
        {
            _session = session;
            _epoch = epoch;
            _pending = null;
        }
    }

    /// <summary>Driver-side: await the next accepted-rebirth wake (independent of the buffer signal).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when a rebirth has been accepted since the last wait.</returns>
    public Task WaitForRebirthAsync(CancellationToken cancellationToken) => _wake.WaitAsync(cancellationToken);

    private void ReleaseWake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
            {
                _wake.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Already signalled — coalesced.
        }
        catch (ObjectDisposedException)
        {
            // Dispose raced with an accept. Drop.
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _wake.Dispose();
    }
}
