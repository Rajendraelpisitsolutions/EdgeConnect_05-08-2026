# Sparkplug B — K1.2d capture-providers execution plan (v3, reality-checked / execution baseline)

**Status:** ✅ **Reality-check approved / execution baseline** (frozen 2026-07-15 after the
review-verdict correction — R9 decoder progress seam added; three implementation notes folded
into §R4 handle single-read, §R5 query-hook timing, §R7 hook-exception passthrough). No further
architectural review required before implementation.

**Date:** 2026-07-15 · **Branch (to cut, AFTER #183 merges):** `feat/sparkplug-b-k1.2d-capture`
**Plan-trail:** v1 → external review → v2 → **reality-check → v3 (frozen)**. **Supersedes:** v2
(same date). v3 folds the §15 reality-check against the *actual* code + Microsoft.Data.Sqlite
8.0.10 behavior. **Unchanged mechanics carry over from v2**; this doc restates only what the
reality check confirmed, changed, or deferred, plus the one high-risk redesign (handle
ownership §R4).

**Governing directive (locked, unchanged):** birth/cutover capture executes **under the
existing per-database ownership lock and writer mutex, on the single owning connection set,
with no secondary SQLite writer and no side-channel mutation path.**

**Gating:** implementation, branch creation, and committing this plan as the execution baseline
remain gated on **PR #183 merging**. The v1+v2+v3 trail is committed together after #183.

---

## R. Reality-check ledger (each §15 / review item → status + evidence)

| # | Item | Status | Evidence |
|---|------|--------|----------|
| 1 | Deferred-tx gives one coherent read snapshot across reads | **Confirmed (executable)** | Scratchpad probe `sqlite-snapshot-probe` on **MDS 8.0.10**: first read pins snapshot; a 2nd connection's commit (`next_sequence 100→200`, new row, updated row) is invisible to later reads in the same tx; post-commit fresh read sees 200. `RESULT: PASS`. |
| 1b | Deferred snapshot is established by the **first statement** | **Confirmed → locked** | Same probe; v3 §R2 makes `cutoff` (`ReadNextSequence`) the deliberate first read. |
| 2 | Commands must set `command.Transaction` | **Confirmed → changed** | Existing read helpers (`ReadMetaValue`, `ReadNextSequence`, `ReadCurrentGeneration`, `ReadCursorValue`) create commands with **no** `Transaction` (`SqliteRouteStore.cs:717`). `WriteMeta` shows the codebase pattern of setting `cmd.Transaction = tx` (`:687`). v3 adds **required-transaction** capture overloads (§R2). |
| 3 | "read-only transaction" wording | **Changed** | `deferred:true` is not read-only per se; it's read-only because K1.2d issues only `SELECT`s. Reworded (§R2). |
| 4 | Handle ownership fits the real factory/disposal path | **CHANGED (high-risk)** | `DefaultRouteBufferFactory.CreateAsync` (`:51`) does real orchestration — path resolution, **legacy-buffer migration shim** (`MigrateLegacyBufferIfPresent :128`), **quarantine→diagnostics wiring** (`:82`). "K1.3 opens `SqliteRouteStore` directly" (v2 §4) would **bypass** all of it. v3 routes the handle through the façade/factory (§R4). |
| 5 | "no manifest scan" via SELECT trigger | **Changed** | SQLite has no `SELECT` triggers. v3 uses structurally separate raw helpers + an optional query-kind hook (§R5). |
| 6 | Canonical-key collision can be manufactured | **Deferred (impossible today)** | `CanonicalMetricKey.Create` does **no** normalization (only non-empty `Require`), and `Equals` is `StringComparison.Ordinal` (`LatestValueSnapshot.cs:66,104`). Two distinct PK triples cannot collide. `TryAdd` stays as a defensive guard; the collision *fixture* is deferred until normalization exists (§R6). |
| 7 | Test seam must be non-racy / self-cleaning | **Changed** | v2's mutable `internal Action?` property risks cross-test leakage. v3 locks **constructor-injected immutable `SqliteRouteStoreTestHooks`** threaded through `OpenAsync` (private ctor already exists, `:132`) (§R7). |
| 8 | Read-tx cleanup shouldn't double-fail | **Changed** | Existing *write* paths call explicit `tx.Rollback()` in `catch` (`:1955` etc.). For the **read** capture tx there's nothing to undo; v3 relies on `using var tx` disposal to release and does **not** call explicit rollback (preserves the original exception) (§R2/§R8). |
| 9 | Cancellation during decode is deterministically testable | **Changed** | A 10k decode can finish before a timer fires. v3 extracts an **internal decoder** unit-tested directly with a pre-canceled token and a decode-progress hook — no `CancelAfter` timing (§R9). |
| 10 | Generation-filter row accumulation | **Confirmed** | `AdvanceGenerationAsync` writes only `meta.current_schema_generation` (`:1809`), never touches `latest_value`; `UpsertManifestLocked` overwrites a present metric's row by PK to the new gen, but a metric **removed** in a later generation leaves a permanent stale-gen row. So total rows can exceed current-gen rows. v3 benchmarks two datasets (§R10). |
| 15d | Two more interfaces under `TreatWarningsAsError` | **Low-risk, verify at impl** | `IReplayBoundaryProvider` + `IReplaySessionStateProvider` are ordinary interfaces; implementing both on `SqliteRouteStore` should not trip nullable/analyzer. Confirm on first build. |

**Net:** the transaction design is sound and now proven; the **highest-risk change is handle
ownership (§R4)**, exactly as the reviewer predicted.

## R2. Capture transaction + helper signatures (locked)

Both capture entry points open **one short read transaction on `_writer` under `_writerMutex`**;
`cutoff` (`ReadNextSequence`) is the **first** read so the deferred snapshot is pinned before any
other read; the tx ends **before** decode. Wording fix: *the transaction begins deferred and
K1.2d executes only `SELECT`s — its purpose is a coherent read snapshot, not enforcement of a
read-only connection.* Cleanup: `using var tx`; `Commit()` in the `try`; on `SqliteException`,
translate (§7) and let `using` disposal release — **no explicit `Rollback()`** in the capture
path (nothing to undo; avoids masking the original failure).

**Capture-specific helper overloads (required transaction — not optional/nullable):**
```csharp
private static long    ReadNextSequence(SqliteConnection c, SqliteTransaction tx);
private static long    ReadCurrentGeneration(SqliteConnection c, SqliteTransaction tx);
private static long?   ReadCursorValue(SqliteConnection c, SqliteTransaction tx, string sinkId);
private static IReadOnlyList<RawManifestRow> ReadCurrentGenerationManifest(
                          SqliteConnection c, SqliteTransaction tx, long generation);
```
Each sets `command.Transaction = tx`. A **required** `SqliteTransaction` (vs. optional nullable)
makes it impossible for a future caller to accidentally issue one capture read outside the
snapshot. Existing non-capture callers keep their connection-only overloads unchanged.

`RawManifestRow` is a deep copy (incl. a copied `byte[] Envelope`) — never an alias to a live
DB buffer — so off-lock decode/immutability (v2 §11 review-item 6) holds.

## R3. Capture algorithm

Unchanged from v2 §3 except: (a) the four reads use the R2 tx overloads; (b) `cutoff` is the
first read; (c) no explicit rollback; (d) the test seam is the injected hook (§R7), invoked
synchronously after `WaitAsync` and before `BeginTransaction`. Boundary-only path stays
lightweight (cursor + cutoff, **no manifest read**).

## R4. Handle ownership — REDESIGN (route through the façade/factory; do NOT open the owner directly)

**Why v2 §4 was wrong:** `DefaultRouteBufferFactory.CreateAsync` is the single construction path
and performs orchestration a raw `SqliteRouteStore.OpenAsync` would skip: buffer-path resolution,
the legacy `{dataPath}/config/buffer` → `{dataPath}/buffer` **migration shim**, and the
**quarantine→`IRoutingEngineDiagnostics`** callback wiring. A replay route that "opens the owner
directly" would lose store-and-forward backlog migration and the operator data-quality signal.

**v3 locks the façade-anchored handle** (both objects reference one owner; single disposal):
```csharp
internal sealed record SqliteRouteStoreHandle(
    IMessageBuffer Buffer,                               // the SqliteBuffer FAÇADE (what the route already holds)
    IReplayBoundaryProvider? ReplayBoundaryProvider,     // the owner (same instance the façade wraps)
    IReplaySessionStateProvider? ReplaySessionStateProvider);
```
- `SqliteBuffer.OpenAsync` is a *pure* wrapper (`SqliteBuffer.cs:44` → `SqliteRouteStore.OpenAsync`),
  so the façade adds no orchestration of its own — the **factory** does. Keep the factory as the
  one construction path.
- Add to the façade (delegating to `_store`, internal):
  ```csharp
  internal ValueTask<ReplayTrackingActivationResult> ActivateReplayStateTrackingAsync(
      string routeId, string replaySinkId, CancellationToken ct);   // → _store
  internal SqliteRouteStoreHandle GetCapabilityHandle();
  ```
  `Buffer` = the façade (`this`); the provider slots = the wrapped `_store` owner (which
  implements both interfaces). One owner, one `_writerMutex`, one `<db>.lock`, one connection set.
  **`GetCapabilityHandle()` must read tracking-state ONCE** into a local and use it for both
  provider slots, so it can never return one provider null and the other non-null:
  ```csharp
  var enabled = _store.IsReplayStateTrackingEnabled;
  return new SqliteRouteStoreHandle(this, enabled ? _store : null, enabled ? _store : null);
  ```
- **Disposal (single authority, both reachable):** `SqliteBuffer.DisposeAsync` already delegates
  to `_store.DisposeAsync` (`:96`), which is idempotent. The route disposes the façade through its
  normal `IMessageBuffer` lifecycle; that disposes the owner once; the handle is a non-disposable
  record and the providers are the owner (post-dispose calls throw the disposed-store error). No
  second disposal path is introduced.
- **Reaching the handle from the factory (K1.3 concern, not built here):** the factory returns
  `IMessageBuffer`. K1.2d does **not** rewire routing. It only provides `GetCapabilityHandle()` +
  `ActivateReplayStateTrackingAsync()` on the concrete `SqliteBuffer`, so K1.3 can extend the
  factory (e.g. an internal `CreateReplayCapableAsync` returning `(SqliteBuffer façade,
  SqliteRouteStoreHandle handle)`) **without** reopening the owner or duplicating the migration /
  quarantine orchestration. v3 does NOT lock that factory extension — it is flagged as the K1.2d↔K1.3
  seam and must be designed in K1.3 against this façade surface.

> This resolves v1's façade/owner contradiction the correct way: `Buffer` **is** the façade;
> providers **are** the owner it wraps; the factory stays the sole constructor.

## R5. Handle currency + "no manifest scan" test

- **Currency (O-B, unchanged):** `GetCapabilityHandle()` is a post-activation snapshot; a handle
  taken before activation keeps `null` providers forever (record is immutable). Test: before→null,
  activate, after→non-null, before still null.
- **"No manifest scan" (changed):** proven **structurally**, not via a (nonexistent) SELECT
  trigger. Two separate raw helpers — `ReadBoundaryRaw` (cursor + cutoff) vs.
  `ReadCurrentGenerationManifest` (the `latest_value` scan). The boundary test asserts, via the
  injected **query-kind hook** (`Action<CaptureQueryKind>? QueryExecuting`, §R7), that
  `CaptureQueryKind.ManifestScan` was **never** emitted during `CaptureReplayBoundaryAsync`. The
  `QueryExecuting` hook fires **immediately before each command executes** (an *executed* logical
  query), not at helper construction — so the assertion reflects real execution.
  (Fallback if the hook is deemed heavy: assert the boundary path calls only the boundary helper
  — a structural/refactor-level test. No production SQL-string parsing.)

## R6. Row validation + canonical-key collision (locked)

Per-row `ValidateRawRow` + `TryAdd` exactly as v2 §6, **except** the collision *fixture* is
**deferred**: `CanonicalMetricKey` does no normalization today, so no two distinct PK triples can
collide. v3 keeps `if (!values.TryAdd(key, lmv)) throw BufferCorrupt(...)` as a **defensive
future-proof guard** and documents that a genuine collision test lands only when/if canonical
normalization is introduced (do not fake it by mocking internals).

## R7. Test hooks — constructor-injected + immutable (locked)

```csharp
internal enum CaptureQueryKind { Boundary, ManifestScan }

internal sealed record SqliteRouteStoreTestHooks(
    Action? CaptureEnteredCriticalSection = null,        // fired after WaitAsync, before BeginTransaction (sync)
    Action<CaptureQueryKind>? QueryExecuting = null);     // fired as each capture query runs
```
- Threaded through `SqliteRouteStore.OpenAsync(..., SqliteRouteStoreTestHooks? testHooks = null)`
  into the existing private ctor (`:132`); production passes `null` (a single immutable field, no
  runtime mutation).
- The critical-section hook stays **synchronous** (no `await` in the mutex region). Tests wire it
  to a `ManualResetEventSlim` pair for deterministic append-vs-capture / generation-vs-capture
  ordering. Tests that inject hooks construct their own store instance (no shared-store mutation),
  so no cross-test leakage and no parallel-collision on a mutable seam.
- **Hook exceptions escape unchanged.** A throwing test hook must NOT be wrapped/translated into
  `BufferIoError`/`BufferCorrupt`/`RouteStoreEnvelopeUnsupported` — it propagates as-is. (Hooks run
  outside the `SqliteException`/decode `catch` scopes, or are explicitly excluded from them.)

## R8. (folded into R2 — read-tx cleanup: no explicit rollback in the capture path.)

## R9. Deterministic cancellation (locked — with the decoder progress seam)

Extract the off-lock decode into an internal, independently-testable method **with an optional
progress callback** so the later-row cancellation test is deterministic without timing:
```csharp
internal static LatestValueSnapshot BuildSnapshotFromRawRows(
    IReadOnlyList<RawManifestRow> rows,
    long generation,
    long cutoff,
    CancellationToken cancellationToken,
    Action<int>? rowDecodedForTest = null);   // internal, optional; null in production
```
Loop shape:
```csharp
for (var i = 0; i < rows.Count; i++)
{
    if ((i & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
    ValidateRawRow(rows[i], generation, cutoff);          // §R6
    var lmv = LatestValueEnvelopeV1.Decode(...);
    if (!values.TryAdd(key, lmv)) throw BufferCorrupt(...);
    rowDecodedForTest?.Invoke(i);                          // fired AFTER each row is decoded
}
```
- The `rowDecodedForTest` callback is internal, optional, and used **only** by direct decoder
  tests — it does **not** join `SqliteRouteStoreTestHooks` (decode runs after the store mutex is
  released and is tested in isolation).
- **Tests (deterministic, no timers):** (a) an **already-canceled** token → throws before any
  decode (the `i==0` check); (b) `rowDecodedForTest` cancels the `CancellationTokenSource` **after
  row 255**, so the next periodic check **at row 256** throws — proving *periodic* cancellation, not
  only entry cancellation — returns no partial snapshot, and the store stays usable (capture again
  succeeds). Provider methods call this after releasing the mutex/tx, so cancellation is always
  post-capture and safe (no durable mutation, nothing persisted).

## R10. Performance datasets (O-D, locked)

No `schema_generation` index in K1.2d (measure first; an index adds write amplification to every
manifest upsert — `UpsertManifestLocked`). Report per capture: total `latest_value` rows,
current-generation rows returned, **under-lock (tx) duration**, **off-lock decode duration**, total
duration, and the **total : current-gen ratio**. Benchmark **two** datasets, because removed
metrics leave permanent stale-gen rows (§R ledger #10):
1. total ≈ current-gen (no schema churn);
2. many stale-generation rows + a smaller current-gen subset (repeated advances with metric removal).

Linear growth in current-gen rows is expected and **does not** block merge; **super-linear** growth
does. A large total:current-gen ratio may later justify a stale-row cleanup policy (delete
`schema_generation < current` on advance) — **out of K1.2d scope**, noted for K1.2e.

## R11. Tests (v2 §11 set, adjusted by the ledger)

Carry v2 §11 with these edits: **duplicate-canonical-key fixture deferred** (§R6, keep the guard);
**no-manifest-scan** asserted via the query-kind hook / structural separation (§R5); **cancellation**
via the internal decoder + already-canceled/gated token (§R9); **concurrency** and
**generation-vs-capture** ordering via the constructor-injected critical-section hook (§R7) with the
exact included/excluded assertions (v2 §9). Boundary, birth/cutover, coherence, restart/rehydrate,
handle before/after-activation, disposed-store, and raw-envelope-ownership tests unchanged. Full
Core.Tests + Management.Tests green before PR; `Category!=Flaky`; no `Thread.Sleep`.

## R12. Implementation sequence (v2 §13, with R-changes)

1. **Shared raw-capture primitives** — R2 tx overloads (required `SqliteTransaction`); `ReadBoundaryRaw`
   (cursor+cutoff) and `ReadCurrentGenerationManifest` (deep-copied `RawManifestRow[]`); constructor
   hooks (§R7). Tests for coherent raw capture + §7 error families.
2. **Boundary provider** — `CaptureReplayBoundaryAsync` via `ReadBoundaryRaw` only; boundary +
   restart-stability + **no-manifest-scan** (§R5) tests.
3. **Session-state provider** — `CaptureBirthStateAsync`/`CaptureCutoverAsync`; `BuildSnapshotFromRawRows`
   (§R9) off-lock with validation (§R6) + error translation (§7); birth/cutover/coherence/corruption/
   cancellation tests.
4. **Capability handle** — `SqliteBuffer.GetCapabilityHandle()` + `ActivateReplayStateTrackingAsync()`
   façade delegates (§R4); before/after-activation snapshot + disposal tests.
5. **Deterministic concurrency + perf evidence** — critical-section hook schedules (append/capture,
   generation/capture); O-D two-dataset measurements; full regressions + hygiene scan.

## R13. Definition of done

As v2 §14, plus: capture uses the **R2 required-transaction helpers inside one deferred read tx**
(snapshot proven, §R ledger #1); the handle is **façade-anchored** (§R4) with the factory left as the
sole construction path (no owner-direct-open); test instrumentation is **constructor-injected +
immutable** (§R7); cancellation tested via the **internal decoder** (§R9); O-D **two-dataset** perf
evidence reported; duplicate-key guard retained with its fixture deferred (§R6). Reality-check
complete — this is the execution baseline for the K1.2d PR once #183 merges.

## R14. Residual assumptions to confirm at first build (low-risk)

- Implementing both provider interfaces on `SqliteRouteStore` is clean under `TreatWarningsAsError`
  (ledger 15d).
- Adding `SqliteRouteStoreTestHooks? = null` to `OpenAsync` + the private ctor doesn't disturb the
  disposed/enabled checks or the existing `SqliteBuffer`/test callers (they pass nothing).
- The K1.2d↔K1.3 factory seam (how a replay route obtains façade + handle) is designed in **K1.3**
  against the §R4 façade surface — K1.2d must not pre-lock the factory extension.
