// ============================================================================
// File: Generation/GenerationOutcomes.cs
// Purpose: Structured result vocabularies for the source-slot gate operations.
//          Host-internal: the gate surface (and therefore its result types) is
//          never handed to adapters or external callers (review G1, focused
//          review #1). Bare bools are avoided so each rejection reason stays
//          distinguishable in tests, history, and logs.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §1.
// Slice 0 — gate foundation (unused scaffolding).
// ============================================================================

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>Result of attempting to mint a new generation lease.</summary>
internal enum LeaseIssueOutcome
{
    Ok = 0,

    /// <summary>The per-slot generation counter is exhausted; minting fails closed (no wrap).</summary>
    AllocatorOverflow = 1,
}

/// <summary>Result of authorizing a generation lease at the slot gate.</summary>
internal enum GenerationAuthorizationOutcome
{
    Ok = 0,

    /// <summary>The lease was minted by a different gate.</summary>
    WrongGate = 1,

    /// <summary>The lease's key does not match this gate's runtime/slot identity.</summary>
    IdentityMismatch = 2,

    /// <summary>The lease has already been authorized once and cannot be re-authorized.</summary>
    AlreadyAuthorized = 3,

    /// <summary>The lease has been retired/abandoned and can never be authorized.</summary>
    AlreadyRetired = 4,

    /// <summary>Another generation already holds publish authority for this slot.</summary>
    AuthorizationConflict = 5,

    /// <summary>
    /// The lease's generation id is at or below the last id this gate authorized;
    /// a stale, out-of-order generation can never gain authority.
    /// </summary>
    StaleGeneration = 6,
}

/// <summary>Result of retiring the current generation at the slot gate.</summary>
internal enum GenerationRetirementOutcome
{
    Ok = 0,

    /// <summary>The expected lease was minted by a different gate.</summary>
    WrongGate = 1,

    /// <summary>The expected lease is not the current generation (e.g. a late stop for a predecessor).</summary>
    NotCurrent = 2,

    /// <summary>The expected lease was already retired (idempotent no-op).</summary>
    AlreadyRetired = 3,
}

/// <summary>Result of abandoning an issued-but-never-authorized lease (e.g. after an activation failure).</summary>
internal enum GenerationAbandonOutcome
{
    Ok = 0,

    /// <summary>The lease was minted by a different gate.</summary>
    WrongGate = 1,

    /// <summary>The lease is authorized (current); abandon is only for issued leases — use retire instead.</summary>
    AlreadyAuthorized = 2,

    /// <summary>The lease was already retired/abandoned (idempotent no-op).</summary>
    AlreadyRetired = 3,
}

/// <summary>Result of a gate-linearized synchronous commit.</summary>
internal enum GenerationCommitOutcome
{
    /// <summary>The lease was current and the synchronous commit returned <c>true</c>.</summary>
    Committed = 0,

    /// <summary>The lease was current but the synchronous commit returned <c>false</c> (e.g. a capacity race).</summary>
    NotCommitted = 1,

    /// <summary>The lease was not the current authorized generation; nothing ran.</summary>
    Rejected = 2,
}
