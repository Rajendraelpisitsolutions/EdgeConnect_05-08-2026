# K3 Slice 6 pass 1 r3 — Exact Source Diff (attachment)

**Commit `edbcbd5`** on `feat/sparkplug-b-k3-session-actor` (PR #188). Full unified diff with function context (`git show edbcbd5 -W`) for every file changed in pass-1 r3 (the single-lock handoff redesign + healthy-commit rewire + cutover-Live fix + tests).

```diff
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
index 4e5bb68..9fd16f8 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
@@ -42,1361 +42,1383 @@ namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;
 /// <summary>
 /// The single-owner Sparkplug session actor. All state mutation is serialized on the internal
 /// gate; the façade never touches protocol state directly.
 /// </summary>
 public sealed class SparkplugSessionActor : IAsyncDisposable
 {
     private const string NotYetImplemented =
         "The Sparkplug replay-session lifecycle lands in K3 slices 5-6 " +
         "(plan v3 §9); it is not available in this build.";
 
     private readonly SemaphoreSlim _gate = new(1, 1);
     private readonly string _instanceId;
 
     private readonly ISparkplugIdentityStateStore? _store;
     private readonly Func<ISparkplugMqttTransport>? _transportFactory;
     private readonly Func<DateTimeOffset> _clock;
 
     // One immutable, atomically-published snapshot of (coarse, fine) state.
     private volatile ActorSnapshot _snapshot = new(AdapterState.Created, SparkplugProtocolState.Stopped);
 
     private SparkplugSinkConfiguration? _config;
     private bool _disposed;
 
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
 
     // Test seam: inject a fake transport factory and/or a deterministic clock.
     internal SparkplugSessionActor(
         string instanceId,
         ISparkplugIdentityStateStore? store,
         Func<ISparkplugMqttTransport>? transportFactory,
         Func<DateTimeOffset>? clock)
     {
         ArgumentException.ThrowIfNullOrEmpty(instanceId);
         _instanceId = instanceId;
         _store = store;
         _transportFactory = transportFactory;
         _clock = clock ?? (() => DateTimeOffset.UtcNow);
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
-    /// Test seam awaited once AFTER a healthy rebirth's authority is published and the episode enters
-    /// Completing, but BEFORE it is cleared (slice-6 review r2 R2.2). Lets a test inject a second control
-    /// event and prove it re-arms a fresh episode against the new epoch rather than being erased.
+    /// Test seam awaited once AFTER a healthy rebirth wins (RebirthCommitting) but BEFORE the new authority
+    /// is published/finished (slice-6 review r2 R2.2 → r3 R3.1). Lets a test inject a control event/disconnect
+    /// in the commit window and prove it re-arms a fresh episode requested against the NEW epoch, never erased.
     /// </summary>
-    internal Func<Task>? PreEpisodeCompleteBarrier { get; set; }
+    internal Func<Task>? PostRebirthCommitBarrier { get; set; }
 
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
             SetAdapterState(AdapterState.Initializing);
 
             var validation = SparkplugSinkConfigurationValidator.Validate(config);
             if (!validation.IsValid)
             {
                 SetFaulted();
                 var issue = validation.Errors[0];
                 throw AdapterException.Configuration(issue.Code, issue.Message);
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
             try
             {
                 SetAdapterState(AdapterState.Starting);
                 if (GateHeldProbe is { } probe)
                 {
                     await probe(cancellationToken).ConfigureAwait(false);
                 }
 
                 SetAdapterState(AdapterState.Running);
             }
             catch
             {
                 SetFaulted();
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
 
         var snapshot = _snapshot;
         var active = _activeSession;
         var protocol = snapshot.ProtocolState;
         var hasSession = active is not null;
 
         var level = snapshot.State switch
         {
             AdapterState.Failed => HealthLevel.Unhealthy,
             AdapterState.Running when protocol is SparkplugProtocolState.Stopped or SparkplugProtocolState.Live
                 => HealthLevel.Healthy,
             AdapterState.Running => HealthLevel.Degraded,
             AdapterState.Degraded => HealthLevel.Degraded,
             _ => HealthLevel.Unknown,
         };
 
         var metrics = new Dictionary<string, object>
         {
             ["protocolState"] = protocol.ToString(),
             ["hasSession"] = hasSession,
             ["lastIssuedGeneration"] = _lastIssuedConnectionGeneration,
         };
         if (active is not null)
         {
             metrics["sessionId"] = active.SessionId.Value;
             metrics["epoch"] = active.Epoch.Value;
             metrics["connectionGeneration"] = active.TransportGeneration;
             metrics["bdSeq"] = active.BdSeq.Value;
         }
 
         return Task.FromResult(new AdapterHealth
         {
             State = snapshot.State,
             Level = level,
             CheckedAt = DateTime.UtcNow,
             Metrics = metrics,
         });
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
             RequireReadyForSession();
             var candidate = await EstablishNewConnectionAsync(
                 start.SessionId, start.Epoch, start.RouteId, start.Host, start.State.Snapshot, cancellationToken)
                 .ConfigureAwait(false);
             await PromoteAndDrainAsync(candidate).ConfigureAwait(false);
         }
         catch
         {
             SetFaulted(); // promote nothing; the driver faults the route; the previous epoch stands
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
         var config = _config!;
         var node = NodeIdentity();
         var endpoint = config.ResolveBrokerEndpoint();
         var storeIdentity = SparkplugStoreIdentity.Create(endpoint, node);
 
         SetProtocolState(SparkplugProtocolState.LoadingSession);
 
         // --- PREFLIGHT (no durable side effects) ---
         var plan = SparkplugBirthPlanner.Plan(snapshot);
         var aliases = _store!.ResolveAliases(storeIdentity, plan.ManifestKeys);
         var resolved = SparkplugBirthPlanner.Resolve(plan, aliases);
         var bdSeqAlias = resolved.AliasMap.Count == 0
             ? 1UL
             : checked(resolved.AliasMap.Values.Max() + 1UL);
         var baseline = SparkplugBirthBaseline.FromResolvedPlan(resolved);
 
         // Generation exhaustion is checked BEFORE reserving a durable bdSeq (carry-forward 2), so the
         // terminal long.MaxValue case can never consume a bdSeq with no possible CONNECT.
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
         var bdSeq = _store.ReserveNextBdSeq(storeIdentity);
 
         var connectRequest = SparkplugMqttConnectRequest.Create(
             endpoint, ResolveClientId(config), config.Username, config.Password, config.KeepAliveSeconds,
             cleanSession: true, SparkplugTopicFactory.NDeath(node), SparkplugPayloadEncoder.EncodeNDeath(bdSeq));
 
         var generation = _lastIssuedConnectionGeneration + 1;
         _lastIssuedConnectionGeneration = generation;
 
         ISparkplugMqttTransport? attempt = _transportFactory!();
         var handoff = new AttemptHandoff(generation);
         var disconnectHandler = MakeDisconnectHandler(generation, handoff);
         var nodeCommandHandler = MakeNodeCommandHandler(generation, handoff);
         attempt.Disconnected += disconnectHandler;
         attempt.NodeCommandReceived += nodeCommandHandler;
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
                 SparkplugSequenceNumber.Create(0), bdSeq, bdSeqAlias, _clock(), resolved.Metrics, resolved.AliasMap);
             var published = await attempt.PublishAsync(SparkplugTopicFactory.NBirth(node), nbirth, cancellationToken)
                 .ConfigureAwait(false);
             if (!published)
             {
                 throw BirthPublishFailed();
             }
 
             // Deterministic race barrier immediately before the initial promotion compare-exchange.
             if (PrePromotionBarrier is { } barrier)
             {
                 await barrier().ConfigureAwait(false);
             }
 
             var candidate = new ActiveSession(
                 attempt, generation, sessionId, epoch, routeId, host, bdSeq, resolved, baseline, handoff);
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
                 attempt.Disconnected -= disconnectHandler;
                 attempt.NodeCommandReceived -= nodeCommandHandler;
                 // ABORT: dispose without a clean DISCONNECT so the broker publishes the Will (NDEATH).
                 try { await attempt.DisposeAsync().ConfigureAwait(false); }
                 catch { /* retiring a failed attempt */ }
             }
         }
     }
 
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
         _nextSeq = 1;               // NBIRTH consumed seq 0
         SetProtocolState(SparkplugProtocolState.Replaying);
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
             try
             {
                 await RebirthGatedAsync(rebirth, cancellationToken).ConfigureAwait(false);
             }
             catch (OperationCanceledException)
             {
                 throw;
             }
             catch
             {
                 SetFaulted();
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
         var published = await SendAsync(session, SparkplugTopicFactory.NBirth(node), nbirth, cancellationToken)
             .ConfigureAwait(false);
         if (!published && !session.Handoff.SuspectAfterPromotion)
         {
             throw BirthPublishFailed(); // a genuine local NBIRTH failure with no transport loss is fatal (§4.5)
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
 
-        // Completed healthy: reuse the transport/generation/handoff/handlers; new epoch/manifest/baseline.
-        // Publish the new authority FIRST so a drained request queues against the NEW epoch.
-        _activeSession = session with { Epoch = rebirth.Epoch, Manifest = resolved, Baseline = baseline };
-        _nextSeq = 1;                             // the re-birth NBIRTH consumed seq 0
-        session.Handoff.BeginEpisodeCompletion(); // Queued -> Completing (a concurrent command re-arms it)
-        SetProtocolState(SparkplugProtocolState.Replaying);
-
-        // Deterministic race seam: a control event landing here re-arms the episode (r2 R2.2).
-        if (PreEpisodeCompleteBarrier is { } episodeBarrier)
+        // The rebirth won: state is RebirthCommitting and the OLD episode is consumed. Any control event
+        // arriving from here until FinishRebirthCommit re-arms a FRESH episode whose queue is suppressed
+        // (r3 R3.1), so it can only be requested against the NEW authority below.
+        if (PostRebirthCommitBarrier is { } commitBarrier)
         {
-            await episodeBarrier().ConfigureAwait(false);
+            await commitBarrier().ConfigureAwait(false);
         }
 
-        // Race-safe completion: if a command/disconnect re-armed the episode during completion, it stays
-        // pending as a NEW episode (never erased) and is drained against the new epoch.
-        if (!session.Handoff.TryCompleteEpisode())
+        // Publish the new authority BEFORE finishing the commit, so the drained fresh episode queues
+        // against the new epoch.
+        _activeSession = session with { Epoch = rebirth.Epoch, Manifest = resolved, Baseline = baseline };
+        _nextSeq = 1; // the re-birth NBIRTH consumed seq 0
+        SetProtocolState(SparkplugProtocolState.Replaying);
+
+        // Finish the commit (RebirthCommitting -> Active, or leave Suspect if a drop raced) and drain any
+        // fresh episode a control event opened during the commit — against the new authoritative epoch.
+        if (session.Handoff.FinishRebirthCommit())
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
         SetProtocolState(SparkplugProtocolState.RecoveringTransport);
         try { await previous.Transport.DisposeAsync().ConfigureAwait(false); }
         catch { /* retiring the suspect client */ }
 
         var candidate = await EstablishNewConnectionAsync(
             sessionId, epoch, previous.RouteId, previous.Host, snapshot, cancellationToken).ConfigureAwait(false);
         await PromoteAndDrainAsync(candidate).ConfigureAwait(false);
     }
 
     private Func<long, Task> MakeDisconnectHandler(long generation, AttemptHandoff handoff) =>
         droppedGeneration =>
         {
             if (droppedGeneration != generation)
             {
                 return Task.CompletedTask; // stale generation: ignore authoritatively (the generation gate)
             }
 
             handoff.OnDisconnect(); // atomic: invalidate a pre-promotion attempt OR mark the authority suspect
             if (handoff.IsInvalidated)
             {
                 return Task.CompletedTask; // pre-promotion drop -> Begin's establishment handles it, no rebirth
             }
 
             handoff.MarkRebirthNeeded(RebirthReason.Other);
             // Queue ONE coalesced Core rebirth now if the authority is published; otherwise establishment
             // drains it after publication (so an idle drop always wakes Core).
             return DrainRebirthAsync(handoff);
         };
 
     private Func<long, ReadOnlyMemory<byte>, Task> MakeNodeCommandHandler(long generation, AttemptHandoff handoff) =>
         (receivedGeneration, payload) =>
         {
             if (receivedGeneration != generation)
             {
                 return Task.CompletedTask; // stale generation: ignore
             }
 
             // Only a valid Node Control/Rebirth = true is actioned; every other NCMD is a no-op. A host
             // command marks the control episode pending (blocking new DATA) but does NOT mark suspect.
             if (!SparkplugNodeCommand.IsRebirthRequest(payload))
             {
                 return Task.CompletedTask;
             }
 
             handoff.MarkRebirthNeeded(RebirthReason.HostCommand);
             return DrainRebirthAsync(handoff);
         };
 
     // Queue exactly one Core rebirth for the handoff's current episode, against the CURRENT authoritative
     // session/epoch. A not-yet-published or superseded authority is a no-op (the drain runs again after
     // publication). If RequestRebirthAsync fails before acceptance, the claim is released so a later
     // attempt can requeue (slice-6 review r1 B1).
     private async Task DrainRebirthAsync(AttemptHandoff handoff)
     {
         var session = _activeSession;
         if (session is null || !ReferenceEquals(session.Handoff, handoff) || !handoff.TryTakeForQueue())
         {
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
             try
             {
                 return await PublishGatedAsync(points, context, started, cancellationToken).ConfigureAwait(false);
             }
             catch (OperationCanceledException)
             {
                 throw; // cancellation is not a fault
             }
             catch
             {
                 SetFaulted(); // a hard fail-closed violation (no session, session/epoch mismatch, material mutation)
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
             session, SparkplugTopicFactory.NData(NodeIdentity()), payload, cancellationToken).ConfigureAwait(false);
         if (!published)
         {
             return await FailWithRebirthAsync(
                 session, RebirthReason.Other, "the DATA batch did not complete at the local transport boundary.",
                 latchSuspect: true, started, cancellationToken).ConfigureAwait(false);
         }
 
         _nextSeq = (_nextSeq + 1) & 0xFF; // advance ONLY after local success
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
             try
             {
                 await CompleteCatchUpGatedAsync(cutover, cancellationToken).ConfigureAwait(false);
             }
             catch (OperationCanceledException)
             {
                 throw;
             }
             catch
             {
                 SetFaulted();
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
                 session, SparkplugTopicFactory.NData(NodeIdentity()), payload, cancellationToken).ConfigureAwait(false);
             if (!published)
             {
                 await RequestRebirthAsync(
                     session, RebirthReason.Other, "the final update did not complete at the local transport boundary.",
                     latchSuspect: true, cancellationToken).ConfigureAwait(false);
                 return; // §4.4: do not enter Live
             }
 
             _nextSeq = (_nextSeq + 1) & 0xFF;
         }
 
         // Deterministic race barrier immediately before the atomic Live commit (review r1 B4).
         if (PreLiveCommitBarrier is { } barrier)
         {
             await barrier().ConfigureAwait(false);
         }
 
-        // Atomic cutover→Live vs. the asynchronous suspect latch: a disconnect/send failure that raced
-        // the commit wins, and we request a rebirth instead of installing Live on a suspect authority.
+        // Atomic cutover→Live: TryCommitLive requires BOTH an Active transport AND an idle episode, so a
+        // disconnect/send failure (suspect) OR a healthy pending rebirth (NCMD/first-observed) that raced
+        // the commit prevents Live (slice-6 review r3 R3.2). A suspect race latches suspect; a healthy
+        // pending race stays healthy — either way we defer to the pending rebirth instead of Live.
         if (!session.Handoff.TryCommitLive())
         {
-            SetProtocolState(SparkplugProtocolState.Suspect);
-            await RequestRebirthAsync(
-                session, RebirthReason.Other, "the transport became suspect during the cutover-to-Live commit.",
-                latchSuspect: false, cancellationToken).ConfigureAwait(false);
+            if (session.Handoff.SuspectAfterPromotion)
+            {
+                SetProtocolState(SparkplugProtocolState.Suspect);
+            }
+
+            await DrainRebirthAsync(session.Handoff).ConfigureAwait(false); // ensure the pending request is queued
             return;
         }
 
         SetProtocolState(SparkplugProtocolState.Live);
     }
 
     /// <summary>End the session gracefully (NDEATH + disconnect). Implemented in K3 slice 6.</summary>
     /// <param name="sessionEnd">The session-end inputs.</param>
     /// <param name="cancellationToken">Cancellation token.</param>
     /// <returns>A task that completes when the session has ended.</returns>
     public Task EndSessionAsync(ReplaySessionEnd sessionEnd, CancellationToken cancellationToken)
         => throw new NotImplementedException(NotYetImplemented);
 
     /// <summary>
     /// Dispose the actor's resources — retires any active transport (ABORT, so the broker
     /// publishes the Will). NOT safe to call concurrently with an in-flight lifecycle call.
     /// </summary>
     /// <returns>A completed task.</returns>
     public async ValueTask DisposeAsync()
     {
         if (_disposed)
         {
             return;
         }
 
         _disposed = true;
         await RetireActiveSessionAsync().ConfigureAwait(false);
         _gate.Dispose();
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
         session.Handoff.MarkRebirthNeeded(reason);
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
         ActiveSession session, string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
     {
         try
         {
             return await session.Transport.PublishAsync(topic, payload, cancellationToken).ConfigureAwait(false);
         }
         catch (OperationCanceledException)
         {
             session.Handoff.MarkSuspect();
             SetProtocolState(SparkplugProtocolState.Suspect);
             throw;
         }
         catch
         {
             session.Handoff.MarkSuspect();
             SetProtocolState(SparkplugProtocolState.Suspect);
             return false;
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
 
     private void SetProtocolState(SparkplugProtocolState protocol) =>
         _snapshot = _snapshot with { ProtocolState = protocol };
 
     private void SetAdapterState(AdapterState target)
     {
         var current = _snapshot.State;
         if (!AdapterStateTransitions.IsAllowed(current, target))
         {
             throw new InvalidOperationException(
                 $"SparkplugSessionActor '{_instanceId}' cannot transition from {current} to {target}.");
         }
 
         _snapshot = _snapshot with { State = target };
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
     }
 
     private void SetFaulted()
     {
         if (AdapterStateTransitions.IsAllowed(_snapshot.State, AdapterState.Failed))
         {
             _snapshot = new ActorSnapshot(AdapterState.Failed, SparkplugProtocolState.Faulted);
         }
     }
 
     private sealed record ActorSnapshot(AdapterState State, SparkplugProtocolState ProtocolState);
 
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
-    /// The atomic authority lifecycle for one CONNECT attempt (review r2 R2 + slice-5 review r1 B4).
-    /// One lock-free state word linearizes three concurrent decisions against an ASYNCHRONOUS
-    /// (un-gated) disconnect callback: (1) establishment vs. a pre-promotion drop; (2) the promotion
-    /// compare-exchange; (3) the cutover-to-Live commit vs. a post-promotion suspect event. A drop or
-    /// send failure concurrent with any of these is never lost — it either invalidates a pre-promotion
-    /// establishment or marks the promoted authority suspect, and a suspect authority can never win the
-    /// Live commit.
+    /// The atomic authority lifecycle + control-request episode for one CONNECT attempt. A SINGLE short
+    /// internal lock guards the compound (transport-state, episode, reason) so every cross-word transition
+    /// is atomic against the ASYNCHRONOUS (un-gated) disconnect/NCMD callbacks (slice-6 review r2 → r3):
+    /// establishment vs. a pre-promotion drop; the promotion CAS; the cutover-to-Live commit (which
+    /// requires BOTH Active transport AND an idle episode); and the healthy rebirth commit
+    /// (Rebirthing → RebirthCommitting → Active) with race-safe episode turnover and a cause bound to the
+    /// episode. The lock is only ever held for a few field assignments (never across an await), so there is
+    /// no contention or deadlock risk.
     /// </summary>
     private sealed class AttemptHandoff
     {
-        // Transport/authority lifecycle (one atomic word). Async (un-gated) callbacks and gated
-        // establishment/cutover/rebirth-completion contend on this word so a drop is never lost and a
-        // suspect authority can never win the Live or rebirth-completion commit (slice-6 review r1 B2/B4).
-        private const int Establishing = 0; // initial CONNECT establishment in flight
-        private const int Invalidated = 1;  // a drop won before the first promotion
-        private const int Active = 2;       // promoted; replaying/catching-up
-        private const int Live = 3;         // cutover committed
-        private const int Rebirthing = 4;   // a HEALTHY in-place rebirth is in flight
-        private const int Suspect = 5;      // a drop / observable-or-uncertain send failure lost the transport
-
-        // Control-request episode (separate from transport health): coalesces the Core rebirth request,
-        // gates DATA/cutover, remembers WHY the rebirth is needed, and completes RACE-SAFELY so a control
-        // event arriving during promotion is never erased (slice-6 review r1 B1 → r2 R2.2/R2.3).
-        private const int EpisodeIdle = 0;
-        private const int EpisodeNeeded = 1;      // a rebirth is required but not yet queued to Core
-        private const int EpisodeQueued = 2;      // the Core request has been queued/accepted
-        private const int EpisodeCompleting = 3;  // the queued rebirth is being promoted; a new need re-arms it
-
+        private const int Establishing = 0;       // initial CONNECT establishment in flight
+        private const int Invalidated = 1;        // a drop won before the first promotion
+        private const int Active = 2;             // promoted; replaying/catching-up
+        private const int Live = 3;               // cutover committed
+        private const int Rebirthing = 4;         // a HEALTHY in-place rebirth is in flight
+        private const int RebirthCommitting = 5;  // the rebirth won; the new authority is being published
+        private const int Suspect = 6;            // a drop / observable-or-uncertain send failure lost the transport
+
+        private readonly object _sync = new();
         private int _state = Establishing;
-        private int _episode = EpisodeIdle;
-        private int _pendingReason = (int)RebirthReason.Other; // the cause of the current episode (diagnostic)
+        private bool _pending;                              // a rebirth is required (control episode open)
+        private bool _queued;                               // the Core request for the current episode was queued
+        private bool _committing;                           // a healthy rebirth is publishing the new authority
+        private RebirthReason _reason = RebirthReason.Other; // the cause of the CURRENT episode (bound on open)
 
         public AttemptHandoff(long generation) => Generation = generation;
 
         /// <summary>This attempt's connection generation.</summary>
         public long Generation { get; }
 
         /// <summary>True once a disconnect invalidated an in-progress (pre-promotion) establishment.</summary>
-        public bool IsInvalidated => Volatile.Read(ref _state) == Invalidated;
+        public bool IsInvalidated { get { lock (_sync) { return _state == Invalidated; } } }
 
         /// <summary>True once the promoted authority's transport became suspect (a drop or an uncertain send).</summary>
-        public bool SuspectAfterPromotion => Volatile.Read(ref _state) == Suspect;
+        public bool SuspectAfterPromotion { get { lock (_sync) { return _state == Suspect; } } }
 
-        /// <summary>
-        /// A rebirth episode is pending (an NCMD/first-observed/transport control latch was observed). No
-        /// new DATA send starts once a rebirth is pending — but a HEALTHY pending rebirth must NOT be
-        /// treated as a transport failure (slice-6 review r1 → r2 R2.1). Distinct from
-        /// <see cref="SuspectAfterPromotion"/>, which is transport loss only.
-        /// </summary>
-        public bool RebirthPending => Volatile.Read(ref _episode) != EpisodeIdle;
+        /// <summary>A rebirth episode is open (control latch observed). Blocks new DATA/cutover (r2 R2.1).</summary>
+        public bool RebirthPending { get { lock (_sync) { return _pending; } } }
 
-        /// <summary>The diagnostic cause of the current episode (Other if the transport is suspect).</summary>
-        public RebirthReason PendingReason =>
-            SuspectAfterPromotion ? RebirthReason.Other : (RebirthReason)Volatile.Read(ref _pendingReason);
+        /// <summary>The diagnostic cause of the current episode; transport suspicion always reports Other and wins (r3 R3.3).</summary>
+        public RebirthReason PendingReason { get { lock (_sync) { return _state == Suspect ? RebirthReason.Other : _reason; } } }
+
+        /// <summary>Claim the initial promotion. Returns false if a disconnect already invalidated the attempt.</summary>
+        public bool TryPromote()
+        {
+            lock (_sync)
+            {
+                if (_state == Establishing) { _state = Active; return true; }
+                return false;
+            }
+        }
 
         /// <summary>
-        /// Record a disconnect for this attempt's generation. Atomically invalidates an in-progress
-        /// establishment (before promotion) OR marks the promoted authority suspect — never both, never lost.
+        /// Record a disconnect: invalidate a pre-promotion establishment, else mark the authority suspect
+        /// (any of Active/Live/Rebirthing/RebirthCommitting → Suspect). Never lost, never both.
         /// </summary>
         public void OnDisconnect()
         {
-            if (Interlocked.CompareExchange(ref _state, Invalidated, Establishing) != Establishing)
+            lock (_sync)
             {
-                MarkSuspect(); // post-promotion (Active/Live/Rebirthing/Suspect) → a suspect authority event
+                if (_state == Establishing) { _state = Invalidated; return; }
+                MarkSuspectLocked();
             }
         }
 
-        /// <summary>Claim the initial promotion. Returns false if a disconnect already invalidated the attempt.</summary>
-        public bool TryPromote() =>
-            Interlocked.CompareExchange(ref _state, Active, Establishing) == Establishing;
+        /// <summary>Mark the promoted authority suspect after an observable/uncertain send failure or a drop.</summary>
+        public void MarkSuspect() { lock (_sync) { MarkSuspectLocked(); } }
+
+        private void MarkSuspectLocked()
+        {
+            if (_state is Active or Live or Rebirthing or RebirthCommitting)
+            {
+                _state = Suspect;
+            }
+        }
 
         /// <summary>
-        /// Mark the promoted authority suspect after an observable/uncertain send failure or a drop.
-        /// Atomically transitions any of Active/Live/Rebirthing to Suspect; idempotent from Suspect.
+        /// Atomically commit Live at cutover: requires BOTH an Active transport AND an idle episode, so a
+        /// healthy pending rebirth (or a suspect event) prevents Live (slice-6 review r3 R3.2).
         /// </summary>
-        public void MarkSuspect()
+        public bool TryCommitLive()
         {
-            while (true)
+            lock (_sync)
             {
-                var state = Volatile.Read(ref _state);
-                if (state is Suspect or Establishing or Invalidated)
-                {
-                    return; // already suspect, or pre-promotion (a pre-promotion drop invalidates, not suspects)
-                }
-
-                if (Interlocked.CompareExchange(ref _state, Suspect, state) == state)
-                {
-                    return;
-                }
+                if (_state == Active && !_pending) { _state = Live; return true; }
+                return false;
             }
         }
 
-        /// <summary>Atomically commit Live at cutover. Returns false if a suspect/rebirth event won the race.</summary>
-        public bool TryCommitLive() =>
-            Interlocked.CompareExchange(ref _state, Live, Active) == Active;
+        /// <summary>Atomically enter a HEALTHY in-place rebirth from Active or Live; false if suspect.</summary>
+        public bool TryBeginRebirth()
+        {
+            lock (_sync)
+            {
+                if (_state is Active or Live) { _state = Rebirthing; return true; }
+                return false;
+            }
+        }
 
         /// <summary>
-        /// Atomically enter a HEALTHY in-place rebirth from Active or Live. Returns false if the authority
-        /// is (or raced into) suspect — the caller escalates to the new-CONNECT branch (§4.1).
+        /// Win the healthy rebirth (Rebirthing → RebirthCommitting) and CONSUME the episode it fulfills, so
+        /// any control event arriving during the commit re-arms a FRESH episode rather than being erased
+        /// (slice-6 review r3 R3.1). Returns false if a disconnect/send loss moved the authority to Suspect
+        /// — the caller pivots to the new-CONNECT branch.
         /// </summary>
-        public bool TryBeginRebirth() =>
-            Interlocked.CompareExchange(ref _state, Rebirthing, Active) == Active
-            || Interlocked.CompareExchange(ref _state, Rebirthing, Live) == Live;
+        public bool TryCompleteRebirth()
+        {
+            lock (_sync)
+            {
+                if (_state != Rebirthing) { return false; }
+                _state = RebirthCommitting;
+                _committing = true; // suppress async drains until the new authority is published + Finish runs
+                _pending = false;   // the rebirth fulfills the current episode; new events open a fresh one
+                _queued = false;
+                return true;
+            }
+        }
 
         /// <summary>
-        /// Atomically complete a healthy rebirth (Rebirthing -> Active). Returns false if a disconnect/send
-        /// failure moved the authority to Suspect during the rebirth — the caller must NOT install the new
-        /// epoch and instead pivots to the transport-suspect new-CONNECT branch (slice-6 review r1 B2).
+        /// Finish the healthy rebirth commit (RebirthCommitting → Active, unless a disconnect moved it to
+        /// Suspect). Returns whether a FRESH episode is pending (a control event that arrived during the
+        /// commit), so the caller drains it against the newly published authority (r3 R3.1).
         /// </summary>
-        public bool TryCompleteRebirth() =>
-            Interlocked.CompareExchange(ref _state, Active, Rebirthing) == Rebirthing;
+        public bool FinishRebirthCommit()
+        {
+            lock (_sync)
+            {
+                if (_state == RebirthCommitting) { _state = Active; }
+                _committing = false; // the new authority is published; async drains may resume
+                return _pending;
+            }
+        }
 
         /// <summary>
-        /// Mark that a rebirth is required for the current episode, remembering its cause. From Idle or
-        /// Completing it (re-)arms to Needed — a control event that lands during a completing rebirth is
-        /// never lost, it becomes a fresh pending episode (slice-6 review r2 R2.2). No-op once Needed/Queued
-        /// (coalesced). The cause is stored for the diagnostic reason (R2.3).
+        /// Open (or coalesce into) a rebirth episode, binding its cause on open. A coalescing event does NOT
+        /// overwrite the accepted cause — first cause wins; transport suspicion is applied at read time via
+        /// <see cref="PendingReason"/> (slice-6 review r3 R3.3).
         /// </summary>
         public void MarkRebirthNeeded(RebirthReason reason)
         {
-            Volatile.Write(ref _pendingReason, (int)reason);
-            while (true)
+            lock (_sync)
             {
-                var episode = Volatile.Read(ref _episode);
-                if (episode is EpisodeNeeded or EpisodeQueued)
+                if (!_pending)
                 {
-                    return; // already pending — coalesce
-                }
-
-                if (Interlocked.CompareExchange(ref _episode, EpisodeNeeded, episode) == episode)
-                {
-                    return; // Idle or Completing -> Needed
+                    _pending = true;
+                    _queued = false;
+                    _reason = reason; // fresh episode installs its cause
                 }
             }
         }
 
         /// <summary>Claim the single Core request for the current episode. Only the first caller queues it.</summary>
-        public bool TryTakeForQueue() =>
-            Interlocked.CompareExchange(ref _episode, EpisodeQueued, EpisodeNeeded) == EpisodeNeeded;
+        public bool TryTakeForQueue()
+        {
+            lock (_sync)
+            {
+                // Never queue mid-commit: the healthy rebirth's Finish drains the fresh episode against the
+                // NEW authority, so an async event during the commit cannot queue against the old epoch (r3 R3.1).
+                if (_pending && !_queued && !_committing) { _queued = true; return true; }
+                return false;
+            }
+        }
 
         /// <summary>Release a claim when RequestRebirthAsync failed before acceptance, so a later attempt can requeue.</summary>
-        public void ReleaseQueue() =>
-            Interlocked.CompareExchange(ref _episode, EpisodeNeeded, EpisodeQueued);
-
-        /// <summary>Enter the completion phase (Queued -> Completing); a concurrent need re-arms it to Needed.</summary>
-        public void BeginEpisodeCompletion() =>
-            Interlocked.CompareExchange(ref _episode, EpisodeCompleting, EpisodeQueued);
-
-        /// <summary>
-        /// Finish the completing episode (Completing -> Idle). Returns false if a control event re-armed it
-        /// to Needed during completion — the caller then drains that fresh pending episode (slice-6 review
-        /// r2 R2.2), so a concurrent command is never erased by the reset.
-        /// </summary>
-        public bool TryCompleteEpisode() =>
-            Interlocked.CompareExchange(ref _episode, EpisodeIdle, EpisodeCompleting) == EpisodeCompleting;
+        public void ReleaseQueue()
+        {
+            lock (_sync)
+            {
+                if (_pending) { _queued = false; }
+            }
+        }
     }
 }
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
index cbaf72e..0c25f86 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
@@ -36,596 +36,611 @@ namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;
 public sealed class SparkplugSessionActorRebirthTests : IDisposable
 {
     private const string Group = "PlantA";
     private const string Node = "gw-1";
     private static readonly DateTimeOffset Clock = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
 
     private readonly string _dir = Path.Combine(Path.GetTempPath(), "k3-rebirth-" + Guid.NewGuid().ToString("N"));
 
     public void Dispose()
     {
         SqliteConnection.ClearAllPools();
         try { if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); } }
         catch { /* best effort */ }
     }
 
     // ==== Healthy in-place rebirth ====
 
     [Fact]
     public async Task Rebirth_HealthyTransport_ReusesConnection_RetainsBdSeq_AdvancesEpoch()
     {
         var (actor, fake, _) = await Born();
         var nbirthsBefore = NBirths(fake).Count;
 
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
 
         actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1)); // epoch advanced
         actor.CurrentBdSeq.Value.Should().Be(0);                 // bdSeq RETAINED (healthy)
         actor.CurrentGeneration.Should().Be(1);                  // same connection/generation
         actor.HasSession.Should().BeTrue();
         actor.ProtocolState.Should().Be(SparkplugProtocolState.Replaying);
         actor.NextSeq.Should().Be(1);                            // re-birth NBIRTH consumed seq 0
         NBirths(fake).Count.Should().Be(nbirthsBefore + 1);      // re-emitted on the SAME connection (no new connect)
     }
 
     [Fact]
     public async Task Rebirth_HealthyTransport_ReEmitsNBirthSeq0_WithRetainedBdSeq()
     {
         var (actor, fake, _) = await Born();
         fake.Published.Clear();
 
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
 
         // The re-birth NBIRTH carries seq=0 and the retained bdSeq=0 (byte-parity via K2).
         var expected = SparkplugPayloadEncoder.EncodeNBirth(
             SparkplugSequenceNumber.Create(0), SparkplugBirthDeathSequence.Create(0), bdSeqAlias: 1UL, Clock,
             actor.CurrentManifest!.Metrics, actor.CurrentManifest.AliasMap);
         NBirths(fake).Single().Should().Equal(expected);
     }
 
     [Fact]
     public async Task Rebirth_HealthyNBirthFails_IsFatal_Faults()
     {
         var (actor, fake, _) = await Born();
         fake.PublishReturnsFalse = true; // the re-birth NBIRTH send fails
 
         await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None))
             .Should().ThrowAsync<AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.BirthPublishFailed);
 
         actor.State.Should().Be(AdapterState.Failed); // healthy-transport rebirth NBIRTH failure is immediately fatal
     }
 
     // ==== Transport-suspect rebirth (new CONNECT + new bdSeq) ====
 
     [Fact]
     public async Task Rebirth_TransportSuspect_NewConnect_NewBdSeq_NewGeneration_RetiresOldClient()
     {
         var store = NewStore();
         var fake1 = new FakeTransport();
         var fake2 = new FakeTransport();
         var call = 0;
         var host = new CapturingHost();
         var actor = new SparkplugSessionActor(
             "spb-1", store, () => call++ == 0 ? (ISparkplugMqttTransport)fake1 : fake2, () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
 
         await fake1.RaiseDisconnected(actor.CurrentGeneration); // drop → suspect (+ one coalesced rebirth request)
         actor.CurrentSessionSuspect.Should().BeTrue();
 
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
 
         fake1.Disposed.Should().BeTrue();                 // old client abandoned (broker publishes its Will)
         fake2.Connected.Should().BeTrue();                // fresh CONNECT on the replacement client
         NBirths(fake2).Should().ContainSingle();          // fresh NBIRTH
         actor.CurrentBdSeq.Value.Should().Be(1);          // NEW bdSeq reserved for the new CONNECT
         actor.CurrentGeneration.Should().Be(2);           // new connection generation
         actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1));
         actor.CurrentSessionSuspect.Should().BeFalse();   // fresh handoff — no longer suspect
         actor.ProtocolState.Should().Be(SparkplugProtocolState.Replaying);
     }
 
     // ==== Rebirth gating ====
 
     [Fact]
     public async Task Rebirth_WrongSession_FailsClosed()
     {
         var (actor, _, _) = await Born();
 
         await actor.Invoking(a => a.RebirthAsync(
                 ReplaySessionRebirth.Create(ReplaySessionId.Create(999), ReplayEpochId.Create(1), StateOf(1)), CancellationToken.None))
             .Should().ThrowAsync<AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.PublishSessionMismatch);
 
         actor.State.Should().Be(AdapterState.Failed);
     }
 
     [Theory]
     [InlineData(0)] // equal to the current epoch
     [InlineData(-1)] // below (encoded as 0 here; equal case covers non-increasing)
     public async Task Rebirth_NonIncreasingEpoch_FailsClosed(int epochDelta)
     {
         var (actor, _, _) = await Born(); // current epoch 0
         var epoch = Math.Max(0, epochDelta);
 
         await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch), CancellationToken.None))
             .Should().ThrowAsync<AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.PublishEpochMismatch);
 
         actor.State.Should().Be(AdapterState.Failed);
     }
 
     // ==== Async idle disconnect -> coalesced Core rebirth ====
 
     [Fact]
     public async Task Disconnect_PostPromotion_RequestsOneCoalescedRebirth_Other()
     {
         var (actor, fake, host) = await Born();
 
         await fake.RaiseDisconnected(actor.CurrentGeneration);
         await fake.RaiseDisconnected(actor.CurrentGeneration); // a repeat drop must coalesce
 
         actor.CurrentSessionSuspect.Should().BeTrue();
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
     }
 
     [Fact]
     public async Task Disconnect_StaleGeneration_Ignored()
     {
         var (actor, fake, host) = await Born();
 
         await fake.RaiseDisconnected(actor.CurrentGeneration + 99); // a retired client's delayed callback
 
         actor.CurrentSessionSuspect.Should().BeFalse(); // stale generation gate — no effect
         host.Requests.Should().BeEmpty();
     }
 
     // ==== NCMD -> HostCommand rebirth ====
 
     [Fact]
     public async Task NodeCommand_RebirthTrue_RequestsHostCommandRebirth_NoSuspect()
     {
         var (actor, fake, host) = await Born();
 
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
 
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.HostCommand);
         actor.CurrentSessionSuspect.Should().BeFalse(); // a host command does not mark the transport suspect
     }
 
     [Fact]
     public async Task NodeCommand_NotRebirth_NoRequest()
     {
         var (actor, fake, host) = await Born();
 
         await fake.RaiseNodeCommand(actor.CurrentGeneration, NonRebirthCommand());
 
         host.Requests.Should().BeEmpty();
     }
 
     [Fact]
     public async Task NodeCommand_StaleGeneration_Ignored()
     {
         var (actor, fake, host) = await Born();
 
         await fake.RaiseNodeCommand(actor.CurrentGeneration + 99, RebirthCommand());
 
         host.Requests.Should().BeEmpty();
     }
 
     [Fact]
     public async Task Disconnect_ThenNodeCommand_CoalesceToOneRequest()
     {
         var (actor, fake, host) = await Born();
 
         await fake.RaiseDisconnected(actor.CurrentGeneration);          // requests (Other)
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // coalesced away
 
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
     }
 
     // ==== B4: establishment-promotion drain (disconnect between promotion CAS and publication) ====
 
     [Fact]
     public async Task Establish_DisconnectAfterPromotionBeforePublish_DrainsExactlyOneRebirth()
     {
         var fake = new FakeTransport();
         var host = new CapturingHost();
         var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         // A disconnect lands after the promotion CAS but before _activeSession is published.
         actor.PostPromotionBarrier = () => fake.RaiseDisconnected(fake.Generation!.Value);
 
         await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
 
         actor.HasSession.Should().BeTrue();
         actor.CurrentSessionSuspect.Should().BeTrue();
         // No DATA arrival required — establishment drained exactly one Other rebirth request.
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
     }
 
     // ==== B1: episode is per-rebirth, resettable, DATA-visible, failure-safe ====
 
     [Fact]
     public async Task Rebirth_Healthy_ThenSecondNodeCommand_StartsNewEpisode_QueuesSecondRequest()
     {
         var (actor, fake, host) = await Born();
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // NCMD #1 -> request 1
         host.Requests.Should().ContainSingle();
 
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy rebirth resets the episode
 
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // NCMD #2 -> a NEW episode
         host.Requests.Should().HaveCount(2);
         host.Requests[1].Reason.Should().Be(RebirthReason.HostCommand);
         host.Requests[1].Epoch.Value.Should().Be(1); // against the newly authoritative epoch
     }
 
     [Fact]
     public async Task NodeCommand_Repeated_BeforeRebirth_CoalesceToOneRequest()
     {
         var (actor, fake, host) = await Born();
 
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
 
         host.Requests.Should().ContainSingle(); // coalesced to one pending request
     }
 
     [Fact]
     public async Task HostRequestFailure_ReleasesClaim_AllowsLaterRebirthRequest()
     {
         var (actor, fake, host) = await Born();
         host.ThrowOnRequestCount = 1; // the first RequestRebirthAsync throws before acceptance
 
         await actor.Invoking(a => fake.RaiseNodeCommand(a.CurrentGeneration, RebirthCommand()))
             .Should().ThrowAsync<InvalidOperationException>();
         host.Requests.Should().BeEmpty(); // not accepted
 
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // a later NCMD can requeue
         host.Requests.Should().ContainSingle(); // the connection is not permanently stuck
     }
 
     // ==== B2: atomic healthy-rebirth completion vs. a racing disconnect ====
 
     [Fact]
     public async Task Rebirth_DisconnectBeforeHealthyCompletion_PivotsToSuspect_NewConnect()
     {
         var (actor, fake1, fake2, host) = TwoFakeActor();
         await Begin(actor, host);
         // A disconnect lands after the re-birth NBIRTH but before the completion CAS.
         actor.PreRebirthCommitBarrier = () => fake1.RaiseDisconnected(1);
 
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
 
         fake1.Disposed.Should().BeTrue();       // pivoted: old client abandoned
         fake2.Connected.Should().BeTrue();      // new CONNECT
         actor.CurrentBdSeq.Value.Should().Be(1); // new bdSeq
         actor.CurrentGeneration.Should().Be(2);  // new generation
         actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1));
         actor.CurrentSessionSuspect.Should().BeFalse();
     }
 
     [Fact]
     public async Task Rebirth_DisconnectDuringHealthyNBirth_PivotsToSuspect()
     {
         var (actor, fake1, fake2, host) = TwoFakeActor();
         await Begin(actor, host);
         fake1.OnPublishOnce = () => fake1.RaiseDisconnected(1); // drop DURING the re-birth NBIRTH publish
 
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
 
         fake2.Connected.Should().BeTrue();       // pivoted to a new CONNECT
         actor.CurrentGeneration.Should().Be(2);
         actor.CurrentBdSeq.Value.Should().Be(1);
     }
 
     [Fact]
     public async Task Rebirth_NodeCommandThenDisconnect_UsesNewConnectionAndBdSeq()
     {
         var (actor, fake1, fake2, host) = TwoFakeActor();
         await Begin(actor, host);
         await fake1.RaiseNodeCommand(1, RebirthCommand()); // host command (healthy pending)
         await fake1.RaiseDisconnected(1);                  // then a transport loss -> suspect wins
 
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
 
         fake2.Connected.Should().BeTrue();       // transport-suspect branch (new CONNECT), not healthy
         actor.CurrentGeneration.Should().Be(2);
         actor.CurrentBdSeq.Value.Should().Be(1);
     }
 
     [Fact]
     public async Task Rebirth_DisconnectAfterHealthyPromotion_RequestsAgainstNewEpoch()
     {
         var (actor, fake, host) = await Born();
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy → epoch 1
 
         await fake.RaiseDisconnected(actor.CurrentGeneration);
 
         actor.CurrentSessionSuspect.Should().BeTrue();
         host.Requests.Should().ContainSingle().Which.Epoch.Value.Should().Be(1); // against the new epoch
     }
 
     // ==== B3: candidate-only suspect rebirth preserves the previous authority on failure ====
 
     [Theory]
     [InlineData("connect")]
     [InlineData("subscribe")]
     [InlineData("nbirth")]
     public async Task Rebirth_SuspectReplacementFails_PreservesPreviousAuthority(string failAt)
     {
         var (actor, fake1, fake2, host) = TwoFakeActor();
         await Begin(actor, host);
         await fake1.RaiseDisconnected(1); // suspect → the rebirth will take the new-CONNECT branch
         var prevManifestCount = actor.CurrentManifest!.Metrics.Length;
         switch (failAt)
         {
             case "connect": fake2.FailConnect = true; break;
             case "subscribe": fake2.FailSubscribe = true; break;
             case "nbirth": fake2.PublishReturnsFalse = true; break;
         }
 
         await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None))
             .Should().ThrowAsync<Exception>();
 
         actor.State.Should().Be(AdapterState.Failed);
         // The PREVIOUS authority is preserved (never erased by the failed candidate) — B3.
         actor.CurrentSessionId.Should().Be(ReplaySessionId.Create(1));
         actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(0)); // still the pre-rebirth epoch
         actor.CurrentBdSeq.Value.Should().Be(0);                 // previous bdSeq
         actor.CurrentManifest!.Metrics.Length.Should().Be(prevManifestCount);
     }
 
     // ==== R2.1: a healthy pending rebirth is not a transport failure ====
 
     [Fact]
     public async Task NodeCommand_PendingRebirth_StaysHealthy_RebirthReusesConnection()
     {
         var (actor, fake, _) = await Born();
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
         actor.CurrentSessionSuspect.Should().BeFalse(); // pending, but transport healthy
 
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
 
         actor.CurrentGeneration.Should().Be(1);  // reused connection (healthy branch, not new-CONNECT)
         actor.CurrentBdSeq.Value.Should().Be(0);  // retained bdSeq
         actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1));
         fake.Disposed.Should().BeFalse();
     }
 
     // ==== R2.2: race-safe episode completion (a command during completion is not erased) ====
 
     [Fact]
     public async Task Rebirth_SecondNodeCommandDuringEpisodeCompletion_QueuesSecondRequest_NewEpoch()
     {
         var (actor, fake, host) = await Born();
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // NCMD #1 -> request 1 (epoch 0)
-        // A second NCMD lands after the new authority is published but before the episode is cleared.
-        actor.PreEpisodeCompleteBarrier = () => fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
+        // A second NCMD lands in the commit window (after the rebirth wins, before the new authority is finished).
+        actor.PostRebirthCommitBarrier = () => fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
 
         await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
 
         host.Requests.Should().HaveCount(2);          // the second command is NOT erased by the reset
         host.Requests[1].Reason.Should().Be(RebirthReason.HostCommand);
         host.Requests[1].Epoch.Value.Should().Be(1);  // queued against the newly authoritative epoch
     }
 
+    [Fact]
+    public async Task Rebirth_DisconnectDuringCommit_InstallsNewEpochSuspect_QueuesWake()
+    {
+        var (actor, fake, host) = await Born();
+        // A disconnect lands in the commit window (after the rebirth wins, before the new authority is finished).
+        actor.PostRebirthCommitBarrier = () => fake.RaiseDisconnected(actor.CurrentGeneration);
+
+        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
+
+        actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(1)); // the new authority IS installed
+        actor.CurrentSessionSuspect.Should().BeTrue();           // ... but suspect (the drop was not lost)
+        // The idle route is woken by a queued request against the NEW epoch (no DATA needed) — r3 R3.1.
+        host.Requests.Should().ContainSingle().Which.Epoch.Value.Should().Be(1);
+    }
+
     // ==== R2.3: the drained reason is preserved (and transport-suspect takes precedence) ====
 
     [Fact]
     public async Task Establish_NodeCommandBeforePublish_DrainsAsHostCommand()
     {
         var fake = new FakeTransport();
         var host = new CapturingHost();
         var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         actor.PostPromotionBarrier = () => fake.RaiseNodeCommand(fake.Generation!.Value, RebirthCommand());
 
         await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
 
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.HostCommand);
         actor.CurrentSessionSuspect.Should().BeFalse(); // an NCMD-only episode is not suspect
     }
 
     [Fact]
     public async Task Establish_NodeCommandThenDisconnectBeforePublish_DrainsOnce_TransportSuspectWins()
     {
         var fake = new FakeTransport();
         var host = new CapturingHost();
         var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         actor.PostPromotionBarrier = async () =>
         {
             await fake.RaiseNodeCommand(fake.Generation!.Value, RebirthCommand()); // host command...
             await fake.RaiseDisconnected(fake.Generation!.Value);                  // ...then a transport loss
         };
 
         await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
 
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other); // suspect precedence
         actor.CurrentSessionSuspect.Should().BeTrue();
     }
 
     // ==== R2.4: healthy-NBIRTH cancellation cleanup ====
 
     [Fact]
     public async Task Rebirth_HealthyNBirthPreCancelled_DoesNotSend_NotSuspect()
     {
         var (actor, fake, _) = await Born();
         var nbirthsBefore = NBirths(fake).Count;
         using var cts = new CancellationTokenSource();
         await cts.CancelAsync();
 
         await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), cts.Token))
             .Should().ThrowAsync<OperationCanceledException>();
 
         actor.CurrentSessionSuspect.Should().BeFalse();     // never entered the transport
         actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(0)); // prior epoch retained
         NBirths(fake).Count.Should().Be(nbirthsBefore);     // no re-birth NBIRTH sent
     }
 
     [Fact]
     public async Task Rebirth_HealthyNBirthInTransportCancellation_MarksSuspect_RetainsPriorEpoch_NotStuckRebirthing()
     {
         var (actor, fake, _) = await Born();
         using var cts = new CancellationTokenSource();
         fake.OnPublishOnce = () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
 
         await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), cts.Token))
             .Should().ThrowAsync<OperationCanceledException>();
 
         actor.State.Should().Be(AdapterState.Running);       // cancellation is not a coarse fault
         actor.CurrentSessionSuspect.Should().BeTrue();       // uncertain in-transport cancel -> suspect
         actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(0)); // candidate epoch NOT promoted
         actor.ProtocolState.Should().Be(SparkplugProtocolState.Suspect); // not stranded in Rebirthing
     }
 
     // ==== Helpers ====
 
     private async Task<(SparkplugSessionActor Actor, FakeTransport Fake, CapturingHost Host)> Born()
     {
         var fake = new FakeTransport();
         var host = new CapturingHost();
         var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
         return (actor, fake, host);
     }
 
     private (SparkplugSessionActor Actor, FakeTransport Fake1, FakeTransport Fake2, CapturingHost Host) TwoFakeActor()
     {
         var fake1 = new FakeTransport();
         var fake2 = new FakeTransport();
         var call = 0;
         var host = new CapturingHost();
         var actor = new SparkplugSessionActor(
             "spb-1", NewStore(), () => call++ == 0 ? (ISparkplugMqttTransport)fake1 : fake2, () => Clock);
         return (actor, fake1, fake2, host);
     }
 
     private static async Task Begin(SparkplugSessionActor actor, CapturingHost host)
     {
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         await actor.BeginReplaySessionAsync(Start(host), CancellationToken.None);
     }
 
     private SqliteSparkplugIdentityStateStore NewStore() =>
         new(Path.Combine(_dir, "sparkplug", "identity-state.db"));
 
     private static SparkplugSinkConfiguration ValidConfig() => new()
     {
         InstanceId = "spb-1",
         ProtocolName = SparkplugBProtocol.ProtocolName,
         BrokerHost = "localhost",
         GroupId = Group,
         EdgeNodeId = Node,
     };
 
     private static ReplaySessionStart Start(CapturingHost host) =>
         ReplaySessionStart.Create(
             ReplaySessionId.Create(1), ReplayEpochId.Create(0), "route-1",
             ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(0))),
             host);
 
     private static ReplaySessionRebirth Rebirth(long epoch) =>
         ReplaySessionRebirth.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(epoch), StateOf(epoch));
 
     // An empty coherent state (boundary cutoff 0, empty snapshot).
     private static ReplaySessionStartState StateOf(long _) =>
         ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(0)));
 
     private static byte[] RebirthCommand()
     {
         var payload = new Payload();
         payload.Metrics.Add(new Payload.Types.Metric
         {
             Name = SparkplugPayloadEncoder.NodeControlRebirthMetricName,
             BooleanValue = true,
         });
         return payload.ToByteArray();
     }
 
     private static byte[] NonRebirthCommand()
     {
         var payload = new Payload();
         payload.Metrics.Add(new Payload.Types.Metric { Name = "Some/Other", IntValue = 1 });
         return payload.ToByteArray();
     }
 
     private static List<byte[]> NBirths(FakeTransport fake) =>
         fake.Published.Where(p => p.Topic.Contains("NBIRTH")).Select(p => p.Payload).ToList();
 
     private sealed class CapturingHost : IReplaySessionHost
     {
         public List<RebirthRequest> Requests { get; } = new();
         public int ThrowOnRequestCount { get; set; } // first N requests throw before acceptance
 
         public ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken)
         {
             if (ThrowOnRequestCount > 0)
             {
                 ThrowOnRequestCount--;
                 throw new InvalidOperationException("host rebirth request rejected");
             }
 
             Requests.Add(request);
             return ValueTask.CompletedTask;
         }
     }
 
     private sealed class FakeTransport : ISparkplugMqttTransport
     {
         public List<(string Topic, byte[] Payload)> Published { get; } = new();
         public long? Generation { get; private set; }
         public bool IsConnected { get; private set; }
         public bool Connected { get; private set; }
         public bool Disposed { get; private set; }
         public bool PublishReturnsFalse { get; set; }
         public bool FailConnect { get; set; }
         public bool FailSubscribe { get; set; }
 
         public event Func<long, Task>? Disconnected;
         public event Func<long, ReadOnlyMemory<byte>, Task>? NodeCommandReceived;
 
         public Task RaiseDisconnected(long generation) => Disconnected?.Invoke(generation) ?? Task.CompletedTask;
 
         public Task RaiseNodeCommand(long generation, byte[] payload) =>
             NodeCommandReceived?.Invoke(generation, payload) ?? Task.CompletedTask;
 
         public Task ConnectAsync(SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken)
         {
             Generation = connectionGeneration;
             if (FailConnect) { throw new InvalidOperationException("connect failed"); }
             IsConnected = true;
             Connected = true;
             return Task.CompletedTask;
         }
 
         public Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken)
         {
             if (FailSubscribe) { throw new InvalidOperationException("subscribe failed"); }
             return Task.CompletedTask;
         }
 
         public Func<Task>? OnPublishOnce { get; set; } // fires once at the start of the next publish
 
         public async Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
         {
             if (OnPublishOnce is { } hook) { OnPublishOnce = null; await hook(); }
             Published.Add((topic, payload.ToArray()));
             return !PublishReturnsFalse;
         }
 
         public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; return Task.CompletedTask; }
 
         public ValueTask DisposeAsync() { Disposed = true; IsConnected = false; return ValueTask.CompletedTask; }
     }
 }
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
index 868031f..52ae0db 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
@@ -42,741 +42,767 @@ namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;
 public sealed class SparkplugSessionActorReplayTests : IDisposable
 {
     private const string Group = "PlantA";
     private const string Node = "gw-1";
     private static readonly DateTimeOffset Clock = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
 
     private readonly string _dir = Path.Combine(Path.GetTempPath(), "k3-replay-" + Guid.NewGuid().ToString("N"));
 
     public void Dispose()
     {
         SqliteConnection.ClearAllPools();
         try { if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); } }
         catch { /* best effort */ }
     }
 
     // ==== Happy-path DATA: phase → is_historical, seq commit, full accept ====
 
     [Fact]
     public async Task Publish_Replay_IsHistorical_AdvancesSeq_FullAccept()
     {
         var (actor, fake) = await BornActor();
 
         var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
 
         result.Success.Should().BeTrue();
         result.AcceptedCount.Should().Be(1);
         actor.NextSeq.Should().Be(2); // seq 1 consumed by this NDATA
 
         var expected = SparkplugPayloadEncoder.EncodeNData(
             SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
             actor.CurrentManifest!.AliasMap, isHistorical: true);
         NData(fake).Should().Equal(expected); // seq=1, is_historical=true, exact alias/value — all via K2
     }
 
     [Fact]
     public async Task Publish_Live_IsNotHistorical()
     {
         var (actor, fake) = await BornActor();
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None); // enter Live via cutover
         actor.ProtocolState.Should().Be(SparkplugProtocolState.Live);
         fake.Published.Clear();
 
         await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Live, first: 10, last: 10), CancellationToken.None);
 
         var expected = SparkplugPayloadEncoder.EncodeNData(
             SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
             actor.CurrentManifest!.AliasMap, isHistorical: false);
         NData(fake).Should().Equal(expected);
     }
 
     [Fact]
     public async Task Publish_CatchUp_IsHistorical()
     {
         var (actor, fake) = await BornActor();
 
         await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.CatchUp, first: 5, last: 5), CancellationToken.None);
 
         var expected = SparkplugPayloadEncoder.EncodeNData(
             SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
             actor.CurrentManifest!.AliasMap, isHistorical: true);
         NData(fake).Should().Equal(expected);
     }
 
     [Fact]
     public async Task Publish_EmptyBatch_AcceptsZero_ConsumesNoSeq_PublishesNothing()
     {
         var (actor, fake) = await BornActor();
 
         var result = await actor.PublishAsync(Array.Empty<CanonicalDataPoint>(), Ctx(ReplayPhase.Replay), CancellationToken.None);
 
         result.Success.Should().BeTrue();
         result.AcceptedCount.Should().Be(0);
         actor.NextSeq.Should().Be(1);            // no seq consumed
         fake.Published.Should().BeEmpty();
     }
 
     // ==== DATA send failure: suspect + rebirth, zero accept, no seq ====
 
     [Fact]
     public async Task Publish_SendFails_LatchesSuspect_RequestsRebirth_ZeroAccept_NoSeq()
     {
         var (actor, fake, host) = await BornActorWithHost();
         fake.PublishReturnsFalse = true;
 
         var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
 
         result.Success.Should().BeFalse();
         result.AcceptedCount.Should().Be(0);
         result.Error!.Code.Should().Be(SparkplugErrors.PublishRebirthRequested);
         result.Error.Retryable.Should().BeTrue();
         actor.NextSeq.Should().Be(1);            // send failure consumes no seq
         actor.CurrentSessionSuspect.Should().BeTrue();
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
     }
 
     [Fact]
     public async Task Publish_WhenRebirthPendingFromNodeCommand_AcceptsNothing_NoSeq_NoPublish()
     {
         var (actor, fake, host) = await BornActorWithHost();
         // A host NCMD makes a rebirth pending (transport still healthy) — the control latch blocks DATA.
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
         fake.Published.Clear();
 
         var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
 
         result.Success.Should().BeFalse();
         result.AcceptedCount.Should().Be(0);
         actor.NextSeq.Should().Be(1);            // no seq consumed once a rebirth is pending
         fake.Published.Should().BeEmpty();       // no new DATA send starts
         host.Requests.Should().ContainSingle();  // still coalesced to the one pending request
         actor.CurrentSessionSuspect.Should().BeFalse(); // a healthy pending rebirth is NOT a transport failure (r2 R2.1)
         result.Error!.Category.Should().Be(Core.Errors.ErrorCategory.Configuration);
     }
 
     [Fact]
     public async Task Cutover_WhenRebirthPendingFromNodeCommand_NoLive_NoSuspect()
     {
         var (actor, fake, host) = await BornActorWithHost();
         await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // healthy pending
         fake.Published.Clear();
 
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
 
         actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live); // deferred to the rebirth
         actor.CurrentSessionSuspect.Should().BeFalse();                  // ... but transport stays healthy (r2 R2.1)
         fake.Published.Should().BeEmpty();
         host.Requests.Should().ContainSingle();
     }
 
+    [Fact]
+    public async Task Cutover_NodeCommandRacesLiveCommit_NoLive_StaysHealthy()
+    {
+        var (actor, fake, host) = await BornActorWithHost();
+        // A host NCMD lands in the window right before the atomic Live commit (r3 R3.2).
+        actor.PreLiveCommitBarrier = () => fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
+
+        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
+
+        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live); // Live requires an idle episode
+        actor.CurrentSessionSuspect.Should().BeFalse();                  // ... but the transport stays healthy
+        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.HostCommand);
+    }
+
+    [Fact]
+    public async Task Publish_FirstObservedThenNodeCommand_RequestReasonStaysSchemaChange()
+    {
+        var (actor, fake, host) = await BornActorWithHost();
+        // First-observed opens a SchemaChange episode (queued); a coalescing NCMD must NOT overwrite its
+        // accepted cause — first cause wins (r3 R3.3).
+        await actor.PublishAsync(new[] { Point("srcNEW", 5) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
+
+        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
+    }
+
     [Fact]
     public async Task Publish_FirstObservedTwice_StaysHealthyPending_NotSuspect()
     {
         var (actor, _, host) = await BornActorWithHost();
 
         // First-observed makes a healthy SchemaChange rebirth pending; a repeated DATA batch must not
         // upgrade it to transport recovery (r2 R2.1).
         (await actor.PublishAsync(new[] { Point("srcNEW", 5) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
             .Success.Should().BeFalse();
         (await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
             .Success.Should().BeFalse();
 
         actor.CurrentSessionSuspect.Should().BeFalse();
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
     }
 
     private static byte[] RebirthCommand()
     {
         var payload = new Payload();
         payload.Metrics.Add(new Payload.Types.Metric
         {
             Name = SparkplugPayloadEncoder.NodeControlRebirthMetricName,
             BooleanValue = true,
         });
         return payload.ToByteArray();
     }
 
     [Fact]
     public async Task Publish_WhenAlreadySuspect_AcceptsNothing_RequestsRebirth_PublishesNothing()
     {
         var (actor, fake, host) = await BornActorWithHost();
         await fake.RaiseDisconnected(actor.CurrentGeneration); // a post-promotion drop → suspect
         actor.CurrentSessionSuspect.Should().BeTrue();
 
         var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
 
         result.Success.Should().BeFalse();
         result.AcceptedCount.Should().Be(0);
         fake.Published.Should().BeEmpty();       // a suspect authority accepts no DATA (carry-forward #1)
         actor.NextSeq.Should().Be(1);
         host.Requests.Should().ContainSingle();
     }
 
     // ==== First-observed: SchemaChange rebirth, no seq, no publish (healthy transport) ====
 
     [Fact]
     public async Task Publish_FirstObservedMetric_RequestsSchemaChangeRebirth_NoSeq_NoPublish_NotSuspect()
     {
         var (actor, fake, host) = await BornActorWithHost();
 
         var result = await actor.PublishAsync(new[] { Point("srcNEW", 5) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
 
         result.Success.Should().BeFalse();
         result.AcceptedCount.Should().Be(0);
         actor.NextSeq.Should().Be(1);            // no seq on an unknown metric
         fake.Published.Should().BeEmpty();       // nothing published
         actor.CurrentSessionSuspect.Should().BeFalse(); // transport is healthy — not suspect
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
         result.Error!.Category.Should().Be(Core.Errors.ErrorCategory.Configuration); // schema growth, not a network error
     }
 
     // ==== Material mutation: fail closed ====
 
     [Fact]
     public async Task Publish_MaterialMutation_FailsClosed_Faults()
     {
         var (actor, _) = await BornActor();
 
         // srcA was announced as Integer; the same key arriving as a Double is a material schema mutation.
         await actor.Invoking(a => a.PublishAsync(
                 new[] { Point("srcA", 2.5d, CanonicalValueType.Double) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.MaterialSchemaMutation);
 
         actor.State.Should().Be(AdapterState.Failed);
         actor.NextSeq.Should().Be(1); // fail-closed throw consumes no seq
     }
 
     // ==== Session / epoch / no-session gating: fail closed ====
 
     [Fact]
     public async Task Publish_StaleSession_FailsClosed_Faults()
     {
         var (actor, _) = await BornActor();
 
         await actor.Invoking(a => a.PublishAsync(
                 new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay, session: 999), CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.PublishSessionMismatch);
 
         actor.State.Should().Be(AdapterState.Failed);
     }
 
     [Fact]
     public async Task Publish_StaleEpoch_FailsClosed_Faults()
     {
         var (actor, _) = await BornActor();
 
         await actor.Invoking(a => a.PublishAsync(
                 new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay, epoch: 7), CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.PublishEpochMismatch);
 
         actor.State.Should().Be(AdapterState.Failed);
     }
 
     [Fact]
     public async Task Publish_NoActiveSession_FailsClosed()
     {
         // Running but Begin never ran — a context publish is a lifecycle-invariant violation.
         var actor = new SparkplugSessionActor("spb-1", NewStore(), () => new FakeTransport(), () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
 
         await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.PublishNoSession);
     }
 
     // ==== Cancellation / transport-exception boundary (review r1 B3) ====
 
     [Fact]
     public async Task Publish_PreCancelledToken_CleanCancellation_NotSuspect()
     {
         var (actor, fake) = await BornActor();
         using var cts = new CancellationTokenSource();
         await cts.CancelAsync(); // cancelled BEFORE the transport is entered
 
         await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), cts.Token))
             .Should().ThrowAsync<OperationCanceledException>();
 
         actor.State.Should().Be(AdapterState.Running);
         actor.CurrentSessionSuspect.Should().BeFalse(); // never entered the send — the authority stays clean
         fake.Published.Should().BeEmpty();
         actor.NextSeq.Should().Be(1);
     }
 
     [Fact]
     public async Task Publish_CancellationAfterTransportEntry_MarksSuspect_NoSeq_NotFaulted()
     {
         var (actor, fake) = await BornActor();
         using var cts = new CancellationTokenSource();
         fake.FailPublish = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
 
         await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), cts.Token))
             .Should().ThrowAsync<OperationCanceledException>();
 
         actor.State.Should().Be(AdapterState.Running);       // cancellation is not a coarse fault
         actor.CurrentSessionSuspect.Should().BeTrue();       // ... but an in-transport cancel is uncertain → suspect
         actor.NextSeq.Should().Be(1);                        // no seq consumed
     }
 
     [Fact]
     public async Task Publish_TransportThrows_ZeroAccept_Suspect_RequestsRebirth_NoSeq_NotFaulted()
     {
         var (actor, fake, host) = await BornActorWithHost();
         fake.FailPublish = _ => throw new InvalidOperationException("socket boom");
 
         var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
 
         result.Success.Should().BeFalse();
         result.AcceptedCount.Should().Be(0);
         actor.State.Should().Be(AdapterState.Running);       // normalized to a rebirth, NOT a terminal fault
         actor.CurrentSessionSuspect.Should().BeTrue();
         actor.NextSeq.Should().Be(1);
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
     }
 
     // ==== seq wrap (modulo 256) — frozen acceptance matrix ====
 
     [Fact]
     public async Task Publish_SeqWrapsThrough255To0_WithWireEvidence()
     {
         var (actor, fake) = await BornActor();
         var aliasMap = actor.CurrentManifest!.AliasMap;
 
         for (var i = 1; i <= 254; i++) // consume seq 1..254
         {
             (await actor.PublishAsync(new[] { Point("srcA", i) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
                 .Success.Should().BeTrue();
         }
 
         actor.NextSeq.Should().Be(255);
         fake.Published.Clear();
         await actor.PublishAsync(new[] { Point("srcA", 1) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // uses seq 255
         NData(fake).Should().Equal(SparkplugPayloadEncoder.EncodeNData(
             SparkplugSequenceNumber.Create(255), Clock, new[] { Sample("srcA", 1) }, aliasMap, isHistorical: true));
         actor.NextSeq.Should().Be(0); // wrapped
 
         fake.Published.Clear();
         await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // uses seq 0
         NData(fake).Should().Equal(SparkplugPayloadEncoder.EncodeNData(
             SparkplugSequenceNumber.Create(0), Clock, new[] { Sample("srcA", 2) }, aliasMap, isHistorical: true));
         actor.NextSeq.Should().Be(1);
     }
 
     // ==== Exhaustive classification precedence: material mutation wins (review r1 B2) ====
 
     [Theory]
     [InlineData(true)]  // [first-observed, material-mutation]
     [InlineData(false)] // [material-mutation, first-observed]
     public async Task Publish_MixedFirstObservedAndMaterialMutation_MaterialWins(bool firstObservedFirst)
     {
         var (actor, fake, host) = await BornActorWithHost();
         var material = Point("srcA", 2.5d, CanonicalValueType.Double); // srcA announced Integer → material mutation
         var firstObserved = Point("srcNEW", 5);                        // not in manifest → first-observed
         var batch = firstObservedFirst ? new[] { firstObserved, material } : new[] { material, firstObserved };
 
         await actor.Invoking(a => a.PublishAsync(batch, Ctx(ReplayPhase.Replay), CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.MaterialSchemaMutation);
 
         actor.State.Should().Be(AdapterState.Failed);
         fake.Published.Should().BeEmpty();      // no publish regardless of order
         actor.NextSeq.Should().Be(1);           // no seq
         host.Requests.Should().BeEmpty();        // no rebirth escaped before the hard violation
     }
 
     [Theory]
     [InlineData(true)]  // [first-observed, known-invalid]
     [InlineData(false)] // [known-invalid, first-observed]
     public async Task Publish_FirstObservedAndMalformedKnownPoint_FailsClosed_NoRebirth(bool firstObservedFirst)
     {
         var (actor, fake, host) = await BornActorWithHost();
         var firstObserved = Point("srcNEW", 5);                                     // valid, not in manifest
         var malformed = Point("srcA", 2, CanonicalValueType.Integer, NonUtcTimestamp); // announced, but non-UTC timestamp
         var batch = firstObservedFirst ? new[] { firstObserved, malformed } : new[] { malformed, firstObserved };
 
         await actor.Invoking(a => a.PublishAsync(batch, Ctx(ReplayPhase.Replay), CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.EncodeTimestampNotUtc);
 
         actor.State.Should().Be(AdapterState.Failed); // full wire preflight rejects the malformed point BEFORE the rebirth decision
         fake.Published.Should().BeEmpty();
         actor.NextSeq.Should().Be(1);
         host.Requests.Should().BeEmpty();             // no SchemaChange rebirth concealed the malformed DATA
     }
 
     [Fact]
     public async Task Publish_FirstObservedPointItself_WrongClrValue_FailsClosed_NoRebirth()
     {
         var (actor, fake, host) = await BornActorWithHost();
         // srcNEW is first-observed AND carries a string under a declared Integer type — the wire preflight
         // must reject it rather than emit a SchemaChange rebirth for a malformed metric.
         await actor.Invoking(a => a.PublishAsync(
                 new[] { Point("srcNEW", "not-an-int", CanonicalValueType.Integer) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.EncodeValueTypeMismatch);
 
         actor.State.Should().Be(AdapterState.Failed);
         fake.Published.Should().BeEmpty();
         actor.NextSeq.Should().Be(1);
         host.Requests.Should().BeEmpty();
     }
 
     // ==== Catch-up cutover: final-update matrix ====
 
     [Fact]
     public async Task Cutover_DirtyMetricReturnsToBirthValue_StillEmitsFinalUpdate_EntersLive()
     {
         var (actor, fake) = await BornActor();
         // 1 (birth) -> 2 (replay, dirty) -> 1 (cutover): stays dirty, final non-historical 1 emitted.
         await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
         fake.Published.Clear();
 
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
 
         var expected = SparkplugPayloadEncoder.EncodeNData(
             SparkplugSequenceNumber.Create(2), Clock, new[] { Sample("srcA", 1) },
             actor.CurrentManifest!.AliasMap, isHistorical: false);
         NData(fake).Should().Equal(expected); // only the dirty metric, non-historical, seq=2
         actor.ProtocolState.Should().Be(SparkplugProtocolState.Live);
         actor.NextSeq.Should().Be(3);
     }
 
     [Fact]
     public async Task Cutover_NoChangeSinceBirth_EmitsNothing_ConsumesNoSeq_EntersLive()
     {
         var (actor, fake) = await BornActor();
 
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
 
         fake.Published.Should().BeEmpty();       // nothing changed → no final update
         actor.NextSeq.Should().Be(1);            // no seq consumed
         actor.ProtocolState.Should().Be(SparkplugProtocolState.Live);
     }
 
     [Fact]
     public async Task Cutover_MissingAnnouncedMetric_FailsClosed_Faults()
     {
         var (actor, _) = await BornActor();
 
         await actor.Invoking(a => a.CompleteCatchUpAsync(Cutover(("srcA", 1)), CancellationToken.None)) // srcB missing
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.ManifestInvariantViolation);
 
         actor.State.Should().Be(AdapterState.Failed);
     }
 
     [Fact]
     public async Task Cutover_FirstObservedMetric_RequestsSchemaChangeRebirth_DoesNotEnterLive()
     {
         var (actor, fake, host) = await BornActorWithHost();
 
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1), ("srcNEW", 9)), CancellationToken.None);
 
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
         fake.Published.Should().BeEmpty();       // no final update emitted
         actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
     }
 
     // ==== Cutover-suspect composition (the §4.4 special rule) ====
 
     [Fact]
     public async Task Cutover_FinalUpdateSendFails_LatchesSuspect_RequestsRebirth_DoesNotEnterLive()
     {
         var (actor, fake, host) = await BornActorWithHost();
         await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // dirty srcA
         fake.Published.Clear();
         fake.PublishReturnsFalse = true; // the final-update send will fail
 
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 5), ("srcB", 1)), CancellationToken.None);
 
         actor.CurrentSessionSuspect.Should().BeTrue();
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
         actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live); // final update not claimed; Core rebirths first
     }
 
     [Fact]
     public async Task Cutover_WhenAlreadySuspect_RequestsRebirth_DoesNotEnterLive()
     {
         var (actor, fake, host) = await BornActorWithHost();
         await fake.RaiseDisconnected(actor.CurrentGeneration); // suspect before cutover
 
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
 
         host.Requests.Should().ContainSingle();
         fake.Published.Should().BeEmpty();
         actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
     }
 
     [Fact]
     public async Task Cutover_StaleEpoch_FailsClosed_Faults()
     {
         var (actor, _) = await BornActor();
 
         await actor.Invoking(a => a.CompleteCatchUpAsync(
                 ReplaySessionCutover.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(9),
                     ReplaySessionCutoverState.Create(6, SnapshotOf(("srcA", 1), ("srcB", 1)))), CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.PublishEpochMismatch);
 
         actor.State.Should().Be(AdapterState.Failed);
     }
 
     // ==== Cutover static-schema preflight (review r1 B1) ====
 
     [Fact]
     public async Task Cutover_MaterialMutation_FailsClosed_NoPublish_NoSeq_NoRebirth()
     {
         var (actor, fake, host) = await BornActorWithHost();
 
         // srcA announced Integer; the cutover snapshot presents it as Double → material mutation.
         await actor.Invoking(a => a.CompleteCatchUpAsync(
                 CutoverTyped(("srcA", 2.5d, CanonicalValueType.Double), ("srcB", 1, CanonicalValueType.Integer)),
                 CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.MaterialSchemaMutation);
 
         actor.State.Should().Be(AdapterState.Failed);
         fake.Published.Should().BeEmpty();  // no final update
         actor.NextSeq.Should().Be(1);       // no seq
         host.Requests.Should().BeEmpty();   // material mutation wins — no rebirth escapes
     }
 
     [Theory]
     [InlineData(true)]  // material metric enumerated first
     [InlineData(false)] // material metric enumerated last (after the first-observed)
     public async Task Cutover_MixedFirstObservedAndMaterialMutation_MaterialWins(bool materialFirst)
     {
         var (actor, fake, host) = await BornActorWithHost();
         var material = ("srcA", (object)2.5d, CanonicalValueType.Double);     // announced Integer → material
         var srcB = ("srcB", (object)1, CanonicalValueType.Integer);          // unchanged
         var firstObserved = ("srcNEW", (object)9, CanonicalValueType.Integer); // not in manifest
         var metrics = materialFirst
             ? new[] { material, srcB, firstObserved }
             : new[] { firstObserved, srcB, material };
 
         await actor.Invoking(a => a.CompleteCatchUpAsync(CutoverTyped(metrics), CancellationToken.None))
             .Should().ThrowAsync<Core.Errors.AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.MaterialSchemaMutation);
 
         actor.State.Should().Be(AdapterState.Failed);
         host.Requests.Should().BeEmpty();
         fake.Published.Should().BeEmpty();
     }
 
     [Fact]
     public async Task Cutover_FinalUpdateTransportThrows_Suspect_RequestsRebirth_NotLive_NotFaulted()
     {
         var (actor, fake, host) = await BornActorWithHost();
         await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // dirty srcA
         fake.FailPublish = _ => throw new InvalidOperationException("socket boom");
 
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 5), ("srcB", 1)), CancellationToken.None);
 
         actor.State.Should().Be(AdapterState.Running); // not faulted
         actor.CurrentSessionSuspect.Should().BeTrue();
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
         actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
     }
 
     [Fact]
     public async Task Cutover_FinalUpdateCancellationAfterTransportEntry_MarksSuspect_NoSeq_NotLive_NotFaulted()
     {
         var (actor, fake) = await BornActor();
         await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // dirty srcA, seq→2
         using var cts = new CancellationTokenSource();
         fake.FailPublish = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
 
         await actor.Invoking(a => a.CompleteCatchUpAsync(Cutover(("srcA", 5), ("srcB", 1)), cts.Token))
             .Should().ThrowAsync<OperationCanceledException>();
 
         actor.State.Should().Be(AdapterState.Running);  // in-transport cancellation is not a coarse fault
         actor.CurrentSessionSuspect.Should().BeTrue();  // ... but the final-update send is uncertain → suspect
         actor.NextSeq.Should().Be(2);                   // the final-update send consumed no seq
         actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
     }
 
     // ==== Cutover→Live vs. the asynchronous suspect latch (review r1 B4) ====
 
     [Fact]
     public async Task Cutover_NoChange_DisconnectWinsBeforeLiveCommit_Suspect_NotLive()
     {
         var (actor, fake, host) = await BornActorWithHost();
         // A disconnect lands in the window immediately BEFORE the Live compare-exchange.
         actor.PreLiveCommitBarrier = () => fake.RaiseDisconnected(actor.CurrentGeneration);
 
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
 
         actor.CurrentSessionSuspect.Should().BeTrue();
         actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live); // suspect won the race — no Live on a dead authority
         host.Requests.Should().ContainSingle();                          // rebirth requested instead
     }
 
     [Fact]
     public async Task Cutover_SuccessfulFinalUpdate_DisconnectWinsBeforeLiveCommit_Suspect_NotLive()
     {
         var (actor, fake, host) = await BornActorWithHost();
         await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // dirty srcA
         actor.PreLiveCommitBarrier = () => fake.RaiseDisconnected(actor.CurrentGeneration);
 
         await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
 
         actor.CurrentSessionSuspect.Should().BeTrue();
         actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
         host.Requests.Should().ContainSingle();
     }
 
     // ==== Helpers ====
 
     private async Task<(SparkplugSessionActor Actor, FakeTransport Fake)> BornActor()
     {
         var fake = new FakeTransport();
         var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         await actor.BeginReplaySessionAsync(StartPopulated(new CapturingHost()), CancellationToken.None);
         fake.Published.Clear(); // drop the birth NBIRTH — tests assert on slice-5 NDATA only
         return (actor, fake);
     }
 
     private async Task<(SparkplugSessionActor Actor, FakeTransport Fake, CapturingHost Host)> BornActorWithHost()
     {
         var fake = new FakeTransport();
         var host = new CapturingHost();
         var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         await actor.BeginReplaySessionAsync(StartPopulated(host), CancellationToken.None);
         fake.Published.Clear(); // drop the birth NBIRTH — tests assert on slice-5 NDATA only
         return (actor, fake, host);
     }
 
     private SqliteSparkplugIdentityStateStore NewStore() =>
         new(Path.Combine(_dir, "sparkplug", "identity-state.db"));
 
     private static SparkplugSinkConfiguration ValidConfig() => new()
     {
         InstanceId = "spb-1",
         ProtocolName = SparkplugBProtocol.ProtocolName,
         BrokerHost = "localhost",
         GroupId = Group,
         EdgeNodeId = Node,
     };
 
     // Birth with srcA and srcB (both Integer=1), boundary H=5.
     private static ReplaySessionStart StartPopulated(CapturingHost host)
     {
         var snapshot = SnapshotOf(("srcA", 1), ("srcB", 1));
         return ReplaySessionStart.Create(
             ReplaySessionId.Create(1), ReplayEpochId.Create(0), "route-1",
             ReplaySessionStartState.Create(ReplayBoundary.Create(0, 5), snapshot), host);
     }
 
     private static LatestValueSnapshot SnapshotOf(params (string Source, int Value)[] metrics)
     {
         var dict = metrics.ToDictionary(m => Key(m.Source), m => LatestMetricValue.Create(
             Key(m.Source), CanonicalValueType.Integer, m.Value, isNull: false, Clock, DataQuality.Good, routeBufferSequence: 1));
         return new LatestValueSnapshot(RouteSchemaGeneration.Create(0), dict);
     }
 
     private static ReplaySessionCutover Cutover(params (string Source, int Value)[] metrics) =>
         ReplaySessionCutover.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(0),
             ReplaySessionCutoverState.Create(5, SnapshotOf(metrics)));
 
     private static ReplaySessionCutover CutoverTyped(params (string Source, object Value, CanonicalValueType Type)[] metrics)
     {
         var dict = metrics.ToDictionary(m => Key(m.Source), m => LatestMetricValue.Create(
             Key(m.Source), m.Type, m.Value, isNull: false, Clock, DataQuality.Good, routeBufferSequence: 1));
         return ReplaySessionCutover.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(0),
             ReplaySessionCutoverState.Create(5, new LatestValueSnapshot(RouteSchemaGeneration.Create(0), dict)));
     }
 
     private static CanonicalMetricKey Key(string source) => CanonicalMetricKey.Create(source, "dev", "temp");
 
     private static PublishContext Ctx(
         ReplayPhase phase, long session = 1, long epoch = 0, long first = 0, long last = 0) =>
         PublishContext.Create("route-1", ReplaySessionId.Create(session), ReplayEpochId.Create(epoch), phase,
             replayCutoffExclusive: 5, catchUpCutoffExclusive: 10, first, last);
 
     private static CanonicalDataPoint Point(
         string source, object? value, CanonicalValueType type = CanonicalValueType.Integer, DateTime? deviceTimestamp = null) => new()
     {
         GatewayId = "gw",
         SourceInstanceId = source,
         ProtocolName = "test",
         DeviceId = "dev",
         TagName = "temp",
         TagPath = "temp",
         Value = value,
         ValueType = type,
         Quality = DataQuality.Good,
         DeviceTimestamp = deviceTimestamp ?? Clock.UtcDateTime,
         GatewayTimestamp = Clock.UtcDateTime,
     };
 
     // A DeviceTimestamp with Kind=Unspecified — the shared mapper must reject it (ENCODE_TIMESTAMP_NOT_UTC).
     private static readonly DateTime NonUtcTimestamp =
         DateTime.SpecifyKind(new DateTime(2021, 1, 1, 0, 0, 0), DateTimeKind.Unspecified);
 
     private static SparkplugMetricSample Sample(string source, object? value, CanonicalValueType type = CanonicalValueType.Integer) => new()
     {
         Key = SparkplugAliasKey.FromCanonical(Key(source)),
         ValueType = type,
         Value = value,
         IsNull = value is null,
         AcquisitionTimestamp = SparkplugAcquisitionTimestamp.RequireUtc(Clock.UtcDateTime),
         Quality = DataQuality.Good,
     };
 
     private static byte[] NData(FakeTransport fake) =>
         fake.Published.Single(p => p.Topic.Contains("NDATA")).Payload;
 
     private sealed class CapturingHost : IReplaySessionHost
     {
         public List<RebirthRequest> Requests { get; } = new();
 
         public ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken)
         {
             Requests.Add(request);
             return ValueTask.CompletedTask;
         }
     }
 
     private sealed class FakeTransport : ISparkplugMqttTransport
     {
         public List<(string Topic, byte[] Payload)> Published { get; } = new();
         public long? Generation { get; private set; }
         public bool IsConnected { get; private set; }
         public bool PublishReturnsFalse { get; set; }
         public Func<CancellationToken, Task>? FailPublish { get; set; }
 
         public event Func<long, Task>? Disconnected;
         public event Func<long, ReadOnlyMemory<byte>, Task>? NodeCommandReceived;
 
         public Task RaiseDisconnected(long generation) => Disconnected?.Invoke(generation) ?? Task.CompletedTask;
 
         public Task RaiseNodeCommand(long generation, byte[] payload) =>
             NodeCommandReceived?.Invoke(generation, payload) ?? Task.CompletedTask;
 
         public Task ConnectAsync(SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken)
         {
             Generation = connectionGeneration;
             IsConnected = true;
             return Task.CompletedTask;
         }
 
         public Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken) => Task.CompletedTask;
 
         public async Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
         {
             if (FailPublish is not null) { await FailPublish(cancellationToken); }
             Published.Add((topic, payload.ToArray()));
             return !PublishReturnsFalse;
         }
 
         public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; return Task.CompletedTask; }
 
         public ValueTask DisposeAsync() => ValueTask.CompletedTask;
     }
 }
```
