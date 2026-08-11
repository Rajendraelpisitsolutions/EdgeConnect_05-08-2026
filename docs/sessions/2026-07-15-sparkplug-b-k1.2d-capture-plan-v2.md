# Sparkplug B — K1.2d capture-providers execution plan (v2, post-review)

**Date:** 2026-07-15 · **Branch (to cut):** `feat/sparkplug-b-k1.2d-capture` (fresh from
`master` after PR #183 merges) · **Plan-trail:** v1 → **external review** → **v2** →
reality-check → v3. **Supersedes:** v1 (same date). Every v1 open item (O-A…O-E) is now
locked (§12); the two v1 blockers and all clarifications are folded in.

**Governing directive (locked):** birth/cutover capture executes **under the existing
per-database ownership lock and writer mutex, on the single owning connection set, with no
secondary SQLite writer and no side-channel mutation path.** K1.3 must reuse this discipline.

---

## 0. Where this sits (unchanged from v1)

K1.1 contracts (#180) → K1.2a owner/façade (#181) → K1.2b migration + activation +
generation CAS (#182) → K1.2c codec + atomic tracked append (#183) → **K1.2d captures (this
plan)** → K1.2e corruption matrix + perf gates. K1.2c can fill the `latest_value` manifest;
K1.2d makes it readable as a coherent birth/cutover state and exposes the two optional
capabilities via a handle. **No** route/Sparkplug wiring.

## 1. Scope

**In:**
1. `IReplayBoundaryProvider.CaptureReplayBoundaryAsync` on `SqliteRouteStore` — boundary-only
   (cursor + cutoff), **does not load the manifest** (§3a).
2. `IReplaySessionStateProvider` on `SqliteRouteStore`:
   `CaptureBirthStateAsync(routeId, sinkId, ct)` → `ReplaySessionStartState`;
   `CaptureCutoverAsync(routeId, ct)` → `ReplaySessionCutoverState` (§3b).
3. `SqliteRouteStoreHandle` capability handle with **pinned** ownership (§4) and
   **snapshot-after-activation** currency (§5).
4. The **short-read-transaction-under-mutex → deep-copy → decode-off-lock** algorithm (§3),
   the fail-closed row-validation contract (§6), the error-translation table (§7), and
   cancellation policy (§8).

**Out (later):** `RouteWorker`/route wiring; Sparkplug assembly/actor/NBIRTH-NDATA;
`IReplayAwareSinkAdapter` consumption; lifecycle-record construction
(`ReplaySessionStart/Rebirth/Cutover`, `RebirthRequest`) — K1.2d returns the *state objects*,
K1.3 carries them intact. Full corruption matrix + final perf gates → K1.2e.

## 2. Blocker 1 resolved — O-A locked: writer connection + ONE short read transaction

v1 proposed several independent autocommit reads under the mutex. **v2 locks:** all raw reads
for a capture run inside **one short read transaction on the existing `_writer` connection,
under `_writerMutex`**:

```
_writer.BeginTransaction(deferred: true)   // shared read lock; WAL snapshots at first read
  read cutoff (next_sequence), generation, cursor (birth only), raw manifest rows
COMMIT                                       // ends BEFORE any envelope decode
```

This makes atomicity a **database-enforced snapshot** — cutoff, generation, cursor, and the
manifest rows all come from one consistent WAL read-snapshot — rather than an inference that
"all current writers honor the mutex." It still honors every locked directive: no new
connection, no secondary writer, no side-channel, same ownership lock, same writer mutex. It
also stays correct if a future diagnostic/migration/recovery path reads outside the expected
sequence. **No reader connection is used.** `deferred: true` takes only a read lock (the
capture never writes); the transaction is read-only and ends before decode.

## 3. Capture algorithm

### 3a. Boundary-only (lightweight — does NOT load the manifest)

```
CaptureReplayBoundaryAsync(sinkId):
  ct.ThrowIfCancellationRequested()
  await _writerMutex.WaitAsync(ct)
  try:
    ThrowIfDisposed()
    require tracking enabled            -> RouteStoreReplayTrackingNotEnabled
    _testHook_CaptureEnteredCriticalSection?.Invoke()   // §9 seam; null in production; synchronous
    tx = _writer.BeginTransaction(deferred: true)
    try:
      cutoff = ReadNextSequence(_writer, tx)
      cursor = ReadCursorValue(_writer, tx, sinkId)
      require cursor != null            -> RouteStoreSinkCursorNotFound
      require 0 <= cursor <= cutoff      -> BufferCursorInconsistent
      tx.Commit()
    catch: tx.Rollback(); translate (§7)
  finally: _writerMutex.Release()
  return ReplayBoundary.Create(cursor, cutoff)     // no manifest load, no decode
```

### 3b. Session state (birth / cutover — loads + decodes the manifest)

```
Capture{Birth|Cutover}:
  ct.ThrowIfCancellationRequested()
  await _writerMutex.WaitAsync(ct)
  try:
    ThrowIfDisposed()
    require tracking enabled            -> RouteStoreReplayTrackingNotEnabled
    require routeId == BufferId          -> RouteStoreRouteMismatch
    _testHook_CaptureEnteredCriticalSection?.Invoke()   // §9 seam
    tx = _writer.BeginTransaction(deferred: true)
    try:
      cutoff = ReadNextSequence(_writer, tx)
      gen    = ReadCurrentGeneration(_writer, tx)
      (birth) cursor = ReadCursorValue(_writer, tx, sinkId)
              require cursor != null      -> RouteStoreSinkCursorNotFound
              require 0 <= cursor <= cutoff -> BufferCursorInconsistent
      rawRows = deep-copy of
                SELECT source_instance_id, device_id, tag_path, value_type,
                       route_buffer_sequence, schema_generation, envelope   // envelope bytes copied
                FROM latest_value WHERE schema_generation = $gen
      tx.Commit()
    catch: tx.Rollback(); translate (§7)
  finally: _writerMutex.Release()

  // ---- OFF-LOCK: validate + decode from the deep-copied rows (§6, §8) ----
  values = new Dictionary<CanonicalMetricKey, LatestMetricValue>()
  for i, row in rawRows:
     if (i & 0xFF) == 0: ct.ThrowIfCancellationRequested()    // §8
     ValidateRawRow(row, gen, cutoff)                          // §6 — fail closed
     key = CanonicalMetricKey.Create(row.src, row.dev, row.tag)
     lmv = LatestValueEnvelopeV1.Decode(row.envelope, key,
                                        (CanonicalValueType)row.value_type, row.route_buffer_sequence)
     if !values.TryAdd(key, lmv): throw BufferCorrupt("duplicate canonical metric identity")   // §6
  snapshot = new LatestValueSnapshot(RouteSchemaGeneration.Create(gen), values)

  birth :  return ReplaySessionStartState.Create(ReplayBoundary.Create(cursor, cutoff), snapshot)
  cutover: return ReplaySessionCutoverState.Create(cutoff, snapshot)
```

Coherence is enforced three ways: the single read-snapshot (§2), the per-row cutoff check
(§6), and the `Create` factories (reject any snapshot value at/beyond the cutoff). Any
violation ⇒ fail closed; never a filtered/partial snapshot.

## 4. Clarification resolved — capability handle ownership (exact objects)

```csharp
internal sealed record SqliteRouteStoreHandle(
    IMessageBuffer Buffer,
    IReplayBoundaryProvider? ReplayBoundaryProvider,
    IReplaySessionStateProvider? ReplaySessionStateProvider);
```

**v1's contradiction ("Buffer = the façade" while "SqliteRouteStore implements IMessageBuffer")
is resolved by making the handle owner-only:**

- **All three slots reference the one `SqliteRouteStore` owner instance.** `Buffer` is the
  **owner** as `IMessageBuffer` (NOT the `SqliteBuffer` façade). `SqliteRouteStore` also
  implements `IReplayBoundaryProvider` + `IReplaySessionStateProvider`, so the provider slots
  are the same instance exposed through those interfaces. One object, one `_writerMutex`, one
  `<db>.lock`, one connection set, one `DisposeAsync`.
- **Disposal authority = the owner.** `SqliteRouteStore.DisposeAsync` is the sole, idempotent
  teardown. The handle is a plain record — **not disposable**, holds no resource, opens
  nothing. Obtaining it adds no disposal path. The provider references are the owner itself
  (not separate view objects), so they cannot be disposed independently.
- **The `SqliteBuffer` façade is unrelated to the handle.** It remains the public entry point
  for non-replay routes (delegates `IMessageBuffer` + `DisposeAsync` to an owner it wraps). A
  **replay-capable route** (K1.3, inside Core, `InternalsVisibleTo`) opens and owns a
  `SqliteRouteStore` directly, obtains the handle, and disposes that one owner once at route
  teardown. A given route uses the façade **or** the owner+handle, never both — so there is no
  two-reachable-objects double-dispose ambiguity. (Public `SqliteBuffer` surface stays stable
  per v3 §4c; it simply isn't what the replay handle carries.)

Ownership shape:
```
SqliteRouteStore  — owns connections, <db>.lock, _writerMutex, reclaim loop, disposal;
                    implements IMessageBuffer + both replay providers.
SqliteBuffer      — public façade for non-replay routes; delegates to an owner; no independent
                    resource ownership. NOT part of the replay handle.
SqliteRouteStoreHandle — non-disposable record; all three slots are the single owner.
```

## 5. Clarification resolved — handle currency is an explicit snapshot (O-B option 1)

Capability exposure is a **snapshot taken after activation**, made explicit in the API (not
convention):

```csharp
var activation = await store.ActivateReplayStateTrackingAsync(routeId, replaySinkId, ct); // one-way
var handle     = store.GetCapabilityHandle();   // providers non-null because now enabled
```

- `internal SqliteRouteStoreHandle GetCapabilityHandle()` returns
  `new(this, this, this)` when `IsReplayStateTrackingEnabled`, else `new(this, null, null)`.
- **A handle is immutable once issued.** A handle obtained *before* activation keeps `null`
  providers forever; activation never mutates an already-issued handle from null → non-null.
  K1.3 obtains a fresh handle *after* activating.
- Test (locked): `handleBefore.providers == null` → activate →
  `handleAfter.providers != null` → `handleBefore.providers still == null`.

## 6. Fail-closed row-validation contract (stronger than v1)

`ValidateRawRow(row, capturedGen, cutoff)` runs per row **before** `Decode`, and the
dictionary insert uses `TryAdd`. Filtering with `WHERE schema_generation = $gen` does **not**
remove the need to select and re-check `schema_generation` — corruption or a future query
change must not bypass validation. Fail closed on any of:

- `schema_generation` is malformed **or != capturedGen**            → `BufferCorrupt`;
- `route_buffer_sequence < 0` **or >= cutoff**                       → `BufferCursorInconsistent`;
- `value_type` is not a defined `CanonicalValueType` member          → `BufferCorrupt`;
- any identity column (`source_instance_id`/`device_id`/`tag_path`) is null / not canonicalizable → `BufferCorrupt`;
- `envelope` is null                                                 → `BufferCorrupt` (deep-copied at capture, never aliased to a DB buffer);
- decoded key / type / sequence disagree with the columns            → `RouteStoreEnvelopeUnsupported` (cross-check already in `LatestValueEnvelopeV1.Decode`);
- **duplicate canonical metric identity** (`!values.TryAdd`)          → `BufferCorrupt` ("two DB triples canonicalize to one key"). Even though the PK forbids identical raw triples, canonical construction may normalize casing/whitespace/separators; a collision is corruption, not a silent overwrite.

## 7. Error-translation table (pinned)

No exception from a corrupted persisted row leaks as `ArgumentException`, `InvalidCastException`,
or a raw construction failure. The capture boundary maps to exactly:

| Failure | Code |
|---|---|
| envelope decode / envelope↔column mismatch | `RouteStoreEnvelopeUnsupported` |
| structural row inconsistency (bad/mismatched generation, undefined value_type, null identity, duplicate canonical id, null envelope) | `BufferCorrupt` |
| cutoff / cursor / session coherence violation (seq ≥ cutoff, cursor out of `[0, cutoff]`) | `BufferCursorInconsistent` |
| tracking disabled / route mismatch / unknown sink | `RouteStoreReplayTrackingNotEnabled` / `RouteStoreRouteMismatch` / `RouteStoreSinkCursorNotFound` |
| SQLite read failure inside the tx | `TranslateSqliteException` (→ `BufferIoError` / `BufferCorrupt`) |
| cancellation | `OperationCanceledException` (unchanged) |

**No new error code is expected.** If review finds a capture failure none of these describe,
add it in `CoreErrors.cs` then — don't pre-invent.

## 8. Cancellation policy (pinned)

- **Inside the mutex / read transaction:** honor cancellation only at entry
  (`ct.ThrowIfCancellationRequested()` before `WaitAsync`). Do **not** check between the
  coherent reads — the short read tx is `Commit`/`Rollback`-bounded and must complete or
  cleanly roll back; no partial tx state escapes.
- **Off-lock decode:** once rows are deep-copied and the mutex/tx released, honor cancellation
  every 256 rows (`if ((i & 0xFF) == 0) ct.ThrowIfCancellationRequested()`). Safe: no durable
  mutation occurred, nothing is persisted, and no partial snapshot is returned (the method
  throws instead of returning a half-built `LatestValueSnapshot`). The store is not poisoned.

## 9. Blocker 2 resolved — deterministic concurrency test seam

The post-mutex capture region is synchronous, so there is no natural async point to gate.
v2 adds a **synchronous, default-null internal seam** invoked *after* `_writerMutex` is
acquired and *before* the read transaction — preserving the "no await inside the mutex region"
production invariant:

```csharp
// null in production; set only by tests.
internal Action? CaptureEnteredCriticalSectionForTest;
```

A test sets it to signal one `ManualResetEventSlim` and block on another, deterministically
ordering an append against a capture without `Thread.Sleep`. Both schedules are proven:

1. **Capture-first:** capture holds the mutex at the seam; a second task's `AppendAsync`
   blocks on the mutex; the append's row (seq == cutoff) is **excluded**; after capture
   releases, the append lands.
2. **Append-first:** the append completes before the capture enters the seam; its row
   (seq < the new cutoff) is **included**.

Exact permitted outcomes asserted (no torn states):
```
included ⇒ metric.sequence <  cutoff  AND snapshot contains it
excluded ⇒ metric.sequence >= cutoff  AND snapshot omits it
never: cutoff includes the sequence but the manifest omits the value, or vice versa
```

## 10. §6→ "internal consistency" (O-C deferred) + boundary tests

`InMemoryBuffer` still does not implement `IReplayBoundaryProvider`; adding it now is untested
surface with no consumer, so **O-C is deferred**. The former "boundary parity" section is
renamed **"SQLite provider internal consistency"**, tested as:
1. standalone `CaptureReplayBoundaryAsync(sink)` equals the boundary inside
   `CaptureBirthStateAsync(routeId, sink)` for the same state;
2. `FirstPendingSequence == cursor`, `CutoffExclusive == next_sequence`;
3. `HasBacklog == (cursor < next_sequence)`;
4. boundary stable across close/reopen;
5. cursor-at-cutoff (caught up), cursor-zero, and empty-store boundaries.

## 11. Tests (v2 set — v1 §7 plus review additions)

**Boundary:** caught-up → `!HasBacklog`, `First==Cutoff`; behind → `HasBacklog`,
`First<Cutoff`; unknown sink → `RouteStoreSinkCursorNotFound`; disabled →
`RouteStoreReplayTrackingNotEnabled`; route-id mismatch → `RouteStoreRouteMismatch`; stable
across reopen; boundary-only path issues **no** `latest_value` read (assert via a trigger or
row-count instrumentation that the manifest isn't scanned).

**Birth/cutover:** empty manifest → empty snapshot at current gen; N metrics → N current-gen
values, each field matches; older-generation rows excluded after an advance; coherence
(`MaxRouteBufferSequence < cutoff`); cutover cutoff == `next_sequence`.

**Fail-closed (review §additions 1, 6, 7, 9):**
1. duplicate canonical-key → `BufferCorrupt`, no overwrite;
6. raw-envelope ownership: mutate the source buffer after append/capture → returned snapshot
   unchanged (deep copy proven);
7. **generation-change serialization:** a capture and `AdvanceGenerationAsync` cannot yield a
   snapshot whose generation and rows come from different generations (seam-ordered, both
   schedules);
9. at least one structural corrupted-row case each: bad/mismatched `schema_generation`,
   undefined `value_type`, null identity, corrupted `envelope`, forced `seq >= cutoff`
   (→ the §7 codes). Full matrix stays K1.2e.

**Cancellation (review §2):** cancel during off-lock decode → `OperationCanceledException`,
no partial state, store still usable afterward (capture again succeeds).

**Capability handle (review §3, 4):** disabled → both providers null, `Buffer` non-null +
usable; enabled → both non-null + functional; before/after-activation snapshot semantics (§5);
capture after disposal → the established disposed-store error; disposing the owner is
idempotent, no second connection/loop (reinforce v3.1 §3).

**Restart/rehydrate:** append → capture birth → reopen → capture birth → identical snapshot
(values + generation) and boundary.

**Immutability (review §5):** repeated captures without mutation are value-equal but do **not**
share mutable backing storage.

Determinism: `Category!=Flaky`, no `Thread.Sleep`; full **Core.Tests** + **Management.Tests**
green before PR.

## 12. Locked decisions (were v1 O-A…O-E)

- **O-A → LOCKED:** existing writer connection + **one short read transaction** under
  `_writerMutex`; transaction ends before decode; **no reader connection**.
- **O-B → LOCKED (option 1):** capability handle is a post-activation snapshot; disabled →
  null providers; already-issued handles are immutable; add the stale-handle test.
- **O-C → DEFERRED:** do not add `IReplayBoundaryProvider` to `InMemoryBuffer`; rename to
  "SQLite provider internal consistency".
- **O-D → MEASURE FIRST:** no `schema_generation` index in K1.2d. Report per capture: total
  `latest_value` rows, current-generation rows returned, **under-lock (tx) duration**,
  **off-lock decode duration**, total duration, and the **total : current-gen ratio** (a large
  ratio from accumulated removed-metric rows may later justify an index or a cleanup policy).
  **Correction to v1:** linear growth is expected and does **not** block merge; only
  **super-linear** growth blocks. An index adds write amplification to every manifest upsert,
  so it is justified only by measured need.
- **O-E → LOCKED (route-wide):** `CaptureCutoverAsync` takes only `routeId` (per the K1.1
  contract); the cutover state is the route's latest-value baseline at cutoff C. No `sinkId`
  unless K1.3 finds a concrete need the replay boundary can't express.

## 13. Implementation sequence (reviewer's revised order)

1. **Shared raw-capture primitive** — writer mutex + short read tx on `_writer`; read &
   deep-copy cutoff/generation/cursor/rows; commit + release. Tests for coherent raw capture +
   the §7 error families. (Boundary-only variant reads just cursor + cutoff — see step 2.)
2. **Boundary provider** — `CaptureReplayBoundaryAsync` using the **lightweight** boundary-only
   tx (cursor + cutoff, **no manifest load**). Boundary + restart-stability tests, incl. the
   "no manifest scan" assertion.
3. **Session-state provider** — `CaptureBirthStateAsync` / `CaptureCutoverAsync`: off-lock
   decode, cancellation, duplicate detection, error translation; birth/cutover/coherence/
   corruption tests.
4. **Capability handle** — `GetCapabilityHandle()` + exact owner/handle lifecycle (§4);
   before/after-activation semantics (§5); disposal tests.
5. **Deterministic concurrency + performance evidence** — the §9 seam; append/capture and
   generation/capture schedules; 100/1k/10k measurements (the O-D metric set); full Core +
   Management regressions; diff-hygiene scan (no `RouteWorker`/Sparkplug symbols).

## 14. Definition of done

Core + solution 0/0; full Core.Tests + Management.Tests green; all §10–§11 tests present/green;
both providers implemented and exposed via the handle **only when enabled**, with immutable
post-activation snapshot semantics; capture runs **exclusively under the ownership lock +
writer mutex inside one short read transaction on the single owning connection, no second
writer / side-channel** (asserted); decode happens off-lock from deep-copied rows with
periodic cancellation; the §6 validation + §7 error families enforced; boundary-only path
proven not to scan the manifest; O-D metric set reported; **no** `RouteWorker`/Sparkplug/actor
code; single disposal authority preserved (D10 reclaim loop untouched). Then reality-check
pass (§15) → v3 → production PR.

## 15. For the reality-check pass (→ v3)

Confirm against the code before implementing: (a) `Microsoft.Data.Sqlite` `deferred: true`
gives a stable WAL read-snapshot across multiple `SELECT`s in one transaction on the writer
connection; (b) `ReadNextSequence`/`ReadCurrentGeneration`/`ReadCursorValue` can each take an
optional `SqliteTransaction` (K1.2b/c wrote them without one — a small signature add); (c) the
`CaptureEnteredCriticalSectionForTest` seam placement doesn't perturb the disposed/enabled
checks; (d) `SqliteRouteStore` implementing two more interfaces raises no analyzer/nullable
issue under `TreatWarningsAsError`; (e) K1.3's ownership assumption (it opens and disposes the
`SqliteRouteStore` owner directly for replay routes) matches the intended route-composition
design.
