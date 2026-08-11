# M.P2.4 — Brother HTTP source adapter migration (v3.1 amendment)

**Status:** v3.1 — LOCKED 2026-05-21 after second ChatGPT review pass. Implementation-ready.
**Date:** 2026-05-21
**Predecessor plans:** [v1](2026-05-20-mp24-brother-http-plan.md) → [v2 (locked)](2026-05-20-mp24-brother-http-plan-v2.md) → [v3 (reality-check)](2026-05-21-mp24-brother-http-plan-v3.md) → this v3.1 (focused amendment).
**Scope:** six determinism + observability locks identified during the v3 review pass. No v3 sections re-litigated; locks below ADD to v3, do not replace it.

---

## A. Why v3.1 exists

v3 review verdict: "Ready for implementation after a small v3 tightening pass." The reviewer flagged that v3 was strong on architecture/governance/parity but thin on:

- deterministic-runtime guarantees (poll-cycle atomicity, timestamp authority, scheduling),
- operational hardening (cancellation, observability surface),
- and one tag-validation edge (DataPoints dedup).

Each of those is load-bearing for the 100-CNC soak and for future milestones (OPC UA Server sink, Live Tag Watch, bulk-provision). Locking now is cheaper than retrofitting after implementation lands.

---

## B. Six locks (carry verbatim into adapter code + DoD)

### B.1 — Poll-cycle atomicity

**LOCKED:** A Brother poll cycle produces ONE immutable batch of `CanonicalDataPoint`s, assembled fully in-memory before any downstream emission. No collector publishes independently. `PollAsync` returns the entire batch as a single `IReadOnlyList<CanonicalDataPoint>`.

**Why:** preserves deterministic cycle boundaries needed for OPC UA Server snapshots, historian alignment, deterministic replay, and Live Tag Watch coherence. Without this lock, subtle race/order bugs surface later when sinks/consumers assume per-cycle consistency.

**Implementation note:** the adapter's per-poll `BrotherPollSession` (already in v3 §6.2) accumulates raw signals from all six collectors → adapter applies precedence-chain post-processing → adapter constructs canonical points via `CanonicalDataPointFactory.CreatePoint(...)` → adapter returns the assembled list. Collectors are pure functions: `(endpointResponse, pollSession) → pollSession'`. They do NOT call the factory or emit anywhere.

**Contract reinforcement:** the `ISourceAdapter.PollAsync` signature already returns one batch — this lock just makes the in-adapter discipline explicit so no future PR adds an "incremental emission" optimization that quietly breaks atomicity.

---

### B.2 — Single timestamp authority per poll cycle

**LOCKED:** ONE UTC timestamp, captured at poll-cycle START (immediately before any endpoint request is issued), is used as the `DeviceTimestamp` AND `GatewayTimestamp` for every `CanonicalDataPoint` emitted by that cycle. Collectors do NOT timestamp independently.

**Why:** historian alignment, OEE calculations across tags from the same machine, OPC UA snapshot coherence, deterministic replay. Per-collector timestamps would let `CycleTime/Cycle` from one HTTP response carry a different timestamp than `Production/PartsCount` from another response in the same logical poll — breaking the "this is what the machine looked like at instant T" contract that consumers rely on.

**Implementation note:** the adapter reads `DateTime.UtcNow` once into a local `pollStartedAtUtc` before fanning out endpoint requests. The local is passed to `CanonicalDataPointFactory.CreatePoint(...)` for every point. Endpoint-response latency is recorded separately (metric, §B.5) but does NOT affect the timestamp on emitted points.

**Open question deferred to implementation:** poll START vs poll COMPLETION as the timestamp. Locking START because (a) it matches "when the consumer requested the snapshot" semantically, (b) it avoids timestamp jitter from variable endpoint latency, and (c) matches FOCAS2's pattern (verify during step 3 against `Focas2SourceAdapter.PollAsync`; flip to COMPLETION if FOCAS2 does it that way for consistency).

---

### B.3 — No overlapping polls per adapter instance

**LOCKED:** Per Brother adapter instance, maximum ONE in-flight poll cycle at any time. If a scheduled poll tick fires while the previous cycle is still running, the new tick is SKIPPED (not queued), a metric is incremented (`elpis_edgeconnect_brother_poll_overruns_total`, §B.5), and a warning is logged with the source instance id and the duration of the still-running cycle.

**Why:** at 100 CNCs × 6 endpoints × 3 s polling, transient endpoint slowness WILL happen. Without a single-flight guard, an over-running cycle (e.g., 4.5 s) lets the next scheduled tick start before the previous completes → overlapping HTTP requests against the same Brother CNC → socket pressure + stale data interleaving + potential CNC-side load issues. The skip-don't-queue choice avoids unbounded queue growth during sustained slowness; the next regularly-scheduled tick will catch up on its own.

**Implementation note:** `BrotherHttpSourceAdapter` holds a `private int _pollInFlight;` (or `SemaphoreSlim(1,1)`) gating the body of `PollAsync`. If the second call observes the flag set, it logs + increments + returns an empty list immediately. The supervisor treats empty-list returns as benign (zero points observed for that tick) — no state degradation, no fault.

**Cross-check with §B.5:** the `poll_overruns_total` metric is the operational signal. Spikes during soak indicate a CNC + network combination where 3s polling isn't sustainable; the operational response is widening the polling interval, not blocking on the platform.

**Empty-list semantics distinction (v3.1 review add-on):** an empty list returned from `PollAsync` due to overrun is OPERATIONALLY DISTINCT from an empty list returned from a successful poll that emitted zero points. They are NEVER conflated in metrics or logs:

| Case | `poll_duration_ms` recorded? | `poll_overruns_total` incremented? | Log level | Adapter state |
|---|---|---|---|---|
| Overrun (previous still in flight) | NO — no cycle ran | YES (+1) | Warning | unchanged |
| Cycle ran, emitted 0 points (filter excluded all) | YES, `outcome="success"` | NO | Debug or silent | unchanged |

Both cases return the same empty `IReadOnlyList<CanonicalDataPoint>` to the supervisor; the metric tagging is what distinguishes them at observation time. This lets dashboards/alerts treat overruns as an operational concern without entangling them with "no relevant data emitted this cycle" benign behaviour.

---

### B.4 — Cancellation discipline / no fire-and-forget

**LOCKED:** Every Task started by the Brother adapter or its collectors is **observed** — awaited or stored against a known handle that is awaited at `DisposeAsync`. Cancellation tokens are honored at every async boundary. `DisposeAsync` cancels in-flight work and awaits it to completion (within the `ISourceAdapter.StopAsync` 10-second bound) before disposing the HttpClient factory references.

**No `Task.Run(...)` without:**
- the returned Task being stored to a field, AND
- a continuation (or `await`) that surfaces faults via the same `_lastError` / `AdapterHealth` channel that synchronous failures use, AND
- a cancellation path that triggers when the adapter is being disposed.

**Why:** Bug 2's root cause was a fire-and-forget `Task.Run` in `RoutingEngine.StartRouteAsync` whose faulted state never surfaced. The composition-root `TaskScheduler.UnobservedTaskException` handler is belt-and-braces; per-adapter discipline is the primary defense. Brother is the first source adapter to ship after that lesson — getting this discipline right at v3.1 prevents the same defect class.

**DoD addition** (also added to §11 of v3 in the implementation-time edit pass):

- [ ] **NEW: code audit confirms no fire-and-forget Tasks.** Every `Task.Run` (if any) in `src/ElpisEdgeConnect.Sources.BrotherHttp/` either has its Task awaited inline OR stored to a field with a continuation that surfaces faults AND is awaited at `DisposeAsync`. Audited by grep for `Task.Run\|Task.Factory.StartNew\|new Thread\|_ = ` plus manual review of all `async` method invocations not preceded by `await`.

---

### B.5 — Metrics surface (Brother-specific)

**LOCKED:** the Brother adapter exposes the following metrics via `System.Diagnostics.Metrics` (per CLAUDE.md §6 metric platform), tagged by source instance id at minimum:

| Metric | Type | Unit | Tags | Purpose |
|---|---|---|---|---|
| `elpis_edgeconnect_brother_poll_duration_ms` | histogram | ms | `source`, `outcome` (`success`/`degraded`/`failed`) | per-cycle latency |
| `elpis_edgeconnect_brother_endpoint_failures_total` | counter | events | `source`, `endpoint` (one of `mcninfo`/`cycletime`/`wkcntr`/`atc_tools`/`alarms`/`maintnotice`) | per-endpoint visibility — 6 endpoints, each can fail independently |
| `elpis_edgeconnect_brother_poll_overruns_total` | counter | events | `source` | ties to §B.3 single-flight rule |

**Outcome label semantics for `poll_duration_ms` (locked at v3.1 review):**

| Outcome | When | Cycle ran? | Points emitted? | Adapter state impact |
|---|---|---|---|---|
| `success` | All six endpoints returned non-error | Yes | ≥0 (could be 0 if DataPoints filter excludes everything) | None |
| `degraded` | `HTTPD_MCNINFO` (health authority per Q4) succeeded BUT one or more of the other five endpoints failed | Yes | Partial — only data from successful endpoints | None — adapter stays `Connected` |
| `failed` | `HTTPD_MCNINFO` itself failed (returned null/exception) | Yes (cycle ran; the health endpoint just refused) | Zero | Consecutive-failure counter increments; reaches 3 → `Faulted` per Q4 |

**Overruns do NOT record `poll_duration_ms`** — see §B.3 cross-ref. The overrun metric stands alone; a spike there is a scheduling signal, not a cycle-health signal.

**Generic source-adapter metrics — REUSE, don't duplicate:**

- Points emitted: reuse the existing `elpis_edgeconnect_source_points_observed_total{source,route}` (seen during Bug 2 diagnosis at `/metrics`).
- Connected state: reuse the existing `route_state` or its source-adapter equivalent (verify exact name during step 3; cross-reference what FOCAS2 exposes).
- Per-cycle failure counts at the supervisor layer: reuse whatever exists; do NOT add a `brother_poll_failures_total` since the supervisor already has source-grain failure visibility.

**Why this scope:** the three Brother-specific metrics fill gaps the generic supervisor-layer metrics can't (per-endpoint granularity, overrun visibility, latency distribution). Reusing the rest avoids cardinality bloat at /metrics and avoids two-source-of-truth drift between supervisor-level and adapter-level counters.

**Implementation note:** declare a `Meter` in `BrotherHttpSourceAdapter` with name `ElpisEdgeConnect.Sources.BrotherHttp`. All three instruments are registered at construction; tags applied per-recording. Prometheus exporter (already wired per CLAUDE.md §6) picks them up automatically.

**DoD addition:**

- [ ] **NEW: metrics surface verified.** All three Brother-specific metrics emit during smoke test; tag cardinality stays bounded (one `source` value per instance × six `endpoint` values for `endpoint_failures_total`). Validation: `curl http://localhost:9100/metrics | grep brother_` after running the demo-mode adapter for one poll cycle.

---

### B.6 — DataPoints filter normalization

**LOCKED:** `BrotherHttpSourceConfiguration.DataPoints` is **normalized once at config-validation time** before any polling begins. Normalization is deterministic and idempotent:

1. Trim each entry.
2. Drop empty entries.
3. Strip a single trailing `/` (so `Tools/` and `Tools` are treated as the same prefix).
4. Lowercase comparison only — original case preserved for display, but membership checks use `OrdinalIgnoreCase`.
5. Deduplicate by normalized form.
6. If both a prefix (`Tools/`) and a leaf under that prefix (`Tools/ActiveNumber`) appear in the same list, the **prefix wins** (the leaf is redundant — already covered by the prefix). Drop the leaf, keep the prefix. Log a `Information`-level "deduplicated DataPoints entry" note (not a validation issue — operator-friendly normalization).
7. If a normalized entry doesn't match any catalog leaf or prefix, the validator emits `BROTHER.UNKNOWN_DATA_POINT` and rejects the config.

**Why:** without explicit normalization, an operator who lists both `Tools/` and `Tools/ActiveNumber` could get duplicate emission depending on collector-loop semantics, OR get an opaque "this config validates differently each run" bug. Locking the normalization rule once at validation means the post-validation `DataPoints` is canonical, deterministic, and safe to consume by every collector.

**Implementation note:** add `BrotherHttpSourceConfiguration.NormalizedDataPoints` (computed once during `FromSourceInstance` after the raw `DataPoints` is loaded, OR computed lazily but cached on first access). Collectors consume `NormalizedDataPoints` exclusively. The raw `DataPoints` field stays as-is for round-trip fidelity (the wizard reads back the operator's original entries, not the normalized form).

**Test addition** (folded into step 4 of v3 §10):

- `BrotherHttpSourceConfigurationTests.Normalize_PrefixAndLeafBoth_KeepsPrefix`
- `BrotherHttpSourceConfigurationTests.Normalize_TrailingSlashVariants_Deduplicate`
- `BrotherHttpSourceConfigurationTests.Normalize_UnknownEntry_FailsValidation`

---

## C. Light touches for v3.1 (not full locks)

These were flagged in the v3 review but don't need full lock-level treatment — small notes folded into implementation guidance.

### C.1 — Canonical catalog versioning rule (light lock)

**Add to v3 §4.4 as the second sentence:**

> Existing canonical paths in `BrotherTagMap` are append-only and semantically stable within the M.P2.x line. Renaming a path or repurposing its meaning is a breaking change requiring an explicit version-bump milestone (e.g., M.P3.x) and a downstream-consumer migration plan.

**Why:** EREMOS V2 dashboards / OEE consumers / historian schemas will start depending on the exact tag paths post-soak. Silently repurposing `Status/Warning` later would break those without warning.

### C.2 — Demo mode state evolution (small enhancement to v3 §10 step 3)

The synthetic demo responses cycle through 5–6 scenarios on a deterministic rotation, but ALSO advance state within each scenario tick:

- `Production/PartsCount` increments by 1 every N cycles (within "running" scenario).
- `CycleTime/Cycle` jitters within a ±10% band around a per-scenario baseline.
- Maintenance `Notice/{idx}/DuePercent` drifts upward by ~0.1% per cycle (so a wizard reviewer who watches for 10 minutes sees the counter move).
- Alarm transitions happen on cycle boundaries (a 30-cycle "running" stretch → 5-cycle "alarm" stretch → 25-cycle "running" stretch → etc.).

**Why:** static demo data goes visually stale within 2 minutes of UAT, which undermines sales/operator confidence. Movement at the data layer makes the demo realistic without simulating a full machine. ~50 LOC addition to `BrotherHttpDemoApi`. No effect on parity test (parity test uses captured sample bytes, not demo API).

### C.3 — OPC UA future-proofing (already addressed by §5)

`BrotherTagMapEntry` already exposes `Unit` and `Description` (matching `Focas2TagMap`). Those carry the engineering-unit metadata that future OPC UA Server sink work will need at node-generation time. No v3.1 lock required — the existing structure is already OPC UA-compatible.

Quality-derivation hints were flagged as a possible addition, but adding them now would violate the §5 structural-purity lock. When OPC UA Server work begins (post-100-CNC), the right move is to add metadata via composition (a separate `BrotherOpcUaMetadata.cs` keyed by `TagPath`) rather than expanding `BrotherTagMapEntry`. v3.1 just notes this design path; no code in M.P2.4.

---

## D. v3.1 → v3 doc edits required at implementation step 1

When step 1 of v3 §10 runs (cross-reference doc renames), also apply these v3.1-driven edits to v3 itself:

- v3 §4.4 (Catalog evolution rule) — append the append-only sentence from §C.1.
- v3 §10 step 3 — append "demo includes state evolution per v3.1 §C.2."
- v3 §10 step 7 — append "PollAsync enforces single-flight via §B.3; emits one atomic batch per §B.1; uses single timestamp per §B.2."
- v3 §10 step 7 — append "no fire-and-forget tasks per §B.4."
- v3 §10 step 6 — append "BrotherHttpSourceConfiguration applies DataPoints normalization per v3.1 §B.6 with three new tests." (corrected from earlier draft which said step 4 — that step is BrotherTagMap)
- v3 §10 step 13 — append "metric verification per v3.1 §B.5."
- v3 §11 — fold in the four new DoD items from v3.1 (no-fire-and-forget audit, metrics surface verification, DataPoints normalization tests, and confirming single-flight + atomic-batch + single-timestamp behaviour pinned by tests).

---

## E. Definition-of-done additions (carry into v3 §11)

Four new DoD items, all surface from v3.1's locks:

- [ ] **NEW: poll-cycle atomicity + single timestamp pinned by tests.** A test seeds a slow endpoint (e.g., `MNTP_CYCLETIME` takes 800 ms) and asserts that all points emitted by that cycle share an identical `DeviceTimestamp` and `GatewayTimestamp` captured before the slow endpoint started.
- [ ] **NEW: single-flight no-overlap pinned by tests.** A test schedules two `PollAsync` calls concurrently (e.g., starts the second one 100 ms after the first against a 500-ms-slow API) and asserts the second returns an empty list immediately + `poll_overruns_total` increments by 1.
- [ ] **NEW: no fire-and-forget audit clean.** Code grep + manual review of all `async` invocations in the new project confirms zero unobserved Tasks.
- [ ] **NEW: metrics surface verified at /metrics.** Demo-mode smoke run produces all three Brother-specific metrics at the Prometheus endpoint with bounded cardinality.

---

## F. Pause-point additions

In addition to the six v3 §13 pause-points, add:

- §B.2 timestamp authority check during step 3 reveals FOCAS2 captures timestamps at COMPLETION instead of START → flip Brother to match for cross-adapter consistency (or surface the divergence to user for an explicit decision).
- §B.5 metric name verification during step 3 reveals existing FOCAS2 / Modbus metric naming pattern that contradicts the `brother_*` convention I proposed → align with existing convention.
- §B.3 single-flight test reveals an existing supervisor-layer guard that already prevents overlapping polls → the Brother-side guard becomes belt-and-braces (still ship it but document the layering).

---

## G. v3.1 sign-off

The six locks (B.1–B.6) plus the three light touches (C.1–C.3) plus four new DoD items (E) plus three new pause-points (F) close the v3 review's open items.

**Scope of v3.1:** focused amendment, ~250 lines (the reviewer estimated 20–30 lines for the rules themselves; the rest is rationale + cross-references). No v3 sections re-litigated. No catalog re-design.

**Architecture-readiness:** the reviewer's final verdict — "Ready for implementation after a small v3 tightening pass" — is satisfied with v3.1 in place.

**Implementation may start at v3 §10 step 1, with the v3.1 §D doc edits folded in during that step.**

---

**End of v3.1 amendment. Implementation gate cleared.**
