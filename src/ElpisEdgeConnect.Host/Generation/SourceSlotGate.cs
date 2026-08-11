// ============================================================================
// File: Generation/SourceSlotGate.cs
// Purpose: The per-source-slot publish-authorization boundary and the single
//          linearization point for "who may affect current runtime state".
//          Mints one-shot leases, authorizes/retires exactly one current
//          generation, and runs generation-fenced synchronous commits.
//          Authorization is never a readable check-then-act property (review
//          B1/G1/G2): every fenced side effect goes through TryCommit so
//          validation and the side effect share one critical section.
//
//          Host-internal: the gate is never handed to adapters or external
//          callers (focused review #1).
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §1-§2,
//            docs/sessions/2026-06-25-source-generation-foundation-slice-0-spec.md §3-§4.
// Slice 0 — gate foundation (unused scaffolding; no runtime wiring).
// ============================================================================

using ElpisEdgeConnect.Core.Generation;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>Outcome of minting a lease: a minted lease, or an allocator overflow.</summary>
internal readonly struct LeaseIssueResult
{
    private LeaseIssueResult(LeaseIssueOutcome outcome, GenerationLease? lease)
    {
        Outcome = outcome;
        Lease = lease;
    }

    public LeaseIssueOutcome Outcome { get; }

    public GenerationLease? Lease { get; }

    public bool IsOk => Outcome == LeaseIssueOutcome.Ok;

    public static LeaseIssueResult Issued(GenerationLease lease) => new(LeaseIssueOutcome.Ok, lease);

    public static LeaseIssueResult Overflow() => new(LeaseIssueOutcome.AllocatorOverflow, null);
}

/// <summary>
/// The per-source-slot publish-authorization boundary. The gate is the single
/// linearization point for "who may affect current runtime state": it mints
/// one-shot leases, authorizes/retires exactly one current generation, and runs
/// generation-fenced synchronous commits. Authorization is never a readable
/// check-then-act property — every fenced side effect goes through
/// <see cref="TryCommit{TState}"/> so validation and the side effect share one
/// critical section.
/// <para>
/// Invariant: a slot has <b>zero or one</b> publish-authorized generation. This
/// is publish authority, not physical liveness — a retired generation may still
/// be executing; that is governed by the host's quiescence/orphan policy
/// (a later Slice 0 commit), not this gate.
/// </para>
/// </summary>
internal sealed class SourceSlotGate
{
    private readonly object _sync = new();
    private readonly RuntimeInstanceId _runtimeInstanceId;
    private readonly string _sourceSlotId;
    private readonly SourceGenerationAllocator _allocator;

    private GenerationLease? _current;
    private ulong _lastAuthorizedGenerationId; // high-water mark; 0 = none authorized yet
    private bool _inCommit;

    public SourceSlotGate(
        RuntimeInstanceId runtimeInstanceId,
        string sourceSlotId,
        SourceGenerationAllocator allocator)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceSlotId);
        ArgumentNullException.ThrowIfNull(allocator);
        _runtimeInstanceId = runtimeInstanceId;
        _sourceSlotId = sourceSlotId;
        _allocator = allocator;
    }

    /// <summary>The stable source-slot id this gate guards.</summary>
    public string SourceSlotId => _sourceSlotId;

    /// <summary>
    /// Whether a generation currently holds publish authority. <b>Diagnostics
    /// only</b> — must never be used to gate a write or state mutation; use
    /// <see cref="TryCommit{TState}"/> for that.
    /// </summary>
    public bool IsPublishAuthorized
    {
        get { lock (_sync) { return _current is not null; } }
    }

    /// <summary>Mint a new lease for this slot, or report allocator overflow.</summary>
    public LeaseIssueResult IssueLease()
    {
        lock (_sync)
        {
            GuardNotInCommit();
            if (!_allocator.TryAllocateNext(_sourceSlotId, out var generationId))
            {
                return LeaseIssueResult.Overflow();
            }

            var key = new GenerationKey(_runtimeInstanceId, _sourceSlotId, generationId);
            return LeaseIssueResult.Issued(new GenerationLease(this, key));
        }
    }

    /// <summary>
    /// Atomically make <paramref name="lease"/> the slot's single publish-authorized
    /// generation. Rejects foreign, mismatched, already-used, conflicting, or
    /// stale (out-of-order) leases. A stale lease — one whose generation id is at
    /// or below the last id this gate authorized — can never gain authority.
    /// </summary>
    public GenerationAuthorizationOutcome TryAuthorize(GenerationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            GuardNotInCommit();
            if (!lease.IsMintedBy(this))
            {
                return GenerationAuthorizationOutcome.WrongGate;
            }
            if (lease.Key.RuntimeInstanceId != _runtimeInstanceId
                || !string.Equals(lease.Key.SourceSlotId, _sourceSlotId, StringComparison.Ordinal))
            {
                return GenerationAuthorizationOutcome.IdentityMismatch;
            }

            switch (lease.State)
            {
                case GenerationLeaseState.Retired:
                    return GenerationAuthorizationOutcome.AlreadyRetired;
                case GenerationLeaseState.Authorized:
                    return GenerationAuthorizationOutcome.AlreadyAuthorized;
            }

            if (lease.Key.GenerationId.Value <= _lastAuthorizedGenerationId)
            {
                return GenerationAuthorizationOutcome.StaleGeneration;
            }
            if (_current is not null)
            {
                return GenerationAuthorizationOutcome.AuthorizationConflict;
            }

            lease.MarkAuthorized();
            _current = lease;
            _lastAuthorizedGenerationId = lease.Key.GenerationId.Value;
            return GenerationAuthorizationOutcome.Ok;
        }
    }

    /// <summary>
    /// Atomically revoke publish authority from <paramref name="expectedLease"/>
    /// — and only that generation, so a late stop for generation N cannot retire
    /// its successor N+1. The successful retirement is the linearization point at
    /// which the generation loses authority. Idempotent on an already-retired lease.
    /// </summary>
    /// <param name="expectedLease">The generation the caller intends to retire.</param>
    /// <param name="reason">Why authority is being revoked (recorded in a later commit).</param>
    public GenerationRetirementOutcome TryRetire(GenerationLease expectedLease, RetirementReason reason)
    {
        ArgumentNullException.ThrowIfNull(expectedLease);
        _ = reason; // recorded in generation history in a later Slice 0 commit
        lock (_sync)
        {
            GuardNotInCommit();
            if (!expectedLease.IsMintedBy(this))
            {
                return GenerationRetirementOutcome.WrongGate;
            }
            if (expectedLease.State == GenerationLeaseState.Retired)
            {
                return GenerationRetirementOutcome.AlreadyRetired;
            }
            if (!ReferenceEquals(_current, expectedLease))
            {
                return GenerationRetirementOutcome.NotCurrent;
            }

            expectedLease.MarkRetired();
            _current = null;
            return GenerationRetirementOutcome.Ok;
        }
    }

    /// <summary>
    /// Terminally abandon an issued-but-never-authorized lease — the
    /// activation-failure path (initialize-before-authorize): a lease whose
    /// <c>InitializeAsync</c> failed is driven straight to <c>Retired</c> so it
    /// can never later be authorized. Rejects an authorized lease (use
    /// <see cref="TryRetire"/>); idempotent on an already-retired lease.
    /// </summary>
    public GenerationAbandonOutcome TryAbandonIssued(GenerationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_sync)
        {
            GuardNotInCommit();
            if (!lease.IsMintedBy(this))
            {
                return GenerationAbandonOutcome.WrongGate;
            }

            switch (lease.State)
            {
                case GenerationLeaseState.Retired:
                    return GenerationAbandonOutcome.AlreadyRetired;
                case GenerationLeaseState.Authorized:
                    return GenerationAbandonOutcome.AlreadyAuthorized;
                default:
                    lease.MarkRetired();
                    return GenerationAbandonOutcome.Ok;
            }
        }
    }

    /// <summary>
    /// Run <paramref name="commit"/> under the gate boundary iff
    /// <paramref name="expectedLease"/> is the current authorized generation, so
    /// that validation and the side effect share one linearization point. The
    /// commit MUST be non-blocking and non-awaiting and MUST NOT re-enter the
    /// gate. <paramref name="state"/> is passed explicitly to avoid per-call
    /// closure allocation on this hot path.
    /// <para>
    /// The gate provides authorization <i>linearization</i>, NOT transaction
    /// rollback: a delegate exception propagates to the caller and any partial
    /// side effects it performed are NOT undone. (The lock is still released on
    /// throw, and authorization is unchanged because nothing above mutated it.)
    /// </para>
    /// </summary>
    /// <typeparam name="TState">Caller state threaded into the commit without a closure.</typeparam>
    /// <param name="expectedLease">The generation that must be current for the commit to run.</param>
    /// <param name="state">Opaque caller state passed to <paramref name="commit"/>.</param>
    /// <param name="commit">The synchronous side effect; its <c>bool</c> result distinguishes committed from not-committed (e.g. a capacity race).</param>
    /// <returns>
    /// <see cref="GenerationCommitOutcome.Rejected"/> when the lease is not the
    /// current authorized generation (nothing runs); otherwise
    /// <see cref="GenerationCommitOutcome.Committed"/> or
    /// <see cref="GenerationCommitOutcome.NotCommitted"/> per the commit's result.
    /// </returns>
    public GenerationCommitOutcome TryCommit<TState>(
        GenerationLease expectedLease, TState state, Func<TState, bool> commit)
    {
        ArgumentNullException.ThrowIfNull(expectedLease);
        ArgumentNullException.ThrowIfNull(commit);
        lock (_sync)
        {
            GuardNotInCommit();
            if (!ReferenceEquals(_current, expectedLease)
                || expectedLease.State != GenerationLeaseState.Authorized)
            {
                return GenerationCommitOutcome.Rejected;
            }

            _inCommit = true;
            try
            {
                return commit(state)
                    ? GenerationCommitOutcome.Committed
                    : GenerationCommitOutcome.NotCommitted;
            }
            finally
            {
                _inCommit = false;
            }
        }
    }

    private void GuardNotInCommit()
    {
        if (_inCommit)
        {
            throw new InvalidOperationException(
                "SourceSlotGate operation was re-entered from within a commit body; " +
                "commits must not call back into the gate.");
        }
    }
}
