# 0032 — Modbus RTU support (serial + RTU-over-TCP) ships inside the Modbus module

**Status:** Accepted (2026-06-25)
**Relates to:** `docs/sessions/2026-06-24-modbus-rtu-support-plan-v1.md`; PHASE3_EXECUTION_PLAN.md §5, §12 (RTU deferred to "Phase 5 if demanded"); ADR-0015 (wizard contract); ADR-0020 (bundle redaction key tiers)

## Context

The Modbus adapter shipped TCP-only. The `RtuOverTcp` encapsulation value
existed in config from F1 but its wire handler threw "not yet implemented"; true
serial RTU (RS-485 / RS-232) was never modelled. A product requirement now needs
EdgeConnect to talk to both serial-gateway (RTU-over-TCP) and physical-serial
RTU devices.

Recon (plan v1 §2) showed the stack was scaffolded for this: `IModbusClient.ConnectAsync`
already takes a `ModbusEncapsulation`, and the connection manager, transaction
executor, scan planner, decoder, and adapter contain no TCP-specific code. The
only TCP-bound component was the production client `FluentModbusClient`.

FluentModbus 5.2.0 ships `ModbusRtuClient` (RTU framing + CRC16) and an
`IModbusRtuSerialPort` seam plus `Initialize(IModbusRtuSerialPort, endianness)`,
which lets a single RTU client serve both transports by swapping the byte source
under the framer.

## Decision

Add Modbus RTU as **two new encapsulations inside the existing Modbus module**,
behind a transport factory. No new protocol, no new assembly, no new license key.

- **`ModbusEncapsulation` gains `SerialRtu`** (alongside `Tcp`, `RtuOverTcp`).
- **`IModbusClientFactory` / `FluentModbusClientFactory`** selects the client by
  encapsulation — the single new transport-selection seam. The adapter builds the
  client from config in `InitializeAsync` (the test/DI ctor still injects one).
- **`FluentModbusRtuClient`** wraps `ModbusRtuClient` for both RTU encapsulations:
  - `SerialRtu` → `ModbusRtuClient.Connect(portName)` over `System.IO.Ports`
    (baud / parity / stop-bits / handshake; RTU is always 8 data bits).
  - `RtuOverTcp` → `ModbusRtuClient.Initialize(TcpModbusRtuSerialPort)`, a custom
    `IModbusRtuSerialPort` that pipes the RTU frames over a TCP `NetworkStream`.
- **`FluentModbusReads`** holds the shared read + CRC/exception-mapping logic; the
  TCP and RTU clients now share one code path.
- **Config** gains `serialPort` / `baudRate` / `parity` / `stopBits` / `handshake`
  (added to `ModbusTcpConnectionKeys.All`, so ADR-0020 redaction coverage is
  automatic — all benign). `host` becomes optional for `SerialRtu`. Validation
  branches: serial requires `serialPort`; TCP/RTU-over-TCP require `host`.
- **Wizard** adds a "Serial RTU" encapsulation option with a conditional
  serial-fields form; the serial block is emitted only for `SerialRtu` so the
  TCP/RTU-over-TCP connection JSON (and its round-trip) is byte-unchanged.

### Supporting choices

1. **Keep the `ModbusTcp` assembly name and `"modbustcp"` protocol id.** RTU is a
   Modbus *encapsulation*, not a separate protocol. Renaming the assembly,
   protocol id, and license key would break existing customer configs, the DI
   alias map, and the license catalog for no functional gain. The slight
   misnomer is accepted and documented here.
2. **One license key (`source-modbus-tcp`) covers all encapsulations.**
3. **`System.IO.Ports` for serial** — cross-platform (Windows `COMx`, Linux
   `/dev/tty*`); the device-path string is operator-supplied. Honors the
   blueprint's "build/run on Linux; no Windows-only APIs" rule. Linux serial
   access requires the service user be in the `dialout` group (documented in the
   wizard help).
4. **`FluentModbusClient` becomes TCP-only with a defensive guard;** the factory
   routes non-TCP encapsulations to `FluentModbusRtuClient`. The old
   "RtuOverTcp not implemented" throw is removed.

## Consequences

- RTU-over-TCP is operator-available through the existing Modbus wizard (it now
  connects instead of throwing). Serial RTU is operator-available via the new
  conditional wizard form.
- The RTU framing path is unit-verified against FluentModbus's real RTU
  encode/decode using CRC-correct frames (no hardware); a standalone RTU-over-TCP
  simulator + skippable integration test exercises the live socket path.
- **Live serial-hardware / real-gateway validation is deferred to whoever has the
  device** (RS-485 adapter or a vendor gateway); CI uses fakes + the simulator,
  consistent with ADR-0031's posture for EtherNet/IP.
- This is an **additive** change: it conflicts with no locked decision
  (protocol-agnostic core, canonical model, modular assemblies, and licensing are
  all untouched). No superseding ADR is required.
- The wizard's serial "Test connection" probe is a follow-up — the probe service
  is still TCP-only, so serial sources skip the probe step for now.
