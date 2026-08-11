# K3 Slice 4 r2 — Exact Source Diff (attachment)

**Commit `4a3cc1d`** on `feat/sparkplug-b-k3-session-actor` (PR #188). Full unified diff with function context (`git show 4a3cc1d -W`) for the exact four files changed in r2. `SparkplugErrors.cs` did **not** change in r2 (its error-code additions landed in r1 `c551d52`).

```diff
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugMqttTransport.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugMqttTransport.cs
index 8bc450f..8a32d97 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugMqttTransport.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugMqttTransport.cs
@@ -32,243 +32,274 @@ namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;
 /// <summary>MQTTnet-backed <see cref="ISparkplugMqttTransport"/>; no automatic reconnect.</summary>
 internal sealed class SparkplugMqttTransport : ISparkplugMqttTransport
 {
     private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
 
     private readonly Func<IMqttClient> _clientFactory;
     private IMqttClient? _client;
     private volatile bool _suppressDisconnectedEvent;
     private bool _disposed;
 
     /// <summary>Construct with the default MQTTnet client factory.</summary>
     public SparkplugMqttTransport()
         : this(() => new MqttFactory().CreateMqttClient())
     {
     }
 
     /// <summary>Construct with an injected client factory (tests).</summary>
     /// <param name="clientFactory">Creates a fresh <see cref="IMqttClient"/> per CONNECT.</param>
     internal SparkplugMqttTransport(Func<IMqttClient> clientFactory)
     {
         ArgumentNullException.ThrowIfNull(clientFactory);
         _clientFactory = clientFactory;
     }
 
     /// <inheritdoc/>
     public event Func<long, Task>? Disconnected;
 
     /// <inheritdoc/>
     public bool IsConnected => _client?.IsConnected ?? false;
 
     /// <inheritdoc/>
     public async Task ConnectAsync(
         SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken)
     {
         ArgumentNullException.ThrowIfNull(request);
         ObjectDisposedException.ThrowIf(_disposed, this);
 
         await RetireClientAsync().ConfigureAwait(false); // abort any prior client before a new session
         _suppressDisconnectedEvent = false;
 
         var client = _clientFactory();
         _client = client;
 
         // Capture THIS attempt's generation so a retired client's delayed callback carries its
         // own generation. A callback fired for an actor-requested teardown is suppressed.
         client.DisconnectedAsync += _ =>
             _suppressDisconnectedEvent ? Task.CompletedTask : RaiseDisconnectedAsync(connectionGeneration);
 
-        var result = await client.ConnectAsync(BuildConnectOptions(request), cancellationToken).ConfigureAwait(false);
+        MqttClientConnectResult result;
+        try
+        {
+            result = await client.ConnectAsync(BuildConnectOptions(request), cancellationToken).ConfigureAwait(false);
+        }
+        catch (OperationCanceledException)
+        {
+            throw; // cancellation stays cancellation — never normalized to a transport failure
+        }
+        catch (Exception ex)
+        {
+            // Normalize a framework/MQTTnet CONNECT throw into a stable, secret-free typed error
+            // (type name only — never the exception message, which could echo endpoint/credentials).
+            throw TransportFailure(
+                SparkplugErrors.TransportConnectFailed, $"CONNECT failed ({ex.GetType().Name}).");
+        }
+
         RequireConnectSuccess(result.ResultCode == MqttClientConnectResultCode.Success, result.ResultCode.ToString());
     }
 
     /// <inheritdoc/>
     public async Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken)
     {
         ArgumentException.ThrowIfNullOrEmpty(topicFilter);
         var client = RequireClient();
-        var result = await client.SubscribeAsync(BuildSubscribeOptions(topicFilter), cancellationToken).ConfigureAwait(false);
+        MqttClientSubscribeResult result;
+        try
+        {
+            result = await client.SubscribeAsync(BuildSubscribeOptions(topicFilter), cancellationToken).ConfigureAwait(false);
+        }
+        catch (OperationCanceledException)
+        {
+            throw; // cancellation stays cancellation
+        }
+        catch (Exception ex)
+        {
+            throw TransportFailure(
+                SparkplugErrors.TransportSubscribeFailed, $"SUBSCRIBE failed ({ex.GetType().Name}).");
+        }
+
         RequireExactNcmdGrant(
             result.Items.Select(i => new KeyValuePair<string, int>(i.TopicFilter.Topic, MapGrantedQos(i.ResultCode))).ToList(),
             topicFilter);
     }
 
     /// <inheritdoc/>
     public async Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
     {
         ArgumentException.ThrowIfNullOrEmpty(topic);
         var client = RequireClient();
         try
         {
             var result = await client.PublishAsync(BuildPublishMessage(topic, payload), cancellationToken).ConfigureAwait(false);
             return result.IsSuccess; // local transport boundary — never broker receipt
         }
         catch (OperationCanceledException)
         {
             throw;
         }
         catch
         {
             return false; // observable/uncertain send failure
         }
     }
 
     /// <inheritdoc/>
     public async Task DisconnectAsync(CancellationToken cancellationToken)
     {
         // GRACEFUL: a clean MQTT DISCONNECT tells the broker to DISCARD the Will (the caller has
         // already published an explicit NDEATH). Suppress the Disconnected event (intentional).
         var client = _client;
         if (client is null || !client.IsConnected)
         {
             return;
         }
 
         _suppressDisconnectedEvent = true;
         try
         {
             await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken)
                 .ConfigureAwait(false);
         }
         catch (Exception ex) when (ex is not OperationCanceledException)
         {
             // Best-effort graceful disconnect.
         }
     }
 
     /// <inheritdoc/>
     public async ValueTask DisposeAsync()
     {
         if (_disposed)
         {
             return;
         }
 
         _disposed = true;
         await RetireClientAsync().ConfigureAwait(false);
     }
 
     // ABORT: dispose the client WITHOUT a clean DISCONNECT, so the broker publishes the Will
     // (NDEATH) for a suspect/uncertain attempt. Suppress the Disconnected event (intentional).
     private async Task RetireClientAsync()
     {
         var client = _client;
         _client = null;
         if (client is null)
         {
             return;
         }
 
         _suppressDisconnectedEvent = true;
         try
         {
             // Deliberately NO DisconnectAsync — a clean DISCONNECT would suppress the Will.
             client.Dispose();
         }
         catch
         {
             // Best-effort — the client is being retired.
         }
 
         await Task.CompletedTask.ConfigureAwait(false);
     }
 
     private Task RaiseDisconnectedAsync(long generation)
     {
         var handler = Disconnected;
         return handler is null ? Task.CompletedTask : handler.Invoke(generation);
     }
 
     private IMqttClient RequireClient() =>
         _client ?? throw new InvalidOperationException("The Sparkplug transport is not connected.");
 
     // ----- Pure factories / validators (unit-testable without a broker) -----
 
     /// <summary>Build the pinned MQTT 3.1.1 client options (incl. the QoS-1, non-retained NDEATH Will).</summary>
     internal static MqttClientOptions BuildConnectOptions(SparkplugMqttConnectRequest request)
     {
         ArgumentNullException.ThrowIfNull(request);
         var builder = new MqttClientOptionsBuilder()
             .WithProtocolVersion(MqttProtocolVersion.V311) // pinned wire contract, not a library default
             .WithTcpServer(request.Endpoint.Host, request.Endpoint.Port)
             .WithClientId(request.ClientId)
             .WithCleanSession(request.CleanSession)
             .WithKeepAlivePeriod(TimeSpan.FromSeconds(request.KeepAliveSeconds))
             .WithTimeout(ConnectTimeout)
             .WithWillTopic(request.WillTopic)
             .WithWillPayload(request.WillPayload.ToArray())
             .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce) // QoS 1
             .WithWillRetain(false);
 
         if (!string.IsNullOrEmpty(request.Username))
         {
             builder.WithCredentials(request.Username, request.Password);
         }
 
         if (request.Endpoint.Tls)
         {
             builder.WithTlsOptions(o => o.UseTls());
         }
 
         return builder.Build();
     }
 
     /// <summary>Build the exact NCMD subscribe options at QoS 1.</summary>
     internal static MqttClientSubscribeOptions BuildSubscribeOptions(string topicFilter) =>
         new MqttFactory().CreateSubscribeOptionsBuilder()
             .WithTopicFilter(f => f.WithTopic(topicFilter).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
             .Build();
 
     /// <summary>Build a QoS-0, non-retained publish message.</summary>
     internal static MqttApplicationMessage BuildPublishMessage(string topic, ReadOnlyMemory<byte> payload) =>
         new MqttApplicationMessageBuilder()
             .WithTopic(topic)
             .WithPayload(payload.ToArray())
             .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce) // QoS 0
             .WithRetainFlag(false)
             .Build();
 
     /// <summary>Fail closed unless CONNECT returned a success CONNACK.</summary>
     internal static void RequireConnectSuccess(bool isSuccess, string resultCode)
     {
         if (!isSuccess)
         {
-            throw new AdapterException(new AdapterError
-            {
-                Code = SparkplugErrors.TransportConnectFailed,
-                Category = ErrorCategory.Network,
-                Message = $"CONNECT was refused (CONNACK '{resultCode}').",
-                Retryable = false,
-            });
+            throw TransportFailure(
+                SparkplugErrors.TransportConnectFailed, $"CONNECT was refused (CONNACK '{resultCode}').");
         }
     }
 
     /// <summary>
     /// Fail closed unless the SUBACK is exactly one entry for the requested NCMD topic granted at
     /// QoS 1 (a downgrade to QoS 0 or a failure result must prevent NBIRTH).
     /// </summary>
     internal static void RequireExactNcmdGrant(IReadOnlyList<KeyValuePair<string, int>> grants, string expectedTopic)
     {
         ArgumentNullException.ThrowIfNull(grants);
         if (grants.Count != 1
             || !string.Equals(grants[0].Key, expectedTopic, StringComparison.Ordinal)
             || grants[0].Value != 1)
         {
             var detail = grants.Count == 0 ? "no grant" : $"'{grants[0].Key}' -> QoS {grants[0].Value}";
-            throw new AdapterException(new AdapterError
-            {
-                Code = SparkplugErrors.TransportSubscribeFailed,
-                Category = ErrorCategory.Network,
-                Message = $"the exact NCMD SUBSCRIBE ('{expectedTopic}') must be granted QoS 1 (was {detail}).",
-                Retryable = false,
-            });
+            throw TransportFailure(
+                SparkplugErrors.TransportSubscribeFailed,
+                $"the exact NCMD SUBSCRIBE ('{expectedTopic}') must be granted QoS 1 (was {detail}).");
         }
     }
 
+    /// <summary>Build a stable, secret-free network <see cref="AdapterException"/> for a transport failure.</summary>
+    private static AdapterException TransportFailure(string code, string message) =>
+        new(new AdapterError
+        {
+            Code = code,
+            Category = ErrorCategory.Network,
+            Message = message,
+            Retryable = false,
+        });
+
     private static int MapGrantedQos(MqttClientSubscribeResultCode code) => code switch
     {
         MqttClientSubscribeResultCode.GrantedQoS0 => 0,
         MqttClientSubscribeResultCode.GrantedQoS1 => 1,
         MqttClientSubscribeResultCode.GrantedQoS2 => 2,
         _ => -1, // any failure/unspecified result
     };
 }
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
index 18617aa..03cb392 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
@@ -41,514 +41,590 @@ namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;
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
 
-    // The single immutable authority, promoted atomically only after a successful NBIRTH.
+    // The single immutable authority, promoted atomically only after a successful NBIRTH. Declared
+    // volatile so an asynchronous transport callback reads the published reference with acquire
+    // semantics (the documented synchronization mechanism for this cross-thread field).
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
 
+    /// <summary>
+    /// Test seam awaited once immediately BEFORE the promotion compare-exchange (disconnect-race
+    /// coverage). Lets a test deterministically interleave a Disconnected callback with the handoff.
+    /// </summary>
+    internal Func<Task>? PrePromotionBarrier { get; set; }
+
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
 
+    // True once a disconnect for the active session's generation arrived AFTER promotion. The
+    // operational recovery path (slice 6) consumes this; slice 4 only proves the drop is not lost.
+    internal bool CurrentSessionSuspect => _activeSession?.Handoff.SuspectAfterPromotion ?? false;
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
 
-            // Pre-authoritative disconnect latch: a drop during Begin invalidates the attempt but
-            // NEVER requests a Core rebirth (no authoritative birth exists yet).
-            var invalidated = false;
+            // Atomic establishment→authority handoff (review r2 R2). A disconnect for THIS attempt's
+            // generation and the promotion contend for ONE atomic decision (compare-exchange): a drop
+            // before promotion invalidates the attempt (Begin faults, promotes nothing); a drop after
+            // promotion flags the promoted session suspect for the operational path (slice 6). A
+            // concurrent disconnect is NEVER lost, and a dead transport can never be promoted as a
+            // clean Replaying authority. The handler stays attached through the handoff — ownership
+            // transfers to the promoted ActiveSession, so a post-promotion drop still routes.
+            var handoff = new AttemptHandoff(generation);
             disconnectHandler = droppedGeneration =>
             {
                 if (droppedGeneration == generation)
                 {
-                    invalidated = true;
+                    handoff.OnDisconnect();
                 }
 
                 return Task.CompletedTask;
             };
             attempt.Disconnected += disconnectHandler;
 
             SetProtocolState(SparkplugProtocolState.Connecting);
             await attempt.ConnectAsync(connectRequest, generation, cancellationToken).ConfigureAwait(false);
-            RequireNotInvalidated(invalidated);
+            RequireNotInvalidated(handoff);
 
             SetProtocolState(SparkplugProtocolState.SubscribingNcmd);
             await attempt.SubscribeExactAsync(SparkplugTopicFactory.NCmdSubscribe(node), cancellationToken).ConfigureAwait(false);
-            RequireNotInvalidated(invalidated);
+            RequireNotInvalidated(handoff);
 
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
 
-            RequireNotInvalidated(invalidated); // a drop between NBIRTH and promotion must not install a dead session
+            // Deterministic race barrier immediately before the promotion compare-exchange.
+            if (PrePromotionBarrier is { } barrier)
+            {
+                await barrier().ConfigureAwait(false);
+            }
+
+            // --- Promote ONE immutable authority via the atomic handoff (only after NBIRTH success) ---
+            // Build the candidate (referencing the handoff) BEFORE the CAS so a post-promotion drop
+            // that marks it suspect is observable through the promoted reference.
+            var candidate = new ActiveSession(
+                attempt, generation, start.SessionId, start.Epoch, start.RouteId, start.Host, bdSeq, resolved, baseline, handoff);
+            if (!handoff.TryPromote())
+            {
+                throw SessionSuspectDuringBegin(); // a disconnect won the race — install no session
+            }
 
-            // --- Promote ONE immutable authority (only after NBIRTH success) ---
-            attempt.Disconnected -= disconnectHandler; // slice 6 wires the operational handler
-            disconnectHandler = null;
-            _activeSession = new ActiveSession(
-                attempt, generation, start.SessionId, start.Epoch, start.RouteId, start.Host, bdSeq, resolved, baseline);
-            attempt = null; // ownership transferred
+            _activeSession = candidate; // volatile publish; handler stays attached (ownership transferred)
+            attempt = null;
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
 
     /// <summary>Publish a phase-tagged batch. Implemented in K3 slice 5.</summary>
     /// <param name="points">The batch points.</param>
     /// <param name="context">The replay context.</param>
     /// <param name="cancellationToken">Cancellation token.</param>
     /// <returns>The publish result.</returns>
     public Task<PublishResult> PublishAsync(
         IReadOnlyList<CanonicalDataPoint> points, PublishContext context, CancellationToken cancellationToken)
         => throw new NotImplementedException(NotYetImplemented);
 
     /// <summary>Complete the catch-up cutover and enter Live. Implemented in K3 slice 5.</summary>
     /// <param name="cutover">The cutover inputs.</param>
     /// <param name="cancellationToken">Cancellation token.</param>
     /// <returns>A task that completes when Live has been entered.</returns>
     public Task CompleteCatchUpAsync(ReplaySessionCutover cutover, CancellationToken cancellationToken)
         => throw new NotImplementedException(NotYetImplemented);
 
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
 
-    private static void RequireNotInvalidated(bool invalidated)
+    private static void RequireNotInvalidated(AttemptHandoff handoff)
     {
-        if (invalidated)
+        if (handoff.IsInvalidated)
         {
-            throw new AdapterException(new AdapterError
-            {
-                Code = SparkplugErrors.SessionSuspectDuringBegin,
-                Category = ErrorCategory.Network,
-                Message = "the transport dropped during initial Begin before an authoritative birth.",
-                Retryable = false,
-            });
+            throw SessionSuspectDuringBegin();
         }
     }
 
+    private static AdapterException SessionSuspectDuringBegin() =>
+        new(new AdapterError
+        {
+            Code = SparkplugErrors.SessionSuspectDuringBegin,
+            Category = ErrorCategory.Network,
+            Message = "the transport dropped during initial Begin before an authoritative birth.",
+            Retryable = false,
+        });
+
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
-        SparkplugBirthBaseline Baseline);
+        SparkplugBirthBaseline Baseline,
+        AttemptHandoff Handoff);
+
+    /// <summary>
+    /// The atomic establishment→authority handoff for one CONNECT attempt (review r2 R2). A
+    /// disconnect for this attempt's generation and the promotion decide via one compare-exchange,
+    /// so a drop concurrent with promotion is never lost: it either invalidates a pre-promotion
+    /// establishment or flags the promoted session suspect.
+    /// </summary>
+    private sealed class AttemptHandoff
+    {
+        private const int Establishing = 0;
+        private const int Invalidated = 1;
+        private const int Promoted = 2;
+
+        private int _state = Establishing;
+        private volatile bool _suspectAfterPromotion;
+
+        public AttemptHandoff(long generation) => Generation = generation;
+
+        /// <summary>This attempt's connection generation.</summary>
+        public long Generation { get; }
+
+        /// <summary>True once a disconnect invalidated an in-progress (pre-promotion) establishment.</summary>
+        public bool IsInvalidated => Volatile.Read(ref _state) == Invalidated;
+
+        /// <summary>True once a post-promotion disconnect flagged the promoted session suspect.</summary>
+        public bool SuspectAfterPromotion => _suspectAfterPromotion;
+
+        /// <summary>
+        /// Record a disconnect for this attempt's generation. Atomically invalidates an in-progress
+        /// establishment (before promotion) OR marks the already-promoted session suspect — never both,
+        /// never lost.
+        /// </summary>
+        public void OnDisconnect()
+        {
+            var prev = Interlocked.CompareExchange(ref _state, Invalidated, Establishing);
+            if (prev == Promoted)
+            {
+                _suspectAfterPromotion = true;
+            }
+        }
+
+        /// <summary>Claim promotion. Returns false if a disconnect already invalidated the attempt.</summary>
+        public bool TryPromote() =>
+            Interlocked.CompareExchange(ref _state, Promoted, Establishing) == Establishing;
+    }
 }
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugMqttTransportBehaviorTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugMqttTransportBehaviorTests.cs
new file mode 100644
index 0000000..a69054f
--- /dev/null
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugMqttTransportBehaviorTests.cs
@@ -0,0 +1,256 @@
+// ============================================================================
+// File: Session/SparkplugMqttTransportBehaviorTests.cs
+// Purpose: Locks the concrete transport's STATEFUL wrapper semantics (slice-4 review
+//          r2 R3) with a controlled IMqttClient double — no broker, no socket. Proves
+//          what the pure factories cannot: suspect ABORT retires the client WITHOUT a
+//          clean DISCONNECT and suppresses the actor-facing callback; graceful
+//          DisconnectAsync issues exactly one clean DISCONNECT and suppresses its
+//          callback; a genuine broker drop surfaces exactly once carrying the attempt's
+//          captured generation; suppression resets across a fresh client; and framework
+//          CONNECT/SUBSCRIBE exceptions are normalized to typed SPARKPLUG.* errors while
+//          cancellation stays cancellation.
+// ============================================================================
+
+using System;
+using System.Collections.Generic;
+using System.Threading;
+using System.Threading.Tasks;
+using ElpisEdgeConnect.Core.Configuration;
+using ElpisEdgeConnect.Core.Errors;
+using ElpisEdgeConnect.Sinks.SparkplugB;
+using ElpisEdgeConnect.Sinks.SparkplugB.Session;
+using FluentAssertions;
+using MQTTnet;
+using MQTTnet.Client;
+using Xunit;
+
+namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;
+
+public sealed class SparkplugMqttTransportBehaviorTests
+{
+    private const string NcmdTopic = "spBv1.0/PlantA/NCMD/gw-1";
+    private static readonly byte[] WillBytes = { 0x01, 0x02 };
+
+    private static SparkplugMqttConnectRequest Request() =>
+        SparkplugMqttConnectRequest.Create(
+            BrokerEndpoint.Create("broker.example", 1883, tls: false), "edge-01", null, null, 30,
+            cleanSession: true, "spBv1.0/PlantA/NDEATH/gw-1", WillBytes);
+
+    private static async Task<(SparkplugMqttTransport Transport, FakeMqttClient Client)> Connected(long generation = 1)
+    {
+        var client = new FakeMqttClient();
+        var transport = new SparkplugMqttTransport(() => client);
+        await transport.ConnectAsync(Request(), generation, CancellationToken.None);
+        return (transport, client);
+    }
+
+    // ==== 1. Suspect retirement (ABORT) ====
+
+    [Fact]
+    public async Task Dispose_AbortsWithoutCleanDisconnect_AndSuppressesCallback()
+    {
+        var (transport, client) = await Connected();
+        var raised = 0;
+        transport.Disconnected += _ => { raised++; return Task.CompletedTask; };
+
+        await transport.DisposeAsync();
+
+        client.DisposeCalls.Should().Be(1);      // client disposed
+        client.DisconnectCalls.Should().Be(0);   // NO clean DISCONNECT — broker publishes the Will (NDEATH)
+        await client.RaiseDisconnectedAsync();   // the retirement's own disconnected callback
+        raised.Should().Be(0);                   // suppressed (actor-requested retirement)
+    }
+
+    // ==== 2. Graceful disconnect ====
+
+    [Fact]
+    public async Task DisconnectAsync_IssuesOneCleanDisconnect_SuppressesCallback_NoSecondOnDispose()
+    {
+        var (transport, client) = await Connected();
+        var raised = 0;
+        transport.Disconnected += _ => { raised++; return Task.CompletedTask; };
+
+        await transport.DisconnectAsync(CancellationToken.None);
+        client.DisconnectCalls.Should().Be(1);   // clean DISCONNECT (broker discards the Will)
+        await client.RaiseDisconnectedAsync();
+        raised.Should().Be(0);                   // intentional — suppressed
+
+        await transport.DisposeAsync();
+        client.DisconnectCalls.Should().Be(1);   // no second clean disconnect on dispose
+    }
+
+    // ==== 3. Genuine broker loss ====
+
+    [Fact]
+    public async Task GenuineDisconnect_SurfacesOnce_CarryingCapturedGeneration()
+    {
+        var client = new FakeMqttClient();
+        var transport = new SparkplugMqttTransport(() => client);
+        long? got = null;
+        var raised = 0;
+        transport.Disconnected += g => { got = g; raised++; return Task.CompletedTask; };
+        await transport.ConnectAsync(Request(), 5, CancellationToken.None);
+
+        await client.RaiseDisconnectedAsync();   // an UNsuppressed genuine drop
+
+        raised.Should().Be(1);
+        got.Should().Be(5);                      // carries the attempt's captured generation
+    }
+
+    // ==== 4. Fresh-client reset ====
+
+    [Fact]
+    public async Task NewClient_ResetsSuppression_AndDelayedRetiredCallbackKeepsItsGeneration()
+    {
+        var clientA = new FakeMqttClient();
+        var clientB = new FakeMqttClient();
+        var seq = 0;
+        var transport = new SparkplugMqttTransport(() => seq++ == 0 ? clientA : (IMqttClient)clientB);
+        var events = new List<long>();
+        transport.Disconnected += g => { lock (events) { events.Add(g); } return Task.CompletedTask; };
+
+        await transport.ConnectAsync(Request(), 1, CancellationToken.None); // client A, gen 1
+        await transport.ConnectAsync(Request(), 2, CancellationToken.None); // retires A, client B, gen 2
+
+        await clientB.RaiseDisconnectedAsync();  // genuine drop on the live client
+        await clientA.RaiseDisconnectedAsync();  // delayed callback from the retired client
+
+        events.Should().Contain(2); // live client's drop is NOT accidentally suppressed
+        events.Should().Contain(1); // the retired client's delayed callback retains its old generation
+        clientA.DisposeCalls.Should().Be(1); // A was retired when B connected
+    }
+
+    // ==== 5. Exception normalization ====
+
+    [Fact]
+    public async Task ConnectAsync_FrameworkException_NormalizedToTransportConnectFailed()
+    {
+        var client = new FakeMqttClient { ConnectThrow = new InvalidOperationException("socket boom") };
+        var transport = new SparkplugMqttTransport(() => client);
+
+        await transport.Invoking(t => t.ConnectAsync(Request(), 1, CancellationToken.None))
+            .Should().ThrowAsync<AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.TransportConnectFailed);
+    }
+
+    [Fact]
+    public async Task ConnectAsync_Cancellation_StaysCancellation_NotWrapped()
+    {
+        var client = new FakeMqttClient { ConnectThrow = new OperationCanceledException() };
+        var transport = new SparkplugMqttTransport(() => client);
+
+        await transport.Invoking(t => t.ConnectAsync(Request(), 1, CancellationToken.None))
+            .Should().ThrowAsync<OperationCanceledException>();
+    }
+
+    [Fact]
+    public async Task SubscribeExactAsync_FrameworkException_NormalizedToTransportSubscribeFailed()
+    {
+        var client = new FakeMqttClient { SubscribeThrow = new InvalidOperationException("subscribe boom") };
+        var transport = new SparkplugMqttTransport(() => client);
+        await transport.ConnectAsync(Request(), 1, CancellationToken.None);
+
+        await transport.Invoking(t => t.SubscribeExactAsync(NcmdTopic, CancellationToken.None))
+            .Should().ThrowAsync<AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.TransportSubscribeFailed);
+    }
+
+    [Fact]
+    public async Task SubscribeExactAsync_Cancellation_StaysCancellation_NotWrapped()
+    {
+        var client = new FakeMqttClient { SubscribeThrow = new OperationCanceledException() };
+        var transport = new SparkplugMqttTransport(() => client);
+        await transport.ConnectAsync(Request(), 1, CancellationToken.None);
+
+        await transport.Invoking(t => t.SubscribeExactAsync(NcmdTopic, CancellationToken.None))
+            .Should().ThrowAsync<OperationCanceledException>();
+    }
+
+    // ---- The controlled IMqttClient double (no broker/socket) ----
+    private sealed class FakeMqttClient : IMqttClient
+    {
+        public int ConnectCalls { get; private set; }
+        public int DisconnectCalls { get; private set; }
+        public int DisposeCalls { get; private set; }
+        public Exception? ConnectThrow { get; init; }
+        public Exception? SubscribeThrow { get; init; }
+
+        public bool IsConnected { get; private set; }
+        public MqttClientOptions Options => null!;
+
+        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
+
+        /// <summary>Invoke the actor-wired disconnected handler (a genuine or retirement drop).</summary>
+        public Task RaiseDisconnectedAsync() =>
+            DisconnectedAsync?.Invoke(new MqttClientDisconnectedEventArgs(
+                clientWasConnected: true, connectResult: null!,
+                reason: MqttClientDisconnectReason.NormalDisconnection,
+                reasonString: null!, userProperties: null!, exception: null!)) ?? Task.CompletedTask;
+
+        public Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken)
+        {
+            ConnectCalls++;
+            if (ConnectThrow is not null)
+            {
+                return Task.FromException<MqttClientConnectResult>(ConnectThrow);
+            }
+
+            IsConnected = true;
+            return Task.FromResult(new MqttClientConnectResult()); // ResultCode defaults to Success
+        }
+
+        public Task<MqttClientSubscribeResult> SubscribeAsync(
+            MqttClientSubscribeOptions options, CancellationToken cancellationToken)
+        {
+            if (SubscribeThrow is not null)
+            {
+                return Task.FromException<MqttClientSubscribeResult>(SubscribeThrow);
+            }
+
+            throw new NotSupportedException("The behavior tests never exercise a successful SUBACK on the double.");
+        }
+
+        public Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken)
+        {
+            DisconnectCalls++;
+            IsConnected = false;
+            return Task.CompletedTask;
+        }
+
+        public void Dispose()
+        {
+            DisposeCalls++;
+            IsConnected = false;
+        }
+
+        // ---- Unused IMqttClient surface (never invoked by the transport) ----
+        event Func<MqttApplicationMessageReceivedEventArgs, Task> IMqttClient.ApplicationMessageReceivedAsync
+        {
+            add { } remove { }
+        }
+
+        event Func<MqttClientConnectedEventArgs, Task> IMqttClient.ConnectedAsync { add { } remove { } }
+
+        event Func<MqttClientConnectingEventArgs, Task> IMqttClient.ConnectingAsync { add { } remove { } }
+
+        event Func<MQTTnet.Diagnostics.InspectMqttPacketEventArgs, Task> IMqttClient.InspectPacketAsync
+        {
+            add { } remove { }
+        }
+
+        public Task PingAsync(CancellationToken cancellationToken) =>
+            throw new NotSupportedException();
+
+        public Task<MqttClientPublishResult> PublishAsync(
+            MqttApplicationMessage applicationMessage, CancellationToken cancellationToken) =>
+            throw new NotSupportedException();
+
+        public Task SendExtendedAuthenticationExchangeDataAsync(
+            MqttExtendedAuthenticationExchangeData data, CancellationToken cancellationToken) =>
+            throw new NotSupportedException();
+
+        public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(
+            MqttClientUnsubscribeOptions options, CancellationToken cancellationToken) =>
+            throw new NotSupportedException();
+    }
+}
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorBeginTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorBeginTests.cs
index 2a7155d..2c6f9e4 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorBeginTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorBeginTests.cs
@@ -37,441 +37,547 @@ namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;
 public sealed class SparkplugSessionActorBeginTests : IDisposable
 {
     private const string Group = "PlantA";
     private const string Node = "gw-1";
     private static readonly DateTimeOffset Clock = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
 
     private readonly string _dir = Path.Combine(Path.GetTempPath(), "k3-begin-" + Guid.NewGuid().ToString("N"));
 
     public void Dispose()
     {
         SqliteConnection.ClearAllPools();
         try { if (Directory.Exists(_dir)) { Directory.Delete(_dir, recursive: true); } }
         catch { /* best effort */ }
     }
 
     // ==== Happy path ====
 
     [Fact]
     public async Task Begin_EmptyRoute_OrdersConnectSubscribeNbirth_AndPromotes()
     {
         var (actor, fake) = await RunningActor();
 
         await actor.BeginReplaySessionAsync(Start(sessionId: 7, epoch: 3), CancellationToken.None);
 
         fake.Calls.Should().Equal(
             "connect",
             $"subscribe:spBv1.0/{Group}/NCMD/{Node}",
             $"publish:spBv1.0/{Group}/NBIRTH/{Node}");
 
         actor.State.Should().Be(AdapterState.Running);
         actor.ProtocolState.Should().Be(SparkplugProtocolState.Replaying);
         actor.HasSession.Should().BeTrue();
         actor.CurrentSessionId.Should().Be(ReplaySessionId.Create(7));
         actor.CurrentEpoch.Should().Be(ReplayEpochId.Create(3));
         actor.CurrentRouteId.Should().Be("route-1");
         actor.CurrentHost.Should().NotBeNull();
         actor.CurrentBdSeq.Value.Should().Be(0); // first reservation
         actor.CurrentGeneration.Should().Be(1); // actor-owned long, first attempt
         actor.LastIssuedGeneration.Should().Be(1);
     }
 
     [Fact]
     public async Task Begin_RegistersNDeathWill_WithReservedBdSeq()
     {
         var (actor, fake) = await RunningActor();
 
         await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);
 
         fake.ConnectRequest!.WillTopic.Should().Be($"spBv1.0/{Group}/NDEATH/{Node}");
         fake.ConnectRequest.WillPayload.ToArray()
             .Should().Equal(SparkplugPayloadEncoder.EncodeNDeath(SparkplugBirthDeathSequence.Create(0)));
         fake.Generation.Should().Be(1); // actor supplied the generation
     }
 
     [Fact]
     public async Task Begin_ReservesBdSeqBeforeConnect_AndPersistsIt()
     {
         var store = NewStore();
         var (actor, _) = await RunningActor(store);
 
         await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);
 
         // The reservation committed to the store (a fresh reserve continues from 1).
         store.ReserveNextBdSeq(StoreId()).Value.Should().Be(1);
     }
 
     [Fact]
     public async Task Begin_PopulatedRoute_ResolvesAliases_AndPromotesManifest()
     {
         var store = NewStore();
         var (actor, _) = await RunningActor(store);
 
         await actor.BeginReplaySessionAsync(StartPopulated(), CancellationToken.None);
 
         actor.CurrentManifest!.Metrics.Should().HaveCount(2);
         actor.CurrentManifest.AliasMap.Values.Should().OnlyHaveUniqueItems().And.NotContain(0UL);
         actor.CurrentBaseline!.BaselineMetrics.Should().HaveCount(2);
     }
 
     // ==== Failed step promotes nothing ====
 
     [Theory]
     [InlineData("connect")]
     [InlineData("subscribe")]
     [InlineData("nbirth")]
     public async Task Begin_FailedStep_PromotesNothing_RetiresAttempt_Faults(string failAt)
     {
         var (actor, fake) = await RunningActor();
         switch (failAt)
         {
             case "connect": fake.FailConnect = _ => throw new InvalidOperationException("connect"); break;
             case "subscribe": fake.FailSubscribe = _ => throw new InvalidOperationException("subscribe"); break;
             case "nbirth": fake.PublishReturnsFalse = true; break;
         }
 
         await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
             .Should().ThrowAsync<Exception>();
 
         actor.HasSession.Should().BeFalse();
         actor.CurrentManifest.Should().BeNull();
         actor.State.Should().Be(AdapterState.Failed);
         actor.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
         fake.Calls.Should().Contain("dispose"); // the failed attempt's transport was retired
     }
 
     [Fact]
     public async Task Begin_FailedNbirth_LeavesStoreBdSeqReservedButUnused()
     {
         var store = NewStore();
         var (actor, fake) = await RunningActor(store);
         fake.PublishReturnsFalse = true;
 
         await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None)).Should().ThrowAsync<Exception>();
 
         // bdSeq 0 was reserved (committed before CONNECT) and is skipped, never reused.
         store.ReserveNextBdSeq(StoreId()).Value.Should().Be(1);
     }
 
     // ==== Preflight failure precedes every durable side effect (review B1) ====
 
     [Fact]
     public async Task Begin_AliasResolutionFails_ConsumesNoBdSeq_NoGeneration_NoTransport()
     {
         // Preflight (plan + alias resolve) runs BEFORE bdSeq reservation, the generation issue,
         // and transport creation. An alias-store failure must therefore leave all three untouched.
         var store = new ThrowingAliasStore();
         var transportCreated = 0;
         var actor = new SparkplugSessionActor(
             "spb-1", store, () => { transportCreated++; return new FakeTransport(); }, () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
 
         await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
             .Should().ThrowAsync<AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.IdentityStoreUnavailable);
 
         store.ReserveCalls.Should().Be(0);           // no durable bdSeq consumed
         actor.LastIssuedGeneration.Should().Be(0);   // no generation issued
         transportCreated.Should().Be(0);             // no client created
         actor.HasSession.Should().BeFalse();
         actor.State.Should().Be(AdapterState.Failed);
     }
 
+    [Fact]
+    public async Task Begin_PreEpochSnapshot_FailsBeforeAliasBdSeqGenerationOrTransport()
+    {
+        // A pre-Unix-epoch acquisition timestamp is a VALID Core LatestValueSnapshot input that the
+        // shared Sparkplug mapper rejects during planning — the reachable planning failure. It must
+        // precede alias resolution, bdSeq reservation, the generation issue, and transport creation.
+        var store = new RecordingStore();
+        var transportCreated = 0;
+        var host = new FakeReplaySessionHost();
+        var actor = new SparkplugSessionActor(
+            "spb-1", store, () => { transportCreated++; return new FakeTransport(); }, () => Clock);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+
+        await actor.Invoking(a => a.BeginReplaySessionAsync(StartPreEpoch(host), CancellationToken.None))
+            .Should().ThrowAsync<AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.EncodeTimestampPreEpoch);
+
+        store.AliasCalls.Should().Be(0);             // planning rejected before alias resolution
+        store.ReserveCalls.Should().Be(0);           // ... and before bdSeq reservation
+        actor.LastIssuedGeneration.Should().Be(0);   // no generation issued
+        transportCreated.Should().Be(0);             // no client created
+        actor.HasSession.Should().BeFalse();
+        actor.State.Should().Be(AdapterState.Failed);
+        actor.ProtocolState.Should().Be(SparkplugProtocolState.Faulted);
+        host.RebirthRequests.Should().Be(0);
+    }
+
+    // ==== Atomic disconnect/promotion handoff (review r2 R2) ====
+
+    [Fact]
+    public async Task Begin_DisconnectRacesPromotion_PromotesNothing_FaultsSuspect()
+    {
+        var (actor, fake) = await RunningActor();
+        var host = new FakeReplaySessionHost();
+
+        // Deterministically inject the drop in the window immediately BEFORE the promotion CAS:
+        // NBIRTH already succeeded locally; the disconnect must still prevent promotion.
+        actor.PrePromotionBarrier = () => fake.RaiseDisconnected(fake.Generation!.Value);
+
+        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(host: host), CancellationToken.None))
+            .Should().ThrowAsync<AdapterException>()
+            .Where(e => e.Error.Code == SparkplugErrors.SessionSuspectDuringBegin);
+
+        actor.HasSession.Should().BeFalse();      // nothing promoted onto a dead transport
+        actor.State.Should().Be(AdapterState.Failed);
+        host.RebirthRequests.Should().Be(0);      // no authoritative birth existed
+        fake.Calls.Should().Contain("dispose");   // the suspect attempt was aborted
+    }
+
+    [Fact]
+    public async Task Begin_PostPromotionDisconnect_MarksSessionSuspect_NotCleanReplaying()
+    {
+        var (actor, fake) = await RunningActor();
+        await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);
+        actor.CurrentSessionSuspect.Should().BeFalse(); // clean at promotion
+
+        // A genuine drop AFTER promotion must be captured, not lost: the promoted authority is
+        // flagged suspect for the operational path (slice 6), never left as clean Replaying.
+        await fake.RaiseDisconnected(actor.CurrentGeneration);
+
+        actor.HasSession.Should().BeTrue();             // the authority remains (slice 6 recovers)
+        actor.CurrentSessionSuspect.Should().BeTrue();  // ... but is now suspect
+        actor.ProtocolState.Should().Be(SparkplugProtocolState.Replaying);
+    }
+
     // ==== Readiness / wiring gates ====
 
     [Fact]
     public async Task Begin_NotWiredWithStore_FailsClosed()
     {
         var actor = new SparkplugSessionActor("spb-1"); // lifecycle-only, no store
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
 
         await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
             .Should().ThrowAsync<AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.SessionNotReady);
     }
 
     [Fact]
     public async Task Begin_BeforeRunning_FailsClosed()
     {
         var actor = new SparkplugSessionActor("spb-1", NewStore(), () => new FakeTransport(), () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None); // not started
 
         await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
             .Should().ThrowAsync<InvalidOperationException>();
     }
 
     [Fact]
     public async Task Begin_HealthAfterBirth_IsDegradedWithSession()
     {
         var (actor, _) = await RunningActor();
         await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);
 
         var health = await actor.CheckHealthAsync(CancellationToken.None);
         health.Level.Should().Be(HealthLevel.Degraded); // replaying = active session
         health.Metrics.Should().ContainKey("hasSession").WhoseValue.Should().Be(true);
     }
 
     // ==== NBIRTH content (byte-parity with an independently-built K2 payload) ====
 
     [Fact]
     public async Task Begin_EmptyRoute_PublishesExpectedNbirthBytes()
     {
         var (actor, fake) = await RunningActor();
         await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);
 
         var expected = SparkplugPayloadEncoder.EncodeNBirth(
             SparkplugSequenceNumber.Create(0), SparkplugBirthDeathSequence.Create(0), bdSeqAlias: 1UL, Clock,
             actor.CurrentManifest!.Metrics, actor.CurrentManifest.AliasMap);
         var nbirth = fake.Published.Single(p => p.Topic.Contains("NBIRTH")).Payload;
         nbirth.Should().Equal(expected); // seq=0, bdSeq=0, Node Control/Rebirth=false, control metrics — all via K2
     }
 
     [Fact]
     public async Task Begin_PopulatedRoute_PublishesExpectedNbirthBytes()
     {
         var (actor, fake) = await RunningActor();
         await actor.BeginReplaySessionAsync(StartPopulated(), CancellationToken.None);
 
         var expected = SparkplugPayloadEncoder.EncodeNBirth(
             SparkplugSequenceNumber.Create(0), SparkplugBirthDeathSequence.Create(0), bdSeqAlias: 3UL, Clock,
             actor.CurrentManifest!.Metrics, actor.CurrentManifest.AliasMap); // bdSeqAlias = max(1,2)+1
         var nbirth = fake.Published.Single(p => p.Topic.Contains("NBIRTH")).Payload;
         nbirth.Should().Equal(expected);
     }
 
     // ==== Duplicate Begin ====
 
     [Fact]
     public async Task Begin_SecondBegin_FailsClosed_WithNoSideEffects()
     {
         var (actor, fake) = await RunningActor();
         await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);
         var generationAfterFirst = actor.LastIssuedGeneration;
         var callsAfterFirst = fake.Calls.Count;
 
         await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None))
             .Should().ThrowAsync<AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.SessionAlreadyActive);
 
         actor.LastIssuedGeneration.Should().Be(generationAfterFirst); // no new generation issued
         fake.Calls.Count.Should().Be(callsAfterFirst);                // no new store/network call
     }
 
     // ==== Generation is an attempt token (consumed even on failure) ====
 
     [Fact]
     public async Task Begin_FailedAttempt_ConsumesGeneration_ThenRestartUsesNext()
     {
         var store = NewStore();
         var fake1 = new FakeTransport { PublishReturnsFalse = true };
         var fake2 = new FakeTransport();
         var factoryCall = 0;
         var actor = new SparkplugSessionActor("spb-1", store, () => (factoryCall++ == 0 ? (ISparkplugMqttTransport)fake1 : fake2), () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
 
         await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), CancellationToken.None)).Should().ThrowAsync<Exception>();
         actor.LastIssuedGeneration.Should().Be(1); // failed attempt consumed generation 1
         actor.HasSession.Should().BeFalse();
 
         // Recover (Stop -> Start) and Begin again — the next attempt uses generation 2.
         await actor.StopAsync(CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         await actor.BeginReplaySessionAsync(Start(), CancellationToken.None);
 
         actor.LastIssuedGeneration.Should().Be(2);
         actor.CurrentGeneration.Should().Be(2);
         fake2.Generation.Should().Be(2);
     }
 
     // ==== Cancellation at each step ====
 
     [Theory]
     [InlineData("connect")]
     [InlineData("subscribe")]
     [InlineData("nbirth")]
     public async Task Begin_CancellationAtStep_FaultsAndPromotesNothing(string step)
     {
         var (actor, fake) = await RunningActor();
         using var cts = new CancellationTokenSource();
         Func<CancellationToken, Task> cancel = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
         switch (step)
         {
             case "connect": fake.FailConnect = cancel; break;
             case "subscribe": fake.FailSubscribe = cancel; break;
             case "nbirth": fake.FailPublish = cancel; break;
         }
 
         await actor.Invoking(a => a.BeginReplaySessionAsync(Start(), cts.Token))
             .Should().ThrowAsync<OperationCanceledException>();
 
         actor.HasSession.Should().BeFalse();
         actor.State.Should().Be(AdapterState.Failed);
         fake.Calls.Should().Contain("dispose");
     }
 
     // ==== Pre-authoritative disconnect ====
 
     [Fact]
     public async Task Begin_PreAuthoritativeDisconnect_FaultsAndRequestsNoRebirth()
     {
         var (actor, fake) = await RunningActor();
         var host = new FakeReplaySessionHost();
         fake.DisconnectDuringSubscribe = true;
 
         await actor.Invoking(a => a.BeginReplaySessionAsync(Start(host: host), CancellationToken.None))
             .Should().ThrowAsync<AdapterException>()
             .Where(e => e.Error.Code == SparkplugErrors.SessionSuspectDuringBegin);
 
         actor.HasSession.Should().BeFalse();
         host.RebirthRequests.Should().Be(0); // no authoritative birth exists — never request a Core rebirth
         fake.Calls.Should().Contain("dispose");
     }
 
     // ==== Helpers ====
 
     private async Task<(SparkplugSessionActor Actor, FakeTransport Fake)> RunningActor(
         SqliteSparkplugIdentityStateStore? store = null)
     {
         var fake = new FakeTransport();
         var actor = new SparkplugSessionActor("spb-1", store ?? NewStore(), () => fake, () => Clock);
         await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
         await actor.StartAsync(CancellationToken.None);
         return (actor, fake);
     }
 
     private SqliteSparkplugIdentityStateStore NewStore() =>
         new(Path.Combine(_dir, "sparkplug", "identity-state.db"));
 
     private static SparkplugStoreIdentity StoreId() =>
         SparkplugStoreIdentity.Create(
             ValidConfig().ResolveBrokerEndpoint(), SparkplugEdgeNodeIdentity.Create(Group, Node));
 
     private static SparkplugSinkConfiguration ValidConfig() => new()
     {
         InstanceId = "spb-1",
         ProtocolName = SparkplugBProtocol.ProtocolName,
         BrokerHost = "localhost",
         GroupId = Group,
         EdgeNodeId = Node,
     };
 
     private static ReplaySessionStart Start(long sessionId = 1, long epoch = 0, FakeReplaySessionHost? host = null) =>
         ReplaySessionStart.Create(
             ReplaySessionId.Create(sessionId), ReplayEpochId.Create(epoch), "route-1",
             ReplaySessionStartState.Create(ReplayBoundary.Create(0, 0), LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(0))),
             host ?? new FakeReplaySessionHost());
 
     private static ReplaySessionStart StartPopulated()
     {
         var snapshot = new LatestValueSnapshot(RouteSchemaGeneration.Create(0), new Dictionary<CanonicalMetricKey, LatestMetricValue>
         {
             [Key("srcA")] = Lmv("srcA"),
             [Key("srcB")] = Lmv("srcB"),
         });
         return ReplaySessionStart.Create(
             ReplaySessionId.Create(1), ReplayEpochId.Create(0), "route-1",
             ReplaySessionStartState.Create(ReplayBoundary.Create(0, 5), snapshot), new FakeReplaySessionHost());
     }
 
+    private static ReplaySessionStart StartPreEpoch(FakeReplaySessionHost host)
+    {
+        var preEpoch = new DateTimeOffset(1969, 12, 31, 0, 0, 0, TimeSpan.Zero); // before the Unix epoch
+        var value = LatestMetricValue.Create(
+            Key("srcA"), CanonicalValueType.Integer, 1, isNull: false, preEpoch, DataQuality.Good, routeBufferSequence: 1);
+        var snapshot = new LatestValueSnapshot(RouteSchemaGeneration.Create(0),
+            new Dictionary<CanonicalMetricKey, LatestMetricValue> { [Key("srcA")] = value });
+        return ReplaySessionStart.Create(
+            ReplaySessionId.Create(1), ReplayEpochId.Create(0), "route-1",
+            ReplaySessionStartState.Create(ReplayBoundary.Create(0, 2), snapshot), host); // cutoff strictly above seq 1
+    }
+
     private static CanonicalMetricKey Key(string source) => CanonicalMetricKey.Create(source, "dev", "temp");
 
     private static LatestMetricValue Lmv(string source) =>
         LatestMetricValue.Create(Key(source), CanonicalValueType.Integer, 1, isNull: false, Clock, DataQuality.Good, routeBufferSequence: 1);
 
     // A store that fails alias resolution and records whether the durable bdSeq reservation was
     // ever reached — proving preflight failure precedes any durable side effect (review B1).
     private sealed class ThrowingAliasStore : ISparkplugIdentityStateStore
     {
         public int ReserveCalls { get; private set; }
 
         public SparkplugBirthDeathSequence ReserveNextBdSeq(SparkplugStoreIdentity identity)
         {
             ReserveCalls++;
             return SparkplugBirthDeathSequence.Create(0);
         }
 
         public IReadOnlyDictionary<SparkplugAliasKey, ulong> ResolveAliases(
             SparkplugStoreIdentity identity, IReadOnlyCollection<SparkplugAliasKey> manifest) =>
             throw new AdapterException(new AdapterError
             {
                 Code = SparkplugErrors.IdentityStoreUnavailable,
                 Category = ErrorCategory.Internal,
                 Message = "injected alias-store failure.",
                 Retryable = false,
             });
 
         public void Dispose()
         {
         }
     }
 
+    // A store that never fails but records whether alias resolution / bdSeq reservation were reached,
+    // so a test can prove a PLANNING failure precedes both (review r2 R1).
+    private sealed class RecordingStore : ISparkplugIdentityStateStore
+    {
+        public int AliasCalls { get; private set; }
+        public int ReserveCalls { get; private set; }
+
+        public SparkplugBirthDeathSequence ReserveNextBdSeq(SparkplugStoreIdentity identity)
+        {
+            ReserveCalls++;
+            return SparkplugBirthDeathSequence.Create(0);
+        }
+
+        public IReadOnlyDictionary<SparkplugAliasKey, ulong> ResolveAliases(
+            SparkplugStoreIdentity identity, IReadOnlyCollection<SparkplugAliasKey> manifest)
+        {
+            AliasCalls++;
+            return new Dictionary<SparkplugAliasKey, ulong>();
+        }
+
+        public void Dispose()
+        {
+        }
+    }
+
     private sealed class FakeReplaySessionHost : IReplaySessionHost
     {
         public int RebirthRequests { get; private set; }
 
         public ValueTask RequestRebirthAsync(RebirthRequest request, CancellationToken cancellationToken)
         {
             RebirthRequests++;
             return ValueTask.CompletedTask;
         }
     }
 
     private sealed class FakeTransport : ISparkplugMqttTransport
     {
         public List<string> Calls { get; } = new();
         public List<(string Topic, byte[] Payload)> Published { get; } = new();
         public SparkplugMqttConnectRequest? ConnectRequest { get; private set; }
         public long? Generation { get; private set; }
         public bool IsConnected { get; private set; }
         public Func<CancellationToken, Task>? FailConnect { get; set; }
         public Func<CancellationToken, Task>? FailSubscribe { get; set; }
         public bool PublishReturnsFalse { get; set; }
         public Func<CancellationToken, Task>? FailPublish { get; set; }
         public bool DisconnectDuringSubscribe { get; set; }
 
         public event Func<long, Task>? Disconnected;
 
+        /// <summary>Raise the actor-facing disconnect callback for a given generation (test-driven drop).</summary>
+        public Task RaiseDisconnected(long generation) => Disconnected?.Invoke(generation) ?? Task.CompletedTask;
+
         public async Task ConnectAsync(SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken)
         {
             Calls.Add("connect");
             ConnectRequest = request;
             Generation = connectionGeneration;
             if (FailConnect is not null) { await FailConnect(cancellationToken); }
             IsConnected = true;
         }
 
         public async Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken)
         {
             Calls.Add($"subscribe:{topicFilter}");
             if (DisconnectDuringSubscribe && Disconnected is not null)
             {
                 await Disconnected(Generation!.Value); // pre-authoritative drop during Begin
             }
 
             if (FailSubscribe is not null) { await FailSubscribe(cancellationToken); }
         }
 
         public async Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
         {
             Calls.Add($"publish:{topic}");
             Published.Add((topic, payload.ToArray()));
             if (FailPublish is not null) { await FailPublish(cancellationToken); }
             return !PublishReturnsFalse;
         }
 
         public Task DisconnectAsync(CancellationToken cancellationToken)
         {
             Calls.Add("disconnect");
             IsConnected = false;
             return Task.CompletedTask;
         }
 
         public ValueTask DisposeAsync()
         {
             Calls.Add("dispose");
             return ValueTask.CompletedTask;
         }
     }
 }
```
