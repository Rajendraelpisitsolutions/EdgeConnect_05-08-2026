# Phase 2 Multi-Protocol Performance Baseline

**Status:** SCAFFOLD — populated at the end-of-week-7 gate of the multi-protocol pilot expansion milestone.
**Reference:** `docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md` §5.5 (Benchmark validity rules), §5.6 (Risk register).
**Companion docs:**
- `docs/benchmarks/multi-protocol-workload-profiles.md` — the workload profiles + validity rules these numbers were measured against.
- `docs/benchmarks/phase1-baseline.md` — the Phase 1 numbers these new numbers build on top of (per-component buffer / serializer / compression baselines).

This document captures the **locked** sustained-operation numbers for the multi-protocol expansion (OPC UA Client + EtherNet/IP source adapters + the cross-cutting platform work). It exists so future milestones have a known-good regression floor and so customer-facing throughput claims can be cited back to a specific, reproducible measurement.

**Every number captured here was measured under the §1 validity rules of `multi-protocol-workload-profiles.md`.** Numbers that did not pass all 7 sustainability gates are filed in §6 ("Informational — did not meet gate") rather than the headline tables.

---

## §0 How to read this document

- The §1–§5 tables get filled in at week-7 close (planned: end of pilot-expansion milestone).
- The §6 section captures any runs that hit informational thresholds but failed one or more sustainability gates — kept honest, not hidden.
- Sustainability gates are evaluated per `multi-protocol-workload-profiles.md` §2.
- Every entry includes the **exact** profile name + parameter set so a future engineer can reproduce.

---

## §1 OPC UA Client — primary throughput (Profile A — Industrial-mix-30K)

| Metric | Target | Measured | Pass / Fail |
|---|---|---|---|
| Throughput | ≥30,000 CDPs/sec sustained | _TBD_ | _TBD_ |
| p99 publish-to-sink latency | <100ms | _TBD_ | _TBD_ |
| Throughput drift (first vs last 5-min) | <5% | _TBD_ | _TBD_ |
| p99 latency drift | <20% | _TBD_ | _TBD_ |
| OPC UA notification queue depth | Bounded <500 | _TBD_ | _TBD_ |
| Buffer depth | Bounded, non-growing | _TBD_ | _TBD_ |
| Gen-2 GC frequency | <1 per 60s | _TBD_ | _TBD_ |
| All 7 sustainability gates | All green | _TBD_ | _TBD_ |

**Notes / observations:** _TBD at week-7._

---

## §2 OPC UA Client — stretch throughput (Profile B — Industrial-mix-50K)

**Activates only if** week-1 stack-ceiling measurement confirms ≥50K achievable on pilot-class hardware.

| Metric | Target | Measured | Pass / Fail |
|---|---|---|---|
| Throughput | TBD post-week-1 measurement (≤50K) | _TBD_ | _TBD_ |
| p99 latency / drift / queue / buffer / GC / gates | Same shape as §1 | _TBD_ | _TBD_ |

**Week-1 stack-ceiling measurement result:** _TBD_

If the measurement returns N below 50K, lock N here and rename the profile to "Industrial-mix-{N}".

---

## §3 EtherNet/IP — per-controller (Profile C — EthernetIp-ControlLogix-L7x)

| Metric | Target | Measured | Pass / Fail |
|---|---|---|---|
| Throughput per source | ≥3,000 tags/sec sustained | _TBD_ | _TBD_ |
| p99 read-cycle latency | <150ms | _TBD_ | _TBD_ |
| All 7 sustainability gates | All green | _TBD_ | _TBD_ |

**Calibration note:** v2.1 §1.2 documented per-controller estimates (MicroLogix ~500, CompactLogix L3x/L4x 1.5–3K, ControlLogix L7x/L8x 3–6K) from community reports. **These are measured numbers — they supersede the estimates.**

**Per-controller table (measured):**

| CPU family | Locked sustained throughput | Notes |
|---|---|---|
| MicroLogix 1400 | _TBD_ | _Pilot-hardware calibration_ |
| CompactLogix L3x/L4x | _TBD_ | |
| ControlLogix L7x | _TBD_ | |
| ControlLogix L8x | _TBD_ | Requires `path=1,0` per v2.1 §3.1 |

**Aggregate framing (LOCKED at week-7):**
- Per-controller numbers above are the engineering numbers.
- Aggregate = sum across configured controllers. Honest commercial framing: "N ControlLogix L7x controllers ≈ N × locked-L7x-throughput aggregate."
- Do NOT publish a single misleading aggregate number without the per-controller context.

---

## §4 Pipeline — zero-transform bypass (Profile D)

| Metric | Target | Measured | Pass / Fail |
|---|---|---|---|
| End-to-end throughput | ≥30,000 CDPs/sec sustained | _TBD_ | _TBD_ |
| Source-to-sink p99 latency | <50ms | _TBD_ | _TBD_ |
| All 7 sustainability gates | All green | _TBD_ | _TBD_ |

**Validates:** v2.1 §1.3.2 — pipeline bypass invariants hold under sustained 30K/sec load (no per-CDP diagnostics skips, no ownership ambiguity, no determinism drift).

---

## §5 Buffer — sustained enqueue (Profile E)

| Metric | Target | Measured | Pass / Fail |
|---|---|---|---|
| Enqueue throughput | ≥30,000 CDPs/sec sustained | _TBD_ | _TBD_ |
| Per-batch commit p99 | <50ms | _TBD_ | _TBD_ |
| WAL file size growth | Bounded by checkpoint cadence | _TBD_ | _TBD_ |
| All 7 sustainability gates | All green | _TBD_ | _TBD_ |

**Validates:** v2.1 §1.3.3 reframe — existing batched `EnqueueAsync` design holds at 30K/sec without a new commit-cadence dimension.

---

## §6 Informational runs — did not meet gate

This section captures runs that hit interesting numbers but failed one or more sustainability gates. **They are NOT customer-facing numbers**; they exist so future engineers can see what was tried and why it doesn't ship as a locked claim.

_TBD at week-7. Expected entries include any 50K-stretch attempts that hit Gen-2 pressure or notification-queue drift._

---

## §7 Hardware + runtime baseline

The exact hardware + runtime spec these numbers were measured against. Required for any future regression run to be comparable. Spec class locked at v2.1 sign-off (2026-05-28 Q10); concrete CPU model + RAM filled in at procurement.

| Field | Locked class | Concrete value (filled at procurement) |
|---|---|---|
| Host model | **Dedicated benchmark host** (NOT shared CI runner) | _TBD_ |
| OS | **Linux** | _TBD distro + version_ |
| CPU | _TBD — closest match to pilot gateway hardware_ | _TBD model + cores + base clock_ |
| RAM | _TBD_ | _TBD_ |
| Disk type | **Local NVMe SSD** (no network storage; SQLite fsync depends on it) | _TBD model_ |
| Power profile | **`tuned-adm profile throughput-performance`** | n/a (locked) |
| CPU affinity | **Pinned benchmark worker cores; OS isolated to non-benchmark cores** | _TBD core IDs_ |
| .NET runtime | **Pinned .NET 8 SDK version** | _TBD — `dotnet --version`_ |
| BenchmarkDotNet | _TBD_ | _TBD version_ |
| GC mode | `ServerGarbageCollection=true` + `ConcurrentGarbageCollection=true` (per v2.1 §2.5) | n/a (locked) |

**Locked governance rule (v2.1 §6 Q10):** Only **nightly dedicated-host numbers** update the baseline tables in §1–§5. PR-smoke shared-infra numbers are informational only and never overwrite locked baselines.

**MQTT broker config:**

| Field | Locked | Concrete |
|---|---|---|
| Broker | **Local Mosquitto on `localhost:1883`** | _TBD — version_ |
| Persistence | _TBD_ | _TBD_ |
| QoS for benchmarks | _TBD_ | _TBD_ |

**OPC UA Sample Server config:**

| Field | Locked | Concrete |
|---|---|---|
| Server | _TBD — OPC Foundation reference server_ | _TBD version_ |
| Endpoint | _TBD_ | _TBD_ |
| Security mode | **SignAndEncrypt + Basic256Sha256** (per v2.1 §6 Q7 engineering baseline) | n/a (locked) |
| Auth | **Anonymous** (per v2.1 §6 Q7 engineering baseline) | n/a (locked) |

---

## §8 Run commands

```
# PR-gate 30s smoke (Profile A reduced to 5K items × 30s)
dotnet run --project tests/ElpisEdgeConnect.Benchmarks --configuration Release -- --filter '*OpcUaClient_Smoke*'

# Nightly full gate (all profiles, 30-min sustained)
dotnet run --project tests/ElpisEdgeConnect.Benchmarks --configuration Release -- --filter '*MultiProtocol*' --job nightly

# Weekly 24h soak with Rule 5 noise injection
dotnet run --project tests/ElpisEdgeConnect.Benchmarks --configuration Release -- --filter '*Soak*' --job soak
```

(Commands captured as locked at week-7; week-1 work fills in the exact `--job` parameters.)

---

## §9 Change log

| Date | Author | Change |
|---|---|---|
| 2026-05-28 | Multi-protocol expansion plan v2.1 | Scaffold created; awaits week-7 measurements |
| _TBD_ | _Week-7 gate close_ | Initial numbers locked |
