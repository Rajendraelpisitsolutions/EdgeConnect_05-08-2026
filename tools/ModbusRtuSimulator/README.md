# Modbus RTU-over-TCP Simulator

A standalone, **dependency-free** Modbus RTU slave that listens on a TCP port
and answers RTU read frames (FC01–FC04) with CRC16-correct responses. It lets
the real `FluentModbusRtuClient` (RTU framing over `TcpModbusRtuSerialPort`) be
exercised end-to-end over a socket — distinct from the unit tests, which feed
canned frames.

Pure C# sockets + hand-rolled RTU framing/CRC; **no FluentModbus dependency**.

## Run it

```bash
dotnet run --project tools/ModbusRtuSimulator -- --port 5020 --verbose
# or the built binary:
dotnet tools/ModbusRtuSimulator/bin/Debug/net8.0/ModbusRtuSimulator.dll --port 5020 --verbose
```

Options: `--port N` (default 5020), `--bind IP` (default 127.0.0.1), `--verbose`.

## Register map (deterministic)

| Object | Value at address `a` |
|--------|----------------------|
| Holding registers (FC03) | `1000 + a` |
| Input registers (FC04) | `1000 + a` |
| Coils (FC01) | even `a` = true, odd = false |
| Discrete inputs (FC02) | even `a` = true, odd = false |

Writes are not supported (the adapter only reads). Reads are fixed 8-byte RTU
request frames; a bad CRC is answered with silence (like a real slave).

## Point EdgeConnect at it

Configure a Modbus source with **Encapsulation = RTU over TCP**, `host =
127.0.0.1`, `port = <sim port>`. The real adapter connects over the socket and
reads via RTU framing, exactly as it would against a serial-to-Ethernet gateway.

## Automated test

`tests/ElpisEdgeConnect.Sources.ModbusTcp.Tests/ModbusRtuOverTcpIntegrationTests.cs`
self-starts this simulator on a free port and drives the real
`FluentModbusRtuClient` against it (holding/input registers + coils). It skips
gracefully if the simulator can't start.
