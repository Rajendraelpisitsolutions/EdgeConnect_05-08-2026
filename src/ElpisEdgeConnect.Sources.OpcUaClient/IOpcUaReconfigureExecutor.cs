// ============================================================================
// File: IOpcUaReconfigureExecutor.cs
// Purpose: Test seam for the surgical "apply diff to live subscriptions"
//          step of hot reconfigure. Substituting this interface lets the
//          adapter tests verify the integration shell (single-flight
//          guards, state transitions, metric counters) without touching
//          real Subscription / MonitoredItem types.
//
// LOCKED design rules (per PR 6b plan + amendments, user lock 2026-05-29):
//
//   1. The executor is invoked AFTER the diff has been computed AND the
//      adapter has cleared the single-flight + reconnect-in-flight gates
//   2. The executor is responsible for 100K-cap pre-validation BEFORE
//      ANY server-side mutation (amendment — pinned via test
//      ReconfigureExecutor_OverCap_DoesNotMutateLiveSubscriptions)
//   3. Per the partial-failure strategy A (best-effort + Degraded, user
//      lock 2026-05-29), the executor may leave subscriptions in a
//      partially-mutated state on exception. The adapter catches and
//      transitions to Degraded; the coordinator + operator recover via
//      reconnect or restart.
//   4. Output enumerates new subscriptions (for FastDataChangeCallback
//      re-wire by the adapter) and removed subscriptions (for the
//      adapter to dispose).
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
//            PR 6b plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Applies a <see cref="OpcUaMonitoredItemDiffResult"/> to the live
/// subscriptions of an OPC UA session. Hot path — no session teardown,
/// no dispatcher restart.
/// </summary>
internal interface IOpcUaReconfigureExecutor
{
    /// <summary>
    /// Apply the diff. Returns the final subscription list (replacing
    /// the adapter's <c>_subscriptions</c>), plus the subset that's
    /// brand-new (needs FastDataChangeCallback re-wire) and the subset
    /// that was emptied (needs disposal by the adapter).
    /// </summary>
    /// <remarks>
    /// Throws <see cref="InvalidOperationException"/> with the
    /// <c>OPCUA.TOO_MANY_MONITORED_ITEMS_AFTER_RECONFIGURE</c> error
    /// code prefix when the new total would exceed the per-session
    /// ceiling. Pre-validation guarantee — no server-side mutation
    /// occurs in that case (pinned by
    /// <c>ReconfigureExecutor_OverCap_DoesNotMutateLiveSubscriptions</c>).
    /// </remarks>
    Task<OpcUaReconfigureExecutionResult> ApplyAsync(
        ISession session,
        IReadOnlyList<Subscription> existingSubscriptions,
        OpcUaMonitoredItemDiffResult diff,
        OpcUaClientSourceConfiguration newConfig,
        CancellationToken ct);
}

/// <summary>
/// Output of <see cref="IOpcUaReconfigureExecutor.ApplyAsync"/>.
/// </summary>
public sealed record OpcUaReconfigureExecutionResult
{
    /// <summary>
    /// Final list of subscriptions on the session (existing minus removed
    /// plus new). Replaces the adapter's <c>_subscriptions</c> field.
    /// </summary>
    public required IReadOnlyList<Subscription> FinalSubscriptions { get; init; }

    /// <summary>
    /// Subscriptions allocated during this execution. The adapter MUST
    /// wire <c>sub.FastDataChangeCallback = _dispatcher.OnNotification</c>
    /// on each one so the dispatcher receives their notifications.
    /// </summary>
    public required IReadOnlyList<Subscription> NewSubscriptions { get; init; }

    /// <summary>
    /// Subscriptions emptied by item removals. The adapter MUST call
    /// <c>Delete(silent: true)</c> + <c>Dispose</c> on each one (the
    /// executor leaves disposal to the adapter so a partial executor
    /// failure doesn't strand half-disposed subs).
    /// </summary>
    public required IReadOnlyList<Subscription> RemovedSubscriptions { get; init; }

    /// <summary>Items added in this execution.</summary>
    public required int ItemsAdded { get; init; }

    /// <summary>Items removed in this execution.</summary>
    public required int ItemsRemoved { get; init; }

    /// <summary>Items modified in this execution.</summary>
    public required int ItemsModified { get; init; }
}
