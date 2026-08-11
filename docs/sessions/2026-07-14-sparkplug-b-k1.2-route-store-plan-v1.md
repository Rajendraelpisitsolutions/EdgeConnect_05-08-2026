# Sparkplug B — K1.2 `SqliteRouteStore` execution plan (v1)

**Date:** 2026-07-14 · **Branch:** `feat/sparkplug-b-k1.2-route-store` (cut from `master` after PR #180 merge `0f0bfd7`)
**Predecessors:** K1.1 contracts merged (PR #180). This milestone implements the persisted
side of those contracts. **Plan-trail:** this is **v1** — goes out for the external review
pass; v2 folds the review back.

> **Naming reconciliation vs plan v2.3 §3.** v2.3 referred to a standalone
> `ILatestValueSnapshotProvider`. K1.1 finalized the contract differently: the atomic
> birth/cutover capture is the composite **`IReplaySessionStateProvider`**
> (`CaptureBirthStateAsync`, `CaptureCutoverAsync`) returning coherent
> `ReplaySessionStartState` / `ReplaySessionCutoverState`. K1.2 implements **that**
> interface. The standalone snapshot provider is gone.

## 1. Scope (locked by the PR #180 approval)

**In K1.2 — this PR only:**
1. `SqliteRouteStore` — one per-route SQLite store holding **both** the buffer rows and
   the latest-value (observed-manifest) table.
2. Atomic **buffer append + latest-value upsert** in a single transaction.
3. Snapshot-**generation** persistence (route-schema generation per the K1.1
   `RouteSchemaGeneration`), incl. generation-transition handling.
4. Coherent **`CaptureBirthStateAsync`** — `(ReplayBoundary H, LatestValueSnapshot@H)` in
   one read transaction.
5. Coherent **`CaptureCutoverAsync`** — `(C, LatestValueSnapshot@C)` in one read
   transaction.
6. **SQLite replay-boundary parity** — `IReplayBoundaryProvider` over the SQLite store
   returns the same boundary semantics the in-memory buffer does (K1.1 parity target).
7. Tests: restart/rehydrate, transaction rollback, **corruption**, generation-transition,
   plus the crash-window matrix from v2.3 §3.
8. **Performance evidence**: the store is opt-in; a route without a replay-aware sink pays
   **zero** extra cost (no latest-value upsert, no snapshot table touched).

**Out of K1.2 (do NOT touch here):**
- `RouteWorker` lifecycle wiring → K1.3.
- H/C batch splitting + acknowledgment orchestration → K1.3.
- Rebirth event handling → K1.3.
- Sparkplug assembly, payloads, actor → K2/K3.

## 2. Storage model

Per-route SQLite file (reuse the existing per-route store location/`Microsoft.Data.Sqlite`
conventions from the Phase-1 `SqliteBuffer`). Two logical concerns, one DB, one
transaction domain:

- **`buffer`** — existing append-only buffer rows + per-sink cursors (unchanged schema;
  K1.2 must not regress `SqliteBuffer`).
- **`latest_value`** — the observed manifest / latest-value table. Proposed columns:
  `route_id, source_instance_id, device_id, tag_path` (composite persistence key, v2.3 §5),
  `value_type INTEGER, is_null INTEGER, value BLOB/scalar, quality INTEGER, quality_reason,
  unit, static_props BLOB, route_buffer_sequence INTEGER, schema_generation INTEGER,
  updated_at`.
- **`route_meta`** — single-row: `current_schema_generation`, gateway/route identity, store
  format version (for migration + corruption detection).

**Persistence key** = `route_id + source_instance_id + device_id + tag_path` (per-route
partition, v2.3 §5). The Edge-Node-scoped **alias** key is a K3 concern, not stored here.

## 3. The two atomic captures (the correctness core)

**Append path (hot path, every accepted point for a replay-aware route):**
`BEGIN IMMEDIATE` → insert buffer row(s) → upsert `latest_value` (only if
`new.route_buffer_sequence >= existing.route_buffer_sequence`) → `COMMIT`. Buffer append
and latest upsert are **one transaction** (v2.3 §3). A crash between the two leaves neither
(rollback), so a replayed value and its manifest never diverge.

**`CaptureBirthStateAsync(routeId, sinkId)`** — one read transaction:
1. read the sink cursor `FirstPendingSequence` and the append cutoff → `ReplayBoundary H`
   via `ReplayBoundary.Create`;
2. read every `latest_value` row **at the current schema generation** whose
   `route_buffer_sequence < H.CutoffExclusive` → build `LatestValueSnapshot(gen, …)`;
3. return `ReplaySessionStartState.Create(H, snapshot)` (its coherence check —
   every value `< H` — must pass by construction).

Because a latest value can be updated *past* H before we read, step 2 must filter on
`route_buffer_sequence < H.CutoffExclusive` and read the **value that held at ≤H**. Two
candidate mechanisms to decide in v2:
- **(a) keep a small "as-of" history** — retain the pre-overwrite value when an upsert
  crosses a live capture; or
- **(b) capture-under-lock** — take H and the snapshot in the same `BEGIN IMMEDIATE` read
  so no append interleaves. Given single-writer per route, **(b) is the leading option**
  (simpler, no history table). **Open question O1.**

**`CaptureCutoverAsync(routeId)`** — same shape at cutoff `C` (the current append cutoff at
capture): `ReplaySessionCutoverState.Create(C, snapshot@C)`.

## 4. Boundary parity

`SqliteReplayBoundaryProvider` implements `IReplayBoundaryProvider` over the store and MUST
return boundaries indistinguishable from the in-memory provider for the same append/cursor
history (empty buffer → `FirstPending == Cutoff`; backlog → `First < Cutoff`). A shared
parity test suite runs both providers through identical sequences.

## 5. Generation transitions

- `route_meta.current_schema_generation` is the source of truth.
- A schema change (metric added/removed, unit/static-property change — detection itself is
  K1.3/K3, but the **persistence** is here) bumps the generation and stamps new rows.
- `CaptureBirthStateAsync` reads **only current-generation** rows, so a removed metric from
  an older generation is not re-birthed (v2.3 §6, ADR-0036 Rule 5).
- Test: write gen N rows, transition to N+1 (drop one metric, change one unit), assert the
  birth snapshot reflects N+1 only.

## 6. Test matrix

- **Crash window** (v2.3 §3): kill after append-before-upsert; after upsert-before-append;
  metric updated after H before the read; restart + rehydrate; explicit rollback.
- **Corruption**: truncated/garbage DB, bad `route_meta` format version → **fail closed**
  with a typed error, never silently serve a partial snapshot.
- **Restart/rehydrate**: reopen store, `CaptureBirthStateAsync` reproduces the pre-restart
  manifest.
- **Generation transition**: as §5.
- **Boundary parity**: §4 shared suite.
- **Zero-cost path**: a route with no replay-aware sink performs **no** `latest_value`
  writes (assert via a counting/spy store or query count).
- Determinism: no `Thread.Sleep`; time abstracted; `Category!=Flaky`.

## 7. Performance evidence

- Micro-benchmark (BenchmarkDotNet, Release): append throughput **with** vs **without** the
  latest-value upsert; assert the replay-aware overhead is bounded and the non-replay path
  is unchanged from the current `SqliteBuffer` baseline.
- Capture latency for `CaptureBirthStateAsync` at representative manifest sizes
  (e.g. 100 / 1k / 10k metrics).

## 8. Deliverables (file list, indicative)

- `src/…Core/Buffer/SqliteRouteStore.cs` (or `Routing/…` — see O2)
- `src/…Core/Buffer/SqliteReplayBoundaryProvider.cs`
- store schema/migration + corruption-detection helper
- `tests/…Core.Tests/Buffer/SqliteRouteStoreTests.cs`, `…/SqliteReplayBoundaryParityTests.cs`,
  generation-transition + crash-window + corruption tests
- `tests/…Benchmarks/…` append-overhead + capture-latency benchmarks
- docs: store format note under `docs/core/` or `docs/config-schemas/`

## 9. Definition of done

Core + solution 0/0; full Core.Tests + Management.Tests green; the crash/corruption/
generation/parity/zero-cost tests present and green; perf evidence captured; **no**
`RouteWorker`/Sparkplug/actor code introduced (scope guard). Then production PR from this
branch.

## 10. Open questions for the review pass

- **O1** — as-of-H mechanism: capture-under-`BEGIN IMMEDIATE` (leading) vs a pre-overwrite
  history table. Confirm single-writer-per-route holds so (b) is safe.
- **O2** — file placement/namespace: `Buffer` (next to `SqliteBuffer`) vs `Routing`. The
  contract split (`IReplayBoundaryProvider` in `Buffer`, `IReplaySessionStateProvider` in
  `Routing`) argues for one class implementing both across namespaces — pick the home.
- **O3** — `static_props` + `ByteArray`/`Value` on-disk encoding (JSON vs a typed BLOB) and
  how it round-trips the K1.1 immutable representation (`ImmutableArray<byte>`).
- **O4** — corruption policy: quarantine-and-restart-empty vs hard-fail the route; interplay
  with the Phase-1 fail-soft-startup ADR.
- **O5** — reuse vs extend `SqliteBuffer`: new sibling store sharing the DB, or extend the
  existing store. Must not regress the D10 reclaim-loop fix.
```
