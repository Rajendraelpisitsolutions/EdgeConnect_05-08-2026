# Sparkplug B — Technical Spike Task Breakdown (v1)

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** Spike work plan — decisions + executable evidence, **no production
Sparkplug code** (a throwaway NBIRTH/NDATA probe is allowed only to verify
MQTTnet/broker behavior).
**Charter:** `2026-07-13-sparkplug-b-sink-plan-v2.1.md` §5. **ADRs:** 0035, 0036.
**Current plan:** `…-sparkplug-b-sink-plan-v2.3.md` — this doc predates it, so the
"v2.1/v2.2/go-no-go" references below are **historical/superseded**; read them as the
then-current target. WS1/WS2/WS7 are **done**; WS3/WS4/WS5/WS8 are **K0** (see
"K0 status & HARD GATE").

> **Goal of the spike:** convert plan v2.1 into an implementation-ready plan (v2.2)
> by resolving the five gating unknowns — replay context, birth inputs (manifest +
> snapshot), MQTTnet QoS-0 semantics, crash-safe `bdSeq`, and route/identity
> cardinality — and produce a **go/no-go** for the protocol kernel.

## Grounding already established this session (don't re-derive)
- `ReplayCoordinator` (`src/…/Routing/ReplayCoordinator.cs`) tracks only
  `IsDraining` for **post-degradation** drain; **cold-start backlog replay is NOT
  flagged** (`SinkPublisher.BeginDrain` fires on first success *after a failure*).
  No epoch / high-water mark exposed. → the replay-phase signal Sparkplug needs
  does not exist today.
- `IMessageBuffer.DequeueBatchAsync` returns `BufferBatch{FirstSequence,
  LastSequence}`; `RegisterSinkAsync` starts a new sink at the **oldest** held
  sequence (backlog replay mechanism). → **high-water mark H = buffer tail sequence
  at birth**; buffer-seq ≤ H is historical, > H is catch-up/live.
- `SinkPublisher` (`internal sealed`) calls `_sink.PublishAsync(batch, ct)` with **no
  context**; it owns retry + `ReplayCoordinator`. → threading a `PublishContext`
  means RouteWorker → SinkPublisher → sink.
- Manifest candidates: `ISourceAdapter.BrowseTagsAsync` → `TagDefinition{Name, Path,
  ValueType, Unit, Description, Writable}`; route config. Snapshot: `IRouteTap`
  (observational, ADR-0018) exists but **no first-class latest-value cache** — the
  real gap.

---

## WS1 — Replay context propagation  *(critical path; gates the Core seam)*
**Objective:** determine whether the engine can tell a sink "this batch is
Replay / CatchUp / Live" + a high-water mark, or whether we add the optional
`IReplayAwareSinkAdapter`.

- **T1.1** Trace the hot path end to end: `RouteWorker` → `SinkPublisher.
  PublishWithRetryAsync` → `IMessageBuffer.DequeueBatchAsync`/`AckAsync` →
  `ReplayCoordinator`. Produce a **sequence diagram** of cursor + phase transitions.
- **T1.2** Confirm the **cold-start gap**: write a throwaway test that registers a
  fresh sink against a buffer preloaded with backlog and asserts `IsDraining` is
  `false` during initial replay (proves the phase signal is missing on cold start).
- **T1.3** Prototype **high-water-mark capture**: compute H from `BufferBatch.
  LastSequence` + buffer tail (`BufferStats` / an empty-dequeue probe). Show
  buffer-seq ≤ H ⇒ historical.
- **T1.4** Prototype **`PublishContext` threading**: spike an optional
  `IReplayAwareSinkAdapter.PublishAsync(points, PublishContext{Phase, ReplayEpoch,
  HighWaterMark}, ct)`; `SinkPublisher` supplies it only when the sink declares the
  capability; existing sinks unaffected (compile + existing tests green).
- **Evidence:** sequence diagram; cold-start gap test; HWM prototype; a minimal
  `PublishContext` API diff.
- **Exit decision:** *existing APIs sufficient* **or** *exact optional Core
  interface approved* (current expectation: seam needed — cold-start replay is
  unflagged).
- **Effort:** L. **Depends on:** none. **Blocks:** WS7, kernel go/no-go.

**Acceptance criteria (2nd-review tightenings — must hold):**
1. **Atomic watermark.** `H` is captured **once, without advancing the sink cursor**,
   and every record appended afterward has sequence `> H`. An empty-dequeue probe is
   fine *exploratory* only — the spike must decide between a purpose-built op
   (`BeginReplayEpochAsync(sinkId) → {EpochId, InitialCursor, HighWaterMark}`, or at
   minimum a head-sequence read) vs. reusing `GetStatsAsync().TotalEnqueued`
   (`== _head` today). No `GetStats`-then-`Dequeue` race may define correctness.
2. **Batch carries its sequence range.** `PublishContext` includes
   `BatchFirstSequence`/`BatchLastSequence`. A batch straddling `H`
   (`First ≤ H < Last`) must be **split at `H`** in the publisher (preferred) — never
   labeled wholly Replay or wholly CatchUp. `AckAsync(upToSequence)` supports
   acking each sub-range.
3. **All epoch-entry cases defined**, not just fresh-sink-backlog:
   | Situation | Expected initial phase |
   |---|---|
   | Fresh sink, empty buffer | Live |
   | Fresh sink, existing backlog | Replay, captured `H` |
   | Existing sink recovering after failure | Replay/CatchUp under a new epoch |
   | Process restart, persisted cursor (SqliteBuffer) | cursor vs captured `H` |
   | Buffer empties during replay | explicit **barrier**, not merely `IsDraining=false` |
4. **Phase stays protocol-neutral.** Core exposes only `Replay | CatchUp | Live`.
   The Sparkplug actor's states (Connecting/Birthing/Rebirthing/…) stay in the sink;
   do **not** move them into `SinkPublisher`, and the actor must **not** infer buffer
   phase from timestamps — `PublishContext.Phase` is the only phase authority.

## WS2 — Birth inputs: metric manifest + latest-value snapshot  *(critical path)*
**Objective:** guarantee a complete manifest + current value for every metric
**before CONNECT** (first-seen can't build a valid birth).

- **T2.1** Enumerate **manifest** sources and pick one: `BrowseTagsAsync` at birth
  vs. a persisted route manifest vs. config-derived tag set. Prove it yields every
  metric the route will publish (incl. source-qualified naming, ADR-0036 Rule 5).
- **T2.2** Enumerate **latest-value snapshot** candidates and prove completeness for
  a **metric that has not changed recently** (the failure mode of a buffer scan):
  (a) `IRouteTap` current-value view; (b) a new pipeline-fed latest-value cache;
  (c) source re-read at birth. Note P1 (tap is observational) — a snapshot provider
  must not perturb the data path.
- **T2.3** Define the **consistency model**: how snapshot values + timestamps +
  quality + `is_null` are captured coherently relative to H (WS1).
- **Evidence:** a working manifest + snapshot for a small mixed-source route,
  including a stale (unchanged) metric present with its current value.
- **Exit decision:** selected manifest source + selected snapshot source + the
  consistency model.
- **Effort:** L. **Depends on:** WS1 (H). **Blocks:** WS7, kernel go/no-go.
- **STATUS — prototype landed 2026-07-13.** `ILatestValueSnapshotProvider` +
  `InMemoryLatestValueSnapshotProvider` (fed post-transform, latest-wins); **A/B
  completeness test green** (retains a long-unchanged metric after it ages out of
  the buffer; buffer scan yields only the recent one). **13/13 spike tests green.**
  Findings + exit decisions + the restart-coverage open decision:
  `2026-07-13-sparkplug-b-ws2-findings-v1.md`. Residual: manifest is NOT
  config-derived (tag defs are protocol-opaque) → snapshot doubles as observed
  manifest; restart persistence is the v2.2 decision.

**Scope decision (2nd review): a latest-value provider is an ACCEPTABLE addition** if
no complete existing source is proven — but it must be a **protocol-neutral Core
capability**, not Sparkplug-internal:
```
LatestMetricValue(MetricId, ValueType, object? Value, bool IsNull,
                  DateTimeOffset Timestamp, CanonicalQuality Quality,
                  long? RouteBufferSequence)
ILatestValueSnapshotProvider.GetSnapshotAsync(RouteId, ct) → LatestValueSnapshot
```
Constraints: keyed by route + immutable canonical metric identity; **fed from the
canonical routed stream after transforms/enrichment**; version relatable to a
route-buffer sequence; carries value/null/datatype/timestamp/quality; does **not**
perturb source polling or routing; not Sparkplug-specific; **defined behavior across
process restart**. The restart point is decisive — an **in-memory-only** cache only
works once the route has observed the metric this process lifetime. For birth
immediately after restart the provider must be one of: **(1) persisted**,
**(2) seeded via a source snapshot/read**, or **(3) allowed to delay CONNECT until
every manifest metric has been observed** — the spike evaluates all three. A buffer
scan cannot prove completeness; `IRouteTap` is observational and must **not** become
the correctness path; **source re-read is last-choice** (bypasses transforms, can
disagree with the buffered canonical stream, fails when a source is down, adds
source load).

## WS3 — Route cardinality (one route per Edge Node)  *(config-side)*
**Objective:** confirm the v1 one-route-per-Edge-Node constraint is enforceable and
where.

- **T3.1** Exercise two publishers/routes targeting one `broker+group+edge_node`
  descriptor against a single session actor; observe seq/manifest/replay incoherence.
- **T3.2** Locate the enforcement point (config validation — `RoutesApi` /
  config validator / `SparkplugConfigurationValidator`); prototype a typed rejection.
- **Evidence:** failing-config test showing deterministic rejection with a typed error.
- **Exit decision:** confirm one-route restriction for v1 (or design aggregation —
  not expected for v1).
- **Effort:** M. **Depends on:** WS8 (shares identity descriptor). **Blocks:** wizard wiring.

## WS4 — MQTTnet QoS-0 completion semantics  *(transport probe)*
**Objective:** pin the exact meaning of "local send completed" so the delivery
contract (ADR-0036 Rule 1) is honest.

- **T4.1** With MQTTnet 4.3.7.1207 (as the MQTT sink uses), publish QoS-0 and force
  **socket loss before / during / after** the `PublishAsync` call; record what the
  client reports in each case.
- **T4.2** Packet-trace (Wireshark/mosquitto log) if practical to correlate
  local-completion vs. broker receipt; characterize the ambiguity window.
- **Evidence:** a short table: failure timing → observed client result → data
  outcome (lost / delivered / duplicated).
- **Exit decision:** precise documented definition of `PublishResult.Success` at the
  local-transport boundary.
- **Effort:** M. **Depends on:** none. **Blocks:** delivery-contract wording (WS6).

## WS5 — Crash-safe `bdSeq` persistence  *(state store)*
**Objective:** an atomic `bdSeq` reservation that survives process crash at any point.

- **T5.1** Design `SparkplugIdentityStateStore` key `broker+group_id+edge_node_id`;
  choose location under the gateway data root (`/config` sibling; reuse the
  atomic-write pattern from `FileSystemConfigurationStore`).
- **T5.2** Prove **reserve-before-CONNECT**: simulate crash before reservation,
  after reservation/before CONNECT, during CONNECT, after CONNACK; assert a skipped
  value (never a reuse) after each.
- **T5.3** Note the clustered/standby requirement (single ownership / lease) as a
  documented constraint (not built in v1).
- **Evidence:** crash-injection test matrix; the reservation algorithm.
- **Exit decision:** atomic reservation algorithm + state-store key + location.
- **Effort:** M. **Depends on:** none. **Blocks:** kernel.

## WS6 — Delivery capability + route validation  *(small, config-side)*
**Objective:** wire the typed `DeliveryCapabilities` and reject broker-acked
AtLeastOnce on a Sparkplug destination (ADR-0036 Rule 1; CLAUDE.md §3 #12 amended).

- **T6.1** Prototype `DeliveryCapabilities{SupportsStoreAndForward=true,
  AcknowledgementBoundary=LocalTransport, SupportsBrokerAcknowledgedAtLeastOnce=false}`
  on the sink; decide where capabilities surface (extend `SinkCapabilities` vs. a new
  record).
- **T6.2** Add route-validation rejection of `AtLeastOnce`(broker-acked) + Sparkplug
  dest; wire the wizard's delivery-notice wording from WS4.
- **Evidence:** failing-config test; the capability record; UI wording draft.
- **Exit decision:** typed validation behavior + UI wording.
- **Effort:** S. **Depends on:** WS4 (wording). **Blocks:** wizard wiring.

## WS7 — Replay → live cutover algorithm  *(depends on WS1+WS2)*
**Objective:** select and prove the exact Birth→Replay→CatchUp→Live transition
(plan v2.1 §3), including the final-update policy and Rebirth-mid-replay.

**Correction (2nd review): TWO finite watermarks are required.** With continuously
arriving live points, "drain everything after `H` then go Live" may never terminate.
Use `H` = replay-start head and `C` = catch-up cutover head:
```
1. Capture H.
2. Load manifest + starting snapshot.
3. Publish NBIRTH.
4. Drain buffer records ≤ H as is_historical.
5. Atomically capture C (current head).
6. Drain records H < seq ≤ C as is_historical.
7. Fold all values through C into the actor's current-value map.
8. Publish ONE final non-historical latest-value update per changed metric.
9. Enter Live for records > C.
```
The actor derives final state from `starting snapshot + all replay/catch-up points
through C` — so the snapshot provider does **not** need an "as-of C" historical view,
and the transition has a finite end under continuous acquisition.

- **T7.1** Implement the two-watermark (H, C) algorithm against a route with backlog
  **plus continuously arriving live points**; prove finite termination into Live.
- **T7.2** Inject an **NCMD Rebirth mid-replay**; assert DATA pauses, a full new
  birth emits (retaining `bdSeq`), then resume.
- **T7.3** Confirm host-visible current value never steps backward (against the mock
  host subscriber).
- **Evidence:** trace of the full cutover under live load; selected final-update
  policy documented; termination proof.
- **Exit decision:** exact H/C Replay/CatchUp/Live algorithm locked.
- **Effort:** L. **Depends on:** WS1, WS2. **Blocks:** kernel go/no-go.
- **STATUS — prototype landed 2026-07-13.** `ReplayCutoverCoordinator` +
  `ICutoverEmitter`; two-watermark (H, C) birth→replay→catch-up→final-update→live.
  Proven: correct partition/ordering, **finite termination with live data
  remaining**, **no backward step** (host value moved only by non-historical
  Birth/FinalUpdate), and **rebirth mid-replay re-announces + completes**. **15/15
  spike tests green.** Final-update policy chosen (host-safe). Findings:
  `2026-07-13-sparkplug-b-ws7-findings-v1.md`. **Same-owner critical path (WS1→WS2→WS7)
  COMPLETE.**

## WS8 — Session ownership / duplicate identity  *(config-side)*
**Objective:** deterministic single-owner enforcement for one
`broker+group+edge_node` identity.

- **T8.1** Attempt two destinations with the same descriptor; prototype the
  validation/registration rejection (shares the enforcement point with WS3).
- **T8.2** Sketch the single-owner **session-actor** boundary (state model from
  ADR-0036 Rule 7) — enough to confirm ownership is representable, not a full build.
- **T8.3** **MQTT Client-ID uniqueness (added, round-2 review):** validate the MQTT
  **Client ID is unique per broker across ALL active destinations** — Sparkplug *and*
  ordinary MQTT — not only the `broker+group+edge_node` descriptor. Two Edge Nodes (or
  a Sparkplug + an MQTT sink) sharing a Client ID evict each other's sessions even
  though their Sparkplug descriptors differ.
- **Evidence:** duplicate-identity rejection test; **duplicate-Client-ID rejection
  test**; actor state-model sketch.
- **Exit decision:** deterministic rejection confirmed (or coordination design).
- **Effort:** M. **Depends on:** none. **Blocks:** WS3 (shared enforcement).

---

## Ownership & parallelization (2nd-review decision)
- **WS1 + WS2 + WS7 stay under ONE design owner** — replay watermarking, snapshot
  consistency, and cutover semantics are too tightly coupled to design independently
  without incompatible assumptions. **WS1 prototype landed 2026-07-13** —
  `IReplayBoundaryProvider` + `PublishContext`/`ReplayPhase` + `IReplayAwareSinkAdapter`
  + `ReplayAwareSinkPublisher`, `IMessageBuffer` untouched. **10/10 spike tests +
  978/978 Core.Tests regression green.** See `2026-07-13-sparkplug-b-ws1-findings-v1.md`
  §9–§10. Remaining: SqliteBuffer boundary parity (deferred to v2.2/kernel).
- **Chipped as independent tracks** (task chips created 2026-07-13):
  **WS4** (MQTTnet QoS-0), **WS5** (bdSeq persistence), **WS3+WS8 together**
  (identity + cardinality share one validation boundary → one owner).
- **WS6** (delivery capability) is small and follows WS4's wording.

### K0 status & HARD GATE (2026-07-13)
**K0 ACCEPTED (2026-07-13)** — all three tracks reviewed and accepted (17 green tests):
WS4 QoS-0 completion boundary (deterministic black-hole-relay probe), WS5 crash-safe
bdSeq in a dedicated gateway-level SQLite identity store, WS3+WS8 route-cardinality/
identity/Client-ID validation (normalized endpoint, active-state, route-level buffer,
MQTT-family scope). See `2026-07-13-sparkplug-b-k0-findings-v1.md`. Docs PR #178 merged;
K0 record PR #179 merged. **K1 gate released** — hardening carried to K1/K3/K4.

These three chipped tracks **are K0** (plan v2.3 §8). They ran **in parallel** —
each owner reads **plan v2.3** (`…-sparkplug-b-sink-plan-v2.3.md`) + this doc's
workstream section.

**HARD GATE — the following stay BLOCKED until (a) the PR #177 review accepts plan
v2.3 AND (b) all three K0 exits are recorded:**
- K1 Core seam promotion + public-API finalization
- RouteWorker production integration (the replay-aware route path)
- Persisted atomic snapshot implementation
- Sparkplug (`Sinks.SparkplugB`) assembly implementation

**Sequence:** re-request PR review + run K0 in parallel → accept review + lock K0
evidence → **close PR #177 without merging** (it is evidence-only) → cut a **fresh K1
production branch from current `master`**. PR #177 (branch `feat/sparkplug-b-ws1-spike`)
never becomes production.

## Sequencing
1. **WS1 — DONE** (prototype + evidence, committed). **Parallel (chipped):** WS4, WS5, WS3+WS8.
2. **After WS1:** WS2 (needs H) — same owner.
3. **After WS1+WS2:** WS7 (cutover) — same owner.
4. **After WS4:** WS6.
5. Static wizard mockup runs in parallel throughout (non-wired).

## Spike deliverables (gate to v2.2 + kernel go/no-go)
1. Code-path + sequence diagram (WS1).
2. Prototype tests: replay context (WS1), MQTTnet QoS-0 (WS4), crash-safe bdSeq (WS5).
3. Selected manifest + snapshot design (WS2).
4. Route-cardinality + session-ownership decision (WS3/WS8).
5. Concrete `PublishContext` / `DeliveryCapabilities` API proposals (WS1/WS6).
6. Exact cutover algorithm (WS7).
7. **Plan v2.2** + any final ADR corrections.
8. **Go/no-go** recommendation for protocol-kernel implementation.

## Notes / flags
- **Highest-risk exits:** WS1 (Core seam) and WS2 (snapshot source). If WS2 finds no
  clean snapshot source, birth correctness needs a new latest-value provider — a
  scope addition to surface before kernel.
- **CI intersection:** WS4/WS5/WS7 want a broker; the repo has no CI yet — spike runs
  locally against Mosquitto; broker-in-CI ties to the deferred "stand up CI" task.
- These workstreams are sized to be spun into separate sessions (spawn_task) if you
  want to parallelize; this doc is their companion (per `feedback_chip_markdown_redundancy`).
