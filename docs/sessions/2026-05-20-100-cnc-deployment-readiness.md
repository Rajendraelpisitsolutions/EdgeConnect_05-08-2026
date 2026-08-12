# 100-CNC deployment — readiness assessment

**Status:** **DRAFT v1** — living document; revise as the customer profile firms up and milestones close.
**Date:** 2026-05-20
**Form:** Deployment-specific gap analysis + execution plan. Not an architectural document — references the v2 product roadmap and ARCHITECTURE_BLUEPRINT.md as source-of-truth and only captures deltas relevant to this customer.

---

## 0. Customer profile (confirmed)

| Field | Value |
|---|---|
| Machine count | **100** CNCs |
| Protocol split | **80 Fanuc FOCAS2 + 20 Brother HTTP** (may shift over time) |
| Tag set | **Identical across all machines per protocol** — only machine name + IP differ per instance. ~65 tags baseline, ~75 with tool-related tags |
| Polling cadence | **3 seconds per tag** (operator-configurable) |
| Aggregate ingest | **~2,200 points/sec across all 100 machines** (well under the 19M/sec Phase 1 ceiling) |
| Northbound | **MQTT only** (EREMOS V2 consumes — already deployed at the customer) |
| MQTT broker | **Our scope to install** — recommendation: Mosquitto 2.x, single broker, no cluster |
| Network | **Flat** (no per-line VLANs) |
| Operator | **Maintenance staff** (not developers, not IT) |
| Maintenance window | **Monthly** — restarts allowed only during this window |
| Customer-site soak window | **Not available** — readiness must be proven in-house before install |
| Backup / restore | **No customer plan yet** — recommendation captured in §8 |

The "identical tag set across all 100 machines" constraint and the "maintenance staff as operator" constraint are the two most important shaping facts for this deployment. The throughput and protocol-split facts are also load-bearing: 80/20 Fanuc/Brother drives gateway grouping (§4) and confirms M.P2.4 Brother migration as a hard blocker.

---

## 1. Gap analysis — what's deployable today vs what this customer needs

### What is built in the new architecture (Phase 2, in flight)

| Capability | Status | Customer-impact note |
|---|---|---|
| Fanuc FOCAS2 source adapter | ✅ Migrated to `Sources.Focas2` | Production-ready for this customer |
| Brother HTTP source adapter | ✅ Migrated to `Sources.BrotherHttp` (M.P2.4, PR #19) | Production-ready alongside FOCAS2 |
| MQTT sink adapter | ✅ Production-ready | Tested against real Mosquitto; 41 tests; PerTag-mode topic shape matches EREMOS V2 subscription |
| Store-and-forward buffer (per-route SQLite) | ✅ Phase 1 closed | Survives MQTT broker outages without data loss — non-negotiable for 100-CNC floor |
| Connectivity Studio UI | ✅ M.2a, M.2b.x stack | Operator-friendly config, draft/validate/apply, hot reload |
| Configuration hot-reload | ✅ M.P2.2 closed | Add/remove machines without gateway restart |
| Diagnostics (3-way: source / pipeline / sink) | ✅ Phase 1 + Phase 2 polish | Operator can answer "is data flowing?" per machine |
| License management | ✅ Phase 1 (offline, RSA-signed) | Customer install is offline — no phone-home, matches industrial reality |
| Bulk source provisioning (CSV / template) | ⚠️ Partial — only Modbus has a CSV importer today | **Critical for 100-machine commissioning** — see §3 |
| Soak validation at scale | ⚠️ Phase 1 baseline was 4hr; 7-day in-house soak is a deferred v1 ship gate | **Must close before this customer** — see §5 |

### Deployment blockers (must close before customer install)

1. **Brother HTTP migration** (§2)
2. **Bulk source provisioning strategy** (§3)
3. **In-house 7-day soak at customer-representative scale** (§5)

Everything else on the v2 product roadmap (Live Tag Watch / Edit-via-Wizard / Shared List Infrastructure / etc.) is **nice-to-have** for this deployment but not blocking. Specifically:

- **Milestone K (OPC UA security hardening)** → NOT on critical path (customer is MQTT-only)
- **M.2c Live Tag Watch** → very useful at commissioning time but the gateway will function without it; operators can fall back to MQTT-side inspection (`mosquitto_sub`) and the existing diagnostics page
- **M.2g First-run onboarding** → out — bulk provisioning bypasses this anyway
- **M.2k Fleet management** → highly relevant once installed (4 gateways = exactly the "≥5 multi-gateway customer" trigger v2 roadmap names), but ships post-deployment

---

## 2. Brother HTTP migration — milestone M.P2.4 — **COMPLETE 2026-05-21**

**Status:** **COMPLETE.** Delivered via [PR #19](https://github.com/elpisitsolutions/EdgeConnect/pull/19) (7 commits, plan trail v1 → v3.1). Manual end-to-end verification confirmed in Studio with `EDGECONNECT_BROTHER_FAKE_MODE=true`: wizard at `/sources/new/brother-http` → source added → route attached to MQTT sink → canonical points flowing to `mosquitto_sub` with synthetic state evolution (v3.1 §C.2). §9 topic-shape cross-check passed — Brother PerTag topics matched the `eremos/+/cnc/+/+` EREMOS V2 subscription.

**Handoff:** [`docs/sessions/2026-05-21-mp24-handoff.md`](2026-05-21-mp24-handoff.md) — full commit-by-commit + deferred-items summary.

**Test posture at handoff:** 2263 tests pass across 12 projects (0 failures), 169 of those in `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/` covering the canonical catalog (incl. v3 §5 structural-purity contract test), six per-endpoint collectors, the adapter's five v3.1 §B determinism + observability locks, demo-mode state evolution, and 5-scenario parity against the legacy `BrotherHttpDataSource` oracle (legacy ⊆ new subset assertion).

**Original scope delivered:**

| Component | Shipped as |
|---|---|
| Source adapter project | `src/ElpisEdgeConnect.Sources.BrotherHttp/` |
| Typed config record | `BrotherHttpSourceConfiguration` with `FromSourceInstance` factory |
| Adapter implementation | `BrotherHttpSourceAdapter : ISourceAdapter` (with v3.1 §B locks wired) |
| API abstraction | `IBrotherHttpApi` + real impl (`BrotherHttpHttpApi` via `IHttpClientFactory` Pattern C) + synthetic (`BrotherHttpDemoApi`) |
| Canonical catalog | `BrotherTagMap` with structural-purity contract test (v3 §5) |
| Six collectors | `Collectors/{MachineInfo,CycleTime,WorkCounter,AtcTools,Alarm,Maintenance}Collector.cs` |
| DI registration | `BrotherHttpRegistrationExtensions.cs` + wire-in at `EdgeConnectComposition` + `RegistrationFactory` dispatcher |
| License module key | `source-brother-http` added to `LicenseModuleKeys.cs` + `docs/licensing/module-catalog.md` |
| Studio wizard | `AddBrotherHttpSource.razor` + picker tile flipped to Available + `BrotherHttpSourceWizardModel` |
| Parity infrastructure | `BrotherHttpTestServer` (HttpListener) + `LegacyCanonicalMapper` (test-only) + 30 fixture files |

**Deferred / known follow-ups (non-blocking):** see [handoff §6](2026-05-21-mp24-handoff.md#6-deferred--known-follow-ups). Headline items: offline-scenario lifecycle parity test, Test Connection button in wizard (M.2d sweep), MTConnect migration (Q-MTC, still out of scope).

---

## 3. Bulk source provisioning — strategy

The "100 machines, identical tag set, only name+IP differs" constraint changes the problem entirely. Hand-wiring 100 source entries through the Studio wizard is operationally infeasible (and any operator would loudly reject it). Three options ordered by deployment-readiness:

### Option A — Template + generator script (RECOMMENDED for this deployment)

**The cheapest correct answer.** Zero new platform code needed; uses the existing `gateway.json` schema directly.

**Mechanics:**

1. Author **one** `template-fanuc.json` source entry containing the full tag definitions, polling settings, retry/backoff, etc., with placeholders for `instanceId`, `deviceId`, `deviceName`, `connection.host`.
2. Author **one** `template-brother.json` similarly (post-M.P2.4).
3. Author **one** `machines.csv` with rows: `make,name,ip` (e.g. `fanuc,Line1-CNC-01,192.168.1.101`).
4. Run a small generator (PowerShell or Python, ~50 LOC) that reads CSV + templates, produces `gateway.json` with 100 source entries.
5. Drop the generated file into `<dataRoot>/config/current.json`; gateway loads on next start.

**Pros:**
- No platform changes — ships today
- Operator-friendly: customer adds/removes a row in CSV, re-runs generator, applies via Studio import-draft button
- Deterministic + audit-friendly — the CSV is the source-of-truth and lives in source control

**Cons:**
- Operator must run the generator (light command-line literacy required)
- No live "preview" — operator has to apply through Studio to see issues

**Risk:** the generator script is bespoke per customer until we standardize. **Standardize early** (commit it to `tools/bulk-provision/` in the repo) and reuse for the next customer.

#### Locked discipline — golden-source templates

Every machine-type template (`template-fanuc.json`, `template-brother.json`, `template-modbus.json`, …) is **version-controlled and treated as the single source-of-truth**. Operators never hand-edit a generated 100-source `gateway.json` directly. All changes flow:

```
template-fanuc.json  ──►  re-run generator  ──►  new gateway.json  ──►  Studio Import draft  ──►  Validate  ──►  Apply
```

**Why this is locked, not optional:**

- **Prevents per-CNC drift.** With 100 machines, a stray hand-edit to one source's tag list is invisible in code review and silent in operations — until that one machine reports different data than its 99 peers, six months later, during an OEE post-mortem. The template + regenerate workflow makes drift impossible by construction.
- **Keeps commissioning reproducible.** A future site visit reproduces the exact deployed config from `template + machines.csv` — no folklore, no "but Ahmed adjusted line 7 last March." Both inputs live in source control.
- **Simplifies support and debugging.** When the customer reports "machine 47 is misbehaving," the support engineer reads `template-fanuc.json` once, not 100 source entries. Symptoms localize to per-machine fields (IP, identity) or to the template — never to a per-machine quirk.
- **Enables deterministic regeneration.** When tags evolve (new datapoint added), polling cadence changes (customer asks for 50ms instead of 100ms), or the EREMOS V2 MQTT contract adjusts (new topic suffix, new payload field), the change lands in **one place** — the template — and propagates to all 100 sources atomically on the next regenerate.
- **Forces audit-trail integrity.** Studio's draft → validate → apply round-trip captures the apply event in the audit chain. With the generator workflow, the audit chain reflects whole-template changes, not random per-machine edits, so the chain is meaningful for incident response.

**Enforcement** (built into the generator per the bulk-provision tooling task):

- The generator emits a `gateway.json` with a provenance header (`Generated-By: bulk-provision` + content hash of the inputs) and **refuses to re-emit over a file that has been hand-edited since the last generation**. Operators see a clear error pointing at this discipline.
- The README under `tools/bulk-provision/` documents the workflow as the locked install + maintenance path; the customer-side runbook (the "swap a gateway" playbook in §8 risks) defers to it.

### Option B — In-Studio "Clone source" + "Import from CSV"

**Proper platform feature.** Operator picks one fully-configured source, hits Clone, the wizard pre-fills everything and prompts only for the new identity + host. Plus a "Bulk import from CSV" button that reads a (name, IP) CSV against a chosen template source.

**Scope:** ~3–4 days. Surface as a new milestone — call it **M.2b.7 Source duplication + bulk-import** — could ship parallel to M.P2.4 since they touch different files.

**Recommend:** ship Option A for THIS customer's go-live; ship Option B as a fast-follow before the next multi-machine customer.

### Option C — Programmatic API for bulk creation

**Build a `POST /api/v1/sources/bulk` endpoint** that accepts `{ template, instances: [{instanceId, deviceId, host}] }` and emits draft entries via `ConfigurationManager`.

Heaviest of the three. Defer — not needed for this customer; revisit if integrators ask for programmatic access.

### Decision for this customer

**Lock Option A.** Build the generator script + template authoring once, reuse for FOCAS2 and Brother HTTP (post-M.P2.4). Schedule Option B as the platform follow-up; do NOT block customer install on it.

---

## 4. Recommended deployment topology

**4 gateways, grouped by protocol** (driven by the locked 80/20 Fanuc/Brother split from §7-Q5):

| Gateway | Protocol | Machines | Rationale |
|---|---|---|---|
| GW-1 | Fanuc FOCAS2 | ~27 | Cluster A — line/cell group A |
| GW-2 | Fanuc FOCAS2 | ~27 | Cluster B — line/cell group B |
| GW-3 | Fanuc FOCAS2 | ~26 | Cluster C — line/cell group C |
| GW-4 | Brother HTTP | 20 | All Brother machines on one gateway — own ops surface, own license module, no Fanuc contention |

**Why protocol-aligned grouping (vs mixed gateways):**
- **Cleaner ops surface** — when something breaks on the Brother gateway, support engineer reads one protocol's docs, not two
- **Independent failure domains** — a FOCAS2 handle leak can't take Brother machines offline
- **License module clarity** — each gateway's license file references only the modules it uses (`source-focas2` vs `source-brother-http`)
- **Growth headroom** — Brother gateway has plenty of capacity if the split shifts toward more Brother machines later (§7-Q5 notes "may vary")

**Driver factors:**

| Driver | Rationale |
|---|---|
| FOCAS2 handle exhaustion | 1 socket + 1 library handle per machine; ~27 concurrent on one process is comfortable, ~80 on one process is risky historically |
| Blast radius | One gateway down = 20–27 machines silent (best-case 20, worst-case 27) — bounded and recoverable per §8 restore procedure |
| Per-adapter isolation (Lock #10) | Already protects within a gateway — but only fails-soft, doesn't eliminate process-level risk |
| Flat network (§7-Q4 locked) | Topology decisions driven purely by blast-radius and handle-pool, not network segments |
| Maintenance windows (§7-Q9 locked) | Monthly window — rolling restart across 4 gateways is staffable within a single window |

**Each gateway publishes to the same MQTT broker.** Topic shape: `eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}` — EREMOS V2 subscription `eremos/+/+/+/+` covers all 4 gateways under one subscriber.

**Hardware sizing per gateway** (based on Phase 1 baseline + this customer's now-locked load profile):

| Resource | Phase 1 baseline (single gateway) | This customer (per gateway, 20–27 CNCs) |
|---|---|---|
| Sustained throughput | 19.15M points/sec (SqliteBuffer mode) | ~600 points/sec (27 CNCs × ~75 tags × 1/3 Hz) — **0.003% of measured ceiling** |
| Memory | <500 MB headroom under sustained 5kpts/sec | Comfortable — well under any reasonable target |
| Disk (store-and-forward) | Sized for outage window | Recommend 5 GB free per gateway: SQLite + audit log + 30-day backup retention |
| CPU | Modest — Phase 1 measured single-digit % on the benchmark box | Single-core idle most of the time |

Recommended box class: standard industrial PC (4-core Atom/Celeron or above, 8GB RAM, 120GB SSD). Nothing exotic. Same box class for all 4 gateways — simpler procurement, simpler spares.

---

## 5. Pre-deployment validation plan (in-house — replaces customer-site soak)

Since the customer can't host a 30-day soak, **the soak happens in our lab before install**. This is also the deferred "7-day continuous soak — v1 ship gate" from [PHASE2_ENTRY.md](../PHASE2_ENTRY.md). Two birds, one stone.

### In-house soak — 7-day continuous

Locked profile per the customer answers in §7:

| Aspect | Configuration |
|---|---|
| Mock source count | **100 mock sources — 80 Fanuc-shaped + 20 Brother-shaped** (matches §7-Q5 locked split) |
| Tag count per source | **65 tags baseline** per mock source (§7-Q2 locked). Run a separate 24-hour spike test at **75 tags** to validate the tool-enabled scenario. |
| Polling cadence | **3 seconds per tag** (§7-Q1 locked) — aggregate ~2,200 pts/sec across all 100 mocks |
| Gateway topology | **4 mock gateways** mirroring the deployment topology (3 Fanuc + 1 Brother) to surface any cross-gateway interference at the broker layer |
| Sink | Real **Mosquitto 2.x** broker on a separate box (§7-Q3 locked broker choice) — representative network hop |
| Subscriber | **Real EREMOS V2 instance** subscribing to `eremos/+/+/+/+` (§7-Q8 confirms EREMOS V2 is already deployed at the customer — same broker contract validation done in-house) |
| Duration | **7 days continuous** (extends Phase 1's 4-hour leak harness pass to v1's ship gate) |
| Mid-soak restart drill | **Day 3 and Day 5**: force-restart each of the 4 gateways in sequence, verify clean drain post-restart (§9 monthly-restart resilience — load-bearing per Bug 2's resolution gate) |
| Pass criteria | Zero memory growth, zero file-handle growth, zero audit-chain corruption, MQTT delivery rate ≥99.99%, store-and-forward catches up cleanly after every restart and every forced broker outage, **EREMOS V2 sees uninterrupted telemetry on the same per-gateway topic prefixes across all restarts** |
| Failure-injection events | Periodic broker disconnects (every 12hr × 30min outage), simulated network jitter, simulated source disconnects, the two mid-soak restart drills above |

**Owner:** schedule with the FOCAS2 migration leg of Phase 2 — the harness exists and just needs to be re-pointed at this scale. Estimate: 1 day of harness configuration + 7 days wall-clock.

### Customer-site acceptance test — 48-hour smoke (replaces a soak)

What we run AT the customer, *after* install but *before* go-live:

| Step | Goal | Duration |
|---|---|---|
| 1. Install + verify all 100 sources Connected | Discovery, basic wiring | < 4 hours |
| 2. Verify MQTT publish rate matches EREMOS V2 ingest expectations | End-to-end data path | < 1 hour |
| 3. **48-hour continuous run** — monitor memory, handle count, audit chain growth, delivery rate | Stability at customer scale | 48 hours wall-clock |
| 4. Forced broker outage drill (30 min) — verify store-and-forward catches up | Resilience proof | < 2 hours |
| 5. Forced gateway restart — verify reload completes within target window | Operational drill | < 1 hour |

48 hours is achievable in a normal pre-go-live commissioning week and gives statistically meaningful signal on the failure modes the in-house soak already cleared.

### Acceptance-test report template

Capture and hand to customer engineering: process metrics, MQTT delivery rate, fault-registry contents (should be empty), Prometheus metrics export, audit chain hash.

### Release-blocking bugs surfaced during M.2b.6.2 smoke (2026-05-20)

Two pre-existing runtime defects were uncovered while smoke-testing M.2b.6.2 against the Modbus simulator. **Both must be resolved before the 7-day in-house soak runs**, because a soak with these defects in place would only demonstrate data loss, not produce a productive measurement.

#### Bug 2 — Sink publish path silently dead (P0, release-blocker)

A route in `Running` state with non-empty persisted buffer, registered sink, and zero degradation/recovery events **does not attempt any publishes**. The buffer fills to `MaxDepth`, intake drops 100% of source points, and `mosquitto_sub` sees nothing. Pre-existing buffered data does not drain post-restart either, which isolates the defect from pure backpressure-controller behavior and points to **worker-task observability, lifecycle truthfulness, or sink-loop liveness**.

**The load-bearing invariant being violated:**

> A route in `Running` state with a non-empty persisted buffer, a registered sink, and zero degradation/recovery events **must eventually attempt at least one publish or emit a sink fault.**

**Why this blocks the 100-CNC deployment:**

- Store-and-forward is the headline architectural feature for the customer install — survives broker outages without data loss
- This defect makes store-and-forward a no-op: data goes in, nothing comes out, every restart preserves the same broken state
- A 7-day soak with this in place would just show 7 days of accumulated buffer growth + total publish failure — useless as a stability signal
- Customer install would result in immediate, complete data loss after the first time the buffer hits MaxDepth

**Status:** queued as a dedicated chip (P0). Full investigation suspects + reproduction recipe + invariant-based test plan captured in the chip prompt. Pick up as a focused session — explicitly **not intermingled with M.P2.4 Brother HTTP migration or bulk-provision tooling**, which both wait until Bug 2 closes.

**Resolution gate:** invariant-based test passes + restart-with-non-empty-buffer drain test passes + `mosquitto_sub` floods within seconds of a fresh start against pre-existing buffered data.

#### Bug 1 — Buffer path misaligned with DataRoot (P3, non-blocking)

`DefaultRouteBufferFactory` is wired with `options.ConfigDirectory` at `CompositionRoot.cs:104`, but is documented to receive the **data root**. Buffer SQLite files end up at `<dataRoot>/config/buffer/` instead of `<dataRoot>/buffer/`. Docs, blueprint (§19.3), and code disagree.

**Why it matters for this deployment** (despite P3):

- Operators planning backup/restore for the 100-CNC install need to know exactly where store-and-forward data lives — current answer differs from documentation
- Couples buffer location to the inert `EDGECONNECT_CONFIG_DIR` override (separate chip) — fixing that without realigning buffers first would unintentionally move buffer locations when operators tweak the config-dir env var

**Status:** queued as a chip (P3). One-line CompositionRoot fix + a migration shim that moves the `.db + .db-shm + .db-wal` triplet from the old path to the canonical path on first open. Land any time post-M.2b.6.2 merge, lower priority than Bug 2 and M.P2.4.

#### Updated sequencing implication

The deployment timeline in §6 now has an extra gate: **Bug 2 closes before the 7-day in-house soak begins.** Estimate adds ~1 week of focused investigation + fix + test, on top of the existing 3–4 week envelope. Bug 1 can ride along whenever convenient — does not extend the schedule.

---

## 6. Critical milestone sequence for this deployment

```
─── Now ────────────────────────────────────────────────────────────────────►
│
├─ M.2b.6.2  Smoke-driven wizard hardening      [merged 2026-05-20]
│
├─── Pre-customer must-haves ─────────────────────────────────────────────►
│
├─ Bug 2    Sink publish path silently dead     🚨 P0 BLOCKER  (~1 week)
│           ↳ MUST close before soak; do not intermingle with Brother work
│
├─ M.P2.4   Brother HTTP source adapter         ✅ COMPLETE 2026-05-21 (PR #19)
├─ Tools    Bulk-provision generator script     (Option A, ~2–3 days)
├─ Soak     7-day in-house soak @ 100 sources   (1 day setup + 7 days wall)
│           ↳ Gated on Bug 2 RESOLVED
│
├─── Customer install window ────────────────────────────────────────────►
│
├─ Install + bulk-provision via Option A
├─ 48-hour acceptance test on-site
├─ Go-live
│
├─── Fast-follow (post-go-live, doesn't block) ──────────────────────────►
│
├─ M.2c       Live Tag Watch + Runtime Tap     (commissioning superpower)
├─ M.2b.7     Source duplication + bulk-import (Option B; for the next customer)
├─ M.2k       Fleet management                  (4-gateway oversight)
```

**Schedule envelope:** Bug 2 fix + Brother HTTP migration + bulk-provision tooling + 7-day soak = roughly **4–5 weeks of focused work** before this customer can go live with the new architecture (revised from the original 3–4 week estimate after Bug 2 surfaced in M.2b.6.2 smoke). If the timeline is tighter than that, the fallback is to deploy the **legacy `src/ElpisEdgeConnect/` codebase** at this customer (Brother HTTP works there today, and the legacy code does not exhibit Bug 2 because it predates the worker-task architecture). Decide explicitly — don't drift into the legacy path by default.

---

## 7. Customer questions — LOCKED (2026-05-20)

Locked answers from the customer conversation on 2026-05-20. These now drive the soak profile, topology, support contract, and the new §8 backup/restore strategy.

| # | Question | Locked answer | Implications |
|---|---|---|---|
| 1 | Polling cadence per machine | **3 seconds** (per-tag scan), must be operator-configurable | ~22 points/sec/machine ingest (65 tags ÷ 3s). 100 machines × 22 = ~2,200 pts/sec total — well under the 19M/sec Phase 1 ceiling. "Configurable" is already supported per-tag in Modbus and per-source in FOCAS2 — no new platform work required. |
| 2 | Total tag count per machine | **~65 tags baseline, ~75 with tool-related tags** | Templates split into base + optional-tools variants. Per gateway with 25 machines: ~540 pts/sec. MQTT bandwidth ≈ 80–95 KB/sec per gateway (PerTag mode, ~150B payload). Modest. |
| 3 | MQTT broker | **Our scope — we install** | Lock the choice now: **Mosquitto 2.x** — mature, low-memory, well-suited at this scale, exactly the broker the project already tests against (41 MQTT sink tests run against real Mosquitto). One broker per site, no cluster. |
| 4 | Network segmentation | **Flat** | Topology decisions driven purely by blast-radius and FOCAS2 handle-pool concerns, not network segments. |
| 5 | Fanuc / Brother split | **80 Fanuc / 20 Brother** (may shift) | Gateway grouping: **3 Fanuc-only gateways (27/27/26 machines) + 1 Brother gateway (20 machines)** is the recommended split — each protocol has its own ops surface, blast radius is bounded, and adding Brother machines later just adds capacity to the Brother gateway without disturbing Fanuc ops. M.P2.4 Brother HTTP migration remains a hard blocker. |
| 6 | Studio operator | **Maintenance staff** (not developers, not IT) | Materially elevates the importance of M.2c Live Tag Watch + a printed runbook. Maintenance staff need answers in the UI, not in logs. Re-confirms the "operational product, not developer tool" framing from platform principles §P6. |
| 7 | Backup / restore expectations | **No plan yet — we suggest** | See new **§8 Backup / restore strategy** below. |
| 8 | EREMOS V2 deployment status | **Already deployed** at the customer | Reduces install-time risk significantly. EREMOS V2 contract validation pass (in-house, against a real instance) is feasible. Also unlocks: pre-install end-to-end smoke (1 gateway → real Mosquitto → real EREMOS V2 ingest) before shipping to the customer site. |
| 9 | Maintenance window | **Monthly** | Hot-reload must work for config changes outside the window (M.P2.2 provides this, post-Bug 2). Restarts are infrequent enough that the **per-restart drain test from Bug 2's definition of done becomes load-bearing** — gateway must survive 30 days of operation, then restart cleanly with no buffer regression. Soak profile §5 updated to include a mid-soak forced restart drill. |

### Throughput summary (now that the answers are locked)

Across the entire customer install:

- **Total source throughput**: 100 machines × ~22 pts/sec/machine = **~2,200 pts/sec aggregate ingest**
- **Per-gateway throughput** (4 gateways at ~25 machines each): **~550 pts/sec**
- **MQTT publish rate per gateway**: PerTag mode → 550 messages/sec at ~150B each = **~83 KB/sec per gateway**
- **EREMOS V2 ingest at the broker**: 4 gateways × 550 = **~2,200 messages/sec at the broker** (well within Mosquitto 2.x's capability — Mosquitto handles 10K+ msgs/sec at this payload size on modest hardware)
- **Phase 1 measured ceiling**: 19.15M pts/sec (SqliteBuffer) — we're using <0.012% of validated capacity

The bottleneck at this customer is **not throughput** — it's **operational robustness** (Bug 2 fix, monthly-restart resilience, maintenance-staff UX). Capacity is a non-issue.

---

## 8. Backup / restore strategy (recommended)

Customer has no existing plan (Q7); this section is the recommendation. **Per-gateway, no clustering, simple-by-design.** Four gateways = four independent backup tasks.

### 8.1 What to back up

| Artifact | Path (post Bug 1 fix) | Frequency | Why |
|---|---|---|---|
| **Gateway identity** | `<dataRoot>/identity/` | Once at install (immutable thereafter) | The UUID is in every MQTT topic. If lost, EREMOS V2 sees the gateway as a brand-new device — topic continuity breaks, history visualizations split, dashboards re-key. **Non-negotiable to preserve.** |
| **Active configuration** | `<dataRoot>/config/current.json` | After every Apply (or nightly) | The deployed state — sources, sinks, routes, tag definitions |
| **Configuration history** | `<dataRoot>/config/history/*.json` | After every Apply (or nightly) | Enables rollback if a bad config is applied |
| **Audit chain** | `<dataRoot>/config/history/audit.log` | After every Apply (or nightly) | SHA-256 chain integrity for compliance + change forensics |
| **Drafts** | `<dataRoot>/config/drafts/` | Nightly (lower priority) | Pending edits; can be recreated from history if lost |
| **License** | `<dataRoot>/license.json` | Once at install + on re-issue | Required for runtime feature gates. **Also keep a copy in the customer's password manager** — disaster recovery from media failure |

### 8.2 What NOT to back up

| Artifact | Why |
|---|---|
| Store-and-forward buffers (`<dataRoot>/buffer/*.db`) | Volatile, often locked, contents transient (within `MaxAgeDays`). Fresh-restart recovery is acceptable — the broker continues to receive new data; backlog loss is bounded by the duration of whatever caused the restore. |
| Logs (`<dataRoot>/logs/` if present) | Too volatile, low recovery value. Use Windows Event Log forwarding instead if forensics matter. |
| Process state, PID files, temporary files | Reconstructed on start |

### 8.3 Backup destination

Three tiers, customer-budget-appropriate:

1. **Primary**: customer's existing NAS / network share (assumed available — most factory IT environments have one)
2. **Secondary**: a separate physical disk on each gateway (RAID-1 or simple mirrored backup partition) — protects against local disk failure between nightly NAS syncs
3. **Tertiary (optional)**: monthly copy to off-site storage if customer's IT policy requires it

### 8.4 Backup cadence

- **Nightly automated** — Windows Scheduled Task, runs at 02:00 local
- **On-demand pre-maintenance** — operator triggers a snapshot as part of the monthly maintenance runbook (before any config changes)
- **Retention**: 30 days on NAS, 7 days on local secondary disk

### 8.5 Backup automation (per gateway)

PowerShell scheduled task — drop this in C:\EdgeConnect\backup\:

```powershell
# nightly-backup.ps1
$ErrorActionPreference = 'Stop'
$src = "C:\ProgramData\EdgeConnect"
$dst = "\\customer-nas\backups\edgeconnect\$(hostname)\$(Get-Date -Format yyyy-MM-dd)"

# Mirror config + identity + license; exclude volatile dirs
robocopy $src $dst /MIR /XD buffer logs /R:3 /W:5 /LOG+:C:\EdgeConnect\backup\backup.log

# Retain 30 days on NAS, 7 days locally if mirrored
Get-ChildItem "\\customer-nas\backups\edgeconnect\$(hostname)" -Directory |
  Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
  Remove-Item -Recurse -Force

# Optional: append summary to a central log for monitoring
"$(Get-Date -Format 'o') | $(hostname) | nightly-backup | OK" |
  Add-Content "\\customer-nas\backups\edgeconnect\_status.log"
```

Schedule via:

```powershell
Register-ScheduledTask `
  -TaskName "EdgeConnect Nightly Backup" `
  -Trigger (New-ScheduledTaskTrigger -Daily -At 2am) `
  -Action (New-ScheduledTaskAction -Execute "powershell.exe" `
            -Argument "-NoProfile -ExecutionPolicy Bypass -File C:\EdgeConnect\backup\nightly-backup.ps1") `
  -User "SYSTEM" -RunLevel Highest
```

### 8.6 Restore procedure — operator runbook

A 1-page laminated runbook handed to maintenance staff. Steps:

1. **Stop the gateway service**
   ```powershell
   Stop-Service "Elpis EdgeConnect"
   ```
2. **Restore identity** — this is the critical step. The gateway must come back with the same UUID:
   ```powershell
   robocopy "\\customer-nas\backups\edgeconnect\<hostname>\<latest-date>\identity" "C:\ProgramData\EdgeConnect\identity" /MIR
   ```
3. **Restore configuration**:
   ```powershell
   robocopy "\\customer-nas\backups\edgeconnect\<hostname>\<latest-date>\config" "C:\ProgramData\EdgeConnect\config" /MIR
   ```
4. **Restore license**:
   ```powershell
   Copy-Item "\\customer-nas\backups\edgeconnect\<hostname>\<latest-date>\license.json" "C:\ProgramData\EdgeConnect\license.json" -Force
   ```
5. **Start the gateway service**:
   ```powershell
   Start-Service "Elpis EdgeConnect"
   ```
6. **Verify in Studio** (`http://127.0.0.1:5080`):
   - Gateway identity matches the backup's UUID (visible on the dashboard)
   - Configuration is intact (sources / sinks / routes all present)
   - Audit chain validates (green "Audit chain verified" banner on the Diagnostics page)
7. **Verify data flow**:
   ```powershell
   mosquitto_sub -h <broker-host> -p 1883 -v -t 'eremos/<gateway-uuid>/#' -W 30
   ```
   Confirm messages are flowing on the same topics as before the swap.
8. **Notify EREMOS V2 team** — no action required on their side; topics remained continuous.

### 8.7 Hardware swap (different machine, same gateway identity)

Same procedure as §8.6. The MQTT identity is bound to `<dataRoot>` contents, not to the physical machine. Pre-requisites for hardware swap:

- New hardware is provisioned with same OS, .NET 8 SDK, and EdgeConnect binaries
- Same data-root path (`C:\ProgramData\EdgeConnect` or whatever the org standard is)
- Network access to the same MQTT broker
- Note: hostname / IP can differ — gateway identity is independent

### 8.8 Tradeoffs called out

| Choice | Tradeoff | Why this customer |
|---|---|---|
| Nightly cadence, not real-time | RPO ~24 hours for config; effectively 0 for telemetry (broker is the destination) | Maintenance staff make config changes monthly at most — nightly is plenty |
| Not backing up buffers | RTO doesn't include backlog replay | Bounded data loss is acceptable; broker continues receiving new data the moment gateway restarts |
| 4 independent gateways, no HA | If a gateway is down, its 25 machines are silent | Simpler ops for maintenance staff vs HA cluster complexity. Acceptable blast-radius. Restore is fast (< 15 min) per gateway. |
| Single broker, no cluster | If the broker is down, all 4 gateways buffer until it returns | Store-and-forward catches up automatically once Bug 2 is fixed. Cluster adds complexity not justified at this scale. |

### 8.9 Customer training

- 1-page laminated runbook handed to maintenance staff at install
- **Hands-on dry-run during the 48-hour acceptance test window** — kill one gateway, restore from backup, verify EREMOS V2 sees continuous data. Document timing as part of the acceptance report.
- Annual refresher recommended; pair with the customer's monthly maintenance window if they're amenable.

### 8.10 Promotion path

This section is currently embedded in the deployment-readiness doc. **At install time, promote to `docs/runbooks/100cnc-backup-restore.md`** as a standalone runbook (and laminate for the customer). Mention in the M.P2.4 handoff to budget time for the runbook promotion.

---

## 9. Risks (deployment-specific, not platform-wide)

| Risk | Likelihood | Severity | Mitigation |
|---|---|---|---|
| FOCAS2 handle exhaustion at 25–50/gateway | Medium | High | Topology recommendation (§4) — keep per-gateway count modest; in-house soak validates the chosen split |
| Brother HTTP migration introduces regressions vs legacy behavior | Medium | High | Side-by-side comparison test: same input, legacy vs new arch, identical canonical output. Land as part of M.P2.4. |
| Hot-reload performance with 100-source `current.json` (reload time > acceptable window) | Medium | Medium | Measure during in-house soak; if > 30s, mark as known limitation and require restart for config changes. M.P2.2 hot-reload was tuned for typical configs, not 100+ sources. |
| Audit log growth at 100 sources × config events | Low | Low | Audit log is append-only; capacity is fine. Operator-side, expose log-rotation as a follow-up if needed. |
| MQTT topic cardinality at the broker | Low | Medium | EREMOS V2 subscription is `eremos/+/cnc/+/+` — broker handles wildcard subscriptions fine at this cardinality, but verify with the customer's broker product in §7-Q3. |
| Customer's operator can't recover from a gateway swap without us on-site | Medium | High | Document a 1-page "swap a gateway" runbook with the bulk-provision flow. Test it during the 48-hour acceptance window. |
| Bulk-provision generator script becomes load-bearing without standardization | Medium | Medium | Commit it to `tools/bulk-provision/` in the repo from day one, with tests, before the customer install. Don't ship a one-off bespoke version. |
| EREMOS V2 contract drift between our PerTag emission and their consumer | Low | High | Already covered by the shared-knowledge MQTT contract reference; re-validate before install with a small e2e test (1 gateway → 1 mock Fanuc → real Mosquitto → real EREMOS V2 ingest). |
| **Bug 3 — `MqttSinkAdapter` slow recovery after broker restart** ([issue #24](https://github.com/elpisitsolutions/EdgeConnect/issues/24)) | Medium | **Medium (perception)** | Store-and-forward absorbs the data — no loss. But ~15s recovery vs the 5s v2-plan threshold could erode customer confidence during commissioning if a broker restart happens in front of them. Mitigation: understand-or-resolve before 7-day soak gate. Investigation plan: [`2026-05-22-bug3-mqtt-reconnect-investigation.md`](2026-05-22-bug3-mqtt-reconnect-investigation.md). |

---

## 10. What's NOT on this customer's critical path

Worth stating explicitly so we don't accidentally block on them:

- **M.2c Live Tag Watch** — useful but the existing diagnostics page + MQTT-side `mosquitto_sub` are workable substitutes during commissioning
- **M.2d Edit-via-Wizard** — bulk-provision via Option A bypasses the wizard entirely for the 100 sources; the wizard is for one-off additions
- **M.2e Shared List Infrastructure** — pure UX polish at this scale
- **M.2g First-run onboarding** — out, bulk-provision handles initial config
- **Milestone K OPC UA security hardening** — customer is MQTT-only
- **Dark mode, license UI, Kafka sink, cloud sinks** — irrelevant to this customer
- **OPC UA Server sink** — irrelevant to this customer

This list exists so a future planning conversation can quickly say "X is on the v2 roadmap but not on the 100-CNC critical path."

---

## 11. Acceptance signal for this document

This is a working document. Acceptance happens incrementally:

- [x] §7 open questions locked with the customer — **COMPLETE 2026-05-20** (this PR — answers locked in §7, backup/restore strategy captured in §8)
- [x] M.P2.4 Brother HTTP migration kickoff + plan trail — **COMPLETE 2026-05-21** (PR [#19](https://github.com/elpisitsolutions/EdgeConnect/pull/19); handoff at [`2026-05-21-mp24-handoff.md`](2026-05-21-mp24-handoff.md))
- [ ] Bulk-provision generator + templates committed to `tools/bulk-provision/`
- [ ] 7-day in-house soak passes acceptance criteria (§5)
- [ ] 48-hour customer-site acceptance test plan agreed with customer engineering
- [ ] EREMOS V2 contract validation pass against the new-arch MQTT emission — **5 of 6 measurable gates landed 2026-05-22** ([PR #23](https://github.com/elpisitsolutions/EdgeConnect/pull/23) mock-fallback path; Gate 5 deferred pending Bug 3 — see next row)
- [ ] **Bug 3 (P2) understood OR resolved before 7-day soak gate** — `MqttSinkAdapter` recovers within the v2-plan threshold OR threshold relaxed with documented rationale OR scenario confirmed non-production-realistic. See [issue #24](https://github.com/elpisitsolutions/EdgeConnect/issues/24) + [investigation plan](2026-05-22-bug3-mqtt-reconnect-investigation.md). **This row exists deliberately so the bug doesn't drift out of mind even at P2 — commissioning-window customer perception of "gateway recovered slowly" matters.**

When the remaining five close, this document graduates from DRAFT v1 to v2 and becomes the install playbook.

---

**End of deployment readiness draft v1. Next moves: bulk-provision tooling (Chip 3), 7-day in-house soak, EREMOS V2 contract revalidation.**
