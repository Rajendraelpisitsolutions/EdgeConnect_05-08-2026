# EREMOS V2 contract revalidation — plan trail v2 (brief, LOCKED)

**Status:** v2 — LOCKED after two ChatGPT review rounds. 18 items folded in: 16 from round-1 (8 gate refinements + 7 question verdicts + the taxonomy split) + 2 from round-2 (explicit sanitization rule documentation + collision-detection test). 0 rejected. Per round-2 ratification: "no further review pass needed before drafting." Brief style → v2 is implementation-ready spec; no v3.
**Date:** 2026-05-21
**Predecessor:** [v1 (open questions)](2026-05-21-eremos-v2-revalidation-plan.md)
**Parent roadmap:** [v2 §3.5](2026-05-21-phase2-wrapup-roadmap-v2.md) (locked decisions, deliverables, 8 success gates)
**Terminology freeze:** [v2.3 §1.2](2026-05-21-phase2-wrapup-roadmap-v2.3.md) (canonical tag path, append-only catalog semantics)
**Estimated size:** ~3-5 days (unchanged from v1; tighter measurement semantics, no scope growth).

---

## 0. What v2 changed from v1

ChatGPT review surfaced 18 distinct items across two rounds. **18 accepted, 0 rejected.** Healthy ratio — all feedback was "tighten measurement semantics" rather than "redesign the approach."

### Round 1 — gate refinements + taxonomy split (16 items)

| # | Item | Verdict | Where in v2 |
|---|---|---|---|
| 1 | **Taxonomy split — contract / resilience / real-EREMOS-only buckets** | ✅ **Strongest organizational move** | §4 (rewritten) |
| 2 | Gate 1 — gateway-side primary, `$SYS` secondary, gateway client only | ✅ Agree | §4.2 |
| 3 | Gate 2 — rename to "emit/receive count parity per topic" | ✅ Agree | §4.1.3 |
| 4 | Gate 3 — PerTag-only scope, drop batch-mode | ✅ Agree | §4.1.1 |
| 5 | **Gate 4 — explicit topic-shape resolution (the biggest technical lock)** | ✅ **Major** | §4.1.2 + §5 (NEW major) |
| 6 | Gate 5 — dedicated test broker, not Windows service stop | ✅ Agree | §4.2.2 + §6.3 |
| 7 | Gate 6 — hard real-vs-mock branching; rename mock variant | ✅ Agree | §4.3.1 |
| 8 | Gate 7 — rework duplicate logic OR real-EREMOS-only | ✅ Agree — **real-EREMOS-only** | §4.3.2 |
| 9 | Gate 8 — resilience gate, not contract gate (taxonomy) | ✅ Agree | §4.2.3 |
| 10 | Q1 — Real ingest counter required OR downgrade mock wording | ✅ Agree | §4.3.1 + §10.1 |
| 11 | Q2 — Owned broker for outage; SlowSinkDecorator for buffer | ✅ Agree | §6.3 + §6.4 |
| 12 | Q3 — Dedicated broker/port for this test | ✅ Agree | §6.3 |
| 13 | Q4 — Audit deviceClass per source NOW | ✅ Agree | §8 (NEW) |
| 14 | Q5 — Curated 5-10 source config interim | ✅ Agree | §6.2 + §7 |
| 15 | Q6 — 6-hour soak re-run cadence | ✅ Agree | §9.8 |
| 16 | Q7 — Pin contract version in docs/results, not payload | ✅ Agree | §7 + §11 |

### Round 2 — final tightenings (2 items)

| # | Item | Verdict | Where in v2 |
|---|---|---|---|
| 17 | **Explicitly define the MQTT sanitization rule** — don't leave it as "sanitize cleanly" | ✅ **Agree — locked in §5.1 from observed code** | §5.1 (NEW) |
| 18 | Add collision-detection test once sanitization rule exists | ✅ Agree — included in §5.3 + §4.1.2 measurement methodology | §5.3 (NEW) |

### Retractions / supersessions

- v1 Gate 2 claimed "zero gaps within any single tag's emission stream" — that was unmeasurable without a per-topic sequence number in the payload. v2 renames to **"emit/receive count parity per topic"** and matches the measurement to what we can actually observe.
- v1 Gate 4 regex `^eremos/[a-z0-9-]+/[a-z0-9-]+/[a-z0-9-]+/[a-z0-9_-]+$` rejected hierarchical canonical tag paths and assumed lowercase-only segments. v2 §5.2 locks the corrected regex based on the observed sanitization behaviour in `MqttTopicResolver.cs`.
- v1 §6 left the real-vs-mock decision deferred to v2. v2 §4.3.1 hard-branches Gate 6 so the mock path is honest about what it CAN'T validate.
- v1 §8 had 7 open questions for v2 to resolve. v2 §10 carries them all as RESOLVED with their final verdicts.

---

## 1. Goal

Unchanged from v1 §1. Prove in-house, ahead of the 100-CNC customer install, that EdgeConnect's MQTT PerTag emission still satisfies the EREMOS V2 ingest contract documented at `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md`.

**v2 framing addition:** revalidation produces an **auditable pass/fail verdict across three gate buckets** (contract / resilience / real-EREMOS-only) with measurement-debuggable semantics. A failed gate identifies which layer broke — EdgeConnect emission, MQTT transport, or EREMOS V2 ingest — rather than producing an ambiguous red light.

---

## 2. Why now

Unchanged from v1 §2. Three forces converge: Brother HTTP migration broadened the emission surface; topic shape evolved between legacy and new sink adapter; customer profile is locked (EREMOS V2 already deployed at site).

---

## 3. Locked inputs from the roadmap

Unchanged from v1 §3. v2 §3.5.1 Q1-Q4 verdicts carry as locked: real instance preferred, contract-level only, one-shot integration test, both standalone gate + soak sub-component.

---

## 4. The gate taxonomy (NEW v2 — the biggest restructure)

v1 had all 8 gates in one bucket. v2 splits them into three buckets so a red gate identifies which layer broke. Pass criterion is unchanged: **all gates must pass** for revalidation to be green.

### 4.1 Contract gates — validate EdgeConnect's emission matches the EREMOS V2 contract

A red gate in this bucket means **EdgeConnect emitted wrong shape/topic/count.** Fix the gateway.

#### 4.1.1 Gate 3 — Schema stability (PerTag-only scope)

**Lock:** validates every received payload against the **PerTag scalar contract only**. The PerTag contract is: UTF-8 string, no JSON wrapper, value-only emission. Batch-mode validation is **explicitly out of v1 scope** per round-1 #4 — the revalidation tests PerTag because PerTag is what EREMOS V2 consumes.

**Pass threshold:** 100% pass rate. Zero JSON wrappers, zero binary content, zero non-UTF-8 bytes.

**Measurement methodology:** `MqttPayloadValidator` test helper. For each received message, attempt parse as the PerTag scalar form. Any wrapper / structural artifact / non-UTF-8 byte = violation. The validator emits per-violation evidence (`topic`, `bytes`, `inferred-form`) into the test log.

#### 4.1.2 Gate 4 — Topic determinism (resolved per §5)

**Lock:** every emitted topic matches the EREMOS V2 PerTag topic shape after EdgeConnect's documented sanitization rule (§5.1). The contract is `eremos/{gw}/{deviceClass}/{src}/{tag-after-sanitization}`.

**Pass threshold:** zero topics outside the shape. Zero collisions between distinct canonical tag paths sanitizing to the same MQTT segment.

**Measurement methodology:** regex match per §5.2 against every observed topic + collision-detection test per §5.3. Both subgates must pass for Gate 4 to be green.

#### 4.1.3 Gate 2 — Emit/receive count parity per topic (renamed from "tag continuity")

**Lock — renamed per round-1 #3.** v1 said "zero gaps within any single tag's emission stream" — but PerTag scalar payloads carry no per-topic sequence number, so true gap detection isn't measurable from the subscriber side alone. v2 renames the gate to be honest about what we measure: **gateway emit count vs subscriber receive count, per topic, over the test window**.

**Pass threshold:** for every topic, `gateway.emit_count[topic] == subscriber.receive_count[topic]` exactly over the 2-minute test span.

**Measurement methodology:**
- Gateway side: read `mqtt_per_topic_publish_count` from the existing diagnostic surface (`RuntimeDiagnosticsCollector.GetSinkSnapshot()`). Reality-check the exact counter name during implementation; if absent, add it as a small instrumentation chip.
- Subscriber side: each subscriber maintains a `Dictionary<topic, long>` receive counter.
- After 2 minutes of steady-state emission (no broker outage injected), assert per-topic equality.

**Out of scope for v2:** true sequence-gap detection. A future enhancement could add an MQTT 5.0 user property carrying a per-source monotonic sequence; that's a separate plan trail, not this revalidation.

### 4.2 Resilience gates — validate EdgeConnect's behaviour under stress (not EREMOS contract)

A red gate in this bucket means **EdgeConnect's runtime behaviour degraded.** Fix the supervisor/buffer/sink. These gates are EdgeConnect-side health, not EREMOS contract compliance.

#### 4.2.1 Gate 1 — MQTT stability (gateway-side primary, `$SYS` secondary)

**Lock — round-1 #2:** gateway-side MQTT sink diagnostics are the **primary signal**. Broker `$SYS/broker/clients/connected` is supporting evidence only. Disconnect count is for **the gateway client only**, not all broker clients.

**Pass threshold:** zero disconnect storms (>3 disconnects in any 60-second window) for the gateway's MQTT client.

**Measurement methodology:**
- Primary: poll `mqtt.disconnects.total` from the sink's diagnostic surface every second; roll a 60-second window over the test span; assert max disconnects-per-window ≤ 3.
- Secondary (supporting evidence on failure only): subscribe a side-channel MQTT client to `$SYS/broker/clients/connected` and capture the connect/disconnect events for the gateway's client-id. If Gate 1 fails, the side-channel timeline is included in the failure evidence to help diagnose broker-side vs gateway-side cause.

#### 4.2.2 Gate 5 — Reconnect behaviour (dedicated test broker)

**Lock — round-1 #6:** broker outage is injected by **stopping the dedicated test broker process** (per §6.3) — NOT by stopping the Mosquitto Windows service. Service-stop is too environment-specific for CI portability.

**Pass threshold:** adapter reconnects within 5s of broker recovery; backpressure metrics stay bounded.

**Measurement methodology:**
1. Inject a 30-second broker outage mid-run by calling `_testBroker.Stop()` (the test owns the process per §6.3).
2. Restart the broker via `_testBroker.Start()`.
3. Measure time-to-first-publish after broker recovery via subscriber timestamp delta — must be ≤5000ms.
4. Measure `mqtt.outbound_queue.depth` ceiling during the outage — must stay below the configured bound (`MaxBufferSize` or equivalent).
5. Measure buffer drain to <10% of peak depth within 30s of broker recovery.

#### 4.2.3 Gate 8 — Backpressure behaviour (taxonomy clarified)

**Lock — round-1 #9:** this is **EdgeConnect sink/buffer validation**, not EREMOS contract validation. Kept in the revalidation because it's an end-to-end readiness gate, but the taxonomy is honest now.

**Pass threshold:** SQLite store-and-forward buffer fills bounded; intake drops measured only after buffer is full; full recovery on broker speedup.

**Measurement methodology:**
1. Inject a 60-second sink slowness via the `SlowSinkDecorator` test wrapper (per round-1 #11 / §6.4). The decorator artificially delays `MqttSinkAdapter.PublishAsync` by N ms per call.
2. Measure SQLite buffer file size growth — must stay bounded by configured `MaxBytes`.
3. Measure `route.intake_dropped.total` — increments only after the buffer reaches `MaxBytes` (not before).
4. After 60s, remove the SlowSinkDecorator delay.
5. Measure emit rate — must return to pre-injection baseline within 60s of speedup.

### 4.3 Real-EREMOS-only gates — meaningful only with an actual EREMOS V2 instance

A red gate in this bucket means **EREMOS V2 ingested differently than gateway emitted.** Investigate the broker/EREMOS layer. **These gates are explicitly skipped under the mock-subscriber fallback** per round-1 #7 + #8.

#### 4.3.1 Gate 6 — EREMOS ingestion (parsing drift) — hard real-vs-mock branch

**Lock — round-1 #7:**

- **Real EREMOS path** (preferred): EREMOS V2's ingest counter is the comparator. Poll the counter at test start + end; gateway `sink.points_emitted.total` at the same timestamps. Assert `|emit - ingest| / emit < 0.01`.
- **Mock path** (fallback): Gate 6 is **renamed "contract subscriber receive parity"** and tests `mock_subscriber.message_count == gateway.emit_count` to within 1%. **It does NOT claim to validate "EREMOS V2 ingest."** The mock cannot catch EREMOS-side parsing regressions; the gate's wording in the test log + the v2 revalidation report makes this explicit.

**Pass threshold (real):** `|emit - ingest| / emit < 0.01` over the test window.
**Pass threshold (mock):** `mock_subscriber.message_count == gateway.emit_count` exactly over the test window.

**Q1 dependency (round-1 #10):** if EREMOS V2 doesn't expose an ingest counter (HTTP endpoint, `$SYS` topic, or DB query), Gate 6 is **mock-only until** an EREMOS V2-side chip adds the counter. The v2 revalidation report explicitly notes which path was taken.

#### 4.3.2 Gate 7 — Duplicate publish detection (renamed; real-EREMOS-only)

**Lock — round-1 #8:** v1's `(topic, deviceTimestamp, value)` tuple-uniqueness check has a false-positive risk — a tag emitting `false` repeatedly with coarse device-clock resolution could legitimately produce duplicate tuples. More importantly, the gate's semantic purpose is **"EREMOS V2 isn't replaying stale messages,"** which only EREMOS V2's side can validate.

**v2 lock:** Gate 7 is **real-EREMOS-only**. Under the mock-subscriber path, this gate is **skipped** (not "tested with false positives"). The v2 revalidation report explicitly logs Gate 7 as `SKIPPED — mock path` rather than fabricating a pass/fail.

**Pass threshold (real only):** zero duplicate `(message-id)` tuples observed in EREMOS V2's ingest log over the 2-minute test window. Where `message-id` = per-source monotonic sequence + topic (or whichever identity field EREMOS V2 uses for replay detection — verified during implementation against the contract).

**Measurement methodology (real only):** query EREMOS V2's ingest log for the test window via whatever surface EREMOS V2 exposes (HTTP / DB / `$SYS`). Reality-check the surface during implementation.

---

## 5. Gate 4 topic-shape resolution (NEW major v2 — the biggest technical lock)

v1 left Gate 4 vague. v2 locks the exact behaviour from the observed `MqttTopicResolver.cs:67-84` implementation.

### 5.1 Verified sanitization rule (read from `MqttTopicResolver.cs:67-84` on 2026-05-21)

**Lock — the existing sanitization rule, verbatim from observed code:**

| Rule | Behaviour |
|---|---|
| `/` → `_` | Forward slash (topic separator) replaced with underscore |
| `+` → `_` | MQTT single-level wildcard replaced with underscore |
| `#` → `_` | MQTT multi-level wildcard replaced with underscore |
| Null byte → stripped | Removed entirely |
| Leading/trailing whitespace → trimmed | `String.Trim()` |
| Null or empty → fallback | `unknown` for `gatewayId`/`sourceId`/`routeId`, `_unknown_` for `tagName`, `cnc` for `deviceClass` |
| Case | **Preserved** (uppercase and lowercase both pass through) |
| Unicode | **Preserved** (no ASCII-only restriction) |

Applies to: `{gatewayId}`, `{sourceId}`, `{routeId}`, `{tagName}`, `{deviceClass}` placeholders in the topic template.

**Concrete examples** (Brother + FOCAS2 canonical tag paths):

| Canonical tag path | Sanitized topic segment |
|---|---|
| `Status/RunState` | `Status_RunState` |
| `Status/Mode` | `Status_Mode` |
| `MachineInfo/Hostname` | `MachineInfo_Hostname` |
| `MachineInfo/StatusCode` | `MachineInfo_StatusCode` |
| `Tools/Magazine/3/ToolNumber` | `Tools_Magazine_3_ToolNumber` |
| `Tools/Tool/15/Name` | `Tools_Tool_15_Name` |
| `Alarms/Active/0/Number` | `Alarms_Active_0_Number` |
| `Maintenance/Notice/2/Description` | `Maintenance_Notice_2_Description` |

The canonical tag path remains hierarchical **internally** (in `BrotherTagMap`, `Focas2TagMap`, `CanonicalDataPoint.TagPath`, the Runtime Tap, the canonical pipeline, the audit chain). Sanitization happens **at the MQTT transport boundary only**. This preserves Runtime Tap semantics, canonical catalogs, future OPC UA alignment, and internal consistency.

### 5.2 Resulting topic regex

**Lock — Gate 4 measurement regex:**

```
^eremos/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+$
```

Differences from v1's regex:

- `[a-z0-9-]` → `[A-Za-z0-9_-]` per the "case preserved" rule. Canonical tag paths use mixed case (`Status/RunState`, `MachineInfo/Hostname`); the sanitized topic segment retains that case.
- Underscore (`_`) is now an allowed segment character per the sanitization rule.
- Five segments total: `eremos / {gw} / {deviceClass} / {src} / {sanitized-tag}`.

The deviceClass segment uses `[A-Za-z0-9_-]+` (not the bare deviceClass vocabulary) so that a future custom deviceClass per Phase 2 of the contract migration doesn't break the regex. Cross-check against the deviceClass vocabulary (`cnc`, `plc`, `daq`, `tracker`, `meter`, `gateway` per `shared-knowledge/contracts/eremos-per-tag-mqtt.md`) is a separate audit (§8).

**Unicode-allowing tag paths:** v2 does NOT extend the regex to allow Unicode in tag segments. v1 catalogs use ASCII-only canonical paths; any future Unicode tag path would require both a regex update and a contract-document update. Out of scope for v2 — the catalogs are ASCII today.

### 5.3 Collision-detection test (NEW per round-2 #18)

ChatGPT correctly identified that sanitization can create collisions between distinct canonical tag paths. Example:

| Canonical tag path | Sanitized MQTT segment |
|---|---|
| `Status/Run/State` | `Status_Run_State` |
| `Status_Run/State` | `Status_Run_State` |
| `Status/Run_State` | `Status_Run_State` |

All three normalize to the same MQTT topic segment, but represent semantically distinct tags. If they're authored on the same source, EREMOS V2 would receive interleaved publications under one topic with no way to disambiguate.

**Lock — collision-detection subgate of Gate 4:**

For each source under test, enumerate the configured canonical tag paths. Apply the sanitization rule (§5.1) to each. Assert no two distinct canonical paths sanitize to the same MQTT segment. A collision = Gate 4 failure (subgate "collision-free").

**Pass threshold:** zero collisions across all sources under test.

**Measurement methodology:**

```csharp
// Pseudocode for the test helper
foreach (var source in configUnderTest.Sources)
{
    var sanitized = source.CanonicalTagPaths
        .Select(p => (canonical: p, mqtt: MqttTopicResolver.Sanitize(p, fallback: "_unknown_")))
        .ToList();

    var grouped = sanitized.GroupBy(s => s.mqtt).Where(g => g.Count() > 1);
    if (grouped.Any())
    {
        // Collision found — log evidence, fail Gate 4
        foreach (var group in grouped)
        {
            log.Error($"Collision: MQTT segment '{group.Key}' produced by canonical paths: {string.Join(", ", group.Select(s => s.canonical))}");
        }
        return GateResult.Fail("collision-free subgate");
    }
}
return GateResult.Pass();
```

**Operator-facing rule (to document in `shared-knowledge/contracts/eremos-per-tag-mqtt.md` per round-2 #17):** operators MUST NOT author tag paths whose post-sanitization form collides with another tag path on the same source. The Provisioning Subsystem (Chip 3) inherits this rule — its template validator should also detect collisions at config-generation time.

### 5.4 Shared-knowledge contract update (cross-project)

Per round-2 #17 and per `CLAUDE.md` shared-knowledge rules ("Changes affecting both projects → edit in `C:\dev\shared-knowledge\`, commit, push"), the sanitization rule + collision-detection rule must be promoted to:

**`C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md`** — a new subsection "Tag-path sanitization rule" documenting:
- The exact per-character rule (§5.1 table)
- The resulting topic regex (§5.2)
- The operator-facing collision-free rule (§5.3)
- Examples (§5.1 examples table)

This shared-knowledge edit is a **dependency step** for the implementation session (added to §9 step 14). It is NOT part of the EdgeConnect repo's commit history; it ships via the shared-knowledge repo.

---

## 6. Real vs mock — hard branching

### 6.1 Decision tree (revised from v1 §6)

1. **First choice — real EREMOS V2 instance.** Stand up the customer-bound EREMOS V2 binary/container in the in-house lab against the dedicated test broker (§6.3). Per §7-Q8, the customer profile confirms EREMOS V2 is already runnable; the in-house team should have or obtain the binary.
2. **Fallback — contract-driven mock subscriber.** A subscriber implementing exactly the EREMOS V2 subscription pattern (`eremos/+/cnc/+/+` for Phase 0). Emits a receive-count surface for Gate 6 (mock variant). Pins the contract on paper; does NOT test EREMOS V2 internals.

### 6.2 Configuration source — curated interim (round-1 #14 / Q5)

**Lock — curated 5-10 source config interim.** The revalidation does NOT block on Chip 3 (Provisioning Subsystem) shipping. v2 uses a hand-curated config under `tests/ElpisEdgeConnect.Integration.Tests/Fixtures/eremos-revalidation-config.json` with:

- 3 FOCAS2 sources
- 3 Brother HTTP sources
- 2 Modbus TCP sources
- 1 MQTT sink (PerTag mode, default template)
- 2 routes (one per protocol class per the M.2c §5.1.1 lock — Fanuc-route + Brother+Modbus-route)
- Total: 8 sources, ~16 tags/source = ~128 active topics

When Chip 3 lands, the test switches to consuming a generated config; canonical tag paths are append-only across the swap so the existing test fixtures continue to validate.

### 6.3 Dedicated test broker (round-1 #11 + #12 / Q2 + Q3)

**Lock — the test owns the broker.** Spawn a dedicated Mosquitto process per test via `Process.Start("mosquitto", "-p {port} -c {minimal-config-file}")` on a randomly-chosen free TCP port. The test's `IAsyncDisposable` tears it down. The broker is **not** the shared `localhost:1883` instance used by other MQTT integration tests — running this test in parallel with the others would actively interfere (Gate 1 asserts "zero disconnect storms" — a parallel test reconnecting would fail it).

**xUnit collection isolation:** `[Collection("EremosRevalidation")]` excludes parallel execution with other MQTT tests as a belt-and-braces guarantee.

**Existing dev-box Mosquitto requirement applies:** the existing MQTT integration tests already require Mosquitto installed on the dev box (per CLAUDE.md §8). v2 reuses that requirement — no new tooling.

### 6.4 SlowSinkDecorator (round-1 #11 / Q2)

**Lock — for Gate 8 only.** A test-only `SlowSinkDecorator : ISinkAdapter` wraps `MqttSinkAdapter` and artificially delays `PublishAsync` by a configured N ms per call. Used to inject sink slowness for Gate 8's backpressure test (§4.2.3). It does NOT affect Gate 5 (broker outage) — that uses dedicated-broker stop/restart per §6.3.

---

## 7. Deliverables (extended from v1 §5)

| File | Purpose | Status |
|---|---|---|
| `tests/ElpisEdgeConnect.Integration.Tests/EremosV2ContractTests.cs` | Standalone one-shot integration test class. `[Collection("EremosRevalidation")]`. Three `[Fact]` methods per taxonomy bucket (Contract / Resilience / Real-EREMOS-only); failure messages cite the bucket. ~2-minute total runtime. | New |
| `tests/ElpisEdgeConnect.Integration.Tests/Fixtures/eremos-revalidation-config.json` | Curated 8-source config per §6.2 | New |
| `tests/ElpisEdgeConnect.Integration.Tests/Eremos/EremosV2ContractValidator.cs` | Gate-by-gate measurement methodology. Each gate is its own method returning `GateResult { Pass, Evidence, Bucket }`. The test assembles all 8 and reports per-bucket pass/fail. | New |
| `tests/ElpisEdgeConnect.Integration.Tests/Eremos/EremosV2MockSubscriber.cs` | Mock subscriber for the fallback path. Implements `eremos/+/cnc/+/+` subscription, ingest-counter surface (for Gate 6 mock variant), receive-tuple recorder. | New |
| `tests/ElpisEdgeConnect.Integration.Tests/Eremos/DedicatedTestBroker.cs` | Owns the Mosquitto process per §6.3. `IAsyncDisposable` cleanup. | New |
| `tests/ElpisEdgeConnect.Integration.Tests/Eremos/SlowSinkDecorator.cs` | Test-only sink wrapper for Gate 8 backpressure injection. | New |
| `tests/ElpisEdgeConnect.Integration.Tests/Eremos/TopicShapeCollisionTests.cs` | Standalone unit tests for the collision-detection logic per §5.3. ~5 tests. | New |
| `docs/contracts/eremos-v2-revalidation.md` | Evergreen results doc — per-gate numerical evidence per run, appended over time. Pin `contractVersion = "v1"` per round-1 #16 / Q7. | New |
| `tools/eremos-v2-contract-harness/` | OPTIONAL standalone harness with `docker-compose.yml` if EREMOS V2 containerization is feasible. | Optional |
| `docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md` | This file. | Drafted |
| **`C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md`** (cross-project) | Add "Tag-path sanitization rule" subsection per §5.4 | New |
| `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` | Update §10 acceptance signal — "EREMOS V2 revalidation gate green" row | Edit |

**Test target:** ~+12 tests (was +10 in v1). Distribution: 1 contract-bucket test, 1 resilience-bucket test, 1 real-EREMOS-only test (skip-on-mock), 5 collision-detection unit tests, ~4 validator helper tests.

---

## 8. deviceClass audit (NEW v2 — round-1 #13 / Q4)

ChatGPT correctly flagged that Gate 4's topic regex assumes a per-source `deviceClass = "cnc"` resolution. If any source today emits a non-`cnc` deviceClass, EREMOS V2's Phase 0 subscription `eremos/+/cnc/+/+` wouldn't see it — and Gate 4 would silently pass on an emission that the contract never observes.

**Audit results (2026-05-22, drawn from inspection of each source module):**

| Source | Effective `deviceClass` resolution | Status |
|---|---|---|
| FOCAS2 (`Focas2SourceAdapter`) | Default `"cnc"` per `MqttTopicResolver.DefaultDeviceClass` constant | ✅ `cnc` |
| MT-LINKi (`Focas2/Collectors/MtLinkiCollector` — sub-protocol within FOCAS2 adapter) | Inherits FOCAS2's `"cnc"` | ✅ `cnc` |
| Brother HTTP (`BrotherHttpSourceConfiguration`) | Sets `DeviceClass = "cnc"` in the configuration default per `BrotherHttpSourceConfiguration.cs:99` (verified 2026-05-21 during M.P2.4 manual verification) | ✅ `cnc` |
| MTConnect | **VERIFY in v2 implementation step 1.** No M.2 migration plan exists yet; legacy MTConnect path may emit different deviceClass. | ⚠️ Reality-check |
| Modbus TCP (`ModbusTcpSourceConfiguration`) | **VERIFY in v2 implementation step 1.** Per-instance configuration. | ⚠️ Reality-check |

**Audit dependency in §9 step 1:** before writing the test, run a smoke that brings up each protocol's source with default configuration and inspects the resolved `deviceClass` via the MQTT topic. Any source resolving to non-`cnc` is either:

1. **Fixed** to emit `cnc` (if the customer's EREMOS V2 Phase 0 deployment requires it), OR
2. **Documented** as a known non-Phase-0 source (with EREMOS V2's subscription extended to `eremos/+/+/+/+` for Phase 1 awareness).

The deployment-readiness §10 acceptance signal cannot go green until the audit is closed.

---

## 9. Step-by-step implementation sequence (revised)

1. **deviceClass audit (per §8).** Confirm Brother + FOCAS2 + MT-LINKi all emit under `deviceClass = "cnc"` (already verified). Verify MTConnect + Modbus TCP. Document results in `docs/contracts/eremos-v2-revalidation.md`.
2. **Curated config fixture.** Author `Fixtures/eremos-revalidation-config.json` per §6.2.
3. **Dedicated test broker.** Implement `DedicatedTestBroker` per §6.3. Verify Mosquitto process spawn + teardown on the dev box.
4. **Mock subscriber.** Implement `EremosV2MockSubscriber` (regardless of whether the real-instance path lands first — the mock is the fallback).
5. **Gate validator core.** Implement `EremosV2ContractValidator` with the 8 gate methods. Each method returns `GateResult`. The validator assembles a `RevalidationReport`.
6. **Contract-bucket gates (Gates 2, 3, 4).** Implement measurement methodology per §4.1. Include the §5.3 collision-detection subgate of Gate 4.
7. **Resilience-bucket gates (Gates 1, 5, 8).** Implement measurement methodology per §4.2. Wire `DedicatedTestBroker.Stop/Start` + `SlowSinkDecorator`.
8. **Real-EREMOS-only gates (Gates 6, 7).** Implement measurement methodology per §4.3. Both gates' methods accept an `IEremosV2IngestSurface` dependency that's null under mock path (and the gate emits `SKIPPED — mock path`).
9. **Real EREMOS V2 setup.** Stand up the EREMOS V2 binary/container. Confirm subscription on `eremos/+/cnc/+/+`. Confirm ingest counter surface (per §4.3.1 Q1 resolution). If absent — fall back to mock and document.
10. **The `[Fact]` test.** Three test methods (one per taxonomy bucket) wired against the validator. Total runtime ≤2 minutes.
11. **Collision-detection unit tests.** `TopicShapeCollisionTests.cs` — 5 tests covering known collision cases (mixed `/` and `_`, multi-slash paths, etc.).
12. **Document observed values.** First clean run writes a snapshot into `docs/contracts/eremos-v2-revalidation.md` — per-gate numerical evidence + the path taken (real/mock) + the audit results from step 1.
13. **Shared-knowledge update.** Per §5.4 — add the "Tag-path sanitization rule" subsection to `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md`. Commit + push the shared-knowledge repo.
14. **Add to the soak success criteria.** Update `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` §10 to cite the revalidation gate green check. The 7-day soak re-runs the test every 6 hours (per Q6) and rolls up 28 pass/fail data points into the soak verdict.
15. **CI integration.** Tag the test `Category=EremosContract`. Confirm the local CI runner has Mosquitto installed (per existing MQTT integration test requirement). Confirm whether the real EREMOS V2 instance is reachable from CI; if not, CI runs the mock path only — local runs cover the real path.

---

## 10. Open questions — all RESOLVED in v2

### 10.1 v1 questions Q1-Q7 resolved per the verdict table in §0

| Q | v1 status | v2 resolution |
|---|---|---|
| Q1 — EREMOS ingest count source | Open | **Real EREMOS counter required for Gate 6 real path; if absent, mock-only OR cross-project EREMOS-side chip to add the counter. Mock variant explicitly renamed.** §4.3.1. |
| Q2 — Backpressure injection mechanism | Open | **`DedicatedTestBroker.Stop/Start` for broker outage (Gate 5); `SlowSinkDecorator` for sink slowness (Gate 8). Not Windows-service stop.** §6.3 + §6.4. |
| Q3 — Dedicated broker or share `localhost:1883` | Open | **Dedicated broker (random free port) owned by the test. `[Collection("EremosRevalidation")]` excludes parallel MQTT tests.** §6.3. |
| Q4 — deviceClass audit | Open | **Audit performed (§8). Brother/FOCAS2/MT-LINKi confirmed `cnc`. MTConnect + Modbus TCP carry to implementation step 1 reality-check.** §8. |
| Q5 — Chip 3 dependency | Open | **Curated 8-source config interim. Switch to Chip 3 output once Chip 3 lands.** §6.2. |
| Q6 — Soak re-run cadence | Open | **Every 6 hours = 28 pass/fail data points over 7 days.** §9.14. |
| Q7 — Contract version negotiation | Open | **Pin `contractVersion = "v1"` marker in `docs/contracts/eremos-v2-revalidation.md`. Not in payload (PerTag scalar contract has no metadata channel).** §7 + §11. |

**No new v2-specific open questions remain.** Implementation step 1 (deviceClass audit reality-check) resolves the last remaining input.

---

## 11. Cross-references

- **Locked inputs:** [Phase 2 wrap-up roadmap v2 §3.5](2026-05-21-phase2-wrapup-roadmap-v2.md).
- **Predecessor:** [v1 (this plan trail)](2026-05-21-eremos-v2-revalidation-plan.md).
- **Terminology freeze:** [v2.3 §1.2](2026-05-21-phase2-wrapup-roadmap-v2.3.md) — canonical tag path, append-only catalog semantics.
- **Customer profile:** [100-CNC deployment readiness](2026-05-20-100-cnc-deployment-readiness.md) §7-Q8 (EREMOS V2 deployed), §10 (acceptance signal), §5 (7-day soak).
- **Contract:** `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md` — extended per §5.4 with the sanitization rule.
- **Legacy contract:** `C:\dev\shared-knowledge\contracts\cnc-mqtt-payload.md` — out of scope for v2 (PerTag-only revalidation per round-1 #4).
- **MQTT sink implementation (anchor for §5.1 sanitization rule):** `src/ElpisEdgeConnect.Sinks.Mqtt/MqttTopicResolver.cs:67-84` (verified 2026-05-21).
- **MQTT sink topic template default:** `src/ElpisEdgeConnect.Sinks.Mqtt/MqttSinkConfiguration.cs` — `PerTagTopicTemplate` default `eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}`.
- **Existing MQTT integration tests (broker-posture reference):** `tests/ElpisEdgeConnect.Integration.Tests/Focas2ToMqttEndToEndTests.cs`, `MTConnectToMqttEndToEndTests.cs`, `ModbusTcpToMqttEndToEndTests.cs`.
- **Chip 3 dependency (Q5 / §6.2):** [Chip 3 Provisioning Subsystem v2](2026-05-21-chip3-provisioning-subsystem-plan-v2.md). Once Chip 3 lands, EREMOS revalidation swaps to consuming generated configs.
- **Test posture target:** roadmap v2 §4.1 reserves ~+10 tests; v2 plan lands ~+12.

---

**End of v2 (brief). LOCKED — ready for implementation.**

Per round-2 verdict: "no further review pass needed before drafting." Brief plan trail stops at v2 per roadmap v2 §2. The deviceClass audit (§8) carries one reality-check item (MTConnect + Modbus TCP confirmation) into implementation step 1; that is implementation-time verification, not a separate review cycle.
