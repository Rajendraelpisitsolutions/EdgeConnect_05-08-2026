# Sample EdgeConnect configurations

Reference `gateway.json` files for common deployment shapes. Drop one in
as `{data-root}/config/current.json`, edit the IPs and tag addresses,
and the host comes up against your hardware.

| File | Shape | Buffer | When to use |
|---|---|---|---|
| [`gateway-modbus-s7-1200.json`](gateway-modbus-s7-1200.json)         | S7-1200 → MQTT | InMemory       | smoke + short soak runs; broker assumed always reachable |
| [`gateway-modbus-s7-1200-snf.json`](gateway-modbus-s7-1200-snf.json) | S7-1200 → MQTT | StoreAndForward | production / pilot runs where broker outages or host restarts must NOT lose data |
| [`gateway-focas2-cnc.json`](gateway-focas2-cnc.json)                 | Fanuc CNC → MQTT | StoreAndForward | Fanuc CNC over native FOCAS2; production-shape with durable buffer |

---

## `gateway-modbus-s7-1200.json` — annotated walkthrough

Single-PLC, single-sink layout. The same file works against:

- A real S7-1200 with the `Modbus_TCP_Server` function block enabled
  (set the host IP and port to match the PLC)
- The bundled pymodbus simulator
  ([`tests/ElpisEdgeConnect.Integration.Tests/ModbusSimulator/`](../../tests/ElpisEdgeConnect.Integration.Tests/ModbusSimulator/README.md))
  for dev / soak runs (set host = `127.0.0.1`, port = `5020`)

The seeded register layout in the simulator deliberately mirrors this
config so the same `gateway.json` exercises every datatype + byte order
combination end-to-end without modification.

### Gateway block

```json
"gateway": { "gatewayId": "gw-line1-edge", "gatewayName": "Line 1 EdgeConnect" }
```

`gatewayId` becomes the second segment of every published MQTT topic
(`eremos/gw-line1-edge/...`). Pick a stable, human-readable value per
edge node — `gw-{plant}-{line}-{role}` reads well on dashboards.

### Source block — Modbus TCP

```json
"sources": [{
  "instanceId":   "modbus-s7-line1",
  "protocolName": "modbustcp",
  "deviceId":     "S7-1200-Line1",
  "deviceClass":  "plc",
  ...
}]
```

- `instanceId` — segment 4 of the MQTT topic (`{sourceId}`). Stable per
  physical device.
- `protocolName` — must be `"modbustcp"` for the Modbus TCP adapter to
  pick this source up.
- `deviceClass` — **required** for Modbus sources. Per the
  [`per-tag MQTT contract`][contract], `plc` is the right value when
  fronting a PLC. Other valid values: `cnc`, `daq`, `tracker`, `meter`,
  `gateway` (gateway self-metrics only).

### Connection — endpoint, retries, backoff

```json
"connection": {
  "host":              "192.168.1.50",   // PLC IP (or 127.0.0.1 for the sim)
  "port":              502,              // Modbus TCP default
  "encapsulation":     "tcp",            // S7-1200 is native Modbus TCP
  "defaultUnitId":     1,                // S7-1200 default unit id
  "connectTimeoutMs":  3000,
  "requestTimeoutMs":  2000,
  "keepAlive":         true,             // re-use the TCP socket across polls
  "maxTransactionRetries": 2,
  "initialBackoffMs":  1000,
  "maxBackoffMs":      30000,
  "backoffMultiplier": 2.0,
  "circuitBreakerThreshold": 5,          // 5 consecutive failures → breaker OPEN
  "circuitBreakerResetMs":   30000,
  "maxGapRegisters":   8                 // F2 scan-planner coalescing budget
}
```

The defaults are conservative for a LAN-attached PLC. For a PLC over a
VPN or shaky wireless, raise `requestTimeoutMs` to 5000+ and reduce
`circuitBreakerThreshold`.

### Tag definitions

13 tags spanning every datatype + four register classes. The shape
matches the simulator's seeded register map:

| Tag | Class | Addr | Datatype | Byte order | Why |
|---|---|---|---|---|---|
| `running`           | Coil    | 0     | bool     | — | line in cycle |
| `alarm_active`      | Coil    | 1     | bool     | — | any active alarm |
| `door_closed`       | DI      | 0     | bool     | — | safety guard |
| `tool_in_spindle`   | DI      | 1     | bool     | — | tool clamp signal |
| `spindle_rpm`       | HR      | 0     | uint16   | (default AB) | rpm |
| `spindle_load`      | HR      | 1     | int16    | (default AB) | -100..100 % |
| `feed_rate`         | HR      | 10    | float32  | ABCD | mm/min |
| `cycle_time`        | HR      | 30    | float32  | ABCD | seconds |
| `energy_kwh`        | HR      | 40    | float32  | ABCD | accumulated kWh |
| `parts_count`       | HR      | 20    | uint32   | CDAB | word-swapped 32-bit counter |
| `alarm_code`        | HR      | 50    | int16    | (default AB) | current alarm number |
| `mode`              | HR      | 60    | string8  | — | AUTO / MDI / JOG (8 chars) |
| `part_id`           | HR      | 100   | string8  | — | active part identifier |
| `temperature`       | IR      | 0     | int16, scale 0.1 | (default AB) | motor temp °C |

For larger tag lists use the
[`ModbusCsvImport`](../../tools/ModbusCsvImport/README.md) tool —
author tags in CSV, paste the JSON fragment under `connection.tagDefinitions`.

### Sink block — MQTT

```json
"sinks": [{
  "instanceId":   "mqtt-eremos",
  "protocolName": "mqtt",
  "connection": {
    "brokerHost":          "20.197.8.189",
    "brokerPort":          1883,
    "clientId":            "edgeconnect-gw-line1-edge",
    "useTls":              false,
    "publishMode":         "PerTag",
    "perTagTopicTemplate": "eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}",
    "qosLevel":            0,
    "reconnectDelayMs":    1000,
    "maxReconnectDelayMs": 30000
  }
}]
```

- `perTagTopicTemplate` uses the v2 shape from the
  [per-tag MQTT contract][contract] — the `{deviceClass}` segment lets
  EREMOS V2 distinguish PLC data from CNC data on the same broker.
- `useTls=false` + plaintext port 1883 are **dev-only**. Production
  must move to TLS on 8883 + auth.

### Route block — wire source to sink

```json
"routes": [{
  "routeId":          "route-line1-to-eremos",
  "sourceInstanceId": "modbus-s7-line1",
  "sinkInstanceIds":  [ "mqtt-eremos" ],
  "buffer":   { "mode": "InMemory", "maxDepth": 50000, "onOverflow": "DropOldest" },
  "delivery": { "mode": "AtLeastOnce", "maxRetries": 100,
                "initialBackoffMs": 100, "maxBackoffMs": 30000,
                "backoffMultiplier": 2.0, "jitterPercent": 10 }
}]
```

`InMemory` buffer is fine for the smoke / soak runs. For production
deployments where the broker can be offline for longer than seconds,
switch to `StoreAndForward` — see the dedicated walkthrough below.

### Resulting MQTT topics

With this config, the broker sees:

```
eremos/gw-line1-edge/plc/modbus-s7-line1/spindle_rpm        → "1450"
eremos/gw-line1-edge/plc/modbus-s7-line1/feed_rate          → "250.5"
eremos/gw-line1-edge/plc/modbus-s7-line1/parts_count        → "1234567"
eremos/gw-line1-edge/plc/modbus-s7-line1/temperature        → "42"
eremos/gw-line1-edge/plc/modbus-s7-line1/running            → "True"
...
```

Witness with `mosquitto_sub`:

```
mosquitto_sub -h 20.197.8.189 -p 1883 -t 'eremos/gw-line1-edge/plc/+/+' -v
```

[contract]: ../../../shared-knowledge/contracts/eremos-per-tag-mqtt.md

---

## `gateway-modbus-s7-1200-snf.json` — Store-and-Forward production shape

Identical to the smoke sample above **except** the `routes[].buffer` block:

```json
"buffer": {
  "mode":        "StoreAndForward",
  "maxDepth":    100000,
  "maxAgeDays":  7,
  "onOverflow":  "DropOldest"
}
```

That single change is everything the operator does to switch from "drop on
broker outage" to "queue durably on disk and replay automatically." The
adapters, transforms, fanout, retry, lifecycle — none of it changes. S&F
is gateway-wide and protocol-agnostic; the same config knob covers
Modbus, FOCAS2, MTConnect, and every protocol that ships later.

### What S&F gives you

- **Broker outage**: source keeps polling, points land in `{routeId}.db`,
  sink reconnects, queue drains. Zero data loss.
- **Host restart**: queued points survive disk → on next start the sink
  resumes from its last cursor. Zero data loss.
- **Lagging sink**: a slow consumer doesn't block the source. The
  `OldestUnackedSinkId` field on the diagnostics surface tells you which
  sink is pinning the tail.

### What it costs you

- **Disk** — SQLite WAL files at `{dataRoot}/buffer/{routeId}.db`. See
  the capacity-sizing table below.
- **Throughput ceiling** — the SQLite buffer's sustained write throughput
  is lower than the in-memory buffer (Phase 1 baseline measured roughly
  ~19 M points/s SQLite vs ~27 M points/s InMemory on commodity dev
  hardware). For pilot-scale rates (a few hundred to a few thousand
  publishes / sec) this ceiling is well above what you'll generate, so
  it's a non-issue in practice.
- **Disk-fail mode** — when the disk is full or the directory is RO,
  enqueue throws and the source-supervisor surfaces an error per
  blueprint §13.1. Set up disk-space alerts on the data root.

### Capacity sizing

Worst-case disk use is bounded by:

```
disk_bytes  ≈  bytes_per_row × min(maxDepth, rate_per_sec × maxAge_seconds)
```

`bytes_per_row` for the EdgeConnect canonical row (sequence, timestamps,
tag name, value, metadata) averages roughly **300-450 B** depending on
metadata richness. Use 500 B for a generous safety margin.

**Example ceilings** (per route):

| Rate (pts/sec) | maxDepth | maxAgeDays | Headroom (raw) | Disk (with 500 B/row) |
|---|---:|---:|---:|---:|
|     50 |  10 000 | 1   |        50 × 86 400 = 4.3 M | bound by maxDepth → **5 MB** |
|     50 |  10 000 | 7   |       50 × 604 800 = 30 M  | bound by maxDepth → **5 MB** |
|     50 | 100 000 | 7   |       50 × 604 800 = 30 M  | bound by maxDepth → **50 MB** |
|    500 | 100 000 | 1   |       500 × 86 400 = 43 M  | bound by maxDepth → **50 MB** |
|    500 | 1 000 000 | 7 |     500 × 604 800 = 302 M  | bound by maxDepth → **500 MB** |
|  5 000 | 1 000 000 | 1 |    5 000 × 86 400 = 432 M  | bound by maxDepth → **500 MB** |

The whichever-is-smaller behaviour means **`maxDepth` typically governs**
disk use unless your retention is very short or your rate is very low.
Pick `maxDepth` so the worst-case disk number stays well inside your
data-root volume's free space, then set `maxAgeDays` for the staleness
limit you're willing to publish — a tag-value older than `maxAgeDays`
is unlikely to be useful telemetry anyway.

WAL adds roughly 30-50% overhead during heavy writes; SQLite checkpoints
fold it back into the main file periodically. Don't size for "main file +
full WAL" continuously — for a healthy broker you'll see well under that.

### Buffer file lifecycle

- **Location**: `{dataRoot}/buffer/{routeId}.db` (and adjacent `-wal` /
  `-shm` files while the buffer is open). One file per route — adding
  / removing routes adds / removes files.
- **Persistence**: the file is created on first start of a route and
  reused on every subsequent start. It is **not** deleted on graceful
  shutdown — that would be a data-loss bug. To purge, see below.
- **Backup**: SQLite WAL semantics make hot-copy unsafe — to back up a
  buffer file safely, stop the host first, copy `.db` and any `-wal`
  alongside it, then start. For most pilots backup isn't required;
  the buffer is a transient store, not a system of record.
- **Purge** (intentional data loss — operator decision): with the host
  stopped, delete the route's `.db` plus its `-wal` and `-shm`
  siblings. On next start the route opens a fresh buffer with empty
  cursors. Pilots typically purge between test campaigns to ensure
  clean baseline measurements.

### Observing the buffer at runtime

The host pushes per-route buffer stats onto the diagnostics surface on
every poll cycle. The soak runner's per-minute heartbeat shows them:

```
[soak] 14:02:30  txs=12000 ok=4000 fail=0 published=4000 rejected=0 buf[StoreAndForward]=0 sz=128KB rss=66MB
[soak] 14:03:30  txs=12500 ok=4170 fail=0 published=4170 rejected=0 buf[StoreAndForward]=0 sz=128KB rss=66MB
                                                                                          ↑ depth=0 → broker keeping up
[soak] 14:04:30  txs=13000 ok=4340 fail=0 published=4290 rejected=0 buf[StoreAndForward]=50 age=8s sz=152KB rss=67MB
                                                                                          ↑ depth=50, oldest unacked = 8s old
```

The same numbers also appear on the per-minute CSV (new columns:
`buffer_mode`, `buffer_depth`, `buffer_size_bytes`, `buffer_total_enqueued`,
`buffer_total_drained`, `buffer_dropped_by_capacity`,
`buffer_dropped_by_retention`, `buffer_oldest_unacked_age_sec`).

### Override at run time without editing the file

The soak runner's `--buffer-mode` flag rewrites every route's buffer
mode at load time. Useful for back-to-back A/B comparison with the
exact same config:

```powershell
# InMemory smoke for fast iteration
ModbusSoakRunner --config gateway-modbus-s7-1200.json --duration-min 5 --buffer-mode InMemory

# StoreAndForward soak using the SAME source config
ModbusSoakRunner --config gateway-modbus-s7-1200.json --duration-min 240 --buffer-mode StoreAndForward
```

The flag also auto-upgrades `AtMostOnce` delivery to `AtLeastOnce` when
StoreAndForward is selected — otherwise the host fails fast at config
load (the StoreAndForward validator requires durable delivery).

### Outage smoke / pilot recipe

To convince yourself S&F works on your specific deployment without
writing test code, run the soak runner against the StoreAndForward
sample, then physically remove network reachability to the broker for a
few minutes, then restore it. The per-minute heartbeat will show:

1. **Pre-outage**: `buf[...]=0`, `published` increments steadily.
2. **During outage**: `buf[...]=N` and growing, `published` flat,
   `rejected` may tick up briefly while MQTTnet decides the connection
   is dead.
3. **Post-outage**: `buf[...]=N` shrinking back to 0, `published`
   surges as the queue drains, then resumes steady increment.

Final `total_publish_successes` should equal `total_transactions` ×
(typical-points-per-transaction-ratio) within a tolerance — i.e. zero
loss across the outage window.

---

## `gateway-focas2-cnc.json` — Fanuc CNC over FOCAS2, production shape

Single-CNC, single-sink layout for a Fanuc controller speaking native
FOCAS2 to EdgeConnect, with the same StoreAndForward + AtLeastOnce
production discipline as the Modbus S&F sample.

The FOCAS2 adapter is **polling-only** (FOCAS2 is not a subscription
protocol). The `polling.intervalMs = 1000` is typical for a Fanuc 30i-B
running a single program — most CNC tags do not change faster than once
per second, and the FOCAS2 library spends ~5-50 ms per data-point read,
so a tight scan rate burns handle budget without producing more useful
data.

### Source block — FOCAS2

```json
"sources": [{
  "instanceId":   "focas2-cnc01",
  "protocolName": "focas2",
  "deviceId":     "CNC-01-Line1",
  "deviceClass":  "cnc",
  "polling":      { "intervalMs": 1000 },
  "connection": {
    "ipAddress":           "192.168.1.101",
    "port":                8193,
    "timeoutSeconds":      10,
    "keepAlive":           true,
    "initialBackoffMs":    5000,
    "maxBackoffMs":        120000,
    "backoffMultiplier":   2.0,
    "maxConnectRetries":   5,
    "dataPoints": [
      "Status/", "Program/", "Axes/", "Spindle/",
      "Alarms/", "Production/", "Tool/"
    ]
  }
}]
```

`dataPoints` is a list of FOCAS2 collector prefixes. Empty list = collect
everything available; an explicit list narrows the scope (useful when a
CNC has thousands of axis variables and you only need spindle + parts
count). See [`docs/adapter-sdk/focas2-adapter.md`](../adapter-sdk/focas2-adapter.md)
for the full collector + tag-name reference.

### Prerequisites — the Fanuc DLL

The FOCAS2 native library (`Fwlib64.dll` on Windows, `libfwlib32.so` on
Linux) is **proprietary and licensed by Fanuc**. The customer must:

1. Procure a license from Fanuc (or have one with the CNC purchase)
2. Place the DLL alongside `ElpisEdgeConnect.Host.exe` on the gateway
3. Confirm with their Fanuc rep that the licensed library can be
   deployed on an edge gateway adjacent to the CNC (as opposed to only
   on the OEM-bundled HMI)

Each gateway needs its own copy / license. This is **non-negotiable** —
we cannot redistribute the Fanuc library with EdgeConnect.

For full deployment + troubleshooting reference, see
[`docs/adapter-sdk/focas2-adapter.md`](../adapter-sdk/focas2-adapter.md).

### What gets emitted

The FOCAS2 collectors emit canonical tag names from the
[CNC vocabulary contract](../../../shared-knowledge/contracts/cnc-vocabulary.md).
With the sample's `dataPoints` list, expect tags like:

```
eremos/gw-line1-cnc-edge/cnc/focas2-cnc01/running           → "True"
eremos/gw-line1-cnc-edge/cnc/focas2-cnc01/mode              → "AUTO"
eremos/gw-line1-cnc-edge/cnc/focas2-cnc01/spindle_rpm       → "1450"
eremos/gw-line1-cnc-edge/cnc/focas2-cnc01/feed_rate         → "250.5"
eremos/gw-line1-cnc-edge/cnc/focas2-cnc01/parts_count       → "1247"
eremos/gw-line1-cnc-edge/cnc/focas2-cnc01/alarm_active      → "False"
eremos/gw-line1-cnc-edge/cnc/focas2-cnc01/axes/x/absolute   → "-127.453"
...
```

EREMOS V2 subscribes to `eremos/+/cnc/+/+` and picks up every tag
automatically.

### Operational limits

- **FOCAS2 handle budget**: most controllers allow ~8 simultaneous
  handles. EdgeConnect uses 1 per source instance (with `keepAlive=true`).
  If the customer also runs Fanuc's own MT-CONNECT-Adapter or another
  data-collection tool on the same CNC, coordinate so the total stays
  under the controller's limit.
- **Polling cost**: with `keepAlive=true`, each poll cycle takes
  ~5 ms × number of collectors. With the sample's 7 collectors, expect
  ~35-50 ms per poll. At `intervalMs=1000` that leaves ~95% idle.
- **No simulator**: FOCAS2 has no public simulator. Unit tests cover
  decode + state-machine logic; real-machine integration is gated on
  customer hardware. See
  [`docs/protocol-certification-matrix.md`](../protocol-certification-matrix.md)
  for the certification status.
