# Sparkplug B Spike — WS2 Findings: Birth Inputs (Manifest + Latest-Value Snapshot) v1

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** Prototype landed with executable evidence; restart strategy = the WS2
exit decision (below), **RESOLVED in plan v2.3** (persist + same-store atomic; a
never-observed metric is absent-until-first-seen; `BufferMode.StoreAndForward`
required). "v2.2" references below are the then-current target — **superseded by v2.3**.
**Charter:** `2026-07-13-sparkplug-b-spike-tasks-v1.md` WS2. **ADR:** 0036 Rule 5.
**Depends on:** WS1 (the replay cutoff `CutoffExclusive` is the consistency anchor).

> **Headline:** there is **no complete existing snapshot source** in Core, and the
> manifest is **not** cleanly config-derived either. The answer — validated by an
> executable A/B test — is a **protocol-neutral latest-value provider fed from the
> canonical routed stream**, which doubles as the observed manifest. Restart coverage
> is **RESOLVED** (plan v2.3 / ADR-0036 Rule 5): persist in the same per-route SQLite
> store, atomic append+upsert; a never-observed metric is absent-until-first-seen.

## 1. Manifest source — NOT cleanly config-derived (finding)

`SourceInstanceConfig` (`src/…/Core/Configuration/SourceInstanceConfig.cs`) carries
**no per-metric list**: its `Tags` are free-form routing labels, and the real
tag/datatype definitions live inside the **opaque per-protocol `Connection` block**
(parsed by each adapter) or come from `ISourceAdapter.BrowseTagsAsync`. So there is
**no protocol-neutral, Core-level complete manifest** to read — reading the
`Connection` block in Core would violate protocol-agnosticism (locked #1).

**Consequence (locked):** the **persisted latest-value snapshot IS the v1 manifest** —
metric set + datatype (`CanonicalDataPoint.ValueType`) for every metric that has flowed
at least once. **Config/browse are NOT used to supply unread metrics** — a
never-yet-read metric is simply **absent from NBIRTH** until its first observation
(a schema change → controlled rebirth). See §4/§6 and ADR-0036 Rule 5.

## 2. Latest-value snapshot — no existing complete source (confirmed)

Searched Core: the only current-value holders are `UpdateCurrentValuesAsync` on
**pull** sinks (OPC UA Server — sink-internal, per-sink, not a route provider) and
`IRouteTap` (observational, ADR-0018, explicitly not a correctness path). A buffer
scan is **insufficient** — an unchanged metric ages out of the backlog. So per the
decision hierarchy, a **new protocol-neutral provider** is required (pre-approved).

## 3. As-built prototype (all internal, spike, non-production)

| Type | File |
|---|---|
| `CanonicalMetricKey(SourceInstanceId, DeviceId, TagName)` + `LatestMetricValue(…, RouteBufferSequence)` + `LatestValueSnapshot` + `ILatestValueSnapshotProvider` | `src/…/Core/Routing/ILatestValueSnapshotProvider.cs` (new) |
| `InMemoryLatestValueSnapshotProvider` — `Observe(routeId, points, bufferSequences?)` feed + latest-wins (by buffer seq, else timestamp) + `GetSnapshotAsync` | `src/…/Core/Routing/InMemoryLatestValueSnapshotProvider.cs` (new) |

Fed from the canonical routed stream **after transforms** (in production, the route
enqueue point in `RouteWorker`; tests drive `Observe` directly). Not
Sparkplug-specific. No locked contract touched. `SinkPublisher`/`RouteWorker`
unchanged.

**Executable results (`FullyQualifiedName~Routing.Spike`): 13/13 green** (10 WS1 + 3
WS2):
- `Snapshot_Retains_Long_Unchanged_Metric_Absent_From_Backlog` — the **A/B case**:
  Metric A (recently changed, in backlog) and Metric B (unchanged long ago, **aged
  out of the buffer**) BOTH appear in the snapshot with current canonical state; a
  buffer scan of the same buffer yields **only A**. Proves buffer-scan insufficiency.
- `Snapshot_Keeps_Latest_Value_For_A_Metric` — latest-wins by buffer sequence.
- `Empty_Route_Yields_Empty_Snapshot`.

## 4. WS2 exit decisions

- **Snapshot source:** the new `ILatestValueSnapshotProvider`, fed post-transform
  from the canonical routed stream. (Not `IRouteTap`; not buffer scan; not source
  re-read.)
- **Manifest source:** the provider's observed metric set + datatype **is** the
  manifest for observed metrics. Config/browse supply only the residual never-read
  set.
- **Consistency model (ties to WS1/WS7) — CORRECTED per PR-review.** The
  latest-wins prototype + `RouteBufferSequence` alone **cannot** answer "as of `H`":
  a value updated past `H` before `GetSnapshotAsync` overwrites the ≤`H` value. The
  production requirement (ADR-0036 Rule 5, plan v2.3 §3) is a **persisted latest-value
  table in the same per-route store as the buffer, with buffer append + snapshot
  upsert committed atomically**, so `(H, snapshot)` is one transaction. The in-memory
  prototype proves *completeness* (the A/B test), **not** as-of-`H` atomicity — that
  is a K1 design requirement with crash-injection tests. Also: the prototype keys by
  `TagName` as a shortcut; the production key is **canonical `TagPath`**
  (`RouteId+SourceInstanceId+DeviceId+TagPath`, ADR-0036 Rule 5).

## 5. Open decision — restart coverage (the residual gap)

The in-memory provider **does not survive process restart**, so a metric last
changed in a **previous** lifetime (e.g. device idle across a restart) would be
absent on cold start. Options (per the hierarchy):
1. **Persist the snapshot** alongside the durable buffer (same tier as SqliteBuffer)
   → cold start rehydrates current values. *(Recommended primary.)*
2. **Source-seed** at birth (last-choice: bypasses transforms, fails when a source
   is down, adds source load).
3. **Delay CONNECT** until every manifest metric has been observed (only viable if
   the manifest is known and all metrics are live).
**RESOLVED in plan v2.3 / ADR-0036 Rule 5:** persist the snapshot **in the same
per-route SQLite store as the buffer**, append+upsert **atomic**, `(H, snapshot)` in
one transaction (one owner, e.g. `SqliteRouteStore`); Sparkplug routes require
`BufferMode.StoreAndForward`. A **genuinely-never-observed metric is absent from
NBIRTH** until first observation (schema change → rebirth); `is_null` is used **only**
for a known manifest metric whose value is explicitly null — *not* the earlier
loose "is_null or delay". (This superseded the round-1 "recommendation to v2.2".)

## 6. Carried to plan v2.3 (locked policy — supersedes the round-1 wording)
Adopt `ILatestValueSnapshotProvider` (protocol-neutral, **persisted**). **Manifest =
the persisted observed set ONLY** (no config/browse for unread metrics — Core has no
protocol-neutral catalogue). **A never-observed, unknown metric is absent from NBIRTH;
its first observation is a schema change → controlled rebirth.** `is_null` applies
**only** to an already-known manifest metric explicitly carrying null. Birth is taken
against a snapshot captured **atomically with `H`** in the **same per-route SQLite
store** as the buffer (not merely via `RouteBufferSequence`). A **material route-schema
change starts a new snapshot generation** so removed metrics are not re-announced
(ADR-0036 Rule 5). Promote internal→public and wire the `Observe` feed into
`RouteWorker`'s enqueue path at the go/no-go, with the buffer reporting appended
sequences under the same transaction.
