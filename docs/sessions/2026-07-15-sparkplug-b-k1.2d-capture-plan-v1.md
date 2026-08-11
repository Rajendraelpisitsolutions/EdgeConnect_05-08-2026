# Sparkplug B — K1.2d capture-providers execution plan (v1, for review)

**Date:** 2026-07-15 · **Branch (to cut):** `feat/sparkplug-b-k1.2d-capture` (fresh from
`master` after PR #183 merges) · **Plan-trail:** **v1** → external review → v2 → reality-check → v3.
**Supersedes for K1.2d only:** the reader-transaction capture sketch in
`2026-07-14-sparkplug-b-k1.2-route-store-plan-v3.md` §9 — see §2 below for why v1 changes it.

**Governing directive (locked for this milestone):** birth/cutover capture operations
**must execute under the existing per-database ownership lock and writer mutex, with no
secondary SQLite writer and no side-channel mutation path.** Every design choice below
serves that boundary. K1.3 must reuse the same discipline; it must not introduce a
capture-time writer of its own.

---

## 0. Where this sits

K1.2 route-store chain: K1.1 contracts (#180) → K1.2a owner/façade (#181) → K1.2b
migration + activation + generation CAS (#182) → **K1.2c codec + atomic tracked append
(#183, in review)** → **K1.2d captures (this plan)** → K1.2e corruption/error + perf gates.

K1.2c left the store able to *fill* the `latest_value` manifest atomically. K1.2d makes the
manifest *readable as a coherent replay-session birth/cutover state*, and exposes the two
optional capabilities through a capability handle — **without** any route/Sparkplug wiring
(that stays K1.3+).

## 1. Scope (LOCKED proposal — confirm in review)

**In:**

1. **`IReplayBoundaryProvider.CaptureReplayBoundaryAsync`** on `SqliteRouteStore` — atomic
   capture of one sink's `ReplayBoundary` (`FirstPendingSequence` = the sink cursor,
   `CutoffExclusive` = the authoritative `next_sequence`).
2. **`IReplaySessionStateProvider`** on `SqliteRouteStore`:
   - `CaptureBirthStateAsync(routeId, sinkId, ct)` → `ReplaySessionStartState`
     (boundary H + birth `LatestValueSnapshot` as of H, current generation).
   - `CaptureCutoverAsync(routeId, ct)` → `ReplaySessionCutoverState`
     (catch-up cutoff C = current `next_sequence` + snapshot as of C).
3. **`SqliteRouteStoreHandle`** capability handle (v3 §4c): `IMessageBuffer` always present;
   the two replay providers present **iff** replay-state tracking is enabled, else `null`.
4. The **read-under-mutex → decode-after-release** capture algorithm (§3), the manifest
   envelope cross-checks on decode (reuse K1.2c `LatestValueEnvelopeV1.Decode`), and
   fail-closed handling of any incoherent row.

**Out (later milestones):**
- Any `RouteWorker` / route-path wiring; the Sparkplug assembly, actor, NBIRTH/NDATA
  encoding; `IReplayAwareSinkAdapter` consumption (K1.3 / K2 / K3).
- Constructing the lifecycle records (`ReplaySessionStart/Rebirth/Cutover`, `RebirthRequest`)
  — K1.2d returns the *state objects*; K1.3 carries them intact into the lifecycle.
- Corruption-matrix hardening + the perf gates (K1.2e).

## 2. Why v1 changes plan v3 §9 (reader-tx → writer-mutex)

Plan v3 §9 sketched the capture on the **reader connection inside a read transaction**
(WAL snapshot), decoding after `COMMIT`. That is correct in isolation, but the K1.2d
directive requires the capture to run **under the writer mutex** so it is serialized with
appends through the one owning instance, with no second connection acting as a concurrent
reader-of-record. v1 therefore adopts:

- **Acquire `_writerMutex`** (the same mutex `AppendAsync`/reclaim/activation take) → no
  append, reclaim, activation, or generation advance can interleave with the raw read.
- **Read the raw bytes on the writer connection** (autocommit `SELECT`s): under the mutex
  the writer connection's last-committed state *is* the current state, so no reader-snapshot
  staleness and no read-transaction to hold open. This keeps everything on the single
  owning connection set — honoring "no secondary SQLite writer / side-channel".
- **Release the mutex, THEN decode + validate + build the immutable state** in memory from
  the captured raw bytes. Decoding 10k envelopes off the lock preserves v3 §9's intent
  (don't hold the serialization boundary during CPU-bound decode) while satisfying the
  directive (the *read* that defines the instant happens under the mutex).

The captured raw rows are an in-memory copy, so releasing the mutex before decode cannot
tear the snapshot: `CutoffExclusive` = `next_sequence` at capture is fixed, and every
captured row already has `route_buffer_sequence < CutoffExclusive` by the append invariant
(§10 of the K1.2c path sets the row sequence then advances `next_sequence` to `last+1`).

> **Open item O-A (for review):** is reading on the *writer* connection under the mutex the
> preferred boundary, or should v1 instead take a short reader-connection read-tx *while
> holding the writer mutex* (belt-and-suspenders snapshot)? Recommendation: writer
> connection only — simpler, no reader-tx lifetime to manage, and the mutex already
> guarantees coherence. Flagged because it is the one place v1 diverges from v3 §9.

## 3. Capture algorithm (both entry points share it)

```
CaptureBirthStateAsync(routeId, sinkId) / CaptureCutoverAsync(routeId):
  cancellationToken.ThrowIfCancellationRequested()
  await _writerMutex.WaitAsync(ct)            // same mutex as AppendAsync — serialized, no side-channel
  try:
    ThrowIfDisposed()
    require replay tracking enabled           -> RouteStoreReplayTrackingNotEnabled
    require routeId == BufferId                -> RouteStoreRouteMismatch
    cutoff   = ReadNextSequence(_writer)       // authoritative append head (K1.2c)
    gen      = ReadCurrentGeneration(_writer)  // snapshot's generation
    (birth only) cursor = ReadCursorValue(_writer, sinkId)
                 require cursor != null         -> RouteStoreSinkCursorNotFound
                 require 0 <= cursor <= cutoff  -> BufferCursorInconsistent
    rawRows  = SELECT source_instance_id, device_id, tag_path, value_type,
                      route_buffer_sequence, schema_generation, envelope
               FROM latest_value WHERE schema_generation = gen        // current-generation only
               (read on _writer, autocommit, under the mutex)
  finally:
    _writerMutex.Release()

  // ---- decode + validate OFF the lock, from the captured bytes ----
  values = {}
  for row in rawRows:
     if row.route_buffer_sequence >= cutoff  -> BufferCursorInconsistent   // defensive; impossible under mutex
     key = CanonicalMetricKey.Create(row.src, row.dev, row.tag)
     lmv = LatestValueEnvelopeV1.Decode(row.envelope, key,
                                        (CanonicalValueType)row.value_type,
                                        row.route_buffer_sequence)          // envelope↔column cross-checks (K1.2c)
     values[key] = lmv
  snapshot = new LatestValueSnapshot(RouteSchemaGeneration.Create(gen), values)

  birth :  return ReplaySessionStartState.Create(ReplayBoundary.Create(cursor, cutoff), snapshot)
  cutover: return ReplaySessionCutoverState.Create(cutoff, snapshot)
```

Coherence is guaranteed twice: structurally (append sets row seq then bumps `next_sequence`,
so `maxSeq < cutoff` always) and by the `Create` factories
(`ReplaySessionStartState`/`ReplaySessionCutoverState` reject a snapshot value at/beyond the
cutoff). Any violation ⇒ **fail closed**, never a filtered/partial snapshot.

**Only current-generation rows** enter the snapshot (`WHERE schema_generation = gen`). After
K1.2b/c a generation advance requires the replay sink to be drained to the head, so no
below-cutoff older-generation rows should exist; excluding them by filter is defensive and
keeps a removed metric from re-announcing (ADR-0036 Rule 6 / birth-before-DATA).

## 4. Capability handle (v3 §4c, honest optionality)

```csharp
internal sealed record SqliteRouteStoreHandle(
    IMessageBuffer Buffer,                               // the façade (SqliteBuffer) — always present
    IReplayBoundaryProvider? ReplayBoundaryProvider,     // non-null iff tracking enabled
    IReplaySessionStateProvider? ReplaySessionStateProvider);
```

- `SqliteRouteStore` implements `IMessageBuffer` (already), and adds
  `IReplayBoundaryProvider` + `IReplaySessionStateProvider`.
- An internal factory returns the handle reflecting **current** tracking state: disabled →
  both providers `null`; enabled → both reference the single store owner.
- **Single disposal authority (v3.1 §3):** the handle and the providers are *views* over the
  store. None opens a connection, starts a reclaim loop, or is separately disposable. Only
  `SqliteRouteStore.DisposeAsync` tears down; the handle holds no disposable state.

> **Open item O-B (for review):** activation is one-way but happens at runtime (K1.2b
> `ActivateReplayStateTrackingAsync`). Options for handle currency: (1) K1.3 calls
> `GetCapabilityHandle()` *after* activating (recommended — matches one-way lifecycle, keeps
> the handle a simple snapshot); (2) providers are always non-null but each method throws
> `RouteStoreReplayTrackingNotEnabled` while disabled. Recommendation: (1). Confirm.

## 5. Concurrency / ownership boundary (the directive, made concrete)

- **One mutex, one lock, one connection set.** Capture takes `_writerMutex`; the store holds
  the exclusive `<db>.lock` for its lifetime (K1.2a, `RouteStoreAlreadyOwned`). No second
  writer or reader-of-record is introduced. The capture read uses the existing `_writer`
  connection; no new connection is opened.
- **No await inside the mutex region** except `WaitAsync` itself — the raw read is
  synchronous `SELECT`s — so nothing can advance the generation, `next_sequence`, or a
  cursor between the reads that define one capture instant.
- **Reclaim interaction:** reclaim also takes `_writerMutex`, so it cannot delete a row
  mid-capture. A row the replay sink still needs sits below its cursor and is retained by the
  existing reclaim invariant; capture reads current-generation rows regardless of cursor.
- **K1.3 note (carried forward):** the route worker must pause intake and request birth via
  this provider; it must not write to the store on a side channel during capture. Documented
  beside the providers.

## 6. Boundary parity (plan v3 §13 "boundary parity")

`InMemoryBuffer` does **not** implement `IReplayBoundaryProvider` today, so there is no
SQLite-vs-memory parity target yet. v1 interprets "boundary parity" as **internal
consistency**, tested:

1. The standalone `CaptureReplayBoundaryAsync(sink)` returns the *same* `ReplayBoundary`
   embedded in `CaptureBirthStateAsync(routeId, sink)` for the same state.
2. `FirstPendingSequence == cursor(sink)` and `CutoffExclusive == next_sequence`.
3. `HasBacklog == (cursor < next_sequence)` after an append leaves the replay sink behind.
4. The boundary is stable across close/reopen (same cursor + `next_sequence`).

> **Open item O-C (for review):** should K1.2d *also* add `IReplayBoundaryProvider` to
> `InMemoryBuffer` to make cross-implementation parity real, or defer that to when an
> in-memory replay path is actually needed? Recommendation: **defer** (no consumer yet;
> adding it now is untested surface). Confirm scope.

## 7. Tests (v1 proposed set)

**Boundary (`SqliteRouteStore` + provider):**
- caught-up sink → `HasBacklog == false`, `First == Cutoff == next_sequence`;
- after append leaves sink behind → `HasBacklog == true`, `First == cursor < Cutoff`;
- unknown sink → `RouteStoreSinkCursorNotFound`; disabled store → `RouteStoreReplayTrackingNotEnabled`;
- route-id mismatch → `RouteStoreRouteMismatch`; boundary stable across reopen.

**Birth state:**
- empty manifest → empty snapshot at the current generation, boundary `[cursor, cutoff)`;
- N metrics appended → snapshot has N current-generation values, each decoded value/quality/
  unit/timestamp matches what was appended; `Snapshot.Generation == gen`;
- older-generation rows excluded (append gen 0, advance to gen 1, append gen 1 → birth snapshot
  holds only gen-1 rows);
- coherence: no snapshot value at/beyond cutoff (asserted via `MaxRouteBufferSequence < cutoff`);
- a deliberately corrupted `latest_value.envelope` (raw-mutated) → `RouteStoreEnvelopeUnsupported`
  (fail closed, no partial snapshot);
- a raw-forced row with `route_buffer_sequence >= cutoff` → `BufferCursorInconsistent`.

**Cutover state:** cutoff == `next_sequence`; snapshot as of cutoff; coherence enforced.

**Concurrency (deterministic, no `Thread.Sleep`):** a capture and an append serialized by the
mutex — assert the appended row is either entirely included (its seq < cutoff) or entirely
excluded (seq >= cutoff), never a torn/partial value. Use a `TaskCompletionSource` gate, not
timing.

**Capability handle:** disabled store → both providers `null`, `Buffer` non-null and usable;
enabled store → both non-null and functional; disposing the store disposes once (idempotent),
no second connection/loop (reinforce v3.1 §3).

**Restart/rehydrate:** append, capture birth, reopen, capture birth again → identical snapshot
(values + generation) and boundary.

Determinism: `Category!=Flaky`; full **Core.Tests** + **Management.Tests** projects green
before PR (topic filters miss cross-cutting guards).

## 8. Error codes

Reuse existing K1.2b/c codes — **no new code expected**: `RouteStoreReplayTrackingNotEnabled`,
`RouteStoreRouteMismatch`, `RouteStoreSinkCursorNotFound`, `BufferCursorInconsistent`,
`RouteStoreEnvelopeUnsupported`, `BufferCorrupt`. If review finds a capture-specific failure
that none of these describes, add one in `CoreErrors.cs` (flag it then, don't pre-invent).

## 9. Performance (measured, gates finalized in K1.2e)

- **Capture latency at 100 / 1k / 10k current-generation metrics** reported and compared
  across commits. The mutex is held only for the raw `SELECT` (bytes), not decode; report the
  under-mutex hold time separately from total capture time to prove appends aren't blocked by
  decode.
- No invented hard ceiling in K1.2d; **super-linear growth blocks merge** (carried from v3
  §11). A single indexed `SELECT ... WHERE schema_generation = ?` over `latest_value` should
  be linear in current-generation row count.

> **Open item O-D (for review):** `latest_value` currently has only the PK
> `(source_instance_id, device_id, tag_path)`. A capture filters on `schema_generation`. At
> 10k metrics a full-table scan per capture may be acceptable (captures are rare — birth /
> rebirth / cutover, not per-point), or we add an index on `schema_generation`. Recommendation:
> **measure first (K1.2e), add the index only if the scan shows super-linear/again-and-again
> cost**; captures are infrequent. Confirm we don't pre-add the index.

## 10. Implementation sequence (commits in one PR)

1. **Boundary provider** — implement `IReplayBoundaryProvider` on `SqliteRouteStore`
   (`CaptureReplayBoundaryAsync`) under the mutex; boundary tests. Behavior-additive only.
2. **Session-state provider** — implement `IReplaySessionStateProvider`
   (`CaptureBirthStateAsync` / `CaptureCutoverAsync`) with the read-under-mutex →
   decode-after-release algorithm; birth/cutover/coherence/corruption tests.
3. **Capability handle** — `SqliteRouteStoreHandle` + internal factory reflecting tracking
   state; handle/disposal tests. No new connection/loop.
4. **Concurrency + restart tests**; full Core + Management regression; diff-hygiene
   (no `RouteWorker`/Sparkplug symbols).

## 11. Definition of done

Core + solution 0/0; full Core.Tests + Management.Tests green; all §7 tests present/green;
both providers implemented and exposed via the handle **only when enabled**; capture runs
**exclusively under the ownership lock + writer mutex on the single owning connection set,
with no second writer / side-channel** (directive satisfied and asserted); decode happens off
the lock from captured bytes; envelope cross-checks reused; capture latency at 100/1k/10k
reported; **no** `RouteWorker`/Sparkplug/actor code; single disposal authority preserved
(D10 reclaim loop untouched). Then production PR.

## 12. Open questions consolidated (for the external review pass → v2)

- **O-A** — writer-connection read under the mutex vs. an added reader-tx (recommend: writer
  connection only).
- **O-B** — handle currency after runtime activation (recommend: `GetCapabilityHandle()`
  called post-activation, providers null while disabled).
- **O-C** — add `IReplayBoundaryProvider` to `InMemoryBuffer` for real parity, or defer
  (recommend: defer).
- **O-D** — index `latest_value.schema_generation` now or measure first (recommend: measure
  first in K1.2e).
- **O-E** — does `CaptureCutoverAsync` need a `sinkId` for any per-sink cutover semantics, or
  is the route-wide cutoff (`next_sequence`) sufficient? The K1.1 contract takes only
  `routeId`; v1 follows it. Confirm no per-sink cutover is required before K1.3.
