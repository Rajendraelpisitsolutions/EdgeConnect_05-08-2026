// ============================================================================
// File: Diagnostics/IReloadOutcomeRegistry.cs
// Purpose: Correlation channel between the runtime-reload coordinator
//          (producer, M.P2.2 phase 2.c) and the Management apply
//          endpoint (consumer, M.P2.2 phase 3). Lets the API surface the
//          reconcile outcome alongside the apply response so operators
//          see what happened to their change without polling
//          /diagnostics/configuration-faults.
//
//          Locked design properties (M.P2.2 phase 3 plan v2, guardrails K-N):
//
//             * K — Bounded + evicting. Concrete implementation caps at
//               capacity 64, FIFO on insertion order. Unbounded growth
//               is a memory-leak vector; this queue is observational,
//               not a durable audit store.
//             * L — Non-blocking, in-memory. Enqueue is synchronous,
//               touches only a private lock + a Dictionary + a
//               TaskCompletionSource. No await on storage, listeners,
//               or observers. The reconcile path cannot be slowed by
//               outcome publication.
//             * M — Observation, not authority. The DTO carries data
//               only; there are no callbacks, actions, or control
//               fields on ReloadOutcome / FaultedReloadEntry. Adding
//               retry / rollback / runtime-mutation surfaces here is
//               REJECTED at the architectural-pin level.
//             * N — Process-lifetime only. No durability across restart.
//               Outcomes are correlation buffers, not history. The
//               audit chain and IConfigurationFaultRegistry are the
//               durable surfaces.
// Reference: docs/sessions/2026-05-16-mp22-phase3-plan.md (v2 locked)
// Milestone: M.P2.2 phase 3
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// In-memory channel correlating reconcile outcomes with apply-endpoint
/// waiters by configuration version id. Singleton-scoped in DI; lives
/// on the gateway process and survives only the process lifetime
/// (guardrail N).
/// </summary>
public interface IReloadOutcomeRegistry
{
    /// <summary>
    /// Publish a completed reconcile outcome for the version it targeted.
    /// Wakes any waiters currently parked on
    /// <see cref="WaitForAsync"/> for that version. Idempotent — a second
    /// enqueue for the same <see cref="ReloadOutcome.NewVersionId"/> is a
    /// no-op (the first outcome wins).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Non-blocking (guardrail L).</b> Implementations must complete
    /// synchronously and must not await storage, listeners, or other
    /// observers. The reconcile path's runtime budget is preserved.
    /// </para>
    /// </remarks>
    void EnqueueCompleted(ReloadOutcome outcome);

    /// <summary>
    /// Publish a Skipped outcome for a stale version that the coordinator
    /// abandoned because a newer Apply superseded it before the stale
    /// reconcile reached the head of the semaphore queue.
    /// <paramref name="supersededBy"/> is the id of the newer version
    /// that won.
    /// </summary>
    /// <remarks>
    /// The Q2 verdict (M.P2.2 phase 3 plan v2 §2): operators and
    /// automation/CLI callers need a terminal outcome for the abandoned
    /// version; an unresolved InProgress would be misleading.
    /// </remarks>
    void EnqueueSkipped(ConfigurationVersionId staleVersion, ConfigurationVersionId supersededBy);

    /// <summary>
    /// Wait for an outcome for <paramref name="versionId"/> to appear,
    /// up to <paramref name="timeout"/>. Returns the outcome on hit,
    /// <c>null</c> on timeout or cancellation.
    /// </summary>
    /// <remarks>
    /// Multiple concurrent waiters on the same version all receive the
    /// outcome when it lands. A waiter that arrives after the outcome
    /// is already cached returns immediately.
    /// </remarks>
    Task<ReloadOutcome?> WaitForAsync(
        ConfigurationVersionId versionId,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
