# Session handoff — Sparkplug B K1.3 (factory→handle seam + RouteWorker replay wiring) kickoff

**Date:** 2026-07-15 · **Author:** prior session (Sudhakar + Claude)
**Read this before writing any code**, then the governing docs in §6. K1.3 is the FIRST track
that leaves the buffer layer and touches route composition — treat it as a design track
(plan-trail first), not a straight code slice.

---

## 0. ⚠️ Branch dependency (read first)

This handoff lands via its own small docs PR on branch **`docs/sparkplug-b-k1.3-kickoff`**.
Once that PR is merged, it is on `master`; until then it is only on the branch. Start K1.3 cold by:

```
git fetch origin
git checkout master
git pull --ff-only
```

`master` already contains **all of K1.2d** (PR #184 merged). Cut the K1.3 work branch fresh from
`master`.

## 1. Where you are right now

- **master tip:** `39cf978` (Merge PR #184 — K1.2d capture providers + capability handle).
- **K1.2d merged as #184** after two external review rounds (r1: 3 blockers fixed — tx-open
  translation, fail-closed storage-class materialization + checked `value_type` narrow, disposed
  identity; r2: approved, one doc-only nuance tightened). No open blockers.
- **Baselines on master:** full **Core.Tests 1191**, **Management.Tests 1149**, solution builds
  **0 warnings / 0 errors** on Core (`TreatWarningsAsError`).
- Untracked review artifacts at repo root (`pr-184.*`, older `pr-*`) are safe to ignore/delete.

## 2. What K1.2d delivered — the seams K1.3 CONSUMES (do not redo)

All on `SqliteRouteStore` (the owner) + `SqliteBuffer` (the façade), merged to `master`:

- **`IReplayBoundaryProvider.CaptureReplayBoundaryAsync(sinkId, ct)`** — cursor + append cutoff in
  one deferred snapshot tx, no manifest scan.
- **`IReplaySessionStateProvider`** — `CaptureBirthStateAsync(routeId, sinkId, ct)` → coherent
  `ReplaySessionStartState` (boundary + birth snapshot); `CaptureCutoverAsync(routeId, ct)` →
  `ReplaySessionCutoverState` (cutoff + snapshot). Capture under lock, decode OFF-lock.
- **`SqliteBuffer.GetCapabilityHandle()`** → `SqliteRouteStoreHandle(Buffer, ReplayBoundaryProvider?,
  ReplaySessionStateProvider?)` — `Buffer` is the façade the route already holds; both provider slots
  are the ONE wrapped owner, non-null iff replay-state tracking is enabled (read once → never
  disagree). Immutable snapshot.
- **`SqliteBuffer.ActivateReplayStateTrackingAsync(routeId, replaySinkId, ct)`** — drained-store,
  one-way disabled→enabled activation delegate.
- Under the hood (K1.2a–c, already merged): the store is the sole DB owner; `AppendAsync` maintains
  the `latest_value` manifest + authoritative `next_sequence` atomically; `AdvanceGenerationAsync`
  is the CAS + drain fence.

These are the Core-neutral seams. **K1.3 wires them into a route; it does NOT change them.**

## 3. What K1.3 must do — scope (design in K1.3, NOT pre-locked)

The v3 K1.2d plan (§R4) deliberately left the factory→handle extension and the route-side
consumption to K1.3, to be designed against the façade surface above. K1.3's job:

1. **Factory seam** — extend `DefaultRouteBufferFactory`
   (`src/ElpisEdgeConnect.Core/Routing/DefaultRouteBufferFactory.cs`) so a replay-capable route can
   obtain the **(façade, `SqliteRouteStoreHandle`) pair** WITHOUT reopening `SqliteRouteStore`
   directly. The factory's `CreateAsync` (`:51`) is the sole construction path and does real
   orchestration — buffer-path resolution, `MigrateLegacyBufferIfPresent` (`:128`,
   legacy `{dataPath}/config/buffer` → `{dataPath}/buffer` backlog migration), and
   quarantine→`IRoutingEngineDiagnostics` wiring. A direct owner-open would bypass all of it. Likely
   shape (per v3 §R4, NOT locked): an internal `CreateReplayCapableAsync` returning
   `(SqliteBuffer façade, SqliteRouteStoreHandle handle)`, activating tracking, then handing the
   handle to the route.
2. **RouteWorker replay-session wiring** — `src/ElpisEdgeConnect.Core/Routing/RouteWorker.cs` must,
   for a route whose sink implements **`IReplayAwareSinkAdapter`**
   (`src/ElpisEdgeConnect.Core/Adapters/IReplayAwareSinkAdapter.cs`), drive the Core-owned lifecycle
   using the handle's providers: `BeginReplaySessionAsync` (birth as-of-H from
   `CaptureBirthStateAsync`) → phase-tagged `PublishAsync` replay/catch-up → `CompleteCatchUpAsync`
   (from `CaptureCutoverAsync`) → Live; plus `RebirthAsync` via the reverse `IReplaySessionHost`
   handshake, and `EndSessionAsync` on stop. Core owns H/C boundaries, batch splitting at the
   boundary, and cursor advancement; the sink only reacts (ADR-0036 R6/R7).
3. **Buffer-assigned sequence reporting to the route** — the birth/cutover snapshots tie values to
   buffer positions; confirm the route path can learn the assigned sequence range additively (the
   tracked `AppendAsync` already returns `AssignedSequenceRange`; wire it through, do NOT amend the
   locked `IMessageBuffer.EnqueueAsync`).

**Keep OUT of K1.3:** the Sparkplug assembly/actor itself (`Sinks.SparkplugB` = K2/K3); protocol
payload/topic/mappers; licensing/config validation (K4); Studio wizard (K5). K1.3 is the Core-side
route plumbing that makes the replay lifecycle drivable by ANY `IReplayAwareSinkAdapter`.

## 4. Exact first action — a plan-trail, not code

Per the planning cadence, K1.3 opens with a **plan-trail** (v1 → external review → v2 →
reality-check → v3-frozen), each in its own dated `docs/sessions/` file, BEFORE any code. The v1
plan should:
- Read the real `DefaultRouteBufferFactory.CreateAsync` + `RouteWorker` sink-loop + the K1.1
  lifecycle contracts (`ReplaySessionContracts.cs`, `IReplayAwareSinkAdapter`, `PublishContext`,
  `ReplaySessionId`/`ReplayEpochId`) and design the factory extension + the RouteWorker replay branch
  against them.
- Decide how a route is classified replay-capable (sink implements `IReplayAwareSinkAdapter` +
  policy/route config), and where activation happens (route apply vs first start).
- Reality-check the epoch-gating + pause-DATA-during-birth + coalesce-rebirth rules (ADR-0036 R7)
  against the actual RouteWorker concurrency model.

Do NOT skip to code — the factory/RouteWorker seam is cross-cutting and Sony's onboarding-package
work + Bhanu's TCP sink both touch routing; check for in-flight conflicts before rebasing (see §8).

## 5. Related open tracks (do NOT lose)

- **K1.2e (route-store finalization) — deferred from K1.2d, still open:** the FULL corruption matrix
  and final perf gates on the capture path, PLUS the honest under-lock-scan measurement the K1.2d
  perf review flagged — fixed current rows (e.g. 10) × {0, 10k, 100k} stale rows, with
  `EXPLAIN QUERY PLAN`, to decide whether a `schema_generation` index / stale-row cleanup policy is
  justified. K1.2d's two-dataset evidence only isolated OFF-lock decode cost (≈equal total rows), NOT
  the under-lock full scan vs stale-row count. Sequencing (K1.2e before or parallel to K1.3) is a
  user call — flag it in the K1.3 v1 plan.
- **O-C (in-memory `IReplayBoundaryProvider`)** — deferred in K1.2d; needed if any route uses the
  in-memory buffer with a replay-aware sink. Confirm whether K1.3's scope includes it.
- **SqliteBuffer parity / in-memory parity tests** (sink plan v2.2 §4/K1.4) — named prerequisite for
  the kernel; confirm status.

## 6. Governing docs (read in this order)

- `docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md` — the frozen K1.2d plan; §R4 defines
  the K1.2d↔K1.3 seam and the façade surface K1.3 designs against.
- `docs/decisions/0036-sparkplug-replay-then-rebirth.md` — the replay-then-rebirth lifecycle
  (birth → replay → cutover → live), epoch gating, rebirth handshake (Rules 4/6/7).
- `docs/decisions/0035-*.md` — canonical→Sparkplug value/quality/null mapping (Rule 5).
- `docs/sessions/2026-07-13-sparkplug-b-sink-plan-v2.2.md` — the K0–K6 kernel sequencing and the
  four confirmed decisions (WS2 persist, additive buffer-seq, sink-assembly placement, defaults
  ship).
- `CLAUDE.md` §3 (architectural locks — Core stays protocol-agnostic; AI/data-path rules) + the
  delivery-mode lock #12 (Sparkplug B = LocalTransport boundary only).

## 7. Cadence you must follow (unchanged)

- **Plan-trail first** (v1→review→v2→reality-check→v3), each a dated `docs/sessions/` file. No code
  until v3 is frozen.
- Small single-concern commits; expect 1–4 external review rounds per PR. Address every finding, add
  the requested tests, re-verify, push. Regenerate `pr-<n>.diff` + `pr-<n>-review-bundle.md` after
  each corrective push.
- **Before opening the PR:** run the FULL `ElpisEdgeConnect.Core.Tests` AND
  `ElpisEdgeConnect.Management.Tests` projects (topic filters miss cross-cutting guards), build the
  whole solution 0/0, diff-hygiene (`git diff --stat master...HEAD` + grep for out-of-scope symbols).
  Run the full Core.Tests project STANDALONE (the reclaim-SLO latency test is timing-sensitive).
- **Own commit→push→PR** after a clean slice; **merges are the user's call.**
- **Verify the current branch (`git branch --show-current`) before every commit.** Stage explicit
  paths, never `git add -A`.

## 8. Gotchas (carried forward)

- **Parallel devs touch routing:** Sony works the onboarding package + EtherNet/IP on
  `Sony_Development`; Bhanu implements the TCP sink — both intersect route composition. Check for
  in-flight cross-cutting work before recommending merges/rebases; assume `DefaultRouteBufferFactory`
  / `RouteWorker` may have concurrent edits.
- `SqliteRouteStore` is `internal`; the capture surface is reached via `SqliteBuffer` (façade) +
  `SqliteRouteStoreHandle`. Tests reach internals via `InternalsVisibleTo`
  (`ElpisEdgeConnect.Core.Tests`).
- xUnit parallelism + mutable `static` = flaky; use instance/constructor state. Studio URL
  `127.0.0.1:5080`; Mosquitto on `localhost:1883` for MQTT integration tests. LF→CRLF warnings on
  `git add` are benign (Windows).
- Core is protocol-agnostic (locked): Sparkplug-specific orchestration lives in `Sinks.SparkplugB`
  (K2/K3), NOT in `ElpisEdgeConnect.Core`. K1.3 adds only the neutral route plumbing.
