# Bug 2 (P0) — Sink publish path silently dead (handoff)

**Status:** **RESOLVED 2026-05-20** — both halves fixed, tests green, production smoke confirms.
**Date:** 2026-05-20
**Form:** End-of-session handoff. Single dated doc rather than the full v1→review→v2→reality-check→v3 cadence because the kickoff (the dedicated chip prompt in `2026-05-20-followup-chips.md` §"Chip 1") already carried the planning surface, the implementation surfaced a second defect during smoke that warranted a tight implementation loop rather than a re-plan, and the user validated each step interactively.

---

## 0. Why a single-doc handoff (cadence note)

The standing plan-trail discipline (v1 → ChatGPT review → v2 → reality-check → v3, separate files) exists to defend against premature implementation on non-trivial planning. For this work:

- The chip prompt in `2026-05-20-followup-chips.md` §"Chip 1" was already a self-contained v1-equivalent — locked invariants, suspects, reproduction recipe, acceptance criteria.
- Investigation surfaced a defect immediately (worker-task fire-and-forget) plus a SECOND defect (`SqliteBuffer` cursor recovery) only AFTER the first fix unblocked the smoke path. A separate review pass before the second defect could be observed would have planned against an incomplete bug shape.
- The user smoked each fix in production immediately (Run 1: 2264 published points; Run 2: clean restart from drained buffer) — that interactive validation substituted for the review-pass surface.

This pattern (chip prompt = v1, single handoff = v2+v3 compressed) is appropriate for **bug fixes where the chip captured the planning fully**. New milestones still follow the full cadence.

---

## 1. What landed

Two source-code fixes + one hardening + the test surface that pins both invariants.

### Bug 2-A — Worker task fault observability (`src/ElpisEdgeConnect.Core/Routing/RoutingEngine.cs`)

**Defect.** `StartRouteAsync` fired `Task.Run(() => worker.RunAsync(cts.Token), cts.Token)` and immediately transitioned the route to `Running`. The worker task was fire-and-forget — any exception escaping `RouteWorker.RunAsync` (e.g. `RegisterSinkAsync` throwing, channel reader faulting, an unhandled internal bug) left the task `Faulted` while the route still reported `Running`. No diagnostic event fired. Operators had no surface to see "Running but actually dead."

**Fix.** Wrap `RunAsync` in a try/catch continuation:

- `OperationCanceledException` under `cts.IsCancellationRequested` → swallow (normal stop path).
- Any other `Exception` → `ObserveWorkerFault(route, ex)`, which:
  1. Calls `route.Lifecycle.TryTransitionTo(RouteState.Failed, reason)` with the exception type + message as the transition reason. `OnRouteStateChanged` fires through the diagnostics seam so the Studio's route page picks it up.
  2. Tolerates the transition being illegal (e.g. the route happens to already be `Stopping` because `Stop` was called concurrently) — losing the race to a deliberate teardown is fine.
  3. Writes the full stack to `Console.Error` for last-resort visibility (Core has no `ILogger` seam by design).

**Why it doesn't break valid cancellation paths.** The `when (cts.IsCancellationRequested)` guard means OCEs *outside* cooperative cancellation still flow to the Failed transition (preserving fail-loud semantics for unexpected cancellation). Cooperative shutdown still reaches `Stopped`, not `Failed` — pinned by `StartRoute_WorkerCancelled_DoesNotTransitionToFailed`.

### Bug 2-B — `SqliteBuffer` cursor recovery after full drain (`src/ElpisEdgeConnect.Core/Buffer/SqliteBuffer.cs`)

**Defect (surfaced AFTER 2-A in the live smoke).** Clean shutdown after a successful drain leaves the file in this state:

- `points` table: empty (all rows were ack'd and the reclaim loop deleted them).
- `cursors` table: high-water mark preserved (e.g. `next_unread = 2264`).

On the next `OpenAsync`:

- `ReadHeadTail` computes `head = COALESCE(MAX(sequence), -1) + 1 = 0` (empty `points`).
- `LoadCursors` sees `cursor (2264) > head (0)` → throws `BufferException(CoreErrors.BufferCursorInconsistent)`.

This bricks restart after every clean shutdown-following-drain. The user hit this in their Run 2 against the Run 1 data dir.

**Fix.** In `OpenAsync`, between `ReadHeadTail` and `LoadCursors`: when `head == 0` (the genuinely-empty-points case), `PeekMaxCursor` reads `MAX(next_unread)` from the cursors table; if non-zero, snap `head` and `tail` forward to it. The `LoadCursors` validation then sees `cursor == head` (or below) and accepts. Future enqueues continue the monotonic sequence past 2264 rather than wrapping to 0.

**Why the gate on `head == 0` is load-bearing.** The existing `CursorAboveHead_OnReopen_Throws` test pins genuine corruption — `points` table has sequences 0–2 but a cursor row claims 9999. That's a real inconsistency (the sink couldn't have ack'd past data that's still on disk), and the throw remains correct. Gating the new "snap forward" behavior on `head == 0` keeps that throw alive while opening the clean-drain path.

**Sequence continuity.** Downstream consumers see strictly-increasing sequence ids for the lifetime of the buffer file — not a wraparound at every clean shutdown. This matters for any future debugging of "where did sequence N come from?" and is the correct semantic for a per-route durable buffer.

### Hardening — `TaskScheduler.UnobservedTaskException` handler (`src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs`)

Belt-and-braces for the broader class of "fire-and-forget Task with no observation." Bug 2-A catches at the source for the route worker specifically. Any *other* unobserved Task fault in the process (sink supervisor poll loop, hot-reload coordinator continuation, license loader, future code) would still vanish into the GC finalizer pre-fix.

Attached once per process at the top of `ConfigureRuntimeAsync` via `Interlocked.CompareExchange` so test harnesses that build multiple hosts don't re-subscribe. The handler calls `args.SetObserved()` (explicit even though .NET 8's default policy already does this) and writes the exception to stderr with an `[edgeconnect]` tag for log-pipeline filtering.

---

## 2. Test surface added (6 new tests, all green)

### `tests/ElpisEdgeConnect.Core.Tests/Routing/RoutingEngineWorkerFaultTests.cs` (new file)

- `StartRoute_WorkerThrows_RouteTransitionsToFailed` — the load-bearing invariant. A buffer factory whose buffer throws on `RegisterSinkAsync` drives the route worker to fault; with the fix, the route reaches `Failed` within 5s and the diagnostics emits a `→Failed` transition event.
- `StartRoute_WorkerCancelled_DoesNotTransitionToFailed` — counter-test. Cooperative cancellation (`StopRouteAsync`) reaches `Stopped`, not `Failed`. Prevents the fix from being too eager.

### `tests/ElpisEdgeConnect.Host.Tests/RuntimeReloadCoordinatorTests.cs` (3 new tests appended)

- `Reconcile_AppliesSourceSinkRoute_SinkReceivesEmittedPoints` — minimal invariant: applying source + sink + route in a single `SimulateApply` delivers points to the sink within 10s.
- `Reconcile_AppliesAllThree_AndThenManyPoints_AllReachSink` — stronger version: 200 emitted points must ALL reach the sink (buffer sized to avoid overflow eviction). Catches the "first publish works but then it stalls" failure mode.
- `Reconcile_AppliesAllThree_WithStoreAndForwardBuffer_SinkReceivesPoints` — same shape, but the route uses `StoreAndForward` with a real `SqliteBuffer` rooted at a temp directory. Pins the durable-buffer path through the hot-reload coordinator end-to-end.

### `tests/ElpisEdgeConnect.Core.Tests/Buffer/SqliteBufferRecoveryTests.cs` (1 new test appended)

- `FullDrainThenRestart_RecoversHeadFromCursors_AndDoesNotThrow` — 2264 points enqueued, fully drained + ack'd + reclaimed → reopen the file → assert no throw, AND that the next enqueue lands at sequence 2264 (continuing past the high-water mark, not wrapping to 0). The literal 2264 mirrors the user-observed cursor in the production smoke.

### Counter-test preserved unchanged

- `CursorAboveHead_OnReopen_Throws` (existing) — still throws when `points` has rows AND a cursor claims to be past them. The fix does not weaken genuine-corruption detection.

---

## 3. Verification trail

### Unit tests

| Project | Before | After | Delta |
|---|---|---|---|
| `Core.Tests` | 878 | 879 | +1 (`FullDrainThenRestart...`) |
| `Host.Tests` | 133 | 136 | +3 (the three reload-coordinator data-flow tests) |
| `Core.Tests` (Routing folder) | unchanged | +2 | the two `RoutingEngineWorkerFaultTests` |
| **Total new** | | | **6** |

All `Category!=Flaky` suites: Core 879 ✓, Host 136 ✓, Management 467 ✓, ModbusTcp 232 ✓, Integration 26 ✓. 0 warnings, 0 errors.

### Production smoke

| Run | Setup | Observed | Verdict |
|---|---|---|---|
| **Run 1** | Studio against the user's old data root (Option B) — config carried Modbus → MQTT route from previous attempts, fresh buffer (empty) | Source emitted 2264 points, MQTT broker received 2264 publishes, route stayed `Running`, no faults logged | Bug 2-A's headline symptom (silently-dead sink) is gone — the worker is now alive AND the sink path delivers |
| **Run 2** | Stop Run 1 cleanly, restart against the same data root — `cursors` table records `next_unread = 2264`, `points` is empty | Pre-fix: `BufferException: Sink 'MQTT-Local-Modbus' has cursor 2264 which is above head 0`. Post-fix: clean boot, route `Running`, data resumes flowing | Bug 2-B confirmed fixed in the exact reproduction the user observed |

The Run 2 BufferException is itself the strongest evidence Bug 2-A was real and is now fixed — that exception requires that Run 1 successfully published all 2264 points and the reclaim loop deleted every row from the `points` table. Pre-2A-fix, the buffer would have stayed at MaxDepth with no drainage.

---

## 4. Files touched

```
Modified:
  src/ElpisEdgeConnect.Core/Routing/RoutingEngine.cs          +47 -2   (Bug 2-A)
  src/ElpisEdgeConnect.Core/Buffer/SqliteBuffer.cs            +43 -0   (Bug 2-B)
  src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs         +38 -0   (UnobservedTaskException handler)
  docs/sessions/2026-05-20-100-cnc-deployment-readiness.md    Bug 2 §5 marked RESOLVED with PR cross-link
  tests/ElpisEdgeConnect.Host.Tests/RuntimeReloadCoordinatorTests.cs   +3 tests + RealBufferFactory helper
  tests/ElpisEdgeConnect.Core.Tests/Buffer/SqliteBufferRecoveryTests.cs +1 test

Added:
  tests/ElpisEdgeConnect.Core.Tests/Routing/RoutingEngineWorkerFaultTests.cs   2 tests (file)
  docs/sessions/2026-05-20-bug2-sink-publish-path-handoff.md                   this file
```

---

## 5. Decisions locked in this session

- **Worker task fault → Failed state, not Degraded or Stopped.** The lifecycle validator permits `Running → Failed` and `Starting → Failed`. `Failed` is terminal until the route is re-registered (e.g. by the hot-reload coordinator on the next apply). That matches operator semantics: "the route's worker died, this needs attention." Degraded reserved for sink-level publish failures with the route still attempting to recover.
- **Sequence-number continuity across restart is preserved, not reset.** When the `points` table is empty on open but cursors carry a high-water mark, sequences continue past the mark rather than wrapping. Trades a small open-time cost (one extra `MAX(next_unread)` query) for cleaner downstream debugging.
- **`UnobservedTaskException` handler installed once per process via `Interlocked` gate.** Idempotent against test harnesses that build multiple hosts.
- **No new `ILogger` seam introduced in Core.** Core is host-agnostic. Worker faults surface via `Console.Error` (last resort) plus the existing `IRoutingEngineDiagnostics.OnRouteStateChanged` event (structured). Host implementations can route stderr wherever they want.

---

## 6. What this DOESN'T close (deferred)

- **Bug 1 (P3)** — `DefaultRouteBufferFactory` rooted at `options.ConfigDirectory` instead of `ResolvedDataRoot`. Out of scope for this session per the locked sequencing in `2026-05-20-100-cnc-deployment-readiness.md` §5. Lower priority than M.P2.3 + bulk-provision tooling.
- **`EDGECONNECT_CONFIG_DIR` inertness** — paired with Bug 1, separate chip.
- **M.P2.3 Brother HTTP migration** — was held behind Bug 2 per the user directive; now unblocked. Pick up in a separate session.
- **Bulk-provision tooling (Option A)** — was held behind Bug 2; now unblocked.

---

## 7. Pickup notes for the next session

- The 7-day in-house soak is no longer gated on Bug 2 — when M.P2.3 + bulk-provision close, the soak harness should re-run cleanly. The new sequence-continuity behavior means soak runs across multiple host restarts will preserve monotonic sequences (cleaner observation surface).
- Operators will now see a `Failed` route state in the Studio if the worker dies for any reason. Worth a follow-up UX pass on the Studio route page to surface the transition reason string (currently in the `RouteStateChangedEvent.Reason` field) rather than just the state — defer to a separate UX milestone unless surfaced again during smoke.
- The `UnobservedTaskException` handler currently writes to stderr only. If the Management host later routes stderr to a structured log sink, the `[edgeconnect]` prefix is the agreed grep token.

---

**End of Bug 2 handoff. Both halves resolved; production smoke confirms; deployment-readiness doc updated; M.P2.3 unblocked.**
