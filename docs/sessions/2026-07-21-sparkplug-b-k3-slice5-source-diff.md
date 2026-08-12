# K3 Slice 5 — Exact Source Diff (attachment)

**Commit `054c18f`** on `feat/sparkplug-b-k3-session-actor` (PR #188). Full unified diff with function context (`git show 054c18f -W`) for the exact files changed in slice 5.

```diff
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
index 03cb392..d223c31 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
@@ -32,6 +32,7 @@ using ElpisEdgeConnect.Core.Routing;
 using ElpisEdgeConnect.Sinks.SparkplugB.Birth;
 using ElpisEdgeConnect.Sinks.SparkplugB.Configuration;
 using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
+using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
 using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
 using ElpisEdgeConnect.Sinks.SparkplugB.Store;
 using ElpisEdgeConnect.Sinks.SparkplugB.Topics;
@@ -41,590 +42,887 @@ namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;
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
 
+    // The Edge-Node modulo-256 message sequence counter. NBIRTH consumes seq 0 at promotion, so the
+    // next NDATA is seq 1; advanced ONLY after a successful local publish (plan v3 §1.3). Reset to 1
+    // atomically with each session promotion (gate-guarded).
+    private int _nextSeq;
+
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
 
+    // The next seq the actor will place on an NDATA (0..255); NBIRTH consumed 0, so this is 1 after Begin.
+    internal int NextSeq => _nextSeq;
+
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
         ISparkplugMqttTransport? attempt = null;
         Func<long, Task>? disconnectHandler = null;
         try
         {
             RequireReadyForSession();
             var config = _config!;
             var node = SparkplugEdgeNodeIdentity.Create(config.GroupId, config.EdgeNodeId);
             var endpoint = config.ResolveBrokerEndpoint();
             var storeIdentity = SparkplugStoreIdentity.Create(endpoint, node);
 
             SetProtocolState(SparkplugProtocolState.LoadingSession);
 
             // --- PREFLIGHT (no durable side effects) ---
             var plan = SparkplugBirthPlanner.Plan(start.State.Snapshot);
             var aliases = _store!.ResolveAliases(storeIdentity, plan.ManifestKeys);
             var resolved = SparkplugBirthPlanner.Resolve(plan, aliases);
             var bdSeqAlias = resolved.AliasMap.Count == 0
                 ? 1UL
                 : checked(resolved.AliasMap.Values.Max() + 1UL);
             var baseline = SparkplugBirthBaseline.FromResolvedPlan(resolved); // built before any send
 
             // --- bdSeq is the LAST durable pre-CONNECT operation (committed before it returns) ---
             var bdSeq = _store.ReserveNextBdSeq(storeIdentity);
 
             var connectRequest = SparkplugMqttConnectRequest.Create(
                 endpoint, ResolveClientId(config), config.Username, config.Password, config.KeepAliveSeconds,
                 cleanSession: true, SparkplugTopicFactory.NDeath(node), SparkplugPayloadEncoder.EncodeNDeath(bdSeq));
 
             // --- Issue a unique generation, PERSISTED BEFORE CONNECT (consumed even on failure) ---
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
 
             var generation = _lastIssuedConnectionGeneration + 1;
             _lastIssuedConnectionGeneration = generation;
 
             attempt = _transportFactory!();
 
             // Atomic establishment→authority handoff (review r2 R2). A disconnect for THIS attempt's
             // generation and the promotion contend for ONE atomic decision (compare-exchange): a drop
             // before promotion invalidates the attempt (Begin faults, promotes nothing); a drop after
             // promotion flags the promoted session suspect for the operational path (slice 6). A
             // concurrent disconnect is NEVER lost, and a dead transport can never be promoted as a
             // clean Replaying authority. The handler stays attached through the handoff — ownership
             // transfers to the promoted ActiveSession, so a post-promotion drop still routes.
             var handoff = new AttemptHandoff(generation);
             disconnectHandler = droppedGeneration =>
             {
                 if (droppedGeneration == generation)
                 {
                     handoff.OnDisconnect();
                 }
 
                 return Task.CompletedTask;
             };
             attempt.Disconnected += disconnectHandler;
 
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
                 throw new AdapterException(new AdapterError
                 {
                     Code = SparkplugErrors.BirthPublishFailed,
                     Category = ErrorCategory.Network,
                     Message = "NBIRTH did not complete at the local transport boundary.",
                     Retryable = false,
                 });
             }
 
             // Deterministic race barrier immediately before the promotion compare-exchange.
             if (PrePromotionBarrier is { } barrier)
             {
                 await barrier().ConfigureAwait(false);
             }
 
             // --- Promote ONE immutable authority via the atomic handoff (only after NBIRTH success) ---
             // Build the candidate (referencing the handoff) BEFORE the CAS so a post-promotion drop
             // that marks it suspect is observable through the promoted reference.
             var candidate = new ActiveSession(
                 attempt, generation, start.SessionId, start.Epoch, start.RouteId, start.Host, bdSeq, resolved, baseline, handoff);
             if (!handoff.TryPromote())
             {
                 throw SessionSuspectDuringBegin(); // a disconnect won the race — install no session
             }
 
             _activeSession = candidate; // volatile publish; handler stays attached (ownership transferred)
+            _nextSeq = 1;               // NBIRTH consumed seq 0; the next NDATA is seq 1
             attempt = null;
             SetProtocolState(SparkplugProtocolState.Replaying);
         }
         catch
         {
             SetFaulted(); // promote nothing; the driver faults the route; the previous epoch stands
             throw;
         }
         finally
         {
             if (attempt is not null)
             {
                 if (disconnectHandler is not null)
                 {
                     attempt.Disconnected -= disconnectHandler;
                 }
 
                 // ABORT: dispose without a clean DISCONNECT so the broker publishes the Will (NDEATH)
                 // for a suspect/uncertain attempt.
                 try { await attempt.DisposeAsync().ConfigureAwait(false); }
                 catch { /* retiring a failed attempt */ }
             }
 
             _gate.Release();
         }
     }
 
     /// <summary>Same-session or transport-suspect rebirth. Implemented in K3 slice 6.</summary>
     /// <param name="rebirth">The rebirth inputs.</param>
     /// <param name="cancellationToken">Cancellation token.</param>
     /// <returns>A task that completes when the re-birth is emitted.</returns>
     public Task RebirthAsync(ReplaySessionRebirth rebirth, CancellationToken cancellationToken)
         => throw new NotImplementedException(NotYetImplemented);
 
-    /// <summary>Publish a phase-tagged batch. Implemented in K3 slice 5.</summary>
-    /// <param name="points">The batch points.</param>
+    /// <summary>
+    /// Publish one phase-tagged (Replay/CatchUp/Live) DATA batch (slice 5). Gates on the active
+    /// session, the context (session, epoch) invariant, and the suspect latch; detects first-observed
+    /// (SchemaChange rebirth, no seq) and material mutation (fail closed); encodes NDATA with the
+    /// current seq WITHOUT advancing; commits seq and marks dirty only after a successful local
+    /// publish. A failed/uncertain send latches suspect, requests a rebirth, and returns
+    /// zero-accepted non-success (plan v3 §4.2, §5.1, §1.3).
+    /// </summary>
+    /// <param name="points">The batch points (all one phase).</param>
     /// <param name="context">The replay context.</param>
     /// <param name="cancellationToken">Cancellation token.</param>
     /// <returns>The publish result.</returns>
-    public Task<PublishResult> PublishAsync(
+    public async Task<PublishResult> PublishAsync(
         IReadOnlyList<CanonicalDataPoint> points, PublishContext context, CancellationToken cancellationToken)
-        => throw new NotImplementedException(NotYetImplemented);
+    {
+        ArgumentNullException.ThrowIfNull(points);
+        ArgumentNullException.ThrowIfNull(context);
+        var started = _clock();
+        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
+        try
+        {
+            try
+            {
+                return await PublishGatedAsync(points, context, started, cancellationToken).ConfigureAwait(false);
+            }
+            catch (OperationCanceledException)
+            {
+                throw; // cancellation is not a fault
+            }
+            catch
+            {
+                SetFaulted(); // a hard fail-closed violation (no session, session/epoch mismatch, material mutation)
+                throw;
+            }
+        }
+        finally
+        {
+            _gate.Release();
+        }
+    }
+
+    private async Task<PublishResult> PublishGatedAsync(
+        IReadOnlyList<CanonicalDataPoint> points, PublishContext context, DateTimeOffset started, CancellationToken cancellationToken)
+    {
+        var session = RequireActiveSession();
+        RequireContextMatches(session, context.SessionId, context.Epoch);
 
-    /// <summary>Complete the catch-up cutover and enter Live. Implemented in K3 slice 5.</summary>
+        // Suspect authority accepts no normal DATA (carry-forward #1): request a rebirth, accept nothing.
+        if (session.Handoff.SuspectAfterPromotion)
+        {
+            return await FailWithRebirthAsync(
+                session, RebirthReason.Other, "the transport is suspect; awaiting rebirth.", latchSuspect: true, started, cancellationToken)
+                .ConfigureAwait(false);
+        }
+
+        if (points.Count == 0)
+        {
+            return PublishResult.Successful(0, _clock() - started); // empty batch: nothing to send, no seq
+        }
+
+        // Classify every point first — a first-observed metric or a material mutation stops the WHOLE
+        // batch (no partial publish, no seq): first-observed requests a SchemaChange rebirth; a material
+        // mutation fails closed.
+        foreach (var point in points)
+        {
+            var classification = SparkplugMaterialSchemaClassifier.Classify(session.Manifest.Schema, point);
+            if (classification == SparkplugMetricClassification.FirstObserved)
+            {
+                return await FailWithRebirthAsync(
+                    session, RebirthReason.SchemaChange, "a first-observed metric requires re-announcement.",
+                    latchSuspect: false, started, cancellationToken).ConfigureAwait(false);
+            }
+
+            SparkplugMaterialSchemaClassifier.ThrowIfMaterialMutation(classification, AliasKeyOf(point).MetricName);
+        }
+
+        var samples = new List<SparkplugMetricSample>(points.Count);
+        foreach (var point in points)
+        {
+            samples.Add(ToSample(point));
+        }
+
+        var isHistorical = context.Phase is ReplayPhase.Replay or ReplayPhase.CatchUp;
+        SetProtocolState(PhaseToProtocolState(context.Phase));
+
+        // Encode with the CURRENT seq without advancing it; a pre-send throw consumes no seq.
+        var payload = SparkplugPayloadEncoder.EncodeNData(
+            SparkplugSequenceNumber.Create(_nextSeq), _clock(), samples, session.Manifest.AliasMap, isHistorical);
+        var published = await session.Transport
+            .PublishAsync(SparkplugTopicFactory.NData(NodeIdentity()), payload, cancellationToken).ConfigureAwait(false);
+        if (!published)
+        {
+            return await FailWithRebirthAsync(
+                session, RebirthReason.Other, "the DATA batch did not complete at the local transport boundary.",
+                latchSuspect: true, started, cancellationToken).ConfigureAwait(false);
+        }
+
+        _nextSeq = (_nextSeq + 1) & 0xFF; // advance ONLY after local success
+        foreach (var point in points)
+        {
+            session.Baseline.Observe(AliasKeyOf(point), SparkplugMetricState.FromDataPoint(point)); // dirtySinceBirth
+        }
+
+        return PublishResult.Successful(points.Count, _clock() - started);
+    }
+
+    /// <summary>
+    /// Complete the catch-up cutover: emit ONE non-historical final update for the metrics that changed
+    /// since birth (dirty ∪ changed-at-cutover, wire-normalized) and enter Live (slice 5). Fails closed
+    /// on a missing announced metric; requests a SchemaChange rebirth (no Live) on a first-observed
+    /// metric; and, on a suspect transport or a failed final-update send, latches suspect, awaits a
+    /// current-session rebirth request, and returns WITHOUT entering Live (plan v3 §1.5, §4.4).
+    /// </summary>
     /// <param name="cutover">The cutover inputs.</param>
     /// <param name="cancellationToken">Cancellation token.</param>
-    /// <returns>A task that completes when Live has been entered.</returns>
-    public Task CompleteCatchUpAsync(ReplaySessionCutover cutover, CancellationToken cancellationToken)
-        => throw new NotImplementedException(NotYetImplemented);
+    /// <returns>A task that completes when Live has been entered (or a rebirth was requested).</returns>
+    public async Task CompleteCatchUpAsync(ReplaySessionCutover cutover, CancellationToken cancellationToken)
+    {
+        ArgumentNullException.ThrowIfNull(cutover);
+        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
+        try
+        {
+            try
+            {
+                await CompleteCatchUpGatedAsync(cutover, cancellationToken).ConfigureAwait(false);
+            }
+            catch (OperationCanceledException)
+            {
+                throw;
+            }
+            catch
+            {
+                SetFaulted();
+                throw;
+            }
+        }
+        finally
+        {
+            _gate.Release();
+        }
+    }
+
+    private async Task CompleteCatchUpGatedAsync(ReplaySessionCutover cutover, CancellationToken cancellationToken)
+    {
+        var session = RequireActiveSession();
+        RequireContextMatches(session, cutover.SessionId, cutover.Epoch);
+
+        if (session.Handoff.SuspectAfterPromotion)
+        {
+            // §4.4: no final update, no Live — latch suspect and let Core rebirth before any Live DATA.
+            await RequestRebirthAsync(
+                session, RebirthReason.Other, "the transport is suspect at cutover; awaiting rebirth.",
+                latchSuspect: true, cancellationToken).ConfigureAwait(false);
+            return;
+        }
+
+        // Map the cutover snapshot to wire-exact states (for the comparison) and keep the source
+        // latest-values (for building the final-update samples).
+        var snapshot = cutover.State.Snapshot;
+        var latestByKey = new Dictionary<SparkplugAliasKey, LatestMetricValue>();
+        foreach (var canonicalKey in snapshot.Metrics)
+        {
+            if (snapshot.TryGet(canonicalKey) is { } latest)
+            {
+                latestByKey[SparkplugAliasKey.FromCanonical(canonicalKey)] = latest;
+            }
+        }
+
+        var cutoverStates = latestByKey.ToDictionary(kv => kv.Key, kv => SparkplugMetricState.FromLatestValue(kv.Value));
+        var comparison = session.Baseline.Compare(cutoverStates);
+
+        if (!comparison.MissingAnnouncedKeys.IsEmpty)
+        {
+            throw Typed(SparkplugErrors.ManifestInvariantViolation,
+                $"announced metric(s) [{string.Join(", ", comparison.MissingAnnouncedKeys.Select(k => k.MetricName))}] " +
+                "are absent from the cutover snapshot.");
+        }
+
+        if (!comparison.FirstObservedKeys.IsEmpty)
+        {
+            // Same-generation growth surfaced at cutover — rebirth, do NOT enter Live.
+            await RequestRebirthAsync(
+                session, RebirthReason.SchemaChange, "first-observed metric(s) at cutover require re-announcement.",
+                latchSuspect: false, cancellationToken).ConfigureAwait(false);
+            return;
+        }
+
+        if (comparison.FinalUpdates.Count > 0)
+        {
+            var samples = comparison.FinalUpdates.Keys
+                .Select(key => ToSample(key, latestByKey[key]))
+                .ToList();
+            var payload = SparkplugPayloadEncoder.EncodeNData(
+                SparkplugSequenceNumber.Create(_nextSeq), _clock(), samples, session.Manifest.AliasMap, isHistorical: false);
+            var published = await session.Transport
+                .PublishAsync(SparkplugTopicFactory.NData(NodeIdentity()), payload, cancellationToken).ConfigureAwait(false);
+            if (!published)
+            {
+                await RequestRebirthAsync(
+                    session, RebirthReason.Other, "the final update did not complete at the local transport boundary.",
+                    latchSuspect: true, cancellationToken).ConfigureAwait(false);
+                return; // §4.4: do not enter Live
+            }
+
+            _nextSeq = (_nextSeq + 1) & 0xFF;
+        }
+
+        SetProtocolState(SparkplugProtocolState.Live);
+    }
 
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
 
+    // ----- Slice-5 replay/DATA helpers (all run under the gate) -----
+
+    private ActiveSession RequireActiveSession() =>
+        _activeSession ?? throw Typed(SparkplugErrors.PublishNoSession,
+            "a context publish/cutover arrived with no active session (Core called the replay path before a successful Begin).");
+
+    private static void RequireContextMatches(ActiveSession session, ReplaySessionId sessionId, ReplayEpochId epoch)
+    {
+        if (sessionId.Value != session.SessionId.Value)
+        {
+            throw Typed(SparkplugErrors.PublishSessionMismatch,
+                $"the context session ({sessionId.Value}) is not the actor's authoritative session ({session.SessionId.Value}).");
+        }
+
+        if (epoch.Value != session.Epoch.Value)
+        {
+            throw Typed(SparkplugErrors.PublishEpochMismatch,
+                $"the context epoch ({epoch.Value}) is not the actor's current birth epoch ({session.Epoch.Value}).");
+        }
+    }
+
+    // Latch suspect (transport reasons only), then await ACCEPTANCE of a current-session/current-epoch
+    // rebirth request before returning — the reverse handshake that lets Core rebirth before retrying.
+    private async Task RequestRebirthAsync(
+        ActiveSession session, RebirthReason reason, string detail, bool latchSuspect, CancellationToken cancellationToken)
+    {
+        if (latchSuspect)
+        {
+            session.Handoff.MarkSuspect();
+            SetProtocolState(SparkplugProtocolState.Suspect);
+        }
+
+        var request = RebirthRequest.Create(session.SessionId, session.Epoch, reason, detail);
+        await session.Host.RequestRebirthAsync(request, cancellationToken).ConfigureAwait(false);
+    }
+
+    private async Task<PublishResult> FailWithRebirthAsync(
+        ActiveSession session, RebirthReason reason, string detail, bool latchSuspect, DateTimeOffset started, CancellationToken cancellationToken)
+    {
+        await RequestRebirthAsync(session, reason, detail, latchSuspect, cancellationToken).ConfigureAwait(false);
+        return PublishResult.Failed(
+            new AdapterError
+            {
+                Code = SparkplugErrors.PublishRebirthRequested,
+                Category = ErrorCategory.Network,
+                Message = detail,
+                Retryable = true, // Core rebirths, then retries the same unacknowledged subrange under the newer epoch
+            },
+            _clock() - started);
+    }
+
+    private static SparkplugProtocolState PhaseToProtocolState(ReplayPhase phase) => phase switch
+    {
+        ReplayPhase.Replay => SparkplugProtocolState.Replaying,
+        ReplayPhase.CatchUp => SparkplugProtocolState.CatchingUp,
+        ReplayPhase.Live => SparkplugProtocolState.Live,
+        _ => SparkplugProtocolState.Replaying,
+    };
+
+    private SparkplugEdgeNodeIdentity NodeIdentity() =>
+        SparkplugEdgeNodeIdentity.Create(_config!.GroupId, _config.EdgeNodeId);
+
+    private static SparkplugAliasKey AliasKeyOf(CanonicalDataPoint point) =>
+        SparkplugAliasKey.FromCanonical(CanonicalMetricKey.Create(point.SourceInstanceId, point.DeviceId, point.TagPath));
+
+    private static SparkplugMetricSample ToSample(CanonicalDataPoint point) => new()
+    {
+        Key = AliasKeyOf(point),
+        ValueType = point.ValueType,
+        Value = point.Value,
+        IsNull = point.Value is null,
+        AcquisitionTimestamp = SparkplugAcquisitionTimestamp.RequireUtc(point.DeviceTimestamp),
+        Quality = point.Quality,
+    };
+
+    private static SparkplugMetricSample ToSample(SparkplugAliasKey key, LatestMetricValue value) => new()
+    {
+        Key = key,
+        ValueType = value.ValueType,
+        Value = value.Value,
+        IsNull = value.IsNull,
+        AcquisitionTimestamp = value.TimestampUtc,
+        Quality = value.Quality,
+    };
+
+    private static AdapterException Typed(string code, string message) =>
+        new(new AdapterError { Code = code, Category = ErrorCategory.Internal, Message = message, Retryable = false });
+
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
     /// The atomic establishment→authority handoff for one CONNECT attempt (review r2 R2). A
     /// disconnect for this attempt's generation and the promotion decide via one compare-exchange,
     /// so a drop concurrent with promotion is never lost: it either invalidates a pre-promotion
     /// establishment or flags the promoted session suspect.
     /// </summary>
     private sealed class AttemptHandoff
     {
         private const int Establishing = 0;
         private const int Invalidated = 1;
         private const int Promoted = 2;
 
         private int _state = Establishing;
         private volatile bool _suspectAfterPromotion;
 
         public AttemptHandoff(long generation) => Generation = generation;
 
         /// <summary>This attempt's connection generation.</summary>
         public long Generation { get; }
 
         /// <summary>True once a disconnect invalidated an in-progress (pre-promotion) establishment.</summary>
         public bool IsInvalidated => Volatile.Read(ref _state) == Invalidated;
 
         /// <summary>True once a post-promotion disconnect flagged the promoted session suspect.</summary>
         public bool SuspectAfterPromotion => _suspectAfterPromotion;
 
         /// <summary>
         /// Record a disconnect for this attempt's generation. Atomically invalidates an in-progress
         /// establishment (before promotion) OR marks the already-promoted session suspect — never both,
         /// never lost.
         /// </summary>
         public void OnDisconnect()
         {
             var prev = Interlocked.CompareExchange(ref _state, Invalidated, Establishing);
             if (prev == Promoted)
             {
                 _suspectAfterPromotion = true;
             }
         }
 
         /// <summary>Claim promotion. Returns false if a disconnect already invalidated the attempt.</summary>
         public bool TryPromote() =>
             Interlocked.CompareExchange(ref _state, Promoted, Establishing) == Establishing;
+
+        /// <summary>
+        /// Flag the already-promoted session suspect after an observable/uncertain DATA send failure
+        /// (slice 5). Only meaningful post-promotion; the operational path (slice 6) consumes it.
+        /// </summary>
+        public void MarkSuspect() => _suspectAfterPromotion = true;
     }
 }
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/SparkplugErrors.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/SparkplugErrors.cs
index 96e1917..39d7c6a 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/SparkplugErrors.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/SparkplugErrors.cs
@@ -15,184 +15,220 @@ namespace ElpisEdgeConnect.Sinks.SparkplugB;
 /// <summary>
 /// Stable catalog of Sparkplug B sink error codes. String constants rather
 /// than an enum, matching <c>CoreErrors</c>. Adding a code is allowed;
 /// removing or renaming an existing code is a breaking change.
 /// </summary>
 public static class SparkplugErrors
 {
     // ==== Encoding / value mapping (validated-model stage) ====
 
     /// <summary>
     /// The canonical value type has no scalar Sparkplug equivalent
     /// (<c>Array</c>, <c>Object</c>), declares no real type (<c>Null</c>), or
     /// is undefined. Never silently dropped (ADR-0035 Rule 5).
     /// </summary>
     public const string EncodeUnmappableDatatype = "SPARKPLUG.ENCODE_UNMAPPABLE_DATATYPE";
 
     /// <summary>The runtime CLR type of a value does not match its declared canonical value type.</summary>
     public const string EncodeValueTypeMismatch = "SPARKPLUG.ENCODE_VALUE_TYPE_MISMATCH";
 
     /// <summary>The null invariant is violated: IsNull=true with a value, or IsNull=false without one.</summary>
     public const string EncodeNullInvariant = "SPARKPLUG.ENCODE_NULL_INVARIANT";
 
     /// <summary>
     /// A timestamp precedes the Unix epoch (1970-01-01T00:00:00Z). Sparkplug
     /// timestamps are unsigned 64-bit milliseconds; a pre-epoch instant is
     /// rejected rather than cast into a fabricated future time (plan v2 §3.3).
     /// </summary>
     public const string EncodeTimestampPreEpoch = "SPARKPLUG.ENCODE_TIMESTAMP_PRE_EPOCH";
 
     /// <summary>A timestamp exceeds the representable unsigned 64-bit millisecond range (rejected, never wrapped).</summary>
     public const string EncodeTimestampOverflow = "SPARKPLUG.ENCODE_TIMESTAMP_OVERFLOW";
 
     /// <summary>
     /// A canonical acquisition <see cref="System.DateTime"/> is not <see cref="System.DateTimeKind.Utc"/>.
     /// Fail loud rather than silently reinterpret a Local/Unspecified instant with the machine's
     /// timezone (which would make encoding non-deterministic across gateways) — matching Core's
     /// tracked-route policy (slice-3 review r2).
     /// </summary>
     public const string EncodeTimestampNotUtc = "SPARKPLUG.ENCODE_TIMESTAMP_NOT_UTC";
 
     /// <summary>The canonical data quality is not a defined <c>DataQuality</c> value.</summary>
     public const string EncodeQualityUndefined = "SPARKPLUG.ENCODE_QUALITY_UNDEFINED";
 
     // ==== Identity / namespace ====
 
     /// <summary>A Sparkplug identity element (group id, edge node id, alias-key component) is empty or contains a forbidden character.</summary>
     public const string IdentityInvalid = "SPARKPLUG.IDENTITY_INVALID";
 
     // ==== Alias table (payload-factory validation, plan v2 §3.1) ====
 
     /// <summary>Alias 0 is reserved by this profile; assigned aliases begin at 1.</summary>
     public const string AliasZeroReserved = "SPARKPLUG.ALIAS_ZERO_RESERVED";
 
     /// <summary>Two metrics share one alias value — aliases must be unique across the Edge Node.</summary>
     public const string AliasDuplicate = "SPARKPLUG.ALIAS_DUPLICATE";
 
     /// <summary>A metric has no alias in the supplied map (every ordinary NBIRTH/NDATA metric requires one).</summary>
     public const string AliasMissing = "SPARKPLUG.ALIAS_MISSING";
 
     /// <summary>
     /// The NBIRTH application metrics and the application alias map are not an
     /// exact set match. Both directions are violations: a birth metric without
     /// an alias, and an alias never announced by a birth metric (a receiver
     /// can only resolve an NDATA alias established in the active NBIRTH).
     /// Surplus aliases are never silently ignored.
     /// </summary>
     public const string AliasTableMismatch = "SPARKPLUG.ALIAS_TABLE_MISMATCH";
 
     // ==== Payload construction ====
 
     /// <summary>An NBIRTH announced the same metric more than once (birth carries one current value per metric).</summary>
     public const string PayloadDuplicateBirthMetric = "SPARKPLUG.PAYLOAD_DUPLICATE_BIRTH_METRIC";
 
     /// <summary>An NDATA payload was requested with no samples.</summary>
     public const string PayloadEmpty = "SPARKPLUG.PAYLOAD_EMPTY";
 
     // ==== Session establishment (K3 slice 4) ====
 
     /// <summary>
     /// The actor was asked to begin a session but was not wired with an identity store /
     /// transport (K4 composition), or was not in a startable state. Fail closed.
     /// </summary>
     public const string SessionNotReady = "SPARKPLUG.SESSION_NOT_READY";
 
     /// <summary>
     /// An NBIRTH publish did not complete at the local transport boundary. Fatal for the
     /// initial Begin (plan v3 §4.5): promotes nothing, faults the route.
     /// </summary>
     public const string BirthPublishFailed = "SPARKPLUG.BIRTH_PUBLISH_FAILED";
 
     /// <summary>A Begin was requested while a session is already active (single-session actor).</summary>
     public const string SessionAlreadyActive = "SPARKPLUG.SESSION_ALREADY_ACTIVE";
 
     /// <summary>The connection-generation counter would overflow (fail closed before creating a client).</summary>
     public const string GenerationOverflow = "SPARKPLUG.GENERATION_OVERFLOW";
 
     /// <summary>The transport dropped (or CONNECT failed) during initial Begin, before an authoritative birth.</summary>
     public const string SessionSuspectDuringBegin = "SPARKPLUG.SESSION_SUSPECT_DURING_BEGIN";
 
     // ==== Transport (K3 slice 4) ====
 
     /// <summary>CONNECT did not return a success CONNACK.</summary>
     public const string TransportConnectFailed = "SPARKPLUG.TRANSPORT_CONNECT_FAILED";
 
     /// <summary>The exact NCMD SUBSCRIBE was rejected or granted a QoS below the requested QoS 1.</summary>
     public const string TransportSubscribeFailed = "SPARKPLUG.TRANSPORT_SUBSCRIBE_FAILED";
 
     // ==== Configuration validation (K3 slice 1) ====
 
     /// <summary>The supplied configuration is not a <c>SparkplugSinkConfiguration</c>.</summary>
     public const string ConfigWrongType = "SPARKPLUG.CONFIG_WRONG_TYPE";
 
     /// <summary>
     /// A connection JSON field is present but has the wrong type or an
     /// out-of-range/non-integral numeric value (e.g. <c>"brokerPort": "8883"</c>).
     /// Never silently coerced to the default (slice-1 review B3).
     /// </summary>
     public const string ConfigInvalidFieldType = "SPARKPLUG.CONFIG_INVALID_FIELD_TYPE";
 
     /// <summary>The required broker host is missing or blank.</summary>
     public const string ConfigMissingBrokerHost = "SPARKPLUG.CONFIG_MISSING_BROKER_HOST";
 
     /// <summary>The broker port is outside the valid 1..65535 range.</summary>
     public const string ConfigInvalidBrokerPort = "SPARKPLUG.CONFIG_INVALID_BROKER_PORT";
 
     /// <summary>The Sparkplug group id is empty or contains a topic-reserved character ('/', '+', '#').</summary>
     public const string ConfigInvalidGroupId = "SPARKPLUG.CONFIG_INVALID_GROUP_ID";
 
     /// <summary>The Sparkplug edge node id is empty or contains a topic-reserved character ('/', '+', '#').</summary>
     public const string ConfigInvalidEdgeNodeId = "SPARKPLUG.CONFIG_INVALID_EDGE_NODE_ID";
 
     /// <summary>The keep-alive interval is outside the MQTT 3.1.1 range (1..65535 seconds).</summary>
     public const string ConfigInvalidKeepAlive = "SPARKPLUG.CONFIG_INVALID_KEEPALIVE";
 
     /// <summary>A present client id is blank/whitespace (omit it to auto-generate, or supply a real id).</summary>
     public const string ConfigInvalidClientId = "SPARKPLUG.CONFIG_INVALID_CLIENT_ID";
 
     /// <summary>
     /// The bounded transport-recovery budget is invalid: a non-positive attempt
     /// count or initial delay, or a max delay below the initial delay (plan v3 §4.7).
     /// </summary>
     public const string ConfigInvalidRecoveryBudget = "SPARKPLUG.CONFIG_INVALID_RECOVERY_BUDGET";
 
     /// <summary>Broker auth is incomplete: username without password, or vice versa.</summary>
     public const string ConfigAuthIncomplete = "SPARKPLUG.CONFIG_AUTH_INCOMPLETE";
 
     // ==== Gateway identity store (K3 slice 2) ====
 
     /// <summary>
     /// The identity-state store is unreadable, corrupt, or a durable write/commit
     /// failed. Fail-closed: the caller must NOT proceed to CONNECT, and the store
     /// is NEVER silently reset to a fresh zero counter (K0 WS5).
     /// </summary>
     public const string IdentityStoreUnavailable = "SPARKPLUG.IDENTITY_STORE_UNAVAILABLE";
 
     /// <summary>The persisted identity-store schema version is not one this build understands (fail closed).</summary>
     public const string IdentityStoreSchemaUnsupported = "SPARKPLUG.IDENTITY_STORE_SCHEMA_UNSUPPORTED";
 
     /// <summary>
     /// A batch alias allocation violated a uniqueness invariant (duplicate canonical
     /// key or alias for the Edge Node) — the whole batch is rolled back (all-or-none).
     /// </summary>
     public const string AliasAllocationConflict = "SPARKPLUG.ALIAS_ALLOCATION_CONFLICT";
 
     // ==== Birth plan / schema (K3 slice 3) ====
 
     /// <summary>
     /// An application metric's source-qualified published name collides with a reserved
     /// well-known name (<c>bdSeq</c> or <c>Node Control/Rebirth</c>). Defensive: the
     /// source-qualified 3-part join structurally cannot equal either, but the birth plan
     /// validates it rather than trusting the invariant.
     /// </summary>
     public const string BirthReservedMetricName = "SPARKPLUG.BIRTH_RESERVED_METRIC_NAME";
 
     /// <summary>Two birth metrics resolve to the same published name (duplicate manifest entry).</summary>
     public const string BirthDuplicateMetricName = "SPARKPLUG.BIRTH_DUPLICATE_METRIC_NAME";
 
     /// <summary>
     /// An already-announced metric's static schema (datatype/unit/name/identity) changed —
     /// generation-changing material mutation, deferred post-K3. K3 fails closed rather than
     /// silently re-announcing or emitting a mismatched NDATA (plan v3 §5.2).
     /// </summary>
     public const string MaterialSchemaMutation = "SPARKPLUG.MATERIAL_SCHEMA_MUTATION";
+
+    // ==== Replay / DATA lifecycle (K3 slice 5) ====
+
+    /// <summary>
+    /// A context publish (<c>PublishAsync</c>/<c>CompleteCatchUpAsync</c>) arrived with no active
+    /// session (Core called the replay path before a successful Begin). A lifecycle-invariant
+    /// violation — fail closed, never a retryable publish failure (plan v3 §7).
+    /// </summary>
+    public const string PublishNoSession = "SPARKPLUG.PUBLISH_NO_SESSION";
+
+    /// <summary>
+    /// A context publish carried a <see cref="ElpisEdgeConnect.Core.Adapters.PublishContext.SessionId"/>
+    /// that is not the actor's authoritative session — a lifecycle-invariant violation (plan v3 §7).
+    /// </summary>
+    public const string PublishSessionMismatch = "SPARKPLUG.PUBLISH_SESSION_MISMATCH";
+
+    /// <summary>
+    /// A context publish carried an epoch that is not the actor's current successful-birth epoch —
+    /// a lifecycle-invariant violation, not a retryable publish failure (plan v3 §7, §1.9).
+    /// </summary>
+    public const string PublishEpochMismatch = "SPARKPLUG.PUBLISH_EPOCH_MISMATCH";
+
+    /// <summary>
+    /// At catch-up cutover an announced metric was absent from the cutover snapshot — a
+    /// manifest-invariant violation the actor fails closed on rather than silently dropping
+    /// (plan v3 §1.5, §5.3).
+    /// </summary>
+    public const string ManifestInvariantViolation = "SPARKPLUG.MANIFEST_INVARIANT_VIOLATION";
+
+    /// <summary>
+    /// A DATA batch was not accepted because the actor requested a Core rebirth first (transport
+    /// suspect after a failed/uncertain send, or a first-observed metric needing re-announcement).
+    /// Retryable: Core processes the rebirth, then retries the same unacknowledged subrange under
+    /// the newer epoch (plan v3 §4.2, §5.1).
+    /// </summary>
+    public const string PublishRebirthRequested = "SPARKPLUG.PUBLISH_REBIRTH_REQUESTED";
 }
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
new file mode 100644
index 0000000..9bf80fa
--- /dev/null
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
@@ -0,0 +1,472 @@
+// ============================================================================
+// File: Session/SparkplugSessionActorReplayTests.cs
+// Purpose: Locks K3 slice-5 Replay/CatchUp/Live DATA + catch-up cutover against a
+//          deterministic fake transport (no broker). Covers: session/epoch/phase
+//          gating (fail-closed on mismatch); the is_historical flag per phase
+//          (Replay/CatchUp = true, Live + final update = false, byte-parity vs an
+//          independently-built K2 NDATA); the seq commit point (advanced ONLY after a
+//          successful local publish — never on empty batch, first-observed, send
+//          failure, suspect, or a fail-closed throw); first-observed → SchemaChange
+//          rebirth-before-retry (no seq, no publish); material mutation fail-closed;
+//          the catch-up final-update matrix (dirty ∪ changed, the 1→2→1 case, missing-
+//          announced fail-closed, first-observed rebirth) and the cutover-suspect
+//          composition (final-update send failure latches suspect, requests rebirth,
+//          does NOT enter Live).
+// ============================================================================
+
+using System;
+using System.Collections.Generic;
+using System.IO;
+using System.Linq;
+using System.Threading;
+using System.Threading.Tasks;
+using ElpisEdgeConnect.Core.Adapters;
+using ElpisEdgeConnect.Core.Buffer;
+using ElpisEdgeConnect.Core.Model;
+using ElpisEdgeConnect.Core.Routing;
+using ElpisEdgeConnect.Sinks.SparkplugB;
+using ElpisEdgeConnect.Sinks.SparkplugB.Configuration;
+using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
+using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
+using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
+using ElpisEdgeConnect.Sinks.SparkplugB.Session;
+using ElpisEdgeConnect.Sinks.SparkplugB.Store;
+using FluentAssertions;
+using Microsoft.Data.Sqlite;
+using Xunit;
+
+namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;
+
+public sealed class SparkplugSessionActorReplayTests : IDisposable
+{
+    private const string Group = "PlantA";
+    private const string Node = "gw-1";
+    private static readonly DateTimeOffset Clock = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
+
+    private readonly string _dir = Path.Combine(Path.GetTempPath(), "k3-replay-" + Guid.NewGuid().ToString("N"));
+
+    public void Dispose()
+    {
+        SqliteConnection.ClearAllPools();
+        try { if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); } }
+        catch { /* best effort */ }
+    }
+
+    // ==== Happy-path DATA: phase → is_historical, seq commit, full accept ====
+
+    [Fact]
+    public async Task Publish_Replay_IsHistorical_AdvancesSeq_FullAccept()
+    {
+        var (actor, fake) = await BornActor();
+
+        var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
+
+        result.Success.Should().BeTrue();
+        result.AcceptedCount.Should().Be(1);
+        actor.NextSeq.Should().Be(2); // seq 1 consumed by this NDATA
+
+        var expected = SparkplugPayloadEncoder.EncodeNData(
+            SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
+            actor.CurrentManifest!.AliasMap, isHistorical: true);
+        NData(fake).Should().Equal(expected); // seq=1, is_historical=true, exact alias/value — all via K2
+    }
+
+    [Fact]
+    public async Task Publish_Live_IsNotHistorical()
+    {
+        var (actor, fake) = await BornActor();
+
+        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Live, first: 10, last: 10), CancellationToken.None);
+
+        var expected = SparkplugPayloadEncoder.EncodeNData(
+            SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
+            actor.CurrentManifest!.AliasMap, isHistorical: false);
+        NData(fake).Should().Equal(expected);
+        actor.ProtocolState.Should().Be(SparkplugProtocolState.Live);
+    }
+
+    [Fact]
+    public async Task Publish_CatchUp_IsHistorical()
+    {
+        var (actor, fake) = await BornActor();
+
+        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.CatchUp, first: 5, last: 5), CancellationToken.None);
+
+        var expected = SparkplugPayloadEncoder.EncodeNData(
+            SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
+            actor.CurrentManifest!.AliasMap, isHistorical: true);
+        NData(fake).Should().Equal(expected);
+    }
+
+    [Fact]
+    public async Task Publish_EmptyBatch_AcceptsZero_ConsumesNoSeq_PublishesNothing()
+    {
+        var (actor, fake) = await BornActor();
+
+        var result = await actor.PublishAsync(Array.Empty<CanonicalDataPoint>(), Ctx(ReplayPhase.Replay), CancellationToken.None);
+
+        result.Success.Should().BeTrue();
+        result.AcceptedCount.Should().Be(0);
+        actor.NextSeq.Should().Be(1);            // no seq consumed
+        fake.Published.Should().BeEmpty();
+    }
+
+    // ==== DATA send failure: suspect + rebirth, zero accept, no seq ====
+
+    [Fact]
+    public async Task Publish_SendFails_LatchesSuspect_RequestsRebirth_ZeroAccept_NoSeq()
+    {
+        var (actor, fake, host) = await BornActorWithHost();
+        fake.PublishReturnsFalse = true;
+
+        var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
+
+        result.Success.Should().BeFalse();
+        result.AcceptedCount.Should().Be(0);
+        result.Error!.Code.Should().Be(SparkplugErrors.PublishRebirthRequested);
+        result.Error.Retryable.Should().BeTrue();
+        actor.NextSeq.Should().Be(1);            // send failure consumes no seq
+        actor.CurrentSessionSuspect.Should().BeTrue();
+        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
+    }
+
+    [Fact]
+    public async Task Publish_WhenAlreadySuspect_AcceptsNothing_RequestsRebirth_PublishesNothing()
+    {
+        var (actor, fake, host) = await BornActorWithHost();
+        await fake.RaiseDisconnected(actor.CurrentGeneration); // a post-promotion drop → suspect
+        actor.CurrentSessionSuspect.Should().BeTrue();
+
+        var result = await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
+
+        result.Success.Should().BeFalse();
+        result.AcceptedCount.Should().Be(0);
+        fake.Published.Should().BeEmpty();       // a suspect authority accepts no DATA (carry-forward #1)
+        actor.NextSeq.Should().Be(1);
+        host.Requests.Should().ContainSingle();
+    }
+
+    // ==== First-observed: SchemaChange rebirth, no seq, no publish (healthy transport) ====
+
+    [Fact]
+    public async Task Publish_FirstObservedMetric_RequestsSchemaChangeRebirth_NoSeq_NoPublish_NotSuspect()
+    {
+        var (actor, fake, host) = await BornActorWithHost();
+
+        var result = await actor.PublishAsync(new[] { Point("srcNEW", 5) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
+
+        result.Success.Should().BeFalse();
+        result.AcceptedCount.Should().Be(0);
+        actor.NextSeq.Should().Be(1);            // no seq on an unknown metric
+        fake.Published.Should().BeEmpty();       // nothing published
+        actor.CurrentSessionSuspect.Should().BeFalse(); // transport is healthy — not suspect
+        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
+    }
+
+    // ==== Material mutation: fail closed ====
+
+    [Fact]
+    public async Task Publish_MaterialMutation_FailsClosed_Faults()
+    {
+        var (actor, _) = await BornActor();
+
+        // srcA was announced as Integer; the same key arriving as a Double is a material schema mutation.
+        await actor.Invoking(a => a.PublishAsync(
+                new[] { Point("srcA", 2.5d, CanonicalValueType.Double) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
+            .Should().ThrowAsync<Core.Errors.AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.MaterialSchemaMutation);
+
+        actor.State.Should().Be(AdapterState.Failed);
+        actor.NextSeq.Should().Be(1); // fail-closed throw consumes no seq
+    }
+
+    // ==== Session / epoch / no-session gating: fail closed ====
+
+    [Fact]
+    public async Task Publish_StaleSession_FailsClosed_Faults()
+    {
+        var (actor, _) = await BornActor();
+
+        await actor.Invoking(a => a.PublishAsync(
+                new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay, session: 999), CancellationToken.None))
+            .Should().ThrowAsync<Core.Errors.AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.PublishSessionMismatch);
+
+        actor.State.Should().Be(AdapterState.Failed);
+    }
+
+    [Fact]
+    public async Task Publish_StaleEpoch_FailsClosed_Faults()
+    {
+        var (actor, _) = await BornActor();
+
+        await actor.Invoking(a => a.PublishAsync(
+                new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay, epoch: 7), CancellationToken.None))
+            .Should().ThrowAsync<Core.Errors.AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.PublishEpochMismatch);
+
+        actor.State.Should().Be(AdapterState.Failed);
+    }
+
+    [Fact]
+    public async Task Publish_NoActiveSession_FailsClosed()
+    {
+        // Running but Begin never ran — a context publish is a lifecycle-invariant violation.
+        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => new FakeTransport(), () => Clock);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+
+        await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None))
+            .Should().ThrowAsync<Core.Errors.AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.PublishNoSession);
+    }
+
+    [Fact]
+    public async Task Publish_Cancellation_Throws_DoesNotFault()
+    {
+        var (actor, fake) = await BornActor();
+        using var cts = new CancellationTokenSource();
+        fake.FailPublish = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
+
+        await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), cts.Token))
+            .Should().ThrowAsync<OperationCanceledException>();
+
+        actor.State.Should().Be(AdapterState.Running); // cancellation is not a fault
+    }
+
+    // ==== Catch-up cutover: final-update matrix ====
+
+    [Fact]
+    public async Task Cutover_DirtyMetricReturnsToBirthValue_StillEmitsFinalUpdate_EntersLive()
+    {
+        var (actor, fake) = await BornActor();
+        // 1 (birth) -> 2 (replay, dirty) -> 1 (cutover): stays dirty, final non-historical 1 emitted.
+        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None);
+        fake.Published.Clear();
+
+        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
+
+        var expected = SparkplugPayloadEncoder.EncodeNData(
+            SparkplugSequenceNumber.Create(2), Clock, new[] { Sample("srcA", 1) },
+            actor.CurrentManifest!.AliasMap, isHistorical: false);
+        NData(fake).Should().Equal(expected); // only the dirty metric, non-historical, seq=2
+        actor.ProtocolState.Should().Be(SparkplugProtocolState.Live);
+        actor.NextSeq.Should().Be(3);
+    }
+
+    [Fact]
+    public async Task Cutover_NoChangeSinceBirth_EmitsNothing_ConsumesNoSeq_EntersLive()
+    {
+        var (actor, fake) = await BornActor();
+
+        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
+
+        fake.Published.Should().BeEmpty();       // nothing changed → no final update
+        actor.NextSeq.Should().Be(1);            // no seq consumed
+        actor.ProtocolState.Should().Be(SparkplugProtocolState.Live);
+    }
+
+    [Fact]
+    public async Task Cutover_MissingAnnouncedMetric_FailsClosed_Faults()
+    {
+        var (actor, _) = await BornActor();
+
+        await actor.Invoking(a => a.CompleteCatchUpAsync(Cutover(("srcA", 1)), CancellationToken.None)) // srcB missing
+            .Should().ThrowAsync<Core.Errors.AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.ManifestInvariantViolation);
+
+        actor.State.Should().Be(AdapterState.Failed);
+    }
+
+    [Fact]
+    public async Task Cutover_FirstObservedMetric_RequestsSchemaChangeRebirth_DoesNotEnterLive()
+    {
+        var (actor, fake, host) = await BornActorWithHost();
+
+        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1), ("srcNEW", 9)), CancellationToken.None);
+
+        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.SchemaChange);
+        fake.Published.Should().BeEmpty();       // no final update emitted
+        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
+    }
+
+    // ==== Cutover-suspect composition (the §4.4 special rule) ====
+
+    [Fact]
+    public async Task Cutover_FinalUpdateSendFails_LatchesSuspect_RequestsRebirth_DoesNotEnterLive()
+    {
+        var (actor, fake, host) = await BornActorWithHost();
+        await actor.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), CancellationToken.None); // dirty srcA
+        fake.Published.Clear();
+        fake.PublishReturnsFalse = true; // the final-update send will fail
+
+        await actor.CompleteCatchUpAsync(Cutover(("srcA", 5), ("srcB", 1)), CancellationToken.None);
+
+        actor.CurrentSessionSuspect.Should().BeTrue();
+        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
+        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live); // final update not claimed; Core rebirths first
+    }
+
+    [Fact]
+    public async Task Cutover_WhenAlreadySuspect_RequestsRebirth_DoesNotEnterLive()
+    {
+        var (actor, fake, host) = await BornActorWithHost();
+        await fake.RaiseDisconnected(actor.CurrentGeneration); // suspect before cutover
+
+        await actor.CompleteCatchUpAsync(Cutover(("srcA", 1), ("srcB", 1)), CancellationToken.None);
+
+        host.Requests.Should().ContainSingle();
+        fake.Published.Should().BeEmpty();
+        actor.ProtocolState.Should().NotBe(SparkplugProtocolState.Live);
+    }
+
+    [Fact]
+    public async Task Cutover_StaleEpoch_FailsClosed_Faults()
+    {
+        var (actor, _) = await BornActor();
+
+        await actor.Invoking(a => a.CompleteCatchUpAsync(
+                ReplaySessionCutover.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(9),
+                    ReplaySessionCutoverState.Create(6, SnapshotOf(("srcA", 1), ("srcB", 1)))), CancellationToken.None))
+            .Should().ThrowAsync<Core.Errors.AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.PublishEpochMismatch);
+
+        actor.State.Should().Be(AdapterState.Failed);
+    }
+
+    // ==== Helpers ====
+
+    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake)> BornActor()
+    {
+        var fake = new FakeTransport();
+        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+        await actor.BeginReplaySessionAsync(StartPopulated(new CapturingHost()), CancellationToken.None);
+        fake.Published.Clear(); // drop the birth NBIRTH — tests assert on slice-5 NDATA only
+        return (actor, fake);
+    }
+
+    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake, CapturingHost Host)> BornActorWithHost()
+    {
+        var fake = new FakeTransport();
+        var host = new CapturingHost();
+        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+        await actor.BeginReplaySessionAsync(StartPopulated(host), CancellationToken.None);
+        fake.Published.Clear(); // drop the birth NBIRTH — tests assert on slice-5 NDATA only
+        return (actor, fake, host);
+    }
+
+    private SqliteSparkplugIdentityStateStore NewStore() =>
+        new(Path.Combine(_dir, "sparkplug", "identity-state.db"));
+
+    private static SparkplugSinkConfiguration ValidConfig() => new()
+    {
+        InstanceId = "spb-1",
+        ProtocolName = SparkplugBProtocol.ProtocolName,
+        BrokerHost = "localhost",
+        GroupId = Group,
+        EdgeNodeId = Node,
+    };
+
+    // Birth with srcA and srcB (both Integer=1), boundary H=5.
+    private static ReplaySessionStart StartPopulated(CapturingHost host)
+    {
+        var snapshot = SnapshotOf(("srcA", 1), ("srcB", 1));
+        return ReplaySessionStart.Create(
+            ReplaySessionId.Create(1), ReplayEpochId.Create(0), "route-1",
+            ReplaySessionStartState.Create(ReplayBoundary.Create(0, 5), snapshot), host);
+    }
+
+    private static LatestValueSnapshot SnapshotOf(params (string Source, int Value)[] metrics)
+    {
+        var dict = metrics.ToDictionary(m => Key(m.Source), m => LatestMetricValue.Create(
+            Key(m.Source), CanonicalValueType.Integer, m.Value, isNull: false, Clock, DataQuality.Good, routeBufferSequence: 1));
+        return new LatestValueSnapshot(RouteSchemaGeneration.Create(0), dict);
+    }
+
+    private static ReplaySessionCutover Cutover(params (string Source, int Value)[] metrics) =>
+        ReplaySessionCutover.Create(ReplaySessionId.Create(1), ReplayEpochId.Create(0),
+            ReplaySessionCutoverState.Create(5, SnapshotOf(metrics)));
+
+    private static CanonicalMetricKey Key(string source) => CanonicalMetricKey.Create(source, "dev", "temp");
+
+    private static PublishContext Ctx(
+        ReplayPhase phase, long session = 1, long epoch = 0, long first = 0, long last = 0) =>
+        PublishContext.Create("route-1", ReplaySessionId.Create(session), ReplayEpochId.Create(epoch), phase,
+            replayCutoffExclusive: 5, catchUpCutoffExclusive: 10, first, last);
+
+    private static CanonicalDataPoint Point(string source, object? value, CanonicalValueType type = CanonicalValueType.Integer) => new()
+    {
+        GatewayId = "gw",
+        SourceInstanceId = source,
+        ProtocolName = "test",
+        DeviceId = "dev",
+        TagName = "temp",
+        TagPath = "temp",
+        Value = value,
+        ValueType = type,
+        Quality = DataQuality.Good,
+        DeviceTimestamp = Clock.UtcDateTime,
+        GatewayTimestamp = Clock.UtcDateTime,
+    };
+
+    private static SparkplugMetricSample Sample(string source, object? value, CanonicalValueType type = CanonicalValueType.Integer) => new()
+    {
+        Key = SparkplugAliasKey.FromCanonical(Key(source)),
+        ValueType = type,
+        Value = value,
+        IsNull = value is null,
+        AcquisitionTimestamp = SparkplugAcquisitionTimestamp.RequireUtc(Clock.UtcDateTime),
+        Quality = DataQuality.Good,
+    };
+
+    private static byte[] NData(FakeTransport fake) =>
+        fake.Published.Single(p => p.Topic.Contains("NDATA")).Payload;
+
+    private sealed class CapturingHost : IReplaySessionHost
+    {
+        public List<RebirthRequest> Requests { get; } = new();
+
+        public ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken)
+        {
+            Requests.Add(request);
+            return ValueTask.CompletedTask;
+        }
+    }
+
+    private sealed class FakeTransport : ISparkplugMqttTransport
+    {
+        public List<(string Topic, byte[] Payload)> Published { get; } = new();
+        public long? Generation { get; private set; }
+        public bool IsConnected { get; private set; }
+        public bool PublishReturnsFalse { get; set; }
+        public Func<CancellationToken, Task>? FailPublish { get; set; }
+
+        public event Func<long, Task>? Disconnected;
+
+        public Task RaiseDisconnected(long generation) => Disconnected?.Invoke(generation) ?? Task.CompletedTask;
+
+        public Task ConnectAsync(SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken)
+        {
+            Generation = connectionGeneration;
+            IsConnected = true;
+            return Task.CompletedTask;
+        }
+
+        public Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken) => Task.CompletedTask;
+
+        public async Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
+        {
+            if (FailPublish is not null) { await FailPublish(cancellationToken); }
+            Published.Add((topic, payload.ToArray()));
+            return !PublishReturnsFalse;
+        }
+
+        public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; return Task.CompletedTask; }
+
+        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
+    }
+}
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/SparkplugSinkAdapterTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/SparkplugSinkAdapterTests.cs
index c81805f..965da28 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/SparkplugSinkAdapterTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/SparkplugSinkAdapterTests.cs
@@ -26,471 +26,467 @@ namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests;
 public sealed class SparkplugSinkAdapterTests
 {
     private const string SecretSentinel = "SUPERSECRET-do-not-log-123";
 
     // ==== Identity & advertised capabilities ====
 
     [Fact]
     public void ProtocolName_IsSparkplugB()
     {
         NewAdapter().ProtocolName.Should().Be("sparkplug-b");
     }
 
     [Fact]
     public void Capabilities_IsPushOnly()
     {
         NewAdapter().Capabilities.Should().Be(SinkCapabilities.Push);
     }
 
     [Fact]
     public void AdvertisedDeliveryCapabilities_IsStoreAndForwardLocalTransport()
     {
         SparkplugSinkAdapter.AdvertisedDeliveryCapabilities.SupportsStoreAndForward.Should().BeTrue();
         SparkplugSinkAdapter.AdvertisedDeliveryCapabilities.AcknowledgementBoundary
             .Should().Be(DeliveryAcknowledgementBoundary.LocalTransport);
         SparkplugSinkAdapter.AdvertisedDeliveryCapabilities.SupportsBrokerAcknowledgedAtLeastOnce
             .Should().BeFalse();
     }
 
     [Fact]
     public void State_StartsCreated()
     {
         NewAdapter().State.Should().Be(AdapterState.Created);
     }
 
     // ==== Initialize + semantic validation (review B1) ====
 
     [Fact]
     public async Task Initialize_ValidConfig_TransitionsToInitialized()
     {
         var adapter = NewAdapter();
 
         await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
 
         adapter.State.Should().Be(AdapterState.Initialized);
     }
 
     [Fact]
     public async Task Initialize_WrongConfigType_ThrowsTypedAndFaults()
     {
         var adapter = NewAdapter();
 
         var act = async () => await adapter.InitializeAsync(
             new OtherSinkConfiguration { InstanceId = "x", ProtocolName = "other" }, CancellationToken.None);
 
         (await act.Should().ThrowAsync<AdapterException>())
             .Which.Error.Code.Should().Be(SparkplugErrors.ConfigWrongType);
         adapter.State.Should().Be(AdapterState.Failed);
         adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
     }
 
     [Theory]
     [InlineData("blankHost", SparkplugErrors.ConfigMissingBrokerHost)]
     [InlineData("badPort", SparkplugErrors.ConfigInvalidBrokerPort)]
     [InlineData("badGroup", SparkplugErrors.ConfigInvalidGroupId)]
     [InlineData("badNode", SparkplugErrors.ConfigInvalidEdgeNodeId)]
     [InlineData("badKeepAlive", SparkplugErrors.ConfigInvalidKeepAlive)]
     [InlineData("badBudget", SparkplugErrors.ConfigInvalidRecoveryBudget)]
     [InlineData("incompleteAuth", SparkplugErrors.ConfigAuthIncomplete)]
     public async Task Initialize_SemanticallyInvalidConfig_ThrowsTypedAndFaults(string mutation, string expectedCode)
     {
         var cfg = mutation switch
         {
             "blankHost" => ValidConfig() with { BrokerHost = "" },
             "badPort" => ValidConfig() with { BrokerPort = 0 },
             "badGroup" => ValidConfig() with { GroupId = "a/b" },
             "badNode" => ValidConfig() with { EdgeNodeId = "a+b" },
             "badKeepAlive" => ValidConfig() with { KeepAliveSeconds = 0 },
             "badBudget" => ValidConfig() with { TransportRecoveryMaxAttempts = 0 },
             "incompleteAuth" => ValidConfig() with { Username = "u", Password = null },
             _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
         };
         var adapter = NewAdapter();
 
         var act = async () => await adapter.InitializeAsync(cfg, CancellationToken.None);
 
         (await act.Should().ThrowAsync<AdapterException>()).Which.Error.Code.Should().Be(expectedCode);
         adapter.State.Should().Be(AdapterState.Failed);
         adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
     }
 
     [Fact]
     public async Task Initialize_InvalidConfigWithSecret_ExceptionDoesNotLeakPassword()
     {
         var cfg = ValidConfig() with { Username = "u", Password = SecretSentinel, BrokerPort = 0 };
         var adapter = NewAdapter();
 
         var act = async () => await adapter.InitializeAsync(cfg, CancellationToken.None);
 
         (await act.Should().ThrowAsync<AdapterException>()).Which.Message.Should().NotContain(SecretSentinel);
     }
 
     [Fact]
     public async Task Initialize_CanceledToken_ThrowsAndLeavesStateCreated()
     {
         var adapter = NewAdapter();
         using var cts = new CancellationTokenSource();
         await cts.CancelAsync();
 
         var act = async () => await adapter.InitializeAsync(ValidConfig(), cts.Token);
 
         await act.Should().ThrowAsync<OperationCanceledException>();
         adapter.State.Should().Be(AdapterState.Created);
     }
 
     // ==== Start / Stop lifecycle ====
 
     [Fact]
     public async Task Start_AfterInitialize_TransitionsToRunning()
     {
         var adapter = await InitializedAdapter();
 
         await adapter.StartAsync(CancellationToken.None);
 
         adapter.State.Should().Be(AdapterState.Running);
     }
 
     [Fact]
     public async Task Start_FromCreated_ThrowsAndFaults()
     {
         var adapter = NewAdapter();
 
         var act = async () => await adapter.StartAsync(CancellationToken.None);
 
         await act.Should().ThrowAsync<InvalidOperationException>();
         adapter.State.Should().Be(AdapterState.Failed);
         adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
     }
 
     [Fact]
     public async Task Start_FromRunning_ThrowsAndFaults()
     {
         var adapter = await RunningAdapter();
 
         var act = async () => await adapter.StartAsync(CancellationToken.None);
 
         await act.Should().ThrowAsync<InvalidOperationException>();
         adapter.State.Should().Be(AdapterState.Failed);
         adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
     }
 
     [Fact]
     public async Task Start_FromFailed_ThrowsAndStaysFaulted()
     {
         var adapter = await FailedAdapter();
 
         var act = async () => await adapter.StartAsync(CancellationToken.None);
 
         await act.Should().ThrowAsync<InvalidOperationException>();
         adapter.State.Should().Be(AdapterState.Failed);
         adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
     }
 
     [Fact]
     public async Task Start_CanceledDuringStarting_FaultsAndGateRemainsUsable()
     {
         // Contract (locked): cancellation BEFORE Start mutates state → remain Initialized
         // (covered elsewhere); cancellation AFTER Start enters Starting → Failed/Faulted.
         var actor = new SparkplugSessionActor("spb-1");
         var probeEntered = new TaskCompletionSource();
         actor.GateHeldProbe = async ct =>
         {
             probeEntered.SetResult();
             await Task.Delay(Timeout.Infinite, ct); // observe the operation token
         };
         var adapter = new SparkplugSinkAdapter("spb-1", actor);
         await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
 
         using var cts = new CancellationTokenSource();
         var startTask = adapter.StartAsync(cts.Token); // enters Starting, blocks in the probe
         await probeEntered.Task;
         await cts.CancelAsync();
 
         var act = async () => await startTask;
         await act.Should().ThrowAsync<OperationCanceledException>();
         adapter.State.Should().Be(AdapterState.Failed);
         adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
 
         // The gate is released on the way out, so cleanup still works.
         await adapter.StopAsync(CancellationToken.None);
         adapter.State.Should().Be(AdapterState.Stopped);
         adapter.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
     }
 
     [Fact]
     public async Task Start_FromStopped_RestartsToRunning()
     {
         var adapter = await RunningAdapter();
         await adapter.StopAsync(CancellationToken.None);
         adapter.State.Should().Be(AdapterState.Stopped);
 
         await adapter.StartAsync(CancellationToken.None);
 
         adapter.State.Should().Be(AdapterState.Running);
     }
 
     [Fact]
     public async Task StartThenStop_EndsStopped()
     {
         var adapter = await RunningAdapter();
 
         await adapter.StopAsync(CancellationToken.None);
 
         adapter.State.Should().Be(AdapterState.Stopped);
     }
 
     [Fact]
     public async Task Stop_FromInitialized_ReachesStopped()
     {
         var adapter = await InitializedAdapter();
 
         await adapter.StopAsync(CancellationToken.None);
 
         adapter.State.Should().Be(AdapterState.Stopped);
     }
 
     [Fact]
     public async Task Stop_FromFaulted_ResetsBothToStopped()
     {
         var adapter = await FailedAdapter();
         adapter.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
 
         await adapter.StopAsync(CancellationToken.None);
 
         adapter.State.Should().Be(AdapterState.Stopped);
         adapter.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
     }
 
     [Fact]
     public async Task Stop_FromCreated_IsNoOp()
     {
         var adapter = NewAdapter();
 
         await adapter.StopAsync(CancellationToken.None);
 
         adapter.State.Should().Be(AdapterState.Created);
     }
 
     [Fact]
     public async Task Stop_WhenAlreadyStopped_IsIdempotent()
     {
         var adapter = await RunningAdapter();
         await adapter.StopAsync(CancellationToken.None);
 
         await adapter.StopAsync(CancellationToken.None);
 
         adapter.State.Should().Be(AdapterState.Stopped);
     }
 
     // ==== Health ====
 
     [Fact]
     public async Task CheckHealth_WhenRunning_IsHealthyWithNoSession()
     {
         var adapter = await RunningAdapter();
 
         var health = await adapter.CheckHealthAsync(CancellationToken.None);
 
         health.State.Should().Be(AdapterState.Running);
         health.Level.Should().Be(HealthLevel.Healthy);
         health.Metrics.Should().ContainKey("hasSession").WhoseValue.Should().Be(false);
     }
 
     [Fact]
     public async Task CheckHealth_CanceledToken_ReturnsCanceled()
     {
         var adapter = await RunningAdapter();
         using var cts = new CancellationTokenSource();
         await cts.CancelAsync();
 
         var act = async () => await adapter.CheckHealthAsync(cts.Token);
 
         await act.Should().ThrowAsync<OperationCanceledException>();
     }
 
     [Fact]
     public async Task CheckHealth_AfterFault_ReportsUnhealthyAndFaulted()
     {
         var adapter = await FailedAdapter();
 
         var health = await adapter.CheckHealthAsync(CancellationToken.None);
 
         health.State.Should().Be(AdapterState.Failed);
         health.Level.Should().Be(HealthLevel.Unhealthy);
         health.Metrics.Should().ContainKey("protocolState")
             .WhoseValue.Should().Be(SparkplugProtocolState.Faulted.ToString());
     }
 
     // ==== Fail-closed surfaces ====
 
     [Fact]
     public void BasePublish_FailsClosed()
     {
         var adapter = NewAdapter();
 
         Action act = () => _ = adapter.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
 
         act.Should().Throw<NotSupportedException>();
     }
 
     [Fact]
     public void UpdateCurrentValues_FailsClosed()
     {
         var adapter = NewAdapter();
 
         Action act = () => _ = adapter.UpdateCurrentValuesAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
 
         act.Should().Throw<NotSupportedException>();
     }
 
     // ==== Config validation delegation ====
 
     [Fact]
     public async Task ValidateConfig_Valid_IsValid()
     {
         (await NewAdapter().ValidateConfigAsync(ValidConfig(), CancellationToken.None)).IsValid.Should().BeTrue();
     }
 
     [Fact]
     public async Task ValidateConfig_WrongType_FailsWithConfigWrongType()
     {
         var result = await NewAdapter().ValidateConfigAsync(
             new OtherSinkConfiguration { InstanceId = "x", ProtocolName = "other" }, CancellationToken.None);
 
         result.IsValid.Should().BeFalse();
         result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigWrongType);
     }
 
     [Fact]
     public async Task ValidateConfig_CanceledToken_ReturnsCanceled()
     {
         using var cts = new CancellationTokenSource();
         await cts.CancelAsync();
 
         var act = async () => await NewAdapter().ValidateConfigAsync(ValidConfig(), cts.Token);
 
         await act.Should().ThrowAsync<OperationCanceledException>();
     }
 
     // ==== Deferred replay lifecycle (slices 4-6): throws, leaves state untouched ====
 
     [Theory]
     [InlineData("Rebirth")]
-    [InlineData("PublishContext")]
-    [InlineData("CompleteCatchUp")]
     [InlineData("EndSession")]
     public void DeferredReplayMethod_ThrowsAndLeavesStateUnchanged(string method)
     {
+        // Rebirth + EndSession land in slice 6; PublishContext + CompleteCatchUp are implemented (slice 5).
         var adapter = NewAdapter();
         var state = adapter.State;
         var protocol = adapter.ProtocolState;
 
         Action act = method switch
         {
             "Rebirth" => () => _ = adapter.RebirthAsync(null!, CancellationToken.None),
-            "PublishContext" => () => _ = adapter.PublishAsync(
-                Array.Empty<CanonicalDataPoint>(), null!, CancellationToken.None),
-            "CompleteCatchUp" => () => _ = adapter.CompleteCatchUpAsync(null!, CancellationToken.None),
             "EndSession" => () => _ = adapter.EndSessionAsync(null!, CancellationToken.None),
             _ => throw new ArgumentOutOfRangeException(nameof(method)),
         };
 
         act.Should().Throw<NotImplementedException>();
         adapter.State.Should().Be(state);
         adapter.ProtocolState.Should().Be(protocol);
     }
 
     [Fact]
     public async Task DeferredReplayCall_DoesNotBlockSubsequentLifecycle()
     {
         var adapter = NewAdapter();
         try { _ = adapter.RebirthAsync(null!, CancellationToken.None); }
         catch (NotImplementedException) { /* expected */ }
 
         await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
         await adapter.StartAsync(CancellationToken.None);
 
         adapter.State.Should().Be(AdapterState.Running);
     }
 
     // ==== Single-gate serialization (deterministic, no Thread.Sleep) ====
 
     [Fact]
     public async Task Gate_SerializesConcurrentLifecycleCalls()
     {
         var actor = new SparkplugSessionActor("spb-1");
         var probeEntered = new TaskCompletionSource();
         var releaseProbe = new TaskCompletionSource();
         actor.GateHeldProbe = async _ =>
         {
             probeEntered.SetResult();
             await releaseProbe.Task;
         };
         var adapter = new SparkplugSinkAdapter("spb-1", actor);
         await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
 
         var startTask = adapter.StartAsync(CancellationToken.None); // enters gate, awaits the probe
         await probeEntered.Task;                                    // Start is now holding the gate
 
         var stopTask = adapter.StopAsync(CancellationToken.None);   // must block on the gate
         stopTask.IsCompleted.Should().BeFalse();                    // serialized behind Start
 
         releaseProbe.SetResult();                                   // let Start complete + release
         await startTask;
         await stopTask;
 
         adapter.State.Should().Be(AdapterState.Stopped);
     }
 
     [Fact]
     public async Task DisposeAsync_IsIdempotent()
     {
         var adapter = NewAdapter();
 
         await adapter.DisposeAsync();
         var act = async () => await adapter.DisposeAsync();
 
         await act.Should().NotThrowAsync();
     }
 
     // ==== Helpers ====
 
     private static SparkplugSinkAdapter NewAdapter() => new("spb-1");
 
     private static async Task<SparkplugSinkAdapter> InitializedAdapter()
     {
         var adapter = NewAdapter();
         await adapter.InitializeAsync(ValidConfig(), CancellationToken.None);
         return adapter;
     }
 
     private static async Task<SparkplugSinkAdapter> RunningAdapter()
     {
         var adapter = await InitializedAdapter();
         await adapter.StartAsync(CancellationToken.None);
         return adapter;
     }
 
     private static async Task<SparkplugSinkAdapter> FailedAdapter()
     {
         var adapter = NewAdapter();
         try
         {
             await adapter.InitializeAsync(
                 new OtherSinkConfiguration { InstanceId = "x", ProtocolName = "other" }, CancellationToken.None);
         }
         catch (AdapterException) { /* expected — leaves the actor Failed */ }
         adapter.State.Should().Be(AdapterState.Failed);
         return adapter;
     }
 
     private static SparkplugSinkConfiguration ValidConfig() => new()
     {
         InstanceId = "spb-1",
         ProtocolName = SparkplugBProtocol.ProtocolName,
         BrokerHost = "localhost",
         GroupId = "PlantA",
         EdgeNodeId = "gw-1",
     };
 
     private sealed record OtherSinkConfiguration : SinkConfiguration;
 }
```
