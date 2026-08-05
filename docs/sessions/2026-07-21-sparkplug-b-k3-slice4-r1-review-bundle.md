# K3 Slice 4 — Review Bundle r1 (initial Begin, transport seam)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `c551d52` — *fix(sparkplug): K3 slice 4 review r1 — preflight ordering, attempt tokens, secret-safe connect*
**Plan (frozen):** `docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md` (§1.1, §1.2, §4, §5, §6, §9)
**Build:** SparkplugB src `0/0` (warnings-as-errors); tests project `0/0`.
**Tests:** `412 passed / 0 failed / 0 skipped` (whole SparkplugB test project, no broker required).

This bundle folds the five blockers from the slice-4 review. No Core change. Below, each blocker maps to the fix and the evidence that locks it.

---

## B1 — Preflight now precedes every durable side effect; Start readiness; duplicate Begin

**Fix (`SparkplugSessionActor.BeginReplaySessionAsync`).** The ordered contract is now:

```
RequireReadyForSession (Running + no active session + store/factory/config wired)
  → plan (SparkplugBirthPlanner.Plan)               // validates snapshot via shared K2 mapper
  → resolve + validate exact alias map              // store ResolveAliases + Planner.Resolve
  → compute + checked bdSeq metric alias
  → build baseline                                  // all preflight, NO durable side effect
  → RESERVE bdSeq                                   // the LAST durable pre-CONNECT step
  → build Will/connect request
  → issue unique checked generation (persisted)     // consumed even on failure
  → CONNECT → SUBSCRIBE(exact NCMD) → NBIRTH
  → promote ONE immutable ActiveSession
```

`bdSeq` remains committed before CONNECT, but is now the **final** durable pre-CONNECT operation rather than the first. A planning/alias failure consumes no `bdSeq`, issues no generation, creates no transport.

**Start readiness — option (a) chosen.** The lifecycle-only (store-less) constructors on both `SparkplugSinkAdapter` and `SparkplugSessionActor` are now `internal` (test/K4-composition only). A **public** adapter always carries an identity store, so it can never report `Running`/`Healthy` while structurally unable to begin a session. The store-less path still fails closed at Begin with `SESSION_NOT_READY`.

**Duplicate Begin.** `RequireReadyForSession` rejects `_activeSession is not null` with `SPARKPLUG.SESSION_ALREADY_ACTIVE` **before** any store/network call — no second `bdSeq`, no second client, active session untouched.

**Evidence**
- `Begin_AliasResolutionFails_ConsumesNoBdSeq_NoGeneration_NoTransport` — fake store throws on `ResolveAliases`; asserts `ReserveCalls == 0`, `LastIssuedGeneration == 0`, transport factory never invoked, `HasSession == false`, `Failed`.
- `Begin_ReservesBdSeqBeforeConnect_AndPersistsIt`, `Begin_FailedNbirth_LeavesStoreBdSeqReservedButUnused` — bdSeq committed-then-skipped, never reused.
- `Begin_SecondBegin_FailsClosed_WithNoSideEffects` — `SESSION_ALREADY_ACTIVE`; `LastIssuedGeneration` and `fake.Calls.Count` unchanged.
- `Begin_NotWiredWithStore_FailsClosed` (`SESSION_NOT_READY`), `Begin_BeforeRunning_FailsClosed`.

**Pushback (planning-failure test).** The review asked for a distinct *planning-failure* actor test alongside the alias-failure one. An actor-level planning failure is **unreachable with valid Core inputs**: `LatestMetricValue.Create` rejects `Array`/`Object`/`Null` at snapshot construction (`LatestValueSnapshot.cs:258`), and a source-qualified published name (`{source}/{device}/{tagPath}`, all non-empty, no `/` in source/device) can never equal a reserved name (`bdSeq`, `Node Control/Rebirth`) nor collide. `SparkplugBirthPlanner.Plan`'s throw paths are covered directly by the birth-layer unit tests (`SparkplugBirthLayerTests`). At the actor level, the alias-failure test is the equivalent preflight-ordering proof (planning runs *before* alias resolution, so if alias failure consumes nothing, planning failure — were it reachable — consumes nothing a fortiori). This is the same source-qualified-injectivity argument accepted in the slice-3 duplicate-name pushback.

---

## B2 — Attempt token vs. session authority; single immutable authority; pre-authoritative latch

**Attempt token.** `_lastIssuedConnectionGeneration` is now a per-attempt counter, separate from the promoted session's generation. Immediately before CONNECT: overflow is checked (`== long.MaxValue` → typed `SPARKPLUG.GENERATION_OVERFLOW` before the client is created), then `generation = _lastIssuedConnectionGeneration + 1; _lastIssuedConnectionGeneration = generation;`. The value is consumed whether or not the attempt births.

**Single immutable authority.** All candidate state is built before the NBIRTH send; the success commit promotes one record:

```csharp
private sealed record ActiveSession(
    ISparkplugMqttTransport Transport, long TransportGeneration,
    ReplaySessionId SessionId, ReplayEpochId Epoch, string RouteId, IReplaySessionHost Host,
    SparkplugBirthDeathSequence BdSeq, ResolvedSparkplugBirthPlan Manifest, SparkplugBirthBaseline Baseline);

_activeSession = new ActiveSession(attempt, generation, start.SessionId, start.Epoch,
    start.RouteId, start.Host, bdSeq, resolved, baseline);
```

The baseline is allocated during preflight (before the send), so a promotion can never leave the receiver with an accepted NBIRTH and no local birth state. The record retains `start.Host` and `start.RouteId` for slice 5 (first-observed `SchemaChange`) and slice 6 (disconnect/NCMD rebirth).

**Pre-authoritative disconnect latch.** The attempt subscribes a disconnect handler before CONNECT; a drop carrying this attempt's generation sets an `invalidated` flag. `RequireNotInvalidated(invalidated)` is checked after CONNECT, after SUBSCRIBE, after NBIRTH publish, and once more immediately before promotion — a drop between a successful NBIRTH and promotion faults with `SPARKPLUG.SESSION_SUSPECT_DURING_BEGIN` and installs no dead session. The handler **never** requests a Core rebirth (no authoritative birth exists yet). On promotion the Begin-time handler is detached (slice 6 wires the operational one).

**Evidence**
- `Begin_FailedAttempt_ConsumesGeneration_ThenRestartUsesNext` — failed attempt → `LastIssuedGeneration == 1`; Stop→Start→Begin → generation `2` on both actor and the second transport.
- `Begin_EmptyRoute_OrdersConnectSubscribeNbirth_AndPromotes` — asserts the promoted authority (session/epoch/route/host/bdSeq/generation) coherently.
- `Begin_PreAuthoritativeDisconnect_FaultsAndRequestsNoRebirth` — disconnect during SUBSCRIBE → `SESSION_SUSPECT_DURING_BEGIN`, `host.RebirthRequests == 0`, attempt disposed.
- `Begin_FailedStep_PromotesNothing_RetiresAttempt_Faults` (connect/subscribe/nbirth) — no promotion; attempt retired.

---

## B3 — Concrete transport proves the MQTT 3.1.1 profile (no broker)

**Fix (`SparkplugMqttTransport`).**
- `WithProtocolVersion(MqttProtocolVersion.V311)` — pinned (protocol level 4), not a library default.
- CONNACK validated: `RequireConnectSuccess` throws `SPARKPLUG.TRANSPORT_CONNECT_FAILED` on any non-success code.
- SUBACK validated: `RequireExactNcmdGrant` requires exactly one entry, the exact requested topic, granted QoS 1 — a downgrade to QoS 0, a failure result, a wrong topic, or a wrong count all throw `SPARKPLUG.TRANSPORT_SUBSCRIBE_FAILED` (prevents NBIRTH before the NCMD control path is established).
- Two teardown semantics: `DisconnectAsync` = graceful clean DISCONNECT (broker discards the Will; used by slice 6 after an explicit NDEATH); `DisposeAsync`/`RetireClientAsync` = ABORT (dispose **without** DISCONNECT so the broker publishes the NDEATH Will) — the correct retirement for a suspect/uncertain attempt. Both suppress the `Disconnected` event (`_suppressDisconnectedEvent`), so an actor-requested teardown never surfaces as transport loss.
- Option/message construction extracted to pure internal statics (`BuildConnectOptions`, `BuildSubscribeOptions`, `BuildPublishMessage`) so the profile is unit-testable without a broker.

**Evidence (`SparkplugMqttTransportProfileTests`, broker-free)**
- `BuildConnectOptions_PinsMqtt311`; `_RequestsCleanSessionAndKeepAlive`; `_ConfiguresQoS1NonRetainedNDeathWill` (topic, payload, QoS `AtLeastOnce`, retain `false`); `_WithCredentials_SetsThem` / `_WithoutCredentials_LeavesThemUnset`; `_Tls_EnablesTls` / `_Plain_DoesNotEnableTls`.
- `BuildSubscribeOptions_RequestsExactTopicAtQoS1`.
- `BuildPublishMessage_IsQoS0NonRetained` (topic, QoS `AtMostOnce`, retain `false`, payload).
- `RequireConnectSuccess_*`; `RequireExactNcmdGrant_ExactQoS1_DoesNotThrow`, `_WrongQoS_*` (downgrade + failure), `_WrongTopic_*`, `_WrongGrantCount_*` (0 and 2).

**Pushback (event-suppression / abort via IMqttClient double).** The review offered two acceptable B3 routes — a controlled `IMqttClient` double **or** extracted pure factories. This fold takes the **factories** route (the review's explicit "or"). Consequently the concrete transport's *stateful* event-suppression and abort-publishes-Will behaviors are proven at the **actor** level (`Begin_FailedStep_*` and `Begin_PreAuthoritativeDisconnect_*` assert the abort/dispose path and that a suppressed teardown does not drive rebirth) rather than by a live `IMqttClient` double. If you want a dedicated client-double test of the two `DisconnectedAsync` paths in isolation, that is a clean, low-risk r2 add — flag it and I will fold it.

---

## B4 — Secret-safe connect request; config-time rejection

**Fix.**
- `SparkplugMqttConnectRequest` is a `sealed class` (was a record — the compiler `ToString()` leaked credentials). `ToString()` emits only endpoint, client id, `HasCredentials` (bool), keep-alive, clean-session, Will topic, and Will payload **length** — never username, password, or Will bytes.
- The Will payload is stored as `ImmutableArray<byte>` via a defensive copy (`ImmutableArray.Create(willPayload.Span)`); a caller's later mutation cannot reach the request.
- `KeepAliveSeconds` validated `1..65535` (16-bit MQTT 3.1.1 word) at `Create`.
- Validator: a **present** `ClientId` must be non-blank (`SPARKPLUG.CONFIG_INVALID_CLIENT_ID`; omit → auto-generated); keep-alive upper bound `65535` enforced.
- The bdSeq metric-alias calc is `checked(resolved.AliasMap.Values.Max() + 1UL)` (validates the `ulong` returned across the store interface).

**Evidence**
- `SparkplugMqttConnectRequestTests`: `ToString_NeverEmitsCredentialsOrWillBytes`, `ToString_WithoutCredentials_ReportsHasCredentialsFalse`, `Create_DefensivelyCopiesWillPayload_CallerMutationDoesNotLeak`, `Create_KeepAliveOutOfRange_Throws`.
- `SparkplugSinkConfigurationTests`: `Validate_KeepAliveOutOfRange_Fails` (0, 65536), `Validate_KeepAliveAtMaximum_Succeeds` (65535), `Validate_BlankClientId_Fails` (`""`, `"   "`), `Validate_OmittedClientId_Succeeds`.

---

## B5 — NBIRTH content proven by byte-parity (not call order)

**Fix.** The fake transport captures published payloads (`Published: List<(Topic, Payload)>`). NBIRTH is compared byte-for-byte against an independently-built K2 payload.

**Evidence**
- `Begin_EmptyRoute_PublishesExpectedNbirthBytes` — equals `EncodeNBirth(seq=0, bdSeq=0, bdSeqAlias=1, Clock, manifest.Metrics, manifest.AliasMap)`; proves `seq=0`, same `bdSeq` as the Will, non-zero unique bdSeq alias, `Node Control/Rebirth=false`, control metrics, fixed test-clock timestamp — all via the shared K2 encoder.
- `Begin_PopulatedRoute_PublishesExpectedNbirthBytes` — `bdSeqAlias = max(app aliases)+1 = 3`; exact metric/alias set.
- `Begin_RegistersNDeathWill_WithReservedBdSeq` — Will topic + exact `EncodeNDeath(bdSeq)` bytes.
- Cancellation: `Begin_CancellationAtStep_FaultsAndPromotesNothing` (connect/subscribe/nbirth) → `OperationCanceledException`, no promotion, attempt disposed.

---

## Approved-and-retained (unchanged from the review's "what is approved" list)
`BrokerPort` omitted-vs-explicit via Core `BrokerEndpoint`; actor owns generation policy; no transport reconnect loop; one fresh client per CONNECT; Will + NBIRTH share the reserved `bdSeq`; Will QoS 1 non-retained; DATA/BIRTH QoS 0 non-retained; exact NCMD subscribed before NBIRTH; store injected, never disposed by the actor; manifest/baseline promote only after NBIRTH success; failed attempts retired; deterministic broker-independent fake-transport actor tests; initial Begin does not use the transport-recovery retry budget.

## Open judgment calls surfaced for your ruling
1. **Planning-failure actor test** — argued unreachable with valid Core inputs; covered by birth-layer unit tests + the alias-failure preflight test. (B1)
2. **B3 stateful event-suppression / abort** — proven at the actor level via the factories route (the review's accepted alternative); a dedicated `IMqttClient`-double test is available as an r2 add if you want it. (B3)

Slice 5 remains paused pending this pass (its cursor ack, first-observed rebirth, epoch gating, and `seq` commit all depend on the authority + transport semantics locked here).
