# Phase 3 — Execution Plan

**Elpis EdgeConnect** · target `v0.2.0` · 8-week scope

> Companion to `docs/ARCHITECTURE_BLUEPRINT.md` and `docs/PHASE1_EXECUTION_PLAN.md`.
> Phase 3 extends the Phase 1 Core and Phase 2 protocol migrations without
> modifying any LOCKED contract (see Blueprint Appendix A).

---

## 1. Objective

Extend EdgeConnect from a two-protocol pilot (FOCAS2 source + MQTT sink) into a
**multi-protocol, multi-sink edge platform** with license-gated adapter
activation and a read-only tag catalog. All extensions plug into the Phase 1
Core without modifying its locked contracts (`ISourceAdapter`, `ISinkAdapter`,
`CanonicalDataPoint`, `RoutingEngine`, `SqliteBuffer`).

---

## 2. Scope

| In scope | Out of scope (deferred) |
|---|---|
| Modbus TCP source adapter | Modbus RTU serial (Phase 5 if demanded) |
| HTTP sink adapter (push, JSON/NDJSON) | OPC UA Server (Phase 5, locked) |
| TCP sink adapter (length-prefixed + newline) | S7, EtherNet/IP, TwinCAT, PROFINET (Phase 5+) |
| License enforcement at adapter registration | Probe-based Modbus "auto-discovery" |
| Tag Catalog API (read-only, metadata only) | Web UI tag authoring (Phase 4) |
| Adapter-scoped diagnostics endpoints | Write-back / control writes (Phase 5) |
| CSV tag import for Modbus | EREMOS topic scheme change (requires contract review) |
| Pilot: 1 CNC + 1 PLC | New buffer / store-and-forward work (Phase 1 done) |

---

## 3. System Architecture — Where Phase 3 Additions Slot In

Existing Core and Phase 2 adapters are untouched. Phase 3 additions are
highlighted with `[P3]`.

```text
┌───────────────────────────────────────────────────────────────────────┐
│                         MANAGEMENT LAYER                              │
│                                                                       │
│  ┌─────────────────┐  ┌──────────────────┐  ┌──────────────────────┐  │
│  │ License Manager │  │ Tag Catalog API  │  │ Adapter Diagnostics  │  │
│  │  (Phase 1)      │  │  [P3] read-only  │  │  [P3] /api/adapters/ │  │
│  │                 │  │  metadata query  │  │  {id}/diagnostics    │  │
│  └────────┬────────┘  └─────────┬────────┘  └──────────┬───────────┘  │
└───────────┼─────────────────────┼──────────────────────┼──────────────┘
            │ gates registration  │ reads metadata       │ reads health
            ▼                     ▼                      ▼
┌───────────────────────────────────────────────────────────────────────┐
│                    CORE RUNTIME  (Phase 1 — LOCKED)                   │
│                                                                       │
│   ┌──────────────┐   ┌────────────┐   ┌─────────┐   ┌───────────┐     │
│   │ISourceAdapter│──▶│  Pipeline  │──▶│ Routing │──▶│SqliteBuffer│──┐ │
│   │   contract   │   │ (4 steps)  │   │ Engine  │   │ per-sink  │  │ │
│   └──────────────┘   └────────────┘   └─────────┘   │  cursors  │  │ │
│                                                     └───────────┘  │ │
└────────────────────────────────────────────────────────────────────┼──┘
                                                                     │
                     ┌───────────────────────────────────────────────┘
                     ▼
┌───────────────────────────────────────────────────────────────────────┐
│                   SOURCE ADAPTERS  (own assemblies)                   │
│                                                                       │
│  FOCAS2 (P2)   │   ModbusTcp [P3]   │   future: S7, UA-Client, ...    │
└───────────────────────────────────────────────────────────────────────┘
                     ▲                           │
                     │ implements                │ emits CanonicalDataPoint
                     │ ISourceAdapter            ▼
┌───────────────────────────────────────────────────────────────────────┐
│                     SINK ADAPTERS  (own assemblies)                   │
│                                                                       │
│   MQTT [P2]   │   HTTP [P3]   │   TCP [P3]   │   future: UA-Server    │
└───────────────────────────────────────────────────────────────────────┘
```

**Invariants preserved:** protocol-agnostic Core (Blueprint LOCK #1),
canonical model (LOCK #2), per-sink fanout (LOCK #9), store-and-forward
(LOCK #8), per-adapter isolation (LOCK #10).

---

## 4. Runtime Data Flow — Single Source of Truth

```text
   Device                                                        Network
     │                                                              ▲
     │ protocol reads                                               │ push
     ▼                                                              │
 ┌────────────────┐    ┌──────────┐   ┌─────────┐   ┌──────────┐   ┌────────┐
 │ Source Adapter │───▶│ Pipeline │──▶│ Routing │──▶│  Buffer  │──▶│  Sink  │
 │  (Modbus/etc.) │    │ transforms   │  fanout │   │  SQLite  │   │ MQTT/  │
 └───────┬────────┘    └──────────┘   └─────────┘   └──────────┘   │ HTTP/  │
         │                                                          │ TCP    │
         │ browse metadata (adapter startup + on-demand)             └────────┘
         ▼
 ┌───────────────────────┐
 │   Tag Catalog  [P3]   │  ◀── read-only view
 │   (metadata only,     │       /api/tags, /api/tags/{id}
 │   NEVER live values)  │
 └───────────────────────┘
```

**Hard rule:** the Tag Catalog holds `TagDefinition` metadata plus a
non-authoritative `lastSeenUtc` / `lastQuality` timestamp. It **never**
serves a value the pipeline didn't produce. OPC UA Server (Phase 5) will
introduce a pull-sink with its own last-known-value store; Phase 3 does
not pre-empt that design.

---

## 5. Modbus TCP Adapter — Internal Architecture

Lives at `src/ElpisEdgeConnect.Sources.ModbusTcp/`. Implements
`ISourceAdapter` with capabilities `Polling | Browse | TestConnect`.
Write-back (`SourceCapabilities.WriteBack`) is deferred to Phase 5.

```text
┌────────────────────────────────────────────────────────────────────────┐
│                 ElpisEdgeConnect.Sources.ModbusTcp                     │
│                                                                        │
│   ┌──────────────────────────────────────────────────────────────┐     │
│   │              ModbusTcpSourceAdapter : ISourceAdapter         │     │
│   │  InstanceId │ ProtocolName="modbustcp" │ Capabilities        │     │
│   └──────────┬───────────────────────────────────┬───────────────┘     │
│              │ lifecycle / polling loop          │ browse              │
│              ▼                                   ▼                     │
│   ┌─────────────────────┐             ┌──────────────────────┐         │
│   │ Connection Manager  │             │   Tag Resolver       │         │
│   │ - TCP + RTU-over-TCP│             │ - CSV import         │         │
│   │ - auto-reconnect    │             │ - template profiles  │         │
│   │ - per-tx timeout    │             │ - validates datatype │         │
│   │ - circuit breaker   │             │   + address class    │         │
│   └──────────┬──────────┘             └──────────┬───────────┘         │
│              │                                   │                     │
│              ▼                                   ▼                     │
│   ┌─────────────────────────────────────────────────────────────┐      │
│   │              Scan-Group Planner & Block Optimizer           │      │
│   │  groups tags by (scanRateMs, unitId, registerClass)         │      │
│   │  packs into FC-compliant blocks (see §6)                    │      │
│   └──────────────────────────┬──────────────────────────────────┘      │
│                              ▼                                         │
│   ┌─────────────────────────────────────────────────────────────┐      │
│   │                  Transaction Executor                       │      │
│   │  FC01/02/03/04 reads │ retry + backoff │ health metrics     │      │
│   └──────────────────────────┬──────────────────────────────────┘      │
│                              ▼                                         │
│   ┌─────────────────────────────────────────────────────────────┐      │
│   │                  Decoder / Normalizer                       │      │
│   │  byte-order: big / little / word-swap (mid-big, mid-little) │      │
│   │  datatypes: bool, int16, uint16, int32, uint32, float32,    │      │
│   │             float64, string (reg-packed)                    │      │
│   │  scale/offset + unit metadata                               │      │
│   └──────────────────────────┬──────────────────────────────────┘      │
│                              ▼                                         │
│                ────▶ CanonicalDataPointFactory ────▶ (to Core Pipeline)│
└────────────────────────────────────────────────────────────────────────┘
```

### Per-tag configuration (locked shape for Modbus)

```
TagDefinition (modbus-scoped):
  name                e.g. "spindle_load"
  unitId              1..247  (slave id behind the TCP gateway)
  registerClass       Coil | DiscreteInput | InputRegister | HoldingRegister
  address             zero-based register offset
  datatype            Bool | Int16 | UInt16 | Int32 | UInt32 | Float32 | Float64 | StringN
  byteOrder           AB | BA | ABCD | CDAB | BADC | DCBA
  scanRateMs          e.g. 100, 1000, 10000
  scale / offset      optional
  unit                "bar", "rpm", ...
```

---

## 6. Scan-Group Planner — How Block Packing Works

The piece that separates a toy Modbus client from a production one.

```text
                   INPUT: list of tags (each tagged with scanRateMs,
                          unitId, registerClass, address, width)

                                  │
                                  ▼
 ┌───────────────────────────────────────────────────────────────┐
 │  Step 1 — Bucket by (scanRateMs, unitId, registerClass)       │
 │                                                               │
 │   (100ms,  uId=1, Holding) ──▶ [tagA, tagB, tagC, ...]        │
 │   (1000ms, uId=1, Holding) ──▶ [tagX, tagY]                   │
 │   (100ms,  uId=1, Coil)    ──▶ [tagK]                         │
 │   (100ms,  uId=2, Holding) ──▶ [tagP]      ← different slave  │
 └────────────────────────────┬──────────────────────────────────┘
                              ▼
 ┌───────────────────────────────────────────────────────────────┐
 │  Step 2 — Within each bucket, sort by address                 │
 └────────────────────────────┬──────────────────────────────────┘
                              ▼
 ┌───────────────────────────────────────────────────────────────┐
 │  Step 3 — Greedy pack into FC-compliant blocks                │
 │                                                               │
 │   Per-FC hard limits:                                         │
 │     FC03/FC04  →  max 125 registers per request               │
 │     FC01/FC02  →  max 2000 coils per request                  │
 │                                                               │
 │   Coalescing rule:                                            │
 │     merge adjacent tags if (gap ≤ maxGapRegs) AND             │
 │     (block size stays within FC limit).                       │
 │     Default maxGapRegs = 8 (configurable per route).          │
 │                                                               │
 │   Why a gap is acceptable:                                    │
 │     one round-trip of 20 regs beats two round-trips of        │
 │     8 and 9 regs on almost any network.                       │
 └────────────────────────────┬──────────────────────────────────┘
                              ▼
 ┌───────────────────────────────────────────────────────────────┐
 │  OUTPUT: ScanPlan                                             │
 │    groups:                                                    │
 │      - intervalMs, unitId, fc, blocks:[{start,count,tags}]    │
 │    timer schedule driven by intervalMs per group              │
 └───────────────────────────────────────────────────────────────┘
```

**Observable outputs** fed into `AdapterHealth`:
last-success-per-block, transactions/sec, average RTT, retry count,
slave-error-exception counts per FC.

---

## 7. Tag Catalog API — Boundary Rules

```text
                    ┌────────────────────────────────┐
                    │        Tag Catalog  [P3]       │
                    │                                │
   browse() on ────▶│  metadata store                │◀──── GET /api/tags
   adapter start    │  - TagDefinition               │      GET /api/tags/{id}
                    │  - owning adapter instanceId   │      GET /api/tags?source=...
                    │  - lastSeenUtc (timestamp)     │
                    │  - lastQuality                 │
                    │                                │
                    │  NO value. NO payload caching. │
                    └────────────────────────────────┘
                                    ▲
                                    │ lastSeenUtc / lastQuality
                                    │ updated by a lightweight
                                    │ observer on the pipeline
                                    │ (non-blocking, fire-and-forget)
                                    │
              Source ──▶ Pipeline ──┴──▶ Routing ──▶ Buffer ──▶ Sinks
```

**Acceptance test:** `GET /api/tags/{id}` returns
`{name, datatype, unit, lastSeenUtc, lastQuality, sourceInstanceId}` and
does **not** return `value`. Any PR that adds a value field fails review.

---

## 8. HTTP & TCP Sinks — Shared Shape

Both ride the existing `SqliteBuffer` per-sink cursor. No sink-specific
persistence layer.

```text
  Buffer cursor (per-sink)
        │
        ▼
 ┌───────────────────┐        ┌───────────────────┐
 │   HTTP Sink       │        │    TCP Sink       │
 │  - URL + auth     │        │  - host:port      │
 │  - JSON / NDJSON  │        │  - framing:       │
 │  - batch size N   │        │    length-prefix  │
 │  - retry/backoff  │        │    OR newline     │
 │  - ack = 2xx      │        │  - reconnect loop │
 └─────────┬─────────┘        └─────────┬─────────┘
           ▼                            ▼
       Northbound                   Northbound
        HTTP(S)                      TCP server
```

Auth modes for HTTP: `None` | `Basic` | `Bearer`. mTLS deferred.

---

## 9. Milestones

| ID  | Milestone                                        | Output |
|-----|--------------------------------------------------|--------|
| E1  | License enforcement at adapter registration     | Adapter DI gate; `Blocked` state; instance-count cap |
| E2  | Tag Catalog API (read-only)                     | `/api/tags*` endpoints; metadata store; pipeline observer |
| F1  | Modbus TCP — connection + transaction layer     | `ModbusTcpSourceAdapter` skeleton, FC01–04 reads, retry/backoff |
| F2  | Modbus — scan-group planner + block optimizer   | `ScanPlan`, FC-limit-safe packing, gap coalescing |
| F3  | Modbus — decoder + byte-order support           | All datatypes, 6 byte-orders, scale/offset |
| F4  | Modbus — CSV tag import + template profiles     | Import command, validator, two reference templates |
| F5  | Modbus — diagnostics integration                | Per-block RTT/error counts on `AdapterHealth` |
| G   | HTTP sink                                        | `ISinkAdapter`, JSON/NDJSON, auth, batch, retry |
| H   | TCP sink                                         | `ISinkAdapter`, length-prefixed + newline framing, reconnect |
| I   | Adapter diagnostics endpoints                    | `/api/adapters/{id}/diagnostics` generic surface |
| K   | Pilot                                            | 1 FOCAS2 CNC + 1 Modbus PLC → MQTT+HTTP+TCP, 72 h stable |

---

## 10. Timeline

```text
Week  1     2     3     4     5     6     7     8
      │                                         │
E1 ▓▓▓│                                         │
E2    │▓▓▓▓▓                                    │
F1    │▓▓▓▓▓▓▓▓                                 │
F2    │     ▓▓▓▓▓▓▓                             │
F3    │           ▓▓▓▓▓                         │
F4    │                 ▓▓▓▓                    │
F5    │                     ▓▓▓                 │
G     │           ▓▓▓▓▓▓▓                       │
H     │                 ▓▓▓▓▓                   │
I     │                       ▓▓▓               │
K     │                             ▓▓▓▓▓▓▓▓▓▓▓▓│
```

Critical path: **F1 → F2 → F3 → F4 → K**. Sink work (G, H) runs in
parallel; handed off to pilot only when F5 is green.

---

## 11. Definition of Done (Phase 3)

- All new code under `src/ElpisEdgeConnect.Sources.ModbusTcp/`,
  `src/ElpisEdgeConnect.Sinks.Http/`, `src/ElpisEdgeConnect.Sinks.Tcp/`
- Zero warnings, zero errors under `TreatWarningsAsErrors=true`
- Unit-test coverage ≥80% on each new assembly
- Integration tests against a real Modbus simulator (e.g., `diagslave`
  or `ModRSsim2`) committed and running in CI
- Modbus scan-group planner covered by property-based tests for
  FC-size safety
- Pilot: 72 h with ≥99.9% delivery on each of MQTT/HTTP/TCP sinks,
  zero adapter crashes
- `docs/phase3-exit/phase3-exit-checklist.md` signed

---

## 12. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Real Modbus PLC rejects block sizes < FC max | Medium | Per-device `maxBlockRegs` override in config |
| Byte-order bugs surface only on customer hardware | High | Canonical byte-order test fixtures; round-trip probe tool in adapter diagnostics |
| HTTP sink backpressure overflows buffer | Medium | Adaptive batch size; buffer watermark alerts |
| TCP sink peer silently drops frames | Medium | Application-level ack if the peer supports it; otherwise rely on TCP + reconnect ack |
| Pilot PLC has RTU-over-TCP gateway | Medium | Encapsulation mode supported from F1 |
| Scope creep into write-back or probe discovery | High | Surface as Phase 5 when raised; do not absorb silently |

---

## 13. Explicitly Deferred

- OPC UA Server (pull sink) — **Phase 5**
- Write-back / control writes — **Phase 5**
- Probe-based Modbus auto-discovery — never as a headline feature;
  optional debug tool in Phase 4
- Web UI tag authoring — **Phase 4**
- Multi-tenant MQTT topic scheme (`eremos/{tenant}/{site}/...`) —
  requires EREMOS contract change in `C:\dev\shared-knowledge\contracts\`
- S7 / EtherNet-IP / TwinCAT / PROFINET — **Phase 5+**, each a separate
  adapter assembly following the Modbus pattern
