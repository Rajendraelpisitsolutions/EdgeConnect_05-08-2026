# EREMOS V2 contract revalidation — observed values + deviceClass audit

**Status:** Evergreen artifact. Updated per revalidation run.
**Contract version pinned:** **v1** (Phase 0 — `eremos/+/cnc/+/+` subscription per `shared-knowledge/contracts/eremos-per-tag-mqtt.md` "Migration plan v1 → v2").
**Plan trail:** [v1 (open questions)](../sessions/2026-05-21-eremos-v2-revalidation-plan.md) → [v2 (LOCKED)](../sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md).

---

## 1. Path taken

Per the EREMOS V2 v2 plan §6 decision tree:

- **First choice:** real EREMOS V2 instance in the in-house lab → **NOT TAKEN.** Customer-bound EREMOS V2 binary / container is not yet available in the in-house lab.
- **Fallback:** contract-driven mock subscriber → **TAKEN.** `EremosV2MockSubscriber` implements the Phase 0 subscription (`eremos/+/cnc/+/+`) and records every received message for the contract + resilience gates' measurement methodology.

**Consequence of the mock path:** Gates 6 (EREMOS ingestion / parsing drift) and 7 (duplicate publish detection) are **explicitly SKIPPED** with reasons logged in the test output. The v2 plan §4.3 explicitly forbids fabricating pass/fail outcomes for real-EREMOS-only gates under the mock path.

---

## 2. deviceClass audit (v2 plan §8 — implementation step 1)

The Phase 0 subscription is `eremos/+/cnc/+/+`. A source emitting under a non-`cnc` deviceClass would be **invisible** to EREMOS V2's Phase 0 subscription — Gate 4 (topic determinism) would still pass on the emission, but the contract revalidation would silently miss the source. The audit confirms every source class in scope today resolves to `cnc`:

| Source | Effective `deviceClass` resolution | Status |
|---|---|---|
| FOCAS2 (`Focas2SourceAdapter`) | Defaults to `"cnc"` via `MqttTopicResolver.DefaultDeviceClass` constant | ✅ `cnc` |
| MT-LINKi (sub-protocol within FOCAS2 adapter) | Inherits FOCAS2's `"cnc"` | ✅ `cnc` |
| Brother HTTP (`BrotherHttpSourceConfiguration`) | Sets `DeviceClass = "cnc"` in the configuration default per `BrotherHttpSourceConfiguration.cs:99` (M.P2.4 verified) | ✅ `cnc` |
| MTConnect (`MTConnectSourceAdapter`) | Defaults to `"cnc"` per `MTConnectSourceAdapter.cs:134-145` — "MTConnect is a CNC-only standard" rule | ✅ `cnc` |
| **Modbus TCP** (`ModbusTcpSourceAdapter`) | **Requires** operator declaration; no default. Operator chooses `plc` / `meter` / `daq` / `cnc` / etc. per `ModbusTcpSourceAdapter.cs:735-760` | ⚠️ Operator-declared |

### Modbus interpretation

Modbus deliberately requires the operator to declare `DeviceClass` (no default; rejects config with a validation error if missing). This is correct per Modbus's protocol-agnostic nature — a Modbus connection might be a CNC, a PLC, an energy meter, a data-acquisition unit, etc.

**Operational consequence for EREMOS V2:**

- If the operator declares `DeviceClass = "cnc"` for a Modbus source, the source IS visible to EREMOS V2's Phase 0 subscription. Revalidation covers it.
- If the operator declares `DeviceClass = "plc"` (or `meter`, `daq`, etc.), the source is INVISIBLE to EREMOS V2's Phase 0 subscription. This is **not a bug** — it's the customer's intentional non-CNC source. Once EREMOS V2 advances to Phase 1 (`eremos/+/+/+/+`), non-CNC Modbus sources become visible.

The 100-CNC customer profile (per `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` §7) is **80 Fanuc + 20 Brother**; Modbus is not in the v1 customer scope. The Modbus operator-declared deviceClass concern is therefore an architectural carry-forward, not a gating issue for the 100-CNC install.

---

## 3. Observed values from the test run

The test suite that produced this snapshot:

| Test class | Count | Outcome |
|---|---|---|
| `TopicShapeCollisionTests` | 10 | All pass — sanitization rule + collision-detection logic locked |
| `EremosV2ContractTests` (validator logic + broker fixture) | 12 | All pass — Gate 1/2/3/4 logic + mock subscriber wiring + DedicatedTestBroker spawn |
| `EremosV2ContractTests` (full end-to-end + Gate 5 + Gate 8) | 3 | **SKIPPED** — deferred follow-up, explicit reasons in test output |

**Effective coverage by gate:**

| Gate | Bucket | This PR | Carry-forward |
|---|---|---|---|
| Gate 3 — Schema stability | Contract | ✅ Validator logic implemented + unit-tested against synthetic payloads | Wiring to full gateway integration |
| Gate 4 — Topic determinism | Contract | ✅ Validator logic + collision-detection subgate + 10 unit tests | Wiring to full gateway integration |
| Gate 2 — Emit/receive count parity per topic | Contract | ✅ Validator logic implemented + unit-tested with synthetic counter maps + mock subscriber wiring | Wiring to full gateway integration |
| Gate 1 — MQTT stability | Resilience | ✅ Validator logic + threshold ≤3 disconnects per 60s window | Live measurement against gateway sink diagnostics |
| Gate 5 — Reconnect behaviour | Resilience | ⏸️ DEFERRED — `DedicatedTestBroker.Stop/StartAsync` supports outage injection; the 3-phase test wiring is the follow-up | Live broker outage injection |
| Gate 8 — Backpressure behaviour | Resilience | ⏸️ DEFERRED — needs a `SlowSinkDecorator` wrapping `MqttSinkAdapter` (v2 §6.4) | Decorator implementation + buffer-depth measurement integration |
| Gate 6 — EREMOS ingestion (parsing drift) | **Real-EREMOS-only** | 🚫 SKIPPED — mock-fallback path. Validator's `BuildMockFallbackReport` returns an explicit Skip with reason. Cannot validate EREMOS V2's ingest pipeline from the subscriber side. | Real EREMOS V2 binary in lab |
| Gate 7 — Duplicate publish detection | **Real-EREMOS-only** | 🚫 SKIPPED — mock-fallback path. Validator returns explicit Skip with reason. Cannot validate EREMOS V2's replay-detection logic from the subscriber side. | Real EREMOS V2 binary in lab |

---

## 4. Sanitization rule (referenced from `MqttTopicResolver.cs:67-84`)

Verified 2026-05-22 against the observed code. **Locked rule:**

| Rule | Behaviour |
|---|---|
| `/` → `_` | Forward slash replaced with underscore |
| `+` → `_` | MQTT single-level wildcard replaced with underscore |
| `#` → `_` | MQTT multi-level wildcard replaced with underscore |
| Null byte → stripped | Removed entirely |
| Leading/trailing whitespace → trimmed | `String.Trim()` |
| Null or empty → fallback | `unknown` / `_unknown_` / `cnc` per the placeholder |
| Case | **Preserved** |
| Unicode | **Preserved** |

Examples (Brother + FOCAS2):

| Canonical tag path | Sanitized MQTT segment |
|---|---|
| `Status/RunState` | `Status_RunState` |
| `MachineInfo/Hostname` | `MachineInfo_Hostname` |
| `Tools/Magazine/3/ToolNumber` | `Tools_Magazine_3_ToolNumber` |
| `Tools/Tool/15/Name` | `Tools_Tool_15_Name` |
| `Alarms/Active/0/Number` | `Alarms_Active_0_Number` |
| `Maintenance/Notice/2/Description` | `Maintenance_Notice_2_Description` |

Topic regex Gate 4 applies:

```
^eremos/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+$
```

Collision-detection subgate: for each source's configured canonical tag paths, no two distinct paths may sanitize to the same MQTT segment. 10 unit tests in `TopicShapeCollisionTests` cover the cases (multi-slash, hyphen-vs-underscore, deduplication, empty/null skipping, etc.).

---

## 5. Carry-forward items

Surfaced explicitly so future planners do not rediscover the gaps:

1. **Real EREMOS V2 instance setup** (v2 §6.1 path 1). Once the customer-bound EREMOS V2 binary or container is reachable in the in-house lab, Gates 6 and 7 can be enabled. The path-discrimination logic is already in `EremosV2ContractValidator.BuildMockFallbackReport`; a sibling `BuildRealEremosReport` method would handle the real path.
2. **Full-gateway end-to-end wiring** (`EndToEnd_FullGatewayPipeline_AllContractAndResilienceGatesPass` — currently `[Skip]`). The validator + broker fixture + subscriber + collision-detection are all production-ready; the wiring layer that runs the gateway against the dedicated broker is the follow-up. ~200-300 LoC estimate based on the existing `Focas2ToMqttEndToEndTests` pattern.
3. **Gate 5 broker-outage injection** (`Gate5_BrokerOutageReconnect_AdapterRecoversWithin5Seconds` — currently `[Skip]`). `DedicatedTestBroker.Stop()` / `StartAsync()` already implemented; the 3-phase test logic (steady-state → outage → recovery) is the follow-up.
4. **Gate 8 sink-backpressure injection** (`Gate8_SinkBackpressure_BufferBoundedAndRecoveryUnder60s` — currently `[Skip]`). Requires a `SlowSinkDecorator : ISinkAdapter` wrapping `MqttSinkAdapter` per v2 §6.4. ~50 LoC estimate.
5. **Shared-knowledge contract update** — v2 §5.4 requires adding the "Tag-path sanitization rule" subsection to `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md`. **Diff prepared at `docs/contracts/eremos-per-tag-mqtt-PROPOSED-AMENDMENT.md` in this PR.** NOT pushed to the shared-knowledge repo from this autonomous run — that's a cross-project edit the user will perform on review.
6. **Modbus deviceClass per-source audit at customer install time** — the 100-CNC customer profile doesn't include Modbus today, but if the customer ever adds a Modbus source under a non-`cnc` deviceClass, EREMOS V2's Phase 0 subscription would silently miss it. Surface in the install playbook.

---

**End of revalidation snapshot.** Updated 2026-05-22 from the mock-fallback path. Next snapshot will append the real-EREMOS-only gate evidence once the lab instance is reachable.
