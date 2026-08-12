# Sparkplug B Sink — Plan (v1)

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** Discussion draft (v1) — first pass; expect a review pass → v2
**Sequence context:** Item 1 of the post-roadmap order
(`2026-07-13-post-roadmap-sequencing-decision.md`). Sparkplug B is the head of the
queue.
**Grounding:** Architecture map produced this session from a full read of the sink
stack (see §2 for cited findings). No code changed.

---

## 1. Why Sparkplug B, and what it is NOT

**Why:** we position as MQTT-first but speak a custom `eremos/{gatewayId}/...`
topic grammar. Sparkplug B is the de-facto industrial-MQTT standard (birth/death
certificates, stateful session awareness, self-describing protobuf payloads) and
is the price of entry for Ignition / Inductive Automation shops and any Unified
Namespace conversation. Cheapest credibility unlock available.

**Explicit non-goal — it does NOT replace the existing MQTT sink.** The
`eremos/...` per-tag topic contract is how **EREMOS V2 ingests**
(`reference_eremos_v2_ingestion`). Sparkplug B is a **second, coexisting**
northbound for third-party / standards-driven consumers. Both sinks can run on the
same gateway, on different routes. This plan adds a sink; it changes nothing about
the EREMOS path.

---

## 2. Verified architecture findings (what we're building on)

**Reuse verdict:** transport plumbing is highly reusable; the payload + session
layer is net-new. Build a **sibling assembly `ElpisEdgeConnect.Sinks.SparkplugB`**,
NOT a mode flag on `MqttSinkAdapter` (mirrors how `MqttPublishMode` kept two
explicit code paths rather than one branchy method).

Reusable as-is / as pattern:
- MQTTnet 4.3.7.1207 client, connect/backoff/reconnect loop, health counters —
  `src/ElpisEdgeConnect.Sinks.Mqtt/MqttSinkAdapter.cs`. MQTTnet carries arbitrary
  `byte[]`, so protobuf payloads fit the existing publish path. LWT
  (`.WithWillTopic/.WithWillPayload`) is supported by MQTTnet but **not currently
  wired** — Sparkplug needs it.
- DI registration triad + license gate — copy
  `src/ElpisEdgeConnect.Host/Adapters/MqttRegistrationExtensions.cs`
  (ResolveInputs → Construct → Build; `ReplaceSinkRegistrationEnumerable` to dodge
  the documented DI deadlock). One line added to `EdgeConnectComposition.cs`.
- `ISinkAdapter` contract (LOCKED) — `Push` capability, `PublishAsync` returns
  `PublishResult`; delivery mode (`AtMostOnce`/`AtLeastOnce`) lives at the route,
  the sink never sees it. `SinkPublisher` wraps publish with retry/drain.
- Stateful-sink precedent — `OpcUaServerSinkAdapter` + `OpcUaSessionTracker`
  (holds session state, declares `SessionTracking`, implements
  `ISessionTrackingSink`). This is the template for Sparkplug's edge-node state.
- Wizard scaffolding — `DestinationProtocolPickerModel` tiles (Available/Pending),
  `MqttSinkWizardModel` (BuildSinkInstance/HydrateFromExisting), `SinkEditRouter`.
- Redaction rules, connection-keys drift guard, test-connection endpoint patterns.

**The six hard fits** (why the session layer is net-new) — all confirmed against
code:
1. **Stateless publish vs. stateful session.** Current `PublishAsync` is
   resolve→serialize→publish→return. Sparkplug needs a connection-scoped state
   machine: NBIRTH on connect, alias table, NDATA/DDATA by alias, NDEATH via LWT,
   per-edge-node `seq` (0–255 wrapping) reset at each rebirth.
2. **Sequence-number retry hazard (the crux — see §4.A).**
   `CanonicalDataPoint.SequenceNumber` is source-scoped monotonic int64 —
   semantically unrelated to Sparkplug's per-node 0–255 wrapping `seq`/`bdSeq`; do
   not reuse it. Worse: `SinkPublisher` **re-publishes the same batch on
   `Success=false`**. Sparkplug `seq` must NOT double-increment on retry, and a
   re-sent NDATA with a stale seq makes the host flag the node stale and demand a
   rebirth. The MQTT PerTag partial-success trick (idempotent latest-wins topics)
   is **invalid** under Sparkplug's ordered-seq contract.
3. **Birth metadata isn't on the canonical point.** NBIRTH/DBIRTH must enumerate
   every metric (name, alias, datatype, ideally unit/properties). The runtime point
   carries `TagName`, `ValueType`, `Unit`, free `Metadata` — but no alias, no
   per-device metric catalogue, no `DeviceInfo` at runtime (only at browse time).
   The sink must learn the metric set (first-seen) or be fed a catalogue
   out-of-band, then own the alias table.
4. **Datatype mapping is lossy.** `CanonicalValueType` has 11 types, no unsigned;
   Sparkplug B has ~20. Mapping collapses (unsigned → signed), `Array`/`Object`
   have no clean scalar equivalent. `ByteArray` is recoverable (Sparkplug Bytes).
5. **LWT/clean-session not wired.** Current setup is `WithCleanSession(true)`, no
   will. Sparkplug mandates a registered NDEATH will (with `bdSeq`) at connect.
6. **Group/EdgeNode/Device IDs** must be derived from
   `GatewayId`/`SourceInstanceId`/`DeviceId` + config — no existing enrichment
   supplies Sparkplug namespace identity.

---

## 3. Proposed shape (backend-first, per S7/MELSEC template)

New assembly `src/ElpisEdgeConnect.Sinks.SparkplugB/`:

- `SparkplugBSinkAdapter : ISinkAdapter, ISessionTrackingSink` — `ProtocolName =
  "sparkplug-b"`, `Capabilities = Push | SessionTracking`. Owns the MQTTnet client
  (reuse the connect/reconnect/health scaffolding) **plus** the Sparkplug session
  state machine.
- `SparkplugSessionState` — per-edge-node: `bdSeq`, `seq` counter (retry-safe;
  assigned at wire-send, see §4.A), alias table, birth-sent flag, known-metric
  catalogue.
- `SparkplugPayloadEncoder` — CanonicalDataPoint(s) → protobuf `Payload`
  (NBIRTH/DBIRTH/NDATA/DDATA/NDEATH). Depends on a protobuf schema lib (§4.B).
- `SparkplugTopicBuilder` — `spBv1.0/{group_id}/{msg_type}/{edge_node_id}/
  {device_id}`.
- `CanonicalToSparkplugTypeMap` — the lossy datatype mapping (§2.4), with explicit
  documented collapse rules.
- `SparkplugBSinkConfiguration : SinkConfiguration` — group id, edge-node id
  strategy, device-id strategy, metric-naming strategy, TLS/auth (reuse MQTT
  fields), primary-host id (optional, §4.D).
- `SparkplugConnectionKeys` + `SparkplugBundleRedactionRules` (copy MQTT pattern).
- License key `LicenseModuleKeys.SinkSparkplugB = "sink-sparkplug-b"` (add, never
  rename — LOCKED once issued).
- Host DI: `SparkplugRegistrationExtensions.cs` +
  `AddSparkplugSinksFromGatewayConfig(...)` wired in `EdgeConnectComposition.cs`.

Studio:
- `DestinationProtocolPickerModel` — add a `sparkplug-b` tile (Pending → Available
  when the wizard ships).
- `SparkplugSinkWizardModel` + `AddSparkplugDestination.razor` (+ test-connection).
- `SinkEditRouter` — add `sparkplug-b` to `RegisteredSinkProtocols`,
  `ProtocolsWithEditWizard`, `ProtocolDisplayNameFor`, `LicenseModuleKeyFor`, and
  the `_sink.ProtocolName` switch.
- **Static HTML mockup of the wizard first**, for sign-off, before wiring any UI
  (per `feedback_static_html_ui_review`).

---

## 4. Architectural decisions

**Decided 2026-07-13** (A, B, D). C and E remain open with proposed defaults —
non-blocking, see §8.

| # | Decision | Chosen |
|---|----------|--------|
| A | S&F vs seq on reconnect | **Replay-then-rebirth** — honor locked S&F (#8); rebirth then drain buffer as DATA with historical in-payload timestamps. |
| B | Protobuf library | **`Google.Protobuf` + our own encoder** — generate C# from the Tahu `.proto`; minimal external-license surface, we own the encoder. |
| D | Primary Host / STATE in v1 | **Publish-only v1 slice** — no STATE subscription; primary-host awareness deferred to v1.x. |

Fork points as originally surfaced (kept for the record):

### A. Sparkplug ordered-seq vs. store-and-forward (the crux)
Store-and-forward is a LOCKED decision (#8), and replay is sequential per sink
(#11). But Sparkplug's `seq` contract assumes a **live, ordered session**: on
reconnect the edge node must **rebirth** (NBIRTH, seq reset), and replaying
hours-old buffered data as NDATA with fresh seq numbers is semantically "live now"
to the host — timestamps in the payload say otherwise, but some hosts treat a seq
gap/rewind as staleness. Decision needed:
- **Option 1 — Replay-then-rebirth:** on reconnect, rebirth, then drain the buffer
  as DDATA/NDATA with historical timestamps in-payload. Preserves the
  no-data-loss guarantee (#7/#8); relies on the host honoring payload timestamps.
- **Option 2 — Live-only Sparkplug:** Sparkplug sink does not replay buffered
  backlog (only live data post-connect); historical continuity is EREMOS's job via
  the MQTT sink. Cleaner Sparkplug semantics, but concedes the store-and-forward
  guarantee **for this sink**, which brushes locked decision #8 — needs explicit
  sign-off that S&F is per-sink-waivable.
- **seq retry-safety (independent of 1/2):** assign `seq` at wire-send, made
  idempotent against `SinkPublisher` re-publish (e.g. the sink treats a retried
  batch as already-sent, or the sink reports `Success` only after true wire-ack so
  the buffer never hands the same batch twice). This must be nailed regardless.

### B. Protobuf library & its license
Sparkplug B payloads are Eclipse Tahu protobuf. Options: (a) Eclipse **Tahu** .NET
library, (b) generate from the Tahu `.proto` via **`Google.Protobuf`** and own the
thin wrapper. Tahu is **EPL-2.0** — pre-launch, dependency licensing has bitten us
before (OPC UA needed the Foundation RCL, `project_opc_foundation_membership`).
**Decision: which library, and is EPL-2.0 acceptable to bundle?** Recommendation
leans (b) — generate from the `.proto`, minimal EPL surface, we own the encoder.

### C. Sparkplug namespace identity mapping
How do `group_id` / `edge_node_id` / `device_id` map onto
gateway/site/source/device? Likely config-driven: `group_id` = site/customer,
`edge_node_id` = gateway, `device_id` = source instance (or per-`DeviceId`).
Needs a default convention + override. Affects the wizard.

### D. Primary Host / STATE support (scope of v1)
Sparkplug's optional Primary Host Application + `STATE/{host_id}` birth/death lets
the edge node know when its consumer is offline (a natural store-and-forward
trigger). Full support is advanced. **Decision: is v1 primary-host-aware, or
v1-slice = publish-only (no STATE subscription)?** Recommendation: **v1 slice =
publish-only**, primary-host awareness deferred (mirrors MELSEC "slice 1 read-only"
scoping).

### E. Metric naming & alias strategy
Sparkplug metric name = canonical `TagName`? `TagPath`? Alias assignment: stable
per (device, metric) across rebirths? Decision drives NBIRTH content and the
first-seen learning logic.

---

## 5. Proposed v1 slice (to be confirmed after §4 decisions)

Following the MELSEC slice precedent — ship something real and narrow:
- **In:** node + device birth/death, NDATA/DDATA by alias, retry-safe seq, the
  datatype map, TLS + user/pass auth, one namespace-identity convention, wizard
  tile + wizard + edit routing, license gate, tests incl. a Sparkplug mock-host
  subscriber.
- **Deferred (v1.x):** primary-host/STATE awareness, DataSet/Template metrics,
  historical backlog replay if §4.A picks Option 2, mutual TLS, metric properties
  beyond unit.

---

## 6. Testing strategy

- Pure-unit: type map, topic builder, payload encoder (decode with the same
  `.proto` and assert structure), seq/bdSeq wrapping + rebirth, alias-table
  stability.
- Broker integration: reuse the Mosquitto harness pattern
  (`Eremos/DedicatedTestBroker.cs`, graceful skip when Mosquitto absent;
  `RequiresMqttBroker` trait excluded in CI).
- **Sparkplug mock-host subscriber** (analog of `EremosV2MockSubscriber`) that
  decodes protobuf, validates the birth→data→death lifecycle and seq ordering, and
  asserts a rebirth on reconnect. This is the real proof the sink is
  spec-compliant.

---

## 7. Plan-trail next steps

1. ~~Get §4 decisions from the user~~ — **done** (A/B/D, see §4 table).
2. **ADRs — done:** `docs/decisions/0035-sparkplug-b-northbound-standard.md`
   (scope/encoder/coexistence) + `docs/decisions/0036-sparkplug-replay-then-rebirth.md`
   (store-and-forward × ordered seq; touches locked #7/#8/#11).
3. Static HTML **wizard mockup** for sign-off (next).
4. Fold review feedback into **v2** (per `feedback_planning_cadence`).
5. Then backend-first implementation slice, S7/MELSEC-style.

---

## 8. Questions for the user

- §4.A / §4.B / §4.D — **answered** (see the §4 decisions table).
- **§4.C / §4.E — still open, non-blocking (proposed defaults below).** Only a
  concrete downstream consumer would override these; if none is dictating them, we
  proceed with the defaults and expose them as wizard fields:
  - **C (namespace identity):** default `group_id` = site/customer id,
    `edge_node_id` = gateway id, `device_id` = source instance id — all
    overridable in the wizard.
    **Open input:** is any specific Ignition/UNS deployment already dictating a
    group/edge-node scheme we must match?
  - **E (metric naming & aliases):** default metric name = canonical `TagName`
    (fall back to `TagPath` when names collide across devices); aliases assigned
    first-seen and held stable per (device, metric) across rebirths.
    **Open input:** any required metric-name convention on the consumer side?
