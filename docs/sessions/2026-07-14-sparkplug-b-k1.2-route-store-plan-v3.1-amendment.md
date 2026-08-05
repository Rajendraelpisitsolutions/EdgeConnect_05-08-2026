# Sparkplug B — K1.2 plan v3.1 amendment (final-confirmation fixes)

**Date:** 2026-07-14 · **Branch:** `feat/sparkplug-b-k1.2-route-store`
**Amends:** plan v3 (`2026-07-14-sparkplug-b-k1.2-route-store-plan-v3.md`). Everything in v3
stands; this adds six targeted locks the final-confirmation pass required before K1.2a. After
this amendment the reviewer cleared K1.2a to begin.

## Why (the remaining gap)

v3's activation initialized an **empty** `latest_value` manifest but did not fence out
**pre-activation backlog already in `points`**. On an existing route DB (e.g. sequences 20–100
buffered, tracking previously disabled), a new Sparkplug sink registered at the old tail would
NBIRTH an empty manifest and then replay sequences 20–100 — DATA for metrics never announced in
the birth. That violates birth-before-DATA. The store **cannot** reconstruct a complete manifest
from buffered rows (older unchanged metrics may already be reclaimed), so activation must
**fence out all pre-activation backlog**, not backfill.

## 1. Replay-tracking activation is a drained-store transition (blocking)

Replace v3 §3's "one-time activation transaction" with an explicit, drained-only activation:

```csharp
ValueTask<ReplayTrackingActivationResult> ActivateReplayStateTrackingAsync(
    string routeId,
    string replaySinkId,
    CancellationToken cancellationToken);
```

One writer transaction (`BEGIN IMMEDIATE`):
```
recover current head (v3 §2 formula: max of MAX(seq)+1, cursors, tail_sequence, next_sequence)
→ verify points has NO retained rows
→ verify every existing cursor == recovered head   (reject if any cursor is behind)
→ register replaySinkId at the recovered head
→ persist next_sequence = recovered head
→ persist route_id (validate against supplied routeId)
→ persist current_schema_generation = 0
→ persist replay_state_tracking = enabled
→ COMMIT
```
If the route is not globally drained → typed **`RouteStoreReplayActivationBacklogPending`**.
**Never** advance a cursor or discard rows to force activation — the operator/runtime drains the
route first, or creates a fresh dedicated route/store for Sparkplug. The replay sink must **not**
be registered at the old tail after activation.

**Tests:** existing retained point → rejected; points empty but a cursor behind head → rejected;
fully drained → succeeds, replay sink cursor starts exactly at activation head, initial birth
snapshot may be empty; append after activation → `latest_value` + `points` updated atomically.

## 2. Tracking is one-way `disabled → enabled` (blocking)

Once enabled for a route-store DB, replay-state tracking **stays enabled** for that DB. There is
no in-place `enabled → disabled`. (A disabled interval would miss `latest_value` upserts and
re-create the manifest gap on any later re-enable.) Removing the replay-aware sink may stop
*using* the providers, but tracked appends continue. Returning to zero-cost mode requires an
explicit new/reset route store via the normal operator data-preservation workflow.
**Tests:** `enabled → disabled` rejected; reopening an enabled store preserves enabled mode.

## 3. Single disposal authority (lifecycle lock)

`SqliteRouteStore` is the **sole** disposable owner. `SqliteBuffer` and the replay providers are
façades/capability views over it — none opens or owns separate connections or background loops.
Disposal is idempotent; disposing the route buffer closes the owner **only after** route
execution has stopped using all capability views. (Reinforces v3 §4b/§4c and protects the D10
reclaim loop.)

## 4. Expanded generation/cursor validation in `AdvanceGenerationAsync`

Inside the transaction, explicitly validate (unknown/out-of-range = **corruption**, not merely
backlog-pending):
- replay tracking is enabled;
- the named sink cursor **exists** (else typed corruption error, not `GenerationBacklogPending`);
- `0 <= cursor <= next_sequence`;
- `cursor == next_sequence` (the drain fence, v3 §4a);
- `next == current + 1`, overflow checked (v3 §8).

## 5. Envelope cross-checks when decoding `LatestValueEnvelopeV1`

Fail closed unless: envelope datatype **equals** the separate `value_type` column; envelope
metric identity (if encoded) agrees with the key columns; `route_buffer_sequence` and
`schema_generation` are non-negative; envelope version is exactly supported. Prevents the
duplicated storage fields from drifting silently.

## 6. New error code + test additions

Add to the v3 §12 list / `CoreErrors.cs`:
- **`RouteStoreReplayActivationBacklogPending`** (activation on a non-drained store);
- corruption codes for unknown/out-of-range sink-cursor state in `AdvanceGenerationAsync` and for
  envelope↔column mismatch (may reuse `RouteStoreCorrupt` / `RouteStoreEnvelopeUnsupported`).
- Tests: the four activation cases (§1), the two one-way-tracking cases (§2), disposal-idempotency
  + no-second-connection/loop (§3), `AdvanceGenerationAsync` unknown/out-of-range cursor →
  corruption (§4), envelope↔column mismatch → fail closed (§5).

## Net effect on the implementation sequence

Fold into v3 §13 **K1.2b** (activation + one-way flag + fencing) and **K1.2c** (envelope
cross-checks), with the disposal authority established in **K1.2a**. No change to the K1.2/K1.3
scope boundary. After this amendment, **K1.2a is cleared to begin**: sole-owner/façade
behavior-neutral refactor with the lifetime lock (v3 §4b) and single disposal authority (§3),
existing `IMessageBuffer` tests unchanged.
```
