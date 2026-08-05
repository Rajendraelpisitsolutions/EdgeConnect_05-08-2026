# Phase 2 wrap-up — roadmap (v2 plan, LOCKED)

**Status:** v2 — LOCKED after ChatGPT review pass folded in. Reality-check (per substantial item) happens in each item's dedicated v3 plan trail.
**Date:** 2026-05-21
**Predecessor:** [v1 (open questions)](2026-05-21-phase2-wrapup-roadmap.md)
**Predecessor state:** master at `a1ea1aa` (PR #16 + PR #19 merged). 2263 tests, 0 failures.

---

## 0. What v2 changed from v1

ChatGPT review surfaced 9 substantive recommendations. v2 folds in 7 as-is, 2 with framing alterations, 0 rejected.

| # | Review item | Verdict | Where in v2 |
|---|---|---|---|
| 1 | Timeline 4-6 wk → 7-9 wk realistic | ✅ Agree | §1 + §4.1 |
| 2 | M.2c Allowed/Forbidden + `runtime-observability.md` as hard prereq | ✅ Agree (highest-leverage change) | §3.6 + §1 + §6 |
| 3 | Split M.2d into M.2d.1 / .2 / .3 / .4 sub-milestones | ✅ Agree | §3.7 |
| 4 | Chip 3 → "Provisioning Subsystem" framing | 🔧 Alter (cap drift detection from v1 scope) | §3.4.1 |
| 5 | Configuration-identity block on every generated config | ✅ Agree, merged with provenance | §3.4.3 |
| 6 | EREMOS revalidation — 8 measurable success gates | ✅ Agree | §3.5.3 |
| 7 | Offline-scenario test validation | ✅ No-op | §3.3 unchanged |
| 8 | "Operational Intelligence Layer" formal naming | 🔧 Alter (recognize convergence, defer naming) | §4.7 |
| 9 | Parallel-platform-shaping coordination risk | ✅ Agree | §4.6 |

---

## 1. Sequencing + dependencies (UPDATED)

```
┌─────────── Tracks (UPDATED timing) ─────────────┐
│                                                  │
│  TRACK A (small serial fixes, ~2-3 days):       │
│    Chip 4  →  Chip 5                            │
│                                                  │
│  TRACK B (small, independent, ~0.5 day):        │
│    Offline-scenario test                        │
│                                                  │
│  TRACK C (provisioning + revalidation,          │
│  ~2-3 wk):                                       │
│    Chip 3 (Provisioning Subsystem)              │
│       ↓                                          │
│    EREMOS V2 revalidation (3-5 days)            │
│                                                  │
│  TRACK D (substantial UX/observability,         │
│  ~4-5 wk):                                       │
│    runtime-observability.md                     │
│      ←─── hard prerequisite gate                │
│    M.2c Live Tag Watch (2-3 wk)                 │
│       ↓                                          │
│    M.2d Edit-via-Wizard split into:             │
│      M.2d.1 shared primitives (~3-4 days)       │
│      M.2d.2 source wizards (~3-4 days)          │
│      M.2d.3 sink + route editors (~3-4 days)    │
│      M.2d.4 cross-wizard sweep (~2-3 days)      │
│                                                  │
│  + INTEGRATION FALLOUT BUFFER (1 wk)            │
│                                                  │
└──────────────────────────────────────────────────┘
```

**Updated total estimate: ~7-9 weeks before a confident 7-day soak**, including a 1-week integration-fallout buffer. (v1's 4-6 weeks was optimistic — no fallout budget, M.2c under-scoped, Chip 3 under-scoped.)

**Hard prerequisite gate added:** `docs/platform-principles/runtime-observability.md` MUST land before M.2c implementation. The platform-principles doc requires its own strategic review pass + explicit user approval per the existing platform-principles governance (see `docs/platform-principles.md` "when to amend" clause).

---

## 2. Plan-trail discipline per item (UPDATED)

| Item | Plan style | Where |
|---|---|---|
| Chip 4 (Bug 1 P3) | Inline | §3.1 |
| Chip 5 (CONFIG_DIR) | Inline | §3.2 |
| Offline-scenario test | Inline | §3.3 |
| **`runtime-observability.md`** | Platform-principle doc + strategic review | New file (`docs/platform-principles/runtime-observability.md`) |
| Chip 3 (Provisioning Subsystem) | Full v1 → v2 → v3 | `2026-05-XX-chip3-provisioning-plan.md` |
| EREMOS V2 revalidation | Brief v1 → v2 | `2026-05-XX-eremos-v2-revalidation-plan.md` |
| M.2c Live Tag Watch | Full v1 → v2 → v3 | `2026-05-XX-m2c-live-tag-watch-plan.md` |
| M.2d.1 / .2 / .3 / .4 | Brief v1 per sub-milestone | `2026-05-XX-m2d{N}-*-plan.md` (4 files) |

---

## 3. Item-by-item plans (LOCKED)

### 3.1 Chip 4 — Bug 1 P3 buffer path realignment

Scope unchanged from v1 §3.1. **No review notes; carry as-is.**

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Host/CompositionRoot.cs:104` | `options.ConfigDirectory` → `options.ResolvedDataRoot` |
| `src/ElpisEdgeConnect.Core/Routing/DefaultRouteBufferFactory.cs` | Migration shim: move `.db + .db-shm + .db-wal` triplet from old → new path on first open |
| `tests/ElpisEdgeConnect.Core.Tests/Routing/DefaultRouteBufferFactoryTests.cs` | Migration test — populated `.db` + non-trivial `.shm`/`.wal` → assert all three move + db queryable after migration |
| `docs/ops-runbook.md` | Backup/restore guidance points to canonical buffer location (if file exists) |

Estimate: ~1 day, ~50 LOC + ~120 LOC tests.

---

### 3.2 Chip 5 — `EDGECONNECT_CONFIG_DIR` inertness resolution

**Locked: Option B — delete the inert env-var read.** v1 Q1 verdict.

**Locked: yes, add startup deprecation warning if env var is set.** v1 Q2 verdict — 2 lines of code; gives operators a clear signal.

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs:95` | Remove `EDGECONNECT_CONFIG_DIR` env-var read. Add `Console.Error.WriteLine` deprecation notice if the env var is still set. |
| `src/ElpisEdgeConnect.Host/HostOptions.cs` | Remove `ConfigDirectory` field. Trace `ResolvedDataRoot` fallback chain — remove the now-dead branch. |
| `tests/ElpisEdgeConnect.Management.Tests/ConfigApiConfigPathTests.cs` | Update `ConfigApi_BuildCurrentConfigVersionDtoAsync_OtherEnvVarSet_DoesNotMisattribute` — flip to assert `CONFIG_DIR` env var is not recognised at all. |
| Docs / inline comments | Replace `EDGECONNECT_CONFIG_DIR` references with `EDGECONNECT_DATA_ROOT`. |

Estimate: ~0.5 day, ~30 LOC + ~50 LOC tests. **Lands immediately after Chip 4 in the same session.**

---

### 3.3 Offline-scenario lifecycle parity test (M.P2.4 deferred follow-up)

**Locked: test BOTH variants** of HTTPD_MCNINFO failure separately (empty-body 200 AND absent-file 404). v1 Q1 verdict.

| File | Change |
|---|---|
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Parity/Samples/offline-empty/HTTPD_MCNINFO.txt` | Empty file (200 OK, no body) |
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Parity/Samples/offline-404/` | Folder present but no files — server returns 404 for everything |
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Parity/ParityTests.cs` | Two new tests: `LegacyOffline_EmptyMcnInfo_AndNewAdapterStartFailure_DivergeAsDocumented` + `_AbsentMcnInfo_AndNewAdapterStartFailure_DivergeAsDocumented`. Assert: legacy returns `Status=Offline`; new fails `StartAsync` → `AdapterState.Failed`. |
| `docs/sessions/2026-05-21-mp24-handoff.md` | Update §6 deferred-items to mark offline-scenario parity CLOSED. |

Estimate: half day. **Standalone commit** with the M.P2.4 handoff doc edit.

---

### 3.4 Chip 3 — Provisioning Subsystem (re-framed)

**Locked framing change:** this is no longer "bulk-provision tooling." It is the **Provisioning Subsystem** — the canonical mechanism by which fleet-scale CNC source configurations enter the system. Refusing this framing is exactly what would let it decay into "random scripts + CSV hacks" (review item #4).

#### 3.4.1 Subsystem architecture (NEW)

```
┌─────────────────────────────────────────────────┐
│              Provisioning Subsystem             │
│                                                  │
│  ┌──────────────┐    ┌──────────────────────┐  │
│  │ Template     │    │ Validation Pipeline   │  │
│  │ Schema       │───→│ (JSON Schema + cross- │  │
│  │              │    │  field checks)        │  │
│  └──────────────┘    └──────────────────────┘  │
│         │                       │                │
│         ▼                       ▼                │
│  ┌──────────────────────────────────────────┐  │
│  │ Rendering Pipeline                        │  │
│  │ template + CSV row → SourceInstanceConfig │  │
│  └──────────────────────────────────────────┘  │
│         │                                        │
│         ▼                                        │
│  ┌──────────────────────────────────────────┐  │
│  │ Provenance + Configuration Identity       │  │
│  │ (_provisioning block on every output)     │  │
│  └──────────────────────────────────────────┘  │
│         │                                        │
│         ▼                                        │
│  ┌──────────────────────────────────────────┐  │
│  │ Generator CLI                             │  │
│  │ (PowerShell — operator-facing surface)    │  │
│  └──────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

**Scope cap (v1):** drift detection is **explicitly out of v1 scope.** Drift detection deserves install-time data to drift against — we don't have that yet. The subsystem must NOT silently extend into drift detection during v1 implementation; it earns a future milestone.

**Lives at:** `tools/bulk-provision/` for v1. Promotion to `src/ElpisEdgeConnect.Provisioning/` (with runtime API access) is a **future decision** post-soak, post-install — not a v1 commitment.

#### 3.4.2 Locked decisions (verdicts from v1 Q4-Q8)

| Q | Decision |
|---|---|
| Q1 PowerShell vs Python | **PowerShell** — Windows toolchain, no Python dep at customer site. |
| Q2 CSV columns | **`make,instanceId,deviceId,deviceName,host,enabled`** — per-gateway CSV (operator runs tool once per gateway with that gateway's machine list). |
| Q3 Provenance format | **`_provisioning` root block** — valid JSON, canonical parser ignores unknown roots. NOT a JSON-comment header. Merged with configuration-identity per #5. See §3.4.3. |
| Q4 Schema validation | **`Microsoft.NJsonSchema` via PowerShell** — already a project dependency (per Phase 1 W1 decision). Falls back to `dotnet run --project src/ElpisEdgeConnect.SchemaValidation` if PS interop is awkward. Reality-check the exact wiring in v3. |
| Q5 Modbus per-tag CSV composition | **Distinct tool, distinct docs section.** This subsystem is per-instance; Modbus per-tag CSV importer (in `src/ElpisEdgeConnect.Sources.ModbusTcp/Import/`) is per-tag. README explicitly clarifies the distinction. |
| Q6 Edited-file detection | **Content hash check** (SHA-256 of canonicalized JSON minus `_provisioning` block) — resilient to file-system metadata loss. |
| Q7 Template inheritance | **Out for v1.** If a customer ever needs Fanuc-A800 vs Fanuc-A600 templates with different tag sets, two separate template files. Revisit if a real customer asks. |
| Q8 Studio integration | Studio's M.2a Config page has an "Import draft from JSON" button. Verify in reality-check (v3); README documents the workflow. |

#### 3.4.3 Provenance + Configuration Identity block (NEW, locked)

Every generated `gateway.json` carries this `_provisioning` root block:

```json
{
  "_provisioning": {
    "generatedBy": "bulk-provision",
    "generatorVersion": "1.0.0",
    "templateId": "fanuc-standard-v1",
    "fleetId": "100cnc-customer-A",
    "generatedAt": "2026-05-22T08:15:00Z",
    "configFingerprint": "sha256:<hash of file MINUS _provisioning block>",
    "csvFingerprint": "sha256:<hash of input machines.csv>"
  },
  "gateway": { ... },
  "sources": [ ... ],
  "sinks": [ ... ],
  "routes": [ ... ]
}
```

**Each field's purpose** (rationale per review item #5):

- `generatedBy` — distinguishes bulk-provision output from hand-edited / Studio-wizard-authored configs. Refuse-overwrite check looks for this.
- `generatorVersion` — drift between generator versions when re-generating later is detectable.
- `templateId` — which Fanuc/Brother template was applied. Drift analysis later can ask "was this fleet generated from template-fanuc-v1 or v2?"
- `fleetId` — identifies the customer / fleet. Future fleet management needs this. Operator picks the value in their CSV → generator stamp.
- `generatedAt` — ISO 8601 UTC. Support / debugging timeline.
- `configFingerprint` — hash of the file content MINUS the `_provisioning` block itself. Used by the refuse-overwrite check: if recomputed fingerprint matches the stored one, file is untouched; if not, it was hand-edited and the generator refuses to overwrite.
- `csvFingerprint` — hash of the input CSV. Enables "did this fleet's machines list change since last generation?" — high-leverage for support and future drift analysis (still out of v1 scope).

**Schema validation:** the canonical `GatewayConfiguration` schema explicitly accepts an unknown `_provisioning` root object. Update `docs/config-schemas/gateway-configuration.schema.json` to declare `_provisioning` as an optional schema-validated block. Reality-check in v3.

#### 3.4.4 Deliverables

| Folder/file | Purpose |
|---|---|
| `tools/bulk-provision/templates/template-fanuc-v1.json` | Golden Fanuc template. Polling 3000 ms (§7-Q1), ~65 baseline tags (§7-Q2), placeholders for `instanceId`, `deviceId`, `deviceName`, `connection.ipAddress`. |
| `tools/bulk-provision/templates/template-brother-v1.json` | Golden Brother template. Polling 3000 ms, ~75 tags (incl. tools), placeholders for `instanceId`, `deviceId`, `deviceName`, `connection.baseUrl`. |
| `tools/bulk-provision/templates/template-modbus-v1.json` | Golden Modbus template (per-instance — distinct from the per-tag importer). |
| `tools/bulk-provision/generate.ps1` | Generator CLI. Schema validation + provenance/identity stamping + refuse-overwrite check. |
| `tools/bulk-provision/samples/machines-100cnc-customer-A.csv` | 100-row sample (80 Fanuc + 20 Brother per §7-Q5). |
| `tools/bulk-provision/samples/gateway-fanuc-line1.json` | Sample generated output (one of the 4 customer gateways). |
| `tools/bulk-provision/README.md` | CSV format, template structure, golden-source rule, regeneration workflow, EREMOS V2 topic shape, Studio "Import draft from JSON" workflow, distinction from Modbus per-tag CSV importer. |
| `tools/bulk-provision/tests/` | Pester tests — generator unit tests + end-to-end test loading generated output via `ConfigurationManager.CreateDraftAsync`. |
| `docs/config-schemas/gateway-configuration.schema.json` | Add `_provisioning` as an optional root object. |

#### 3.4.5 Definition of done

- [ ] Subsystem architecture per §3.4.1 implemented (template schema, validation pipeline, rendering pipeline, provenance/identity, generator CLI).
- [ ] End-to-end test: 100-row CSV → generator → `gateway.json` → 100 source instances resolve via `Focas2/BrotherHttpSourceConfiguration.FromSourceInstance(...)`.
- [ ] Schema-violation test: deliberate invalid input → clear error → non-zero exit → no file written.
- [ ] Refuse-overwrite test: regenerating over a hand-edited file is rejected with a clear error.
- [ ] Provenance/identity test: every generated file has a valid `_provisioning` block with all 7 fields.
- [ ] README walks an operator through the install-day workflow.
- [ ] Sample `gateway-fanuc-line1.json` checked in.
- [ ] Deployment-readiness §10 acceptance signal — "Bulk-provision generator + templates committed" row checked.
- [ ] **NO drift-detection logic added** (scope cap).

Estimate: ~1.5-2 weeks (review-revised). Full plan-trail discipline: v1 → ChatGPT review → v2 → reality-check → v3 → implementation across 2 sessions.

---

### 3.5 EREMOS V2 contract revalidation

#### 3.5.1 Locked decisions (verdicts from v1 Q9-Q10)

| Q | Decision |
|---|---|
| Q1 Real instance or contract mock | **Real local EREMOS V2 instance if available; contract-driven mock as fallback.** Reality-check whether a local EREMOS V2 instance is achievable in the in-house lab; if not, fall back to a mock subscriber that pins the documented MQTT contract from `shared-knowledge/contracts/eremos-per-tag-mqtt.md`. |
| Q2 Validation scope | **Contract-level only** — subscribe + parse + assert structure matches expected. Do NOT test EREMOS V2's internal storage shape — that's their architecture. |
| Q3 Live or canned | **One-shot integration test** — deterministic, runs under `dotnet test --filter "Category!=Flaky"`, fits CI cadence. |
| Q4 Standalone or soak component | **Both:** standalone pre-soak gate (few minutes) AND a sub-component of the 7-day soak's success criteria (verifying EREMOS V2 keeps consuming throughout the soak). |

#### 3.5.2 Deliverables

| File | Purpose |
|---|---|
| `tests/ElpisEdgeConnect.Integration.Tests/EremosV2ContractTests.cs` | Standalone one-shot integration test. Launches gateway with a bulk-generated config (§3.4 dependency), real Mosquitto on localhost:1883, real EREMOS V2 ingest if available else mock. Runs for ~2 minutes, asserts all 8 success criteria below. |
| `tools/eremos-v2-contract-harness/` (optional) | Standalone harness with `docker-compose.yml` if EREMOS V2 containerization is feasible. |
| `docs/sessions/2026-05-XX-eremos-v2-revalidation-plan.md` | Brief v1 plan trail (this is a brief item per §2). |
| `docs/contracts/eremos-v2-revalidation.md` | Documentation of the 8 success criteria + observed values from the test run. |

#### 3.5.3 Explicit success criteria (NEW, from review item #6)

The test passes iff **all eight** of these gates pass — no subjective interpretation.

| # | Gate | How measured | Pass threshold |
|---|---|---|---|
| 1 | MQTT stability | Track MQTT broker disconnect events over the test window | Zero disconnect storms (>3 disconnects in 60s) |
| 2 | Tag continuity | Track per-tag SequenceNumber gaps | Zero gaps within any single tag's emission stream |
| 3 | Schema stability | Validate every payload against the documented PerTag JSON schema | 100% pass rate; zero schema violations |
| 4 | Topic determinism | Track which topics receive messages | Every emitted topic matches `eremos/{gw}/{deviceClass}/{src}/{tag}` exactly; zero unexpected topic paths |
| 5 | Reconnect behavior | Inject a 30-second broker outage mid-run | Adapter reconnects within 5s of broker recovery; backpressure metrics stay bounded |
| 6 | EREMOS ingestion (parsing drift) | If real instance: poll EREMOS V2's ingest count vs gateway's emit count | Counts equal within 1% over the test window |
| 7 | Historian continuity (no duplicate storms) | Compare {topic, deviceTimestamp, value} tuples against EREMOS V2's stored set | Zero duplicates received within 5-minute windows |
| 8 | Backpressure behavior | Inject a 60-second sink slowness | Store-and-forward buffer fills bounded; intake drops measured; full recovery on broker speedup |

These 8 gates are the **revalidation contract.** Pass means revalidation gate green. Fail means revalidation gate red — fix the gateway, not the test.

Estimate: ~3-5 days (review-revised). Brief v1 → v2 plan trail.

---

### 3.6 M.2c Live Tag Watch (re-framed under Runtime Observability principles)

#### 3.6.1 Hard prerequisite (NEW, from review item #2)

**`docs/platform-principles/runtime-observability.md` MUST land before M.2c implementation begins.** This document locks the platform-level boundary of Runtime Tap — without it, M.2c can quietly become historian / streaming bus / debugging shell / analytics substrate / monitoring system (review item #8 is exactly this risk).

The doc requires its own strategic review pass + explicit user approval per the platform-principles governance clause in `docs/platform-principles.md`.

#### 3.6.2 Locked invariants (Allowed / Forbidden — NEW from review item #2)

These will be promoted into `runtime-observability.md`'s body. M.2c's implementation is bounded by them.

| Allowed | Forbidden |
|---|---|
| Observational runtime stream | NO runtime mutation — Runtime Tap is read-only side-channel |
| Bounded retention (last ≤100 values per tag OR last ≤5 min, whichever is smaller) | NO historian semantics — persistence is the Phase 5 historian milestone |
| Transient watch sessions (operator's browser open + small refresh buffer) | NO durable subscriptions — no resume-from-cursor, no offline queueing |
| Sampled diagnostics (operator-driven, server-side filtering by tag-path) | NO replay pipeline — no time-warp, no go-back-and-see, no scrubbing |
| Per-route + per-source introspection | NO cross-route orchestration — no joining streams, no cross-route transforms |
| Performance budget: ≤1% CPU overhead at 100-CNC scale (~540 pts/sec/gateway) | NO write-back to data path |
| Read-only side-channel via `Channel<T>` or `IObservable<T>` with bounded buffer, dropped-oldest on overflow | NO blocking the supervisor's hot loop |

#### 3.6.3 Locked decisions (verdicts from v1 Q11-Q15)

| Q | Decision |
|---|---|
| Q1 Subscription model | **Server-Sent Events (SSE).** Studio exposes `/api/v1/live-tags?source=X&tags=Y,Z`; native browser `EventSource` consumes. Lowest complexity, works in-process. |
| Q2 History buffer location | **Per-source supervisor + bounded ring** (last 100 values OR last 5 minutes per tag, whichever is smaller). |
| Q3 Tap mechanism | **`Channel<CanonicalDataPoint>` per source** with bounded buffer + dropped-oldest on overflow. Non-blocking. Supervisor's `RunSourceLoopAsync` does a non-blocking `TryWrite` after emitting to the canonical pipeline. |
| Q4 UI scope (minimum-viable) | Single source per session; multi-tag with operator-selected tag-path filter; stale indicator (>2× poll interval); quality indicator (Good/Bad/Uncertain). |
| Q5 Server-side filtering | **Yes** — Studio sends a list of tag-paths in the SSE query; supervisor only emits matching points. Critical for 100-CNC scale. |
| Q6 Authentication | **Defer to Phase 4 auth story.** Document the localhost-only posture in the runtime-observability.md as a known temporary state with Phase 4 ADR cross-ref. |
| Q7 Historical persistence | **Out for v1.** Phase 5 historian is separate. Live Tag Watch's "last 5 minutes" is in-memory only. |

#### 3.6.4 Deliverables

| Component | Notes |
|---|---|
| `docs/platform-principles/runtime-observability.md` | Locks §3.6.2 + retention + subscription + determinism + performance budgets + AI-interaction model. Strategic review required. |
| `src/ElpisEdgeConnect.Core/Diagnostics/IRuntimeTap.cs` | Read-only side-channel contract. `IObservable<CanonicalDataPoint> Subscribe(string sourceId, IEnumerable<string> tagPaths)`. |
| `src/ElpisEdgeConnect.Core/Diagnostics/RuntimeTap.cs` | Per-source `Channel<>`-backed implementation. Bounded buffer, dropped-oldest on overflow. |
| `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` (edit) | Non-blocking `TryWrite` to RuntimeTap after canonical-pipeline emission. |
| `src/ElpisEdgeConnect.Management/Api/LiveTagsApi.cs` | SSE endpoint at `/api/v1/live-tags`. |
| `src/ElpisEdgeConnect.Management/Components/Pages/LiveTagWatch.razor` | Operator-facing page. Source picker, tag-path filter, value table with stale + quality indicators. |
| Tests | RuntimeTap unit tests + LiveTagsApi SSE tests + LiveTagWatch page model tests + performance-budget sanity test. |

Estimate: ~2-3 weeks (review-revised). Full plan-trail discipline. **Cannot start until `runtime-observability.md` is reviewed + approved + landed.**

---

### 3.7 M.2d Edit-via-Wizard — split into 4 sub-milestones (NEW from review item #3)

M.2d v1 framed as a single sweep was a trap ("small cleanup milestone becomes platform rewrite"). v2 splits into 4 explicit sub-milestones with their own DoDs.

#### 3.7.1 M.2d.1 — Shared primitives (~3-4 days)

**Scope:** extract the common UX vocabulary from the existing 6 wizards into shared, reusable components. No wizard touched yet — just builds the components.

Deliverables:
- `Components/Shared/WizardShell.razor` — header + numbered sections + footer + save state.
- `Components/Shared/WizardValidationBanner.razor` — surfaces errors + warnings, links to fields.
- `Components/Shared/WizardWatchSlot.razor` — placeholder for embedded M.2c Live Tag Watch (renders nothing if M.2c not yet wired; ready for M.2d.2/.3 to populate).
- `Components/Shared/WizardActions.razor` — Save / Cancel / Test Connection buttons.
- `Wizards/EditModeContext.cs` — discriminates Add vs Edit; loads existing config into wizard model.

DoD: components exist with unit tests; no wizard depends on them yet.

#### 3.7.2 M.2d.2 — Source wizards (~3-4 days)

**Scope:** apply M.2d.1 primitives to source wizards. Backfill M.P2.4 Q12.

Deliverables:
- `AddBrotherHttpSource.razor` — adopts WizardShell + Edit mode. **Adds Test Connection button** (M.P2.4 Q12 backfill — posts to new `/api/v1/sources/brother-http/probe` endpoint firing `HTTPD_MCNINFO`).
- `AddFocas2Source.razor` — adopts WizardShell + Edit mode. Existing Browse Controller subsumed under the standardised Test Connection pattern.
- `AddModbusSource.razor` — adopts WizardShell + Edit mode. Test Connection probes the TCP socket.

DoD: all source wizards on the shared shell; edit-mode tests green; Test Connection works on all three.

#### 3.7.3 M.2d.3 — Sink + route editors (~3-4 days)

**Scope:** apply M.2d.1 primitives to sink wizards + route wizard.

Deliverables:
- `AddMqttDestination.razor` — adopts WizardShell + Edit mode.
- `AddOpcUaServerDestination.razor` — adopts WizardShell + Edit mode.
- `AddRoute.razor` — adopts WizardShell + Edit mode.

DoD: all sink/route wizards on shared shell; edit-mode tests green.

#### 3.7.4 M.2d.4 — Cross-wizard consistency sweep (~2-3 days)

**Scope:** standardise validation patterns + UX polish across all 6 wizards.

Deliverables:
- Extend M.2b.6.2's `ModbusTagValidator` composition pattern to every protocol's per-instance validation.
- Validation banner unification (same severity classes, same link behaviour).
- Final UX polish + docs updates.

DoD: all 6 wizards pass the same cross-wizard consistency audit; ADR added documenting the wizard contract.

**Total: ~12-15 days = 1.5-2 weeks.** Aligns with ChatGPT's review estimate. Each sub-milestone is its own PR.

---

## 4. Cross-cutting concerns (UPDATED)

### 4.1 Test posture goals (revised totals)

| After item | Expected test count |
|---|---|
| Chip 4 + Chip 5 + offline-scenario | ~2280 (+17) |
| Chip 3 | ~2315 (+35) |
| EREMOS V2 revalidation | ~2325 (+10) |
| M.2c Live Tag Watch | ~2410 (+85) |
| M.2d.1 / .2 / .3 / .4 | ~2470 (+60) |

Cumulative: **~2470 tests** after the wrap-up. From today's 2263, that's ~+207 over 7-9 weeks.

### 4.2 Deployment-readiness §10 acceptance signal trajectory

After Chip 3 + EREMOS V2 land, three of the remaining five checkboxes close. Soak + customer-site acceptance plan remain.

### 4.3 §1 deployment-readiness gap analysis edit

Brother HTTP row still shows "❌ **NOT migrated**" — should be ✅. Folded into the Chip 4 + Chip 5 commit's housekeeping.

### 4.4 Plan-trail file naming convention

Unchanged from v1 §4.4.

### 4.5 Worktree / branch posture

Unchanged from v1 §4.5.

### 4.6 Coordination risk (NEW, from review item #9)

**The risk:** multiple platform-shaping milestones in flight simultaneously — provisioning subsystem, Runtime Tap, wizard consistency framework, EREMOS contract validation, AI groundwork, diagnostics evolution, deployment stabilisation, soak preparation. They interact; one item's design quietly forces re-work in another.

**Symptoms to watch for:**
- Scope creep across milestones (e.g., M.2c suddenly wants historian semantics).
- ADR drift (decisions made implicitly without writing them down).
- "Just one more thing" tendency at the end of substantial milestones.
- Cross-milestone refactoring that wasn't planned.

**Mitigation (locked):**
- **Strict per-milestone plan-trail discipline** (the M.P2.4 cadence is the standard).
- **ADR rigor** — every architectural decision that survives a milestone gets an ADR; per CLAUDE.md "Before making any architectural choice, scan `docs/decisions/` first — these are locked unless the user explicitly invokes a decision number to revisit."
- **Scope locks** — every milestone's v2 plan has an explicit "out of scope" subsection.
- **Sequencing locks** — `runtime-observability.md` BEFORE M.2c; M.2c BEFORE M.2d; Chip 4 BEFORE Chip 5; etc.

### 4.7 Convergence note (NEW, from review item #8)

**Observation:** Runtime Tap (M.2c substrate) + diagnostics evolution + future AI substrate are emerging as one architectural layer. They share the same observational-side-channel discipline, the same bounded-retention semantics, the same per-route scoping.

**Locked decision:** v2 does NOT formally name this layer or lock its architecture. Naming the layer before M.2c's substrate lands would risk getting the name + boundaries wrong. After M.2c lands and the runtime-observability principles are concrete artifacts, a follow-up ADR (numbered after the existing 0010) can name the layer.

**Working name for internal reference only:** "operational intelligence layer." Don't promote to user-facing surfaces until the ADR lands.

---

## 5. Resolved + remaining questions

### 5.1 Resolved (v1 Q1-Q20 verdicts now locked)

| # | Resolution location |
|---|---|
| Q1 (Chip 5 Option A vs B) | §3.2 — Option B locked |
| Q2 (Chip 5 deprecation warning) | §3.2 — yes, locked |
| Q3 (Offline-scenario both variants) | §3.3 — yes, both locked |
| Q4 (Chip 3 PowerShell vs Python) | §3.4.2 — PowerShell locked |
| Q5 (Chip 3 CSV layout) | §3.4.2 — locked |
| Q6 (Chip 3 provenance format) | §3.4.3 — `_provisioning` root block, merged with config identity |
| Q7 (Chip 3 edited-file detection) | §3.4.2 — content hash locked |
| Q8 (Chip 3 template inheritance) | §3.4.2 — out for v1 locked |
| Q9 (EREMOS real vs mock) | §3.5.1 — real if available, mock fallback |
| Q10 (EREMOS one-shot vs live) | §3.5.1 — one-shot locked + sub-component of soak |
| Q11 (M.2c subscription model) | §3.6.3 — SSE locked |
| Q12 (M.2c history buffer location) | §3.6.3 — per-source supervisor + bounded ring |
| Q13 (M.2c UI scope) | §3.6.3 — minimum-viable locked |
| Q14 (M.2c server-side filtering) | §3.6.3 — yes locked |
| Q15 (M.2c auth) | §3.6.3 — defer to Phase 4 locked |
| Q16-Q18 (M.2d shared base, PATCH vs full-replace, Test Connection pattern) | §3.7 — superseded by 4-phase split + standardisation in M.2d.4 |
| Q19 (M.2c before M.2d) | §1 — confirmed, locked sequencing |
| Q20 (offline-scenario commit cadence) | §3.3 — standalone commit confirmed |

### 5.2 Remaining open (for v3 reality-check)

| # | Item | Question |
|---|---|---|
| Q21 | Chip 3 | Does `src/ElpisEdgeConnect.SchemaValidation/` already exist with a callable surface? Reality-check determines whether PS interop is feasible or we shell out. |
| Q22 | Chip 3 Studio integration | Verify Studio's M.2a Config page actually has an "Import draft from JSON" button today. Reality-check the workflow. |
| Q23 | Chip 3 schema | Add `_provisioning` to `docs/config-schemas/gateway-configuration.schema.json` — verify the canonical parser ignores unknown roots (it should, but confirm). |
| Q24 | M.2c | Does `SourceSupervisor.RunSourceLoopAsync` have a clean injection point for `TryWrite` to RuntimeTap without restructuring the loop? Reality-check during the M.2c v3 pass. |
| Q25 | M.2c | What's the SSE keep-alive cadence needed for browsers to not time-out the connection on a quiet topic (operator selected a tag with low update rate)? Reality-check in v3. |
| Q26 | M.2d.2 Brother Test Connection endpoint | Does the existing `/api/v1/sources/focas2/probe` endpoint shape generalize to Brother, or does Brother need its own probe-endpoint contract? Reality-check before M.2d.2 starts. |

These are reality-check items — not architectural decisions. They get resolved during each substantial item's v3 pass without re-opening v2.

---

## 6. Next steps

1. **You ChatGPT-review v2.** Expect a much shorter review — the substantive issues from v1 review are folded in.
2. **If v2 ratified:** I produce `docs/platform-principles/runtime-observability.md` v1 (the hard prerequisite for M.2c). It gets its own strategic review pass per the platform-principles governance.
3. **In parallel:** I produce the dedicated v1 plan-trail files for Chip 3 + EREMOS V2 + M.2c + M.2d.1/2/3/4 (7 docs total — could batch as one session or split).
4. **Implementation kicks off** with the lowest-risk session: Chip 4 + Chip 5 + offline-scenario test (small, well-scoped, no dependencies on the substantial-item plan trails).

---

**End of v2 wrap-up roadmap. LOCKED — ready for review.**
