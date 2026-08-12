# Sparkplug B — K1.3 route-wiring plan v3.1 (amendment to the frozen v3)

**Status:** ✅ **Amendment to the frozen v3 execution baseline** (2026-07-15). Folds the second
external-review pass (post-v3): four narrow locks + the slice-1 entry gate. **v3 stands; this file
adds/tightens only the items below.** Read alongside
`2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.md`.

**Both scope deferrals reconfirmed by the reviewer:** generation-changing (material-schema) rebirth
stays deferred; replay-sink hot-replacement becomes full route stop→start. This amendment closes the
remaining gaps the reviewer raised before slice 1.

---

## A1. Material route-schema replacement must NOT reuse the same replay store (NEW lock — closes a v3 gap)

v3 R4 restricted the *sink* hot-swap, but a route stop→start **at the same generation on the same
buffer DB** does NOT remove old manifest rows (the buffer file is `{dataPath}/buffer/{routeId}.db` —
identity == `routeId`; `DefaultRouteBufferFactory.cs:65`). So a materially-changed schema reusing the
same DB would re-birth stale/removed metrics from the persisted manifest — violating the locked
Sparkplug policy (removed/materially-changed schema requires **generation** treatment, which K1.3
defers).

**A "material route-schema change" includes:** a transformed metric removed; canonical metric
identity changed; a metric's canonical datatype changed; filters/transforms changed such that the
persisted observed set is no longer authoritative.

**Locked outcome for K1.3:** a material route-schema change **must not** restart against the old
replay-enabled database at the same generation. It must instead **(a) use a new route/buffer identity
(new `routeId`)**, or **(b) be rejected**, or **(c) go through a future explicit destructive /
new-generation migration**. K1.3 **never** restarts against the old DB and calls it a schema
transition.

**K1.3's contribution vs. the deferred part:** *detecting* a material change (a schema diff of the
new config against the persisted manifest) is a **configuration-layer / future-milestone** concern —
K1.3 does not build a schema-diff engine. K1.3's contribution is the **prohibition + a fail-closed
guard**: on activation of a replay route against an already-enabled DB, if the persisted replay
ownership (replay sink id) does not match the incoming config, fail closed (persisted-mismatch, per
v3 R5 slice 1 / test 4) rather than silently adopting the old manifest. A config pipeline that cannot
prove the schema is unchanged must route the change to a new `routeId` or reject it — it must not
reach the same-DB restart path.

## A2. First-observed-metric handling — rebirth BEFORE successful publish/ack (NEW ordering lock)

Deferring generation changes does **not** remove additive schema emergence. Locked Sparkplug policy: a
metric first seen after birth is a schema change → **controlled rebirth**, never published as an
unannounced alias. A **same-generation** rebirth correctly includes it (tracked append already added
it to the current-generation manifest — so the fresh birth snapshot contains it). The driver must
enforce this exact ordering so birth-before-DATA is never violated:

```
new metric reaches the replay-aware sink (in a DATA subrange)
sink detects the metric is absent from its current birth catalogue
sink queues RebirthRequest (via IReplaySessionHost) and returns NO full success for that subrange
driver strict-ack rule (Success && Accepted==Count && Rejected==0) → Core acks NOTHING
driver PROCESSES the pending rebirth BEFORE retrying the failed DATA subrange   ← the lock
CaptureBirthState (same generation) now includes the new metric → RebirthAsync → promote new epoch
the original DATA subrange is re-dequeued and retried under the new epoch
```

**Locked driver rule:** when a publish returns not-full-success AND a rebirth request is pending for
the current session/epoch, the driver processes the rebirth **before** re-attempting the same
subrange. The triggering subrange is thus neither acked nor successfully published before the
rebirth. This reuses v3's strict-ack + re-dequeue + coalescing-host machinery — no generation change,
no new capability. (Add a test: a first-observed metric forces rebirth before the triggering subrange
is acked or published-through.)

## A3. Remove `AdvanceGenerationAsync` from the K1.3 capability (TIGHTEN — supersedes v3 §1 B1 / R3)

Since K1.3 runs at a **fixed** generation (v3 R3), exposing `AdvanceGenerationAsync` on
`IReplayRouteBuffer` is an attractive accidental path back to the empty-birth defect. **Remove it.**
The K1.3 capability is minimal:
```csharp
internal interface IReplayRouteBuffer
{
    ValueTask<ReplayRouteActivation> ActivateReplayAsync(
        string routeId, string replaySinkId, CancellationToken cancellationToken);
    ValueTask<AssignedSequenceRange> AppendTrackedAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        RouteSchemaGeneration expectedGeneration, CancellationToken cancellationToken);
    IReplayBoundaryProvider BoundaryProvider { get; }
    IReplaySessionStateProvider SessionStateProvider { get; }
}
```
`ReplayRouteActivation` carries the fixed persisted generation. A future material-schema capability
adds generation advancement **together with** the required manifest-seeding semantics — never a bare
counter increment. (The owner's internal `AdvanceGenerationAsync` on `SqliteRouteStore` remains for
that future milestone; it is simply NOT surfaced on the K1.3 route capability.)

## A4. Configuration replacement reaches `EndSessionAsync(ConfigurationReplaced)` before the Host restarts the sink (MAKE CONCRETE — v3 R4)

v3 R4 restricted hot-replace to a route stop→start; this makes the ordering explicit. For a
config-driven replace of a replay route, the Host reload coordinator must, **in order**:
1. Signal the route driver to end the live session with **`ReplaySessionEndReason.ConfigurationReplaced`**
   and **await** `EndSessionAsync(ConfigurationReplaced)` completing;
2. only THEN stop/restart the sink (`SinkSupervisor.RestartAsync` / route re-register).

The end reason is **threaded from the coordinator**, never inferred from a bare cancellation token
(v3 R4). The favorable Host ordering (v3 R2) makes this achievable **without moving sink ownership
into Core** — the coordinator's small guard (v3 R8) plus this ordered teardown is the only Host-side
change. A full stop/start replaces the **live session only**; it does not (and must not claim to)
perform a schema migration (see A1).

## A5. Slice-1 entry gate — CONFIRMED

The reviewer's six gate items, resolved against v3 + this amendment:

| # | Gate item | Where locked |
|---|-----------|--------------|
| 1 | Material route-schema replacement cannot reuse the same replay store without a future migration | **A1** |
| 2 | First-observed metric forces rebirth before successful publish/ack | **A2** |
| 3 | `AdvanceGenerationAsync` absent from the K1.3 route capability | **A3** (removed) |
| 4 | Config replacement reaches `EndSessionAsync(ConfigurationReplaced)` before Host sink restart | **A4** |
| 5 | Activation is the final fallible registration op before publishing the route | v3 R-ledger 7 / R5 slice 1 |
| 6 | No automatic downgrade from an enabled replay DB to legacy `EnqueueAsync` | v3 §1 B2 / R4 |

**Disposition:** slice 1 (`IReplayRouteBuffer` capability + activation-at-commit + `ReplayRouteContext`)
is ready to implement once this amendment + v3 pass the final external line-by-line review. Attach BOTH
`...-plan-v3.md` and this `...-plan-v3.1-amendment.md` to that pass for the go/no-go.

## A6. Test-plan additions (on top of v3 §6's 20)

21. A material-schema change cannot reuse the same replay store (persisted-mismatch guard fails closed;
    a new `routeId` is required).
22. A first-observed metric forces a rebirth before its triggering subrange is acked or published-through.
23. The K1.3 `IReplayRouteBuffer` surface does not expose generation advancement.
24. A config-replace of a replay route drives `EndSessionAsync(ConfigurationReplaced)` (awaited) before
    the Host restarts the sink.
