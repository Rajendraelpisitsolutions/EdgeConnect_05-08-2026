# Phase 4 — Execution Plan (plan-of-record, frozen)

**Elpis EdgeConnect** · target `v0.3.0` · 9-week scope · two committed customers

> Companion to `docs/ARCHITECTURE_BLUEPRINT.md`, `docs/PHASE1_EXECUTION_PLAN.md`,
> and `docs/PHASE3_EXECUTION_PLAN.md`.
>
> **Status: FROZEN.** Three architectural review passes preceded this document
> (logged in commits `510e92a`, `ce2fc0c`, `2ece6f1`, `2b79133`). Further
> architectural change waits for real customer feedback after Customer A ships.
> See §11 "Architecture freeze policy".

---

## 1. Objective

Two simultaneous customer commitments drive Phase 4:

| Customer | Deliverable | Calendar target |
|---|---|---|
| **A** | Production-ready Fanuc FOCAS2 connector to MQTT (EREMOS V2) | **End of week 2** |
| **B** | OPC UA Server endpoint exposing Modbus + S7 (+ ABB) data, with Connectivity Studio UI for operations | **End of week 9** |

Customer B explicitly does **not** consume EREMOS V2. EdgeConnect must therefore
stand alone as an industrial connectivity platform with its own operator
visibility — that's what reframes Phase 4 from "more protocols" to "industrial
interoperability platform with a configuration + diagnostics UI."

---

## 2. Scope

| In scope | Out of scope (deferred / non-goal) |
|---|---|
| FOCAS2 production-ship (validation, docs, sample config) | FOCAS2 simulator (none publicly available) |
| OPC UA Server sink, `OPCFoundation/UA-.NETStandard` | Historical Access service, Alarms & Conditions service |
| S7 source adapter (Sharp7), S7-300 + S7-1200 + S7-1500 | S7 symbol-table / TIA Portal import (Phase 4.5) |
| ABB validate-or-build (Modbus path if AC500; OPC UA Client if AC800M) | EtherNet/IP, TwinCAT, PROFINET, BACnet (Phase 5) |
| `TagSemantics` registry + asset hierarchy on configs | Per-tag deadband / sampling-rate hints (defer until consumer exists) |
| `Quality` + `EngineeringUnit` on `CanonicalDataPoint` | OPC UA Events, Methods, Subscriptions write-back (Phase 5) |
| License module catalog + DI-level enforcement | Per-deployment override of OPC UA metadata derivation (premature abstraction) |
| Connectivity Studio: config + tag browser + diagnostics + SCADA session visibility | Dashboards, charts, historian, alarms, analytics — see §3 Non-goals |
| OPC UA NamespaceUri versioning (`urn:elpis:edgeconnect:v1`) | Multi-namespace OPC UA Server (one URI for v1 lifetime) |

---

## 3. Non-goals (explicit)

EdgeConnect is **not**:

- a **historian** — EREMOS V2 owns time-series storage. EdgeConnect's S&F buffer is a forwarding queue, not a long-term store.
- a **dashboarding platform** — Connectivity Studio shows operational health, never time-series charts or KPI tiles.
- an **analytics engine** — no aggregations, no derived metrics, no rule-based computation on data in flight.
- an **alarm engine** — `Quality=Bad` is propagated, but no alarm logic, no acknowledgement workflow, no notification surface.
- a **scripting / workflow engine** — transforms are pre-defined steps from the Phase 1 catalog; no operator-authored code.
- a **SCADA / HMI** — Connectivity Studio is for commissioning and operations, not real-time process viewing.

**Why this matters:** every non-goal protects the EREMOS V2 separation and prevents
scope drift. If a feature request looks like it lands in a non-goal, the answer is
"that's an EREMOS V2 concern, or out of v1 scope."

---

## 4. Architecture freeze — what was locked

These were decided across three review passes and are now immutable for Phase 4:

| Decision | Choice | Where it lives |
|---|---|---|
| OPC UA library | `OPCFoundation/UA-.NETStandard` (dual GPL-2 / RCL) | §6.H |
| OPC UA licensing | Commercial via OPC Foundation membership (procurement starts week 1) | §10 risks |
| OPC UA MVP security | `SecurityMode=None` + Anonymous; schema accepts Sign/Encrypt/Username/Cert from day 1 | §6.H, §6.K |
| OPC UA NamespaceUri | `urn:elpis:edgeconnect:v1` — reserve `:v2` for future shape evolutions | §6.H.0 |
| OPC UA NodeId shape | `ns=2;s={gatewayId}/{sourceId}/{stableTagId}` — readable + stable via `StableTagId` decoupled from display name | §6.H.0 |
| OPC UA BrowsePath | Template-driven via `OpcUaServerConfiguration.namespace.browsePathTemplate` | §6.H.0 |
| S7 library | `Sharp7` (LGPL — safe for dynamic linking) | §6.I |
| Tag metadata shape | `TagSemantics` (per-tag) **separate from** asset hierarchy (per-gateway / per-source) | §6.G.6 |
| Quality model | 3-state enum: `Good` / `Bad` / `Uncertain` on `CanonicalDataPoint` | §6.G.5 |
| License modules | Per-protocol + per-sink + connectivity-studio + future historian-bridge — DI-enforced at registration | §6.G.7 |
| UI framework | ASP.NET Core minimal API + **Blazor Server** in one host process | §6.M |
| UI contract rule | Blazor consumes Management REST API only; no direct DI service access | §6.M |
| Adapter capability flags | Existing `SourceCapabilities` extended with `Browse`, `Discovery`, `Quality`, `Writeback`. New `SinkCapabilities` mirrors. | §6.G.6 |
| Symbol-table discovery (S7 TIA Portal) | **Deferred** to Phase 4.5 milestone N | §6.N |
| `ExpectedUpdateRate` / `IOpcUaNodeMetadataProvider` | **Deferred** until a real consumer exists | §11 |

---

## 5. Scale targets (commits for v0.3.0)

Anchored to existing Phase 1 benchmarks where possible, raised where customers explicitly require it.

| Capability | MVP target | Aspirational | Source of number |
|---|---|---|---|
| OPC UA nodes per server | **5,000** | 10,000 | Customer B's anticipated tag count |
| Concurrent OPC UA client sessions | **10** | 20 | Customer B's MES + 1-2 commissioning tools |
| OPC UA subscriptions per session | **200** | 1,000 | OPC Foundation reference |
| OPC UA publish latency p95 | **≤ 500 ms** | ≤ 100 ms | SCADA refresh rate of 1 Hz typical |
| MQTT sustained publish rate | **≥ 300 msg/s** | 1,000 msg/s | Phase 3 4-hour soak measured 282 msg/min ≈ 4.7 msg/s avg; headroom plenty |
| Tags per gateway (total across routes) | **10,000** | 50,000 | Phase 1 buffer benchmark room |
| Routes per gateway | **20** | 100 | Phase 1 routing engine measured |
| End-to-end source-to-broker latency p95 | **≤ 500 ms** | ≤ 100 ms | Customer B SCADA refresh tolerance |
| RSS at scale-target load | **≤ 250 MB** | ≤ 150 MB | Phase 3 soak measured 66 MB at pilot rates |
| Restart resumption time (S&F drain after host restart with N points queued) | **≤ 30 s for 10,000 points** | ≤ 5 s | Phase 1 deferred benchmark target |

**Acceptance gate**: Customer A's soak (week 2) and Customer B's soak (week 9)
each measure against these targets. Failures → triage; not all targets block ship,
but RSS / publish-delivery / restart-resumption are hard gates.

---

## 6. Milestones

```
Week  1     G          FOCAS2 production-ship
Week  1     —          OPC UA Foundation membership procurement starts
Week  2     G.5        Quality + EngineeringUnit on CanonicalDataPoint
Week  3     G.6 + G.7  TagSemantics + asset hierarchy + license modules
Week  4-6   H          OPC UA Server MVP                      | M.1 parallel
Week  6-7   I          S7 source (Sharp7)                     | M.2 parallel
Week  7     J          ABB validate-or-build
Week  8     K          OPC UA Server security
Week  8-9   L          Customer B pilot soak
Week  9     M.3        Preview surfaces (namespace / topic preview)
Phase 4.5   N          S7 symbol discovery (deferred)
```

### G — FOCAS2 production-ship (3-5 days)

**Files:**
- `docs/samples/gateway-focas2-cnc.json` — sample mirroring `gateway-modbus-s7-1200.json` shape
- `docs/samples/README.md` — FOCAS2 walkthrough: 8-handle limit, Fanuc DLL deployment + per-gateway licensing
- `docs/adapter-sdk/focas2-adapter.md` — operator guide
- `shared-knowledge/contracts/cnc-vocabulary.md` — standard CNC tag names + units (feed_rate/mm-min, spindle_rpm/rpm, alarm_active/bool, cycle_time/s, parts_count, mode/string, etc.) — reusable across FOCAS2, MTConnect, S7-with-CNC-on-Modbus
- `docs/protocol-certification-matrix.md` — initial entry for FOCAS2

**Definition of done:** 4-hour soak against Customer A's actual FOCAS2 PLC passes all 5 KepServer criteria; customer signs off on sample config matching their tag layout; deployment doc covers DLL licensing.

**Dependency:** real PLC access (on-site or remote) by end of week 1.

### G.5 — `Quality` + `EngineeringUnit` on `CanonicalDataPoint` (1 week)

**Files:**
- `src/ElpisEdgeConnect.Core/Model/Quality.cs` — `enum Quality { Good, Bad, Uncertain }`
- `src/ElpisEdgeConnect.Core/Model/CanonicalDataPoint.cs` — new fields: `Quality Quality` (default `Good`), `string? EngineeringUnit`
- `src/ElpisEdgeConnect.Core/Model/CanonicalDataPointFactory.cs` — accepts engineering unit at config time, propagates to every point; Quality defaults `Good`, overridden to `Bad` when scan fails or source is degraded, `Uncertain` when value is stale past 3× scan rate
- All 4 source adapters (`Modbus`, `FOCAS2`, `MTConnect`, future S7) — populate Quality on each emit
- Tests: every adapter pins Good-on-success / Bad-on-failure / Uncertain-on-stale paths

**Definition of done:** full gate green (1,265+ tests); canonical points carry quality + unit through routing → buffer → sinks; MQTT sink Batch-mode JSON includes both fields; existing PerTag mode unchanged (single-value payload).

### G.6 — TagSemantics registry + asset hierarchy (1 week)

**Files:**
- `src/ElpisEdgeConnect.Core/Tags/TagSemantics.cs` — new record:
  ```csharp
  public sealed record TagSemantics {
      public required string StableTagId { get; init; }  // stable identity, NOT display
      public required string Name { get; init; }         // operator-readable, mutable
      public string? DisplayName { get; init; }
      public string? Description { get; init; }
      public string? EngineeringUnit { get; init; }
      public double? HighLimit { get; init; }
      public double? LowLimit { get; init; }
      public bool Writable { get; init; }
      public string? Category { get; init; }       // taxonomy hint (Status, Alarm, Production, …)
      public ValueSemantic? ValueSemantic { get; init; }
      public IReadOnlyDictionary<string, string> Hints { get; init; } = new Dictionary<string, string>();
  }

  public enum ValueSemantic { Analog, Digital, Counter, State, Text }
  ```
- `src/ElpisEdgeConnect.Core/Configuration/GatewaySettings.cs` — add `SiteId`, `Site`, `AreaId`, `Area`
- `src/ElpisEdgeConnect.Core/Adapters/SourceConfiguration.cs` — add `LineId`, `Line`, `AssetId`, `AssetClass`
- `CanonicalDataPoint.Metadata` — carries Site/Area/Line/AssetId/AssetClass to sinks (no new fields, uses existing dict)
- Each protocol's tag definition gains a `Semantics: TagSemantics` field; `ISourceAdapter.BrowseTagsAsync` returns these
- `src/ElpisEdgeConnect.Core/Adapters/SourceCapabilities.cs` — extend flags enum with `Browse`, `Discovery`, `Quality`, `Writeback`
- `src/ElpisEdgeConnect.Core/Adapters/SinkCapabilities.cs` — new flags enum: `Push`, `Pull`, `Browsable`, `SessionTracking`, `Subscription`

**Definition of done:** sinks can query `TagSemantics` for any tag; OPC UA Server (H) builds nodes from them without protocol awareness; existing tests stay green.

### G.7 — License module catalog + DI enforcement (1 day, lands with G.6)

**Files:**
- `docs/licensing/module-catalog.md` — canonical module-key catalog:

  | Module key | Tier | Notes |
  |---|---|---|
  | `core-runtime` | base | Required, always-on with valid license |
  | `sink-mqtt` | standard | MQTT publish |
  | `sink-opc-ua-server` | premium | OPC UA Server endpoint |
  | `source-modbus-tcp` | standard | |
  | `source-focas2` | premium | Requires Fanuc DLL |
  | `source-mtconnect` | standard | |
  | `source-s7` | premium | Sharp7-backed |
  | `source-opc-ua-client` | premium | Future |
  | `connectivity-studio` | premium | Management UI |
  | `historian-bridge` | premium | Future |
- `Add{Protocol}SourcesFromGatewayConfig` extensions — at DI registration, check `ILicenseManager.IsModuleEnabled(moduleKey)`; if disabled and config requires it, log `LICENSE.MODULE_NOT_ENABLED` and skip registration for that source (per-adapter isolation, Locked Decision #10)
- Test: a config with a Modbus source under a license missing `source-modbus-tcp` results in the host starting without that source registered, with the other sources continuing

**Definition of done:** module-disabled-but-configured scenarios don't crash the host; missing-module errors surface clearly in logs and diagnostics.

### H — OPC UA Server MVP (3 weeks, weeks 4-6)

**New project:** `src/ElpisEdgeConnect.Sinks.OpcUaServer/`

**Library licensing posture (LOCKED):** `OPCFoundation/UA-.NETStandard`
is dual-licensed (GPL-2.0 OR OPC Foundation Reciprocal Community
License). We develop and unit-test against the GPL-2.0 NuGet — no
procurement gate on H.1 / H.2 / H.3. **Binary distribution to
customers requires OPC Foundation Corporate membership (RCL terms)**;
that's the ship-gate for Milestone L, not for development. The
membership procurement starts in week 1 and runs in parallel. See
`docs/licensing/module-catalog.md` § Third-party library licensing for
the full posture.

**H.0 — Contract design (days 1-3, no implementation yet):**
- Settle `OpcUaServerConfiguration` shape — endpoint URL, namespace template, security block (full schema even though only None implemented)
- Confirm NodeId format `ns=2;s={gatewayId}/{sourceId}/{stableTagId}`
- Confirm NamespaceUri `urn:elpis:edgeconnect:v1`
- Author `shared-knowledge/contracts/opcua-namespace-policy.md` — consumer-facing compatibility contract

**H.1 — Core implementation:**
- `OpcUaServerSinkAdapter.cs` — implements `ISinkAdapter`; on publish, updates the corresponding OPC UA node's value + status from `Quality` mapping (Good → Good, Bad → BadCommunicationError, Uncertain → UncertainLastUsableValue)
- `OpcUaAddressSpaceBuilder.cs` — static build from union of all configured tag definitions at host start; consumes `TagSemantics` for Description / DisplayName / EURange / EngineeringUnits / AccessLevel
- `OpcUaNodeIdResolver.cs` — stable NodeId derivation
- `OpcUaSessionTracker.cs` — captures session lifecycle events for diagnostics surface

**H.2 — Diagnostics + metrics:**
- `SinkSessionSummary` record in Core (protocol-agnostic — generic enough to surface MQTT broker-side / HTTP keepalive in the future; OPC UA Server is the v1 producer). Fields: SessionId, SessionName, ClientApplicationUri, ClientIpAddress, ConnectedAtUtc, LastActivityUtc, UserTokenType, SubscriptionCount, MonitoredItemCount.
- `ISinkSessionHealthSink.RecordActiveSessions(...)` push seam (mirrors `IBufferHealthSink`)
- `SinkHealthSnapshot.ActiveSessions` — new optional property
- Prometheus metrics: `opcua_active_sessions`, `opcua_monitored_items_total`, `opcua_subscriptions_total`, `opcua_publish_latency_seconds`, `opcua_node_count`, `opcua_rejected_connections_total`, `opcua_cert_failures_total`, `opcua_session_disconnects_total`

**H.3 — Integration test:**
- `tests/ElpisEdgeConnect.Integration.Tests/OpcUaServerEndToEndTests.cs` — in-process OPC UA client connects, browses the address space, subscribes to a tag, confirms updates arrive within scan rate; kills the upstream source, verifies clients see `Bad` quality within 3 scan periods

**Definition of done:** UaExpert browses & subscribes successfully; quality propagation verified; active sessions visible on diagnostics; sample `docs/samples/gateway-modbus-opcua.json` works end-to-end; commercial license confirmed acquired before any external customer demo.

### I — S7 source adapter (2 weeks, weeks 6-7)

**New project:** `src/ElpisEdgeConnect.Sources.S7/`

**Files:**
- `S7SourceAdapter.cs` — mirrors `ModbusTcpSourceAdapter` shape (scan-planner, retry/backoff, circuit breaker)
- `S7ConnectionManager.cs` — wraps Sharp7's `S7Client`
- `S7AddressParser.cs` — accepts: `DB10.DBX0.0` (bit), `DB10.DBW2` (word), `DB10.DBD4` (dword), `DB10.DBB1` (byte), `M10.5`, `I0.0`, `Q4.2`, `T5`, `C3`
- `S7Datatype.cs` — bool, byte, word, dword, real, dint, sint, usint, uint, udint, string (S7 length-prefixed), DTL
- `Configuration/S7SourceConfiguration.cs` — host/port/rack/slot/`optimizedDbAccess` flag/connectionType (PG/OP/Basic)/tagDefinitions
- `tests/ElpisEdgeConnect.Sources.S7.Tests/` — unit tests
- `tests/ElpisEdgeConnect.Integration.Tests/S7SimulatorFixture.cs` — Snap7-server-backed fixture (in-process; no Docker)
- `tools/S7AddressProbe/` — operator helper (decode under every datatype/endian to discover correct config)
- Real-PLC validation against Customer B's S7-300 AND S7-1200

**Definition of done:** Snap7-server integration tests green; soak against both S7-300 and S7-1200 → all 5 KepServer criteria pass; Optimized + Non-Optimized DB modes both work; `S7SourceConfiguration.Capabilities` flags `Polling | Browse | Quality` (Browse via simple validate-and-probe, NOT full symbol discovery — that's milestone N).

### J — ABB validate-or-build (0.5-2 weeks, week 7)

**Decision tree (resolved in week 4 by customer correspondence):**

- **AC500 / Compact PLC / AC800M with Modbus option:** validate via existing Modbus adapter. ~3 days. Doc update only.
- **AC800M without Modbus / 800xA:** build `src/ElpisEdgeConnect.Sources.OpcUaClient/` (separate sink-mode-swapped instance of OPC UA library). ~2 weeks. Reusable for any OPC UA-speaking PLC.

**Definition of done (either path):** real-PLC validation against Customer B's hardware; soak passes all 5 KepServer criteria; documented in `docs/protocol-certification-matrix.md`.

### K — OPC UA Server security hardening (1 week, week 8)

Drop-in on the schema and seams already designed in H.0. **No code changes required outside the OPC UA Sinks project.**

**Files:**
- `OpcUaCertManager.cs` — generate self-signed application cert on first start; load existing cert if `applicationCertificatePath` set; rotate procedure documented
- `OpcUaServerSinkAdapter.cs` — implement `SecurityMode=Sign`, `SecurityMode=SignAndEncrypt`; implement `UserName` and `Certificate` token policies
- `docs/ops-runbook.md` — OPC UA cert lifecycle, trust list management, rejected-clients folder operations

**Definition of done:** Customer B's MES connects with SecurityMode=Sign+Encrypt + Username auth; cert rotation procedure dry-run validated; trusted-clients folder behavior matches OPC UA spec.

### L — Customer B pilot soak (2-3 days, weeks 8-9)

Same harness shape as the Modbus 4-hour pilot; extended with OPC UA-specific health: active session count, subscription count, write attempts. Run against their PLC fleet → OPC UA Server endpoint → their MES.

**Definition of done:** all 5 KepServer acceptance criteria pass for at least 4 hours; no spurious session disconnects; no cert failures; quality propagation observed when a PLC connection is briefly disrupted.

### M — Connectivity Studio (4-5 weeks, weeks 2-7 parallel)

**New project:** `src/ElpisEdgeConnect.Management/` (ASP.NET Core minimal API + Blazor Server in the same host)

**Architectural rule (locked):** Blazor pages consume the Management REST API only. No direct DI service access from Razor. Enforcement test: assembly-load check rejecting direct project references from the Razor project to `ElpisEdgeConnect.Core` or `ElpisEdgeConnect.Host`.

**Design philosophy (locked):** commissioning-first. Optimize for the integrator wiring a brand-new PLC, not the executive viewing rollup KPIs. NO dashboards, NO time-series charts, NO historian views — those are EREMOS V2's job.

**M.1 — Read surface (weeks 2-3):**
- `GET /api/v1/routes` + `GET /api/v1/routes/{id}` — full `RouteHealthSnapshot` (sources + sinks + buffer)
- `GET /api/v1/sinks/{id}/sessions` — for OPC UA Server sink, live session list (from H.2)
- `GET /api/v1/diagnostics/recent-errors` — bounded event log
- `GET /api/v1/config/current` — read-only view
- Blazor pages: **Overview** (route status grid, broker reachability, OPC UA endpoint reachability), **Sources**, **Sinks** (with OPC UA session list), **Routes** (buffer depth live), **Diagnostics**

**M.2 — Write surface (weeks 4-5):**
- `POST /api/v1/config/draft` / `apply` / `rollback`
- Blazor "Add source" wizards: Modbus / FOCAS2 / S7. Each hosts the relevant commissioning tool inline (`ModbusByteOrderProbe`, `S7AddressProbe`)
- "Browse tags" / "Try read" / "Try subscribe" buttons exercising live adapters without writing to current.json

**M.3 — Preview surfaces (week 6-7):**
- "OPC UA namespace preview" — given config, render the address-space tree
- "MQTT topic preview" — given config, list every topic that would be published

**Definition of done:** non-technical integrator wires a brand-new Modbus PLC end-to-end to the OPC UA Server without touching JSON; live SCADA session list shows their MES's connection in real time; management API exercised independently via `curl` (no Blazor coupling).

### N — S7 symbol discovery (DEFERRED, Phase 4.5)

Optional follow-up. TIA Portal export parsing + symbol-table read for "browse PLC tags" Kepware-parity feature. Not blocking customer B; ~1-2 weeks when scheduled.

---

## 7. Sub-system additions outside milestones (lightweight)

| Item | Location | When |
|---|---|---|
| `docs/protocol-certification-matrix.md` | living doc, updated per protocol validation | Initial entry with G; extended through K and J |
| `docs/test-strategy/protocol-simulators.md` | per-protocol simulator commitment | With I (when Snap7 fixture lands) |
| `shared-knowledge/contracts/opcua-namespace-policy.md` | consumer-facing OPC UA compatibility contract | H.0 |
| `docs/adapter-sdk/build-your-own-protocol-adapter.md` | living protocol-SDK checklist | After I (S7) lands — patterns proven across 3 protocols |
| `src/ElpisEdgeConnect.Management/README.md` | commissioning-first design principles, API-first rule | M.1 |

---

## 8. Risk register

| # | Risk | Mitigation |
|---|---|---|
| 1 | OPC Foundation membership lead time (4-8 weeks) | Procurement starts week 1. Internal development continues under GPL-2 fallback; **no external commercial deployment until membership is active**. |
| 2 | OPC UA address-space contract drift across customers | Settle namespace template at H.0 before code; document in `opcua-namespace-policy.md`; gate against the contract in tests. Same discipline as the EREMOS V2 topic-shape coordination from Phase 3. |
| 3 | Sharp7 + S7-300 non-optimized DB edge cases | 1-day buffer in milestone I. If blocking, swap to S7NetPlus is ~2-3 days; both libraries support the same address syntax. |
| 4 | ABB AC800M-no-Modbus discovered late in week 7 | Already budgeted as the J fork. If realized, week 7+ shifts +1.5 weeks; Customer B target moves to week 10. |
| 5 | Real-PLC access slippage (FOCAS2 week 1, S7 week 6-7, ABB week 7) | Schedule customer access **now**. Without access, soak-validation milestones slip; UI/code milestones don't. |
| 6 | FOCAS2 has no public simulator → CI relies on customer hardware | Documented in `protocol-simulators.md` and sales material. Unit tests cover everything mockable; E2E E2E gated on customer access. |
| 7 | Scope drift toward dashboarding / analytics in UI | Non-goals (§3) are explicit. Every UI feature request gets validated against "is this in the non-goal list?" before scoping. |

---

## 9. Compatibility policy commitments

For customers integrating SCADA / MES against this gateway:

**OPC UA (covered in detail in `shared-knowledge/contracts/opcua-namespace-policy.md`):**
- `NamespaceUri = urn:elpis:edgeconnect:v1` is stable for the lifetime of v1
- `NodeId` is derived from `{gatewayId}/{sourceId}/{stableTagId}`; **the `stableTagId` is the operator-renamable display-decoupled identity**. Renaming a tag's display name (`spindle_rpm` → `spindle_speed`) does NOT change its NodeId.
- `BrowsePath` follows the configured `browsePathTemplate`; **changes to the template are MINOR version bumps** with migration guidance. Operators should pin a template per gateway lifecycle.
- `DisplayName` / `Description` are freely changeable — never API-stable.
- Removing a tag from config = removing the NodeId; clients see `BadNodeIdUnknown`. Operators should subscribe to a deprecation notice (future feature).

**MQTT topics:** topic template stays stable per Phase 3's `eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}` contract. Same versioning approach if it evolves.

---

## 10. Definition of overall Phase 4 done

- ✅ Customer A: 4-hour FOCAS2 soak passes all 5 KepServer acceptance criteria; deployment doc covers Fanuc licensing.
- ✅ Customer B: 4-hour combined-source soak (Modbus + S7 + ABB if Modbus, OPC UA Client if not) → OPC UA Server → their MES, all 5 KepServer criteria pass.
- ✅ Full test gate green: every existing test still passes; all new milestones add tests that exercise their respective surfaces.
- ✅ Connectivity Studio: non-technical user wires a new Modbus PLC to OPC UA Server end-to-end without JSON edits.
- ✅ `protocol-certification-matrix.md` lists FOCAS2, Modbus, S7-300, S7-1200, Customer B's ABB, OPC UA endpoint as certified.
- ✅ `opcua-namespace-policy.md` committed and acknowledged by Customer B.
- ✅ OPC Foundation membership active.
- ✅ License module catalog enforced at DI registration; missing-module scenarios surface cleanly.

---

## 11. Architecture freeze policy

Three review passes preceded this document. Further deep architectural change is paused until:

1. Customer A's FOCAS2 ship completes (end of week 2). Customer feedback informs whether semantic layering / OPC UA shape / module catalog need revision.
2. Customer B's pilot completes (end of week 9). Operational pain reveals what was speculative vs essential.

**Allowed during Phase 4:** small additive changes (new modules, new metrics, new sample configs) that don't violate any Blueprint LOCK and don't break the API surface contracts in §9.

**Not allowed during Phase 4:** new abstraction layers, contract shape changes, renamed types in public APIs, additional speculative metadata fields without a real consumer, scope creep toward §3 non-goals.

This freeze is the same discipline that closed Phase 1: lock the contracts, build to them, revisit with real-world data.

---

## 12. Open commercial decisions (require operator input)

These do not block engineering kickoff but should be answered before week 4:

1. **License module → product edition mapping.** Which modules ship in `base`, `standard`, `premium`? §6.G.7 has a starter catalog; final tiering is yours.
2. **OPC UA Foundation membership tier.** Logo / Corporate / End User — affects annual cost and benefits package. Procurement should know which level to apply for.
3. **Customer B's exact ABB model.** Determines whether J is 3 days (Modbus validate) or 2 weeks (OPC UA Client build).
4. **Pricing position vs Kepware / Matrikon.** Indirectly affects how aggressively Tier-2 UI features (Tag Discovery / TIA Portal import / etc.) get prioritized post-Phase-4.

---

**End of plan-of-record. Implementation begins.**
