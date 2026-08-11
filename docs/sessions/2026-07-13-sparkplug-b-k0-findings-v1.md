# Sparkplug B — K0 Findings & Exit Record (v1)

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** **K0 COMPLETE — exits reviewed and accepted (2026-07-13).** All three
tracks (WS4, WS5, WS3+WS8) are accepted on the corrective evidence; the **K1 gate is
released**. Production hardening is carried to K1/K3/K4 (see "Carry-forwards" below) —
not a reason to hold K0.
**Plan:** `2026-07-13-sparkplug-b-sink-plan-v2.3.md`. **ADR:** 0036.

**Evidence provenance (IMPORTANT):** Executable K0 test code is preserved in the
**closed, unmerged evidence PR #177 at commit `1ef4eb5`** (`feat/sparkplug-b-ws1-spike`,
deleted after close). The test paths referenced below are **evidence-branch paths and
are NOT present on `master`** — Sparkplug-specific prototypes stayed in the test
project so Core remains protocol-agnostic. The `k0-evidence.diff` (`962413a..1ef4eb5`)
captures the full K0 patch.

> **17 K0 tests green** (WS5: 7, WS3+WS8: 8, WS4: 2), plus the 15 WS1/WS2/WS7 tests.

## WS4 — MQTTnet QoS-0 completion semantics  *(deterministic evidence)*
**Probe:** `tests/…/Sinks.Mqtt.Tests/QoS0BoundaryProbeTests.cs` (2 tests, live
Mosquitto). The during-send window uses a **controllable TCP relay**, not a flaky
broker-kill loop:
```
MQTTnet publisher → ControllableTcpRelay → real Mosquitto
                                            ↑ verification subscriber (direct to broker)
```
**Proven (deterministic):**
- QoS-0 publish to a live broker → `IsSuccess`, **`PacketIdentifier == null`** (no
  packet id, no PUBACK).
- **Before:** relay forwards → the direct subscriber receives.
- **During:** relay black-holes client→broker (reads+discards, socket open) → the
  QoS-0 publish still returns success with no packet id, **yet the subscriber receives
  nothing** within a bounded wait.
- **After:** relay closed → a later publish is an observable failure (throws).

| Send window (QoS 0) | Client result | Broker receipt |
|---|---|---|
| forwarding | success, no packet id | received |
| **black-holed during send** | **success, no packet id** | **NOT received** |
| transport gone | throws | not sent |

**Exit (revised wording):** `PublishResult.Success` for the Sparkplug sink means
**"MQTTnet `PublishAsync` completed with no observable local error"** — NOT broker
receipt (the relay proves success can coincide with non-receipt). Confirms the
`LocalTransport` ack boundary (ADR-0036 Rule 1); store-and-forward retries only
*observable* failures.

## WS5 — Crash-safe `bdSeq` reservation  *(revised: dedicated SQLite identity store)*
**Prototype + tests:** `tests/…/Core.Tests/Sparkplug/K0/BdSeqStoreTests.cs` (7 tests).
Corrects all four first-review gaps.

**Exit (revised, locked as recommended):** `bdSeq` is reserved in a **dedicated,
gateway-level Sparkplug identity SQLite store** (`data/sparkplug/identity-state.db`,
NOT the per-route snapshot store — identity is scoped to `broker+group+edge_node` and
must survive route recreation), keyed by **typed, normalized identity columns** (no
lossy filename mapping). Reservation runs in a **serialized `BEGIN IMMEDIATE`
transaction that COMMITS before the value is returned** (before CONNECT construction);
**commit failure throws and prevents CONNECT**; **corrupt/unreadable state fails
closed** (throws, never resets to 0). A committed-but-unused value is **skipped after
restart, never reused**. Clustered/standby ownership still requires a lease (deferred).

**Proven:** initial/increment/restart; committed-before-return; commit-before-CONNECT
skip; **two store instances concurrently reserving** unique values (SQLite write lock,
not an in-memory lock); **corrupt state fails closed** (throws, no reset to 0);
**injected pre-commit failure** returns no value and doesn't advance; identities
independent. (The absolute file-`File.Move` durability claim is withdrawn — the exit
specifies the SQLite transaction mechanism instead.)

## WS3+WS8 — Route cardinality + identity validation  *(rules accepted, tightened)*
**Prototype + tests:** `tests/…/Core.Tests/Sparkplug/K0/RouteValidatorTests.cs` (8 tests).

**Four rules + the review's constraints:**
1. **Duplicate Edge-Node descriptor** — compared on a **normalized broker endpoint**
   (host + effective port + TLS), so `BROKER.EXAMPLE.COM:1883` == `broker.example.com`
   (default 1883).
2. **>1 route per Sparkplug Edge Node** (real shape: one destination referenced by two
   routes).
3. **Duplicate Client ID per broker endpoint across the MQTT FAMILY** (`mqtt` +
   `sparkplug-b`) — a collision with a non-MQTT protocol (e.g. `opcua-server`) is
   **ignored**.
4. **Each ROUTE feeding the destination must be `StoreAndForward`** (buffer mode is
   route-level; an `InMemory`/`None` referencing route is rejected).
**Active-state:** validation applies to **would-be-active (enabled)** destinations — a
disabled duplicate may exist, but **enabling it fails**.

**Proven:** clean config passes; normalized-endpoint duplicate; same Client-ID +
different endpoint allowed; two-routes-per-node; non-durable referencing route;
Sparkplug+MQTT Client-ID collision; non-MQTT Client-ID collision ignored;
disabled-duplicate-allowed-but-enabling-fails.

**Exit:** enforce all four (with these constraints) at **config validation** (K4);
production reads the real `GatewayConfiguration`.

## Carry-forwards (production hardening — K1/K3/K4, not K0 blockers)
- **Canonical broker identity everywhere.** The same typed `BrokerEndpoint` (host +
  effective port + TLS) used by identity/cardinality validation (WS3+WS8) is the
  identity key for the `bdSeq`/alias store (WS5) — not arbitrary broker strings. (K3/K4)
- **`bdSeq` state validation fails closed.** Schema/runtime validation rejects
  negative/overflowing persisted values; corruption fails closed. (K3)
- **Explicit SQLite durability mode** for the identity store, not environmental
  defaults. (K3)
- **Aliases and `bdSeq` share the Edge-Node-scoped identity store**, in **separate
  tables/records**. (K3)
- **Permanent WS4 relay test** replaces the fixed 200 ms delay with an explicit
  "all relay bridge sockets closed" completion signal (remove timing sensitivity). (K1 test)
- **Production validators use real typed config** — route-enablement and typed enums
  (protocol/buffer-mode), not the spike's strings. (K4)

## Gate status
- ✅ Docs-only PR **#178 merged** (accepted design on master).
- ✅ **K0 ACCEPTED** (2026-07-13) — 17 green tests; all three exits reviewed and
  accepted. **K1 gate released.**
- ⏭ **Sequence:** merge PR #179 → close PR #177 (unmerged) → delete
  `feat/sparkplug-b-ws1-spike` → cut `feat/sparkplug-b-k1` from updated `master`.
