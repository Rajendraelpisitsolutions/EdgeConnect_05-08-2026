# EREMOS V2 contract revalidation — plan trail v1 (brief)

**Status:** **DRAFT v1 (brief)** — one of 7 parallel plan trails for the Phase 2 wrap-up. Per the roadmap §2 plan-trail discipline table, EREMOS V2 revalidation is a **brief v1 → v2** item (no v3 reality-check pass).
**Date:** 2026-05-21
**Parent roadmap:** `docs/sessions/2026-05-21-phase2-wrapup-roadmap-v2.md` §3.5 (locked decisions, deliverables, 8 success gates)
**Terminology freeze:** `docs/sessions/2026-05-21-phase2-wrapup-roadmap-v2.3.md` §1.2 applies — use **canonical tag path** and **append-only catalog semantics** where applicable; do not coin synonyms.

---

## 1. Goal

Prove, in-house and ahead of the 100-CNC customer install, that EdgeConnect's MQTT PerTag emission still satisfies the EREMOS V2 ingest contract documented at `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md`. Revalidation must produce an auditable, repeatable, pass/fail verdict against 8 measurable success gates — not a subjective "looks fine on the dashboard" check. A green revalidation gate is one of the load-bearing acceptance signals listed in `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` §10 and is also wired in as a sub-component of the 7-day in-house soak's success criteria (§3.5.1 Q4).

---

## 2. Why now

Three forces converge:

1. **Brother HTTP migration just landed (M.P2.4, PR #19).** A second non-CNC-shaped device class (`brother-http` → `deviceClass = "cnc"` for v1 compat, but a new source surface nonetheless) is now publishing through the same MQTT sink as FOCAS2 and MTConnect. The PerTag emission path is broader than the day the contract was last manually eyeballed.
2. **Topic shape evolved slightly between the legacy `src/ElpisEdgeConnect/` path and the new `Sinks.Mqtt` adapter.** Legacy emitted under the v1 contract (`eremos/{gw}/cnc/{src}/{tag}`); the new adapter's `PerTagTopicTemplate` default already carries the v2 shape (`eremos/{gw}/{deviceClass}/{src}/{tag}`) with `cnc` as the default device class. EREMOS V2 is on Phase 0 of the contract migration table (`shared-knowledge/contracts/eremos-per-tag-mqtt.md` §"Migration plan v1 → v2") and only subscribes to `eremos/+/cnc/+/+`. We need to confirm that the v2-shape default still resolves to a v1-compatible string for CNC sources, and that no new non-CNC source has snuck a non-`cnc` `deviceClass` into the emission stream.
3. **Customer profile is locked.** Per `2026-05-20-100-cnc-deployment-readiness.md` §7-Q8, EREMOS V2 is already deployed at the customer site, MQTT-only, Mosquitto 2.x is our scope to install. The pre-install end-to-end smoke (1 gateway → real Mosquitto → real EREMOS V2 ingest) is now feasible and necessary.

Revalidation is the controlled in-house equivalent of the customer-site smoke, repeatable in CI cadence.

---

## 3. Locked inputs from the roadmap

These are LOCKED per §3.5.1 of the v2 wrap-up roadmap. v1 of this plan does not relitigate them; v2 may add elaboration but not reverse them.

### 3.1 Verdicts (Q1-Q4, v2 §3.5.1)

| Q | Decision | Source |
|---|---|---|
| Q1 — real instance vs contract mock | **Real local EREMOS V2 instance if available; contract-driven mock subscriber as fallback.** | v2 §3.5.1 |
| Q2 — validation scope | **Contract-level only.** Subscribe + parse + assert structure. Do NOT test EREMOS V2's internal storage shape. | v2 §3.5.1 |
| Q3 — live or canned | **One-shot integration test.** Deterministic, runs under `dotnet test --filter "Category!=Flaky"`, fits CI cadence. | v2 §3.5.1 |
| Q4 — standalone or soak component | **Both.** Standalone pre-soak gate (few minutes) AND a sub-component of the 7-day soak's success criteria. | v2 §3.5.1 |

### 3.2 Estimate

~3-5 days (review-revised, v2 §3.5 closer).

---

## 4. The 8 success gates (restated from v2 §3.5.3 with measurement methodology)

The test passes iff **all eight** of these gates pass. No subjective interpretation; no "mostly passing." A failed gate means the gateway is broken, not the test.

| # | Gate | Pass threshold (verbatim from v2 §3.5.3) | Measurement methodology this plan would implement |
|---|---|---|---|
| 1 | MQTT stability | Zero disconnect storms (>3 disconnects in 60s) | Subscribe a side-channel MQTT client to the broker's `$SYS/broker/clients/connected` topic (Mosquitto exposes this) and tail the gateway's MQTT sink diagnostic counter `mqtt.disconnects.total`. Roll a 60-second window over the test span; assert max disconnects-per-window ≤ 3. |
| 2 | Tag continuity | Zero gaps within any single tag's emission stream | The sink already emits a per-tag `SequenceNumber` field for batched mode and an incrementing index per-topic for PerTag. For PerTag we will rely on a wrapping side-channel: a subscriber that records `(topic, monotonic-receive-counter)` and the gateway-side `emit_count` counter — gap = (emitted - received) per topic > 0. Reality-check the exact diagnostic surface in v2. |
| 3 | Schema stability | 100% pass rate; zero schema violations | A `MqttPayloadValidator` test helper that parses every received payload against the PerTag scalar contract (UTF-8 string, no JSON wrapper) per `shared-knowledge/contracts/eremos-per-tag-mqtt.md` §"Payload format". Any wrapper detection = violation. Batch-mode payloads (if any are emitted under the same broker session) are validated against the legacy CNC JSON shape. |
| 4 | Topic determinism | Every emitted topic matches `eremos/{gw}/{deviceClass}/{src}/{tag}` exactly; zero unexpected topic paths | Regex `^eremos/[a-z0-9-]+/[a-z0-9-]+/[a-z0-9-]+/[a-z0-9_-]+$` on every observed topic; topics outside this shape fail the gate. Cross-check the captured `{deviceClass}` segment against the deviceClass vocabulary in the contract (`cnc`, `plc`, `daq`, `tracker`, `meter`, `gateway`). |
| 5 | Reconnect behavior | Adapter reconnects within 5s of broker recovery; backpressure metrics stay bounded | Inject a 30-second broker outage mid-run (mechanism TBD — see §8 Q3). Measure: (a) time-to-first-publish after broker recovery via subscriber timestamp delta, (b) `mqtt.outbound_queue.depth` ceiling during the outage, (c) buffer drained to <10% of peak within 30s of recovery. |
| 6 | EREMOS ingestion (parsing drift) | Counts equal within 1% over the test window | If real instance: EREMOS V2 ingest counter polled at start + end; gateway `sink.points_emitted.total` polled at the same timestamps; assert `\|emit - ingest\| / emit < 0.01`. If mock subscriber: the subscriber's own message counter is the comparator. See §8 Q1. |
| 7 | Historian continuity (no duplicate storms) | Zero duplicates received within 5-minute windows | Subscriber records `(topic, deviceTimestamp, value)` tuples in a hash-set; assert set cardinality equals message count, modulo legitimate same-tag-same-value-same-instant edge cases (documented separately as Q-x in v2 if found). |
| 8 | Backpressure behavior | Store-and-forward buffer fills bounded; intake drops measured; full recovery on broker speedup | Inject a 60-second sink slowness (mechanism TBD — see §8 Q3). Measure: (a) SQLite buffer file size growth bounded by configured `MaxBytes`, (b) `route.intake_dropped.total` increments only after buffer is full, (c) emit rate returns to pre-injection baseline within 60s of broker speedup. |

These 8 gates are the **revalidation contract.** Pass means revalidation gate green. Fail means revalidation gate red — fix the gateway, not the test. Reuse of canonical tag-path identifiers across runs honors the append-only catalog semantics — the test fixture must not rename a tag mid-run.

---

## 5. Deliverables

| File | Purpose | Status at v1 |
|---|---|---|
| `tests/ElpisEdgeConnect.Integration.Tests/EremosV2ContractTests.cs` | Standalone one-shot integration test class. Launches gateway with a bulk-generated config (depends on §3.4 Chip 3 deliverable for the source config — interim: a curated 5-source config can stand in), real Mosquitto on `localhost:1883`, real EREMOS V2 ingest if available else mock subscriber. Runs for ~2 minutes, asserts all 8 success gates. xUnit `[Fact]` with `Category=EremosContract` trait. | Planned in v1 |
| `tools/eremos-v2-contract-harness/` (OPTIONAL) | Standalone harness with `docker-compose.yml` if EREMOS V2 containerization is feasible. Skipped if customer-bound EREMOS V2 binary is reachable directly. | Optional — decide in v2 |
| `docs/contracts/eremos-v2-revalidation.md` | Documentation of the 8 success gates + observed values from each test run (treated as an evergreen artifact, appended to per Phase). | Planned in v1 |
| `docs/sessions/2026-05-21-eremos-v2-revalidation-plan.md` | This file. | Drafted |

---

## 6. Decision on real-instance vs mock-subscriber fallback

Per Q1 (v2 §3.5.1), **real EREMOS V2 instance is preferred when feasible.**

Decision tree for v1, to be confirmed in v2:

1. **First choice:** stand up a real EREMOS V2 instance in the in-house lab against the same Mosquitto we already use for `Focas2ToMqttEndToEndTests` / `MTConnectToMqttEndToEndTests`. The customer profile (§7-Q8) confirms EREMOS V2 is already runnable; the in-house team should have or be able to obtain the binary/container.
2. **Fallback (if (1) is blocked by binary access, container packaging, or licensing):** a contract-driven mock subscriber implementing exactly the EREMOS V2 subscription pattern (`eremos/+/cnc/+/+` for Phase 0, optionally `eremos/+/+/+/+` for Phase 1+) and emitting an ingest counter via either an HTTP endpoint or a `$SYS`-style status topic. The mock pins the contract — it does not test EREMOS V2's internals.
3. **Gate 6 (parsing drift)** is the only gate that meaningfully differs between (1) and (2): with the real instance, parsing drift catches an entire class of EREMOS V2-side regressions (rename a deviceClass column, change ingest queue depth, etc.) the mock cannot catch. The mock can only confirm that EdgeConnect's emission matches the contract on paper.

v2 of this plan must declare which path applies and either remove the fallback wiring (if real instance is achievable) or commit to it.

---

## 7. Step-by-step implementation sequence

1. **Confirm broker posture.** Verify Mosquitto 2.x is reachable on `localhost:1883`, anonymous, matching the existing MQTT sink test posture. If a separate broker is needed for revalidation isolation, settle that in §8 Q3.
2. **Stand up the subscriber side.** Decide real-vs-mock per §6; if real, prepare the EREMOS V2 instance with a subscription on `eremos/+/cnc/+/+` (Phase 0 contract) and an accessible ingest counter. If mock, scaffold `EremosV2MockSubscriber` under `tests/ElpisEdgeConnect.Integration.Tests/` with the gate-6 counter shape locked.
3. **Wire the gateway harness.** Reuse `HostHarness.cs` from `tests/ElpisEdgeConnect.Integration.Tests/`. Generate a representative config (5-10 sources at first; full 100-CNC config is for the soak, not for this 2-minute one-shot) with PerTag mode and the default `eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}` template. Canonical tag paths must come from the existing `Brother`/`Focas2`/`MTConnect` tag maps (append-only — do not invent test-only paths).
4. **Build the validation harness.** `EremosV2ContractValidator` class encapsulating the 8-gate measurement methodology from §4. Each gate is its own private method returning `(bool pass, string evidence)`. The test assembles all 8 and reports per-gate pass/fail.
5. **Implement injection points.** Mechanism for gate 5 (broker outage) and gate 8 (sink slowness) — both depend on resolution of §8 Q2. Candidates: stopping the Mosquitto Windows service for 30s/60s, network-level firewall block, broker auth flip, or a per-test "slow sink" wrapper. v2 picks one.
6. **Write the `[Fact]` test.** Wire the harness + validator + injections in a deterministic order. Total test runtime budget: ~2 minutes, all 8 gates measured serially within a single test run (no inter-gate flakiness, no race conditions).
7. **Document observed values.** First clean run writes a snapshot into `docs/contracts/eremos-v2-revalidation.md` — per-gate numerical evidence (e.g. "Gate 1: 0 disconnect-storms over 120s, max disconnects/60s window = 0"). This snapshot is the baseline subsequent runs are compared against.
8. **Add to the soak success criteria.** Update `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` §10 acceptance signals to cite the revalidation gate green check. The 7-day soak must re-run the same `EremosV2ContractTests` periodically (suggested cadence: every 6 hours) and roll up pass/fail into the soak verdict per Q4.
9. **Decide on CI integration.** Tag the test `Category=EremosContract` and decide in v2 whether the local CI runner has Mosquitto installed (it should — existing MQTT integration tests already require it) and whether the real EREMOS V2 instance is reachable from CI.
10. **Write the test count.** Roadmap v2 §4.1 reserved ~10 tests for EREMOS V2 revalidation; this plan should land near that count (one `[Fact]` per gate plus a small handful of harness helpers).

---

## 8. Open questions for v2 ratification

These are v1-elaboration items, not architectural debates. v2 closes them.

| # | Question | Why it matters | Recommendation seed |
|---|---|---|---|
| Q1 | **Where does the EREMOS V2 ingest count come from?** Does EREMOS V2 expose an HTTP endpoint / a `$SYS`-style topic / a database query for ingest count, or do we count from the MQTT subscriber side only? | Gate 6 (parsing drift) is meaningful only if the count comparator is genuinely on EREMOS V2's ingest side. If we count from the subscriber, we're just measuring our own subscriber, not EREMOS V2's ingest pipeline. | Reach out to EREMOS V2 team; if they have no ingest endpoint, propose adding one as a small EREMOS V2-side chip (lives outside this plan but cited here as a dependency). |
| Q2 | **What backpressure injection mechanism do we use for gates 5 and 8?** Stop the broker? Firewall rule? Freeze the subscriber? Insert a slow-sink wrapper in front of `MqttSinkAdapter`? | Each mechanism stresses a different layer. Stopping the broker tests the adapter's reconnect logic; freezing the subscriber tests broker-side backpressure; slow-sink wrapper tests internal store-and-forward. Different gates may need different mechanisms. | Recommend Windows-service stop for gate 5 (broker outage) and a `SlowSinkDecorator` for gate 8 (sink slowness). Confirm in v2. |
| Q3 | **Does the integration test need its own Mosquitto instance or can it share the existing MQTT-tests broker?** Concurrent test runs may collide on the broker's connection / topic space. | Existing MQTT integration tests already share `localhost:1883`. Adding a 2-minute test that also asserts "zero disconnect storms" means parallel test runs would actively interfere. | Recommend: either run `EremosV2ContractTests` in its own `[Collection]` that excludes other MQTT tests, or use a dedicated test broker on a non-default port. v2 picks. |
| Q4 | **Does the v1 contract's `eremos/+/cnc/+/+` subscription still match every CNC source we emit today?** New sources (Brother) emit under `cnc` device class by default, but the per-source `DeviceClass` override pattern is on the migration plan (Phase 2 in `shared-knowledge/contracts/eremos-per-tag-mqtt.md`). | If any source already emits under a non-`cnc` device class, EREMOS V2's Phase 0 subscription wouldn't see it and gate 4 (topic determinism) would still pass. | Audit each source's effective `deviceClass` resolution path. v2 documents what each source currently emits. |
| Q5 | **Bulk-generated config dependency on Chip 3.** v2 §3.5.2 says the test launches gateway with a "bulk-generated config" — Chip 3 (provisioning subsystem) is the dependency. If Chip 3 isn't done when EREMOS revalidation starts, what's the substitute? | EREMOS revalidation is brief v1 → v2; Chip 3 is full v1 → v2 → v3. Sequencing matters. | Recommend a curated hand-written 5-10 source config as the v1 interim; switch to Chip 3 output once Chip 3 lands. Either way, the canonical tag paths are append-only across the swap. |
| Q6 | **Soak sub-component integration.** Q4 says revalidation is also a sub-component of the 7-day soak. What's the right re-run cadence within the soak? Once at start? Once per hour? Every 6 hours? | Test execution itself costs MQTT bandwidth and broker connections. Too-frequent re-runs may perturb the soak; too-infrequent runs may miss late-soak regressions. | Recommend every 6 hours; rolls up into the soak verdict as 28 pass/fail data points across 7 days. v2 ratifies. |
| Q7 | **Does the test need to assert on EREMOS V2 contract version negotiation?** The contract is in a v1 → v2 migration; EREMOS V2 today is Phase 0, EdgeConnect emits v2-shape-compatible default. | If EREMOS V2 advances to Phase 1 during the customer's lifecycle, do our PerTag emissions still pass? Gate 4 would catch a regression but only after EREMOS V2 upgrades its subscription. | Pin a `contractVersion = "v1"` marker in `docs/contracts/eremos-v2-revalidation.md`; revisit when EREMOS V2 moves to Phase 1. |

---

## 9. Cross-references

- **Locked inputs:** `docs/sessions/2026-05-21-phase2-wrapup-roadmap-v2.md` §3.5 (this plan inherits all decisions).
- **Terminology:** `docs/sessions/2026-05-21-phase2-wrapup-roadmap-v2.3.md` §1.2 (canonical tag path, append-only catalog semantics).
- **Customer profile:** `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` §7-Q8 (EREMOS V2 already deployed), §10 (acceptance signal), §5 (7-day soak profile).
- **Contract:** `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md` (the contract this plan revalidates against).
- **Legacy contract:** `C:\dev\shared-knowledge\contracts\cnc-mqtt-payload.md` (only relevant if a batch-mode regression is in scope — flagged via gate 3).
- **MQTT sink implementation:** `src/ElpisEdgeConnect.Sinks.Mqtt/MqttSinkConfiguration.cs` (PerTag template default), `src/ElpisEdgeConnect.Sinks.Mqtt/MqttTopicResolver.cs` (sanitization + `deviceClass` resolution).
- **Existing MQTT integration tests:** `tests/ElpisEdgeConnect.Integration.Tests/Focas2ToMqttEndToEndTests.cs`, `MTConnectToMqttEndToEndTests.cs`, `ModbusTcpToMqttEndToEndTests.cs` — share broker posture and `HostHarness` patterns.
- **Test posture target:** roadmap v2 §4.1 reserves ~+10 tests for this milestone (target test count ~2325 after this lands).

---

## 10. v2 entry conditions

v1 → v2 happens when:

1. All 7 open questions above (§8 Q1-Q7) have either a recommendation accepted or a deferred-with-rationale call.
2. The real-instance vs mock-subscriber fallback decision is made (§6).
3. The Chip 3 dependency is either resolved or the curated-config interim is endorsed (§8 Q5).
4. ChatGPT review pass complete on this v1.

v2 is the implementation-ready spec. There is no v3 — brief plan trail stops at v2 per roadmap v2 §2.

---

**End of v1 (brief). Awaiting review.**
