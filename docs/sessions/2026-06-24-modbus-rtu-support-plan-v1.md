# Modbus RTU support — design & implementation plan — v1

**Date:** 2026-06-24
**Status:** v1 — initial design + implementation plan, **for review**. Do **not** implement off v1.
**Author:** session (design pass)
**Trigger:** Product requirement — *"EdgeConnect must support Modbus RTU, not only Modbus TCP."*
**Scope question answered here:** which "RTU" (serial vs RTU-over-TCP), where it plugs in, what changes,
how it's tested, and what (if anything) needs an ADR.

> Cadence: **v1** → review → v2 → reality-check → v3, each its own dated file. v1 folds in the recon
> pass below; it is **not** a lock. No code before v3.

---

## 1. Executive summary

"Modbus RTU" means two distinct things, and we need **both**, in this order:

| Mode | What it is | Effort | Dependencies |
|------|-----------|--------|--------------|
| **RTU-over-TCP** | RTU frames (slave addr + PDU + **CRC16**, no MBAP header) tunnelled over a TCP socket — used by serial-to-Ethernet gateways. | Small | None new (cross-platform) |
| **Serial RTU** | Classic Modbus RTU over a physical RS-485/RS-232 port (`COM3` / `/dev/ttyUSB0`). | Medium | `System.IO.Ports`; cross-platform device paths; hardware/virtual-port testing |

**The good news (recon):** the adapter stack was built transport-agnostic. `IModbusClient.ConnectAsync`
*already* takes a `ModbusEncapsulation` argument, the connection manager / transaction executor / adapter
contain **no TCP-specific code**, the config layer already parses `RtuOverTcp`, and the wizard already
lists it. **The only place that is TCP-only is the production client `FluentModbusClient`**, which
hardcodes `new ModbusTcpClient()` and throws on `RtuOverTcp`
([FluentModbusClient.cs:38, :65-73](../../src/ElpisEdgeConnect.Sources.ModbusTcp/FluentModbusClient.cs#L65)).

**The lever:** FluentModbus 5.2.0 ships `ModbusRtuClient` (RTU framing + CRC16) and an
`IModbusRtuSerialPort` seam plus `ModbusRtuClient.Initialize(IModbusRtuSerialPort, ModbusEndianness)`.
This lets us implement **both** RTU variants behind **one** new `IModbusClient` implementation, differing
only in the byte stream under the RTU framer:

- **Serial RTU** → `ModbusRtuClient` over the default serial port (`System.IO.Ports.SerialPort`).
- **RTU-over-TCP** → `ModbusRtuClient` over a **custom `IModbusRtuSerialPort` backed by a TCP
  `NetworkStream`** (the framer/CRC is identical; only the wire underneath changes).

So the central change is a **transport factory + one RTU client wrapper**, with config + wizard +
validation + tests around it. The rest of the pipeline is untouched.

---

## 2. Current-state recon (evidence)

| # | Finding | Evidence |
|---|---------|----------|
| **F1** | The client seam is already encapsulation-aware. | `IModbusClient.ConnectAsync(host, port, ModbusEncapsulation, ...)` ([IModbusClient.cs:40-95](../../src/ElpisEdgeConnect.Sources.ModbusTcp/IModbusClient.cs)) |
| **F2** | Only the production client is TCP-bound. | `private readonly ModbusTcpClient _client = new();` and the `RtuOverTcp` throw ([FluentModbusClient.cs:38, :65-73](../../src/ElpisEdgeConnect.Sources.ModbusTcp/FluentModbusClient.cs#L65)) |
| **F3** | Manager / executor / adapter are transport-agnostic. | Manager only calls `IModbusClient` methods + passes `_config.Encapsulation` ([ModbusConnectionManager.cs](../../src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusConnectionManager.cs)); executor dispatches by function code only ([ModbusTransactionExecutor.cs](../../src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusTransactionExecutor.cs)) |
| **F4** | The client is injectable; production path hardcodes the client. | Adapter test ctor takes `IModbusClient`; production ctor does `new FluentModbusClient()` ([ModbusTcpSourceAdapter.cs:73-98](../../src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusTcpSourceAdapter.cs#L73)); DI builds the adapter via the production ctor ([ModbusTcpRegistrationExtensions.cs](../../src/ElpisEdgeConnect.Host/Adapters/ModbusTcpRegistrationExtensions.cs)) |
| **F5** | Config already models + parses encapsulation. | `Encapsulation` enum (`Tcp`, `RtuOverTcp`) ([ModbusEncapsulation.cs](../../src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusEncapsulation.cs)); `ReadEncapsulation` accepts `"rtuOverTcp"`/`"rtu-over-tcp"` ([ModbusTcpSourceConfiguration.cs](../../src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusTcpSourceConfiguration.cs)); tests assert parse-accept + connect-reject ([ModbusTcpSourceConfigurationTests.cs](../../tests/ElpisEdgeConnect.Sources.ModbusTcp.Tests/ModbusTcpSourceConfigurationTests.cs)) |
| **F6** | Wizard + picker already reference RTU. | Encapsulation dropdown has `Tcp` + `RtuOverTcp` ([AddModbusSource.razor](../../src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddModbusSource.razor)); picker tile text mentions RTU ([SourceProtocolPickerModel.cs](../../src/ElpisEdgeConnect.Management/Wizards/SourceProtocolPickerModel.cs)) |
| **F7** | Config schema is opaque per-protocol JSON. | `Connection` is a `JsonElement`; no schema change needed to add serial keys |
| **F8** | One license key covers both. | `source-modbus-tcp` / `LicenseModuleKeys.SourceModbusTcp` gates the module |
| **F9** | FluentModbus has the RTU client we need. | `ModbusRtuClient` (BaudRate/Parity/StopBits/Handshake; `Connect(string port)`) + `IModbusRtuSerialPort` + `Initialize(IModbusRtuSerialPort, ModbusEndianness)` (FluentModbus 5.2.0 XML docs) |
| **F10** | RTU was always planned, deferred. | `PHASE3_EXECUTION_PLAN.md` lists "Modbus RTU serial (Phase 5 if demanded)" and "RTU-over-TCP gateway … encapsulation mode supported from F1" |

**Conclusion:** the platform was scaffolded for RTU; the work is to **fill in the transport layer** and the
operator-facing config/UI/tests around it — not to re-architect the adapter.

---

## 3. Design — transport architecture

### 3.1 Encapsulation model (proposed)

Extend the existing enum to a **third member** (keeps the established "encapsulation" vocabulary; the enum
already mixes wire-format and transport for the TCP cases):

```csharp
public enum ModbusEncapsulation
{
    Tcp        = 0,   // MBAP-framed PDU over a TCP socket          (ModbusTcpClient)        — exists
    RtuOverTcp = 1,   // RTU frame (CRC16) over a TCP socket        (ModbusRtuClient/TCP)    — NEW wire
    SerialRtu  = 2,   // RTU frame (CRC16) over a serial port       (ModbusRtuClient/serial) — NEW
}
```

Mapping to FluentModbus:
- `Tcp` → `ModbusTcpClient` (today's `FluentModbusClient`).
- `RtuOverTcp` → `ModbusRtuClient` initialized over a **TCP-backed `IModbusRtuSerialPort`**.
- `SerialRtu` → `ModbusRtuClient` over the **default serial `IModbusRtuSerialPort`** (System.IO.Ports).

### 3.2 The seam: a client factory

Today the adapter's production ctor hardcodes `new FluentModbusClient()` (F4). Introduce a factory and
select by encapsulation — the single new decision point:

```csharp
internal interface IModbusClientFactory
{
    IModbusClient Create(ModbusTcpSourceConfiguration cfg);
}

internal sealed class FluentModbusClientFactory : IModbusClientFactory
{
    public IModbusClient Create(ModbusTcpSourceConfiguration cfg) => cfg.Encapsulation switch
    {
        ModbusEncapsulation.Tcp        => new FluentModbusTcpClient(),          // = today's FluentModbusClient
        ModbusEncapsulation.RtuOverTcp => new FluentModbusRtuClient(RtuTransport.Tcp),
        ModbusEncapsulation.SerialRtu  => new FluentModbusRtuClient(RtuTransport.Serial),
        _ => throw new ModbusFatalException(ModbusErrors.ConfigInvalid, ...),
    };
}
```

- The adapter's **production** ctor calls the factory; the **internal/test** ctor keeps taking an
  `IModbusClient` directly (so `FakeModbusClient` unit tests are unchanged).
- The factory is the *only* production wiring change; the manager/executor/adapter are untouched (F3).

### 3.3 `FluentModbusRtuClient` (the one new transport)

A new `IModbusClient` implementation wrapping `FluentModbus.ModbusRtuClient`. It owns:
- **Serial mode:** `rtu.Connect(cfg.SerialPort, endianness)` after setting `BaudRate/Parity/StopBits/Handshake`.
- **RTU-over-TCP mode:** a `TcpModbusRtuSerialPort : IModbusRtuSerialPort` that opens a `TcpClient` to
  `host:port` and exposes its `NetworkStream` as the byte pipe; then `rtu.Initialize(thatPort, endianness)`.
- The four reads (`ReadCoils/DiscreteInputs/HoldingRegisters/InputRegisters`) delegate to `ModbusRtuClient`
  (inherited from the FluentModbus base — identical surface to the TCP client, so the executor is unchanged).
- `IsConnected`, `Disconnect`, `DisposeAsync` mirror the existing client.

> **Spike required (see §10):** confirm `ModbusRtuClient.Initialize(IModbusRtuSerialPort, …)` over a TCP
> `NetworkStream` actually round-trips against a real RTU-over-TCP server — exactly how we verified the
> EtherNet/IP simulator against real libplctag. If FluentModbus's RTU framer assumes serial inter-frame
> timing that a TCP stream can't satisfy, fall back to a hand-rolled RTU/CRC16 framer over `NetworkStream`
> (small, well-specified) for the RTU-over-TCP case.

### 3.4 What is explicitly *not* changing
Canonical pipeline, routing, store-and-forward, the `ModbusRegisterClass`/`ModbusDatatype`/`ModbusDecoder`
stack, the circuit-breaker/backoff in `ModbusConnectionManager`, the protocol name `"modbustcp"`, and the
license key — all transport-neutral and reused as-is.

---

## 4. Configuration model changes

### 4.1 New fields on `ModbusTcpSourceConfiguration`

| Field | Type | Applies to | Notes |
|-------|------|-----------|-------|
| `SerialPort` | `string?` | SerialRtu | `COM3` (Windows) / `/dev/ttyUSB0` (Linux) |
| `BaudRate` | `int` | SerialRtu | default 9600 |
| `Parity` | enum (None/Even/Odd) | SerialRtu | FluentModbus default Even; we default **None** (most common field setting — confirm in review) |
| `DataBits` | `int` | SerialRtu | default 8 |
| `StopBits` | enum (One/Two) | SerialRtu | default One |
| `Handshake` | enum (None/…) | SerialRtu | default None |
| (existing) `Host`/`Port` | | Tcp, RtuOverTcp | unchanged |
| (existing) `DefaultUnitId` / per-tag `UnitId` | | all | **More important for RTU** — RS-485 multidrop addresses multiple slaves on one port |

### 4.2 New keys in `ModbusTcpConnectionKeys` (+ add to the `All` list per ADR-0020 redaction discipline)
`serialPort`, `baudRate`, `parity`, `dataBits`, `stopBits`, `handshake`. (All benign — no secrets.)

### 4.3 Parsing + validation
- Extend `ReadEncapsulation` to accept `"serialRtu"` / `"serial"`.
- Extend `FromSourceInstance` to read the serial keys.
- Extend `ValidateConfigAsync`:
  - `SerialRtu` ⇒ `SerialPort` **required**, `Host` ignored/forbidden; baud > 0; dataBits ∈ {7,8}; valid parity/stopbits.
  - `Tcp`/`RtuOverTcp` ⇒ `Host` required (today's rules).
  - Cross-field: reject serial fields supplied with a TCP encapsulation (fail-closed, surfaced clearly).

---

## 5. Cross-platform & dependencies

- **`System.IO.Ports`** is required for serial. It supports Windows **and** Linux (`/dev/tty*`), so serial
  RTU is **not** Windows-only — but the **device-path string differs** by OS (operator-supplied; we don't
  hardcode). No `OperatingSystem.IsWindows()` guard is needed for the happy path; we *do* need a clear
  error when the port can't be opened (missing/permission), and Linux serial access typically needs the
  user in the `dialout` group (document in the wizard help + runbook).
- **RTU-over-TCP** has **no** new dependency and is fully cross-platform.
- Confirm `System.IO.Ports` is pulled in transitively by FluentModbus or add the package explicitly to
  `ElpisEdgeConnect.Sources.ModbusTcp.csproj` (verify in the spike).
- CLAUDE.md / blueprint constraint honored: "build and run on Linux; avoid Windows-only APIs except behind
  guards." `System.IO.Ports` is cross-platform, so this is satisfied.

---

## 6. Operator surface (wizard / probe)

- **RTU-over-TCP:** dropdown already present (F6) — once the wire works, it functions with **no UI change**
  (still `host`+`port`). Probe (`/api/v1/sources/browse/modbus`) reuses the TCP path.
- **Serial RTU:** the wizard's connection section becomes **conditional on encapsulation**:
  - `Tcp`/`RtuOverTcp` → `Host` + `Port` (today).
  - `SerialRtu` → `SerialPort` + `BaudRate` + `Parity` + `DataBits` + `StopBits` (+ help text on
    device-path format and Linux `dialout`).
  - `ModbusSourceWizardModel` serializes the serial keys into the connection JSON; tag-definition UI is
    unchanged (protocol-agnostic).
  - **UI rule:** per the project's static-mockup convention, the conditional serial form needs an operator
    sign-off mockup **before** the Razor change (call out in v2/v3).
- **Serial probe:** a `SerialModbusProbeTransport` (or skip "Test connection" for serial in the MVP and
  document) — decide in review.

---

## 7. Licensing & module naming

- **Licensing:** no change — `source-modbus-tcp` gates all Modbus encapsulations (F8).
- **Naming wrinkle (needs a decision):** the assembly/folder is `ElpisEdgeConnect.Sources.ModbusTcp` and
  the protocol name is `"modbustcp"`, but it will now also do serial RTU. Options:
  1. **Keep the names** (recommended). RTU is a Modbus *encapsulation*, not a new protocol; renaming the
     assembly + protocol id + license key is a breaking, cross-cutting change (DI alias map, config
     `protocolName`, existing customer configs, license catalog). Document the slight misnomer in the ADR.
  2. Rename to `…Sources.Modbus` with a back-compat alias for `"modbustcp"`. Larger blast radius; defer.
  → v1 recommends Option 1; flag for review.

---

## 8. Testing strategy

Mirrors the existing Modbus + EtherNet/IP-simulator patterns; **deterministic first, hardware never in CI.**

1. **Unit (no hardware, no network):**
   - `FakeModbusClient` already covers adapter logic — unchanged.
   - New: `FluentModbusRtuClient` tested against a **fake `IModbusRtuSerialPort`** (an in-memory RTU
     loopback that returns canned, CRC-correct frames). Proves framing/decoding without a port.
   - Config: parse/validate tests for `SerialRtu` + cross-field rejection (extend
     `ModbusTcpSourceConfigurationTests`).
2. **Integration — RTU-over-TCP:** stand up an **RTU-over-TCP server** and drive the real
   `FluentModbusRtuClient` against it (same philosophy as the EtherNet/IP simulator we built). Either
   extend the pymodbus simulator (`framer=ModbusRtuFramer` on a TCP listener) behind a skippable fixture,
   or add a small standalone C# RTU-over-TCP sim under `tools/`. **Skippable** when unavailable.
3. **Integration — serial:** use a **virtual serial port pair** (com0com on Windows / `socat` PTY on
   Linux) behind an `IsAvailable`-gated fixture (matches `ModbusTcpSimulatorFixture`'s graceful-skip
   posture). Primary correctness still comes from the fake-serial-port unit tests; this is best-effort.
4. **PR gate:** run the **full** `Management.Tests` project (house rule — filtered runs bypass
   cross-cutting isolation/schema guards).
5. **Matrix per mode:** connect, four function codes, CRC error handling, timeout/reconnect, multidrop
   unit-id addressing (RTU-specific), invalid-config rejection.

---

## 9. ADR plan

This is an architecturally meaningful addition (new transports, a client factory, config surface, a
TCP-named module gaining serial). Author **ADR-0032 "Modbus RTU support (serial + RTU-over-TCP)."**

- **Does NOT conflict with any locked decision:** protocol-agnostic core untouched (Lock #1), canonical
  model untouched (#2), modular-assembly model preserved (#4, stays inside the Modbus module), license
  gating unchanged (#5). So this is an **additive** ADR, not a superseding one.
- **Records:** the three-mode encapsulation, the factory seam, the single RTU client wrapper over a
  swappable `IModbusRtuSerialPort`, the keep-the-name decision (§7), and the cross-platform serial stance.
- Reference `PHASE3_EXECUTION_PLAN.md` (RTU was deferred there; this activates it).

---

## 10. Phased execution

- **Phase 0 — Decide & document.** v1 → review → v2 → reality-check → v3; draft ADR-0032. **Spike**
  (throwaway): prove `ModbusRtuClient` over a TCP-backed `IModbusRtuSerialPort` round-trips against a real
  RTU-over-TCP server; confirm `System.IO.Ports` is available on the target. **No production code before
  v3 lock.**
- **Phase 1 — RTU-over-TCP.** Client factory + `FluentModbusTcpClient` rename-in-place + `FluentModbusRtuClient`
  (TCP transport); remove the throw; config parse already done; unit + skippable integration tests. **No
  UI/schema/DI/license change.** Smallest, highest-confidence slice. Own PR.
- **Phase 2 — Serial config + transport.** `SerialRtu` enum value, serial config fields/keys/validation,
  `FluentModbusRtuClient` (serial transport), fake-serial-port unit tests, skippable virtual-port
  integration test. Own PR.
- **Phase 3 — Serial wizard.** Conditional connection form (after static-mockup sign-off), wizard-model
  serialization, optional serial probe. Own PR.
- **Phase 4 — Hardening.** Multidrop soak, CRC/timeout fault taxonomy, docs/runbook (Linux `dialout`,
  device paths), protocol-certification-matrix update.

---

## 11. Risks & mitigations

| Risk | Severity | Mitigation |
|------|----------|-----------|
| FluentModbus RTU framer assumes serial timing → won't work over a TCP stream | **High** | Phase-0 spike; fallback = hand-rolled RTU/CRC16 framer over `NetworkStream` |
| `System.IO.Ports` Linux quirks / permissions (`dialout`) | Med | Document; clear open-failure errors; serial integration test skippable |
| No real serial hardware in CI | Med | Fake-serial-port unit tests are the gate; virtual-port + hardware tests best-effort |
| Multidrop unit addressing regressions (one port, many slaves) | Med | Explicit multidrop test cases; per-tag `UnitId` already supported |
| TCP-named module now does serial (operator confusion) | Low | ADR records it; wizard/help text clarifies; keep names (§7) |
| CRC / endianness mismatches vs field devices | Med | Matrix tests; expose endianness like the TCP client does today |

---

## 12. Open decisions for review (→ v2)

1. **Confirm scope = both** RTU-over-TCP **and** serial RTU (v1 assumes both, RTU-over-TCP first). If only
   one is needed now, Phase 2/3 drop out.
2. **Enum vs. separate transport field:** add `SerialRtu` to `ModbusEncapsulation` (v1's choice) or
   introduce a distinct `Transport` axis? Enum is simpler; a separate axis is cleaner if more wire formats
   ever appear.
3. **Module naming:** keep `Sources.ModbusTcp` / `"modbustcp"` (v1 recommends) or rename with alias?
4. **Serial defaults:** Parity default None vs Even (FluentModbus default); confirm against the target
   device population.
5. **Serial probe / "Test connection":** implement a serial probe transport now, or defer (skip the button
   for serial in MVP)?
6. **Spike outcome:** does FluentModbus RTU-over-TCP via `IModbusRtuSerialPort` work, or do we hand-roll
   the RTU-over-TCP framer? (Decides Phase 1 internals.)
7. **Simulator choice:** extend the pymodbus simulator for RTU, or add a standalone C# RTU sim under
   `tools/` (consistent with the EtherNet/IP simulator)?
