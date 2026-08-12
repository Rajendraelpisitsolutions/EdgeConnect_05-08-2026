// ============================================================================
// File: Session/SparkplugActorDiagnostics.cs
// Purpose: The complete, redacted, point-in-time diagnostic state of one
//          SparkplugSessionActor (K3 slice 7 — health/diagnostics). A single
//          immutable, atomically-published, monotonically-VERSIONED snapshot so
//          health and diagnostics can never report a combination of fields that
//          never existed. This is NOT a historical transition log — it is the
//          actor's coherent state at one observation point: lifecycle + protocol
//          substate, the immediately preceding transition, the session authority,
//          recovery progress, lifetime counters, and the last-event timestamps.
//          It carries ONLY safe values (ids, generations, bdSeq, epoch, counts,
//          timestamps, sanitized error code/category) — never credentials, broker
//          endpoint, client id, topics, payload bytes, or metric values.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §8, §11.2.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;

/// <summary>
/// A coherent, redacted, versioned snapshot of the Sparkplug session actor's state
/// (plan v3 §8). Published atomically at each transition; read as a single reference
/// so no field combination is ever torn. Contains no secret-bearing values.
/// </summary>
internal sealed record SparkplugActorDiagnostics
{
    /// <summary>Monotonic snapshot version — strictly increases with each republish.</summary>
    public required long Version { get; init; }

    // --- lifecycle + the immediately preceding transition ---

    /// <summary>Coarse adapter lifecycle state (the <c>ISinkAdapter</c> contract surface).</summary>
    public required AdapterState State { get; init; }

    /// <summary>Fine protocol substate (internal diagnostics; never the contract surface).</summary>
    public required SparkplugProtocolState ProtocolState { get; init; }

    /// <summary>The coarse state immediately before the last transition.</summary>
    public required AdapterState PreviousState { get; init; }

    /// <summary>The fine substate immediately before the last transition.</summary>
    public required SparkplugProtocolState PreviousProtocolState { get; init; }

    /// <summary>When the last transition was published (injected clock).</summary>
    public required DateTimeOffset LastStateChangeAt { get; init; }

    /// <summary>A short, stable, secret-free reason code for the last transition.</summary>
    public required string LastTransitionReasonCode { get; init; }

    /// <summary>True once disposal has won (terminal, non-resurrectable).</summary>
    public required bool TerminalDisposed { get; init; }

    // --- session authority (present only when an authority exists) ---

    /// <summary>True when an authoritative session is currently promoted.</summary>
    public required bool HasSession { get; init; }

    /// <summary>The authoritative replay session id, or null when none.</summary>
    public long? SessionId { get; init; }

    /// <summary>The authoritative replay epoch, or null when none.</summary>
    public long? Epoch { get; init; }

    /// <summary>The authoritative route id, or null when none.</summary>
    public string? RouteId { get; init; }

    /// <summary>The authoritative connection generation, or null when none.</summary>
    public long? ConnectionGeneration { get; init; }

    /// <summary>The most recently ISSUED connection generation (whether or not it birthed).</summary>
    public required long LastIssuedGeneration { get; init; }

    /// <summary>The authoritative bdSeq wire value, or null when none.</summary>
    public int? BdSeq { get; init; }

    /// <summary>The next DATA wire sequence, or null when no session.</summary>
    public int? NextSeq { get; init; }

    // --- recovery / rebirth progress (current state) ---

    /// <summary>True when the authoritative transport is suspect (a drop or uncertain send).</summary>
    public required bool SuspectTransport { get; init; }

    /// <summary>True when a Core rebirth is pending (control episode open).</summary>
    public required bool PendingRebirth { get; init; }

    /// <summary>The pending rebirth's reason, or null when none pending.</summary>
    public string? PendingRebirthReason { get; init; }

    /// <summary>The ordinal within the CURRENTLY active recovery episode; 0 when none is running.</summary>
    public required int CurrentRecoveryAttempt { get; init; }

    /// <summary>The configured recovery-attempt budget.</summary>
    public required int RecoveryAttemptBudget { get; init; }

    /// <summary>The sanitized code of the last recovery failure, or null.</summary>
    public string? LastRecoveryFailureCode { get; init; }

    /// <summary>The sanitized diagnostic code of the last inbound NCMD classification (no names/bytes), or null.</summary>
    public string? LastNodeCommandDiagnosticCode { get; init; }

    // --- last-event timestamps (injected clock; null until first occurrence) ---

    /// <summary>When the last successful birth (NBIRTH promotion) occurred.</summary>
    public DateTimeOffset? LastSuccessfulBirthAt { get; init; }

    /// <summary>When the last successful DATA publish occurred.</summary>
    public DateTimeOffset? LastDataPublishAt { get; init; }

    /// <summary>When the last Core rebirth request was queued.</summary>
    public DateTimeOffset? LastRebirthRequestAt { get; init; }

    /// <summary>When the last error was recorded.</summary>
    public DateTimeOffset? LastErrorAt { get; init; }

    /// <summary>The sanitized code of the last recorded error (no message/endpoint/payload), or null.</summary>
    public string? LastErrorCode { get; init; }

    /// <summary>The category of the last recorded error, or null.</summary>
    public string? LastErrorCategory { get; init; }

    /// <summary>
    /// The last recorded error as a sanitized <see cref="AdapterError"/> (Message cleared so no endpoint,
    /// credential, or payload value can leak through health/diagnostics), or null. Feeds
    /// <see cref="AdapterHealth.LastError"/> so both come from one coherent read.
    /// </summary>
    public AdapterError? LastError { get; init; }

    // --- monotonic lifetime counters (per actor instance; NEVER reset on rebirth) ---

    /// <summary>Delayed disconnect callbacks from a retired transport generation, ignored.</summary>
    public required long StaleDisconnectCallbacks { get; init; }

    /// <summary>Delayed NCMD callbacks from a retired transport generation, ignored.</summary>
    public required long StaleNodeCommandCallbacks { get; init; }

    /// <summary>Inbound NCMDs that classified as a non-actionable (ignored) kind.</summary>
    public required long NodeCommandsIgnored { get; init; }

    /// <summary>Core rebirth requests actually queued (a fresh episode woke Core).</summary>
    public required long RebirthRequestsQueued { get; init; }

    /// <summary>Rebirth signals coalesced into an already-open episode (no second Core request).</summary>
    public required long RebirthRequestsCoalesced { get; init; }

    /// <summary>Healthy in-place rebirths committed (NBIRTH re-emitted on the same connection).</summary>
    public required long HealthyRebirths { get; init; }

    /// <summary>Transport-suspect recovery episodes started.</summary>
    public required long TransportRecoveryStarts { get; init; }

    /// <summary>Complete CONNECT/SUBSCRIBE/NBIRTH recovery attempts made (lifetime).</summary>
    public required long TransportRecoveryAttempts { get; init; }

    /// <summary>Recovery episodes that succeeded within budget.</summary>
    public required long TransportRecoverySuccesses { get; init; }

    /// <summary>Recovery episodes that exhausted the budget and faulted.</summary>
    public required long TransportRecoveryExhaustions { get; init; }

    /// <summary>Observable/uncertain DATA publish failures that requested a rebirth.</summary>
    public required long PublishFailures { get; init; }

    /// <summary>NBIRTH publish failures (initial or rebirth).</summary>
    public required long BirthFailures { get; init; }

    /// <summary>NDEATH publish failures during graceful end.</summary>
    public required long DeathPublishFailures { get; init; }
}
