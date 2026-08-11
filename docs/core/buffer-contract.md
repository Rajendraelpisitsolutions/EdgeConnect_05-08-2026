# Elpis EdgeConnect — Buffer Contract

**Status:** Stable behavioral contract shared by **all** `IMessageBuffer` implementations, locked at C2a and preserved unchanged in C2b.
**Reference:** `ARCHITECTURE_BLUEPRINT.md` §6, §19; `PHASE1_EXECUTION_PLAN.md` Milestones C2a and C2b.
**Scope of this document:** the externally observable behavior every implementation must exhibit. SQLite-specific durability design, transaction boundaries, recovery algorithm, and storage-layer failure handling are **not** contract rules — they live in [`buffer-durability.md`](buffer-durability.md).

---

## 1. Purpose

The buffer is the per-route durability layer between the transform pipeline and the sinks. It owns:

- **Sequence assignment.** Every enqueued point gets a strictly monotonic `long` sequence per buffer instance.
- **Per-sink cursors.** Each sink advances independently. A slow sink does not block a fast sink.
- **Eviction.** Storage is physically released only when *all* registered sinks have acked past a sequence (or when the drop policy forces an eviction).
- **Drop semantics.** When the buffer is full, the configured `DropPolicy` decides what happens; producers never throw on overflow.
- **Stats.** Counters for depth, total enqueued/drained/dropped, oldest message age, registered sink count.

The contract is consumed by the C3 routing engine. Two implementations target it: `InMemoryBuffer` (C2a, this milestone) and `SqliteBuffer` (C2b, next milestone).

---

## 2. Locked design refinements vs blueprint §6

The blueprint §6 sketch shows a simpler four-method contract. Three refinements are locked here based on the C2a pre-generation review (decisions D1, D6, D7):

| ID | Refinement | Why |
|---|---|---|
| **D1** | `DequeueBatchAsync` and `AckAsync` take a `string sinkId` | A single-cursor contract cannot support fanout: a route with two sinks would need to either duplicate the storage or block one sink behind the other. Sink-aware cursors are the right primitive. |
| **D6** | `RegisterSinkAsync` / `DeregisterSinkAsync` model sink lifecycle | Without explicit registration, eviction math is wrong: the buffer cannot decide when to release a slot if it doesn't know which sinks may still want to read it. |
| **D7** | `DequeueBatchAsync` returns `BufferBatch` (carries sequence range) | Lets a sink ack precisely without separate bookkeeping. |

These are not departures *from* the blueprint — they are concretizations *of* it. The blueprint sketch was a simplification.

---

## 3. The contract

```csharp
public interface IMessageBuffer : IAsyncDisposable
{
    string BufferId { get; }

    ValueTask EnqueueAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken cancellationToken);

    ValueTask<BufferBatch> DequeueBatchAsync(
        string sinkId,
        int maxCount,
        CancellationToken cancellationToken);

    ValueTask AckAsync(
        string sinkId,
        long upToSequence,
        CancellationToken cancellationToken);

    ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken);
    ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken);

    ValueTask<BufferStats> GetStatsAsync();
}

public sealed record BufferBatch
{
    public static BufferBatch Empty { get; }
    public required IReadOnlyList<CanonicalDataPoint> Points { get; init; }
    public required long FirstSequence { get; init; }
    public required long LastSequence { get; init; }
    public bool IsEmpty { get; }
}
```

---

## 4. Semantics

### 4.1 Sequences
- The buffer assigns a monotonic `long` sequence to each enqueued point.
- Sequences are independent of `CanonicalDataPoint.SequenceNumber` (which is source-assigned).
- Sequences never repeat for the lifetime of a buffer instance.
- Concurrent enqueue is safe; sequences remain unique and strictly increasing.

### 4.2 Cursors
- Each registered sink has a cursor representing the **next unread sequence** — i.e., points with sequence `< cursor` have been delivered.
- After acking up to sequence `N` inclusive, the cursor becomes `N + 1`.
- Cursors **cannot regress.** `AckAsync(sinkId, k)` where `k + 1 ≤ current` is a no-op.
- **`RegisterSinkAsync(sinkId)` starts the new cursor at the OLDEST currently held sequence.** A freshly attached sink replays the buffered backlog. **This rule is part of the stable contract and holds identically for every implementation — including the durable SQLite buffer in C2b, where "oldest currently held sequence" refers to the durable tail on disk.**
- Re-registering an existing sink id is **idempotent** — the existing cursor is preserved.
- `DeregisterSinkAsync` removes the sink and lets the buffer release any data that was only being held for it.

#### 4.2.1 Why the registration rule is contract, not policy
A proposal to start new sinks at `_head` instead of `_tail` was **considered and rejected** during C2b design (decision D12). Silently changing the starting position would alter operational behavior between the in-memory and durable implementations of the same contract — a sink that replayed the backlog under `InMemoryBuffer` would skip the backlog under `SqliteBuffer`. Hosts that want new sinks to start at the head of a large durable buffer must do so **explicitly** by calling `AckAsync(sinkId, currentHead - 1)` immediately after `RegisterSinkAsync`, or by an opt-in registration mode that may be introduced as a future, explicit, contract-versioned extension. Implementations must not embed that policy in the register call itself.

### 4.3 Eviction
- The buffer's tail (oldest held sequence) advances when `min(cursors) > tail`.
- When all sinks have acked past sequence `N`, the slot for `N` is freed and `TotalDrained` increments.
- When the buffer is full and a producer enqueues:
  - **`DropOldest`**: forcibly advance the tail by one slot, dropping the oldest data. Any sink whose cursor was at the dropped slot is fast-forwarded to the new tail. The loss is counted (see §4.5).
  - **`DropNewest`**: refuse the new point. The loss is counted (see §4.5).
  - **`Block`**: suspend the producer until a sink ack frees space. Cancellation is honored.

### 4.4 Reclaim invariant (behavioral)

For every active sink `s`, after any committed mutation (enqueue, ack, register, deregister, or eviction), `s.next_unread` **must** refer either to an existing sequence currently held by the buffer, or exactly to the current `_head`.

Equivalently: no registered cursor may point at a sequence that is strictly between `(−∞, _tail)` or strictly above `_head`. A cursor at `_head` (meaning the sink is caught up) is valid and common. A cursor below `_tail` or above `_head` is a contract violation.

This invariant is the single correctness condition for eviction: any code path that deletes a row must, in the same atomic step, advance any cursor that would otherwise be left pointing at the deleted row. Tests should assert the invariant after every observable state change.

### 4.5 Ordering
- A sink observes its assigned points in **strict sequence order** (per blueprint §19.6).
- Cross-source ordering is NOT promised — only per-source-per-sink ordering.

### 4.6 Drop counters (additive)

Points can be lost for several distinct reasons, each with its own operational story. `BufferStats` exposes them separately so dashboards and alerts do not collapse unrelated signals.

| Counter | Meaning | When it increments |
|---|---|---|
| `TotalDropped` | Sum of all drops. Retained for backward compatibility with the C2a shape. Equals `DroppedByCapacity + DroppedByRetention` for any implementation that reports either. | On any drop |
| `DroppedByCapacity` | Points refused or evicted because the buffer hit `MaxDepth`. Includes both the `DropNewest` refusal and the `DropOldest` eviction cases. | When the depth ceiling is hit |
| `DroppedByRetention` | Points evicted by an implementation-specific age- or size-based retention policy (e.g., the SQLite buffer's `MaxAge` sweep). **This category represents deliberate unread-data loss** and is counted separately so operators can distinguish policy-driven loss from capacity-driven loss. | On retention-driven delete of unacked rows |
| `SinksFastForwarded` | Count of fast-forward **events** (not points) — one per eviction pass that had to advance at least one lagging sink cursor past a deleted row. | On eviction with lagging sinks |

An implementation that has no retention policy (e.g., `InMemoryBuffer`) reports `DroppedByRetention = 0` always.

### 4.7 Stats shape
The `BufferStats` record is additive — new fields may be added without breaking the C2a shape.

| Field | Type | Meaning |
|---|---|---|
| `CurrentDepth` | `long` | Points currently held (`_head - _tail`). |
| `TotalEnqueued` | `long` | Lifetime count of successfully enqueued points. |
| `TotalDrained` | `long` | Lifetime count of points released after all sinks acked. |
| `TotalDropped` | `long` | Lifetime sum of all drop counters (§4.6). |
| `DroppedByCapacity` | `long` | See §4.6. |
| `DroppedByRetention` | `long` | See §4.6. |
| `SinksFastForwarded` | `long` | See §4.6. |
| `OldestMessageAt` | `DateTime?` | `GatewayTimestamp` of the oldest held row, or `null` if empty. |
| `OldestUnackedSinkId` | `string?` | Sink id whose cursor currently pins the tail, or `null`. **Diagnostics breadcrumb** for C4 alerting on lagging sinks. |
| `OldestUnackedAt` | `DateTime?` | Wall-clock enqueue time of the oldest sequence still held for that sink. |
| `SizeBytes` | `long` | Approximate storage footprint. In-memory implementations return 0. Durable implementations MAY compute an approximate byte figure; exact accounting is not required by the contract. |
| `RegisteredSinks` | `int` | Current sink cursor count. |

### 4.8 Disposal
- `DisposeAsync` is **cleanup, not correctness.** Any behavior relied on for data durability must hold even if `DisposeAsync` is never called (e.g., the process is killed). A graceful dispose MAY do housekeeping such as releasing waiters, flushing optional buffers, or checkpointing write-ahead logs, but the contract does not require it for correctness.
- `DisposeAsync` does NOT drain the buffer — the route engine is responsible for orderly shutdown via the C3 lifecycle (blueprint §19.5).
- After `DisposeAsync` has run, any call to a buffer method throws `ObjectDisposedException`.

---

## 5. Implementations

Two implementations target this contract in Phase 1:

| Implementation | Milestone | Durable across restart? | Storage |
|---|---|---|---|
| `InMemoryBuffer` | C2a ✅ | No | Fixed-capacity ring + `SinkCursorTracker` |
| `SqliteBuffer` | C2b | Yes | SQLite (WAL, `synchronous=FULL` by default) |

Both implementations satisfy every rule in §4 identically. The contract does not distinguish between them — a caller that works against `IMessageBuffer` works against either.

**Implementation-specific design documents:**
- In-memory: summarized in §5.1 below (the full design was captured in the C2a gate review).
- SQLite: [`buffer-durability.md`](buffer-durability.md) covers WAL configuration, transaction boundaries, the recovery algorithm, retention, and failure-mode handling. That document is the authoritative design reference for `SqliteBuffer` — it is **not** part of the contract.

### 5.1 `InMemoryBuffer` (C2a) — summary
- Backing storage: `CanonicalDataPoint?[]` of size `MaxDepth`. Slot index = `seq % capacity`.
- `_head`: next sequence to assign. Strictly monotonic; never wraps.
- `_tail`: oldest sequence still held.
- A single `lock` guards the ring + counters. `SinkCursorTracker` provides the per-sink cursor table; `InMemoryBuffer` and `SqliteBuffer` both consume it.
- Block mode uses a `TaskCompletionSource<bool>` re-created on each space release. Producers re-check after creating a waiter.
- `RegisterSinkAsync` starts the new sink at `_tail`, per §4.2.
- No durability. Data is lost on process exit.

### 5.2 `SqliteBuffer` (C2b) — pointer
See [`buffer-durability.md`](buffer-durability.md).

---

## 7. `BufferPolicy` vs `BufferPolicyConfig` — DTO vs runtime separation

These are TWO distinct types and must not be confused:

| | `BufferPolicyConfig` | `BufferPolicy` |
|---|---|---|
| Layer | B1 — JSON DTO | C2a — runtime policy |
| Location | `Configuration/BufferPolicyConfig.cs` | `Buffer/BufferPolicy.cs` |
| Validation | DataAnnotations + cross-record | Strict required-init record |
| Mapping | Source for `BufferPolicy.FromConfig(...)` | Target consumed by `InMemoryBuffer` and `SqliteBuffer` |
| Mutability | User-edited via `current.json` | Built once at route activation |

The DTO is *forgiving and optional*; the runtime policy is *strict and final*. Tests, code, and docs should never use one in place of the other. The mapping function `BufferPolicy.FromConfig` is the only place the conversion happens.

---

## 8. `BufferMode` and `DropPolicy` are reused from B1

Per decision D3, the enums `Configuration.BufferMode` and `Configuration.DropPolicy` are NOT duplicated under the `Buffer/` namespace. The buffer code references them directly via `using ElpisEdgeConnect.Core.Configuration;`. This is intentional to avoid divergence between the JSON DTO and the runtime types.

---

## 9. Serialization format — locked

The on-wire format for persisted points is **`BinaryWriterFormat` (`binary-v1`)**, locked at the C2a gate review. The decision is captured in `docs/benchmarks/phase1-baseline.md`:

- `binary-v1` round-trip: ~422 ns/point (~2.37 M pts/sec)
- `messagepack-v1` round-trip: ~1,043 ns/point (~959 k pts/sec, 2.47× slower)

`MessagePackFormat` and the `MessagePack` NuGet dependency will be removed in C2b. The format facade `CanonicalDataPointSerializer.Binary` is the only blessed entry point going forward.

The contract does not mandate a specific wire format — the format is an implementation detail of the durable buffer. However, until Phase 2 introduces format versioning, C2b persists `binary-v1` bytes directly.

---

## 10. Compression

`CompressionCodec` is a stateless LZ4 wrapper. Format:
```
[int32 originalLength][lz4 block bytes...]
```
The length prefix lets `Decompress` allocate the destination buffer exactly without scanning the LZ4 stream.

C2a's `InMemoryBuffer` does NOT compress (in-memory storage is by reference; compressing would just burn CPU). The codec ships in C2a so its benchmark numbers land in the Phase 1 baseline; C2b's `SqliteBuffer` is the consumer.

The codec must achieve **≥5x ratio** on the realistic CNC payload fixture (`C2aTestFixtures.RealisticCncBatch`); pinned by `CompressionCodecTests.Compress_RealisticCncBatch_Achieves_AtLeast_5x_Ratio`.

---

## 11. Benchmark targets and gate results

### C2a targets (passed — see `docs/benchmarks/phase1-baseline.md`)

| Benchmark | Target | Result |
|---|---|---|
| `InMemoryBuffer_Enqueue` | ≥ 100 k pts/sec | 29.2 M pts/sec (292× headroom) |
| `InMemoryBuffer_EnqueueDequeue` | ≥ 100 k pts/sec | 82.3 M pts/sec (823× headroom) |
| `Serializer_Roundtrip` (Binary, winner) | ≥ 500 k pts/sec | 2.37 M pts/sec (4.74× headroom) |
| `Compression_Ratio` | ≥ 5× | pinned by unit test |
| `Compression_Throughput` | ≥ 200 MB/sec | ~1.74 GB/sec |
| Steady-state enqueue allocations | zero | WriteLocked path is allocation-free |

### C2b targets (to be confirmed at the C2b gate)

C2b is a **durability** milestone, not a throughput milestone. Correctness comes first. See [`buffer-durability.md`](buffer-durability.md) §9 for SQLite-specific throughput floors and recovery-time expectations.

Run buffer benchmarks with:
```
dotnet run --project tests/ElpisEdgeConnect.Benchmarks --configuration Release -- --filter "*Buffer*"
```

Numbers are captured to `docs/benchmarks/phase1-baseline.md` at each gate review and become the regression floor for subsequent milestones.
