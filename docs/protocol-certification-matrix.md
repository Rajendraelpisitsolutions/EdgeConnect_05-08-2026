# Protocol Certification Matrix

**Status:** living document — updated as each protocol completes
real-hardware validation.

This table is the single source of truth for what hardware EdgeConnect
has been validated against. Sales / support / onboarding teams reference
this when scoping deployments. If a customer's hardware isn't listed,
the protocol may still work, but is not yet "certified" and requires
real-PLC validation as part of the customer pilot.

Certification levels:

- **Certified** — Validated against this exact model + firmware in a
  4-hour minimum soak run passing all 5 KepServer-benchmarked acceptance
  criteria. Production deployment supported.
- **Pilot** — Validated end-to-end against an in-process simulator and
  in unit + integration tests. Real-hardware validation pending; one
  customer is currently piloting against real hardware.
- **Lab** — Validated against simulator only. No real-hardware run yet.
- **Planned** — Adapter not yet built.

---

## Source protocols

| Protocol | Hardware | Firmware / version | Status | Last validated | Notes |
|---|---|---|---|---|---|
| **Modbus TCP** | Siemens S7-1200 (with `Modbus_TCP_Server`) | TIA Portal V17 | **Certified** | 2026-04-25 | 4-hour pilot soak, 205k transactions, 99.994% publish delivery |
| Modbus TCP | pymodbus simulator | pymodbus 3.6-3.7 | Certified (CI) | continuous | Integration test fixture; CI gate |
| Modbus TCP | ABB AC500 | — | Planned | — | Validate once Customer B confirms model |
| Modbus TCP | Generic vendors | — | Lab | — | Any conformant Modbus TCP slave; user validates per device |
| **FOCAS2** | Fanuc 30i-B | typical | Pilot | — | Customer A pilot scheduled (week 1 of Phase 4). FOCAS2 has no public simulator — real hardware required. |
| FOCAS2 | Fanuc 0i-TF | — | Planned | — | Same adapter; awaiting customer access |
| FOCAS2 | Fanuc 31i / 35i | — | Planned | — | Same adapter |
| **MTConnect** | MTConnect agent | 1.8.x | Pilot | — | E2E integration test against in-process agent; real-machine validation pending customer |
| **S7** | Siemens S7-1200 | TIA Portal V17 (Optimized DB) | Lab (adapter built; impl complete) | — | Milestone I (Phase 4 week 6-7). Adapter via Sharp7 (MIT-licensed). Real-PLC validation pending Customer B hardware access. |
| S7 | Siemens S7-300 | non-optimized DB | Lab | — | Same adapter; non-optimized DB mode tested in unit tests |
| S7 | Siemens S7-1500 | Optimized DB | Lab | — | Same adapter; OptimizedDbAccess flag set in config — operator must enable PUT/GET permissions on the CPU |
| **OPC UA Client** | (any conformant server) | — | Planned | — | Milestone J fork — only built if Customer B's ABB needs it |

## Sink protocols

| Protocol | Target | Version | Status | Notes |
|---|---|---|---|---|
| **MQTT** | EREMOS V2 broker | Mosquitto 2.x | Certified | 4-hour pilot soak; 67k publishes; auto-reconnect verified across wifi outages |
| MQTT | Generic broker | MQTT 3.1.1 / 5 | Certified (CI) | MQTTnet 4.3.7 integration tests |
| **OPC UA Server** | (any conformant client) | — | Lab (all surfaces wired) | Milestones H + K. Sign / SignAndEncrypt + UserName / Certificate auth all implemented. Namespace + NodeId stability contract: `shared-knowledge/contracts/opcua-namespace-policy.md`. Cert / trust list operator workflow: `docs/ops-runbook.md` § 6. Real-client soak validation pending Customer B pilot (Milestone L). |

---

## What "Certified" requires

A protocol+hardware combination earns Certified status when ALL of:

1. Soak runner ran continuously for ≥4 hours against the hardware.
2. All 5 KepServer-benchmarked acceptance criteria passed:
   - Source delivery ≥ 99.9%
   - Publish delivery ≥ 99.9%
   - RSS final ≤ 150 MB
   - RSS growth (post-warm-up) ≤ 20%
   - Avg CPU per-core ≤ 5%
3. Outage recovery verified — for sinks: broker disconnect + reconnect
   with zero data loss. For sources: device disconnect + reconnect with
   correct Quality=Bad emission during outage and Quality=Good resumption.
4. Sample `gateway.json` config committed under `docs/samples/`.
5. Operator docs cover deployment + licensing specifics (where applicable
   — e.g., Fanuc DLL licensing).

Customer-specific certifications (e.g., "S7-300 with Customer X's
non-standard DB layout") may be tracked privately. This document covers
the publicly-supportable matrix.

---

## See also

- `docs/test-strategy/protocol-simulators.md` — what each protocol uses
  for CI testing (planned)
- `tools/ModbusSoakRunner/README.md` — the harness that produces
  certification soak data
- `shared-knowledge/contracts/cnc-vocabulary.md` — canonical CNC tag
  names every CNC adapter SHOULD emit
- `shared-knowledge/contracts/opcua-namespace-policy.md` — OPC UA Server
  consumer-facing compatibility contract (NodeId stability, BrowsePath
  template, NamespaceUri versioning)
