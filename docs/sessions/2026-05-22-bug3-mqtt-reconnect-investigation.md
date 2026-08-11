# Bug 3 — `MqttSinkAdapter` slow recovery after broker process restart (P2, INVESTIGATION)

**Status:** OPEN — investigation plan. Not yet root-caused. Surfaced by the EREMOS V2 revalidation Gate 5 test ([PR #23](https://github.com/elpisitsolutions/EdgeConnect/pull/23) commit `c7a82a9`).
**Severity:** **P2** — operationally observable, no data loss (store-and-forward absorbs), but recovery is slower than the v2 plan's locked 5-second threshold.
**Date:** 2026-05-22
**Reporter:** EREMOS V2 revalidation autonomous run (Gate 5 wiring revealed the finding).
**Reproducer:** `tests/ElpisEdgeConnect.Integration.Tests/Eremos/EremosV2EndToEndTests.cs::Gate5_BrokerOutageReconnect_AdapterRecoversWithin5Seconds` (currently `[Skip]` with the observation in its reason string).

---

## 1. Observation

When a dedicated test Mosquitto broker is stopped and restarted on the same TCP port:

- The `MqttSinkAdapter`'s `publishSuccesses` metric does NOT increment within 5 seconds of `broker.StartAsync()` completion.
- Observed wall-clock recovery: **~15 seconds** (the test's polling deadline). The actual successful-publish moment may be later, but no successful publish is observed within the deadline.
- The store-and-forward buffer correctly absorbs points during the outage (verified by direct `RuntimeDiagnosticsCollector.GetRouteSnapshot(routeId).Buffer.CurrentDepth` reads).
- Gate 5's v2 plan §4.2.2 threshold is **adapter reconnects within 5s of broker recovery**. The observation violates that.

The other 6 EREMOS V2 gates landed on master via PR #23 all pass:

- Gates 1 (MQTT stability), 2 (emit/receive parity), 3 (schema), 4 (topic determinism) — pass via the full-gateway E2E foundation.
- Gate 8 (sink backpressure) — passes via `SlowSinkDecorator` slowness injection; buffer stays bounded by `MaxDepth` with `DropPolicy.DropOldest`.

---

## 2. Why this is P2, not P0/P1

| Severity | Criteria | Bug 3 fit |
|---|---|---|
| P0 | Data loss / system unusable | ❌ — store-and-forward buffer absorbs points during the outage; nothing is lost |
| P1 | Significantly degraded operation | ❌ — recovery still happens, just slower than the 5s target |
| **P2** | **Operationally observable, fixable without blocking** | **✅ — multi-second extra recovery delay is annoying but not catastrophic** |
| P3 | Nice to have | ❌ — the 5s threshold is in the v2 plan as a customer-facing commitment |

For the 100-CNC customer install: if Mosquitto restarts on the gateway (or a network blip causes a disconnect-and-reconnect within seconds), the data will accumulate in the buffer and drain when the adapter eventually reconnects. **No data is lost.** The only operational impact is a ~10-15s delay in the recovery window — observable in dashboards, fine for batch-aggregating systems like EREMOS V2's per-tag historian.

---

## 3. Hypotheses (ranked by likelihood)

### H1 — MQTTnet client refuses to reconnect to a different broker session

**Description:** MQTTnet 4.x's `MqttClient` may detect that the broker has a different session-state (Mosquitto with `persistence false` starts fresh) and either reject the reconnect OR back off aggressively. The client may also be confused by the previous TCP socket being closed unexpectedly (broker process killed, not gracefully shut down).

**Test infrastructure context:** `DedicatedTestBroker.StopAsync` uses `Process.Kill(entireProcessTree: true)` — abrupt. The MQTT connection sees a TCP RST or unclean disconnect. Then a NEW Mosquitto process starts on the same port. To the MQTTnet client, the broker effectively "got amnesia" — different session id, different connection ack history.

**Likelihood:** **HIGH**. This is the most likely explanation. Production deployments rarely restart Mosquitto in-place during normal operation; if they do, it's usually a graceful restart that the client handles differently.

**Investigation:** capture the MQTTnet client's `DisconnectedAsync` event reason during the Gate 5 test. If the reason is `Other` or `Unspecified` with a TCP-level error message, this hypothesis is confirmed.

### H2 — `MqttSinkAdapter`'s reconnect backoff is too aggressive

**Description:** Looking at `MqttSinkAdapter` initialisation, the config takes `ReconnectDelayMs` (initial, default likely small) and `MaxReconnectDelayMs` (cap). If the adapter does exponential backoff starting at the initial delay, after several failures it could hit the cap and stay at the cap until reconnect succeeds. If the cap is e.g. 5000ms, recovery might exceed 5s by construction.

**Test config in Gate 5:** `ReconnectDelayMs = 200, MaxReconnectDelayMs = 1000`. With exponential backoff (multiplier 2): 200 → 400 → 800 → 1000 (cap) → 1000 → 1000 ... If the broker is down for 1.5s, by the time it's back the adapter has been backing off at 1000ms. The next attempt at 1000ms succeeds. Total: ~3-4s. That's < 5s, so this hypothesis alone doesn't fully explain the 15s observation.

**Likelihood:** **MEDIUM**. Could be a contributing factor combined with H1.

**Investigation:** add structured logging to the adapter's reconnect loop. Capture each reconnect attempt with a timestamp. The actual elapsed-time profile reveals the backoff curve.

### H3 — Route worker isn't pulling buffered points until the next poll cycle

**Description:** The route worker loop (`RoutingEngine`) is responsible for pulling points from the per-route buffer and calling `MqttSinkAdapter.PublishAsync`. If the worker is on a poll-based cadence (e.g., 1Hz), even after the adapter reconnects, the worker won't immediately drain — it waits for the next loop tick.

**Likelihood:** **LOW**. The route worker is responsive — it processes the buffer continuously, not on a poll. But the wiring may have an unexpected delay path. Worth verifying.

**Investigation:** add tracing to `RoutingEngine`'s worker loop. Confirm it tries `PublishAsync` immediately after the buffer non-empty signal.

### H4 — Test setup gap (not a real bug)

**Description:** The test stops + restarts a Mosquitto process on the same port. Production gateways never see this exact pattern. Real-world recovery scenarios are:
- Network blip → TCP-level reconnect (no broker process change) → MQTTnet handles cleanly
- Broker upgrade → graceful shutdown + restart with proper persistence → session continuity

If the failing scenario only manifests in `Process.Kill + spawn-fresh-process`, the test's reproducer isn't representative and the 5s threshold may be fine for production.

**Likelihood:** **MEDIUM**. If H1 is confirmed, H4 follows — the test scenario isn't a real production scenario.

**Investigation:** the customer-soak scenario (graceful Mosquitto restart vs network-blip with broker still running) is the real validation. The Gate 5 test as written exercises a corner case.

---

## 4. Recommended investigation order

1. **Add structured logging to the Gate 5 test** capturing:
   - `MqttClient.DisconnectedAsync` reason + timestamp
   - Each `MqttSinkAdapter` reconnect attempt + outcome + timestamp
   - `RoutingEngine` worker loop activity (publish-attempt timestamps)
   - Subscriber receive timestamps
   This reveals H1 vs H2 vs H3 in one test run.

2. **If H1 confirmed**: investigate whether the existing config's `ReconnectDelayMs` / `MaxReconnectDelayMs` semantics are appropriate. May need to add `CleanStart=true` or similar to the MQTTnet client options on reconnect to handle the session-state-mismatch case.

3. **If H2 confirmed**: relax the backoff cap OR add an "immediate retry on first failure" path that short-circuits the exponential backoff for the first reconnect attempt.

4. **If H4 confirmed**: revise the Gate 5 test to use a graceful broker restart (Mosquitto persistence enabled + SIGTERM) OR a network-level disruption (firewall block + unblock). Document the test scenario clearly.

5. **Production check (regardless of root cause)**: verify behaviour on the 100-CNC customer's deployment topology. If the operator restarts Mosquitto on the gateway during a maintenance window, what's the actual recovery time? Add this to the customer-site acceptance plan.

---

## 5. Acceptance criteria for Bug 3 resolution

- [ ] Root cause identified (H1 / H2 / H3 / H4 confirmed or rejected).
- [ ] Either: adapter modified to meet the 5s threshold under the test scenario, OR test scenario revised to match a production-realistic disconnect pattern + threshold met under that scenario, OR the threshold relaxed with documented rationale + customer engineering sign-off.
- [ ] `Gate5_BrokerOutageReconnect_AdapterRecoversWithin5Seconds` un-skipped and passing reliably (no flakiness).
- [ ] Findings documented in this file's §6 (resolution record).
- [ ] If the adapter was modified: regression test ensures the new behaviour doesn't break the existing Focas2 / MTConnect / Modbus E2E MQTT scenarios.

---

## 6. Resolution record (to be filled in)

*Will be populated when the bug is resolved. Captures root cause + fix + verification + any cross-project impact (EREMOS V2 contract revalidation passes Gate 5 under the customer-realistic scenario).*

---

## 7. Cross-references

- **EREMOS V2 revalidation plan trail:** [v1](2026-05-21-eremos-v2-revalidation-plan.md) → [v2](2026-05-21-eremos-v2-revalidation-plan-v2.md) (the 5s threshold is locked in §4.2.2).
- **Gate 5 test source:** `tests/ElpisEdgeConnect.Integration.Tests/Eremos/EremosV2EndToEndTests.cs::Gate5_BrokerOutageReconnect_AdapterRecoversWithin5Seconds`.
- **Test infrastructure (production-ready, reusable):** `DedicatedTestBroker.Stop/StartAsync`, `MqttSinkAdapter.CheckHealthAsync` publishSuccesses metric, `RuntimeDiagnosticsCollector.GetRouteSnapshot` buffer-depth observable.
- **Production adapter:** `src/ElpisEdgeConnect.Sinks.Mqtt/MqttSinkAdapter.cs` (focus areas: reconnect loop, `MqttClient` configuration, `DisconnectedAsync` event handling).
- **Bug tracking series:** Bug 1 (P3 — buffer path realignment, RESOLVED in PR #21), Bug 2 (P0 — sink publish path silently dead, RESOLVED in PR #18), Bug 3 (this file).
- **Deployment context:** [100-CNC deployment readiness](2026-05-20-100-cnc-deployment-readiness.md). MQTT-only customer profile; broker restart scenarios are operationally realistic during gateway maintenance windows.
- **EREMOS V2 revalidation snapshot:** [docs/contracts/eremos-v2-revalidation.md](../contracts/eremos-v2-revalidation.md) — Gate 5 status carry-forward.

---

## 8. Why this isn't blocking the 100-CNC install

- No data loss — store-and-forward buffer absorbs points during the outage. Even if recovery takes 15s, all points eventually flow through to EREMOS V2.
- The 5s threshold is a v2 plan commitment for the customer-soak acceptance gate, not a launch blocker. If the 7-day soak surfaces real recovery scenarios that exceed the threshold, the bug becomes higher priority.
- The customer's typical recovery scenarios (network blip, broker upgrade with graceful shutdown) likely don't trigger the failing path (per H1 + H4).
- Workaround: keep the broker stable. Don't restart Mosquitto during data-collection windows.

The bug should be investigated + resolved before the EREMOS V2 contract revalidation gate goes green on the **real** EREMOS V2 instance path. Until then, mock-fallback covers 5 of 6 measurable gates (Gate 5 deferred).

---

**End of Bug 3 investigation plan.** Tracking issue may be opened separately via `gh issue create` referencing this file.
