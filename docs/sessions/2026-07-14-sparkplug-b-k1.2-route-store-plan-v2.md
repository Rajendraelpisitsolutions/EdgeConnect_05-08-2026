# Sparkplug B — K1.2 `SqliteRouteStore` execution plan (v2)

**Date:** 2026-07-14 · **Branch:** `feat/sparkplug-b-k1.2-route-store`
**Supersedes:** plan v1 (same date). **Plan-trail:** v1 → external review → **this v2** (folds the
review in; O1–O5 resolved, seven blocking additions incorporated, grounded in a read of the
real `SqliteBuffer`/`SqliteBufferSchema`). Implementation-ready pending sign-off.

## 0. Reconnaissance findings (real state of `SqliteBuffer`, verified 2026-07-14)

- **`SqliteBuffer` is `public sealed class SqliteBuffer : IMessageBuffer`.** Likely
  instantiated by Host DI/factories/tests → O5 lands as the **façade** option.
- **Schema versioning already exists and is authoritative:** the `meta` table holds
  `schema_version` (`SqliteBufferSchema.CurrentSchemaVersion = 1`). **No `PRAGMA
  user_version` is used.** → K1.2 bumps `CurrentSchemaVersion` to **2** and migrates
  additively; we do **not** introduce a second version mechanism (resolves review §4).
- **PRAGMAs are already locked** in `SqliteBufferSchema`: writer = `journal_mode=WAL`,
  `synchronous=FULL`, `busy_timeout=5000`, `temp_store=MEMORY`, `cache_size=-2000`,
  `wal_autocheckpoint=1000`, `foreign_keys=OFF`; reader = WAL + busy_timeout + temp_store +
  cache. → K1.2 reuses these verbatim (resolves review §5). `foreign_keys=OFF` stays; the
  `latest_value` table therefore enforces its point/sequence relationship in app logic, not
  via an FK.
- **Monotonic-head risk confirmed (review §1):** `ReadHeadTail` sets `head = MAX(sequence)+1`;
  when `points` is empty it collapses to 0 and is recovered forward **only** via
  `PeekMaxCursor` (max `next_unread` across cursors). The persisted `meta.tail_sequence`
  is **written but not read back** as the authoritative head on `OpenAsync`. So after a full
  reclaim with no cursor high-water (or a replay-only route), the cutoff can move backward.
- Existing tables: `points(sequence PK, payload BLOB, enqueued_at, expires_at)`,
  `cursors(sink_id PK, next_unread, updated_at)`, `meta(key PK, value)`.
- Existing serializers present: `CanonicalDataPointSerializer`, `MessagePackFormat`,
  `BinaryWriterFormat`, `ISerializationFormat` — reuse candidates for O3.
- D10 reclaim-loop race fix lives in the reclaim loop (`Interlocked.Exchange(ref
  _reclaimSignal, …)`); must not regress.

## 1. Scope (locked by the PR #180 approval — unchanged from v1)

**In:** `SqliteRouteStore`; atomic append+upsert; generation persistence + transitions;
coherent `CaptureBirthStateAsync`/`CaptureCutoverAsync`; SQLite boundary parity;
restart/rollback/corruption/generation tests; hot-path zero-cost evidence.
**Out (guarded):** `RouteWorker` wiring, H/C batch splitting + ack orchestration, rebirth
handling (K1.3); Sparkplug assembly/payloads/actor (K2/K3).

## 2. O1–O5 — resolved (adopting the review verbatim)

- **O1 — capture via a single SQLite read-snapshot transaction; no history table; no
  `BEGIN IMMEDIATE` for capture.** A plain read transaction on the reader connection sees a
  stable DB snapshot; a concurrent writer's later commit is invisible to it, so `(cutoff,
  snapshot)` is coherent without a writer lock and without relying on single-writer.
  **Correctness is fail-closed, not filtered:** read the current-generation rows and **throw**
  if any `route_buffer_sequence >= cutoff` — never silently `WHERE … < cutoff` away a bad row
  (that would mask store divergence).
- **O2 — `src/ElpisEdgeConnect.Core/Buffer/SqliteRouteStore.cs`.** The DB owner belongs beside
  the buffer; interface namespaces don't dictate the folder.
- **O3 — versioned typed binary envelope; no untyped JSON.** Reuse the existing canonical
  serializer if it preserves integer width, float-vs-double, `DateTime`, byte-array identity,
  and typed static-property values; otherwise an internal versioned DTO codec on the repo's
  established binary serializer. Store `value_type`, `route_buffer_sequence`,
  `schema_generation`, and the key columns as separate columns for validation/indexing; the
  typed value + static-property envelope is a **versioned BLOB**. Must round-trip every
  K1.1 arm (Boolean/Integer/Long/Float/Double/String/DateTime/ByteArray, known-null-with-real-
  datatype, quality+reason, unit, immutable static props); `Array`/`Object`/`Null` stay
  rejected upstream by K1.1.
- **O4 — route-local fail closed; never auto-reset.** On invalid DB / failed integrity check /
  unsupported format / malformed payload / invalid generation / route-id mismatch: refuse to
  open for delivery, emit a typed route-store diagnostic, mark **that** route Failed/Blocked,
  leave other routes running, preserve the DB for recovery. No automatic quarantine-empty.
- **O5 — one `SqliteRouteStore` owner; existing `SqliteBuffer` becomes a thin façade**
  delegating to it (Option B, chosen to preserve the public `SqliteBuffer` surface and its
  test/factory call sites). One mutation owner, one transaction domain, one reclaim loop.

## 3. Storage model (v2)

Bump `CurrentSchemaVersion` → **2**. Additive DDL (idempotent), same DB file:

- **`points`, `cursors`** — unchanged.
- **`meta`** — reused; add keys: `next_sequence` (durable monotonic head — see §4.1),
  `route_id`, `current_schema_generation`. `schema_version` stays the authoritative format
  version.
- **`latest_value`** — the observed manifest:
  ```sql
  CREATE TABLE IF NOT EXISTS latest_value (
      source_instance_id   TEXT    NOT NULL,
      device_id            TEXT    NOT NULL,
      tag_path             TEXT    NOT NULL,
      value_type           INTEGER NOT NULL,
      route_buffer_sequence INTEGER NOT NULL,
      schema_generation    INTEGER NOT NULL,
      envelope             BLOB    NOT NULL,   -- versioned typed value + quality/reason/unit/static-props
      updated_at           INTEGER NOT NULL,
      PRIMARY KEY (source_instance_id, device_id, tag_path)
  );
  ```
  **Physical key = `(source_instance_id, device_id, tag_path)`** — the DB is already
  per-route, so `route_id` lives once in `meta`, not redundantly on every row (review storage
  refinement). The logical key `RouteId + SourceInstanceId + DeviceId + TagPath` is satisfied
  by (DB identity + `meta.route_id`). On open, the supplied route id **must** equal
  `meta.route_id` or the store fails closed (O4).

  > `device_id` NOT NULL: node-only v1 has no device level → persist a fixed sentinel
  > (e.g. empty-string canonical form) consistently in both the store and the K1.1 key.
  > **Open micro-question M1** (does not block): confirm the sentinel matches how
  > `CanonicalMetricKey` renders a node-only device.

## 4. Blocking additions (folded from the review)

### 4.1 Persist a monotonic append head (review §1)

`next_sequence` becomes **authoritative and durable** in `meta`. Today `tail_sequence` is
written but not read on open; K1.2 makes the head monotonic:

- **Append** (single transaction): read `next_sequence` → assign one sequence per point →
  insert `points` → upsert `latest_value` (if tracking enabled) → set `next_sequence =
  assigned_max + 1` → commit.
- **Open** recovery: `head = max(MAX(sequence)+1, PeekMaxCursor, meta.next_sequence)` so the
  head never regresses even when both `points` and `cursors` are empty. (Keeps the existing
  fully-drained-restart fix and adds the persisted floor.)
- **Capture** reads `meta.next_sequence` as the exclusive cutoff (H and C).
- **Test:** append→100, reclaim every row, capture cutoff = **101** (not 0/1), restart, next
  append continues at 101.

### 4.2 Schema-generation fencing (review §2)

Persisting the generation is insufficient without proving writes belong to it. Extend the
append/transition surface:

```csharp
// additive; the existing IMessageBuffer.EnqueueAsync remains for non-replay routes
ValueTask<AppendResult> AppendAsync(
    IReadOnlyList<CanonicalDataPoint> points,
    RouteSchemaGeneration expectedGeneration,
    bool updateLatestValues,
    CancellationToken ct);

ValueTask AdvanceGenerationAsync(
    RouteSchemaGeneration expectedCurrent,   // compare-and-set
    RouteSchemaGeneration next,
    CancellationToken ct);
```

Inside one transaction: read `current_schema_generation`; **reject (typed failure)** if
`expectedGeneration != current`; else append+upsert. Locked rules: generation never
decreases; not reused after rollback; stale-generation append fails typed; captures never mix
generations; superseded-generation rows may remain physically but are ignored by
current-generation capture. **Add a stale-writer concurrency test.**

### 4.3 Generation-transition visibility (review §3)

`AdvanceGenerationAsync` yields an **empty** observed manifest for the new generation. Only
values observed and appended under the new generation enter its birth snapshot; prior-
generation values are **not** copied forward through (possibly changed) transforms — matches
the accepted observed-set-only policy (ADR-0036 Rule 5). Route/worker pause orchestration is
K1.3; K1.2 only guarantees safe persistence if a stale worker still writes.

### 4.4 Migration from the current (v1) `SqliteBuffer` DB (review §4)

K1.2 runs against existing route DBs. On open of a v1 file: additively create `latest_value`
+ new `meta` keys, seed `next_sequence` from the recovered head, seed
`current_schema_generation = 0`, set `meta.route_id`, bump `schema_version` → 2. **Do not**
rebuild/copy `points`/`cursors`. **Migration test:** create a v1 DB with rows+cursors, open
via `SqliteRouteStore`, assert rows/cursors readable, head monotonic, latest-value tracking
starts at a defined generation.

### 4.5 PRAGMAs / transaction enlistment (review §5)

Reuse the locked `SqliteBufferSchema` PRAGMAs (already WAL/FULL/busy_timeout/foreign_keys=OFF).
Capture runs on the **reader** connection inside one explicit read transaction with every
participating command enlisted; append runs on the writer connection in one transaction. No
reliance on machine defaults. Any move to weaker durability requires an explicit ADR, not an
incidental perf tweak.

### 4.6 Corrected crash-window matrix (review §6)

Append order is `points insert → latest upsert → commit`, so "upsert-before-append" is
**not reachable**. Deterministic fault-injection points (no real power-kill needed):

| Fault point | Durable state after restart |
|---|---|
| before any write | neither `points` nor `latest_value` changed |
| after point insert, before upsert | rolled back → neither changed |
| after upsert, before commit | rolled back → neither changed |
| commit failure | neither changed; caller gets typed failure |
| commit succeeds, process dies before return | both visible; delivery-layer retry may duplicate, store stays coherent |

### 4.7 Define "zero-cost" precisely (review §7)

Gate on a store option `EnableReplayStateTracking`. When **disabled**, the enqueue path
performs **no** snapshot serialization, generation query, `latest_value` SQL, or extra
transaction statement vs. the pre-K1.2 `SqliteBuffer` path. K1.3 enables it per-route when a
replay-aware sink is present. **Test via SQL/codec call counts or an injected spy**, not by
checking whether a table has rows.

## 5. Captures (the correctness core)

`CaptureBirthStateAsync(routeId, sinkId)` — one reader read-transaction: validate
`routeId == meta.route_id`; read sink cursor → `FirstPendingSequence`; read `next_sequence`
→ `H`; read `current_schema_generation`; read all current-generation `latest_value` rows;
**fail closed** if any `route_buffer_sequence >= H`; build `LatestValueSnapshot(gen, …)`;
return `ReplaySessionStartState.Create(ReplayBoundary.Create(firstPending, H), snapshot)`.
`CaptureCutoverAsync(routeId)` — same shape at `C = next_sequence` →
`ReplaySessionCutoverState.Create(C, snapshot)`. Both decode the typed envelope back into the
K1.1 immutable arms (incl. `ImmutableArray<byte>`).

## 6. Boundary parity

`SqliteRouteStore` implements `IReplayBoundaryProvider`; a shared parity suite drives the
in-memory and SQLite providers through identical append/cursor/reclaim histories and asserts
identical `ReplayBoundary` results (empty → `First == Cutoff`; backlog → `First < Cutoff`;
post-reclaim → monotonic cutoff).

## 7. Tests (v1 matrix + review's expanded set)

Crash-window (§4.6); corruption→fail-closed; restart/rehydrate; generation transition;
boundary parity; **plus:** (1) monotonic head after reclaim+restart; (2) migration from v1
DB; (3) stale-generation writer rejected; (4) unknown sink cursor → typed error; (5) route-id
mismatch → fail closed; (6) retention removes `points` rows but not the current
`latest_value`; (7) capture stays coherent while another connection commits appends;
(8) every scalar + ByteArray arm round-trips the envelope; (9) malformed row → fail closed
(not skipped); (10) two instances can't independently own/mutate one route DB (or ownership
prevents it); (11) **D10 reclaim-loop regression** under deterministic concurrent
capture/append/reclaim/dispose; (12) tracking-disabled path does no latest-state codec/SQL
work. Determinism: no `Thread.Sleep`; `Category!=Flaky`. New error codes → `CoreErrors.cs`.

## 8. Performance evidence

BenchmarkDotNet (Release): append throughput with vs without latest-value upsert; assert the
tracking-disabled path is unchanged from the current `SqliteBuffer` baseline and the enabled
overhead is bounded (batching absorbs `synchronous=FULL`). Capture latency at 100 / 1k / 10k
metrics.

## 9. Implementation sequence (commits within one PR; K1.2a may stand alone if large)

- **K1.2a** — recon lock-in: introduce `SqliteRouteStore` as sole DB owner; `SqliteBuffer`
  becomes a façade; **prove `IMessageBuffer` behavior/tests unchanged** (behavior-neutral).
- **K1.2b** — persisted monotonic `next_sequence` + `current_schema_generation`; additive v1→v2
  migration; generation CAS/fencing tests.
- **K1.2c** — typed latest-value codec + `latest_value` table; atomic append+upsert;
  rollback/restart/round-trip tests.
- **K1.2d** — `CaptureBirthStateAsync`/`CaptureCutoverAsync` read-snapshot transaction; boundary
  parity + concurrent-writer tests.
- **K1.2e** — corruption/error handling + perf evidence; full Core + Management regression.

## 10. Definition of done

Core + solution 0/0; full Core.Tests + Management.Tests green; all §7 tests present/green;
perf evidence captured; new error codes in `CoreErrors.cs`; **no** `RouteWorker`/Sparkplug/
actor code (scope guard); `SqliteBuffer` public behavior proven unchanged. Then production PR.

## 11. Remaining micro-questions (non-blocking)

- **M1** — node-only `device_id` sentinel must match `CanonicalMetricKey`'s node-only rendering.
- **M2** — whether `AppendResult` needs to carry the assigned sequence range now (K1.3 needs it
  for H/C splitting) or can be added when K1.3 lands. Leaning: include it now (cheap, avoids a
  later contract change).
```
