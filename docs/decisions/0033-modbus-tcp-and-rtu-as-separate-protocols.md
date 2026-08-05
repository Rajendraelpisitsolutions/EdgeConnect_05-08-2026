# 0033 — Modbus TCP and Modbus RTU are separate protocols sharing one core

**Status:** Accepted (2026-06-25)
**Supersedes:** ADR-0032 (Modbus RTU as an encapsulation inside the Modbus TCP module)
**Relates to:** `docs/sessions/2026-06-24-modbus-rtu-support-plan-v1.md`; ADR-0015 (wizard contract)

## Context

ADR-0032 added RTU as an *encapsulation* of the single `modbustcp` protocol —
one tile, one wizard, one protocol id. Product direction changed: Modbus TCP and
Modbus RTU must be **two distinct operator features** (separate picker tiles,
wizards, config identity, and diagnostics), because operators think of them as
different connection types and a serial RTU source should not present as "Modbus
TCP with a serial encapsulation."

The transport/decoder/scan-planner/connection-manager code is genuinely shared,
so full module duplication is not warranted.

## Decision

Expose **two protocol identities over one shared implementation**:

- **`modbustcp`** — native Modbus TCP only (encapsulation `Tcp`).
- **`modbusrtu`** — RTU framing, either `SerialRtu` (serial port) or
  `RtuOverTcp` (serial-to-Ethernet gateway).

Both are served by the **same adapter, connection manager, transaction executor,
decoder, scan planner, and transport clients** in the `ElpisEdgeConnect.Sources.ModbusTcp`
assembly. The split is at the *identity + validation + UX* layer:

- `ModbusTcpSourceConfiguration.FromSourceInstance` accepts both protocol ids;
  the default encapsulation is per protocol (`modbusrtu` → `SerialRtu`,
  `modbustcp` → `Tcp`). The adapter reports its config's `ProtocolName`, so an
  RTU source shows as `modbusrtu` in diagnostics and on the MQTT topic.
- **Validation enforces the pairing:** `modbustcp` ⇒ encapsulation must be `Tcp`;
  `modbusrtu` ⇒ encapsulation must be `SerialRtu` or `RtuOverTcp`. This is what
  makes them genuinely separate rather than overlapping.
- Host registration recognizes both ids (`IsModbusProtocol` matches `modbustcp`
  and `modbusrtu`); `RegistrationFactory` already dispatches via that helper, so
  both route to the one Modbus builder.
- Management gains a second picker tile ("Modbus RTU") and the Modbus wizard
  handles both modes (one component, a TCP route and an RTU route); the edit
  router, display-name map, and onboarding flow learn `modbusrtu`.

### Supporting choices

1. **No assembly rename.** The shared code stays in `Sources.ModbusTcp`; the
   slight misnomer is accepted (renaming breaks DI, configs, the license
   catalog). Same rationale as ADR-0032 §1.
2. **One license key for both.** `source-modbus-tcp` gates `modbustcp` and
   `modbusrtu`, so the RTU tile is Available without editing license files. A
   dedicated `source-modbus-rtu` key is a possible future split (catalog change);
   deferred.
3. **One shared config type** (`ModbusTcpSourceConfiguration`) and **one shared
   wizard model** (`ModbusSourceWizardModel`). The wizard component carries a
   TCP/RTU mode rather than duplicating ~350 lines of Razor.
4. **`modbusrtu` is NOT added to bulk-import** yet (template-based, TCP-oriented).

## Consequences

- Operators see and configure "Modbus TCP" and "Modbus RTU" as two features;
  diagnostics, config `protocolName`, and MQTT topics distinguish them.
- A `modbustcp` source can no longer select an RTU encapsulation (validation
  rejects it) and vice-versa — this is the intended separation, and a behavior
  change versus ADR-0032 (where `modbustcp` accepted RTU encapsulations).
- Existing `modbustcp` sources are unaffected (they were `Tcp` only in practice;
  any that had set an RTU encapsulation must move to a `modbusrtu` source).
- No duplication of the transport/decoder/executor core; bug fixes apply to both.
- Live verification posture unchanged (RTU-over-TCP simulator + unit-level RTU
  framing tests; serial hardware deferred to whoever has the device).
