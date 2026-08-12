# ElpisEdgeConnect.Sources.ModbusTcp

Modbus TCP source adapter for Elpis EdgeConnect. Implements `ISourceAdapter`
and slots into the host's source-supervisor loop alongside FOCAS2 and
MTConnect.

## Status

**Phase 3 · Milestone F1 — connection + transaction layer.** This assembly
currently delivers:

- `ModbusTcpSourceAdapter` lifecycle (initialize / start / stop / health / validate)
- `IModbusClient` abstraction + `FluentModbusClient` production implementation
- `ModbusConnectionManager` — connect, disconnect, exponential backoff, circuit breaker, single-in-flight wire lock
- `ModbusTransactionExecutor` — FC01 / FC02 / FC03 / FC04 with per-transaction retry, slave-exception mapping, and FC-limit validation

Not yet implemented (later milestones in the plan):

| Milestone | Delivers |
|-----------|----------|
| F2 | Scan-group planner + block optimizer (`ScanPlan`) — groups tags by `(scanRateMs, unitId, registerClass)` and packs them into FC-compliant blocks |
| F3 | Decoder + byte-order support (`AB`, `BA`, `ABCD`, `CDAB`, `BADC`, `DCBA`), all datatypes, scale/offset |
| F4 | CSV tag import + template profiles |
| F5 | Per-block RTT / error-count metrics surfaced via `AdapterHealth` |

`PollAsync` returns an empty list until F3 wires the decoder in. Tests can
exercise the transaction layer directly via `ExecuteAsyncInternal` (exposed
through `InternalsVisibleTo`).

## Architecture

```
ModbusTcpSourceAdapter : ISourceAdapter
        │
        ├── ModbusConnectionManager
        │     ├── IModbusClient (FluentModbusClient in prod, FakeModbusClient in tests)
        │     ├── exponential backoff
        │     └── circuit breaker (Closed / Open / HalfOpen)
        │
        └── ModbusTransactionExecutor
              ├── request validation (FC-limit, address range)
              ├── retry budget for fatal transport errors
              └── slave-exception → AdapterError mapping
```

See `docs/PHASE3_EXECUTION_PLAN.md §5` for the target internal architecture
diagram and the per-tag configuration shape the planner will consume.

## Encapsulation

The `encapsulation` config field accepts `"tcp"` (default) and `"rtuOverTcp"`.
**Only `tcp` is wired at F1**; `rtuOverTcp` currently rejects the connect call
with a clear error. Wire support for RTU-over-TCP lands with the pilot work
(milestone K) — the config field is available from F1 so routes authored now
won't need migration when that wire support arrives.

## Configuration example

```json
{
  "instanceId": "plc-line1",
  "protocolName": "modbustcp",
  "deviceId": "Line1MainPlc",
  "enabled": true,
  "connection": {
    "host": "192.168.10.50",
    "port": 502,
    "encapsulation": "tcp",
    "defaultUnitId": 1,
    "connectTimeoutMs": 2000,
    "requestTimeoutMs": 1000,
    "keepAlive": true,
    "maxTransactionRetries": 2,
    "initialBackoffMs": 2000,
    "maxBackoffMs": 60000,
    "backoffMultiplier": 2.0,
    "circuitBreakerThreshold": 5,
    "circuitBreakerResetMs": 30000
  },
  "polling": { "intervalMs": 1000 }
}
```
