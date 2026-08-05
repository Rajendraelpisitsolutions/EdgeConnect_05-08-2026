# Multi-Protocol Workload Profiles + Benchmark Validity Rules

**Status:** v1 — locked at plan v2.1 (2026-05-28). Reference profile parameters get tuned in week-1 of implementation based on pilot-customer site survey.
**Reference:** `docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md` §5.5 (Benchmark validity rules — LOCKED).
**Companion doc:** `docs/benchmarks/phase2-multi-protocol-baseline.md` (captures the actual measured numbers at end-of-week-7 gate).

This document is the single source of truth for **what a "30K tags/sec sustained" claim actually means** in EdgeConnect. Every benchmark in the multi-protocol expansion runs against one of the profiles below; every regression gate is interpreted against the validity rules in §1.

> **Why this matters:** without explicit validity rules, benchmark regressions degrade into synthetic-throughput games. A 30K/sec "win" on identical-value payloads with no MQTT serialization tells nothing about pilot-time behaviour. ChatGPT review on 2026-05-28 surfaced this gap; locking it here before implementation kicks off.

---

## §1 Benchmark validity rules (LOCKED)

These six rules apply to EVERY multi-protocol benchmark in the perf gate. A run that violates any of them is informational only; it does NOT count toward locked numbers.

### Rule 1 — Realistic value-change distribution

Workloads MUST model independent per-tag change rates. NOT all 30K tags changing simultaneously every cycle.

**Reference profile — "industrial COV":**
- 1% of tags change every publish cycle (high-velocity sensors)
- 9% change every 5 cycles (typical process signals)
- 30% change every 30 cycles (slow process variables)
- 60% quasi-static — change only on operator action or process event

**Why:** Real OPC UA Server publishing patterns are dominated by long-tail COV. Benchmarks that fire every tag every cycle exercise the encoder path far harder than reality, and exercise the change-detection path not at all.

### Rule 2 — Heterogeneous payload mix

No "all-DINT" or "all-BOOL" runs allowed as the primary regression gate.

**Reference profile — "industrial mix":**

| Type | Share | Notes |
|---|---|---|
| BOOL | 40% | Coil / discrete I/O patterns |
| DINT / INT | 30% | Process registers, counts, statuses |
| REAL / LREAL | 20% | Analog values, calculated process variables |
| STRING | 10% | Tag names, recipe codes, status messages. 95% at 8–32 chars; **5% at 256+ chars** (ControlLogix-style string arrays — exercises the larger CIP frame size) |

**Why:** STRING handling stresses the encoder hot path the most. Excluding STRINGs or running them at uniform short lengths hides the 80th-percentile cost.

### Rule 3 — End-to-end serialization enabled

No "/dev/null sinks" in the primary regression gate.

**MQTT sink benchmarks** MUST run through:
- Batch mode JSON serialization (System.Text.Json with the production options)
- PerTag mode raw scalar serialization
- Real broker round-trip (Mosquitto on `localhost:1883` is acceptable; broker tuning per `docs/qa/...`)

**OPC UA Server sink benchmarks** MUST run through:
- Full `EdgeConnectNodeManager.UpsertTagValue` path
- BrowsePath placeholder resolution
- Notification dispatch to a connected reference client (UA Sample Client subscribing to ≥100 monitored items)

**Why:** Sinks doing real work is part of the perf reality we ship. Mocked sinks make the upstream pipeline look ~3× faster than it actually is at the operator's MQTT broker.

### Rule 4 — Realistic sink cadence

No zero-latency sink mocks in the primary regression gate.

- MQTT broker round-trip: **≥ 1ms** acknowledged
- OPC UA Server publish: **≥ 50ms** publishing interval to subscribed reference client
- HTTP sink (future): **≥ 5ms** per request

**Why:** Backpressure behaviour is invisible without realistic ack latency. A 0ms sink hides the spill-over path that real deployments exercise constantly.

### Rule 5 — Soak-test reconnect noise

Performance **soak runs** (24-hour sustained throughput tests) MUST inject:
- **Simulated network blips:** 5-second drops every 10 minutes (random offset)
- **Subscription churn:** add/remove 1% of monitored items every 30 minutes
- **Configuration changes:** `ReconfigureAsync` call every 2 hours that adds and removes 100 tags net-zero

A system that hits 30K/sec under sterile lab conditions but degrades under real-world noise is NOT shipping at 30K/sec.

**Why:** Real edge gateways spend their operational life dealing with this kind of noise. Soak runs without it falsely paint a healthier picture than production sees.

### Rule 6 — Realistic OPC UA value semantics

Notifications MUST carry valid:
- `ServerTimestamp` (UA-format DateTime)
- `SourceTimestamp` (UA-format DateTime)
- `StatusCode` (proper UA StatusCode struct, not just `Good`)

Zeroed timestamps and stub StatusCodes bypass encoder/decoder overhead that is part of the perf reality we measure.

---

## §2 Sustained-vs-peak definition (LOCKED)

> The benchmark ceiling is defined at **sustained stable operation for ≥30 minutes**, not peak burst.

A workload that hits 50K/sec for 30 seconds then dies under GC pressure or notification-queue backlog accumulation is NOT 50K/sec. The number we lock is the **highest throughput sustainable indefinitely with stable latency, stable reconnect behaviour, bounded queue growth, and no degradation drift.**

### Sustainability gates — ALL must hold simultaneously for the full duration

| Gate | Threshold | How measured |
|---|---|---|
| Throughput drift | <5% across run window | First 5-min mean vs last 5-min mean |
| p99 latency drift | <20% across run window | First 5-min p99 vs last 5-min p99 |
| OPC UA notification queue depth | Bounded; <500 deep at any sample point | Reflection-based wrapper around stack's `m_messageCache` (see plan v2.1 §2.6) |
| Buffer depth | Bounded; not monotonically growing across run | `BufferStats.CurrentDepth` sampled every 60s |
| Gen-2 GC frequency | <1 per 60 seconds | `GC.CollectionCount(2)` deltas |
| Reconnect recovery | Within `ReconnectPeriodExponentialBackoff` ceiling (15s) | Time from injected blip → first successful publish |
| `ReconfigureAsync` injection | Apply within 100ms; no data loss; no drift after | Compare in-flight CDPs pre/post reconfigure |

A run that fails ANY of these does NOT count toward the locked number. We report honestly:
- "30K/sec for 28 minutes then drifted to 26K/sec under Gen-2 pressure" — informational
- "30K/sec sustained for 30 minutes with all 7 gates green" — locked

NOT "30K/sec sustained" with footnote "asterisk: latency drifted 35% in the second half but we don't count that."

---

## §3 Workload profiles

Each profile is a named, parameterized harness. Benchmarks reference profiles by name; profile parameters are versioned per this document.

### Profile A — "Industrial-mix-30K"

**Use:** OPC UA Client primary throughput gate.

| Parameter | Value |
|---|---|
| Monitored items | 30,000 |
| Subscriptions | 30 (1,000 items each — stack subscription limit) |
| Publishing interval | 50 ms |
| Sampling interval | 50 ms |
| Value-change distribution | Per §1 Rule 1 — industrial COV |
| Payload type mix | Per §1 Rule 2 — industrial mix |
| Security mode | SignAndEncrypt + Basic256Sha256 (per v2.1 §6 Q7 baseline) |
| Sink fan-out | 1 MQTT (Batch mode) + 1 OPC UA Server |
| **Warmup** | **15K monitored items for 5 min before stepping to 30K** (v2.1 §6 Q9 lock — avoids measuring cold-JIT/transient-allocator noise as steady-state) |
| Duration | 30 min sustained gate AFTER warmup; 24h soak with §1 Rule 5 noise |

**Locked targets:**
- Throughput: ≥30,000 CDPs/sec sustained
- p99 publish-to-sink latency: <100ms
- All 7 sustainability gates green

### Profile B — "Industrial-mix-50K" (STRETCH)

**Use:** OPC UA Client stretch-target gate. Activates ONLY if the week-1 stack-ceiling measurement confirms 50K is achievable on pilot-class hardware.

| Parameter | Value |
|---|---|
| Monitored items | 50,000 |
| Subscriptions | 50 |
| Publishing interval | 50 ms |
| Otherwise | Same as Profile A |

**Locked targets:** TBD pending week-1 measurement. If measurement returns N below 50K, lock N as the stretch target and rename this profile.

### Profile B′ — "Industrial-mix-75K" (EXPLORATORY CEILING)

**Use:** Week-1 stack-ceiling measurement only. Documents where the OPC Foundation .NET stack actually falls over on pilot-class hardware. Numbers here are **informational only** — NEVER publish as a customer-facing claim.

| Parameter | Value |
|---|---|
| Monitored items | 75,000 |
| Subscriptions | 75 |
| Otherwise | Same as Profile A |

**Purpose:** identifies the failure mode (notification queue growth? Gen-2 pressure? subscription manager lock contention?) so we have data for any future stack-tuning conversation. Numbers from this profile go to `phase2-multi-protocol-baseline.md` §6 "Informational — did not meet gate."

### Locked week-1 measurement sequence

The stack-ceiling measurement runs profiles in this exact order (v2.1 §6 Q9 lock):

1. **15K warmup** for 5 minutes (Profile A reduced)
2. **30K sustained gate** — Profile A, 30 min, all 7 sustainability gates must hold
3. **50K stretch attempt** — Profile B, 30 min, informational unless all gates green
4. **75K exploratory ceiling** — Profile B′, 30 min, informational only

Each phase produces an entry in `phase2-multi-protocol-baseline.md`. Phases that pass all 7 gates become locked baseline numbers; phases that fail any gate land in the §6 informational section.

### Profile C — "EthernetIp-ControlLogix-L7x"

**Use:** EtherNet/IP per-controller gate.

| Parameter | Value |
|---|---|
| Tags | 3,000 (mix of atomic + UDT-expanded members) |
| Polling interval | 100 ms |
| UDT depth | Up to 3 levels (typical industrial pattern) |
| Connection size | Negotiate to ~500B (default Forward-Open) |
| Sink fan-out | 1 MQTT (PerTag mode) |
| Duration | 30 min sustained gate; 24h soak with §1 Rule 5 noise |

**Locked targets:**
- Throughput: ≥3,000 tags/sec sustained per source (= per controller)
- p99 read-cycle latency: <150ms
- All 7 sustainability gates green

### Profile D — "Pipeline-30K-NoTransforms"

**Use:** Pipeline throughput gate (validates §1.3.2 bypass behaviour at scale).

| Parameter | Value |
|---|---|
| CDP intake rate | 30,000/sec (synthetic source) |
| Pipeline transforms | NONE (zero-step bypass path) |
| Sink | OPC UA Server (full UpsertTagValue) |
| Duration | 30 min sustained gate |

**Locked targets:**
- End-to-end throughput: ≥30,000 CDPs/sec sustained
- Source-to-sink latency p99: <50ms

### Profile E — "Buffer-30K-Sustained"

**Use:** SQLite buffer throughput gate at sustained 30K/sec.

| Parameter | Value |
|---|---|
| Enqueue rate | 30,000 CDPs/sec |
| Batch size | 256 CDPs (matches `RouteWorker.IntakeBatchSize`) |
| Sinks registered | 2 (per Profile A fan-out) |
| Duration | 30 min sustained gate |

**Locked targets:**
- Enqueue throughput: ≥30,000 CDPs/sec sustained
- Per-batch commit latency p99: <50ms
- WAL file size growth bounded (checkpoint cadence holds)

---

## §4 PR-gate vs nightly cadence

- **PR gate (30 s smoke):** runs Profile A reduced to 5K monitored items for 30 seconds. Catches >10% regression. Cheap enough for every PR.
- **Nightly (full gate):** runs all profiles for 30 min sustained. Catches drift; locks the headline numbers.
- **Weekly soak (24h):** runs Profile A with §1 Rule 5 noise injection. Catches degradation drift.

CI matrix runs nightly on a dedicated benchmark host (specs locked in `docs/benchmarks/phase2-multi-protocol-baseline.md` at end of week 7) to keep numbers comparable across runs.

---

## §5 Future profiles (post-pilot — NOT in scope for v2.1 lock)

Captured here so they don't get lost. Both are explicitly **non-blocking** for the multi-protocol pilot expansion — they belong to the **post-pilot benchmark roadmap**. Recorded at v2.1 sign-off (ChatGPT review pass on 2026-05-28).

### Profile F — "Degraded-sink-isolation"

**Use:** validates the architecture's actual differentiator — per-sink cursor isolation under asymmetric sink pressure. EdgeConnect's biggest non-throughput moat is the wake-only dispatcher + per-sink cursors + bounded retry domains; we should eventually benchmark the thing the architecture is uniquely designed to do, not just raw throughput.

**Setup:**
- Profile A workload (Industrial-mix-30K) feeding **two sinks** in fan-out
- Sink A: healthy, normal MQTT broker round-trip
- Sink B: intentionally degraded — injected latency (500ms publish), occasional throws, periodic stalls (10s every 5 min)

**Locked targets (when activated):**
- Sink A throughput remains within 5% of unimpaired Profile A
- Sink B buffer depth grows but stays bounded (no unbounded growth into OOM)
- Sink A retry budget never touched by Sink B failures
- `FanoutDispatcher` wake behaviour stays edge-coalesced (no signal leak between sinks)
- Recovery: when Sink B unblocks, drain completes within its own retry-budget ceiling without degrading Sink A

**Why it matters:** raw throughput is a Kepware-class commodity claim. Per-sink isolation under degradation is what differentiates EdgeConnect's runtime architecture from the field.

### Profile G — "Multi-route mixed workload"

**Use:** real deployments rarely have one high-volume route. Typical industrial sites run many medium routes with mixed protocols, mixed cadences, mixed sinks. The route worker scheduler / SQLite buffer per-route file behaviour / global GC behaviour differ materially in that regime.

**Setup:**
- 20 routes, each handling 1,500 tags/sec sustained
- Mixed source protocols: 8 OPC UA Client, 8 EtherNet/IP, 4 Modbus
- Mixed sinks: each route fans out to MQTT + OPC UA Server
- Mixed publishing intervals: 50ms / 100ms / 250ms across the route set
- Some routes use transforms (Filter / Deadband / RateLimit); some don't (validates §1.3.2 bypass behaviour at scale)

**Locked targets (when activated):**
- Aggregate: ≥30,000 CDPs/sec across all 20 routes
- Per-route fairness: no route's throughput drifts below 90% of its expected 1.5K/sec
- Per-route p99 latency unchanged from single-route Profile A
- Gen-2 GC frequency from §2 holds (route count doesn't compound GC pressure)
- One degraded route does NOT affect siblings (Profile F invariants extended across routes)

**Why it matters:** customer-deployment shape is "many medium routes," not "one fat route." The scheduling and caching paths exercised here are different from the single-fat-route shape, and bugs in those paths are exactly the kind that don't show up until the customer's third site goes live.

### When to activate F and G

After pilot start. Profile F is the natural week-9 / week-10 follow-up because it stresses the architecture's actual moat. Profile G belongs to the post-Phase-2 fleet-management story when customers start running multi-source / multi-sink deployments. Both get their own short plan trail.

---

## §6 Open items

### Locked at v2.1 sign-off (2026-05-28)

- ✅ **Benchmark host spec** — dedicated Linux host, `tuned-adm profile throughput-performance`, pinned CPU affinity, local Mosquitto, local NVMe SSD, pinned .NET 8 SDK. Concrete CPU model + RAM TBD at procurement; capture in `phase2-multi-protocol-baseline.md` §7 before week-1 runs start.
- ✅ **OPC UA security mode** — engineering baseline locked at SignAndEncrypt + Basic256Sha256 + Anonymous (v2.1 §6 Q7). Real pilot endpoint security mode mandatory before customer-facing numbers publish.
- ✅ **Stack-ceiling measurement sequence** — 15K warmup → 30K → 50K → 75K (v2.1 §6 Q9).
- ✅ **Baseline governance** — only nightly dedicated-host numbers update locked baselines; PR-smoke shared-infra numbers are informational only (v2.1 §6 Q10).

### Still open at week-1 close

1. **Concrete benchmark host CPU model + RAM** — captured in `phase2-multi-protocol-baseline.md` §7 once procurement completes.
2. **Mosquitto broker tuning** — concrete `mosquitto.conf` settings used in benchmarks. Locked at first stable Profile A run.
3. **UA Sample Server vs real FactoryTalk endpoint calibration** — primary regression gate uses UA Sample Server (deterministic, reproducible). Calibration runs against real FactoryTalk at week 6 verify the lab numbers track real-world.
4. **STRING long-tail share** (§1 Rule 2 — 5% at 256+ chars) — confirm against pilot tag-list distribution; adjust if pilot data shows different shape.
5. **Subscription/monitored-item churn rate** (§1 Rule 5 — 1% per 30 min) — adjust based on customer's typical config-change cadence.
6. **EtherNet/IP per-controller calibration** — Profile C (ControlLogix L7x) gets its first measured number at week 6 against pilot hardware. Numbers for L1x + Micro800 carry pre-week-5 smoke risk (v2.1 §6 Q8) — schedule those measurements before week 4 starts.
