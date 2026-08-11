# Sparkplug B — K3 Session Actor Plan (v1)

**Date:** 2026-07-19
**Author:** Session with Sudhakar
**Status:** DRAFT v1 — awaiting external review pass (plan-trail cadence:
v1 → review → v2 → reality-check → v3 frozen → implement).
**Governing docs:** frozen master plan `2026-07-13-sparkplug-b-sink-plan-v2.3.md`
§8 (K3 line + K3 carry-forward), **ADR-0036** (the runtime spec — K3's home),
**ADR-0035** (scope), K2 handoff `2026-07-19-sparkplug-b-k2-handoff.md`.
**Baseline:** `master` @ `808fae7` (K2 merged; wire layer + Core replay
contracts all present).

---

## 1. Scope — what K3 is

Per master plan v2.3 §8:

> `K3  SparkplugSessionActor (consumes Core context+lifecycle) + bdSeq store +
> aliases + Rebirth NCMD`

K3 is the **stateful MQTT-3.1.1 session actor** — the concrete
`IReplayAwareSinkAdapter` for Sparkplug B. It consumes the K1.3 Core replay
lifecycle and drives the K2 wire factories. Concretely:

1. **`SparkplugSinkAdapter : IReplayAwareSinkAdapter`** — the single-owner
   session actor (ADR-0036 Rule 7). Owns the MQTTnet client, all Sparkplug
   protocol/session transitions, and **serializes every MQTT publish**. Reacts
   to the Core-driven lifecycle (`BeginReplaySessionAsync` → phase-tagged
   `PublishAsync` → `CompleteCatchUpAsync` → Live → `RebirthAsync` →
   `EndSessionAsync`); never derives or advances buffer phase/cursor (Core owns
   those). Protocol state model (ADR-0036 Rule 7):
   `Stopped → LoadingSession → Connecting → SubscribingNCMD → Birthing →
   (Core-driven Replaying/CatchingUp) → Live → Rebirthing → Stopping`.
2. **The birth-then-replay sequence** (ADR-0036 Rule 2): load manifest +
   snapshot → reserve+persist `bdSeq` → build NDEATH Will → CONNECT(clean) →
   SUBSCRIBE exact NCMD → NBIRTH(`seq=0`, current snapshot, `Node
   Control/Rebirth=false`) → historical replay (`is_historical=true`) →
   catch-up final update → Live.
3. **The persistent identity store** — gateway-level Sparkplug identity SQLite
   store (`data/sparkplug/identity-state.db`, per K0 WS5), keyed by
   `broker+group+edge_node`, holding **`bdSeq`** (reserve+commit-before-CONNECT,
   fail-closed on corruption, skip-committed-unused-after-restart) **and** the
   **Edge-Node alias allocator** (canonical-identity-keyed, globally unique per
   node, `Node Control/Rebirth` gets none) in a **separate table**, both under
   the same store. Mirrors Core's `SqliteRouteStore`/`SqliteBuffer` patterns.
4. **`seq`/`bdSeq` lifecycle** (ADR-0036 Rule 3): `seq` = single Edge-Node
   modulo-256 counter, every NBIRTH/NDATA participates, reset to 0 at each
   (re)birth, NDEATH excluded; `bdSeq` increments on **every new CONNECT**,
   same value in that session's Will-NDEATH and NBIRTH, retained across a
   same-session rebirth.
5. **The snapshot→wire mapping** — translate Core's `LatestValueSnapshot`
   (birth/rebirth/cutover state) and `CanonicalDataPoint` batches into K2
   `SparkplugMetricSample`s + the alias map: source-qualified metric naming
   `{SourceInstanceId}/{DeviceId?}/{TagPath}` (ADR-0036 Rule 5), duplicate- and
   reserved-name validation (`bdSeq`/`Node Control/Rebirth` collisions), and the
   manifest = the persisted observed set (never-observed → absent from NBIRTH).
6. **The catch-up final update** (ADR-0036 Rule 2): at `CompleteCatchUpAsync`,
   emit one non-historical latest-value update per metric whose **full
   host-visible state changed since the current birth generation** — value +
   null-state + datatype + quality + quality-reason + acquisition timestamp,
   **not value alone**.
7. **The Rebirth NCMD handler** (ADR-0036 Rule 4): subscribe the **exact**
   `spBv1.0/{group}/NCMD/{edge_node}` at QoS 1 before birth; on `Node
   Control/Rebirth=true` call `IReplaySessionHost.RequestRebirthAsync` (the
   K1.3 reverse seam); ignore any other NCMD metric with a diagnostic + no side
   effect; pause DATA while the new birth is emitted; coalesce repeats.
8. **QoS-0 session-suspect reconnect** (ADR-0036 Rule 1): BIRTH/DATA at QoS 0
   retain=false; on **any observable or uncertain** send error, disconnect and
   establish a **new session** (new `bdSeq`, new CONNECT, new NBIRTH) before
   retrying — preserving `seq` validity — rather than resuming a suspect
   session. `PublishResult.Success` = "completed at the local MQTTnet transport
   boundary with no observable error", never broker receipt.
9. **`SparkplugSinkConfiguration`** + `ValidateConfigAsync` + the advertised
   **`DeliveryCapabilities(SupportsStoreAndForward=true,
   AcknowledgementBoundary=LocalTransport)`** + `CheckHealthAsync` 3-way health
   snapshot.
10. **Epoch/session gating in the actor** (ADR-0036, `IReplayAwareSinkAdapter`
    docs, plan v2.3 §8 K3 carry-forward): the actor records the epoch of its
    **most recent successful** birth as authoritative and rejects/ignores any
    `PublishAsync`/`CompleteCatchUpAsync`/rebirth input whose session **or**
    epoch does not match — a failed NBIRTH must **not** promote the candidate
    epoch.

## 2. Scope — what K3 is NOT

- **No route-validation integration, no license gating, no DI registration
  triad, no module-catalog entry** — **K4**. (Includes the K1.3 follow-up #3,
  production `ISinkReplayCapabilityClassifier` registration.) K3 delivers the
  adapter + identity store + advertised capability; wiring it into config
  apply/validation and the license-gated host composition is K4.
- **No Studio wizard / `AddSparkplugDestination.razor` / edit routing** — **K5**
  (mockup-first, ADR-0035 Rule 7).
- **No material-schema / generation-changing rebirth**
  (`AdvanceGenerationAsync`) — K1.3 follow-up #1, a **post-K3** slice needing a
  new-generation manifest seed Core lacks today.
- **No device-level DBIRTH/DDATA/DDEATH** — ADR-0036 Rule 8, deferred until an
  `IDeviceLifecycleProvider` exists.
- **No clustered/standby lease** — ADR-0036 Rule 7 / K0 WS5: single-owner,
  single-node identity store in v1; the lease is a later capability.
- **No Core changes** unless a genuine gap appears — then stop-and-surface, not
  a quiet Core edit. (K1.3 delivered the seams; the expectation is K3 consumes
  them unchanged.)
- **No broker in unit tests** — the actor is tested against an in-process
  MQTTnet server / injected `IMqttClient` (the MQTT sink's existing test
  pattern); a real-broker/interop pass is the **K6** release gate (ADR-0035
  Open item 4).

## 3. The hard problems (where the risk is)

1. **Session-actor concurrency.** One actor serializes all publishes while Core
   drives lifecycle callbacks from the replay driver AND the broker delivers
   NCMDs asynchronously AND a disconnect fires a reconnect. The publish path,
   the NCMD-receive path, and the reconnect path all touch `seq`/session state.
   Needs one serialization discipline (a single publish/transition lock or a
   mailbox), plus the non-reentrancy rule (the actor must not block on Core
   while Core awaits a publish — ADR-0036 Rule 4/6).
2. **bdSeq crash-safety** (K0 WS5): reserve in a serialized `BEGIN IMMEDIATE`
   transaction that **commits before the value is returned** (before CONNECT
   construction); commit failure throws and prevents CONNECT; corrupt/unreadable
   state fails closed (throws, never resets to 0); a committed-but-unused value
   is skipped after restart, never reused.
3. **Reconnect = new session, not resume.** Unlike the MQTT sink's
   reconnect-in-place, a Sparkplug reconnect must mint a **new `bdSeq`**, send a
   new NBIRTH, and reset `seq=0` — because a rewound/ambiguous `seq` in a live
   session makes a host demand a rebirth (ADR-0036 Rule 1). Reconciling this
   with Core's cursor ownership (Core replays from the buffer after a new birth)
   is the subtle part: **does a transport reconnect surface to Core as a rebirth
   request, or is it actor-internal with Core simply continuing to publish?**
   → open decision §5.1.
4. **The changed-since-birth comparison** (ADR-0036 Rule 2) needs the current
   birth-generation baseline snapshot retained by the actor and compared on full
   host-visible state; a same-session rebirth `10→20→10` must still land `10`
   (the "changes-then-returns" case).
5. **Manifest/alias identity coherence.** Aliases are allocated from the
   persisted per-node allocator keyed by canonical identity; a metric first seen
   after birth is a schema change → controlled rebirth (never an unannounced
   alias). The actor must announce, in NBIRTH, exactly the alias set it will use
   in NDATA (the K2 encoder enforces this as an exact set-match — the actor must
   feed it a coherent pair).
6. **Test determinism without a broker** — driving the full state machine
   (birth, replay, catch-up, rebirth, session-suspect reconnect, NCMD) against
   an in-process MQTTnet server, with no `Thread.Sleep`, is a real test-harness
   design task.

## 4. Proposed slices

| Slice | Content | Exit evidence |
|---|---|---|
| 1 | `SparkplugSinkConfiguration` + `ValidateConfigAsync` + advertised `DeliveryCapabilities` + protocol-state scaffold (no MQTT yet) | config validation unit tests; capability = LocalTransport |
| 2 | Persistent identity store: `bdSeq` reserve/commit-before-return + fail-closed + skip-unused; alias allocator (separate table, canonical-key, node-unique, Rebirth gets none) | crash-injection tests mirroring K0 WS5 (7) + alias-allocation tests |
| 3 | MQTTnet connection lifecycle: CONNECT(clean) + QoS-1 NDEATH Will + exact NCMD subscribe + QoS-0 publish + session-suspect reconnect-as-new-session | in-process MQTTnet server tests: Will registered, NCMD subscribed, reconnect mints new bdSeq |
| 4 | Snapshot→wire mapping + manifest/naming validation + birth sequence (`BeginReplaySessionAsync`) + `seq` allocation | NBIRTH-from-snapshot tests incl. empty route; duplicate/reserved-name rejection |
| 5 | Replay/live publish path (`PublishAsync(context)`) + epoch/session gating + `CompleteCatchUpAsync` changed-since-birth final update | phase-tagged publish tests; stale-epoch rejection; changes-then-returns |
| 6 | Rebirth NCMD handler (subscribe→receive→`RequestRebirthAsync`, coalesce, ignore-others, pause-DATA) + `RebirthAsync` + `EndSessionAsync` graceful NDEATH | NCMD-driven rebirth test; graceful-end test; epoch acceptance tests (plan v2.3 §8 K3 carry-forward) |
| 7 | 3-way health + diagnostics counters + full state-machine/failure-injection test sweep | health snapshot tests; the K3 acceptance matrix green |

Same working style as K1.3/K2: slice-per-commit, external review per slice,
`v3.x` amendments if reality diverges.

## 5. Open decisions to resolve at the review pass (v1 → v2)

1. **Transport reconnect vs. Core rebirth.** When the actor loses the broker and
   must start a new session (new bdSeq/NBIRTH), does it (a) request a rebirth
   from Core via `RequestRebirthAsync` so Core re-drives birth+replay from a
   fresh coherent snapshot, or (b) handle it actor-internally and let Core
   continue publishing into the new session? Proposal: **(a)** — reuse the
   existing coherent-snapshot rebirth path rather than invent a second birth
   path; the actor's "session is suspect" becomes a rebirth request. Needs
   confirming against the `ReplaySessionRebirthHost` epoch semantics (a
   transport reconnect is not a host-commanded rebirth — is `RebirthReason` rich
   enough? it has `HostCommand | SchemaChange | Other`).
2. **Protocol sub-states vs. base `AdapterState`.** ADR-0036 Rule 7's fine
   states (`LoadingSession`/`Connecting`/`SubscribingNCMD`/`Birthing`/
   `Rebirthing`) must map onto the coarse base `AdapterState`
   (Created/Initializing/…/Running/Degraded/…). Proposal: keep the protocol
   state as an **internal** actor field for diagnostics; the base `AdapterState`
   stays the ISinkAdapter contract surface (Running once Live, Degraded on
   session-suspect). Confirm the mapping table in v2.
3. **Coordinated replay-sink hot replacement** (K1.3 follow-up #2). The K2
   handoff tentatively assigned this to K3. It is a large coordinator↔driver
   subsystem. Proposal: **defer to a K3.x follow-up**, keep K1.3's fail-closed
   reject for an in-place replay-sink change in v1; K3 core is the actor +
   identity store + rebirth. Flagging for an explicit call — do not silently
   fold it in.
4. **Identity-store rooting + lifetime.** `data/sparkplug/identity-state.db` —
   confirm the `data/` root resolution (gateway data dir convention) and whether
   the store is one gateway-wide singleton (shared across all Sparkplug
   destinations, keyed rows) or one file per destination. K0 WS5 says
   gateway-level, keyed by identity → **one shared store**. Confirm.
5. **Where the manifest/naming validation runs.** The source-qualified naming +
   duplicate/reserved-name checks (ADR-0036 Rule 5) are a *pre-CONNECT* actor
   concern in K3, but the *route-level* identity/cardinality validation is K4.
   Proposal: the actor validates the **manifest it is about to birth** (fail
   startup on a duplicate/reserved published name) in K3; the *config-time*
   route validation is K4. Confirm the split so a check isn't dropped between
   milestones.
6. **`InitializeAsync`/`StartAsync` vs. `BeginReplaySessionAsync` division.**
   Per `IReplayAwareSinkAdapter` docs: `StartAsync` starts adapter-local
   resources (client, store) but does **not** connect or birth;
   `BeginReplaySessionAsync` connects + births. Confirm the identity-store open
   happens in `StartAsync` and the bdSeq reservation happens in
   `BeginReplaySessionAsync` (it must be per-session, before each CONNECT).

## 6. Exit criteria (K3 gate)

- Solution builds **0 warnings / 0 errors** (warnings-as-errors on the new
  project).
- `SparkplugSinkAdapter` implements `IReplayAwareSinkAdapter`; the full state
  machine (birth → replay → catch-up → live → rebirth → graceful end → session-
  suspect reconnect) is exercised by deterministic tests against an in-process
  MQTTnet server — **no `Thread.Sleep`**, no real broker.
- bdSeq crash-safety proven (the K0 WS5 matrix, now against the production
  store); aliases persist and stay stable across route recreation.
- Epoch/session gating proven against the real actor (the three plan v2.3 §8
  K3 carry-forward cases: stale-session-same-epoch, non-increasing rebirth
  epoch, promotion-only-after-successful-NBIRTH).
- The advertised `DeliveryCapabilities` is `LocalTransport`; NDEATH carries no
  `seq`; reconnect mints a new `bdSeq`.
- No change to `ElpisEdgeConnect.Core` or any existing project; only MQTTnet
  (already a repo dependency) + the K2 SparkplugB project referenced.
- Full unfiltered regression green (Core + Host + Management + SparkplugB); the
  full Management.Tests project run before any PR.
- Plan trail updated; handoff written before sign-off.
- **Still NOT operator-shippable** — the tile is Available only after K5. K3
  completion is a backend milestone (CLAUDE.md §8).

## 7. What K3 explicitly carries forward (so nothing is lost)

- **K4:** route validation (delivery boundary + identity/descriptor uniqueness +
  one-route-per-Edge-Node cardinality), license module `sink-sparkplug-b` +
  catalog tier, DI registration triad, production `ISinkReplayCapabilityClassifier`.
- **K5:** wizard (mockup-first) + edit routing.
- **K6:** broker-in-CI + real Ignition/MQTT-Engine interop (ADR-0035 Open 4).
- **Post-K3:** material-schema generation-changing rebirth; coordinated
  replay-sink hot replacement (pending §5.3); clustered/standby lease.
