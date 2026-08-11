# Elpis EdgeConnect — SQLite Buffer Durability Design (C2b)

**Status:** Design approved, implementation pending. This document is the authoritative design reference for `SqliteBuffer` (`src/ElpisEdgeConnect.Core/Buffer/SqliteBuffer.cs`). It is **not** part of the stable contract — the contract is in [`buffer-contract.md`](buffer-contract.md), and `SqliteBuffer` must satisfy that contract without modification.
**Reference:** `ARCHITECTURE_BLUEPRINT.md` §6, §19; `PHASE1_EXECUTION_PLAN.md` Milestone C2b.
**Revision history:**
- C2b design v1 — initial pre-generation note.
- **C2b design v2 (this document)** — post-review revisions: `synchronous=FULL` default, C2a registration semantics preserved (D12 reversal), split drop counters, formal reclaim invariant, best-effort reclaim, dispose treated as cleanup not correctness.

---

## 1. Goals and non-goals

### Goals
- A durable `IMessageBuffer` backed by SQLite that satisfies the contract in [`buffer-contract.md`](buffer-contract.md) **without modification**.
- **WAL** journal mode, **`synchronous=FULL`** by default.
- Batch-transaction commits — one fsync per batch, not per point.
- **At-least-once** delivery: once an `AckAsync` returns success, the record is gone for that sink; anything enqueued but not yet acked survives process crash, host crash, and power loss of committed transactions.
- Per-sink durable cursors that survive restart.
- A crash-safe recovery algorithm that replays committed batches, replays unacked data, and never surfaces partially written batches as valid records.
- Reclaim that never deletes data still needed by any active sink cursor, unless an explicit retention policy is in play and the loss is separately counted.
- Reuse `SinkCursorTracker` from C2a as the in-memory mirror of the durable cursor table.
- Reuse `BinaryWriterFormat` (`binary-v1`) exactly as locked in C2a.
- A documented failure-mode table for BUSY, FULL, CORRUPT, partial writes, and cursor-inconsistency.
- Realistic SQLite throughput floors and recovery-time floors captured at the C2b gate.

### Non-goals
- No compression redesign — the existing `CompressionCodec` is the only LZ4 path.
- No routing-engine logic (C3).
- No diagnostics UI or management API (C4/Phase 2).
- No multi-process writers. Single service, single writer path.
- No automatic file quarantine on corruption. C2b surfaces a typed exception; the host decides policy.
- No retention beyond `MaxDepth`, optional `MaxAge`, and the lagging-sink fast-forward rule.
- No changes to `BufferPolicy`, `BufferMode`, `DropPolicy`, or the stable contract.
- No serializer work (`MessagePackFormat` removed; only `binary-v1` persists).
- No change to C2a's in-memory sink-registration semantics. New sinks start at the current durable `_tail` — see §6.

---

## 2. Approved decision matrix (post-review)

| ID | Decision | Resolution |
|---|---|---|
| **D1** | `synchronous` PRAGMA | **`FULL` by default**. This is the approved default durability mode for C2b because it best aligns with the acknowledged-persistence requirement in the C2b constraints. Once a commit returns success under `WAL + FULL`, that transaction's rows are on disk at a level SQLite considers durable against process and host crashes. An opt-in `BufferPolicy.SynchronousNormal` flag may be introduced later **only if benchmark data demonstrates a real need** — it is deferred, not promised. |
| **D2** | Connection topology | One writer connection, one reader connection. All writer operations are serialized via a `SemaphoreSlim(1, 1)` held by the buffer instance. Reads do not take the writer mutex; WAL gives readers a consistent snapshot without blocking writers. |
| **D3** | File location | `{dataPath}/buffer/{routeId}.db`, where `dataPath` comes from `GatewaySettings.DataPath`. Each route gets its own file. |
| **D4** | Schema version | Stored in the `meta` table as `schema_version`. C2b ships v1; a schema-version mismatch on open throws `BufferException(BufferSchemaMismatch)`. |
| **D5** | Max points per enqueue transaction | 10,000. Producer batches larger than this are split into consecutive transactions. Configurable via `BufferPolicy.MaxBatchSize`. |
| **D6** | Lagging-sink auto-timeout | **No auto-timeout.** Stats expose `OldestUnackedSinkId` and `OldestUnackedAt` so C4 diagnostics can alert. The decision to deregister a lagging sink belongs with the host, not the buffer. |
| **D7** | Reclaim trigger | **Periodic only.** A dedicated single-thread timer (default 5 s) runs the reclaim pass. Ack handlers may **signal** the reclaim loop via a `TaskCompletionSource` flip — but no ack handler ever enters a `DELETE` path or awaits a reclaim transaction. Ack latency is bounded by a single-row UPDATE and a single COMMIT. Enforced by the `ReclaimDoesNotExtendAckLatency` benchmark. |
| **D8** | WAL checkpoint on dispose | `DisposeAsync` runs `PRAGMA wal_checkpoint(TRUNCATE)` as **cleanup**, not as a correctness dependency. Correctness must hold if `DisposeAsync` is never called (e.g., the process is killed). `PRAGMA wal_autocheckpoint=1000` provides the ambient correctness guarantee. |
| **D9** | Corruption detection | `PRAGMA quick_check` runs on every open. Failure throws `BufferException(BufferCorrupt)` and `OpenAsync` does not return a usable buffer. No auto-quarantine in C2b. The host receives a typed exception and decides whether to rename the file, create a fresh buffer, alert, etc. Cost of `quick_check` is measured in the recovery benchmarks and revisitable if it dominates startup on large files. |
| **D10** | New error code | `CORE.BUFFER_SCHEMA_MISMATCH` added to `CoreErrors`. |
| **D11** | Dispose lifetime | Checkpoint then close both connections. Dispose is still cleanup, not correctness. |
| **D12** | Initial sink cursor on register | **REVERSED from the v1 design.** New sinks register at the current durable `_tail`, exactly matching C2a. A pinned contract test (`Register_NewSink_StartsAtTail_AndReplaysBacklog`) enforces parity with the C2a `RegisterSinkAfterEnqueue_PlaysBackBacklog` pin. Operators who want new sinks to start at `_head` must explicitly call `AckAsync(sinkId, currentHead - 1)` immediately after `RegisterSinkAsync`. |
| **D13** | Reuse `SinkCursorTracker` | Yes — as an in-memory mirror of the `cursors` table. Reads of the tracker are cheap; every ack writes to both the tracker (in memory) and the `cursors` table (in SQL) inside the same mutex, with the SQL update committed first so the in-memory state is never ahead of durable state. |
| **D14** | Cancellation mid-batch | No. Cancellation is honored between transactions but never mid-transaction. Once `BEGIN IMMEDIATE` has been issued, the transaction runs to COMMIT or ROLLBACK without checking the token. |

### Additive rules from the C2b design review
| Rule | Where |
|---|---|
| **Split drop counters** into `DroppedByCapacity`, `DroppedByRetention`, `SinksFastForwarded`. `TotalDropped` is retained as the sum for backward compatibility. | [`buffer-contract.md`](buffer-contract.md) §4.6 |
| **Formal reclaim invariant**: every active sink cursor must point at either an existing held sequence or exactly `_head` after any committed mutation. | [`buffer-contract.md`](buffer-contract.md) §4.4 |
| **Reclaim-does-not-extend-ack-latency** SLO is a design rule AND a benchmark. | §9 below |
| **`MaxAge` is explicit unread-data loss** — documented as policy, counted under `DroppedByRetention`, defaults to `null` (disabled) so hosts opt in. | §7 below |

---

## 3. SQLite schema

Three tables, created lazily on `OpenAsync` if missing. A `meta.schema_version` row gates future migrations.

```sql
-- Durable backing store for enqueued points.
CREATE TABLE IF NOT EXISTS points (
    sequence    INTEGER PRIMARY KEY,         -- monotonic, writer-assigned; never reused
    payload     BLOB    NOT NULL,            -- binary-v1 serialized CanonicalDataPoint
    enqueued_at INTEGER NOT NULL,            -- unix ms wall clock at insert time
    expires_at  INTEGER                      -- unix ms; NULL means no age cap for this row
);

-- One row per registered sink. Durable cursor state.
CREATE TABLE IF NOT EXISTS cursors (
    sink_id      TEXT PRIMARY KEY,
    next_unread  INTEGER NOT NULL,           -- next sequence to deliver to this sink
    updated_at   INTEGER NOT NULL            -- unix ms of last ack
);

-- Single-row buffer metadata.
CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- Partial index accelerates age-based retention sweeps without bloating
-- when most rows have no expires_at.
CREATE INDEX IF NOT EXISTS idx_points_expires_at ON points (expires_at)
    WHERE expires_at IS NOT NULL;
```

### PRAGMAs applied on every connection open
| Pragma | Value | Why |
|---|---|---|
| `journal_mode` | `WAL` | Constraint #1. Concurrent readers, bounded fsync. |
| **`synchronous`** | **`FULL`** | Approved C2b default. See D1 and §4.1. |
| `busy_timeout` | `5000` ms | SQLite internal retry on transient locks. No outer retry loop. |
| `temp_store` | `MEMORY` | Avoid temp file noise. |
| `cache_size` | `-2000` (~2 MB) | Modest, predictable. |
| `wal_autocheckpoint` | `1000` (default) | Periodic WAL truncation. |
| `foreign_keys` | `OFF` | Not used. |

### Sequence numbering
- `sequence` is a normal `INTEGER PRIMARY KEY` (SQLite ROWID), **writer-assigned**, not `AUTOINCREMENT`.
- On `OpenAsync`, the writer reads `SELECT COALESCE(MAX(sequence), -1) + 1 FROM points` and stores it as the in-process `_head`.
- Each enqueue increments `_head` in-process between rows; the in-process value is the source of truth for the duration of the process lifetime.
- After restart, the new process reads `MAX(sequence)` again and picks up where the previous process left off. Monotonicity is preserved across restarts because `MAX(sequence) + 1 > MAX(sequence)`.

---

## 4. Record lifecycle from enqueue to ack to reclaim

```
Producer                       Sink                          Reclaim loop (periodic, 5s)
────────                       ────                          ───────────────────────────
EnqueueAsync(points)           DequeueBatchAsync(sinkId, n)  Tick
  acquire writer mutex            SELECT from points on         compute min_cursor =
  BEGIN IMMEDIATE                 the READ connection;            SELECT MIN(next_unread)
  INSERT each point               no state change.                FROM cursors
  COMMIT  ← durable                                                (or take the cached
  update in-process _head      Sink publishes downstream          snapshot from the
  release writer mutex         AckAsync(sinkId, lastSeq)          SinkCursorTracker)
  producer awaits completion      acquire writer mutex           if min_cursor > _tail:
                                  BEGIN IMMEDIATE                  acquire writer mutex
                                  UPDATE cursors                   BEGIN IMMEDIATE
                                    SET next_unread = lastSeq+1    DELETE FROM points
                                  COMMIT  ← durable                  WHERE sequence <
                                  advance in-memory tracker           min_cursor
                                  release writer mutex              update meta.tail
                                  SIGNAL reclaim loop (TCS flip)   COMMIT
                                  return completed ValueTask       advance in-process _tail
```

### 4.1 What makes a record "durably accepted"
A record is durably accepted **only** when the `COMMIT` of its enqueue transaction has returned successfully. At that instant, under `WAL + synchronous=FULL`, the row's WAL frame has been fsync'd and SQLite's durability guarantees apply. The row is visible to any subsequent read and will be present after process or host crash recovery. `EnqueueAsync` does not return success in any other case (BUSY timeout, FULL, IO error, ROLLBACK, or exception-before-COMMIT all propagate as `BufferException`).

The C2b design treats successful `EnqueueAsync` completion as a hard commitment. This is the core posture behind the choice of `synchronous=FULL`: we do not want a producer to receive success for a record that can quietly disappear.

### 4.2 Enqueue sequence in detail
1. Acquire writer mutex.
2. `BEGIN IMMEDIATE` (acquires RESERVED lock; fails fast with BUSY on contention).
3. For each point: serialize with `BinaryWriterFormat.Instance.Serialize(point)`, compute `sequence = _head + i`, `INSERT INTO points (sequence, payload, enqueued_at, expires_at) VALUES (?, ?, ?, ?)`.
4. If `points.Count > MaxBatchSize (10,000)`, split into multiple consecutive BEGIN/COMMIT transactions.
5. `COMMIT` — fsyncs the WAL under `synchronous=FULL`.
6. Update in-process `_head += points.Count`.
7. Release writer mutex.
8. Producer's `ValueTask` completes.

At step 5 the records are durably accepted. At step 8 the producer knows.

### 4.3 Read sequence in detail
1. Look up the sink's cursor in the `SinkCursorTracker` (in-memory mirror).
2. `SELECT sequence, payload FROM points WHERE sequence >= :cursor ORDER BY sequence LIMIT :n` on the read connection. No transaction; WAL provides a consistent snapshot.
3. Deserialize each payload via `BinaryWriterFormat.Instance.Deserialize`.
4. Return `BufferBatch { Points, FirstSequence, LastSequence }`.
5. No state change. No cursor advance.

### 4.4 Ack sequence in detail
1. Acquire writer mutex.
2. `BEGIN IMMEDIATE`.
3. `UPDATE cursors SET next_unread = :upToSeq + 1, updated_at = :now WHERE sink_id = :sinkId AND next_unread <= :upToSeq`. The `next_unread <= :upToSeq` predicate is the monotonic guard — a backward ack is a no-op.
4. `COMMIT` — fsync.
5. Update the in-memory tracker (mirror).
6. Flip the reclaim-signal TCS so the reclaim loop wakes next tick if `min(cursors)` may have advanced.
7. Release writer mutex.
8. Sink's `ValueTask` completes.

No DELETE runs on this path. Ack latency is bounded by one row's UPDATE + COMMIT + fsync.

### 4.5 Reclaim sequence in detail
The reclaim loop runs on its own timer (default 5 s). On each tick (or when the reclaim-signal TCS is flipped):

1. Read `min_cursor = _cursors.Min(defaultIfEmpty: _head)` from the in-memory tracker. Cheap.
2. If `min_cursor <= _tail`, skip (nothing to do).
3. Acquire writer mutex.
4. `BEGIN IMMEDIATE`.
5. `DELETE FROM points WHERE sequence < :min_cursor`.
6. `UPDATE meta SET value = :min_cursor WHERE key = 'tail_sequence'`.
7. `COMMIT`.
8. Update in-process `_tail = min_cursor`.
9. Release writer mutex.
10. If the delete freed space and the buffer was at `MaxDepth`, flip the producer-space-waiter TCS (Block-mode producers).

Reclaim is never blocking from the caller's perspective. A reclaim tick that hits a busy writer simply defers to the next tick.

---

## 5. Transaction boundaries

Exactly four transaction bodies exist in `SqliteBuffer`. Nothing else opens a transaction.

| Operation | Body | Frequency |
|---|---|---|
| Enqueue batch | `INSERT INTO points (…) VALUES (…)` repeated N times (bounded by `MaxBatchSize`) | High |
| Ack | single-row `UPDATE cursors` | High |
| Reclaim | `DELETE FROM points WHERE sequence < :min_cursor` + `UPDATE meta` | Low (periodic) |
| Register / Deregister sink | `INSERT … ON CONFLICT DO NOTHING` / `DELETE FROM cursors` | Rare |

All four use `BEGIN IMMEDIATE`. All four commit under `synchronous=FULL`. Each is protected by the single writer mutex.

### Why `BEGIN IMMEDIATE` rather than the default `BEGIN DEFERRED`
`BEGIN IMMEDIATE` acquires the RESERVED lock at the start of the transaction, so a contended writer fails fast (BUSY) rather than promoting a deferred transaction mid-way through. This keeps the failure path predictable and consistent with the no-outer-retry rule.

### BUSY handling rule
SQLite's internal `busy_timeout=5000` is the **only** retry layer in `SqliteBuffer`. There is no outer retry loop. If `busy_timeout` is exhausted, the operation throws `BufferException(BUFFER_IO_ERROR)` once, the transaction is rolled back, and the decision to retry belongs to the caller (the route engine in C3).

This rule exists to prevent the "nested retry multiplies the stall" footgun. Exactly one retry layer; exactly one timeout.

---

## 6. Recovery algorithm on startup

`SqliteBuffer.OpenAsync(path, policy)` performs the following steps in order on the writer connection. The call is synchronous from the caller's perspective — it returns when the buffer is ready to accept enqueues and dequeues, with no background warm-up.

1. **Open the SQLite file** and apply all PRAGMAs in §3.
2. **`PRAGMA quick_check`** for structural integrity. Failure throws `BufferException(BufferCorrupt)`. The buffer is not opened; the host decides what to do next (out of C2b scope).
3. **Create tables and index if missing** (`CREATE TABLE IF NOT EXISTS` is idempotent).
4. **Read or initialize `meta.schema_version`.** If present and not equal to `1`, throw `BufferException(BufferSchemaMismatch)`. If absent, write `1`.
5. **Read `_head = COALESCE(MAX(sequence), -1) + 1 FROM points`.**
6. **Read `_tail = COALESCE(MIN(sequence), _head) FROM points`.**
7. **Read all cursor rows into the in-memory `SinkCursorTracker`.** For each cursor, verify the reclaim invariant from [`buffer-contract.md`](buffer-contract.md) §4.4:
   - If `next_unread < _tail`, clamp to `_tail` (defensive — the rows the cursor pointed at were already reclaimed in a previous run; the sink loses them; this is documented `DropOldest` semantics from the previous run). Increment `SinksFastForwarded`.
   - If `next_unread > _head`, throw `BufferException(BufferCursorInconsistent)`. This is database corruption and requires manual repair. (A cursor above head means an ack was committed for a sequence that does not exist — a contract violation that should never happen under correct C2b code.)
8. **Run the reclaim pass once** to catch any slack from a previous run that died between ack commit and periodic reclaim.
9. **Open the read connection** used by `DequeueBatchAsync`.
10. **Mark the buffer ready.** Return.

### Recovery time floors (informal, measured at the C2b gate)
| File size | Recovery time floor (target) |
|---|---|
| Empty | < 50 ms |
| 100,000 rows | < 500 ms |
| 1,000,000 rows | < 5 s |

The dominant cost is expected to be `quick_check`. If the measurement shows `quick_check` is the limiting factor, a `BufferPolicy.IntegrityCheck` enum (`QuickCheck` | `Skip` | `DeepCheck`) may be introduced as a follow-up — but C2b ships `QuickCheck` by default without question.

---

## 7. Eviction and retention

Two eviction triggers are recognized by C2b. They are distinct and separately counted.

### 7.1 Capacity-based eviction (`MaxDepth`)
When `_head - _tail >= MaxDepth`, the writer must make room before accepting a new enqueue. Behavior depends on the policy's `DropPolicy`:

- **`Block`**: `EnqueueAsync` suspends on a `TaskCompletionSource` that is signaled when the reclaim loop (or an `AckAsync`-triggered reclaim signal) advances `_tail`. A lagging sink can block producers indefinitely — this is the documented semantics of `Block` and the host's diagnostics layer is responsible for alerting (D6).
- **`DropOldest`**: the writer **fast-forwards** any sink whose `next_unread` is at or below the current `_tail`, then `DELETE`s the oldest row(s) in the same writer transaction as the new INSERT. Increments `DroppedByCapacity` for each dropped row and `SinksFastForwarded` for each cursor bumped.
- **`DropNewest`**: the new point is refused. Increments `DroppedByCapacity`. No transaction is opened.

### 7.2 Age-based retention (`MaxAge`) — **explicit unread-data loss**
`MaxAge` is a **deliberate policy choice** that instructs the buffer to delete rows older than a wall-clock age regardless of whether they have been acked by every sink. Hosts that enable `MaxAge` are agreeing that age-expired rows may be lost to lagging sinks.

Default: `MaxAge = null` (no age cap). Hosts opt in explicitly via `BufferPolicyConfig.MaxAgeDays`.

When enabled, a periodic retention sweep runs on the same timer as reclaim (default 5 s):

```sql
BEGIN IMMEDIATE;
-- Compute the cutoff; fast-forward any cursor below the cutoff.
UPDATE cursors SET next_unread = (
    SELECT COALESCE(MIN(sequence), :cutoff_sequence + 1)
    FROM points WHERE sequence >= :cutoff_sequence
)
WHERE next_unread < :cutoff_sequence;
-- Then delete the expired rows.
DELETE FROM points WHERE expires_at IS NOT NULL AND expires_at < :cutoff_ts;
COMMIT;
```

Every deleted row increments `DroppedByRetention`. Every fast-forwarded cursor increments `SinksFastForwarded`. The two are distinct and operators can see either independently.

### 7.3 Lagging-sink "block forever" prevention
There is no auto-timeout in C2b (D6). The three layers of protection are:

1. **`Block` mode**: can block producers forever if a sink never acks. The host's C4 diagnostics layer is responsible for alerting on `OldestUnackedAt` crossing a threshold. The decision to deregister the bad sink belongs with the host.
2. **`DropOldest`**: producers never block. Lagging sinks lose data; the loss is counted under `DroppedByCapacity` and surfaces via `SinksFastForwarded`.
3. **`DropNewest`**: producers never block. New data is discarded; the lagging sink keeps its old data. Counted under `DroppedByCapacity`.

Operators pick one of the three per route policy. The buffer does not make policy decisions for them.

---

## 8. Failure-mode table

| Scenario | Detection | Buffer response | Recovery on restart | Data integrity |
|---|---|---|---|---|
| **Process crash mid-enqueue (before COMMIT)** | n/a | n/a | SQLite rolls back the half-written transaction on next open. Rows are not visible. | At-least-once preserved. Producer did not receive success; may retry. |
| **Process crash after COMMIT, before `EnqueueAsync` returns** | n/a | n/a | All committed rows are present. | Producer did not receive success; will retry → duplicates → at-least-once preserved. |
| **Process crash between data commit and cursor update** | n/a | n/a | Data is durable; cursor is at old position. Sink replays from its stored cursor. | At-least-once preserved. Sink may see duplicates (contract). |
| **Host OS crash or clean power loss** | n/a | n/a | Under `WAL + synchronous=FULL`, committed transactions are durable up to SQLite's guarantee. Partial transactions are rolled back by SQLite recovery. | No partial transactions surface. Durable commits survive. |
| **`SQLITE_BUSY` returned from `BEGIN IMMEDIATE` or `COMMIT`** | Return code after `busy_timeout` exhausted | Transaction rolled back. Operation throws `BufferException(BUFFER_IO_ERROR, "lock contention timeout")`. No outer retry loop. | n/a | No partial state. Caller decides retry. |
| **`SQLITE_FULL` (disk full)** | Return code on INSERT or COMMIT | Transaction rolled back. Operation throws `BufferException(BUFFER_IO_ERROR, "disk full")`. | n/a | No data accepted. Producer must back-pressure or apply drop policy. |
| **`SQLITE_CORRUPT` reported on any read or write** | Return code | Operation throws `BufferException(BufferCorrupt)`. | On next open, `quick_check` catches the same condition before any data is read or written. Host decides whether to quarantine (out of C2b scope). | C2b never silently writes over a corrupt file. |
| **`quick_check` fails on startup** | `OpenAsync` step 2 | `OpenAsync` throws `BufferException(BufferCorrupt)`. Buffer is not opened. | Up to the host. | Safe — no data read or written. |
| **Schema version mismatch on startup** | `OpenAsync` step 4 | Throws `BufferException(BufferSchemaMismatch)`. Buffer is not opened. | Host decides (migrate or reject). | Safe. |
| **Cursor row with `next_unread > _head`** | `OpenAsync` step 7 | Throws `BufferException(BufferCursorInconsistent)`. Buffer is not opened. | Manual repair. | Loud failure prevents silent data corruption. |
| **Cursor row with `next_unread < _tail`** | `OpenAsync` step 7 | Clamped to `_tail` (defensive). Increments `SinksFastForwarded`. Buffer continues opening. | Sink resumes from new `_tail`. | Documented `DropOldest` semantics from the prior run. |
| **`MaxDepth` reached + `Block` + lagging sink + crash** | n/a | Producers blocked before crash; their transactions never opened. On restart, the buffer is at the depth it had on crash; blocked producers must call `EnqueueAsync` again. | n/a | No corruption. Producer requests that were blocked at crash time are lost from the producer's perspective (never received success). |
| **WAL file grows unboundedly (rare)** | Detected via `PRAGMA wal_checkpoint(PASSIVE)` periodic checks | Reclaim loop issues `PRAGMA wal_checkpoint(PASSIVE)` after long quiet periods. `DisposeAsync` runs `PRAGMA wal_checkpoint(TRUNCATE)`. Neither is correctness-critical. | n/a | Bounded WAL. |

---

## 9. Benchmark plan and recovery-time SLOs

C2b is a **durability** milestone. Correctness and recovery semantics are the primary gate. Throughput is secondary.

### Throughput benchmarks
Run on a local SSD; numbers may differ on NVMe, HDD, or remote storage. The C2b gate captures actuals on the same host that captured C2a numbers so the deltas are meaningful.

| Benchmark | What it measures | Floor (approximate) |
|---|---|---|
| `SqliteBuffer_Enqueue_Single` | One point per BEGIN/COMMIT (pathological small-batch case) | ≥ 5 k tx/sec |
| `SqliteBuffer_Enqueue_Batch_100` | 100-point batch per BEGIN/COMMIT | ≥ 100 k pts/sec |
| `SqliteBuffer_Enqueue_Batch_1000` | 1,000-point batch per BEGIN/COMMIT | ≥ 500 k pts/sec |
| `SqliteBuffer_DequeueAck_Roundtrip` | Enqueue 100 → dequeue 100 → ack | ≥ 50 k pts/sec |
| `SqliteBuffer_AckOnly` | Pre-populated buffer; pure ack throughput | ≥ 10 k acks/sec |
| `SqliteBuffer_Reclaim` | DELETE 10,000 rows in one transaction after ack | ≥ 500 k rows/sec |
| **`ReclaimDoesNotExtendAckLatency`** | Ack P50/P99 with idle reclaim loop vs. actively running reclaim | the two distributions must be statistically equivalent |

### Recovery-time floors
| File size | Floor |
|---|---|
| Empty | < 50 ms |
| 100,000 rows | < 500 ms |
| 1,000,000 rows | < 5 s |

The recovery benchmarks report `quick_check` time separately from `MAX/MIN(sequence)` and cursor-table load time, so a later regression can be attributed quickly.

### What the C2b gate requires
1. All correctness tests pass, including the contract parity tests (§10).
2. All throughput benchmarks meet their floors.
3. The `ReclaimDoesNotExtendAckLatency` SLO holds.
4. The recovery-time floors are met.
5. `docs/benchmarks/phase1-baseline.md` is updated with actuals.

Unlike C2a, the C2b gate does **not** require order-of-magnitude headroom. SQLite has its own floor determined by disk fsync cost; we document what the disk can do and move on.

---

## 10. Test plan

Estimated ~70 new unit tests, grouped:

| Test class | Count | What it covers |
|---|---|---|
| `SqliteBufferContractTests` | ~15 | **Re-runs every C2a `IMessageBuffer` contract test against `SqliteBuffer`** — the single most important category. Proves the contract is unchanged. Includes the D12 pin: a `Register_NewSink_StartsAtTail_AndReplaysBacklog` test mirroring the C2a `RegisterSinkAfterEnqueue_PlaysBackBacklog`. |
| `SqliteBufferDurabilityTests` | ~10 | Enqueue → close → reopen → dequeue returns same data. Cursor survives close/reopen. Ack survives close/reopen. Sequences survive close/reopen. |
| `SqliteBufferRecoveryTests` | ~12 | Open empty file. Open populated file. Cursor clamping on below-tail. Cursor above-head throws. Missing tables created. Schema version mismatch throws. `quick_check` failure throws. |
| `SqliteBufferCursorTests` | ~10 | Two sinks at different speeds (the §4 worked example in `buffer-contract.md`). Min-cursor reclaim. Lagging fast-forward under `DropOldest`. Deregister releases pinned data and triggers reclaim. Reclaim invariant asserted after every state change. |
| `SqliteBufferFailureModeTests` | ~6 | `SQLITE_BUSY` via lock contention. Corruption by byte-mutating the file between opens. Disk-full scenarios are harder to simulate portably and may be skipped or gated behind an opt-in marker. |
| `SqliteBufferConcurrencyTests` | ~6 | Multiple producer tasks racing on `EnqueueAsync`. Multiple sink tasks racing on `DequeueBatch + Ack`. Serialization correctness under contention. |
| `SqliteBufferRetentionTests` | ~6 | `MaxAge` eviction sweep. `MaxDepth + DropOldest` fast-forward. `MaxDepth + DropNewest` reject. Counter attribution is correct (`DroppedByCapacity` vs `DroppedByRetention` vs `SinksFastForwarded`). |
| `SqliteBufferSchemaTests` | ~4 | Tables created. PRAGMAs applied. Index present. Reopen preserves PRAGMAs. |
| `ReclaimInvariantTests` | ~4 | The formal invariant from [`buffer-contract.md`](buffer-contract.md) §4.4 is asserted after enqueue, ack, register, deregister, reclaim, and fast-forward. |

---

## 11. Answers to the six required questions (post-review)

1. **What makes a record "durably accepted"?** The `COMMIT` of the enqueue transaction returned successfully under `WAL + synchronous=FULL`. At that instant, SQLite's durability guarantees apply. `EnqueueAsync` does not return success in any other case.

2. **What exact sequence happens on enqueue, read, ack, restart?** See §4.2, §4.3, §4.4, and §6.

3. **How do we prevent a lagging sink from blocking the system forever?** Three layers — `Block` relies on C4 diagnostics to alert; `DropOldest` fast-forwards the lagging sink and counts the loss under `DroppedByCapacity`; `DropNewest` refuses new data and counts it under `DroppedByCapacity`. There is no automatic timeout in C2b (D6). Stats expose `OldestUnackedSinkId` and `OldestUnackedAt` for host-level alerting.

4. **How do we reclaim old rows safely?** One SQL statement under one transaction: `DELETE FROM points WHERE sequence < :min_cursor` where `:min_cursor = MIN(next_unread) FROM cursors`. By construction this cannot delete data any active sink still needs. The two retention paths that delete above `min_cursor` (`DropOldest` and `MaxAge`) update any invalidated cursor in the same transaction before deleting. The formal reclaim invariant in [`buffer-contract.md`](buffer-contract.md) §4.4 is the single correctness condition and is asserted by tests after every state change.

5. **What happens if a crash occurs between data commit and cursor update?** Data is durable. The cursor is at its old position. On restart, the sink replays the data from where its cursor stopped. At-least-once is preserved. The sink may see duplicates — that is the contract.

6. **What happens if SQLite returns BUSY, FULL, or corruption?** BUSY → internal `busy_timeout=5000` is the only retry; on exhaustion, throw `BufferException(BUFFER_IO_ERROR)` once. FULL → rollback and throw `BufferException(BUFFER_IO_ERROR, "disk full")`. CORRUPT → throw `BufferException(BufferCorrupt)` on detection; `quick_check` catches the same condition on startup. C2b never auto-quarantines; the host receives the typed exception and decides.

---

## 12. What is explicitly out of scope for this design

- Hot buffer migration (move a route from in-memory to durable at runtime).
- Cross-route buffer consolidation.
- Remote or replicated buffers.
- Compression redesign.
- Multi-writer.
- Format version migration (only `binary-v1` is persisted).
- Automatic corruption repair / quarantine.
- Lagging-sink timeout enforcement at the buffer layer.
- Routing-engine decisions about when to create, destroy, or reconfigure buffers.
