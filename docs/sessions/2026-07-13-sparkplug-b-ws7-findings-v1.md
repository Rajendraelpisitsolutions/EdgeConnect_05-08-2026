# Sparkplug B Spike — WS7 Findings: Replay→Live Cutover (v1)

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** Prototype + executable evidence. Closes the same-owner critical path
(WS1→WS2→WS7). Exact algorithm locked; kernel integration = v2.2.
**Charter:** `2026-07-13-sparkplug-b-sink-plan-v2.1.md` §3. **ADR:** 0036 Rule 2.
**Depends on:** WS1 (replay boundary), WS2 (latest-value snapshot).

> **Headline:** the two-watermark cutover works and **terminates finitely under
> continuous live arrivals**, and the host's current value **never steps backward** —
> historical data does not move it; the final non-historical update lands the newest
> value. Rebirth mid-replay re-announces and still completes.

## 1. As-built (internal, spike, non-production)

`ReplayCutoverCoordinator` (`src/…/Core/Routing/ReplayCutoverCoordinator.cs`)
assembles WS1 + WS2 and emits to a protocol-neutral `ICutoverEmitter`
(Birth / HistoricalData(phase) / FinalUpdate / Live) — **no Sparkplug payload
encoder here**, and **not wired into RouteWorker**. Algorithm:

```
1. Capture H (WS1 boundary).           birth watermark
2. Birth from snapshot as-of-H (WS2).  non-historical current values
3. Drain seq < H          -> Replay    (is_historical)
4. Capture C (WS1 boundary).           catch-up cutoff (fixed instant)
5. Drain H <= seq < C     -> CatchUp   (is_historical)
6. FinalUpdate: non-historical latest-value for metrics changed since birth
7. Live: seq >= C flows as steady-state live
```

Two watermarks are the key: `C` is captured once, so the catch-up drain is finite
**even while live points keep arriving** (they land at seq >= C and are not part of
the historical drain). Rebirth: `RequestRebirth()` restarts from Birth, retaining
acked progress; `BirthCount` increments.

## 2. Executable results — 15/15 spike tests green (10 WS1 + 3 WS2 + 2 WS7)

`ReplayCutoverCoordinatorTests`:
- `Cutover_Emits_Birth_Replay_CatchUp_FinalUpdate_Live_And_Lands_Newest_Value` —
  order `Birth → Data:Replay(0..4) → Data:CatchUp(5..9) → FinalUpdate → Live`;
  partition correct against H and C; **host current value ends at the newest (101),
  not a replayed 100**; and a point arriving after C flows as **live from seq 10**
  (proves finite termination with live data remaining).
- `Rebirth_MidReplay_ReAnnounces_And_Still_Completes` — `BirthCount == 2`, two Births
  emitted, cutover still ends at Live.

The emitter models host semantics: **only Birth and FinalUpdate (non-historical)
move the host's current value; HistoricalData does not** — this is what prevents the
backward step.

## 3. Exit decision (carry to v2.2)

- **Algorithm locked:** birth → replay(<H) → catch-up(H..C) → **final non-historical
  latest-value update** → live. Two finite watermarks.
- **Final-update policy chosen:** emit a single non-historical latest-value update
  per changed metric (host-safe — the host's current value never steps through the
  backlog). The alternative (send held delayed points as live) is **not** chosen.
- **Kernel integration (v2.2):** wire the emitter to the real Sparkplug payload
  factory (NBIRTH/NDATA with `is_historical` + dual timestamps), attach `seq`/`bdSeq`
  (retained across a same-session rebirth), and feed the WS2 provider from
  `RouteWorker`'s enqueue path (with buffer-assigned sequences for the as-of-H tie).

## 4. Spike status — same-owner critical path COMPLETE

| Track | Status |
|---|---|
| WS1 replay context | ✅ prototype + evidence, committed |
| WS2 birth inputs | ✅ prototype + A/B evidence, committed (restart = v2.2 decision) |
| WS7 cutover | ✅ prototype + evidence (this doc) |
| WS4 / WS5 / WS3+WS8 | chipped, parallel — awaiting owners |

Remaining before the **kernel go/no-go**: the three chipped tracks land, the WS2
restart-coverage decision, and SqliteBuffer replay-boundary parity — then plan v2.2
promotes the seams internal→public and integrates into the production route loop.
