# M.2c — Live Tag Watch (v2 plan, LOCKED after ChatGPT review)

**Status:** v2 — LOCKED. Folds in 17 review items: 14 accept as-is, 2 accept with refinement, 1 reject of v1 recommendation (Q-V1.6 — the only architectural inconsistency in v1). Per ChatGPT round-1 verdict: "no further review needed before drafting."
**Date:** 2026-05-21
**Predecessor:** [v1 (open questions)](2026-05-21-m2c-live-tag-watch-plan.md)
**Predecessor (locked roadmap):** [Phase 2 wrap-up roadmap v2](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.6 + [v2.1](2026-05-21-phase2-wrapup-roadmap-v2.1.md) §1.2 + [v2.2](2026-05-21-phase2-wrapup-roadmap-v2.2.md) §1.3 + [v2.3](2026-05-21-phase2-wrapup-roadmap-v2.3.md) §1.2.
**Architectural authority:** [`docs/platform-principles.md`](../platform-principles.md) — **P1 Runtime Tap is strictly observational**.
**Estimated size:** 2-3 weeks of focused work (unchanged from v1; v2 additions tighten correctness without increasing implementation surface meaningfully).
**Test baseline:** 2263 passing across the solution today. Target after M.2c: ~2435 with ~+115 tests (v1 target was +85; v2 adds ~30 for the new locks).

---

## 0. What v2 changed from v1

ChatGPT review surfaced 17 distinct items across 8 open-question verdicts + 8 important improvements + 1 architectural caution. **14 accepted as-is, 2 accepted with refinement, 1 rejected v1's own recommendation.** No items rejected outright.

### Verdicts on v1's 9 open questions

| Question | Verdict | Where in v2 |
|---|---|---|
| Q24 — Supervisor injection point | ✅ Accept (v1 §7.1 resolution stands) | §7 (unchanged) |
| Q25 / Q-V1.3 — 15s heartbeat | ✅ Accept — locked | §4.4, §12.3 |
| Q-V1.1 — Single `event: tag-value` frame | ✅ Accept — locked | §6 wire shape |
| Q-V1.2 — Hash-set membership | ✅ Accept — locked | §4.2 |
| Q-V1.4 — Native JSON value + `valueType` | ✅ Accept — locked | §6 wire shape |
| Q-V1.5 — Subscribe-time tag validation | ✅ Accept with location nuance — **Management/API layer, NOT in Core `IRuntimeTap`** | §6.2 |
| **Q-V1.6 — Wall-clock retention** | 🔴 **REJECT v1 recommendation. Enforce real 5-min eviction via lazy prune.** | §4.2.1 (NEW major) |
| Q-V1.7 — Catalog provider | ✅ Accept with scope tightening — **per source instance, Management-side** | §6.3 (NEW) |
| Q-V1.8 — Subtle reconnect banner | ✅ Accept — locked | §6.4 |

### Verdicts on v1's 8 important-improvement items

| # | Item | Verdict | Where in v2 |
|---|---|---|---|
| 1 | Truly zero-cost no-subscriber path | 🔧 Make explicit — `HasSubscribers` on `IRuntimeTap` contract | §3.1.1 (NEW) |
| 2 | Snapshot-on-subscribe | ✅ Accept — biggest UX addition | §4.2.2 (NEW major) |
| 3 | Sequence number scope | ✅ Accept — per source instance, monotonic | §4.2.3 (NEW) |
| 4 | Source-first registry traversal | 🔧 Make explicit (already implicit in v1) | §4.2.4 (NEW) |
| 5 | License-disabled three-layer lock | ✅ Accept with nuance — **API returns 403, not 404** | §4.5 (NEW) |
| 6 | Catalog per source instance, not per protocol | ✅ Accept — Modbus is the forcing function | §6.3 (NEW) |
| 7 | Stale rule with explicit floor | ✅ Accept — `max(2×PollIntervalMs, 5s)` against `IngestTimestamp` | §4.6 (NEW) |
| 8 | SSE endpoint limits | ✅ Accept — max 50 tags/session, max 10 concurrent sessions | §4.7 (NEW) |

### Architectural caution

| Item | Verdict | Where in v2 |
|---|---|---|
| No server-side query language | ✅ Accept — added to anti-scope | §2.1 (6th bullet) |

### Critical wording correction (round-1 user feedback)

The phrase "zero-cost when no subscribers exist" — repeated 5 times in v1 — was technically accurate for the event-emission path only. The snapshot-on-subscribe addition (Imp #2) introduces an **always-on latest-value cache**. v2 carefully separates the two paths in every relevant section:

> **Event-emission cost is zero when no subscribers exist for that source.** The bounded latest-value cache runs unconditionally with O(1) per-publish overhead. Two budgets, two benchmarks, two acceptance gates.

This wording correction propagates to: §3 (P1 enforcement table), §4.4 (performance budget), §8 (benchmark methodology), §11 (test names), §14 (DoD).

---

## 1. Goal

Deliver an operator-facing **Live Tag Watch** page in Studio that lets a commissioning engineer or factory-floor supervisor select a single configured source and watch a chosen subset of its canonical tags update in real time. The page surfaces value, quality (Good/Bad/Uncertain), staleness, and source-side timestamps. The subsystem is built on a **Runtime Tap** — a strictly observational read-only side-channel governed by P1. **Watch sessions** are operator-initiated, browser-bound, and transient.

**v2 addition — snapshot-on-subscribe:** when an operator opens the Watch page and selects tags, the page immediately renders the latest known value per selected tag (sourced from the always-on latest-value cache). The operator does not wait for the next poll cycle to see initial data. New points stream in over SSE on top of that initial snapshot.

This is the load-bearing commissioning workflow for the 100-CNC customer (per `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md`).

---

## 2. Architectural framing — what this is / what this is NOT

### 2.1 The 6 anti-scope locks (was 5 in v1; +1 from review architectural caution)

Pinned at the top because they are the single biggest execution risk. Any implementation drift toward one is a **pause-and-surface** event, not a discretionary decision.

1. **NOT a historian.** Persistence is the Phase 5 historian milestone. Bounded per-tag ring in-memory only; dies with the Watch session or sooner on bounded overflow. No SQLite, no disk, no resume-from-cursor.
2. **NOT a streaming platform.** Multi-consumer fanout, durable subscriptions, replay semantics — all out. A Watch session is one browser, one source, one filter, one server connection.
3. **NOT a diagnostics bus.** `/diagnostics` (M.1c.2) surfaces fault registry + audit chain + event timeline; M.2c is per-tag live values for an operator-driven session. Zero shared state.
4. **NOT an analytics feed.** No aggregation, no statistical summarisation (min/max/avg over windows), no derived signals, no rate-of-change calculations.
5. **NOT a support telemetry framework.** Operator-initiated, browser-bound, transient. Closing the browser ends the session.
6. **NOT a server-side query language (NEW v2 lock — architectural caution).** No `WHERE`/`SELECT`/`JOIN` over the tap stream. No expression engine. No filter syntax beyond literal tag-path set membership. Wildcards, regex, prefix patterns, computed filters — all out of v1 forever. If filtering needs grow, the answer is per-source `ICanonicalCatalogProvider` evolution (per Imp #6), not a query language.

If implementation slides toward any of these, **stop and report.** The right answer is almost always "defer until a dedicated future milestone names that capability."

### 2.2 What this is

A protocol-agnostic, observational side-channel + a thin Studio page that consumes it. Four layers (was three in v1; v2 adds the catalog provider):

- **Core contract** (`ElpisEdgeConnect.Core.Diagnostics.IRuntimeTap`): per-source publish + subscribe seam over `CanonicalDataPoint`. Lives in Core.
- **Host wiring** (`SourceSupervisor` edit): one non-blocking `TryPublish` call per emitted point per §7.
- **Management catalog layer** (NEW v2 — Imp #6): `ICanonicalCatalogProvider` returns the canonical tag-path list for a source instance. Lives in Management layer (NOT Core) so per-source-instance configuration shape stays Management-bound.
- **Management presentation** (SSE endpoint + Razor page): `/api/v1/live-tags` and `LiveTagWatch.razor`.

### 2.3 Locked architectural invariants

Unchanged from v1 §2.3:
- Protocol-agnostic Core.
- Canonical data model.
- Per-adapter isolation.
- No AI in the data path.

---

## 3. P1 as the architectural authority

This section cites P1 verbatim. M.2c enforces P1; it does NOT extend, parallel, or reinterpret P1.

### 3.1 The P1 principle (quoted)

> The Runtime Tap subsystem is a non-intrusive observation layer over the deterministic runtime data path. **Subscribers can READ; nothing in the runtime path can READ from subscribers.**

#### 3.1.1 P1 rule 1 — corrected wording (NEW v2 nuance per user feedback)

v1 repeated "zero-cost when no subscribers exist" 5 times. After v2's snapshot-on-subscribe addition (§4.2.2), that wording is misleading — the latest-value cache runs unconditionally. **v2's precise wording:**

> **Event-emission cost is zero when no subscribers exist for that source.** The bounded latest-value cache runs unconditionally with O(1) per-publish overhead.

Two distinct paths in `RuntimeTap.TryPublish`:

| Path | Subscriber-count gated? | Per-call cost |
|---|---|---|
| **Event-emission path** (publish to subscriber channels + filter + ring write) | Yes — short-circuits when `HasSubscribers(sourceId) == false` | Zero when gated; bounded when active |
| **Latest-value cache path** (per-source `Dictionary<tagPath, CanonicalDataPoint>` write) | NO — always runs | O(1) dictionary update; ~540 writes/sec/gateway at 100-CNC scale |

The latest-value cache adds a measured but bounded cost — explicitly budgeted at <0.1% CPU per §8.2 measurement. The benchmark in §8 separates the two paths so future regressions land on the right side.

### 3.2 P1's 5 enforcement-in-practice rules — how M.2c satisfies each (v2 updated)

| # | P1 rule | M.2c implementation satisfies it because |
|---|---|---|
| 1 | Tap publication has **zero event-emission cost when no subscribers exist** (latest-value cache cost is bounded, always-on, separately budgeted) | `RuntimeTap.TryPublish` short-circuits the event-emission path before any `Channel<T>.Writer.TryWrite` when `HasSubscribers(sourceId)` returns false. Latest-value cache write is O(1) and always runs. |
| 2 | Subscriber backpressure is **isolated** — slow subscriber drops own ring entries before slowing the publisher | Per-subscriber bounded ring (≤100 values AND ≤5 min, **enforced by lazy prune per §4.2.1**). Overflow drops oldest entry on the subscriber's ring — never blocks the supervisor. |
| 3 | No data-path code reads any subscriber state. **Publisher-only from runtime's perspective** | `SourceSupervisor.RunPollLoopAsync` calls `_runtimeTap.TryPublish(sourceId, point)`; never reads anything back. The supervisor has no knowledge of subscriber count, identity, or filter set. |
| 4 | License gating can disable the tap entirely. **API returns 403, not 404 (v2 nuance per Imp #5).** | When `live-tag-watch` module is absent, DI binds `NullRuntimeTap` (no-op event emission, no-op cache writes). SSE endpoint returns 403 with `{"error":"feature-disabled","module":"live-tag-watch"}` body. Supervisor's hot loop is byte-identical with vs without the gate. |
| 5 | **Deterministic replay** — recorded session re-played with tap on vs tap off produces byte-identical canonical points | Deterministic-replay test (test list §11): record a 60-second canonical stream with tap on; replay with tap off; assert byte-identical sequences. The test fails if any tap code path can mutate a `CanonicalDataPoint`. |

### 3.3 P1's 4 explicit anti-patterns ruled out — M.2c restates them

Unchanged from v1 §3.3:
- No "trace mode" that mutates pipeline behaviour when tap is active.
- No "adaptive sampling" that throttles based on tap consumer count.
- No tap-emitted event affecting audit-chain content.
- No sink-delivery decision (retry, drop, requeue) influenced by tap subscriber state.

---

## 4. M.2c-specific implementation invariants (extending P1)

### 4.1 Allowed / Forbidden invariants

Unchanged from v1 §4.1. Reproduced for traceability:

| Allowed | Forbidden |
|---|---|
| Observational runtime stream | NO runtime mutation |
| Bounded retention (≤100 values per tag AND ≤5 min per tag — **lazy-prune enforced, v2 §4.2.1**) | NO historian semantics |
| Transient Watch sessions | NO durable subscriptions |
| Sampled diagnostics, server-side filtering by canonical tag path | NO replay pipeline |
| Per-route + per-source introspection | NO cross-route orchestration |
| Performance budget: ≤1% CPU event-emission overhead (subscriber-active), zero when zero subscribers; cache budget <0.1% CPU always | NO write-back to data path |
| Read-only side-channel via `Channel<T>` with bounded buffer, dropped-oldest on overflow | NO blocking the supervisor's hot loop |
| **No server-side query language (v2 §2.1.6)** | **No filter syntax beyond literal tag-path set membership** |

### 4.2 Retention bounds — both enforced (v2 corrects v1 §4.2)

#### 4.2.1 Lazy-prune retention (NEW v2 — resolves Q-V1.6 architectural inconsistency)

v1 §4.2 locked "≤100 values OR ≤5 minutes, whichever is smaller" then Q-V1.6 silently deferred the 5-minute bound to the rendering layer. **That violated the stated invariant. v2 rejects v1's recommendation and enforces both bounds in `RuntimeTap`.**

**Lock — lazy prune.** No background timer; pruning happens opportunistically:

1. **On `TryPublish`** to a subscriber's ring: before enqueuing the new point, drop entries from the head of the ring whose `IngestTimestamp` is older than 5 minutes relative to the new point's timestamp.
2. **On `Reader.ReadAsync`** by the subscriber: skip-and-evict entries older than 5 minutes before yielding the next one. (Defends against the case where a subscriber is slow to consume.)
3. **On `Subscribe`** snapshot seeding (§4.2.2): filter the latest-value cache entries by age too — entries older than 5 minutes are excluded from the snapshot.

Cost: one timestamp compare on the head of the ring per operation. The ring is a `Channel<T>` underneath — peeking the head is O(1). No background timer, no allocation, no thread-pool work.

**Test coverage** (§11.1):
- `Publish_DropsRingEntriesOlderThanFiveMinutes_BeforeEnqueue`
- `Read_SkipsAndEvictsEntriesOlderThanFiveMinutes`
- `Subscribe_SnapshotExcludesEntriesOlderThanFiveMinutes`

The "5 minutes" threshold is a constant per v2 lock; configurability is explicitly out-of-scope. Operators wanting longer retention use the Phase 5 historian.

#### 4.2.2 Snapshot-on-subscribe semantics (NEW v2 — Imp #2, biggest UX addition)

When an operator opens the Watch page and the SSE endpoint subscribes to `IRuntimeTap`, the subscription emits the **latest known value per selected tag** as the first event(s) on the stream, BEFORE the next live publish arrives. This makes the commissioning workflow immediate — operator sees current state, not an empty table.

**Data source — always-on latest-value cache:**

`RuntimeTap` maintains a per-source `Dictionary<canonicalTagPath, CanonicalDataPoint>` ("latest-value cache"). On every `TryPublish`, the cache is updated (O(1) dictionary write). The cache:

- Always populates, regardless of subscriber count. (This is the cost reified in §3.1.1's P1-rule-1 wording correction.)
- Is bounded **per source** by the number of distinct canonical tags emitted (~16 tags/source × 100 sources × ~200 bytes/point = ~320 KB total per gateway — well within budget).
- Entries older than 5 minutes are NOT served on snapshot (lazy-prune §4.2.1 rule 3).
- Is cleared per source on supervisor restart (acceptable — Watch sessions are transient).

**Snapshot semantics — single subscriber:**

When `Subscribe(sourceInstanceId, IReadOnlySet<string> tagPaths)` is called:

1. The subscriber's channel is constructed.
2. The latest-value cache is looked up: for each tag in `tagPaths`, the latest cached point (if any, and ≤5 min old) is yielded as the first emission(s) on the subscriber's channel — in canonical-tag-path sort order (deterministic; per Imp #4 source-first registry traversal).
3. The subscriber is registered in the per-source subscriber list.
4. Subsequent live publishes flow normally.

**Snapshot semantics — multi-subscriber to same source:**

Each subscriber gets its own independent snapshot at its own subscribe time — no shared state, no replay across subscribers.

**SSE wire format for snapshot emissions:**

Each snapshot emission uses the same `event: tag-value` frame as live emissions (v1 Q-V1.1 lock), but the JSON payload includes a `"snapshot": true` discriminator field so client-side JS can distinguish initial render from live updates. Live publishes carry `"snapshot": false` (or absent — client treats missing as false).

**Test coverage** (§11.1):
- `Subscribe_EmitsSnapshotPerSelectedTag_FromLatestValueCache`
- `Subscribe_SnapshotOrderIsCanonicalTagPathAscending`
- `Subscribe_OmitsSnapshotEntries_OlderThanFiveMinutes`
- `Subscribe_NoSnapshotEntries_WhenSourceNeverEmittedThoseTags`
- `Subscribe_TwoSubscribersToSameSource_GetIndependentSnapshots`

#### 4.2.3 Sequence number scope (NEW v2 — Imp #3)

**Lock: sequence number is monotonically increasing PER SOURCE INSTANCE.**

`RuntimeTap` maintains `Dictionary<sourceInstanceId, long _sequenceCounter>` and increments on each successful publish before the cache write + subscriber fan-out. The sequence number is included in every emitted `LiveTagEventDto`.

Properties:

- Per source — different sources have independent counters.
- Monotonic within a source — gaps detectable by the subscriber.
- Reset on supervisor restart (or `IRuntimeTap` recomposition) — Watch sessions are transient; this is acceptable.
- Snapshot emissions carry their original publish-time sequence number (so the client can identify the snapshot relative to subsequent live emissions).
- NOT used for replay, ordering, or storage — purely a diagnostic field for the Watch page.

#### 4.2.4 Source-first registry traversal (NEW v2 — Imp #4, makes v1's implicit lock explicit)

v1 §11.1 implied per-source registry isolation via the test `Subscribe_DifferentSourcesIsolated_PublishToAOnlyReachesSubscribersOfA`. v2 makes the traversal order **explicit at the contract level** to prevent future implementations from regressing to "iterate all subscribers, check each subscriber's source filter."

**Lock — `RuntimeTap` internal structure:**

```
Dictionary<sourceInstanceId, RuntimeTapSourceEntry>
  where RuntimeTapSourceEntry = {
    long _sequenceCounter,
    Dictionary<canonicalTagPath, CanonicalDataPoint> _latestValueCache,
    List<RuntimeTapSubscription> _subscribers,
  }
```

`TryPublish(sourceId, point)` traversal:

1. Dictionary lookup → `RuntimeTapSourceEntry` for `sourceId` (single hash lookup, O(1)).
2. Increment sequence counter on the entry (atomic — `Interlocked.Increment`).
3. Update `_latestValueCache[point.TagPath]` (O(1)).
4. Check `_subscribers.Count == 0` → if zero, return early (P1 rule 1 enforcement).
5. Otherwise iterate ONLY subscribers under this source; for each, check tag-path hash-set membership (O(1) per subscriber).

At 100 sources × 100 subscribers fleet-wide hypothetical (extreme upper bound), iteration cost stays O(subscribers-of-this-source) not O(total-subscribers). Critical for the performance budget.

### 4.3 Subscription scope

Unchanged from v1 §4.3 except where tightened by §4.7 below:
- One source per Watch session.
- Multi-tag within the source.
- Server-side filtering (hash-set membership per Q-V1.2 lock).
- Transient — session lifecycle = browser lifecycle.

### 4.4 Performance budget — measurement methodology (v2 extended)

The ≤1% CPU constraint is split into two separately-measured paths per the §3.1.1 wording correction:

**Path A — Event-emission overhead** (the subscriber-count-gated path):
- Target: ≤1% CPU delta vs `NullRuntimeTap` baseline when **subscriber count is zero** for all sources.
- Target: ≤5% CPU delta vs baseline when **one subscriber is active** with 30 tags subscribed.
- Allocation: zero when subscriber count is zero (the short-circuit is the fast path).

**Path B — Latest-value cache overhead** (the always-on path):
- Target: <0.1% CPU delta vs `NullRuntimeTap` baseline at 540 pts/sec/gateway.
- Target: O(1) per publish — single dictionary update, no allocation per publish (the dictionary entry already exists after the first emission of each tag; subsequent emissions update the value reference).
- Memory ceiling: 100 sources × ~16 tags × ~200 bytes = ~320 KB total per gateway. Acceptance gate: <1 MB measured.

**Subscriber-active overhead is measured separately for Path A** because it dominates and is the operator-visible cost. Path B remains bounded regardless of subscriber state.

**Heartbeat cadence:** 15 seconds (Q-V1.3 / Q25 lock).

### 4.5 License-disabled three-layer lock (NEW v2 — Imp #5)

Locked behavior when the `live-tag-watch` license module is absent:

| Layer | Behavior |
|---|---|
| **Runtime** | DI binds `NullRuntimeTap` (no event emission, no cache writes). Supervisor's hot loop byte-identical to with-license case. Verified by code review + a parametrised supervisor test. |
| **API** | `GET /api/v1/live-tags` returns **HTTP 403** (was 404 in v1; v2 corrects per Imp #5 nuance). Body: `{"error":"feature-disabled","module":"live-tag-watch"}`. NOT 404 because the resource exists; it's permission-denied. |
| **UI** | The Live Tag Watch nav link in the Studio chrome is hidden (not just disabled) when the license is absent. The page route itself, if reached via direct URL, renders a friendly "Feature not licensed — contact your Elpis support representative" message and does not attempt the SSE subscription. |

All three locks tested:
- `RuntimeTapBenchmarks.cs` — supervisor parametrised over `RuntimeTap` and `NullRuntimeTap`, asserts byte-identical canonical-point flow.
- `LiveTagsApiTests.cs.Sse_LicenseDisabled_Returns403_WithFeatureDisabledBody`.
- `LiveTagWatchModelTests.cs.NavigationLinkHidden_WhenLicenseAbsent`.

### 4.6 Stale rule (NEW v2 — Imp #7)

v1 §11.5 mentioned "stale flag when 2× poll interval exceeded" but didn't pin the exact rule. v2 locks:

```
stale = (now - point.IngestTimestamp) > max(2 × source.PollIntervalMs, 5000ms)
```

| Element | Lock |
|---|---|
| Timestamp basis | `point.IngestTimestamp` (gateway-side, monotonic) — NOT `point.DeviceTimestamp` (CNC-side, may be skewed or absent on some protocols). |
| "Now" | `DateTime.UtcNow` invariant culture. |
| Poll-interval source | The source's currently-applied `PollIntervalMs` from the active gateway configuration. Not from the wizard form — the *applied* config. |
| Floor | 5000 ms. Avoids false-positive stale flags on aggressive polling configurations (e.g., a 1000 ms poll source would otherwise stale-flag at 2000 ms which is too aggressive for the UI tick cadence). |
| Recomputation cadence | UI tick at 1 Hz (Razor timer). Stale flag recomputed per-tag per tick — cheap; no per-publish work. |

**Test coverage** (§11.5):
- `Model_ComputesStale_AgainstIngestTimestamp_NotDeviceTimestamp`
- `Model_StaleFloor_Is5000ms_RegardlessOfPollInterval`
- `Model_StaleThreshold_Uses2xPollIntervalWhenAbove5000ms`

### 4.7 SSE endpoint limits (NEW v2 — Imp #8)

To prevent Watch from accidentally becoming streaming infrastructure:

| Limit | Lock |
|---|---|
| Max tags per Watch session | **50.** Subscribe with more → 400 Bad Request with `{"error":"too-many-tags","limit":50}`. Realistic operator workflows watch 5-15 tags. |
| Max concurrent Watch sessions per gateway | **10.** 11th concurrent subscription → 429 Too Many Requests with `{"error":"too-many-concurrent-sessions","limit":10,"retryAfter":"30"}`. Operators close stale tabs; the limit forces hygiene at v1 scale. |
| Heartbeat cadence | **15 seconds.** Comment-line `: heartbeat\n\n` per Q-V1.3. |
| Disconnect cleanup window | **≤15 seconds** of TCP close (one heartbeat period). After cleanup, the subscriber slot is reclaimed and `_subscribers.Count` decrements. |

**Test coverage** (§11.4):
- `Sse_TooManyTags_Returns400_WithTooManyTagsError`
- `Sse_TooManyConcurrentSessions_Returns429_WithRetryAfter`
- `Sse_DisconnectCleanup_CompletesWithinHeartbeatPeriod`

---

## 5. Locked inputs from the wrap-up roadmap

Unchanged from v1 §5. All 7 roadmap §3.6.3 verdicts (SSE, per-source bounded ring, Channel<T> tap, single-source minimum-viable, server-side filtering, defer auth to Phase 4, no historical persistence) carry as locked.

---

## 6. Deliverables (v2 extended)

Adapted from v1 §6 with the new components for snapshot/cache/catalog/limits.

| File / component | Status | Notes |
|---|---|---|
| `src/ElpisEdgeConnect.Core/Diagnostics/IRuntimeTap.cs` | new | Public contract. **v2 additions:** `bool HasSubscribers(string sourceInstanceId)` property (Imp #1 — promotes the short-circuit predicate to the contract); explicit snapshot semantics in `Subscribe` doc. XML doc cites P1 + §3.1.1 wording correction. |
| `src/ElpisEdgeConnect.Core/Diagnostics/IRuntimeTapSubscription.cs` | new | Per-subscription handle. `ChannelReader<CanonicalDataPoint> Reader { get; }` + `IAsyncDisposable`. |
| `src/ElpisEdgeConnect.Core/Diagnostics/RuntimeTap.cs` | new | Default implementation. **v2 internals:** source-first registry structure (§4.2.4); always-on latest-value cache (§4.2.2); per-source monotonic sequence counter (§4.2.3); lazy-prune retention (§4.2.1). Sealed. |
| `src/ElpisEdgeConnect.Core/Diagnostics/NullRuntimeTap.cs` | new | License-gated no-op. `TryPublish` is empty; `HasSubscribers` returns false; `Subscribe` returns a zero-emission subscription. |
| `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` | edit | Inject `IRuntimeTap` via constructor. Add the single non-blocking `_runtimeTap.TryPublish(adapter.InstanceId, point)` call at the §7.1 injection point. |
| `src/ElpisEdgeConnect.Host/CompositionRoot.cs` | edit | DI registration: `RuntimeTap` when license enabled; `NullRuntimeTap` otherwise. Singleton. |
| `src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs` | edit | Add `LiveTagWatch = "live-tag-watch"` const. |
| `src/ElpisEdgeConnect.Management/Api/LiveTagsApi.cs` | new | SSE endpoint at `GET /api/v1/live-tags`. **v2 additions:** subscribe-time tag validation via `ICanonicalCatalogProvider` (Q-V1.5); 403 license-disabled (Imp #5); 400 too-many-tags + 429 too-many-sessions (Imp #8); 15s heartbeat (Q-V1.3). |
| `src/ElpisEdgeConnect.Management/Api/LiveTagEventDto.cs` | new | Wire DTO: `{ tagPath, value, valueType, quality, deviceTimestamp, ingestTimestamp, sequenceNumber, snapshot }`. JSON-encoded inside each SSE `data:` line. **v2 additions:** `sequenceNumber` (Imp #3), `snapshot` bool (Imp #2). |
| **`src/ElpisEdgeConnect.Management/Diagnostics/ICanonicalCatalogProvider.cs`** | **new (Imp #6)** | Per-source-instance catalog surface. `IReadOnlyList<CanonicalTagDescriptor> GetCatalog(string sourceInstanceId)`. Implementations: `Focas2CanonicalCatalogProvider` (consults `Focas2TagMap` ∩ source's configured `DataPoints`); `BrotherHttpCanonicalCatalogProvider` (consults `BrotherTagMap` ∩ filter); `ModbusTcpCanonicalCatalogProvider` (consults source's configured `Connection.Tags[]` directly — no static map). DI-resolved per `ProtocolName`. |
| **`src/ElpisEdgeConnect.Management/Diagnostics/CanonicalTagDescriptor.cs`** | **new (Imp #6)** | Record: `{ TagPath, ValueType, Unit, Description }`. Mirrors the structurally-pure tag-map entry but Management-side. |
| `src/ElpisEdgeConnect.Management/Components/Pages/LiveTagWatch.razor` | new | Operator-facing page. Source picker (single-select); tag-path multi-select **driven by `ICanonicalCatalogProvider` per selected source** (Imp #6); value table with quality + stale columns; auto-reconnect on SSE disconnect with subtle banner (Q-V1.8). |
| `src/ElpisEdgeConnect.Management/Components/Pages/LiveTagWatchModel.cs` | new | POCO view-model. **v2 additions:** snapshot-vs-live discriminator (Imp #2); stale rule per §4.6; sequence-number gap detection (logs gap to console for now — UI surfacing deferred to v1.1). |
| `tests/ElpisEdgeConnect.Core.Tests/Diagnostics/RuntimeTapTests.cs` | new | ~40 tests (was ~25 in v1; +15 for cache/snapshot/sequence/prune/HasSubscribers). |
| `tests/ElpisEdgeConnect.Core.Tests/Diagnostics/RuntimeTapDeterministicReplayTests.cs` | new | The single P1-rule-5 test — byte-identical canonical-point sequences with tap on vs off. |
| `tests/ElpisEdgeConnect.Host.Tests/Adapters/SourceSupervisorTapTests.cs` | new | Supervisor-level (~10 tests, unchanged from v1). |
| `tests/ElpisEdgeConnect.Management.Tests/Api/LiveTagsApiTests.cs` | new | ~25 tests (was ~15 in v1; +10 for 403/400/429/snapshot/sequence). |
| `tests/ElpisEdgeConnect.Management.Tests/Components/LiveTagWatchModelTests.cs` | new | ~30 tests (was ~25; +5 for stale-rule-floor, snapshot discriminator). |
| **`tests/ElpisEdgeConnect.Management.Tests/Diagnostics/CanonicalCatalogProviderTests.cs`** | **new (Imp #6)** | ~10 tests: FOCAS2 protocol, Brother protocol, Modbus protocol, unknown source returns empty, source with empty `DataPoints` returns empty. |
| `tests/ElpisEdgeConnect.Benchmarks/RuntimeTapBenchmarks.cs` | new | **v2 expansion:** separate benchmarks for event-emission path and latest-value cache path per §4.4. ~5 benchmarks (was 3 in v1). |
| `docs/licensing/module-catalog.md` | edit | Document `live-tag-watch` module. |
| `docs/benchmarks/m2c-runtime-tap.md` | new | Captured measurements. |

**Test target:** ~+115 tests per v2 §0 estimate (was +85 in v1). Distribution: ~40 RuntimeTap core (incl. cache/snapshot/sequence/prune), 1 deterministic-replay, ~10 supervisor, ~25 SSE API, ~30 page model, ~10 catalog provider, ~5 benchmark gate.

### 6.1 `IRuntimeTap` contract (v2 final)

```csharp
public interface IRuntimeTap
{
    void TryPublish(string sourceInstanceId, CanonicalDataPoint point);
    bool HasSubscribers(string sourceInstanceId);   // NEW v2 (Imp #1)
    IRuntimeTapSubscription Subscribe(
        string sourceInstanceId,
        IReadOnlySet<string> canonicalTagPaths,
        CancellationToken ct);
}
```

`TryPublish` MUST NOT throw (defensive try/catch around the call IS NOT NEEDED in the supervisor — the contract requires the implementation to swallow internal errors and signal via metrics). Test enforces this: `Publish_NeverThrows_EvenIfChannelWriterFails`.

`HasSubscribers` is the public predicate the supervisor (or any future runtime publisher) calls to short-circuit before constructing call-site arguments. Pure read; no allocation; O(1) dictionary lookup.

`Subscribe` returns a subscription whose `Reader` emits:
1. Snapshot entries (per §4.2.2) — one event per selected tag that has a cached value ≤5 min old, in canonical-tag-path sort order, each carrying `snapshot: true` in the DTO.
2. Subsequent live publishes that match the filter set, in publish order, each carrying `snapshot: false`.

### 6.2 Subscribe-time tag validation (Q-V1.5 — Management-side, not Core)

The `LiveTagsApi.cs` endpoint validates tag paths BEFORE calling `_runtimeTap.Subscribe(...)`. The validation surface is the `ICanonicalCatalogProvider` (§6.3) — for the selected source, every requested tag path must appear in `GetCatalog(sourceInstanceId)`. Unknown tag paths produce a 400 Bad Request with `{"error":"unknown-tags","tags":["Status/RunStateTypo"],"sourceInstanceId":"..."}`.

**Why Management-side, not Core:** the Core `IRuntimeTap` has no knowledge of which source emits which tags — it just publishes whatever points flow in. Pushing catalog awareness into Core would create a circular dependency (Core → Management catalog → Core types). The Management layer owns the per-protocol catalog providers (§6.3) and is the natural place for tag validation.

### 6.3 `ICanonicalCatalogProvider` — per source instance (NEW v2 — Imp #6)

ChatGPT correctly flagged that v1's "per-protocol canonical catalog" framing breaks for Modbus, where the catalog IS the source's configured `Connection.Tags[]` list, not a static protocol map.

**Lock — per source instance, dispatched by protocol:**

```csharp
namespace ElpisEdgeConnect.Management.Diagnostics;

public interface ICanonicalCatalogProvider
{
    IReadOnlyList<CanonicalTagDescriptor> GetCatalog(string sourceInstanceId);
}

public sealed record CanonicalTagDescriptor
{
    public required string TagPath { get; init; }
    public required string ValueType { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
}
```

| Protocol implementation | Catalog source |
|---|---|
| `Focas2CanonicalCatalogProvider` | `Focas2TagMap` static catalog **intersected with** the source's configured `DataPoints` filter (so if the operator configured 30 tags out of FOCAS2's 80-tag catalog, the provider returns those 30). |
| `BrotherHttpCanonicalCatalogProvider` | `BrotherTagMap` static catalog **intersected with** the source's configured `DataPoints` filter. |
| `ModbusTcpCanonicalCatalogProvider` | Source's configured `Connection.Tags[]` directly. No static map; Modbus catalogs are operator-authored per-source. |

The dispatcher (probably a `CompositeCanonicalCatalogProvider` that consults the source's `ProtocolName` and delegates to the protocol-specific implementation) lives in Management; the protocol-specific implementations live in Management too (NOT in the protocol source modules — Modbus's source module has no UI knowledge by design).

**Lives in Management layer.** This is the v2 location nuance per Imp #6: catalog providers are a Management/API concern, not a Core concern. Core's `IRuntimeTap` is protocol-agnostic; Management's `ICanonicalCatalogProvider` is protocol-aware.

### 6.4 Reconnect UX (Q-V1.8 lock)

When SSE disconnects (network blip, server restart), browser `EventSource` auto-reconnects on a 3-second default backoff. The Studio page surfaces a **subtle banner** during the gap:

```
[ ⟳ Reconnecting to Live Tag Watch... ]
```

The banner is dismissible. Auto-clears on the first SSE event received after reconnect. Reality-check in v3 confirms the shared `MudAlert`/banner primitive matches the M.2b.5/6 v3 §3 premium-UX patterns.

---

## 7. SourceSupervisor injection-point reality-check

Unchanged from v1 §7. The injection point at line 601-602 (between intake-channel `WriteAsync` and `RecordSourceObservation`) is reaffirmed by ChatGPT review verdict on Q24.

`SourceSupervisor` constructor gains a fourth parameter `IRuntimeTap runtimeTap`. The supervisor calls `_runtimeTap.TryPublish(adapter.InstanceId, point)` for each emitted point — never reads from the tap, never wraps the call in try/catch (the contract guarantees `TryPublish` doesn't throw).

---

## 8. Performance budget — measurement methodology (v2 extended)

### 8.1 Benchmark setup — two paths, two budgets

`tests/ElpisEdgeConnect.Benchmarks/RuntimeTapBenchmarks.cs`, BenchmarkDotNet.

**Path A — Event-emission overhead** benchmarks:

| Benchmark | Setup | Acceptance gate |
|---|---|---|
| `Baseline_NoTap_540PtsPerSec` | 100 synthetic source loops, `NullRuntimeTap`, 60s run | Reference baseline; no gate, just measurement |
| `TapOn_ZeroSubscribers_540PtsPerSec` | Identical but `RuntimeTap` with zero active subscribers | **≤1% CPU delta vs baseline. Zero allocation per `TryPublish`. (Imp #1 / P1 rule 1.)** |
| `TapOn_OneSubscriber_30Tags_540PtsPerSec` | One subscriber filtering on 30 tags | **≤5% CPU delta vs baseline. Bounded allocation per publish (channel write + filter check + ring enqueue + sequence increment).** |

**Path B — Latest-value cache overhead** benchmarks (NEW v2):

| Benchmark | Setup | Acceptance gate |
|---|---|---|
| `LatestValueCache_AlwaysOn_540PtsPerSec` | `RuntimeTap` cache populates on every publish; zero subscribers (isolates cache cost from event-emission cost) | **<0.1% CPU delta vs Path-A baseline. <1 MB memory ceiling at 100 sources × ~16 tags.** |
| `Snapshot_OnSubscribe_30Tags_FromPopulatedCache` | Cache pre-populated; subscribe with 30-tag filter; measure snapshot emission cost | **<10ms p99 for the snapshot emission burst.** |

### 8.2 What the budget proves

- Path A zero-subscriber benchmark validates the **revised** P1 rule 1 wording — zero **event-emission cost** when no subscribers.
- Path A subscriber-active benchmark bounds the worst-case operator-driven cost.
- Path B always-on benchmark validates the **bounded latest-value cache cost** claim (§3.1.1). This is the cost that's NOT zero when no subscribers — and we measure it so future regressions can't hide.
- Snapshot benchmark validates that opening a Watch session doesn't stall the SSE endpoint.

### 8.3 CI integration

BenchmarkDotNet runs not part of standard `dotnet test`. Pattern matches Phase 1 baseline benchmarks: capture results in `docs/benchmarks/m2c-runtime-tap.md` with date + machine + measured numbers.

---

## 9. Step-by-step implementation sequence (v2 — 17 steps)

Locked sequence. Two sessions: session 1 = steps 1-10 (Core + Host + cache + snapshot), session 2 = steps 11-17 (Management + page + benchmarks + sweep).

1. **`IRuntimeTap` + `IRuntimeTapSubscription` contracts.** XML docs cite P1 + §3.1.1 wording correction. Smallest-possible passing test: contract compiles.
2. **`NullRuntimeTap` implementation.** Empty `TryPublish`; `HasSubscribers` returns false; `Subscribe` returns zero-emission subscription. Allocation test: 1000 `TryPublish` calls allocate zero bytes.
3. **`RuntimeTap` core skeleton + source-first registry (§4.2.4).** Per-source `RuntimeTapSourceEntry` structure with sequence counter, latest-value cache, subscriber list.
4. **`RuntimeTap` latest-value cache (§4.2.2 + §3.1.1).** Always-on `Dictionary<tagPath, CanonicalDataPoint>` per source. Test: cache populates on every publish; entries older than 5 min excluded from snapshot.
5. **`RuntimeTap` per-source sequence counter (§4.2.3).** `Interlocked.Increment`. Test: monotonic per source, independent across sources, snapshot entries carry publish-time sequence.
6. **`RuntimeTap` subscriber fan-out + filter (Q-V1.2).** Hash-set membership; per-subscriber bounded channel; drop-oldest on overflow.
7. **`RuntimeTap` lazy-prune retention (§4.2.1, RESOLVES Q-V1.6).** Three pruning points: on publish, on read, on snapshot. Test: ring entries older than 5 min evicted; lazy prune correctness under load.
8. **`RuntimeTap` snapshot-on-subscribe (§4.2.2, Imp #2).** Subscribe emits cached entries first, in canonical-tag-path sort order, each marked `snapshot: true`. Test: snapshot order; snapshot omits stale entries; two subscribers get independent snapshots.
9. **Deterministic-replay test (P1 rule 5).** Single critical test. If it fails, M.2c does not ship.
10. **License gating wiring (§4.5, Imp #5).** Add `LiveTagWatch = "live-tag-watch"` to `LicenseModuleKeys.cs`. DI registration: `RuntimeTap` when licensed, `NullRuntimeTap` otherwise. Test: byte-identical supervisor behaviour.
11. **`SourceSupervisor.cs` injection (§7).** Constructor parameter + single `TryPublish` call. Update existing supervisor tests to pass `NullRuntimeTap`.
12. **Supervisor-level tap tests.** §11.3 list. Slow-subscriber-doesn't-block test is the load-bearing one.
13. **`LiveTagEventDto` wire shape.** Per §6 final field set (including `sequenceNumber` + `snapshot` v2 additions). Round-trip serialise/deserialise test.
14. **`ICanonicalCatalogProvider` + 3 protocol implementations (§6.3, Imp #6).** Tests per protocol. Modbus tests the no-static-map path.
15. **`LiveTagsApi.cs` SSE endpoint.** All v2 behaviors: 403 license-disabled, 400 too-many-tags, 429 too-many-sessions, subscribe-time tag validation against catalog provider, 15s heartbeat, disconnect cleanup ≤15s.
16. **`LiveTagWatchModel` + `LiveTagWatch.razor` page.** POCO view-model (P2 pattern); snapshot-vs-live discriminator; stale rule per §4.6; subtle reconnect banner (Q-V1.8); catalog-driven tag multi-select.
17. **Benchmarks + Studio navigation + solution-wide sweep + commit.** Both Path A and Path B benchmarks green per §8. Live Tag Watch nav link added (hidden when license absent). Manual smoke through Studio: add a Mock source, open Watch page, pick 5 tags, see snapshot render immediately, see live updates flowing.

---

## 10. Out of scope (v2 — extended)

Beyond the 6 anti-scope locks in §2.1 (apply to the whole subsystem), v1 explicitly excludes (carried unchanged):

- Multi-source per session.
- Authentication (defer to Phase 4).
- Recording / export.
- AI integration (Phase 4.5).
- Per-tag charts / graphing.
- Cross-route view.
- Heatmaps / aggregation visualisations.
- Modifying source configuration from the Watch page.
- Embedding the Watch in other pages (M.2d.1's `WizardWatchSlot.razor` reserves the slot but ships a stub).
- Studio "fleet view" surfaces.

**v2 additions to out-of-scope:**

- **No server-side query language** (§2.1.6). Filter syntax is hash-set membership over literal tag paths. Wildcards, regex, prefix patterns, computed filters, expression engines — all out forever.
- **No retention-bound configurability.** The 100-entries AND 5-minutes bounds are constants (§4.2.1). Operators wanting longer retention use the Phase 5 historian.
- **No snapshot-on-resubscribe-after-disconnect.** Browser auto-reconnect re-subscribes from the current cache state; the operator doesn't see a "history" gap-filler beyond the lazy-pruned 5-minute snapshot. If the customer reports operational confusion during soak, promote to v1.1.
- **No UI surfacing of sequence-number gaps.** v1 logs gaps to browser console only. UI surfacing (e.g., "3 events missed in the last minute") deferred to v1.1.

---

## 11. Test list (target ~+115 tests, was ~+85 in v1)

### 11.1 `RuntimeTapTests.cs` (~40 tests; v1 had ~25)

v1's 25 tests carry forward. **v2 additions (~15 new):**

- `Publish_DropsRingEntriesOlderThanFiveMinutes_BeforeEnqueue` (§4.2.1)
- `Read_SkipsAndEvictsEntriesOlderThanFiveMinutes` (§4.2.1)
- `LatestValueCache_PopulatesOnEveryPublish_RegardlessOfSubscribers` (§4.2.2 / §3.1.1)
- `LatestValueCache_PerSource_IsIsolatedFromOtherSources` (§4.2.4)
- `Subscribe_EmitsSnapshotPerSelectedTag_FromLatestValueCache` (§4.2.2)
- `Subscribe_SnapshotOrderIsCanonicalTagPathAscending` (§4.2.2)
- `Subscribe_SnapshotExcludesEntriesOlderThanFiveMinutes` (§4.2.1 + §4.2.2)
- `Subscribe_NoSnapshotEntries_WhenSourceNeverEmittedThoseTags` (§4.2.2)
- `Subscribe_TwoSubscribersToSameSource_GetIndependentSnapshots` (§4.2.2)
- `Subscribe_SnapshotEmissionsCarrySnapshotTrueFlag_InDto` (§4.2.2)
- `Publish_IncrementsSequenceCounter_PerSourceMonotonic` (§4.2.3)
- `Publish_SequenceCounter_IsIndependentAcrossSources` (§4.2.3)
- `HasSubscribers_ReturnsFalse_WhenNoSubscribersForSource` (Imp #1)
- `HasSubscribers_IsConstantTimeReadLookup` (Imp #1)
- `Publish_SourceFirstRegistryTraversal_DoesNotIterateAllSubscribers` (§4.2.4)

The v1 test `Subscribe_PerSubscriptionWallClockBound_DropsAfter5Minutes` is **no longer deferred** (v1 marked it deferred due to Q-V1.6); v2 promotes it to live status.

### 11.2 `RuntimeTapDeterministicReplayTests.cs` (1 critical test)

- `Replay_TapOnVsTapOff_ProducesByteIdenticalCanonicalPoints` (P1 rule 5)

### 11.3 `SourceSupervisorTapTests.cs` (~10 tests, unchanged from v1)

### 11.4 `LiveTagsApiTests.cs` (~25 tests; v1 had ~15)

v1's 15 tests carry forward (with `Sse_LicenseDisabled_Returns404` updated to `Sse_LicenseDisabled_Returns403`). **v2 additions (~10):**

- `Sse_LicenseDisabled_Returns403_WithFeatureDisabledBody` (Imp #5)
- `Sse_UnknownTagPath_Returns400_WithUnknownTagsList` (Q-V1.5 + §6.2)
- `Sse_TooManyTags_Returns400_WithLimit50` (Imp #8)
- `Sse_TooManyConcurrentSessions_Returns429_WithRetryAfter30` (Imp #8)
- `Sse_DisconnectCleanup_CompletesWithinHeartbeatPeriod` (Imp #8)
- `Sse_FirstEventsAfterSubscribe_AreSnapshotEntries` (§4.2.2 wire)
- `Sse_SnapshotDtoCarriesSnapshotTrueFlag_LiveDtoCarriesSnapshotFalse` (§4.2.2 wire)
- `Sse_DtoCarriesSequenceNumber_Monotonic` (§4.2.3 wire)
- `Sse_TagValidationUsesCatalogProvider_NotProtocolStaticMap` (§6.2 + Imp #6)
- `Sse_HeartbeatComment_EmittedEveryFifteenSeconds` (Q-V1.3)

### 11.5 `LiveTagWatchModelTests.cs` (~30 tests; v1 had ~25)

v1's 25 tests carry forward. **v2 additions (~5):**

- `Model_ComputesStale_AgainstIngestTimestamp_NotDeviceTimestamp` (§4.6)
- `Model_StaleFloor_Is5000ms_RegardlessOfPollInterval` (§4.6)
- `Model_RendersSnapshotEntries_BeforeLiveEntries_OnInitialLoad` (§4.2.2)
- `Model_NavigationLinkHidden_WhenLicenseAbsent` (Imp #5)
- `Model_LogsSequenceNumberGapToConsole_OnGapDetected` (§4.2.3 / out-of-scope §10 ack)

### 11.6 `CanonicalCatalogProviderTests.cs` (~10 tests, NEW v2)

- `Focas2CatalogProvider_IntersectsStaticMapWithSourceDataPoints`
- `BrotherCatalogProvider_IntersectsBrotherTagMapWithSourceDataPoints`
- `ModbusCatalogProvider_ReturnsSourceConfiguredTags_NoStaticMap`
- `CatalogProvider_UnknownSource_ReturnsEmptyCollection`
- `CatalogProvider_SourceWithEmptyDataPoints_ReturnsEmptyCollection`
- `CompositeCatalogProvider_DispatchesByProtocolName`
- ... plus structural-purity / DI smoke tests.

### 11.7 `RuntimeTapBenchmarks.cs` (5 benchmarks per §8)

- `Baseline_NoTap_540PtsPerSec`
- `TapOn_ZeroSubscribers_540PtsPerSec` (gate: ≤1% delta, zero allocation)
- `TapOn_OneSubscriber_30Tags_540PtsPerSec` (gate: ≤5% delta)
- `LatestValueCache_AlwaysOn_540PtsPerSec` (gate: <0.1% delta, <1MB memory)
- `Snapshot_OnSubscribe_30Tags_FromPopulatedCache` (gate: <10ms p99)

---

## 12. Open questions (v2 — drastically reduced)

### 12.1 Open questions RESOLVED in v2

All v1 open questions are resolved per the verdict table in §0:

- **Q24** — Resolved (v1 §7.1 stands; reaffirmed by review).
- **Q25 / Q-V1.3** — Resolved: 15s heartbeat.
- **Q-V1.1** — Resolved: single `event: tag-value` frame.
- **Q-V1.2** — Resolved: hash-set membership.
- **Q-V1.4** — Resolved: native JSON value + `valueType` field.
- **Q-V1.5** — Resolved: subscribe-time validation at Management/API layer, NOT Core.
- **Q-V1.6** — Resolved: lazy-prune in Runtime Tap (rejects v1's rendering-layer deferral).
- **Q-V1.7** — Resolved: `ICanonicalCatalogProvider`, per source instance, Management-side.
- **Q-V1.8** — Resolved: subtle banner.

### 12.2 New v2-specific open questions (for v3 reality-check only — no further ChatGPT pass)

| # | Area | Question |
|---|---|---|
| Q-V2.1 | Catalog provider DI dispatch | `CompositeCanonicalCatalogProvider` dispatches by `ProtocolName`. How does it discover the source's protocol from `sourceInstanceId` — by looking up the active `GatewayConfiguration` (one extra dependency on `IConfigurationManager`)? Or by accepting a `(sourceInstanceId, protocolName)` tuple? Recommendation: the former is simpler from the API's perspective. v3 reality-check confirms `IConfigurationManager`'s read-shape supports this. |
| Q-V2.2 | Latest-value cache memory accounting | The <1MB ceiling assumes ~16 tags/source average. What's the actual fleet-wide tag count for the 100-CNC customer? Reality-check against the customer-locked deployment-readiness §7 answers — and if the average exceeds 30 tags/source, revise the memory ceiling claim before v3 closes. |
| Q-V2.3 | Sequence-number wrap | At `long` width, sequence number won't wrap on any realistic timescale. But v3 reality-check confirms no per-source-instance long restart issue (supervisor restart resets the counter to 0 — that's the documented behaviour per §4.2.3). |
| Q-V2.4 | Sse_TooManyConcurrentSessions_RetryAfter value | The 429 response includes `Retry-After: 30` (seconds). Is 30 the right hint? Operators will close stale tabs; 30s seems reasonable. v3 reality-check confirms or revises. |
| Q-V2.5 | Snapshot emission burst pacing | When a Watch session subscribes with 30 selected tags and the cache is fully populated for all 30, the snapshot emits 30 events in rapid succession before the first live event. Does this need to be paced (e.g., 10/sec) to avoid SSE write-buffer overflow on slow networks? v3 reality-check benchmarks. |
| Q-V2.6 | `ICanonicalCatalogProvider` discovery for Modbus per-tag CSV importer | M.2d.4 (cross-wizard sweep) is supposed to extend the per-instance-validator pattern across protocols (per its v1 plan §5.1). Does Modbus's per-tag CSV importer need its own catalog-provider shape, or does it pre-populate the source's `Connection.Tags[]` such that `ModbusTcpCanonicalCatalogProvider` works unchanged? Cross-reference to M.2d.4 v1 plan during reality-check. |

### 12.3 v3 reality-check items (no recommendation needed yet)

- Confirm DI registration site for `IRuntimeTap`.
- Confirm `LicenseModuleKeys.cs` exact location + naming.
- Confirm Studio navigation file (likely `MainLayout.razor`).
- Confirm benchmark docs directory pattern.
- Confirm `LiveTagWatch.razor` route attribute matches Studio conventions.
- Confirm subtle-banner primitive matches M.2b.5/6 patterns.

---

## 13. Risks and mitigations (v2 extended)

v1's 10 risks carry forward. **v2 additions:**

| # | Risk | Likelihood | Severity | Mitigation |
|---|---|---|---|---|
| 11 | Latest-value cache memory grows unbounded if a source emits unbounded distinct tag paths (e.g., dynamic tag names from a misconfigured protocol) | Low | Medium | Per-source cache cap (e.g., 200 entries — generous for any realistic catalog). Beyond cap, log a warning and evict LRU. v3 reality-check confirms cap value. |
| 12 | Snapshot emission burst at subscribe time causes SSE write-buffer overflow on slow networks | Low | Low | Q-V2.5 reality-check benchmarks; if needed, pace the snapshot to 10 emissions/sec |
| 13 | Sequence-number reset on supervisor restart causes false "gap detected" log in the Watch page | Medium | Low | Subscriber side already handles the gap by re-subscribing (browser SSE auto-reconnect). The console log message includes `"reason: likely-restart"` when the gap is large (>1000). |
| 14 | `ICanonicalCatalogProvider` dispatcher introduces a circular dependency between Management and protocol source modules | Medium | Medium | The dispatcher lives in Management; each protocol-specific implementation lives in Management (not in the source module). Management already references the source modules' tag-map types (`Focas2TagMap`, `BrotherTagMap`); no new dependency direction. |
| 15 | License-disabled hidden nav link vs disabled-with-tooltip — operator confusion | Low | Low | Lock: hidden when license absent. Phase 4 may revisit when the licensing UX matures. |

---

## 14. Definition of done (v2 extended)

- [ ] All 6 anti-scope locks (§2.1) honoured. Code review confirms.
- [ ] P1 rule 5 (deterministic replay) enforced by `RuntimeTapDeterministicReplayTests.cs`; test green.
- [ ] **P1 rule 1 (revised wording, §3.1.1)** — event-emission path zero-cost when no subscribers (`TapOn_ZeroSubscribers` benchmark green); latest-value cache path bounded always-on cost (`LatestValueCache_AlwaysOn` benchmark green).
- [ ] P1 rule 2 (isolated backpressure) enforced; supervisor pump throughput unchanged with a deliberately-slow subscriber.
- [ ] P1 rule 3 (publisher-only) enforced by code review.
- [ ] **P1 rule 4 (license-gateable, three-layer)** — `NullRuntimeTap` registration when module disabled; API returns **403** (not 404); UI nav link hidden.
- [ ] Performance budgets green: Path A ≤1%/≤5%, Path B <0.1%, snapshot p99 <10ms.
- [ ] **Both retention bounds enforced (§4.2.1):** ≤100 values AND ≤5 min per tag, lazy-prune verified by three pruning-point tests.
- [ ] **Snapshot-on-subscribe (§4.2.2) implemented:** operator sees current state on session open, before next live event.
- [ ] **Per-source monotonic sequence number (§4.2.3) implemented:** independent counters; reset on supervisor restart; carried on every emitted DTO.
- [ ] **Source-first registry traversal (§4.2.4) implemented:** O(subscribers-of-this-source), not O(total-subscribers), per publish.
- [ ] **Stale rule (§4.6) implemented:** `max(2×PollIntervalMs, 5000ms)` against `IngestTimestamp`.
- [ ] **Endpoint limits (§4.7) implemented:** max 50 tags/session, max 10 concurrent sessions, 15s heartbeat, ≤15s cleanup.
- [ ] **`ICanonicalCatalogProvider` (§6.3) implemented per protocol; per-source-instance dispatch.**
- [ ] Zero new warnings.
- [ ] Coverage ≥85% on `RuntimeTap.cs`.
- [ ] Full solution test sweep clean.
- [ ] End-to-end Studio smoke: select Mock source → pick 5 tags → see snapshot render immediately → see live updates flowing for 60s → observe stale + quality indicators correctly.
- [ ] All 9 v1 open questions resolved (all in v2; none deferred).
- [ ] All 6 v2-specific open questions reality-checked in v3.
- [ ] License module catalog documents `live-tag-watch`.
- [ ] Benchmark capture in `docs/benchmarks/m2c-runtime-tap.md`.
- [ ] Plan trail captured: this v1 → review → v2 → reality-check → v3 → implementation handoff.

---

## 15. Pause-point criteria (stop and report if any of these)

Unchanged from v1 §15. The pause-point criteria remain: anti-scope drift, injection-point geometry change, performance budget failure, replay-determinism failure, catalog-provider impossibility, P1-rule un-testable.

---

## 16. Knock-on / next-session items

Unchanged from v1 §16. Key carry-forward items:

- M.2d.1 populates `WizardWatchSlot.razor` with a real reference to the M.2c page (currently a stub).
- EREMOS V2 contract revalidation uses the Watch page for visual confirmation during the 7-day soak.
- Operational Intelligence layer ADR triggers when the second non-M.2c consumer of Runtime Tap arrives.
- Per-tag charts / graphs deferred to post-soak feedback.
- UI surfacing of sequence-number gaps deferred (v2 §10).
- Snapshot-on-resubscribe history deferred (v2 §10).

### 16.1 Hard prerequisite — Bug 3 understood before M.2c implementation starts

**Locked dependency** (added 2026-05-22 after the EREMOS V2 Gate 5 finding surfaced [Bug 3 (P2)](2026-05-22-bug3-mqtt-reconnect-investigation.md), tracked at [issue #24](https://github.com/elpisitsolutions/EdgeConnect/issues/24)):

> **M.2c implementation does NOT start until Bug 3 is at least understood** (root cause identified, even if not yet resolved).

Rationale: Bug 3 is `MqttSinkAdapter`'s slow reconnect after broker restart. Both M.2c (Live Tag Watch SSE subscription) and the MQTT sink path touch **runtime confidence and operator-facing diagnostics**. If Bug 3's root cause is H1 (MQTTnet session-state coupling on reconnect), the Runtime Tap subscription path may exhibit the same fragility under broker restart — and that fragility would land directly in the operator's Live Tag Watch UX during commissioning. Better to understand the failure mode before building a UI that exposes it.

This is a sequencing constraint, not a scope change. Once Bug 3's investigation identifies the root cause (H1 / H2 / H3 / H4 per the investigation plan), M.2c implementation is cleared to proceed — even if the bug itself is deferred for resolution.

Status check before starting M.2c implementation: review issue #24's resolution record (§6 of the investigation plan), confirm a root cause is locked, then proceed with M.2c steps 1-17 per §9 above.

---

## 17. Cross-references

Unchanged from v1 §17. Single architectural authority remains `docs/platform-principles.md` P1. v2 references P1 verbatim and extends with M.2c-specific implementation invariants per the roadmap v2.1 §C governance model.

---

**End of v2 draft. LOCKED — ready for v3 reality-check pass during implementation session.**

Per ChatGPT round-1 verdict: "no further review needed before drafting." v3 resolves Q-V2.1 through Q-V2.6 from inside the codebase during the implementation session; no separate ChatGPT review iteration.
