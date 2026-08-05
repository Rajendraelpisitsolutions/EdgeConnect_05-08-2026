# Session handoff — Sparkplug B K1.2c (codec + atomic tracked append)

**Date:** 2026-07-14 · **Author:** prior session (Sudhakar + Claude)
**Read this + the newest ADRs + `docs/sessions/2026-07-14-sparkplug-b-k1.2-route-store-plan-v3.md`
(and its `-v3.1-amendment`) before writing any code.**

---

## 1. Where you are right now

- **Branch:** `feat/sparkplug-b-k1.2c-codec-append` (checked out, pushed, tracking set).
  Cut clean from `master` **after PR #182 merged**. **No commits on it yet** — you start K1.2c here.
- **master tip:** `f0f3bb0` (Merge PR #182).
- **Baseline test counts on master:** full **Core.Tests 1083**, **Management.Tests 1149**, solution builds **0 warnings / 0 errors**.
- Untracked local artifacts at repo root (not committed, safe to ignore/delete): `pr-177.*`, `pr-180.*`, `pr-181.*`, `pr-182.*` (review diffs/bundles), `k0-evidence*`.

## 2. What is already DONE and merged (do not redo)

The Sparkplug B northbound work is being built in small, individually-reviewed PRs off a
plan-trail. Merged so far:

- **K1.1 contracts** (PR #180): `ReplayPhase`, `PublishContext` (two-watermark H/C),
  `ReplaySessionId`/`ReplayEpochId`/`RouteSchemaGeneration`, replay-session lifecycle
  (`IReplayAwareSinkAdapter`, `ReplaySessionStart/Rebirth/Cutover/End`, `RebirthRequest`),
  `IReplaySessionStateProvider` + `ReplaySessionStartState/CutoverState`, latest-value
  snapshot (`CanonicalMetricKey`, `LatestMetricValue`, `LatestValueSnapshot`),
  `BrokerEndpoint`, delivery boundary (`DeliveryAcknowledgementBoundary`,
  `DeliveryCapabilities`, `DeliveryBoundaryRules`). All non-bypassable (private ctor +
  validated `Create`), fail-closed. `ByteArray` supported via `ImmutableArray<byte>`.
- **K1.2a** (PR #181): `SqliteBuffer` (public sealed) is now a **thin façade** over the
  internal **`SqliteRouteStore`** — the sole owner of the per-route DB connections,
  reclaim loop, disposal, and an exclusive `<db>.lock` ownership lock
  (`RouteStoreAlreadyOwned`). Behavior-neutral.
- **K1.2b** (PR #182): on `SqliteRouteStore`:
  - schema **v2 migration**, constraint-aware (validates type / NOT NULL / PK
    membership+order before any DDL; fresh = no app tables; v1→v2 creates only the v2
    addition; v2 validates all core tables; malformed/future/damaged fail closed, no repair);
  - **monotonic head recovery** seeded from `tail_sequence`/`next_sequence`;
  - **drained-store replay activation** `ActivateReplayStateTrackingAsync(routeId,
    replaySinkId, ct)` — one-way `disabled→enabled`, registers the replay sink at head,
    persists meta atomically, `RouteStoreReplayActivationBacklogPending` if not drained;
  - **strict replay-state loader** on open (enabled store must have consistent
    route_id/replay_sink_id+cursor/generation/next_sequence==head; fails closed);
  - **generation CAS + drain fence** `AdvanceGenerationAsync(expectedCurrent, next, ct)`
    — fences on the **persisted** replay sink; `next==current+1` checked/overflow-safe;
    stale/backlog/corruption typed errors; `DeregisterSinkAsync` protects the replay cursor;
  - legacy `EnqueueAsync` **rejected once enabled** (`RouteStoreLegacyAppendOnEnabledStore`).

Key files: `src/ElpisEdgeConnect.Core/Buffer/SqliteRouteStore.cs` (the owner — big),
`SqliteBuffer.cs` (façade), `SqliteBufferSchema.cs` (DDL + meta keys + `LatestValueTableDdl`
+ `V2AdditionStatements`), `ReplayTrackingActivationResult.cs`, `Errors/CoreErrors.cs`
(all `ROUTE_STORE_*` codes). Tests: `tests/…/Buffer/SqliteRouteStore{Ownership,Recovery,
Migration,Activation,Generation,ReplayStateLoad}Tests.cs`.

## 3. What K1.2c must do (this branch) — scope is LOCKED

1. **`LatestValueEnvelopeV1`** — a typed, versioned envelope codec (use the repo's existing
   **MessagePack** dependency, as `MessagePackFormat` does; **fixed field layout + explicit
   type discriminators, NOT typeless/object serialization** — plan v3 O3). Must round-trip
   every K1.2 arm: Boolean, Integer, Long, Float, Double, String, DateTime, **ByteArray
   (as raw bytes ↔ `ImmutableArray<byte>`)**, known-null-with-real-datatype, quality +
   quality reason, unit, immutable static properties. **Fail closed** on unknown codec
   version / unknown discriminator / malformed field / unsupported type. `Array`/`Object`/
   `CanonicalValueType.Null` stay rejected (consistent with K1.1 `LatestMetricValue`).
   Cross-check on decode: envelope datatype == the separate `value_type` column, etc.
2. **Atomic tracked `AppendAsync`** on `SqliteRouteStore` (enabled stores only): in **ONE
   transaction** — `points` insert **+** `latest_value` upsert (keep the row with the
   greater `route_buffer_sequence`) **+** `next_sequence` advance. This is the enabled-store
   append path (legacy `EnqueueAsync` stays rejected when enabled). The `latest_value` table
   already exists (created empty in K1.2b); this fills it.
3. **Re-enable the three deferred tests** (now reachable because append can move
   `next_sequence` ahead of a not-yet-consumed cursor):
   - `AdvanceGenerationAsync` **cursor behind head → `RouteStoreGenerationBacklogPending`**;
   - **cursor beyond head → corruption** (`BufferCursorInconsistent`);
   - **post-activation append advances `next_sequence`**.

**Keep OUT of K1.2c** (later milestones): `CaptureBirthStateAsync`/`CaptureCutoverAsync`
(that's K1.2d), `RouteWorker`/route-path wiring and the Sparkplug assembly/actor (K1.3/K2/K3).

**Suggested slicing (two commits):** (1) codec + its round-trip/fail-closed tests, no store
changes; (2) atomic `AppendAsync` + upsert + `next_sequence` maintenance, then re-enable the
three deferred tests.

## 4. How this project runs its PRs (the cadence you must follow)

- **Plan-trail first** for anything non-trivial: v1 → external review → v2 → reality-check →
  v3 (+ amendments), each a dated file in `docs/sessions/`. The accepted K1.2 plan is
  `2026-07-14-sparkplug-b-k1.2-route-store-plan-v3.md` + `-v3.1-amendment.md`.
- **Small, single-concern PRs.** Each has gone through 1–4 external review rounds; expect
  the same. Address every finding, add the requested tests, re-verify, push.
- **Before opening a PR:** run the **FULL** `ElpisEdgeConnect.Core.Tests` **and**
  `ElpisEdgeConnect.Management.Tests` projects (topic filters miss cross-cutting guards),
  build the whole solution 0/0, and do a **diff-hygiene check** (`git diff --stat
  master...<branch>` + grep for out-of-scope symbols).
- **Review handoff artifacts** at repo root: `pr-<n>.diff` (`git diff master...<branch>`) and
  a compact `pr-<n>-review-bundle.md`. Regenerate after each corrective push.
- **Own the commit→push→PR loop** after a clean milestone; **merges are the user's call**.
- **After merge:** update master → delete the feature branch (local+remote) → cut the next
  branch fresh from master. Never stack unreviewed work.

## 5. Gotchas / lessons from this session (save yourself the pain)

- **xUnit parallelism + mutable `static` = flaky.** A `static` test seam leaked across
  parallel buffer-test classes and passed only by luck. Use instance state or
  deterministic file tricks (e.g. a directory at the lock path) — never a mutable static seam.
- **The reclaim-SLO latency test is timing-sensitive.** It failed once under CPU contention
  from a `dotnet build` running in the same command. Run the full test project **standalone**
  (not chained after a build) to judge green/red; it's been deterministic standalone.
- **`git add -A` once swept in `tests/…/ModbusSimulator/.venv` binaries.** Now gitignored
  (`.venv/`, `**/.venv/`). **Stage explicit file paths**, not `-A`.
- LF→CRLF warnings on `git add` are benign (Windows).
- **Reachability:** an enabled store keeps `cursor == next_sequence` until an append moves
  `next_sequence` — that's why the three deferred tests couldn't run in K1.2b and belong here.
- `next_sequence` is **enabled-only** (disabled enqueue must never touch it — zero-cost path).
- `SqliteRouteStore` is `internal`; tests reach it via `InternalsVisibleTo`
  (`ElpisEdgeConnect.Core.Tests`). Activation/generation tests call `SqliteRouteStore.OpenAsync`
  directly; buffer/façade tests use `SqliteBuffer.OpenAsync`.
- Studio URL is `127.0.0.1:5080`. Mosquitto for MQTT integration tests on `localhost:1883`.

## 6. Exact first action for the new session

1. Confirm you're on `feat/sparkplug-b-k1.2c-codec-append` and `git pull` is clean.
2. Read `SqliteBufferSchema.LatestValueTableDdl` (the `latest_value` columns you'll write)
   and `Routing/LatestValueSnapshot.cs` (`LatestMetricValue` — the shape you'll encode).
3. Look at `Buffer/MessagePackFormat.cs` for the existing MessagePack usage/pattern.
4. Build slice 1: `LatestValueEnvelopeV1` (encode/decode + tests), commit, then slice 2
   (atomic `AppendAsync`) + re-enable the three deferred tests.

There was one open confirm pending when this session ended: **use MessagePack (fixed layout +
discriminators) for the envelope** — that was the recommendation; proceed unless the user
says otherwise.
