// ============================================================================
// File: Generation/SourceSlot.cs
// Purpose: The stable source slot: owns the route-facing channel + a STABLE
//          ISourceIntake (survives generations), the slot gate, and the current
//          generation. Lifecycle is two-phase (review C2-1): PrepareGeneration
//          consumes an id + builds the scoped writer (unauthorized); TryActivate
//          is the SOLE atomic current-reference + authorization transition;
//          AbandonPrepared irreversibly retires an initialization-failed
//          candidate. Permanent removal is terminal (review C2-2): it completes
//          the stable channel and rejects all further prepare/activate.
//          Retirement revokes authority FIRST, then detaches the writer —
//          WITHOUT completing the channel (review G3/G4).
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §0, §1, §6.
// Slice 0 — commit 2 scaffolding (unused; not wired into the supervisor).
// ============================================================================

using System.Threading.Channels;
using ElpisEdgeConnect.Core.Generation;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>Outcome of preparing a new generation candidate on a slot.</summary>
internal enum PrepareGenerationOutcome
{
    Prepared = 0,
    AllocatorOverflow = 1,
    SlotTerminal = 2,
}

/// <summary>Result of <see cref="SourceSlot.PrepareGeneration"/>.</summary>
internal readonly struct PrepareGenerationResult
{
    private PrepareGenerationResult(PrepareGenerationOutcome outcome, PreparedSourceGeneration? prepared)
    {
        Outcome = outcome;
        Prepared = prepared;
    }

    public PrepareGenerationOutcome Outcome { get; }

    public PreparedSourceGeneration? Prepared { get; }

    public bool IsPrepared => Outcome == PrepareGenerationOutcome.Prepared;

    public static PrepareGenerationResult AsPrepared(PreparedSourceGeneration prepared) =>
        new(PrepareGenerationOutcome.Prepared, prepared);

    public static PrepareGenerationResult Overflow() =>
        new(PrepareGenerationOutcome.AllocatorOverflow, null);

    public static PrepareGenerationResult SlotTerminal() =>
        new(PrepareGenerationOutcome.SlotTerminal, null);
}

/// <summary>Outcome of activating a prepared generation.</summary>
internal enum ActivateGenerationOutcome
{
    Activated = 0,
    AuthorizationFailed = 1,
    SlotTerminal = 2,
}

/// <summary>Result of <see cref="SourceSlot.TryActivate"/>.</summary>
internal readonly struct ActivateGenerationResult
{
    private ActivateGenerationResult(
        ActivateGenerationOutcome outcome,
        SourceGeneration? generation,
        GenerationAuthorizationOutcome authorizationOutcome)
    {
        Outcome = outcome;
        Generation = generation;
        AuthorizationOutcome = authorizationOutcome;
    }

    public ActivateGenerationOutcome Outcome { get; }

    public SourceGeneration? Generation { get; }

    /// <summary>The gate authorization outcome when <see cref="Outcome"/> is <see cref="ActivateGenerationOutcome.AuthorizationFailed"/>.</summary>
    public GenerationAuthorizationOutcome AuthorizationOutcome { get; }

    public bool IsActivated => Outcome == ActivateGenerationOutcome.Activated;

    public static ActivateGenerationResult Activated(SourceGeneration generation) =>
        new(ActivateGenerationOutcome.Activated, generation, GenerationAuthorizationOutcome.Ok);

    public static ActivateGenerationResult AuthorizationFailed(GenerationAuthorizationOutcome authorizationOutcome) =>
        new(ActivateGenerationOutcome.AuthorizationFailed, null, authorizationOutcome);

    public static ActivateGenerationResult SlotTerminal() =>
        new(ActivateGenerationOutcome.SlotTerminal, null, GenerationAuthorizationOutcome.Ok);
}

/// <summary>
/// A stable source slot. The channel and <see cref="Intake"/> live for the
/// slot's lifetime; only the <see cref="SourceGeneration"/> swaps on restart /
/// reconfigure, so a route bound to <see cref="Intake"/> never goes stale.
/// </summary>
internal sealed class SourceSlot
{
    private const int DefaultCapacity = 1024;

    private readonly object _sync = new();
    private readonly SourceSlotGate _gate;
    private readonly Channel<CanonicalDataPoint> _channel;

    private SourceGeneration? _current;
    private bool _terminal;

    public SourceSlot(
        RuntimeInstanceId runtimeInstanceId,
        string sourceSlotId,
        SourceGenerationAllocator allocator,
        int capacity = DefaultCapacity)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceSlotId);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _gate = new SourceSlotGate(runtimeInstanceId, sourceSlotId, allocator);
        _channel = Channel.CreateBounded<CanonicalDataPoint>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,                  // review G5
                AllowSynchronousContinuations = false, // review G5
                FullMode = BoundedChannelFullMode.Wait,
            });

        SourceSlotId = sourceSlotId;
        Intake = new SourceSlotIntake(sourceSlotId, _channel.Reader);
    }

    /// <summary>The stable source-slot id.</summary>
    public string SourceSlotId { get; }

    /// <summary>The stable route-facing intake. Reference-stable across generations.</summary>
    public ISourceIntake Intake { get; }

    /// <summary>The current generation, or <c>null</c> when none is active.</summary>
    public SourceGeneration? CurrentGeneration
    {
        get { lock (_sync) { return _current; } }
    }

    /// <summary>Whether a generation currently holds publish authority (diagnostics only).</summary>
    public bool IsPublishAuthorized => _gate.IsPublishAuthorized;

    /// <summary>True once the slot has been permanently removed (channel completed; no further generations).</summary>
    public bool IsTerminal
    {
        get { lock (_sync) { return _terminal; } }
    }

    /// <summary>
    /// Phase 1 of activation: consume a generation id and build the scoped
    /// writer, WITHOUT making the candidate current. The adapter is initialized
    /// against the returned candidate while unauthorized; an initialization
    /// failure still consumed the id (the allocator advanced).
    /// </summary>
    public PrepareGenerationResult PrepareGeneration()
    {
        lock (_sync)
        {
            if (_terminal)
            {
                return PrepareGenerationResult.SlotTerminal();
            }

            var issue = _gate.IssueLease();
            if (!issue.IsOk)
            {
                return PrepareGenerationResult.Overflow();
            }

            var lease = issue.Lease!;
            var writer = new GenerationScopedIntakeWriter<CanonicalDataPoint>(_gate, lease, _channel.Writer);
            return PrepareGenerationResult.AsPrepared(new PreparedSourceGeneration(lease, writer));
        }
    }

    /// <summary>
    /// Phase 2 of activation: the SOLE atomic current-reference + authorization
    /// transition. On success the candidate becomes the current, publish-
    /// authorized generation; no observer ever sees a new current paired with
    /// stale authority. Rejects a stale or conflicting candidate, or a terminal slot.
    /// </summary>
    public ActivateGenerationResult TryActivate(PreparedSourceGeneration prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        lock (_sync)
        {
            if (_terminal)
            {
                return ActivateGenerationResult.SlotTerminal();
            }

            var authorization = _gate.TryAuthorize(prepared.Lease);
            if (authorization != GenerationAuthorizationOutcome.Ok)
            {
                return ActivateGenerationResult.AuthorizationFailed(authorization);
            }

            var generation = new SourceGeneration(prepared.Lease, prepared.Writer);
            _current = generation;
            return ActivateGenerationResult.Activated(generation);
        }
    }

    /// <summary>
    /// Irreversibly retire an initialization-failed prepared candidate (it was
    /// never current). Detaches its writer and drives its lease terminal so it
    /// can never later be activated.
    /// </summary>
    public GenerationAbandonOutcome AbandonPrepared(PreparedSourceGeneration prepared, RetirementReason reason)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        _ = reason; // recorded in generation history in a later Slice 0 commit
        lock (_sync)
        {
            var outcome = _gate.TryAbandonIssued(prepared.Lease);
            prepared.Writer.Detach();
            return outcome;
        }
    }

    /// <summary>
    /// Retire the current generation (ordinary stop/reconfigure): revoke publish
    /// authority (the linearization point) FIRST, then detach its writer. The
    /// stable channel is NOT completed — only permanent removal does that.
    /// </summary>
    public GenerationRetirementOutcome RetireCurrent(RetirementReason reason)
    {
        lock (_sync)
        {
            if (_current is null)
            {
                return GenerationRetirementOutcome.NotCurrent;
            }

            var outcome = _gate.TryRetire(_current.Lease, reason);
            if (outcome == GenerationRetirementOutcome.Ok)
            {
                _current.Writer.Detach();
                _current.MarkRetired();
                _current = null;
            }
            return outcome;
        }
    }

    /// <summary>
    /// Permanently remove the slot: atomically mark it terminal, retire any
    /// current generation, and complete the stable channel so the bound route
    /// observes end-of-stream. Idempotent. After this, prepare/activate are
    /// rejected and a re-add requires a brand-new <see cref="SourceSlot"/> and intake.
    /// </summary>
    /// <returns><c>true</c> if this call performed the terminal transition; <c>false</c> if already terminal.</returns>
    public bool CompleteIntakeForPermanentRemoval()
    {
        lock (_sync)
        {
            if (_terminal)
            {
                return false; // idempotent
            }

            if (_current is not null)
            {
                _gate.TryRetire(_current.Lease, RetirementReason.PermanentRemoval);
                _current.Writer.Detach();
                _current.MarkRetired();
                _current = null;
            }

            _terminal = true;
            _channel.Writer.TryComplete();
            return true;
        }
    }
}
