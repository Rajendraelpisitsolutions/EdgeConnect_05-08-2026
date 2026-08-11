# Modbus TCP simulator for integration tests

A [pymodbus] 3.x TCP server used by the Phase 3 integration tests
(`ModbusTcpF1IntegrationTests` for the raw transaction layer, and
`ModbusTcpToMqttEndToEndTests` for the F3 decoder-through-sink chain).
The test fixture (`ModbusTcpSimulatorFixture`) builds this image on demand
and runs a throwaway container for the duration of the test class.

A background thread inside the simulator drives small random walks on the
numeric tags every second so a trend-aware subscriber (EREMOS V2) sees
fresh data across multiple polls.

## PLC-shaped address map (unit id = 1)

All offsets are zero-based. Byte order notes apply at the decoder (F3)
side — the wire is always big-endian per register.

| Tag | Class | Addr | Datatype | Byte order | Unit | Initial value |
|---|---|---|---|---|---|---|
| `running`           | Coil    | 0     | bool     | — | — | `true` |
| `alarm_active`      | Coil    | 1     | bool     | — | — | `false` |
| `door_closed`       | DI      | 0     | bool     | — | — | `true` |
| `tool_in_spindle`   | DI      | 1     | bool     | — | — | `true` |
| `spindle_rpm`       | HR      | 0     | uint16   | AB    | rpm     | 1450 |
| `spindle_load`      | HR      | 1     | int16    | AB    | %       | -15 |
| `feed_rate`         | HR      | 10    | float32  | ABCD  | mm/min  | 250.5 |
| `parts_count`       | HR      | 20    | uint32   | CDAB  | —       | 1 234 567 |
| `cycle_time`        | HR      | 30    | float32  | ABCD  | s       | 42.75 |
| `energy_kwh`        | HR      | 40    | float32  | ABCD  | kWh     | 128.4 |
| `alarm_code`        | HR      | 50    | int16    | AB    | —       | 0 |
| `mode`              | HR      | 60    | string16 | —     | —       | `"AUTO"` (padded) |
| `part_name`         | HR      | 100   | string8  | —     | —       | `"SHAFT-7X"` |
| `temperature`       | IR      | 0     | int16    | AB    | °C (scale 0.1) | raw 420 → 42.0 |

Coverage: every datatype, four different byte orders, scale/offset
(temperature), and all four register classes.

## Manual run

### Native Windows / Linux / macOS (no Docker needed) — recommended for soak

The simulator is just a Python script. For dev / soak runs on a Windows box
where Docker isn't installed, run it directly:

```powershell
# one-time setup
py -3.12 -m venv .venv
.\.venv\Scripts\Activate.ps1
# Pinned to <3.8 — pymodbus 3.8 deprecated and 3.9+ renamed
# `ModbusSlaveContext` → `ModbusDeviceContext`, breaking server.py.
pip install "pymodbus>=3.6,<3.8"

# every run
py server.py        # or:  python server.py
```

The simulator listens on `0.0.0.0:5020` by default. Override with
`MODBUS_PORT=1502 py server.py`.

### Docker (used by the integration-test fixture)

```bash
cd tests/ElpisEdgeConnect.Integration.Tests/ModbusSimulator
docker build -t elpis-modbus-sim:test .
docker run --rm -p 5020:5020 elpis-modbus-sim:test
```

### Quick verification (after either run)

```bash
python - <<'PY'
from pymodbus.client import ModbusTcpClient
c = ModbusTcpClient("127.0.0.1", port=5020)
c.connect()
print("spindle_rpm:", c.read_holding_registers(0, 1, slave=1).registers)
print("feed_rate regs:", c.read_holding_registers(10, 2, slave=1).registers)
print("parts_count regs (CDAB):", c.read_holding_registers(20, 2, slave=1).registers)
c.close()
PY
```

## S7-1200 flavouring (env-var configurable)

The simulator approximates real-PLC quirks that matter for the
Modbus-MQTT soak. Defaults are conservative; tests don't depend on them.

| Env var | Default | Effect |
|---|---|---|
| `MODBUS_SIM_JITTER_MS`        | `5`   | Uniform 0..N ms delay applied to every read. Models S7-1200 scan-cycle coupling. Set to `0` for bit-perfect deterministic test runs. |
| `MODBUS_SIM_SLOW_AFTER`       | `0`   | After this many reads, enter a "slow slave" episode. `0` = disabled. |
| `MODBUS_SIM_SLOW_DURATION_S`  | `30`  | Episode duration in seconds. |
| `MODBUS_SIM_SLOW_EXTRA_MS`    | `100` | Extra delay added to every read during an episode. |

Example — exercise the adapter's circuit breaker / retry tuning by
inducing a slow episode every ~5000 reads:

```powershell
$env:MODBUS_SIM_SLOW_AFTER = "5000"
$env:MODBUS_SIM_SLOW_DURATION_S = "30"
$env:MODBUS_SIM_SLOW_EXTRA_MS = "200"
py server.py
```

### What's NOT simulated

- **Connection cap** — real S7-1200 limits to ~3 concurrent clients.
  The sim accepts unlimited connections. Validate that behaviour in
  Phase A'' against actual PLC hardware.
- **Random disconnects / TCP RST** — soak script kills the sim process
  for partition tests; finer-grained TCP misbehaviour requires a real
  PLC or a network-fault-injection layer.

## CI

The integration-test fixture (`ModbusTcpSimulatorFixture`) uses Docker
to build and run the sim for the test class lifetime. If Docker is not
available, those tests skip gracefully — the rest of the suite still
runs.

For the dev/soak workflow on a Windows box without Docker, use the
native-run path above.

[pymodbus]: https://github.com/pymodbus-dev/pymodbus
