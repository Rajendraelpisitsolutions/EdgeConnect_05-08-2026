# Session handoff — Sparkplug B K1.2d (capture providers + capability handle) kickoff

**Date:** 2026-07-15 · **Author:** prior session (Sudhakar + Claude)
**Read this + the frozen v3 plan before writing any code:**
`docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md` (execution baseline; v1/v2 are
the design history — read v2 for unchanged mechanics v3 doesn't restate).

---

## 0. ⚠️ Branch dependency (read first)

This handoff and the K1.2d plan trail (v1/v2/v3) live on the branch
**`feat/sparkplug-b-k1.2d-capture`**, **NOT on `master`**. Start cold by:

```
git fetch origin
git checkout feat/sparkplug-b-k1.2d-capture
git pull --ff-only
```

`master` has K1.2c (PR #183) but none of the K1.2d docs. Do your work on this branch.

## 1. Where you are right now

- **Branch:** `feat/sparkplug-b-k1.2d-capture` — cut fresh from `master` after PR #183 merged.
  Pushed, tracking set.
- **Branch tip:** `b45052d` (docs: K1.2d plan trail v1→v2→v3 frozen) — the branch's only commit
  beyond master so far. **No implementation code yet — you start R12 step 1 here.**
- **master tip:** `79326e0` (Merge PR #183 — K1.2c LatestValueEnvelopeV1 codec + atomic tracked
  append).
- **Baseline test counts on this branch (== master):** full **Core.Tests 1128**,
  **Management.Tests 1149**, solution builds **0 warnings / 0 errors** on Core.
- Local untracked review artifacts at repo root (`pr-183.*`, older `pr-*`, `k0-evidence*`) are
  safe to ignore/delete; not part of K1.2d.

## 2. What is already DONE and merged (do not redo)

K1.2 route-store chain, all merged to `master`:
- **K1.1** (#180): replay contracts — `ReplayBoundary`/`IReplayBoundaryProvider`,
  `IReplaySessionStateProvider` + `ReplaySessionStartState`/`ReplaySessionCutoverState`,
  `LatestValueSnapshot`/`LatestMetricValue`/`CanonicalMetricKey`, `RouteSchemaGeneration`.
- **K1.2a** (#181): `SqliteRouteStore` is the sole DB owner; `SqliteBuffer` a thin façade;
  `<db>.lock` lifetime ownership lock.
- **K1.2b** (#182): schema v2 migration, `next_sequence` authority, drained-store activation
  (`ActivateReplayStateTrackingAsync`, one-way disabled→enabled), generation CAS
  (`AdvanceGenerationAsync`) + drain fence.
- **K1.2c** (#183): `LatestValueEnvelopeV1` codec (fixed MessagePack layout + explicit
  discriminators, fail-closed); atomic tracked `AppendAsync` (one tx: points + latest_value
  upsert-keep-greater + next_sequence); UTC-required manifest timestamp (fail closed on non-UTC).

**K1.2d must NOT touch** the append/codec/generation logic — it only READS the manifest into
coherent capture states and exposes the two optional capabilities via a handle.

## 3. What K1.2d must do — scope is LOCKED in v3 (frozen)

Implement on `SqliteRouteStore` + `SqliteBuffer` (no route/Sparkplug wiring):
1. `IReplayBoundaryProvider.CaptureReplayBoundaryAsync` — boundary-only (cursor + cutoff), **no
   manifest load**.
2. `IReplaySessionStateProvider` — `CaptureBirthStateAsync(routeId, sinkId)` /
   `CaptureCutoverAsync(routeId)`, using **read-under-mutex (one deferred read tx on `_writer`) →
   decode-off-lock from deep-copied rows**.
3. `SqliteRouteStoreHandle` — **façade-anchored**: `Buffer` = the `SqliteBuffer` façade, provider
   slots = the wrapped `SqliteRouteStore` owner; providers non-null **iff** tracking enabled
   (single captured `enabled` local for both slots). `SqliteBuffer.GetCapabilityHandle()` +
   internal `ActivateReplayStateTrackingAsync` delegate. **Do NOT open `SqliteRouteStore`
   directly** from route composition — `DefaultRouteBufferFactory` must stay the sole constructor
   (it does legacy-buffer migration + quarantine-diagnostics wiring an owner-direct-open would
   bypass). The factory extension for K1.3 to reach the handle is the **K1.2d↔K1.3 seam** — NOT
   built in K1.2d.

**Keep OUT:** `RouteWorker`/route wiring; Sparkplug assembly/actor; lifecycle-record
construction; full corruption matrix + final perf gates (K1.2e). O-C (in-memory boundary
provider) deferred; O-D (schema_generation index) measure-first only.

## 4. Exact first action — R12 step 1 (raw-capture primitives)

Per v3 §R12 step 1 (a self-contained first commit/slice):
1. **Required-transaction capture helper overloads** (set `command.Transaction = tx`; required,
   NOT optional nullable): `ReadNextSequence(conn, tx)`, `ReadCurrentGeneration(conn, tx)`,
   `ReadCursorValue(conn, tx, sinkId)`, `ReadCurrentGenerationManifest(conn, tx, gen)`. Keep the
   existing connection-only overloads for non-capture callers.
2. **`RawManifestRow`** record — deep-copied fields incl. a copied `byte[] Envelope` (never an
   alias to a live DB buffer).
3. **Constructor-injected immutable `SqliteRouteStoreTestHooks`** (`CaptureEnteredCriticalSection`
   sync hook + `Action<CaptureQueryKind>? QueryExecuting`) threaded through
   `SqliteRouteStore.OpenAsync(..., SqliteRouteStoreTestHooks? testHooks = null)` into the private
   ctor; production passes null; **hook exceptions escape unchanged** (not translated to
   Buffer errors).
4. The **shared raw-capture** under `_writerMutex`: `cutoff` (ReadNextSequence) read FIRST to pin
   the deferred snapshot; then generation/cursor/manifest; commit; **no explicit rollback** in the
   capture path (rely on `using var tx` disposal). Tests for coherent raw capture + the §7 error
   families.

Then R12 steps 2–5 (boundary provider → session-state provider → capability handle →
concurrency/perf) in subsequent commits on this branch, one PR.

## 5. Key reality-check facts (proven — don't re-litigate)

- **Deferred-tx snapshot is proven** on Microsoft.Data.Sqlite **8.0.10**: within one
  `BeginTransaction(deferred: true)` on `_writer`, the first read pins the snapshot and later
  reads don't see another connection's commit (a throwaway `sqlite-snapshot-probe` passed). Make
  `cutoff` the first read.
- **Read helpers today set NO `command.Transaction`** (`SqliteRouteStore.cs` `ReadMetaValue` etc.)
  — hence the required-tx overloads. `WriteMeta` shows the codebase's `cmd.Transaction = tx`
  pattern.
- **Handle ownership is the highest-risk area.** `DefaultRouteBufferFactory.CreateAsync` does
  path resolution + `MigrateLegacyBufferIfPresent` + quarantine→`IRoutingEngineDiagnostics`
  wiring. Route the handle through the façade; keep the factory the sole constructor.
- **Canonical-key collision is impossible today** (`CanonicalMetricKey.Create` does no
  normalization; Ordinal equality) — keep `TryAdd`→`BufferCorrupt` as a defensive guard but
  **defer** the collision fixture (don't fake it by mocking).
- **`AdvanceGenerationAsync` never touches `latest_value`** — removed metrics leave permanent
  stale-gen rows, so benchmark TWO datasets (total≈current-gen; many-stale + small-current). No
  `schema_generation` index in K1.2d (measure first).
- **Cancellation** off-lock only: internal `BuildSnapshotFromRawRows(..., Action<int>?
  rowDecodedForTest = null)` checks every 256 rows; deterministic tests via already-cancelled
  token (row 0) and cancel-after-row-255 → throws at row 256.
- **Error families (pinned):** envelope → `RouteStoreEnvelopeUnsupported`; structural row
  inconsistency (bad/mismatched gen, undefined value_type, null identity, dup canonical id, null
  envelope) → `BufferCorrupt`; cutoff/cursor/session coherence → `BufferCursorInconsistent`;
  capability/route/sink → existing codes; cancellation → `OperationCanceledException`. **No new
  error code expected.** Do not leak `ArgumentException`/`InvalidCastException`.

## 6. Cadence you must follow (unchanged from K1.2a–c)

- Small single-concern commits; expect 1–4 external review rounds per PR (v1→review→v2→
  reality-check→v3 for plans; findings-addressed for code). Address every finding, add the
  requested tests, re-verify, push.
- **Before opening the PR:** run the **FULL** `ElpisEdgeConnect.Core.Tests` **and**
  `ElpisEdgeConnect.Management.Tests` projects (topic filters miss cross-cutting guards), build
  the whole solution 0/0, diff-hygiene (`git diff --stat master...HEAD` + grep for out-of-scope
  symbols — no `RouteWorker`/Sparkplug). Run the full test project **standalone** (not chained
  after a build) — the reclaim-SLO latency test is timing-sensitive.
- Review handoff artifacts at repo root: `pr-<n>.diff` (`git diff master...HEAD -- '*.cs'`) + a
  compact `pr-<n>-review-bundle.md`; regenerate after each corrective push.
- **Own commit→push→PR** after a clean slice; **merges are the user's call.**
- **Verify the current branch (`git branch --show-current`) before every commit** — the
  system-prompt branch header goes stale in long sessions. **Stage explicit paths, never
  `git add -A`.**

## 7. Gotchas (carried from K1.2a–c)

- xUnit parallelism + mutable `static` = flaky; use instance/constructor state (hence the
  constructor-injected test hooks). Studio URL `127.0.0.1:5080`; Mosquitto on `localhost:1883`
  for MQTT integration tests. LF→CRLF warnings on `git add` are benign (Windows).
- `SqliteRouteStore` is `internal`; tests reach it via `InternalsVisibleTo`
  (`ElpisEdgeConnect.Core.Tests`). Capture/activation/generation tests call
  `SqliteRouteStore.OpenAsync` directly; façade tests use `SqliteBuffer.OpenAsync`.
