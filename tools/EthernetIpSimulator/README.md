# EtherNet/IP Simulator

A standalone, **dependency-free** EtherNet/IP (CIP) PLC simulator. It speaks
enough of the real Allen-Bradley / Logix wire protocol over TCP **44818** that
the production `libplctag`-backed adapter (`LibPlcTagClient`) can
`RegisterSession → ForwardOpen → Read Tag` against it for genuine end-to-end
**transport** testing.

This is different from the in-process `EthernetIpDemoClient` (demo mode):

| | `EthernetIpDemoClient` (demo mode) | This simulator |
|---|---|---|
| Layer | Replaces the client object in-process | Real TCP/CIP server on the network |
| Exercises libplctag / native stack? | No | **Yes** |
| Use for | UI demos, pipeline/routing tests, no PLC | Validating the real adapter transport |
| Activated by | `EDGECONNECT_ETHERNETIP_FAKE_MODE=1` | Running this exe + pointing a source at it |

## Run it

```bash
dotnet run --project tools/EthernetIpSimulator
# or the built binary:
dotnet tools/EthernetIpSimulator/bin/Debug/net8.0/EthernetIpSimulator.dll --verbose
```

### Options

```
--port N         TCP port (default 44818, the EtherNet/IP port)
--bind IP        bind address (default 127.0.0.1)
--verbose        log every CIP request/response (hex)
--tag name:TYPE  add a tag; TYPE = BOOL|SINT|INT|DINT|LINT|REAL|LREAL (repeatable)
--help
```

Default tags (when none given): `spindle_speed:DINT`, `temperature:REAL`,
`running:BOOL`. Tag values are live — each traces a 30-second sine with a stable
per-name phase, so reads look like moving process data.

```bash
dotnet run --project tools/EthernetIpSimulator -- --verbose \
  --tag Speed:DINT --tag Temp:REAL --tag Running:BOOL --tag Count:LINT
```

## Point EdgeConnect at it

Configure an EtherNet/IP source (NOT in demo mode — clear
`EDGECONNECT_ETHERNETIP_FAKE_MODE`) with:

- **host** = `127.0.0.1` (or wherever the simulator runs)
- **cpuFamily** = `ControlLogix`
- tags whose **name** matches the simulator's tag names and whose **datatype**
  matches the simulator's type.

The real adapter then connects over the wire and reads live values, which flow
through routes to your sinks (e.g. MQTT) exactly as a real PLC would.

## Protocol scope

Implemented (the subset libplctag uses for Logix symbolic atomic reads):

- Encapsulation: `RegisterSession`, `UnRegisterSession`, `SendRRData`, `SendUnitData`
- CIP: `ForwardOpen` / `Large ForwardOpen`, `ForwardClose`, `Read Tag` (0x4C),
  `Read Tag Fragmented` (0x52), `Unconnected Send` (0x52 → CM), `Multiple Service Packet`
- Atomic types: `BOOL`, `SINT`, `INT`, `DINT`, `LINT`, `REAL`, `LREAL`

**Out of scope:** writes, UDT/array/structured reads, and the AB `STRING`
structure. Reads of an undefined tag return a CIP path error (non-fatal).

## Automated test

`tests/ElpisEdgeConnect.Sources.EthernetIp.Tests/EthernetIpSimulatorIntegrationTests.cs`
self-starts this simulator as a child process and drives the real
`LibPlcTagClient` against it. It skips gracefully if the port is busy or
`dotnet`/libplctag native aren't available.
