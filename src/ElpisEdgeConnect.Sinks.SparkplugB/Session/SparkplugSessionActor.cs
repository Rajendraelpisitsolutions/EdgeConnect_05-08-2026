// ============================================================================
// File: Session/SparkplugSessionActor.cs
// Purpose: The sole owner of ALL mutable Sparkplug protocol/session state
//          (plan v3 §1.1/§B2). Every Core-driven lifecycle call enters the actor's
//          single async serialization gate; the thin SparkplugSinkAdapter façade
//          forwards into it. The actor owns the coarse AdapterState + fine protocol
//          substate (one atomic snapshot), the config, the monotonic
//          connection-generation token, and — as ONE immutable ActiveSession record
//          promoted atomically only on NBIRTH success — the transport, session/epoch,
//          route/host, bdSeq, resolved manifest, and birth baseline.
//
//          Slice 4 (review r1): BeginReplaySessionAsync does full PREFLIGHT (plan +
//          alias-resolve + bdSeq-alias) BEFORE reserving bdSeq (bdSeq is the LAST
//          durable pre-CONNECT step); rejects a second Begin; issues a unique checked
//          generation per attempt (persisted BEFORE CONNECT, consumed even on failure);
//          latches a pre-authoritative disconnect (no Core rebirth before birth); and
//          promotes one immutable authority. A failed CONNECT/SUBSCRIBE/NBIRTH promotes
//          nothing, ABORTS (retires) the attempt so the broker publishes its Will, and
//          faults. Slices 5-6 fill Rebirth/Publish/CompleteCatchUp/EndSession.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.1, §1.2, §4, §6, §9.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Sinks.SparkplugB.Birth;
using ElpisEdgeConnect.Sinks.SparkplugB.Configuration;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
using ElpisEdgeConnect.Sinks.SparkplugB.Store;
using ElpisEdgeConnect.Sinks.SparkplugB.Topics;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;

/// <summary>
/// The single-owner Sparkplug session actor. All state mutation is serialized on the internal
/// gate; the façade never touches protocol state directly.
/// </summary>
public sealed class SparkplugSessionActor : IAsyncDisposable
{

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _instanceId;

    private readonly ISparkplugIdentityStateStore? _store;
    private readonly Func<ISparkplugMqttTransport>? _transportFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay; // injectable backoff (deterministic tests)

    // One immutable, atomically-published snapshot of (coarse, fine) state.
    private volatile ActorSnapshot _snapshot = new(AdapterState.Created, SparkplugProtocolState.Stopped);

    private SparkplugSinkConfiguration? _config;
    // The SINGLE disposal linearization point: installed atomically (Interlocked.CompareExchange) the
    // instant disposal wins ownership, BEFORE the gate is taken. Every gated surface reads it, so a call
    // that acquires the gate after ownership is won fails closed; concurrent Dispose callers await it.
    private Task? _disposeTask;

    // The identity token for the single in-flight transport-recovery loop (plan v3 §4.7). A lifecycle
    // call (End/Stop/Dispose) that runs while the recovery has released the gate for backoff nulls this,
    // so the recovery aborts on reacquire instead of racing a competing transition.
    private volatile object? _activeRecoveryToken;

    // The monotonic connection-generation token — every CONNECT attempt consumes a unique value,
    // whether or not it births. Distinct from the authoritative session's generation.
    private long _lastIssuedConnectionGeneration;

    // The Edge-Node modulo-256 message sequence counter. NBIRTH consumes seq 0 at promotion, so the
    // next NDATA is seq 1; advanced ONLY after a successful local publish (plan v3 §1.3). Reset to 1
    // atomically with each session promotion (gate-guarded).
    private int _nextSeq;

    // The single immutable authority, promoted atomically only after a successful NBIRTH. Declared
    // volatile so an asynchronous transport callback reads the published reference with acquire
    // semantics (the documented synchronization mechanism for this cross-thread field).
    private volatile ActiveSession? _activeSession;

    // The handoff of the CURRENTLY in-progress establishment attempt (set when the attempt's client+handoff
    // are created, cleared on promotion or abort). A transport callback is STALE unless its handoff is either
    // the authoritative session's handoff OR this in-progress one — the concrete transport echoes its own
    // captured generation, so a retired client's delayed callback matches its own handler and must be
    // recognised by identity, not by the generation argument alone (slice-7 review B2).
    private volatile AttemptHandoff? _establishingHandoff;

    // --- Slice 7 diagnostics (plan v3 §8): monotonic lifetime counters, per actor instance, NEVER
    // reset on rebirth. Interlocked for the ones touched from asynchronous MQTT callbacks; the rest are
    // gate-owned but use Interlocked consistently so the health snapshot reads them coherently. ---
    private long _staleDisconnectCallbacks;
    private long _staleNodeCommandCallbacks;
    private long _nodeCommandsIgnored;
    private long _rebirthRequestsQueued;
    private long _rebirthRequestsCoalesced;
    private long _healthyRebirths;
    private long _transportRecoveryStarts;
    private long _transportRecoveryAttempts;
    private long _transportRecoverySuccesses;
    private long _transportRecoveryExhaustions;
    private long _publishFailures;
    private long _birthFailures;
    private long _deathPublishFailures;

    // Last-event timestamps as UtcTicks (0 = never); Interlocked.Exchange/Read for 64-bit atomicity.
    private long _lastSuccessfulBirthAtTicks;
    private long _lastDataPublishAtTicks;
    private long _lastRebirthRequestAtTicks;
    private long _lastErrorAtTicks;

    // The coherent SEMANTIC snapshot (plan v3 §8, slice-7 review B3): the lifecycle/protocol/session fields
    // that must be mutually consistent, published as a single volatile reference ONLY at a completed gated
    // transition (under _diagLock). A reader NEVER reconstructs semantic state from independent field reads;
    // it takes this last coherent record and overlays the independent, monotonic counters/timestamps/last-event
    // values (each read atomically). So no torn combination is ever observed, and Version is a transition
    // change-token that does NOT advance on a mere read.
    private readonly object _diagLock = new();
    private long _diagVersion;
    private int _currentRecoveryAttempt;                                             // current-state (semantic; gated)
    private volatile string? _lastRecoveryFailureCode;                               // last-event overlay (read live)
    private volatile string? _lastNodeCommandDiagnosticCode;                         // last-event overlay (read live)
    private volatile AdapterError? _lastError;                                       // sanitized; last-event overlay
    private AdapterState _priorState = AdapterState.Created;                          // state before the last transition
    private SparkplugProtocolState _priorProtocol = SparkplugProtocolState.Stopped;
    private AdapterState _lastObservedState = AdapterState.Created;                   // most recent state (change detector)
    private SparkplugProtocolState _lastObservedProtocol = SparkplugProtocolState.Stopped;
    private DateTimeOffset _lastStateChangeAt;
    private string _lastTransitionReasonCode = "created";
    private volatile SparkplugActorDiagnostics? _semantic;

    /// <summary>Construct a lifecycle-only actor (no identity store — cannot begin a session). Test/internal.</summary>
    /// <param name="instanceId">The sink instance id (non-empty).</param>
    internal SparkplugSessionActor(string instanceId)
        : this(instanceId, store: null, transportFactory: null, clock: null)
    {
    }

    /// <summary>
    /// Construct a production actor with the injected gateway identity store (K4). The store's
    /// eager constructor validation IS its readiness. Uses the real MQTTnet transport.
    /// </summary>
    /// <param name="instanceId">The sink instance id (non-empty).</param>
    /// <param name="store">The gateway identity store singleton (owned by K4; never disposed here).</param>
    public SparkplugSessionActor(string instanceId, ISparkplugIdentityStateStore store)
        : this(instanceId, store ?? throw new ArgumentNullException(nameof(store)), () => new SparkplugMqttTransport(), clock: null)
    {
    }

    // Test seam: inject a fake transport factory, a deterministic clock, and/or a controllable backoff delay.
    internal SparkplugSessionActor(
        string instanceId,
        ISparkplugIdentityStateStore? store,
        Func<ISparkplugMqttTransport>? transportFactory,
        Func<DateTimeOffset>? clock,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        _instanceId = instanceId;
        _store = store;
        _transportFactory = transportFactory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Test seam awaited once while the gate is held during <see cref="StartAsync"/>.</summary>
    internal Func<CancellationToken, Task>? GateHeldProbe { get; set; }

    /// <summary>
    /// Test seam awaited once immediately BEFORE the promotion compare-exchange (disconnect-race
    /// coverage). Lets a test deterministically interleave a Disconnected callback with the handoff.
    /// </summary>
    internal Func<Task>? PrePromotionBarrier { get; set; }

    /// <summary>
    /// Test seam awaited once immediately BEFORE the cutover-to-Live commit (review r1 B4 race
    /// coverage). Lets a test interleave an async disconnect with the Live compare-exchange.
    /// </summary>
    internal Func<Task>? PreLiveCommitBarrier { get; set; }

    /// <summary>
    /// Test seam awaited once AFTER the establishment promotion CAS but BEFORE _activeSession is
    /// published (slice-6 review r1 B4). Lets a test land a disconnect in that window and prove the
    /// establishment drains exactly one rebirth request after publication.
    /// </summary>
    internal Func<Task>? PostPromotionBarrier { get; set; }

    /// <summary>
    /// Test seam awaited once immediately BEFORE the healthy rebirth-completion compare-exchange
    /// (slice-6 review r1 B2). Lets a test interleave an async disconnect with the completion.
    /// </summary>
    internal Func<Task>? PreRebirthCommitBarrier { get; set; }

    /// <summary>
    /// Test seam awaited once AFTER a healthy rebirth wins (RebirthCommitting) but BEFORE the new authority
    /// is published/finished (slice-6 review r2 R2.2 → r3 R3.1). Lets a test inject a control event/disconnect
    /// in the commit window and prove it re-arms a fresh episode requested against the NEW epoch, never erased.
    /// </summary>
    internal Func<Task>? PostRebirthCommitBarrier { get; set; }

    /// <summary>
    /// Test seam awaited once in the healthy-rebirth authority-swap window: AFTER the new-epoch
    /// <c>_activeSession</c> is published but BEFORE the semantic diagnostic root is republished. Lets a test
    /// prove the live control overlay binds on the full authority (session+epoch+generation), so a new-epoch
    /// control condition is not attached to the old-epoch semantic root (slice-7 review r3 R3.1).
    /// </summary>
    internal Func<Task>? PostAuthorityPublishBarrier { get; set; }

    /// <summary>The coarse adapter lifecycle state (the ISinkAdapter contract surface).</summary>
    public AdapterState State => _snapshot.State;

    /// <summary>The fine protocol substate (internal diagnostics only).</summary>
    public SparkplugProtocolState ProtocolState => _snapshot.ProtocolState;

    // Internal accessors used by tests to verify atomic promotion (and non-promotion on failure),
    // consumed by the slice-5 publish path.
    internal bool HasSession => _activeSession is not null;
    internal long LastIssuedGeneration => _lastIssuedConnectionGeneration;
    internal long CurrentGeneration => _activeSession?.TransportGeneration ?? 0;
    internal ReplaySessionId CurrentSessionId => _activeSession?.SessionId ?? default;
    internal ReplayEpochId CurrentEpoch => _activeSession?.Epoch ?? default;
    internal string? CurrentRouteId => _activeSession?.RouteId;
    internal SparkplugBirthDeathSequence CurrentBdSeq => _activeSession?.BdSeq ?? default;
    internal ResolvedSparkplugBirthPlan? CurrentManifest => _activeSession?.Manifest;
    internal SparkplugBirthBaseline? CurrentBaseline => _activeSession?.Baseline;
    internal IReplaySessionHost? CurrentHost => _activeSession?.Host;

    // True once a disconnect for the active session's generation arrived AFTER promotion. The
    // operational recovery path (slice 6) consumes this; slice 4 only proves the drop is not lost.
    internal bool CurrentSessionSuspect => _activeSession?.Handoff.SuspectAfterPromotion ?? false;

    // The next seq the actor will place on an NDATA (0..255); NBIRTH consumed 0, so this is 1 after Begin.
    internal int NextSeq => _nextSeq;

    /// <summary>Validate and store the configuration (slice-1 review B1).</summary>
    /// <param name="config">The sink configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the actor is initialized.</returns>
    public async Task InitializeAsync(SinkConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed(); // fail closed after disposal has won (r2 R3)
            SetAdapterState(AdapterState.Initializing);

            var validation = SparkplugSinkConfigurationValidator.Validate(config);
            if (!validation.IsValid)
            {
                var issue = validation.Errors[0];
                var configError = AdapterException.Configuration(issue.Code, issue.Message);
                SetFaulted(configError.Error); // records the sanitized code/category only, never the message
                throw configError;
            }

            _config = (SparkplugSinkConfiguration)config;
            SetAdapterState(AdapterState.Initialized);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Start adapter-local resources. A production actor's store readiness is established by the
    /// store's eager construction (a public adapter always has one). Does NOT connect or birth.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the actor is running.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed(); // fail closed after disposal without faulting the terminal state (r2 R2.3)
            try
            {
                SetAdapterState(AdapterState.Starting);
                if (GateHeldProbe is { } probe)
                {
                    await probe(cancellationToken).ConfigureAwait(false);
                }

                SetAdapterState(AdapterState.Running);
            }
            catch (Exception ex)
            {
                SetFaulted(AsAdapterError(ex));
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stop adapter-local resources. Forgiving: Initialized/Failed go straight to Stopped;
    /// Running/Degraded pass through Stopping; Created and Stopped/Stopping are no-ops. Slice 4
    /// retires any active transport (graceful NDEATH is slice 6).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the actor has stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DisposalWon)
            {
                return; // Stop after disposal has won is an idempotent no-op (terminal state stands)
            }

            _activeRecoveryToken = null; // supersede any in-flight recovery loop
            switch (_snapshot.State)
            {
                case AdapterState.Stopped:
                case AdapterState.Stopping:
                case AdapterState.Created:
                    return;

                case AdapterState.Initialized:
                case AdapterState.Failed:
                    await RetireActiveSessionAsync().ConfigureAwait(false);
                    SetState(AdapterState.Stopped, SparkplugProtocolState.Stopped);
                    return;

                default:
                    SetAdapterState(AdapterState.Stopping);
                    await RetireActiveSessionAsync().ConfigureAwait(false);
                    SetState(AdapterState.Stopped, SparkplugProtocolState.Stopped);
                    return;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Build a coherent, session-aware 3-way health snapshot.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The health snapshot.</returns>
    public Task<AdapterHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AdapterHealth>(cancellationToken);
        }

        // One coherent read (plan v3 §8): the lifecycle/protocol/session fields come from a single semantic
        // snapshot (never reconstructed from a torn read), so health can never report a combination that never
        // existed.
        var diag = DiagnosticsSnapshot;

        // Health uses the lifecycle state, HasSession, AND the live operational-control overlay (slice-7 review
        // r2 R2.1). Any active control condition — disposal in progress, a suspect transport, or a pending
        // rebirth (e.g. a valid NCMD that latched the control episode and is blocking DATA) — is Degraded, even
        // over a Live/Stopped protocol. Healthy is ONLY ready-no-session or active-Live with no such condition.
        var degradedControl = diag.TerminalDisposed || diag.SuspectTransport || diag.PendingRebirth;
        var level = diag.State switch
        {
            AdapterState.Failed => HealthLevel.Unhealthy,
            AdapterState.Running when !degradedControl && diag.ProtocolState is SparkplugProtocolState.Stopped && !diag.HasSession
                => HealthLevel.Healthy,
            AdapterState.Running when !degradedControl && diag.ProtocolState is SparkplugProtocolState.Live && diag.HasSession
                => HealthLevel.Healthy,
            AdapterState.Running => HealthLevel.Degraded,
            AdapterState.Degraded => HealthLevel.Degraded,
            _ => HealthLevel.Unknown,
        };

        // Stable metric keys — Studio, support bundles, and K4 meters may depend on them. Redacted by
        // construction: only ids/generations/bdSeq/epoch/counts/timestamps/sanitized-codes, never a
        // credential, endpoint, client id, topic, payload byte, or metric value.
        var metrics = new Dictionary<string, object>
        {
            ["protocolState"] = diag.ProtocolState.ToString(),
            ["hasSession"] = diag.HasSession,
            ["lastIssuedGeneration"] = diag.LastIssuedGeneration,
            ["diagnosticsVersion"] = diag.Version,
            ["lastTransitionReasonCode"] = diag.LastTransitionReasonCode,
            ["previousState"] = diag.PreviousState.ToString(),
            ["previousProtocolState"] = diag.PreviousProtocolState.ToString(),
            ["terminalDisposed"] = diag.TerminalDisposed,
            ["suspectTransport"] = diag.SuspectTransport,
            ["pendingRebirth"] = diag.PendingRebirth,
            ["currentRecoveryAttempt"] = diag.CurrentRecoveryAttempt,
            ["recoveryAttemptBudget"] = diag.RecoveryAttemptBudget,
            ["lastStateChangeAt"] = diag.LastStateChangeAt.UtcDateTime,
            ["staleDisconnectCallbacks"] = diag.StaleDisconnectCallbacks,
            ["staleNodeCommandCallbacks"] = diag.StaleNodeCommandCallbacks,
            ["nodeCommandsIgnored"] = diag.NodeCommandsIgnored,
            ["rebirthRequestsQueued"] = diag.RebirthRequestsQueued,
            ["rebirthRequestsCoalesced"] = diag.RebirthRequestsCoalesced,
            ["healthyRebirths"] = diag.HealthyRebirths,
            ["transportRecoveryStarts"] = diag.TransportRecoveryStarts,
            ["transportRecoveryAttempts"] = diag.TransportRecoveryAttempts,
            ["transportRecoverySuccesses"] = diag.TransportRecoverySuccesses,
            ["transportRecoveryExhaustions"] = diag.TransportRecoveryExhaustions,
            ["publishFailures"] = diag.PublishFailures,
            ["birthFailures"] = diag.BirthFailures,
            ["deathPublishFailures"] = diag.DeathPublishFailures,
        };
        if (diag.HasSession)
        {
            metrics["sessionId"] = diag.SessionId!.Value;
            metrics["epoch"] = diag.Epoch!.Value;
            metrics["connectionGeneration"] = diag.ConnectionGeneration!.Value;
            metrics["bdSeq"] = diag.BdSeq!.Value;
            metrics["nextSeq"] = diag.NextSeq!.Value;
        }
        if (diag.PendingRebirthReason is { } reason) { metrics["pendingRebirthReason"] = reason; }
        if (diag.LastNodeCommandDiagnosticCode is { } ncmdCode) { metrics["lastNodeCommandDiagnosticCode"] = ncmdCode; }
        if (diag.LastRecoveryFailureCode is { } recoveryFailure) { metrics["lastRecoveryFailureCode"] = recoveryFailure; }
        if (diag.LastErrorCode is { } errorCode) { metrics["lastErrorCode"] = errorCode; }
        if (diag.LastErrorCategory is { } errorCategory) { metrics["lastErrorCategory"] = errorCategory; }
        AddInstant(metrics, "lastSuccessfulBirthAt", diag.LastSuccessfulBirthAt);
        AddInstant(metrics, "lastDataPublishAt", diag.LastDataPublishAt);
        AddInstant(metrics, "lastRebirthRequestAt", diag.LastRebirthRequestAt);
        AddInstant(metrics, "lastErrorAt", diag.LastErrorAt);

        return Task.FromResult(new AdapterHealth
        {
            State = diag.State,
            Level = level,
            CheckedAt = _clock().UtcDateTime,
            LastSuccessAt = diag.LastSuccessfulBirthAt?.UtcDateTime,
            LastError = diag.LastError,
            Metrics = metrics,
            Detail = diag.LastTransitionReasonCode,
        });
    }

    private static void AddInstant(IDictionary<string, object> metrics, string key, DateTimeOffset? instant)
    {
        if (instant is { } value)
        {
            metrics[key] = value.UtcDateTime;
        }
    }

    /// <summary>
    /// Begin a new replay session (slice 4). See the file header for the full ordered contract.
    /// </summary>
    /// <param name="start">The session-start inputs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the birth is emitted.</returns>
    public async Task BeginReplaySessionAsync(ReplaySessionStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed(); // a Begin queued behind Dispose (or after it) must NOT resurrect the actor (r2 R2.3)
            RequireReadyForSession();
            var candidate = await EstablishNewConnectionAsync(
                start.SessionId, start.Epoch, start.RouteId, start.Host, start.State.Snapshot, cancellationToken)
                .ConfigureAwait(false);
            await PromoteAndDrainAsync(candidate).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            throw; // disposed: fail closed without faulting/mutating the terminal state
        }
        catch (Exception ex)
        {
            SetFaulted(AsAdapterError(ex)); // promote nothing; the driver faults the route; the previous epoch stands
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The shared new-CONNECT establishment core (slice 4, refactored for slice 6 review r1 B3): full
    // preflight, then the generation-exhaustion check BEFORE the durable bdSeq reservation
    // (carry-forward 2), then CONNECT -> exact NCMD SUBSCRIBE -> NBIRTH -> initial promotion CAS. It
    // RETURNS a candidate authority and does NOT write _activeSession, so a failed candidate never erases
    // the previously successful authority; the caller promotes it. On failure it aborts (retires) the
    // attempt so the broker publishes its Will. Callers hold the gate and fault on throw.
    private async Task<ActiveSession> EstablishNewConnectionAsync(
        ReplaySessionId sessionId, ReplayEpochId epoch, string routeId, IReplaySessionHost host,
        LatestValueSnapshot snapshot, CancellationToken cancellationToken)
    {
        var prepared = PrepareBirth(snapshot);
        return await AttemptConnectionAsync(
            prepared, sessionId, epoch, routeId, host, recoveryToken: null, recoveryAttemptOrdinal: 0, cancellationToken)
            .ConfigureAwait(false);
    }

    // The NON-RETRYABLE birth preparation (slice-6 review r1 B1): snapshot planning, alias-store resolution,
    // and the immutable manifest/baseline + bdSeq alias. A failure here (unmappable/pre-epoch snapshot,
    // store/alias/config invariant, alias overflow) is deterministic and fatal — the recovery loop runs it
    // ONCE, never sleeps-and-retries it. No durable side effect (no bdSeq reserved, no transport created).
    private PreparedBirth PrepareBirth(LatestValueSnapshot snapshot)
    {
        var node = NodeIdentity();
        var storeIdentity = SparkplugStoreIdentity.Create(_config!.ResolveBrokerEndpoint(), node);

        SetProtocolState(SparkplugProtocolState.LoadingSession);
        var plan = SparkplugBirthPlanner.Plan(snapshot);
        var aliases = _store!.ResolveAliases(storeIdentity, plan.ManifestKeys);
        var resolved = SparkplugBirthPlanner.Resolve(plan, aliases);
        var bdSeqAlias = resolved.AliasMap.Count == 0
            ? 1UL
            : checked(resolved.AliasMap.Values.Max() + 1UL);
        var baseline = SparkplugBirthBaseline.FromResolvedPlan(resolved);
        return new PreparedBirth(node, storeIdentity, resolved, bdSeqAlias, baseline);
    }

    // ONE bounded, RETRYABLE transport attempt: the generation-exhaustion check (fatal, before bdSeq so it
    // consumes none), a fresh durable bdSeq, the Will/connect request, a fresh client + generation, then
    // CONNECT -> exact NCMD SUBSCRIBE -> NBIRTH -> promotion CAS. Returns a candidate (does NOT write
    // _activeSession); on failure it aborts (retires) the attempt so the broker publishes its Will. Only a
    // TRANSPORT failure of this method is retryable (see IsRetryableEstablishmentFailure).
    private async Task<ActiveSession> AttemptConnectionAsync(
        PreparedBirth prepared, ReplaySessionId sessionId, ReplayEpochId epoch, string routeId, IReplaySessionHost host,
        object? recoveryToken, int recoveryAttemptOrdinal, CancellationToken cancellationToken)
    {
        var config = _config!;
        var node = prepared.Node;

        // Ownership contract (review r5 / r5.1 design ruling): disposal (or a superseding lifecycle call)
        // prevents ADMISSION of a new establishment attempt — it does NOT interrupt one already admitted under
        // the actor gate. This re-check rejects an attempt that has not yet passed this point; an attempt
        // already past it may finish or abort, and any committed-but-unused bdSeq / generation gap is
        // intentional (monotonic, never reused), with disposal retiring any resulting transport before it
        // completes. This is defense-in-depth narrowing, NOT a hard no-gap linearization guarantee. Begin
        // (null token) fails closed with ObjectDisposedException (its non-faulting passthrough); a recovery
        // attempt validates its token (aborts with OperationCanceledException).
        if (recoveryToken is null)
        {
            ThrowIfDisposed();
        }
        else
        {
            ValidateRecoveryOwnership(recoveryToken);
        }

        // Generation exhaustion is checked BEFORE reserving a durable bdSeq (carry-forward 2 / B1), so the
        // terminal long.MaxValue case can never consume a bdSeq with no possible CONNECT, and it is fatal.
        if (_lastIssuedConnectionGeneration == long.MaxValue)
        {
            throw new AdapterException(new AdapterError
            {
                Code = SparkplugErrors.GenerationOverflow,
                Category = ErrorCategory.Internal,
                Message = "the connection-generation counter is exhausted.",
                Retryable = false,
            });
        }

        // --- bdSeq is the LAST durable pre-CONNECT operation (committed before it returns) ---
        var bdSeq = _store!.ReserveNextBdSeq(prepared.StoreIdentity);

        var connectRequest = SparkplugMqttConnectRequest.Create(
            config.ResolveBrokerEndpoint(), ResolveClientId(config), config.Username, config.Password, config.KeepAliveSeconds,
            cleanSession: true, SparkplugTopicFactory.NDeath(node), SparkplugPayloadEncoder.EncodeNDeath(bdSeq));

        var generation = _lastIssuedConnectionGeneration + 1;
        _lastIssuedConnectionGeneration = generation;

        ISparkplugMqttTransport? attempt = _transportFactory!();
        var handoff = new AttemptHandoff(generation);
        _establishingHandoff = handoff; // this attempt is now the in-progress one (stale-callback identity, B2)
        var disconnectHandler = MakeDisconnectHandler(generation, handoff);
        var nodeCommandHandler = MakeNodeCommandHandler(generation, handoff);
        attempt.Disconnected += disconnectHandler;
        attempt.NodeCommandReceived += nodeCommandHandler;

        // The REAL recovery-attempt boundary (slice-7 review r2 R2.2): count the lifetime tally and publish the
        // ordinal only AFTER a bdSeq was reserved, the request built, and the client created — right before
        // CONNECT. A store/generation/factory failure (fatal preparation) or a disposal-rejected admission
        // therefore records NO "complete CONNECT/SUBSCRIBE/NBIRTH attempt"; a CONNECT failure counts as one.
        if (recoveryToken is not null)
        {
            Interlocked.Increment(ref _transportRecoveryAttempts);
            _currentRecoveryAttempt = recoveryAttemptOrdinal;
            PublishTransition("recovery-attempt");
        }

        try
        {
            SetProtocolState(SparkplugProtocolState.Connecting);
            await attempt.ConnectAsync(connectRequest, generation, cancellationToken).ConfigureAwait(false);
            RequireNotInvalidated(handoff);

            SetProtocolState(SparkplugProtocolState.SubscribingNcmd);
            await attempt.SubscribeExactAsync(SparkplugTopicFactory.NCmdSubscribe(node), cancellationToken).ConfigureAwait(false);
            RequireNotInvalidated(handoff);

            SetProtocolState(SparkplugProtocolState.Birthing);
            var nbirth = SparkplugPayloadEncoder.EncodeNBirth(
                SparkplugSequenceNumber.Create(0), bdSeq, prepared.BdSeqAlias, _clock(), prepared.Resolved.Metrics, prepared.Resolved.AliasMap);
            bool published;
            try
            {
                published = await attempt.PublishAsync(SparkplugTopicFactory.NBirth(node), nbirth, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _birthFailures); // in-transport NBIRTH cancellation (uncertain send, B4)
                throw;
            }
            if (!published)
            {
                Interlocked.Increment(ref _birthFailures); // NBIRTH publish failed (plan v3 §8)
                throw BirthPublishFailed();
            }

            // Deterministic race barrier immediately before the initial promotion compare-exchange.
            if (PrePromotionBarrier is { } barrier)
            {
                await barrier().ConfigureAwait(false);
            }

            var candidate = new ActiveSession(
                attempt, generation, sessionId, epoch, routeId, host, bdSeq, prepared.Resolved, prepared.Baseline, handoff);
            if (!handoff.TryPromote())
            {
                throw SessionSuspectDuringBegin(); // a disconnect won the race; install no session
            }

            attempt = null; // ownership transferred to the candidate; the caller publishes it
            return candidate;
        }
        finally
        {
            if (attempt is not null)
            {
                // This attempt failed/aborted: it is no longer the in-progress establishment.
                Interlocked.CompareExchange(ref _establishingHandoff, null, handoff);
                attempt.Disconnected -= disconnectHandler;
                attempt.NodeCommandReceived -= nodeCommandHandler;
                // ABORT: dispose without a clean DISCONNECT so the broker publishes the Will (NDEATH).
                try { await attempt.DisposeAsync().ConfigureAwait(false); }
                catch { /* retiring a failed attempt */ }
            }
        }
    }

    // Only a TRANSPORT failure of a complete establishment attempt is retryable within the recovery budget
    // (slice-6 review r1 B1). Store/mapping/alias/config/generation failures are deterministic and fatal.
    private static bool IsRetryableEstablishmentFailure(Exception ex) =>
        ex is AdapterException adapterException && adapterException.Error.Code is
            SparkplugErrors.TransportConnectFailed
            or SparkplugErrors.TransportSubscribeFailed
            or SparkplugErrors.BirthPublishFailed
            or SparkplugErrors.SessionSuspectDuringBegin;

    private sealed record PreparedBirth(
        SparkplugEdgeNodeIdentity Node,
        SparkplugStoreIdentity StoreIdentity,
        ResolvedSparkplugBirthPlan Resolved,
        ulong BdSeqAlias,
        SparkplugBirthBaseline Baseline);

    // Publish a freshly-established candidate as the authoritative session and drain any rebirth request a
    // disconnect/NCMD marked while _activeSession did not yet exist (slice-6 review r1 B4). The barrier is
    // a deterministic seam AFTER the promotion CAS but BEFORE publication.
    private async Task PromoteAndDrainAsync(ActiveSession candidate)
    {
        if (PostPromotionBarrier is { } barrier)
        {
            await barrier().ConfigureAwait(false);
        }

        _activeSession = candidate; // volatile publish
        Interlocked.CompareExchange(ref _establishingHandoff, null, candidate.Handoff); // now authoritative, not in-progress
        _nextSeq = 1;               // NBIRTH consumed seq 0
        Interlocked.Exchange(ref _lastSuccessfulBirthAtTicks, _clock().UtcTicks); // NBIRTH succeeded (plan v3 §8)
        // Normalize the diagnostic substate: a candidate that became suspect between promotion and
        // publication reports Suspect, not Replaying (pass-1 r3 carry-forward).
        SetProtocolState(candidate.Handoff.SuspectAfterPromotion
            ? SparkplugProtocolState.Suspect
            : SparkplugProtocolState.Replaying);
        // Drain any rebirth a disconnect/NCMD marked while _activeSession did not yet exist; the drain
        // derives the correct reason (HostCommand/SchemaChange/Other) from the episode (r2 R2.3).
        await DrainRebirthAsync(candidate.Handoff).ConfigureAwait(false);
    }

    /// <summary>
    /// Same-session rebirth (slice 6). Retains the <see cref="ReplaySessionId"/>, requires a strictly
    /// increasing epoch, and branches on the actor-owned latch: a HEALTHY transport re-emits NBIRTH on
    /// the existing connection (retaining bdSeq, atomic completion vs. a racing drop); a SUSPECT transport
    /// (or a drop that races the healthy completion) abandons the client and establishes a fresh
    /// connection with a new bdSeq while the previous authority is preserved until success.
    /// </summary>
    /// <param name="rebirth">The rebirth inputs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the re-birth is emitted.</returns>
    public async Task RebirthAsync(ReplaySessionRebirth rebirth, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rebirth);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed(); // fail closed after disposal without faulting the terminal state (r2 R2.3)
            try
            {
                await RebirthGatedAsync(rebirth, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetFaulted(AsAdapterError(ex));
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RebirthGatedAsync(ReplaySessionRebirth rebirth, CancellationToken cancellationToken)
    {
        var session = RequireActiveSession();

        if (rebirth.SessionId.Value != session.SessionId.Value)
        {
            throw Typed(SparkplugErrors.PublishSessionMismatch,
                $"the rebirth session ({rebirth.SessionId.Value}) is not the actor's authoritative session ({session.SessionId.Value}).");
        }

        if (rebirth.Epoch.Value <= session.Epoch.Value)
        {
            throw Typed(SparkplugErrors.PublishEpochMismatch,
                $"a rebirth epoch ({rebirth.Epoch.Value}) must strictly exceed the current epoch ({session.Epoch.Value}).");
        }

        var snapshot = rebirth.State.Snapshot;

        // The actor OWNS the healthy-vs-suspect decision via its latch, never the public reason. When a
        // host command and a transport loss coalesce, transport-suspect wins (a new CONNECT).
        if (session.Handoff.SuspectAfterPromotion || !session.Handoff.TryBeginRebirth())
        {
            await SuspectRebirthAsync(session, rebirth.SessionId, rebirth.Epoch, snapshot, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // HEALTHY branch: the handoff is now Rebirthing. Reuse the connection + bdSeq; re-emit NBIRTH seq=0.
        var node = NodeIdentity();
        var storeIdentity = SparkplugStoreIdentity.Create(_config!.ResolveBrokerEndpoint(), node);

        SetProtocolState(SparkplugProtocolState.Rebirthing);
        var plan = SparkplugBirthPlanner.Plan(snapshot);
        var aliases = _store!.ResolveAliases(storeIdentity, plan.ManifestKeys);
        var resolved = SparkplugBirthPlanner.Resolve(plan, aliases);
        var bdSeqAlias = resolved.AliasMap.Count == 0
            ? 1UL
            : checked(resolved.AliasMap.Values.Max() + 1UL);
        var baseline = SparkplugBirthBaseline.FromResolvedPlan(resolved);

        var nbirth = SparkplugPayloadEncoder.EncodeNBirth(
            SparkplugSequenceNumber.Create(0), session.BdSeq, bdSeqAlias, _clock(), resolved.Metrics, resolved.AliasMap);
        // Uncertain-send boundary (r2 R2.4): an in-transport cancellation/exception marks the reused handoff
        // suspect (Rebirthing -> Suspect) and never strands it in Rebirthing. A clean local false with no
        // transport loss stays a genuine (fatal) NBIRTH failure.
        var published = await SendAsync(
            session, SparkplugTopicFactory.NBirth(node), nbirth,
            () => Interlocked.Increment(ref _birthFailures), cancellationToken).ConfigureAwait(false);
        if (!published)
        {
            // Count ANY non-completed NBIRTH — a genuine local failure (fatal below) OR an uncertain send that
            // pivots to transport recovery when the drop already latched suspect (slice-7 review B4).
            Interlocked.Increment(ref _birthFailures);
            if (!session.Handoff.SuspectAfterPromotion)
            {
                throw BirthPublishFailed(); // a genuine local NBIRTH failure with no transport loss is fatal (§4.5)
            }
        }

        // Deterministic race barrier immediately before the atomic rebirth-completion compare-exchange.
        if (PreRebirthCommitBarrier is { } barrier)
        {
            await barrier().ConfigureAwait(false);
        }

        if (!session.Handoff.TryCompleteRebirth())
        {
            // A disconnect/send loss won during the rebirth: do NOT install the new epoch; pivot to the
            // transport-suspect new-CONNECT branch (a host command + transport loss coalesces to suspect).
            await SuspectRebirthAsync(session, rebirth.SessionId, rebirth.Epoch, snapshot, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // The rebirth won: state is RebirthCommitting and the OLD episode is consumed. Any control event
        // arriving from here until FinishRebirthCommit re-arms a FRESH episode whose queue is suppressed
        // (r3 R3.1), so it can only be requested against the NEW authority below.
        if (PostRebirthCommitBarrier is { } commitBarrier)
        {
            await commitBarrier().ConfigureAwait(false);
        }

        // Publish the new authority BEFORE finishing the commit, so the drained fresh episode queues
        // against the new epoch.
        _activeSession = session with { Epoch = rebirth.Epoch, Manifest = resolved, Baseline = baseline };
        _nextSeq = 1; // the re-birth NBIRTH consumed seq 0
        Interlocked.Increment(ref _healthyRebirths); // a healthy in-place NBIRTH re-announcement committed (plan v3 §8)
        Interlocked.Exchange(ref _lastSuccessfulBirthAtTicks, _clock().UtcTicks);

        // Deterministic seam INSIDE the authority-swap window: _activeSession is now the NEW epoch but the
        // semantic root is not yet republished (slice-7 review r3 R3.1 — proves the diagnostic overlay binds on
        // the full authority and does not attach new-epoch control to the old semantic root).
        if (PostAuthorityPublishBarrier is { } authorityBarrier)
        {
            await authorityBarrier().ConfigureAwait(false);
        }

        // Finish the commit (RebirthCommitting -> Active, or leave Suspect if a drop raced) and drain any
        // fresh episode a control event opened during the commit — against the new authoritative epoch.
        var freshPending = session.Handoff.FinishRebirthCommit();
        // Normalize the diagnostic substate: a drop during the commit reports Suspect, not Replaying
        // (pass-1 r3 carry-forward).
        SetProtocolState(session.Handoff.SuspectAfterPromotion
            ? SparkplugProtocolState.Suspect
            : SparkplugProtocolState.Replaying);
        if (freshPending)
        {
            await DrainRebirthAsync(session.Handoff).ConfigureAwait(false);
        }
    }

    // The transport-suspect rebirth: retire the old client (broker publishes its Will) but PRESERVE the
    // previous authority in _activeSession until a fresh candidate succeeds (slice-6 review r1 B3). On
    // establishment failure the previous epoch remains the recorded authority.
    private async Task SuspectRebirthAsync(
        ActiveSession previous, ReplaySessionId sessionId, ReplayEpochId epoch, LatestValueSnapshot snapshot, CancellationToken cancellationToken)
    {
        // Claim recovery ownership ATOMICALLY, BEFORE the first await (review r5). The token must exist before
        // the initial suspect-transport retirement AND before birth preparation, so a disposal that wins during
        // either of those awaits is observed by ValidateRecoveryOwnership and aborts the recovery — it can never
        // resume past an await and install a fresh token, reserve a bdSeq, issue a generation or open a
        // transport after disposal has won. The CAS also enforces the single-recovery invariant (plan v3 §4.7 /
        // slice-6 review r1 B2 → r2 R2.1): a second recovery entering while one is in flight fails the CAS and
        // is rejected NONFATALLY (OperationCanceledException), so RebirthAsync's OCE passthrough does NOT
        // SetFaulted and the original recovery A is left intact, regardless of B's epoch.
        var token = new object();
        if (Interlocked.CompareExchange(ref _activeRecoveryToken, token, null) is not null)
        {
            throw new OperationCanceledException(
                "a transport recovery is already in flight for this actor (single-recovery invariant).");
        }

        Interlocked.Increment(ref _transportRecoveryStarts); // one recovery episode started (token claimed)
        try
        {
            ValidateRecoveryOwnership(token); // a disposal that won BEFORE we claimed the token aborts here

            SetProtocolState(SparkplugProtocolState.RecoveringTransport);
            try { await previous.Transport.DisposeAsync().ConfigureAwait(false); }
            catch { /* retiring the suspect client */ }

            ValidateRecoveryOwnership(token); // a disposal DURING the initial retirement aborts before PrepareBirth

            // Prepare the birth ONCE (non-retryable): a snapshot/store/alias/config failure fails immediately
            // rather than sleeping-and-retrying a deterministic error (slice-6 review r1 B1).
            var prepared = PrepareBirth(snapshot);

            // The bounded complete-ATTEMPT recovery (plan v3 §4.6/§4.7): retry only a TRANSPORT failure of the
            // full CONNECT/SUBSCRIBE/NBIRTH attempt within the frozen budget, each attempt consuming a distinct
            // generation + bdSeq, with capped exponential backoff (no jitter). Backoff releases the actor gate
            // under the recovery token; a lifecycle call (End/Stop/Dispose/cancel) invalidates it so we abort.
            var config = _config!;
            var maxAttempts = Math.Max(1, config.TransportRecoveryMaxAttempts);
            var delayMs = config.TransportRecoveryInitialDelayMs;
            var maxDelayMs = config.TransportRecoveryMaxDelayMs;

            for (var attempt = 1; ; attempt++)
            {
                ValidateRecoveryOwnership(token); // and before EACH attempt — no CONNECT/bdSeq/generation after loss

                // The lifetime attempts tally + the current ordinal are published INSIDE AttemptConnectionAsync
                // at the real attempt boundary (after bdSeq/request/client, before CONNECT), so a fatal
                // preparation (store/generation/factory) or a disposal-rejected admission records no attempt
                // and advertises no ordinal (slice-7 review r2 R2.2).
                try
                {
                    var candidate = await AttemptConnectionAsync(
                        prepared, sessionId, epoch, previous.RouteId, previous.Host, token, attempt, cancellationToken).ConfigureAwait(false);
                    await PromoteAndDrainAsync(candidate).ConfigureAwait(false);
                    Interlocked.Increment(ref _transportRecoverySuccesses);
                    _currentRecoveryAttempt = 0;
                    PublishTransition("recovery-success");
                    return; // recovered within budget — no route fault
                }
                catch (Exception ex)
                    when (ex is not OperationCanceledException && attempt < maxAttempts && IsRetryableEstablishmentFailure(ex))
                {
                    // A retryable transport failure consumed this attempt's distinct generation + bdSeq; record
                    // its code (visible during the backoff, r2 R2.2), then back off (gate released) and retry.
                    // Reflect the honest RecoveringTransport substate during the delay window. A superseding
                    // lifecycle call during the delay throws (aborts) here; a non-retryable/last-attempt faults.
                    _lastRecoveryFailureCode = AsAdapterError(ex)?.Code;
                    SetProtocolState(SparkplugProtocolState.RecoveringTransport);
                    await BackoffWithGateReleasedAsync(TimeSpan.FromMilliseconds(delayMs), token, cancellationToken).ConfigureAwait(false);
                    delayMs = (int)Math.Min((long)delayMs * 2, maxDelayMs);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation anywhere in the recovery (CONNECT/SUBSCRIBE/NBIRTH or backoff, r2 R2.2): if a
            // lifecycle call did NOT supersede us, normalize the diagnostic substate to Suspect — the
            // previous authority (session/epoch/manifest/baseline/bdSeq) is retained and awaiting rebirth.
            if (ReferenceEquals(_activeRecoveryToken, token) && _activeSession is not null)
            {
                SetProtocolState(SparkplugProtocolState.Suspect);
            }

            throw;
        }
        catch (Exception ex) when (IsRetryableEstablishmentFailure(ex))
        {
            // A RETRYABLE transport failure reaching here means the budget was exhausted (had attempts
            // remained, the loop's inner filter would have retried it) → terminal fault (plan v3 §8 envelope).
            // A non-retryable/fatal-prep failure does not match this filter and propagates without counting.
            Interlocked.Increment(ref _transportRecoveryExhaustions);
            _lastRecoveryFailureCode = AsAdapterError(ex)?.Code; // overlaid on read; the fault below publishes state
            throw;
        }
        finally
        {
            // Clear ONLY if the token is still ours — a superseding lifecycle call may already have replaced
            // or nulled it, and we must not stomp a newer owner.
            Interlocked.CompareExchange(ref _activeRecoveryToken, null, token);
            _currentRecoveryAttempt = 0; // no episode running once we exit
            PublishTransition("recovery-ended");
        }
    }

    // The single recovery-ownership check: abort (OperationCanceledException) if disposal has won OR another
    // lifecycle call replaced/cleared this recovery's token. Called before the first await and after EVERY
    // await point so no transport / generation / durable bdSeq work proceeds once ownership is lost (r5).
    private void ValidateRecoveryOwnership(object token)
    {
        if (DisposalWon || !ReferenceEquals(_activeRecoveryToken, token))
        {
            throw new OperationCanceledException(
                "the transport recovery was superseded by disposal or another lifecycle call.");
        }
    }

    // Release the actor gate for the backoff delay, then reacquire and verify the recovery token is still
    // current. A lifecycle call (End/Stop/Dispose/cancel) that ran meanwhile invalidated it, so we abort
    // the recovery (OperationCanceledException) instead of racing a competing transition (plan v3 §4.7).
    private async Task BackoffWithGateReleasedAsync(TimeSpan delay, object token, CancellationToken cancellationToken)
    {
        _gate.Release();
        try
        {
            await _delay(delay, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false); // always reacquire to balance the gate
        }

        // Abort if disposal has won OR another lifecycle call invalidated the token, via the same ownership
        // check the loop uses everywhere — so the recovery loop honors the disposal linearization point and
        // can never enter another AttemptConnectionAsync after disposal (r3 → r5).
        ValidateRecoveryOwnership(token);
    }

    private Func<long, Task> MakeDisconnectHandler(long generation, AttemptHandoff handoff) =>
        droppedGeneration =>
        {
            if (droppedGeneration != generation)
            {
                return Task.CompletedTask; // inconsistent transport argument — defensive ignore (not a stale event)
            }

            // A genuine delayed callback from a REPLACED client (its own real generation) is stale: its handoff
            // is neither authoritative nor the in-progress attempt. Count it and ignore, never touching the
            // current authority (slice-7 review B2).
            if (IsStaleCallback(handoff))
            {
                Interlocked.Increment(ref _staleDisconnectCallbacks);
                return Task.CompletedTask;
            }

            handoff.OnDisconnect(); // atomic: invalidate a pre-promotion attempt OR mark the authority suspect
            if (handoff.IsInvalidated)
            {
                return Task.CompletedTask; // pre-promotion drop -> Begin's establishment handles it, no rebirth
            }

            if (!handoff.MarkRebirthNeeded(RebirthReason.Other))
            {
                Interlocked.Increment(ref _rebirthRequestsCoalesced); // folded into an open episode (B2)
            }

            // Queue ONE Core rebirth now if the authority is published; otherwise establishment drains it after
            // publication (so an idle drop always wakes Core).
            return DrainRebirthAsync(handoff);
        };

    private Func<long, ReadOnlyMemory<byte>, Task> MakeNodeCommandHandler(long generation, AttemptHandoff handoff) =>
        (receivedGeneration, payload) =>
        {
            if (receivedGeneration != generation)
            {
                return Task.CompletedTask; // inconsistent transport argument — defensive ignore (not a stale event)
            }

            if (IsStaleCallback(handoff))
            {
                Interlocked.Increment(ref _staleNodeCommandCallbacks); // a replaced client's delayed NCMD (B2)
                return Task.CompletedTask;
            }

            // Classify every NCMD into a redacted kind: an actionable rebirth (with/without unknown extras) is
            // actioned; every other kind is a no-op but now DISTINGUISHABLE — tallied + a sanitized diagnostic
            // code, never a raw metric name or payload byte (slice-7 review B1).
            var kind = SparkplugNodeCommand.Classify(payload);
            RecordNodeCommand(kind);
            if (!kind.IsActionableRebirth())
            {
                return Task.CompletedTask;
            }

            if (!handoff.MarkRebirthNeeded(RebirthReason.HostCommand))
            {
                Interlocked.Increment(ref _rebirthRequestsCoalesced); // folded into an open episode (B2)
            }

            return DrainRebirthAsync(handoff);
        };

    // Queue exactly one Core rebirth for the handoff's current episode, against the CURRENT authoritative
    // session/epoch. A not-yet-published or superseded authority is a no-op (the drain runs again after
    // publication). If RequestRebirthAsync fails before acceptance, the claim is released so a later
    // attempt can requeue (slice-6 review r1 B1).
    private async Task DrainRebirthAsync(AttemptHandoff handoff)
    {
        var session = _activeSession;
        if (session is null || !ReferenceEquals(session.Handoff, handoff))
        {
            return; // authority not yet published or superseded — the drain runs again after publication
        }

        if (!handoff.TryTakeForQueue())
        {
            // Could not claim the queue (already queued/committing, or nothing pending). A re-drain carries NO
            // new signal, so it is NOT coalescing — coalescing is counted at MarkRebirthNeeded, where a genuine
            // new signal folds into an open episode (slice-7 review B2).
            return;
        }

        // The reason comes from the episode (transport suspicion always reports Other and takes
        // precedence, since it forces a new-CONNECT branch); the detail is derived (diagnostic only).
        var reason = handoff.PendingReason;
        var detail = reason switch
        {
            RebirthReason.HostCommand => "Node Control/Rebirth",
            RebirthReason.SchemaChange => "a first-observed metric requires re-announcement",
            _ => "transport disconnect or uncertain send",
        };
        try
        {
            var request = RebirthRequest.Create(session.SessionId, session.Epoch, reason, detail);
            await session.Host.RequestRebirthAsync(request, CancellationToken.None).ConfigureAwait(false);
            Interlocked.Increment(ref _rebirthRequestsQueued); // exactly one Core request queued for this episode
            Interlocked.Exchange(ref _lastRebirthRequestAtTicks, _clock().UtcTicks); // overlaid live on read (B3)
        }
        catch
        {
            handoff.ReleaseQueue(); // not accepted -> allow a later requeue; never leave the episode stuck
            throw;
        }
    }

    /// <summary>
    /// Publish one phase-tagged (Replay/CatchUp/Live) DATA batch (slice 5). Gates on the active
    /// session, the context (session, epoch) invariant, and the suspect latch; detects first-observed
    /// (SchemaChange rebirth, no seq) and material mutation (fail closed); encodes NDATA with the
    /// current seq WITHOUT advancing; commits seq and marks dirty only after a successful local
    /// publish. A failed/uncertain send latches suspect, requests a rebirth, and returns
    /// zero-accepted non-success (plan v3 §4.2, §5.1, §1.3).
    /// </summary>
    /// <param name="points">The batch points (all one phase).</param>
    /// <param name="context">The replay context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The publish result.</returns>
    public async Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points, PublishContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(context);
        var started = _clock();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed(); // fail closed after disposal without faulting the terminal state (r2 R2.3)
            try
            {
                return await PublishGatedAsync(points, context, started, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // cancellation is not a fault
            }
            catch (Exception ex)
            {
                SetFaulted(AsAdapterError(ex)); // a hard fail-closed violation (no session, session/epoch mismatch, material mutation)
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PublishResult> PublishGatedAsync(
        IReadOnlyList<CanonicalDataPoint> points, PublishContext context, DateTimeOffset started, CancellationToken cancellationToken)
    {
        var session = RequireActiveSession();
        RequireContextMatches(session, context.SessionId, context.Epoch);

        // A SUSPECT transport accepts no new DATA and latches suspect (transport recovery).
        if (session.Handoff.SuspectAfterPromotion)
        {
            return await FailWithRebirthAsync(
                session, RebirthReason.Other, "the transport is suspect; awaiting rebirth.", latchSuspect: true, started, cancellationToken)
                .ConfigureAwait(false);
        }

        // A HEALTHY pending rebirth (NCMD/first-observed) blocks new DATA but must NOT be turned into a
        // transport failure (slice-6 review r2 R2.1): ensure the request is queued, accept nothing, no seq,
        // and DO NOT mark suspect — the ensuing rebirth stays a same-connection healthy re-birth.
        if (session.Handoff.RebirthPending)
        {
            await DrainRebirthAsync(session.Handoff).ConfigureAwait(false);
            return PublishResult.Failed(
                new AdapterError
                {
                    Code = SparkplugErrors.PublishRebirthRequested,
                    Category = ErrorCategory.Configuration,
                    Message = "a rebirth is pending; awaiting re-announcement.",
                    Retryable = true,
                },
                _clock() - started);
        }

        if (points.Count == 0)
        {
            return PublishResult.Successful(0, _clock() - started); // empty batch: nothing to send, no seq
        }

        // EXHAUSTIVE, side-effect-free classification (review r1 B2): inspect EVERY point before any
        // decision, so a hard material mutation is never hidden behind a first-observed metric's
        // position. Precedence: material mutation (fail closed) > first-observed (SchemaChange rebirth)
        // > publish. No rebirth request or publish escapes before the whole batch is validated.
        var anyFirstObserved = false;
        string? materialMutationName = null;
        foreach (var point in points)
        {
            var classification = SparkplugMaterialSchemaClassifier.Classify(session.Manifest.Schema, point);
            if (classification == SparkplugMetricClassification.MaterialMutation)
            {
                materialMutationName ??= AliasKeyOf(point).MetricName;
            }
            else if (classification == SparkplugMetricClassification.FirstObserved)
            {
                anyFirstObserved = true;
            }
        }

        if (materialMutationName is not null)
        {
            SparkplugMaterialSchemaClassifier.ThrowIfMaterialMutation(
                SparkplugMetricClassification.MaterialMutation, materialMutationName); // fail closed — wins over first-observed
        }

        // Validate the phase enum before any side effect (fail closed on an undefined phase).
        var phaseState = PhaseToProtocolState(context.Phase);

        // FULL fallible wire preflight for EVERY point (review r1 B2 → r2): UTC enforcement + value
        // mapping + sample/state construction happen BEFORE the first-observed decision, so a
        // malformed DATA point (non-UTC timestamp, CLR mismatch, pre-epoch, unmappable) fails closed
        // and is never concealed by a first-observed metric's presence. Includes the first-observed
        // point itself. Nothing fallible then runs after a successful MQTT publish.
        var samples = new List<SparkplugMetricSample>(points.Count);
        var observed = new List<(SparkplugAliasKey Key, SparkplugMetricState State)>(points.Count);
        foreach (var point in points)
        {
            samples.Add(ToSample(point));
            observed.Add((AliasKeyOf(point), SparkplugMetricState.FromDataPoint(point)));
        }

        // Only after the WHOLE batch is validated: a first-observed metric requests a SchemaChange rebirth.
        if (anyFirstObserved)
        {
            return await FailWithRebirthAsync(
                session, RebirthReason.SchemaChange, "a first-observed metric requires re-announcement.",
                latchSuspect: false, started, cancellationToken).ConfigureAwait(false);
        }

        var isHistorical = context.Phase is ReplayPhase.Replay or ReplayPhase.CatchUp;
        SetProtocolState(phaseState);

        // Encode with the CURRENT seq without advancing it; a pre-send throw consumes no seq.
        var payload = SparkplugPayloadEncoder.EncodeNData(
            SparkplugSequenceNumber.Create(_nextSeq), _clock(), samples, session.Manifest.AliasMap, isHistorical);
        var published = await SendAsync(
            session, SparkplugTopicFactory.NData(NodeIdentity()), payload,
            () => Interlocked.Increment(ref _publishFailures), cancellationToken).ConfigureAwait(false);
        if (!published)
        {
            Interlocked.Increment(ref _publishFailures); // observable/uncertain DATA send failure (plan v3 §8)
            return await FailWithRebirthAsync(
                session, RebirthReason.Other, "the DATA batch did not complete at the local transport boundary.",
                latchSuspect: true, started, cancellationToken).ConfigureAwait(false);
        }

        _nextSeq = (_nextSeq + 1) & 0xFF; // advance ONLY after local success
        // Hot path: update the liveness timestamp cheaply (Interlocked only) — the full coherent record is
        // rebuilt lazily when diagnostics/health is actually READ (infrequent), not on every DATA batch.
        Interlocked.Exchange(ref _lastDataPublishAtTicks, _clock().UtcTicks); // liveness (plan v3 §8)
        foreach (var (key, state) in observed)
        {
            session.Baseline.Observe(key, state); // dirtySinceBirth — reuses the pre-built states (no fallible work post-send)
        }

        return PublishResult.Successful(points.Count, _clock() - started);
    }

    /// <summary>
    /// Complete the catch-up cutover: emit ONE non-historical final update for the metrics that changed
    /// since birth (dirty ∪ changed-at-cutover, wire-normalized) and enter Live (slice 5). Fails closed
    /// on a missing announced metric; requests a SchemaChange rebirth (no Live) on a first-observed
    /// metric; and, on a suspect transport or a failed final-update send, latches suspect, awaits a
    /// current-session rebirth request, and returns WITHOUT entering Live (plan v3 §1.5, §4.4).
    /// </summary>
    /// <param name="cutover">The cutover inputs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when Live has been entered (or a rebirth was requested).</returns>
    public async Task CompleteCatchUpAsync(ReplaySessionCutover cutover, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cutover);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed(); // fail closed after disposal without faulting the terminal state (r2 R2.3)
            try
            {
                await CompleteCatchUpGatedAsync(cutover, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetFaulted(AsAdapterError(ex));
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CompleteCatchUpGatedAsync(ReplaySessionCutover cutover, CancellationToken cancellationToken)
    {
        var session = RequireActiveSession();
        RequireContextMatches(session, cutover.SessionId, cutover.Epoch);

        // §4.4: a suspect transport defers to Core (latch suspect); a healthy pending rebirth defers too but
        // stays healthy (r2 R2.1). Either way: no final update, no Live.
        if (session.Handoff.SuspectAfterPromotion)
        {
            await RequestRebirthAsync(
                session, RebirthReason.Other, "the transport is suspect at cutover; awaiting rebirth.",
                latchSuspect: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (session.Handoff.RebirthPending)
        {
            await DrainRebirthAsync(session.Handoff).ConfigureAwait(false); // ensure queued; no suspect
            return;
        }

        // Map the cutover snapshot to its source latest-values (keyed by alias).
        var snapshot = cutover.State.Snapshot;
        var latestByKey = new Dictionary<SparkplugAliasKey, LatestMetricValue>();
        foreach (var canonicalKey in snapshot.Metrics)
        {
            if (snapshot.TryGet(canonicalKey) is { } latest)
            {
                latestByKey[SparkplugAliasKey.FromCanonical(canonicalKey)] = latest;
            }
        }

        // STATIC-SCHEMA PREFLIGHT (review r1 B1): a static schema difference is the classifier's job,
        // NOT a dynamic final update. Classify every cutover metric first; a material mutation fails
        // closed and WINS over first-observed — no rebirth, publish, seq, or Live may occur. Exhaustive
        // + side-effect-free, mirroring the DATA path.
        string? materialMutationName = null;
        foreach (var (key, latest) in latestByKey)
        {
            var classification = SparkplugMaterialSchemaClassifier.Classify(
                session.Manifest.Schema, key, SparkplugMetricSchema.From(latest.ValueType));
            if (classification == SparkplugMetricClassification.MaterialMutation)
            {
                materialMutationName ??= key.MetricName;
            }
        }

        if (materialMutationName is not null)
        {
            SparkplugMaterialSchemaClassifier.ThrowIfMaterialMutation(
                SparkplugMetricClassification.MaterialMutation, materialMutationName); // fail closed
        }

        // Only after there is no material mutation: dynamic comparison for the final update + manifest deltas.
        var cutoverStates = latestByKey.ToDictionary(kv => kv.Key, kv => SparkplugMetricState.FromLatestValue(kv.Value));
        var comparison = session.Baseline.Compare(cutoverStates);

        if (!comparison.MissingAnnouncedKeys.IsEmpty)
        {
            throw Typed(SparkplugErrors.ManifestInvariantViolation,
                $"announced metric(s) [{string.Join(", ", comparison.MissingAnnouncedKeys.Select(k => k.MetricName))}] " +
                "are absent from the cutover snapshot.");
        }

        if (!comparison.FirstObservedKeys.IsEmpty)
        {
            // Same-generation growth surfaced at cutover — rebirth, do NOT enter Live.
            await RequestRebirthAsync(
                session, RebirthReason.SchemaChange, "first-observed metric(s) at cutover require re-announcement.",
                latchSuspect: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (comparison.FinalUpdates.Count > 0)
        {
            var samples = comparison.FinalUpdates.Keys
                .Select(key => ToSample(key, latestByKey[key]))
                .ToList();
            var payload = SparkplugPayloadEncoder.EncodeNData(
                SparkplugSequenceNumber.Create(_nextSeq), _clock(), samples, session.Manifest.AliasMap, isHistorical: false);
            var published = await SendAsync(
                session, SparkplugTopicFactory.NData(NodeIdentity()), payload,
                () => Interlocked.Increment(ref _publishFailures), cancellationToken).ConfigureAwait(false);
            if (!published)
            {
                Interlocked.Increment(ref _publishFailures); // final-update DATA send failure (plan v3 §8)
                await RequestRebirthAsync(
                    session, RebirthReason.Other, "the final update did not complete at the local transport boundary.",
                    latchSuspect: true, cancellationToken).ConfigureAwait(false);
                return; // §4.4: do not enter Live
            }

            _nextSeq = (_nextSeq + 1) & 0xFF;
            Interlocked.Exchange(ref _lastDataPublishAtTicks, _clock().UtcTicks);
        }

        // Deterministic race barrier immediately before the atomic Live commit (review r1 B4).
        if (PreLiveCommitBarrier is { } barrier)
        {
            await barrier().ConfigureAwait(false);
        }

        // Atomic cutover→Live: TryCommitLive requires BOTH an Active transport AND an idle episode, so a
        // disconnect/send failure (suspect) OR a healthy pending rebirth (NCMD/first-observed) that raced
        // the commit prevents Live (slice-6 review r3 R3.2). A suspect race latches suspect; a healthy
        // pending race stays healthy — either way we defer to the pending rebirth instead of Live.
        if (!session.Handoff.TryCommitLive())
        {
            if (session.Handoff.SuspectAfterPromotion)
            {
                SetProtocolState(SparkplugProtocolState.Suspect);
            }

            await DrainRebirthAsync(session.Handoff).ConfigureAwait(false); // ensure the pending request is queued
            return;
        }

        SetProtocolState(SparkplugProtocolState.Live);
    }

    /// <summary>
    /// End the session gracefully (slice 6 pass 2): invalidate any in-flight transport recovery, publish
    /// ONE explicit NDEATH for the born session, then a clean MQTT DISCONNECT (so the broker discards the
    /// Will — no second death), and retire the transport. Idempotent: with no active session it is a no-op.
    /// Death/disconnect failure is best-effort/diagnostic; there is no rebirth during shutdown (§4.5).
    /// </summary>
    /// <param name="sessionEnd">The session-end inputs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the session has ended.</returns>
    public async Task EndSessionAsync(ReplaySessionEnd sessionEnd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionEnd);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // An End that LOSES to disposal is a no-op — it must NOT publish an NDEATH or clean DISCONNECT;
            // disposal owns the abort-retirement (r2 R3).
            if (DisposalWon)
            {
                return;
            }

            var session = _activeSession;

            // Gate on the authoritative identity (slice-6 review r1 B4): a delayed End from a superseded
            // session/route must NOT tear down the current authority. No active session → idempotent no-op.
            if (session is null
                || sessionEnd.SessionId.Value != session.SessionId.Value
                || !string.Equals(sessionEnd.RouteId, session.RouteId, StringComparison.Ordinal))
            {
                return;
            }

            _activeRecoveryToken = null; // supersede any in-flight recovery loop
            _activeSession = null;       // the session is ending; block any further DATA/rebirth against it
            SetProtocolState(SparkplugProtocolState.Stopping);
            var node = NodeIdentity();

            // ONE explicit NDEATH. Only on a CONFIRMED local publish do we issue the clean DISCONNECT that
            // tells the broker to DISCARD the Will; if the NDEATH is unconfirmed/uncertain, we ABORT instead
            // so the broker publishes the Will — never "no death at all" (slice-6 review r1 B3).
            var deathConfirmed = false;
            try
            {
                deathConfirmed = await session.Transport
                    .PublishAsync(SparkplugTopicFactory.NDeath(node), SparkplugPayloadEncoder.EncodeNDeath(session.BdSeq), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Uncertain NDEATH (false/exception/in-transport cancellation) → do NOT clean-disconnect.
            }

            if (!deathConfirmed)
            {
                Interlocked.Increment(ref _deathPublishFailures); // NDEATH unconfirmed → Will-only end (plan v3 §8)
            }

            if (deathConfirmed)
            {
                try { await session.Transport.DisconnectAsync(cancellationToken).ConfigureAwait(false); }
                catch { /* best-effort clean DISCONNECT */ }
            }

            // ABORT-dispose: if the NDEATH was confirmed we already cleanly disconnected (Will discarded);
            // if not, disposing without a clean DISCONNECT lets the broker publish the Will.
            try { await session.Transport.DisposeAsync().ConfigureAwait(false); }
            catch { /* retiring the ended client */ }

            // Ready-no-session: coarse stays Running, protocol Stopped, so health reports Healthy and a
            // future Begin is possible (slice-6 review r1 B4).
            SetProtocolState(SparkplugProtocolState.Stopped);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Dispose the actor's resources — TERMINAL: invalidate any in-flight recovery, retire the active
    /// transport (ABORT, so the broker publishes the Will), and publish a coherent terminal Stopped/Stopped
    /// state. After disposal no Begin/Rebirth/DATA/cutover proceeds (they fail closed); Stop/End are no-ops.
    /// Concurrent callers all await the SAME retirement via a shared completion task, so caller B never
    /// completes before caller A's retirement finishes (slice-6 review r1 B2 → r2 R2.3). The gate itself is
    /// NOT disposed, so a recovery parked in its backoff can safely reacquire it and observe the nulled token.
    /// </summary>
    /// <returns>A task that completes when the retirement has finished.</returns>
    public ValueTask DisposeAsync()
    {
        // Invalidate any in-flight recovery BEFORE the ownership CAS (review r3 re-review): a recovery parked
        // in gate-released backoff must never observe a still-current token after disposal has won. Nulling
        // the token and installing the marker is now ONE ordering decision, so the marker-vs-token interval is
        // closed — no new transport/generation/bdSeq can begin once disposal wins. Every concurrent Dispose
        // caller may null the token (idempotent); any Dispose supersedes recovery.
        _activeRecoveryToken = null;

        // The FIRST caller installs the shared disposal task and runs the retirement; others await it.
        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = Interlocked.CompareExchange(ref _disposeTask, mine.Task, null);
        return new ValueTask(existing ?? DisposeCoreAsync(mine));
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        // (_activeRecoveryToken was already nulled in DisposeAsync, before the ownership CAS.)
        try
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await RetireActiveSessionAsync().ConfigureAwait(false);
                _snapshot = new ActorSnapshot(AdapterState.Stopped, SparkplugProtocolState.Stopped); // terminal
                PublishTransition("disposed"); // reflect the terminal Stopped/Stopped + TerminalDisposed
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            completion.SetResult(); // concurrent callers await THIS completed retirement
        }

        // Intentionally NOT _gate.Dispose(): a recovery parked in BackoffWithGateReleasedAsync may still
        // reacquire the gate to observe the nulled token and abort. SemaphoreSlim without an allocated
        // WaitHandle needs no explicit disposal.
    }

    private bool DisposalWon => Volatile.Read(ref _disposeTask) is not null;

    private void ThrowIfDisposed()
    {
        if (DisposalWon)
        {
            throw new ObjectDisposedException(nameof(SparkplugSessionActor), "the Sparkplug session actor has been disposed.");
        }
    }

    private async Task RetireActiveSessionAsync()
    {
        var active = _activeSession;
        _activeSession = null;
        if (active is not null)
        {
            try { await active.Transport.DisposeAsync().ConfigureAwait(false); }
            catch { /* best-effort retirement */ }
        }
    }

    private string ResolveClientId(SparkplugSinkConfiguration config) =>
        string.IsNullOrWhiteSpace(config.ClientId) ? $"edgeconnect-sparkplug-{_instanceId}" : config.ClientId;

    // ----- Slice-5 replay/DATA helpers (all run under the gate) -----

    private ActiveSession RequireActiveSession() =>
        _activeSession ?? throw Typed(SparkplugErrors.PublishNoSession,
            "a context publish/cutover arrived with no active session (Core called the replay path before a successful Begin).");

    private static void RequireContextMatches(ActiveSession session, ReplaySessionId sessionId, ReplayEpochId epoch)
    {
        if (sessionId.Value != session.SessionId.Value)
        {
            throw Typed(SparkplugErrors.PublishSessionMismatch,
                $"the context session ({sessionId.Value}) is not the actor's authoritative session ({session.SessionId.Value}).");
        }

        if (epoch.Value != session.Epoch.Value)
        {
            throw Typed(SparkplugErrors.PublishEpochMismatch,
                $"the context epoch ({epoch.Value}) is not the actor's current birth epoch ({session.Epoch.Value}).");
        }
    }

    // Latch suspect (transport reasons only), then await ACCEPTANCE of a current-session/current-epoch
    // rebirth request before returning — the reverse handshake that lets Core rebirth before retrying.
    private async Task RequestRebirthAsync(
        ActiveSession session, RebirthReason reason, string detail, bool latchSuspect, CancellationToken cancellationToken)
    {
        _ = (cancellationToken, detail); // the reverse handshake queues against Core with its own (None) token; the drain derives the detail
        if (latchSuspect)
        {
            session.Handoff.MarkSuspect();
            SetProtocolState(SparkplugProtocolState.Suspect);
        }

        // Mark the control episode (with its cause) and drain it: coalesces with any async disconnect/NCMD
        // so only the first caller emits the Core request for this episode (slice-6 review r1 B1 → r2 R2.3).
        if (!session.Handoff.MarkRebirthNeeded(reason))
        {
            Interlocked.Increment(ref _rebirthRequestsCoalesced); // folded into an open episode (B2)
        }

        await DrainRebirthAsync(session.Handoff).ConfigureAwait(false);
    }

    private async Task<PublishResult> FailWithRebirthAsync(
        ActiveSession session, RebirthReason reason, string detail, bool latchSuspect, DateTimeOffset started, CancellationToken cancellationToken)
    {
        await RequestRebirthAsync(session, reason, detail, latchSuspect, cancellationToken).ConfigureAwait(false);
        return PublishResult.Failed(
            new AdapterError
            {
                Code = SparkplugErrors.PublishRebirthRequested,
                // A first-observed (SchemaChange) rebirth is a healthy-transport schema-growth event, not a
                // network failure; a transport-suspect (Other) rebirth is a network condition.
                Category = reason == RebirthReason.SchemaChange ? ErrorCategory.Configuration : ErrorCategory.Network,
                Message = detail,
                Retryable = true, // Core rebirths, then retries the same unacknowledged subrange under the newer epoch
            },
            _clock() - started);
    }

    // The transport-boundary send with the frozen suspect semantics (review r1 B3). Once the transport
    // call is entered the actor can no longer prove no bytes were queued, so an observable local failure
    // (false) OR any exception makes the authority suspect. A non-cancellation exception is normalized to
    // a local failure (false → the caller requests a rebirth, NOT a terminal fault); cancellation is
    // rethrown (still suspect) so it is never mistaken for cancellation BEFORE the send.
    private async Task<bool> SendAsync(
        ActiveSession session, string topic, ReadOnlyMemory<byte> payload, Action onSuspectSendFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            return await session.Transport.PublishAsync(topic, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // IN-TRANSPORT cancellation: the transport call was entered, so we cannot prove no bytes were
            // queued — mark suspect AND count the observable/uncertain send failure (a PRE-send cancellation
            // aborts at the gate and never reaches here, so it counts nothing, slice-7 review B4).
            session.Handoff.MarkSuspect();
            SetProtocolState(SparkplugProtocolState.Suspect);
            onSuspectSendFailure();
            throw;
        }
        catch
        {
            session.Handoff.MarkSuspect();
            SetProtocolState(SparkplugProtocolState.Suspect);
            return false; // normalized to a local failure; the caller counts it on the !published path
        }
    }

    private static SparkplugProtocolState PhaseToProtocolState(ReplayPhase phase) => phase switch
    {
        ReplayPhase.Replay => SparkplugProtocolState.Replaying,
        ReplayPhase.CatchUp => SparkplugProtocolState.CatchingUp,
        ReplayPhase.Live => SparkplugProtocolState.Live,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Undefined replay phase; failing closed."),
    };

    private SparkplugEdgeNodeIdentity NodeIdentity() =>
        SparkplugEdgeNodeIdentity.Create(_config!.GroupId, _config.EdgeNodeId);

    private static SparkplugAliasKey AliasKeyOf(CanonicalDataPoint point) =>
        SparkplugAliasKey.FromCanonical(CanonicalMetricKey.Create(point.SourceInstanceId, point.DeviceId, point.TagPath));

    private static SparkplugMetricSample ToSample(CanonicalDataPoint point) => new()
    {
        Key = AliasKeyOf(point),
        ValueType = point.ValueType,
        Value = point.Value,
        IsNull = point.Value is null,
        AcquisitionTimestamp = SparkplugAcquisitionTimestamp.RequireUtc(point.DeviceTimestamp),
        Quality = point.Quality,
    };

    private static SparkplugMetricSample ToSample(SparkplugAliasKey key, LatestMetricValue value) => new()
    {
        Key = key,
        ValueType = value.ValueType,
        Value = value.Value,
        IsNull = value.IsNull,
        AcquisitionTimestamp = value.TimestampUtc,
        Quality = value.Quality,
    };

    private static AdapterException Typed(string code, string message) =>
        new(new AdapterError { Code = code, Category = ErrorCategory.Internal, Message = message, Retryable = false });

    private void RequireReadyForSession()
    {
        if (_snapshot.State != AdapterState.Running)
        {
            throw new InvalidOperationException(
                $"SparkplugSessionActor '{_instanceId}' must be Running to begin a session (was {_snapshot.State}).");
        }

        if (_activeSession is not null)
        {
            throw new AdapterException(new AdapterError
            {
                Code = SparkplugErrors.SessionAlreadyActive,
                Category = ErrorCategory.Internal,
                Message = "a Sparkplug session is already active (single-session actor).",
                Retryable = false,
            });
        }

        if (_store is null || _transportFactory is null || _config is null)
        {
            throw new AdapterException(new AdapterError
            {
                Code = SparkplugErrors.SessionNotReady,
                Category = ErrorCategory.Internal,
                Message = "the Sparkplug actor was not wired with an identity store/transport (K4 composition).",
                Retryable = false,
            });
        }
    }

    private static void RequireNotInvalidated(AttemptHandoff handoff)
    {
        if (handoff.IsInvalidated)
        {
            throw SessionSuspectDuringBegin();
        }
    }

    private static AdapterException SessionSuspectDuringBegin() =>
        new(new AdapterError
        {
            Code = SparkplugErrors.SessionSuspectDuringBegin,
            Category = ErrorCategory.Network,
            Message = "the transport dropped during initial Begin before an authoritative birth.",
            Retryable = false,
        });

    private static AdapterException BirthPublishFailed() =>
        new(new AdapterError
        {
            Code = SparkplugErrors.BirthPublishFailed,
            Category = ErrorCategory.Network,
            Message = "NBIRTH did not complete at the local transport boundary.",
            Retryable = false,
        });

    private void SetProtocolState(SparkplugProtocolState protocol)
    {
        _snapshot = _snapshot with { ProtocolState = protocol };
        PublishTransition($"protocol:{protocol}");
    }

    private void SetAdapterState(AdapterState target)
    {
        var current = _snapshot.State;
        if (!AdapterStateTransitions.IsAllowed(current, target))
        {
            throw new InvalidOperationException(
                $"SparkplugSessionActor '{_instanceId}' cannot transition from {current} to {target}.");
        }

        _snapshot = _snapshot with { State = target };
        PublishTransition($"state:{target}");
    }

    private void SetState(AdapterState target, SparkplugProtocolState protocolState)
    {
        var current = _snapshot.State;
        if (!AdapterStateTransitions.IsAllowed(current, target))
        {
            throw new InvalidOperationException(
                $"SparkplugSessionActor '{_instanceId}' cannot transition from {current} to {target}.");
        }

        _snapshot = new ActorSnapshot(target, protocolState);
        PublishTransition($"state:{target}/{protocolState}");
    }

    // Extract a sanitized AdapterError from a caught exception for diagnostics (last-error code/category).
    // Only the structured error carries safe fields; a raw exception's Message is NOT recorded (it could
    // echo an endpoint/credential), so a non-AdapterException yields null and SetFaulted records the fallback.
    private static AdapterError? AsAdapterError(Exception ex) => (ex as AdapterException)?.Error;

    private void SetFaulted(AdapterError? error = null)
    {
        // Every fault records a last error (with a sanitized FALLBACK code for an untyped failure — an illegal
        // transition or actor-loop exception carries no AdapterException), so a Faulted actor always exposes a
        // last-error code + time per §8 (slice-7 review B4).
        RecordError(error);
        if (AdapterStateTransitions.IsAllowed(_snapshot.State, AdapterState.Failed))
        {
            _snapshot = new ActorSnapshot(AdapterState.Failed, SparkplugProtocolState.Faulted);
        }

        PublishTransition("faulted");
    }

    // Record a SANITIZED last error (code/category/retryable only — Message cleared so no endpoint/credential/
    // payload can leak) and its time. A null error (untyped failure) records the stable fallback code (B4).
    private void RecordError(AdapterError? error)
    {
        var source = error ?? new AdapterError
        {
            Code = SparkplugErrors.ActorFailure,
            Category = ErrorCategory.Internal,
            Message = string.Empty,
            Retryable = false,
        };
        _lastError = new AdapterError
        {
            Code = source.Code,
            Category = source.Category,
            Message = string.Empty,
            Retryable = source.Retryable,
        };
        Interlocked.Exchange(ref _lastErrorAtTicks, _clock().UtcTicks);
    }

    // A transport callback is STALE unless its handoff is the authoritative session's handoff OR the current
    // in-progress establishment handoff. The concrete transport echoes its own captured generation, so a
    // retired client's delayed callback matches its own handler by generation — identity is the authoritative
    // staleness test, not the generation argument alone (slice-7 review B2).
    private bool IsStaleCallback(AttemptHandoff handoff)
    {
        var active = _activeSession;
        if (active is not null && ReferenceEquals(active.Handoff, handoff)) { return false; }
        return !ReferenceEquals(_establishingHandoff, handoff);
    }

    // Record an inbound NCMD classification: tally the ignored kinds and surface a sanitized diagnostic code
    // (never a raw metric name or payload byte). Overlaid on read — no semantic republish from the callback.
    private void RecordNodeCommand(SparkplugNodeCommandKind kind)
    {
        _lastNodeCommandDiagnosticCode = kind.DiagnosticCode();
        if (!kind.IsActionableRebirth())
        {
            Interlocked.Increment(ref _nodeCommandsIgnored);
        }
    }

    private sealed record ActorSnapshot(AdapterState State, SparkplugProtocolState ProtocolState);

    /// <summary>The atomic operational-control triple read from a handoff under one lock (r3 R3.1).</summary>
    private readonly record struct HandoffDiagnostics(bool Suspect, bool Pending, RebirthReason? Reason);

    private static DateTimeOffset? TicksToOffset(long ticks) =>
        ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);

    /// <summary>
    /// The current coherent diagnostic snapshot (health/test accessor). Returns the last SEMANTIC record
    /// (lifecycle/protocol/session) published at a completed gated transition, with two overlays applied live:
    /// the independent monotonic counters/timestamps/last-event codes, AND the operational-control state
    /// (suspect / pending-rebirth / reason / terminal-disposed). The control overlay is read from the
    /// authoritative handoff — bound by connection generation so it is only applied when the handoff still
    /// matches the semantic authority — so an ASYNCHRONOUS disconnect/NCMD that latches suspect or pending is
    /// visible immediately, without reconstructing lifecycle/session from a torn read (slice-7 review r2 R2.1).
    /// A read is NOT a transition: <c>Version</c> does not advance.
    /// </summary>
    internal SparkplugActorDiagnostics DiagnosticsSnapshot
    {
        get
        {
            var semantic = _semantic ?? BuildInitialSemantic();
            var lastError = _lastError;

            // Live control overlay bound to the FULL semantic authority (slice-7 review r3 R3.1): session +
            // epoch + connection generation must ALL match, so a healthy same-generation epoch promotion (which
            // retains the generation but advances the epoch) can never attach new-epoch control state to the old
            // semantic root. When bound, the control triple is captured in ONE atomic handoff read (never torn);
            // otherwise the root's coherent baked values stand.
            var active = _activeSession;
            var bound = active is not null && semantic.HasSession
                && active.SessionId.Value == semantic.SessionId
                && active.Epoch.Value == semantic.Epoch
                && active.TransportGeneration == semantic.ConnectionGeneration;
            var control = bound ? active!.Handoff.ReadDiagnostics() : (HandoffDiagnostics?)null;
            var suspect = control?.Suspect ?? semantic.SuspectTransport;
            var pending = control?.Pending ?? semantic.PendingRebirth;
            var pendingReason = pending
                ? (control is { } c ? c.Reason?.ToString() : semantic.PendingRebirthReason)
                : null;

            return semantic with
            {
                TerminalDisposed = DisposalWon,           // live: true the instant disposal wins (before retirement)
                SuspectTransport = suspect,
                PendingRebirth = pending,
                PendingRebirthReason = pendingReason,
                LastError = lastError,
                LastErrorCode = lastError?.Code,
                LastErrorCategory = lastError?.Category.ToString(),
                LastRecoveryFailureCode = _lastRecoveryFailureCode,
                LastNodeCommandDiagnosticCode = _lastNodeCommandDiagnosticCode,
                LastSuccessfulBirthAt = TicksToOffset(Interlocked.Read(ref _lastSuccessfulBirthAtTicks)),
                LastDataPublishAt = TicksToOffset(Interlocked.Read(ref _lastDataPublishAtTicks)),
                LastRebirthRequestAt = TicksToOffset(Interlocked.Read(ref _lastRebirthRequestAtTicks)),
                LastErrorAt = TicksToOffset(Interlocked.Read(ref _lastErrorAtTicks)),
                StaleDisconnectCallbacks = Interlocked.Read(ref _staleDisconnectCallbacks),
                StaleNodeCommandCallbacks = Interlocked.Read(ref _staleNodeCommandCallbacks),
                NodeCommandsIgnored = Interlocked.Read(ref _nodeCommandsIgnored),
                RebirthRequestsQueued = Interlocked.Read(ref _rebirthRequestsQueued),
                RebirthRequestsCoalesced = Interlocked.Read(ref _rebirthRequestsCoalesced),
                HealthyRebirths = Interlocked.Read(ref _healthyRebirths),
                TransportRecoveryStarts = Interlocked.Read(ref _transportRecoveryStarts),
                TransportRecoveryAttempts = Interlocked.Read(ref _transportRecoveryAttempts),
                TransportRecoverySuccesses = Interlocked.Read(ref _transportRecoverySuccesses),
                TransportRecoveryExhaustions = Interlocked.Read(ref _transportRecoveryExhaustions),
                PublishFailures = Interlocked.Read(ref _publishFailures),
                BirthFailures = Interlocked.Read(ref _birthFailures),
                DeathPublishFailures = Interlocked.Read(ref _deathPublishFailures),
            };
        }
    }

    private SparkplugActorDiagnostics BuildInitialSemantic()
    {
        PublishTransition("observed");
        return _semantic!;
    }

    // Publish the coherent SEMANTIC snapshot at a completed gated transition, under _diagLock. Captures the
    // lifecycle/protocol/session fields as ONE mutually-consistent set (from the gated, atomic _snapshot and
    // the immutable _activeSession authority) and advances the Version change-token. Counters/timestamps/
    // last-events are not the coherence concern — they are overlaid live on read (slice-7 review B3). Called
    // only from GATED transitions, never from an un-gated callback (so it never reads a mid-write state).
    private void PublishTransition(string reasonCode)
    {
        lock (_diagLock)
        {
            var snapshot = _snapshot;      // volatile read: coarse+fine are atomic together
            var active = _activeSession;   // volatile read: the immutable authority
            var now = _clock();

            // Update the "immediately preceding transition" fields only on an ACTUAL state change.
            if (snapshot.State != _lastObservedState || snapshot.ProtocolState != _lastObservedProtocol)
            {
                _priorState = _lastObservedState;
                _priorProtocol = _lastObservedProtocol;
                _lastObservedState = snapshot.State;
                _lastObservedProtocol = snapshot.ProtocolState;
                _lastStateChangeAt = now;
                _lastTransitionReasonCode = reasonCode;
            }

            // ONE atomic handoff read (slice-7 review r3 R3.1): suspect/pending/reason captured under a single
            // handoff-lock acquisition, so the baked control triple is never internally torn.
            var control = active?.Handoff.ReadDiagnostics();

            _semantic = new SparkplugActorDiagnostics
            {
                Version = Interlocked.Increment(ref _diagVersion),
                State = snapshot.State,
                ProtocolState = snapshot.ProtocolState,
                PreviousState = _priorState,
                PreviousProtocolState = _priorProtocol,
                LastStateChangeAt = _lastStateChangeAt,
                LastTransitionReasonCode = _lastTransitionReasonCode,
                TerminalDisposed = DisposalWon,
                HasSession = active is not null,
                SessionId = active?.SessionId.Value,
                Epoch = active?.Epoch.Value,
                RouteId = active?.RouteId,
                ConnectionGeneration = active?.TransportGeneration,
                LastIssuedGeneration = Interlocked.Read(ref _lastIssuedConnectionGeneration),
                BdSeq = active is null ? null : active.BdSeq.Value,
                NextSeq = active is null ? null : Volatile.Read(ref _nextSeq),
                SuspectTransport = control?.Suspect ?? false,
                PendingRebirth = control?.Pending ?? false,
                PendingRebirthReason = (control?.Pending ?? false) ? control!.Value.Reason?.ToString() : null,
                CurrentRecoveryAttempt = _currentRecoveryAttempt,
                RecoveryAttemptBudget = _config is null ? 0 : Math.Max(1, _config.TransportRecoveryMaxAttempts),
                // A stable baseline for the last-event/counter fields — overlaid live on read.
                LastRecoveryFailureCode = _lastRecoveryFailureCode,
                LastNodeCommandDiagnosticCode = _lastNodeCommandDiagnosticCode,
                LastSuccessfulBirthAt = TicksToOffset(Interlocked.Read(ref _lastSuccessfulBirthAtTicks)),
                LastDataPublishAt = TicksToOffset(Interlocked.Read(ref _lastDataPublishAtTicks)),
                LastRebirthRequestAt = TicksToOffset(Interlocked.Read(ref _lastRebirthRequestAtTicks)),
                LastErrorAt = TicksToOffset(Interlocked.Read(ref _lastErrorAtTicks)),
                LastErrorCode = _lastError?.Code,
                LastErrorCategory = _lastError?.Category.ToString(),
                LastError = _lastError,
                StaleDisconnectCallbacks = Interlocked.Read(ref _staleDisconnectCallbacks),
                StaleNodeCommandCallbacks = Interlocked.Read(ref _staleNodeCommandCallbacks),
                NodeCommandsIgnored = Interlocked.Read(ref _nodeCommandsIgnored),
                RebirthRequestsQueued = Interlocked.Read(ref _rebirthRequestsQueued),
                RebirthRequestsCoalesced = Interlocked.Read(ref _rebirthRequestsCoalesced),
                HealthyRebirths = Interlocked.Read(ref _healthyRebirths),
                TransportRecoveryStarts = Interlocked.Read(ref _transportRecoveryStarts),
                TransportRecoveryAttempts = Interlocked.Read(ref _transportRecoveryAttempts),
                TransportRecoverySuccesses = Interlocked.Read(ref _transportRecoverySuccesses),
                TransportRecoveryExhaustions = Interlocked.Read(ref _transportRecoveryExhaustions),
                PublishFailures = Interlocked.Read(ref _publishFailures),
                BirthFailures = Interlocked.Read(ref _birthFailures),
                DeathPublishFailures = Interlocked.Read(ref _deathPublishFailures),
            };
        }
    }

    /// <summary>The single immutable session authority, promoted atomically on NBIRTH success.</summary>
    private sealed record ActiveSession(
        ISparkplugMqttTransport Transport,
        long TransportGeneration,
        ReplaySessionId SessionId,
        ReplayEpochId Epoch,
        string RouteId,
        IReplaySessionHost Host,
        SparkplugBirthDeathSequence BdSeq,
        ResolvedSparkplugBirthPlan Manifest,
        SparkplugBirthBaseline Baseline,
        AttemptHandoff Handoff);

    /// <summary>
    /// The atomic authority lifecycle + control-request episode for one CONNECT attempt. A SINGLE short
    /// internal lock guards the compound (transport-state, episode, reason) so every cross-word transition
    /// is atomic against the ASYNCHRONOUS (un-gated) disconnect/NCMD callbacks (slice-6 review r2 → r3):
    /// establishment vs. a pre-promotion drop; the promotion CAS; the cutover-to-Live commit (which
    /// requires BOTH Active transport AND an idle episode); and the healthy rebirth commit
    /// (Rebirthing → RebirthCommitting → Active) with race-safe episode turnover and a cause bound to the
    /// episode. The lock is only ever held for a few field assignments (never across an await), so there is
    /// no contention or deadlock risk.
    /// </summary>
    private sealed class AttemptHandoff
    {
        private const int Establishing = 0;       // initial CONNECT establishment in flight
        private const int Invalidated = 1;        // a drop won before the first promotion
        private const int Active = 2;             // promoted; replaying/catching-up
        private const int Live = 3;               // cutover committed
        private const int Rebirthing = 4;         // a HEALTHY in-place rebirth is in flight
        private const int RebirthCommitting = 5;  // the rebirth won; the new authority is being published
        private const int Suspect = 6;            // a drop / observable-or-uncertain send failure lost the transport

        private readonly object _sync = new();
        private int _state = Establishing;
        private bool _pending;                              // a rebirth is required (control episode open)
        private bool _queued;                               // the Core request for the current episode was queued
        private bool _committing;                           // a healthy rebirth is publishing the new authority
        private RebirthReason _reason = RebirthReason.Other; // the cause of the CURRENT episode (bound on open)

        public AttemptHandoff(long generation) => Generation = generation;

        /// <summary>This attempt's connection generation.</summary>
        public long Generation { get; }

        /// <summary>True once a disconnect invalidated an in-progress (pre-promotion) establishment.</summary>
        public bool IsInvalidated { get { lock (_sync) { return _state == Invalidated; } } }

        /// <summary>True once the promoted authority's transport became suspect (a drop or an uncertain send).</summary>
        public bool SuspectAfterPromotion { get { lock (_sync) { return _state == Suspect; } } }

        /// <summary>A rebirth episode is open (control latch observed). Blocks new DATA/cutover (r2 R2.1).</summary>
        public bool RebirthPending { get { lock (_sync) { return _pending; } } }

        /// <summary>The diagnostic cause of the current episode; transport suspicion always reports Other and wins (r3 R3.3).</summary>
        public RebirthReason PendingReason { get { lock (_sync) { return _state == Suspect ? RebirthReason.Other : _reason; } } }

        /// <summary>
        /// Read the operational-control triple (suspect / pending / reason) under a SINGLE lock acquisition, so
        /// the diagnostic overlay never observes a torn combination that never existed atomically — e.g.
        /// suspect=false with reason=Other, which a disconnect racing between separate property reads could
        /// otherwise produce (slice-7 review r3 R3.1).
        /// </summary>
        public HandoffDiagnostics ReadDiagnostics()
        {
            lock (_sync)
            {
                var suspect = _state == Suspect;
                var reason = _pending ? (suspect ? RebirthReason.Other : _reason) : (RebirthReason?)null;
                return new HandoffDiagnostics(suspect, _pending, reason);
            }
        }

        /// <summary>Claim the initial promotion. Returns false if a disconnect already invalidated the attempt.</summary>
        public bool TryPromote()
        {
            lock (_sync)
            {
                if (_state == Establishing) { _state = Active; return true; }
                return false;
            }
        }

        /// <summary>
        /// Record a disconnect: invalidate a pre-promotion establishment, else mark the authority suspect
        /// (any of Active/Live/Rebirthing/RebirthCommitting → Suspect). Never lost, never both.
        /// </summary>
        public void OnDisconnect()
        {
            lock (_sync)
            {
                if (_state == Establishing) { _state = Invalidated; return; }
                MarkSuspectLocked();
            }
        }

        /// <summary>Mark the promoted authority suspect after an observable/uncertain send failure or a drop.</summary>
        public void MarkSuspect() { lock (_sync) { MarkSuspectLocked(); } }

        private void MarkSuspectLocked()
        {
            if (_state is Active or Live or Rebirthing or RebirthCommitting)
            {
                _state = Suspect;
            }
        }

        /// <summary>
        /// Atomically commit Live at cutover: requires BOTH an Active transport AND an idle episode, so a
        /// healthy pending rebirth (or a suspect event) prevents Live (slice-6 review r3 R3.2).
        /// </summary>
        public bool TryCommitLive()
        {
            lock (_sync)
            {
                if (_state == Active && !_pending) { _state = Live; return true; }
                return false;
            }
        }

        /// <summary>Atomically enter a HEALTHY in-place rebirth from Active or Live; false if suspect.</summary>
        public bool TryBeginRebirth()
        {
            lock (_sync)
            {
                if (_state is Active or Live) { _state = Rebirthing; return true; }
                return false;
            }
        }

        /// <summary>
        /// Win the healthy rebirth (Rebirthing → RebirthCommitting) and CONSUME the episode it fulfills, so
        /// any control event arriving during the commit re-arms a FRESH episode rather than being erased
        /// (slice-6 review r3 R3.1). Returns false if a disconnect/send loss moved the authority to Suspect
        /// — the caller pivots to the new-CONNECT branch.
        /// </summary>
        public bool TryCompleteRebirth()
        {
            lock (_sync)
            {
                if (_state != Rebirthing) { return false; }
                _state = RebirthCommitting;
                _committing = true; // suppress async drains until the new authority is published + Finish runs
                _pending = false;   // the rebirth fulfills the current episode; new events open a fresh one
                _queued = false;
                return true;
            }
        }

        /// <summary>
        /// Finish the healthy rebirth commit (RebirthCommitting → Active, unless a disconnect moved it to
        /// Suspect). Returns whether a FRESH episode is pending (a control event that arrived during the
        /// commit), so the caller drains it against the newly published authority (r3 R3.1).
        /// </summary>
        public bool FinishRebirthCommit()
        {
            lock (_sync)
            {
                if (_state == RebirthCommitting) { _state = Active; }
                _committing = false; // the new authority is published; async drains may resume
                return _pending;
            }
        }

        /// <summary>
        /// Open (or coalesce into) a rebirth episode, binding its cause on open. A coalescing event does NOT
        /// overwrite the accepted cause — first cause wins; transport suspicion is applied at read time via
        /// <see cref="PendingReason"/> (slice-6 review r3 R3.3).
        /// </summary>
        /// <returns>
        /// <c>true</c> if this opened a NEW episode; <c>false</c> if the signal folded into an already-open
        /// episode (a genuine coalesce). The caller counts coalescing here — never on a mere re-drain, which
        /// carries no new signal (slice-7 review B2).
        /// </returns>
        public bool MarkRebirthNeeded(RebirthReason reason)
        {
            lock (_sync)
            {
                if (!_pending)
                {
                    _pending = true;
                    _queued = false;
                    _reason = reason; // fresh episode installs its cause
                    return true;      // opened a new episode
                }

                return false;         // a new signal folded into the open episode → coalesced
            }
        }

        /// <summary>Claim the single Core request for the current episode. Only the first caller queues it.</summary>
        public bool TryTakeForQueue()
        {
            lock (_sync)
            {
                // Never queue mid-commit: the healthy rebirth's Finish drains the fresh episode against the
                // NEW authority, so an async event during the commit cannot queue against the old epoch (r3 R3.1).
                if (_pending && !_queued && !_committing) { _queued = true; return true; }
                return false;
            }
        }

        /// <summary>Release a claim when RequestRebirthAsync failed before acceptance, so a later attempt can requeue.</summary>
        public void ReleaseQueue()
        {
            lock (_sync)
            {
                if (_pending) { _queued = false; }
            }
        }
    }
}
