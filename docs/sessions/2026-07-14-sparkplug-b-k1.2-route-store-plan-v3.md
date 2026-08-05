# Sparkplug B — K1.2 `SqliteRouteStore` execution plan (v3, implementation-ready)

**Date:** 2026-07-14 · **Branch:** `feat/sparkplug-b-k1.2-route-store`
**Supersedes:** v2 (same date). **Plan-trail:** v1 → review → v2 → reality-check → **v3**.
v3 folds the reality-check's ten required changes. Sections unchanged from v2 (scope §1,
storage §3 except keys, boundary parity §6, impl sequence, DoD) carry over; this doc restates
every **locked invariant** so no cross-reference is needed to implement.

## 0. Carry-over facts (from v2 §0 reconnaissance, verified)

`SqliteBuffer` is `public sealed : IMessageBuffer`. Authoritative format version =
`meta.schema_version` (`CurrentSchemaVersion` 1 → **2**); no `PRAGMA user_version`. PRAGMAs
locked in `SqliteBufferSchema` (WAL/`synchronous=FULL`/`busy_timeout=5000`/`foreign_keys=OFF`).
Head today = `MAX(sequence)+1` recovered via `PeekMaxCursor` only when empty; `meta.tail_sequence`
persisted but not read as authoritative head. D10 reclaim-loop race fix must not regress.

## 1. O1–O5 (locked, from v2 — unchanged)

O1 read-snapshot capture (no `BEGIN IMMEDIATE`, **fail-closed not filtered**) · O2 `Core.Buffer`
· O3 dedicated typed envelope (now pinned, §5) · O4 route-local fail-closed, never auto-reset ·
O5 one `SqliteRouteStore` owner, `SqliteBuffer` a façade (now with a lifetime lock + capability
handle, §4).

## 2. Reality-check change 1 — migration seeds `next_sequence` from `tail_sequence`

`next_sequence` does not exist in a v1 DB. The migration seed is:
```
next_sequence = max(
    MAX(points.sequence) + 1,     // absent → 0
    MAX(cursors.next_unread),     // absent → 0
    parse(meta.tail_sequence),    // absent → 0
    parse(meta.next_sequence))    // absent → 0
```
Malformed / negative / overflowing metadata → **fail closed** (typed `RouteStoreCorrupt`).
After migration: `next_sequence` is the sole authoritative append head; `tail_sequence` keeps
only its retention/tail meaning and is **not** repurposed or deleted (the façade refactor must
first prove nothing depends on it). **Test:** v1 DB, `points` empty, `cursors` empty,
`tail_sequence=101` → migrate → `next_sequence=101` → next append gets sequence 101.

## 3. Reality-check change 2 — reconcile zero-cost with authoritative `next_sequence`

Persist an explicit `meta.replay_state_tracking ∈ {disabled, enabled}`.

- **Disabled (default for non-replay routes):** the existing `SqliteBuffer.EnqueueAsync` SQL
  path is **byte-for-byte unchanged** — no generation read, no envelope encoding, no
  `latest_value` SQL, and **no extra `next_sequence` write** beyond what the current path
  already does. (So authoritative `next_sequence` maintenance is an *enabled-only* cost; this
  is what makes "zero-cost" truthful.)
- **Enable = one-time activation transaction** (`BEGIN IMMEDIATE`): recover the head
  (§2 formula) → persist `next_sequence` → validate/set `meta.route_id` → init
  `current_schema_generation = 0` → set `replay_state_tracking = enabled` → `COMMIT`.
- **After activation:** every tracked append uses the generation-aware atomic path (§ append)
  and maintains `next_sequence`. Calling the legacy `EnqueueAsync` on an **enabled** store is
  an internal invariant violation → typed `RouteStoreLegacyAppendOnEnabledStore` (never
  silently append to `points` only).

## 4. Reality-check changes 3 & 4 — generation backlog fence, lifetime lock, capability handle

### 4a. Drain-before-generation-advance (the decisive correctness fix)

A new generation with an empty manifest is unsafe if old-generation rows still sit in `points`
below a replay-aware sink's cursor: K1.3 would NBIRTH gen N+1 then replay gen-N DATA
(removed metrics / old datatypes / old names) — violating birth-before-data. Generation
fencing stops new stale writes but not already-buffered ones. **Lock:** for a replay-aware
sink, generation may advance only after intake is paused (K1.3) **and that sink's cursor has
caught up to `next_sequence`**, verified atomically by the store:

```csharp
ValueTask<GenerationAdvanceResult> AdvanceGenerationAsync(
    RouteSchemaGeneration expectedCurrent,
    RouteSchemaGeneration next,               // must == expectedCurrent + 1 (§8)
    string mustBeCaughtUpSinkId,
    CancellationToken ct);
```
In one transaction: verify `expectedCurrent == current`; verify
`cursor(mustBeCaughtUpSinkId).next_unread == next_sequence`; else reject typed
`GenerationBacklogPending`; else commit the new generation (empty new-generation manifest).
K1.3 owns pausing intake and requesting the transition; **K1.2 owns the atomic fence.** This
avoids a per-row generation column and a multi-generation replay policy.
**Tests:** cursor behind head → advance rejected; cursor caught up → advance succeeds + new
snapshot empty; old-generation append after advance → stale-generation rejection.

### 4b. Lifetime ownership lock

The owner holds `<route-db-path>.lock` (exclusive share-mode) for its lifetime; if already
held → typed `RouteStoreAlreadyOwned`, fail route startup. A process crash releases the OS
handle. The owner owns the lock, writer+reader connections, migration, the **single** reclaim
loop, and disposal. **`SqliteBuffer` façade must not open independent connections or start a
second reclaim loop.**

### 4c. Capability handle (honest optionality)

An internal factory returns:
```csharp
internal sealed record SqliteRouteStoreHandle(
    IMessageBuffer Buffer,                               // always present (the façade)
    IReplayBoundaryProvider? ReplayBoundaryProvider,     // present only when tracking enabled
    IReplaySessionStateProvider? ReplaySessionStateProvider);
```
Disabled → replay providers `null`; enabled → both reference the same store owner. Public
`SqliteBuffer` API stays stable; K1.3 consumes the handle during route composition.

## 5. Reality-check change 5 — pin `LatestValueEnvelopeV1`

A dedicated internal codec (may use the repo's MessagePack dependency) — **not** arbitrary
`object`/typeless serialization, **not** the public contract persisted directly. `latest_value`
columns `source_instance_id, device_id, tag_path, value_type, route_buffer_sequence,
schema_generation, updated_at` stay separate for validation/indexing; the envelope BLOB encodes:
codec version; declared `CanonicalValueType`; null state; scalar value union;
**`DateTimeOffset`** acquisition timestamp (offset/UTC validated) — note: v2 said `DateTime`;
the K1.1 type is `DateTimeOffset`; quality + quality reason; unit; immutable static properties
using the **exact K1.1 scalar union** (byte[]→raw bytes); `ByteArray` value as raw bytes.
Unknown version / unknown discriminator / malformed field / unsupported static-prop type →
**fail closed**. Qualification tests cover every value + static-prop arm plus an
unknown-version case.

## 6. Micro-questions resolved

- **M1 — real canonical `DeviceId`, no sentinel.** Node-only Sparkplug means no device *topic*,
  not loss of canonical identity; `CanonicalMetricKey` already validates `DeviceId`. Persist the
  actual `DeviceId`; the `latest_value` PK `(source_instance_id, device_id, tag_path)` uses it
  (prevents same-`TagPath` collisions across two devices under one source).
- **M2 — internal assigned-sequence result; no public appender yet.**
  ```csharp
  internal readonly record struct AssignedSequenceRange(long FirstSequence, long LastSequence, int Count);
  ```
  K1.3's route-execution design decides later whether any public optional append capability is
  needed.

## 7. Storage model (final, supersedes v2 §3 keys)

Schema v2, additive DDL: `points`/`cursors` unchanged; `meta` gains `next_sequence`,
`route_id`, `current_schema_generation`, `replay_state_tracking`; new `latest_value` with PK
`(source_instance_id, device_id, tag_path)` and the columns in §5. `route_id` lives once in
`meta`; on open the supplied route id must equal `meta.route_id` or fail closed
(`RouteStoreRouteMismatch`).

## 8. Reality-check change 8 — corrected generation semantics

Remove "not reused after rollback" (that's a `bdSeq` reservation rule, not a local
transactional schema generation). **Lock instead:** committed generation never decreases;
`next` must equal `expectedCurrent + 1`; a failed/rolled-back transition leaves the current
generation unchanged; **retrying the same proposed `next` is valid** (an uncommitted generation
was never visible); overflow → fail closed.

## 9. Reality-check — capture read-transaction refinement (O1 hygiene)

```
BEGIN read transaction (reader connection)
→ read meta (route_id, next_sequence=cutoff, generation), sink cursor, RAW current-generation latest rows
→ COMMIT
→ decode + validate envelopes in memory; fail closed on any row with sequence >= cutoff or a bad envelope
→ construct the immutable LatestValueSnapshot + ReplaySessionStartState/CutoverState
```
All row data + sequences come from one SQLite snapshot; decoding happens **after** COMMIT to
cut WAL retention pressure at 10k metrics. **No external/sink callback while the read
transaction is open.** Correctness stays fail-closed (a stray `sequence >= cutoff` throws, not
filtered).

## 10. Append path (final)

Enabled store, one writer transaction: read `current_schema_generation`; **reject typed**
`RouteStoreStaleGeneration` if `expectedGeneration != current`; read `next_sequence`; assign a
contiguous range (`AssignedSequenceRange`); insert `points`; upsert `latest_value` (keep the
row with the greater `route_buffer_sequence`); set `next_sequence = last + 1`; commit. Order is
`points insert → latest upsert → commit`, so "upsert-before-append" is unreachable (crash
matrix per v2 §4.6).

## 11. Performance gates (reality-check change 9 — measurable)

- **Disabled:** identical SQL/codec trace vs current `SqliteBuffer` **and** ≤ **5%** throughput
  regression (review threshold).
- **Enabled:** sustain the existing single-route SQLite target **≥ 5,000 points/sec** at the
  representative batch size.
- **Capture latency** at 100 / 1k / 10k metrics reported and compared across commits; **no**
  invented hard ceiling, but **super-linear growth blocks merge.**

## 12. Tests (v2 §7 set + reality-check additions)

Carry v2 §7 (crash matrix, corruption→fail-closed, restart/rehydrate, boundary parity,
round-trip arms, D10 regression, tracking-disabled no-op, route-id mismatch, coherent capture
under concurrent commit, retention keeps latest, unknown-sink typed error). **Add:**
(1) v1 empty DB with only `tail_sequence` seeds `next_sequence`; (2) disabled→enabled activation;
(3) legacy `EnqueueAsync` rejected after enable; (4) generation transition with pending backlog
(`GenerationBacklogPending`) + caught-up success; (5) route-store lifetime ownership
(`RouteStoreAlreadyOwned`); (6) unknown codec version fails closed; (7) generation/sequence
**overflow** fails closed. New error codes → `CoreErrors.cs`:
`RouteStoreCorrupt`, `RouteStoreRouteMismatch`, `RouteStoreAlreadyOwned`,
`RouteStoreStaleGeneration`, `RouteStoreGenerationBacklogPending`,
`RouteStoreLegacyAppendOnEnabledStore`, `RouteStoreEnvelopeUnsupported`. Determinism: no
`Thread.Sleep`; `Category!=Flaky`.

## 13. Implementation sequence (commits in one PR; K1.2a may stand alone)

- **K1.2a** — `SqliteRouteStore` sole owner + lifetime lock; `SqliteBuffer` façade; prove
  `IMessageBuffer` behavior/tests unchanged (behavior-neutral).
- **K1.2b** — v1→v2 migration (incl. `tail_sequence` seed); `next_sequence` authoritative;
  `replay_state_tracking` + activation; generation CAS + `next==current+1`; fencing tests.
- **K1.2c** — `LatestValueEnvelopeV1` codec + `latest_value`; atomic append+upsert;
  rollback/restart/round-trip + capability-handle.
- **K1.2d** — captures (read-tx then decode) + boundary parity + concurrent-writer + backlog
  fence.
- **K1.2e** — corruption/error handling + perf gates; full Core + Management regression.

## 14. Definition of done

Core + solution 0/0; full Core.Tests + Management.Tests green; all §12 tests present/green;
perf gates (§11) met and reported; new error codes in `CoreErrors.cs`; **no**
`RouteWorker`/Sparkplug/actor code; `SqliteBuffer` public behavior proven unchanged; lifetime
lock + capability handle in place. Then production PR.
```
