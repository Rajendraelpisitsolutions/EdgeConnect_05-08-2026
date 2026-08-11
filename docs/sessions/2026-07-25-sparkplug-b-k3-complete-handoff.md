# K3 — Sparkplug B Session Actor: COMPLETE (handoff)

**Date:** 2026-07-25
**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master) — **ready to merge**
**Frozen plan:** `docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md`
**Status:** all 7 slices implemented, externally reviewed, and **APPROVED & LOCKED**. §12 exit gate met. **No Core change.**

---

## 1. What K3 delivered

A single-owner **Sparkplug B session actor** (`SparkplugSessionActor`) behind a thin façade (`SparkplugSinkAdapter`) that implements the full replay-sink lifecycle for a local-transport (QoS-0 DATA/BIRTH, QoS-1 NCMD/Will) Sparkplug edge node, with no Core API or behavior change:

- **birth → replay → catch-up → live → operational rebirth → transport-suspect recovery → graceful end**, all serialized behind one `SemaphoreSlim` gate;
- durable gateway identity store (crash-safe `bdSeq`, batch-atomic alias allocator);
- candidate-only establishment (a failed candidate never erases the previous authority);
- the single-lock `AttemptHandoff` control plane (transport state + control episode + reason) that closed the two-word races;
- bounded, gate-released, single-owner transport recovery with correct retry classification, capped-exponential backoff, and terminal, non-resurrectable, race-free disposal;
- a complete, redacted, **coherent versioned diagnostic surface** and 3-way health (§8), with lifetime counters and correct failure/attempt accounting.

**Advertised contract:** `DeliveryCapabilities` = store-and-forward / `LocalTransport`; NDEATH carries no `seq`; transport-suspect recovery mints a new `bdSeq`; healthy rebirth retains it.

## 2. Slice map (all APPROVED & LOCKED)

| Slice | Content | Final |
|---|---|---|
| 1 | façade, config, actor skeleton, capability, fail-closed base methods | locked |
| 2 | gateway identity store (crash-safe bdSeq, batch-atomic aliases) | locked |
| 3 | pure birth-plan/mapping, wire-normalized comparator, material-schema classifier | locked |
| 4 | MQTT transport seam + initial Begin (CONNECT→SUBSCRIBE→NBIRTH, Will, generation token) | locked |
| 5 | Replay/CatchUp/Live DATA, is_historical, seq commit, final update, cutover | locked |
| 6 | operational rebirth (healthy + transport-suspect), async disconnect, stale-callback suppression, graceful End, terminal disposal | locked (pass 1 + pass 2 r0–r5 + r5.1) |
| 7 | 3-way health, coherent versioned diagnostics, lifetime counters, redaction, failure sweep, §11 acceptance matrix | locked (r0 → r3) |

## 3. §12 exit gate — MET

- ✅ Solution builds **0 errors**; `ElpisEdgeConnect.Sinks.SparkplugB` **0 warnings** under warnings-as-errors. (Two pre-existing warnings live in the legacy `ElpisEdgeConnect` project — `MachineManagerService.cs`, untouched, no warnings-as-errors there.)
- ✅ Full state machine exercised by **deterministic** tests (injected clock + injected delay + explicit transport hooks/barriers) — **no `Thread.Sleep`, no external broker**.
- ✅ `bdSeq` crash-safety (K0 WS5 matrix against the production store); aliases persist + stay stable across route recreation; batch-atomic all-or-none allocation.
- ✅ Epoch/session gating proven against the real actor (stale-session-same-epoch, non-increasing rebirth epoch, promotion-only-after-successful-NBIRTH).
- ✅ **No Core API or behavior change.** Changes confined to the SparkplugB assembly/tests + necessary metadata.
- ✅ **Full unfiltered regression green** (2026-07-25, `Category!=Flaky&Category!=RequiresMqttBroker`):
  Core **1250**, Host **225**, Management **1149** (full project), **SparkplugB 581**, Integration **79**, plus every source/sink adapter project — **0 failures solution-wide**.
- ✅ Plan trail + this handoff written before sign-off.
- ⚠️ **Still NOT operator-shippable** — the add-destination wizard tile is Available only after **K5**. K3 completion is a backend milestone (CLAUDE.md §8).

## 4. Key locked design anchors (do not relitigate)

- The actor owns ALL protocol state behind one `SemaphoreSlim` gate, never held across an await.
- `AttemptHandoff` is a **single-lock** compound; `ReadDiagnostics()` reads the (suspect, pending, reason) control triple under one acquisition.
- Disposal is the single linearization point: `_disposeTask` installed via CAS before the gate; `DisposeAsync` nulls `_activeRecoveryToken` before the CAS; `SuspectRebirthAsync` claims its recovery token atomically before the first await; `ValidateRecoveryOwnership` (DisposalWon || token-mismatch) guards every await boundary and the in-attempt allocation.
- **Ownership contract:** disposal prevents ADMISSION of a new establishment attempt; an attempt already admitted under the gate may finish or abort; committed-but-unused `bdSeq`/generation gaps are intentional/monotonic/never-reused; disposal retires any resulting transport before completing.
- **Diagnostics:** the lifecycle/session semantic root is published only at gated transitions; counters/timestamps/last-event codes AND the operational-control triple are overlaid live on read, the latter bound to the FULL authority (session+epoch+generation). `diagnosticsVersion` is a transition change-token (does not advance on a read). Everything redacted — no credential/endpoint/client-id/topic/payload/metric-value ever surfaces.
- Recovery-attempt tally + ordinal fire at the real attempt boundary (after bdSeq/request/client, before CONNECT); fatal preparation and disposal-rejected admission record no attempt.

## 5. Frozen outage envelope (carried into health docs)

- an outage **within** `TransportRecoveryMaxAttempts` may recover automatically (no route fault);
- budget **exhaustion terminally faults** the replay route (`transportRecoveryExhaustions`++, Unhealthy);
- recovery from that terminal state currently requires an operator **configuration re-apply** (no auto-restart);
- K3 does **not** yet provide legacy-MQTT-style **indefinite Degraded / store-and-forward outage parity** — that is the Core follow-up below.

## 6. Carry-forward (nothing lost)

- **K4:** route validation (delivery boundary + identity/descriptor uniqueness + one-route-per-Edge-Node cardinality); license module `sink-sparkplug-b` + catalog tier; DI registration triad; production `ISinkReplayCapabilityClassifier`; **gateway-data-root resolution + identity-store singleton registration**; wiring the SparkplugB diagnostic counters into Core `DiagnosticsMeters` (System.Diagnostics.Metrics) — the meters project from the same counter source K3 already exposes.
- **K5:** add-destination **wizard (mockup-first)** + edit routing → makes the tile Available (operator-shippable).
- **K6:** broker-in-CI + real Ignition / MQTT-Engine interop (ADR-0035 Open 4); the concrete `SparkplugMqttTransport` (MQTTnet wiring) is validated against a real broker here, not by K3 unit tests.
- **Core follow-up (frozen, §10.2/§13):** give `ReplayRouteDriver`/worker a **Degraded + backoff + store-and-forward** path for a sustained transport outage on Begin/Rebirth instead of terminal `Failed` — restoring parity with the legacy `SinkPublisher`. A Core change, out of K3's Core-clean scope. K3 ships the bounded in-`RebirthAsync` reconnect as the local mitigation.
- **Post-K3:** material-schema generation-changing rebirth (`AdvanceGenerationAsync`); clustered/standby lease; device-level DBIRTH/DDATA/DDEATH.

## 7. Next work (per the 2026-07-23 sequencing decision)

With Sparkplug B (K3) closed, review the **parked connectivity backlog** (`docs/connectivity-coverage-map.html`): SINUMERIK-without-OPC-UA → Softing/Deltalogic gateway; Okuma THINC (out of edge-only scope → MTConnect); EZSocket + HEIDENHAIN RemoTools (edge-side SDKs, viable); BACnet (real Energy-Management ask — check Modbus-first). Nothing was to be built there until K3 closed. The agreed post-roadmap order remains: Sparkplug B → Contextualization → RBAC+62443 → Linux (deferred).

## 8. Merge

PR #188 is ready. The last-in-chain commit is `fce8365` (r3 bundle); the r3 code is `b3b19a9`. Merge is the user's call.
