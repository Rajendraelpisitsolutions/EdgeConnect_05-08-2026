# MTConnect source adapter — deployment and configuration guide

**Status:** Phase 2 — complete against lab fixtures; real-Agent pilot pending.
**Project:** `src/ElpisEdgeConnect.Sources.MTConnect/`
**Tests:** `tests/ElpisEdgeConnect.Sources.MTConnect.Tests/` (38 unit) + 1 end-to-end in Integration.Tests

## 1. What this adapter does

Polls the MTConnect Agent's `/current` endpoint over HTTP on every
`PollIntervalMs`, parses the XML response, and emits canonical data
points. Works with any CNC that exposes an MTConnect Agent:

- Brother Speedio
- Mazak
- Okuma THINC-OSP
- DMG Mori / Okuma MTConnect adapters
- Haas
- Makino, Doosan, Hurco, Toyoda, etc.

MTConnect is an open, royalty-free standard — **no proprietary native
library is required**, which is the big operational difference vs. FOCAS2.
The adapter's only runtime dependency is an HTTP client.

### Emitted canonical tags

| Tag | Source (MTConnect element) | Mapping |
|---|---|---|
| `status/run_state` | `<Execution>` | `ACTIVE → Running`, `INTERRUPTED/FEED_HOLD → Hold`, `STOPPED → Stop`, `READY → Reset` |
| `status/controller_mode` | `<ControllerMode>` | `AUTOMATIC → MEM`, `MANUAL → JOG`, `MANUAL_DATA_INPUT → MDI`, `EDIT → EDIT` |
| `status/emergency_stop` | `<EmergencyStop>` | `TRIGGERED → true`, otherwise `false` |
| `program/main_program` | `<Program>` | raw string |
| `program/running_program` | `<SubProgram>` (fallback: `<Program>`) | raw string |
| `spindle/speed` | `<SpindleSpeed>` or `<RotaryVelocity>` | double |
| `spindle/load` | `<SpindleLoad>` or `<Load>` | double |
| `axes/feed_rate` | `<PathFeedrate>` | double |
| `production/parts_count` | `<PartCount>` | long |
| `production/cycle_time` | `<CycleTime>` or `<ProcessTimer>` | double seconds |
| `axes/{name}/absolute` | `<Position name="X" subType="ACTUAL">` | double |
| `axes/{name}/machine` | `<Position name="X" subType="MACHINE">` | double |
| `alarms/count` | count of `<Fault>` under `<Condition>` | int |
| `alarms/first_fault` | first `<Fault>` message (or native code) | string; empty when no active fault |

Tag names deliberately mirror the FOCAS2 adapter's names where the
semantics overlap, so downstream consumers can treat `status/run_state`
uniformly regardless of source protocol.

Items marked `UNAVAILABLE` in the Agent response are silently skipped —
the tag is simply not emitted for that poll.

## 2. Prerequisites

**No native library required.** The MTConnect Agent runs on the CNC side
(or on a PC beside it, for older CNCs) and exposes an HTTP endpoint.

You need:

1. Network reachability from the EdgeConnect host to the Agent's HTTP
   listen port (typically 5000).
2. The Agent's base URL, e.g. `http://192.168.1.10:5000/`.
3. *(Optional)* the Agent-side device name when the Agent hosts multiple
   devices. Without this, the adapter hits the Agent's default device.

### Verifying the Agent manually

```bash
curl -s http://192.168.1.10:5000/probe   | head -40    # should return MTConnectDevices XML
curl -s http://192.168.1.10:5000/current | head -80    # should return MTConnectStreams XML
```

If either returns a non-200 or empty body, the adapter will surface
the HTTP status / transport error through `CheckHealthAsync` and back
off before retrying.

## 3. Configuration

```jsonc
{
  "instanceId": "mtc-lathe-1",
  "protocolName": "mtconnect",
  "deviceId": "lathe1",
  "deviceName": "Mazak Integrex",
  "polling": { "intervalMs": 2000 },
  "connection": {
    "agentBaseUrl": "http://192.168.1.10:5000/"
  }
}
```

### Full `connection` field reference

| Field | Type | Default | Notes |
|---|---|---|---|
| `agentBaseUrl` | string | **required** | Absolute `http://` or `https://` URL |
| `agentDeviceName` | string | null | Agent-side device name when multi-device; otherwise null |
| `timeoutSeconds` | int | `10` | HTTP request timeout |
| `initialBackoffMs` | int | `2000` | Delay after first failure |
| `maxBackoffMs` | int | `60000` | Cap on exponential backoff |
| `backoffMultiplier` | double | `2.0` | Applied on every consecutive failure |
| `degradeAfterConsecutiveFailures` | int | `3` | After this many consecutive failures, transition to `Degraded` |

### `polling.intervalMs` guidance

- Default is `1000` (1 Hz).
- MTConnect Agents are typically fine with 0.5 Hz to 5 Hz polling; beyond
  that the Agent itself becomes the bottleneck.
- For long-horizon production monitoring, 2–5 seconds is plenty.
- Setting `0` disables pacing (used by unit tests only — don't do this
  in production or the Agent's own CPU becomes the bottleneck).

## 4. Identity and routing

Every point emitted by this adapter carries:

| Field | Source |
|---|---|
| `GatewayId` | `IGatewayIdentity.GatewayId` (persistent UUID under `{dataRoot}/identity`) |
| `SourceInstanceId` | The `instanceId` field above |
| `ProtocolName` | Always `"mtconnect"` |
| `DeviceId` / `DeviceName` | The `deviceId` / `deviceName` fields |
| `TagName` / `TagPath` | Per-tag (see §1 table) |

Default MQTT PerTag topic shape:
```
eremos/{gatewayId}/cnc/{sourceId}/{tagName}
```
e.g. `eremos/{uuid}/cnc/mtc-lathe-1/status_run_state` (slashes inside
the tag name get sanitized to underscores by the MQTT topic resolver).

## 5. Operations

### Backoff + degradation behaviour

1. **Success** → `consecutiveFailures = 0`, state stays `Running`, backoff resets.
2. **Single failure** → `consecutiveFailures++`, adapter backs off for `initialBackoffMs`; state stays `Running`.
3. **Sustained failures** → once `consecutiveFailures >= degradeAfterConsecutiveFailures`, state transitions to `Degraded`. Data flow on other routes is unaffected (per-adapter isolation).
4. **Recovery** → first successful poll resets state to `Running`.

The adapter **never** transitions to `Failed` on transient HTTP errors —
the design assumes the Agent can restart without taking the host down.

### Metrics surfaced via `CheckHealthAsync`

```
pollAttempts                 long    total PollAsync invocations
pollSuccesses                long    successful collections
pollFailures                 long    HTTP / parse failures
consecutiveFailures          int     resets on any success
probeCompleted               bool    true after first /probe success
agentBaseUrl                 string  configured endpoint
agentDeviceName              string  or empty
deviceUuid                   string  (populated after /probe)
deviceManufacturer           string  (populated after /probe when advertised)
lastFailureAtUtc             string  ISO-8601, optional
```

These propagate through the diagnostics collector to Prometheus.

## 6. Migration from the legacy `MTConnectDataSource`

The Phase 2 adapter is a structural migration of
`src/ElpisEdgeConnect/DataSources/MTConnectDataSource.cs`.

| Legacy file | New file | Change |
|---|---|---|
| `MTConnectDataSource.cs` | `MTConnectSourceAdapter.cs` | Implements `ISourceAdapter` against the Phase 1 Core contracts; state machine formalized (`AdapterState` transitions); HTTP work split across `IMTConnectClient` + `MTConnectHttpClient` |
| (inline HTTP client in legacy) | `IMTConnectClient` + `MTConnectHttpClient` | New seam enables unit tests via `FakeMTConnectClient` without a live Agent |
| (inline XML parse in legacy) | `MTConnectStreamParser.cs` | Pure function — takes XML + factory, emits canonical points; unit-testable against fixture XML files |
| (legacy mapping to `CncMachineData`) | `MTConnectTagMap.cs` | Canonical `TagName` / `TagPath` / `ValueType` / `Unit` metadata per emitted tag |
| `MTConnectSettings` class | `MTConnectSourceConfiguration` | Inherits `Core.Adapters.SourceConfiguration`; adds `FromSourceInstance(SourceInstanceConfig)` for JSON-driven launches |

### Not ported

- The legacy "disconnect flag" and `MachineStatus.Offline/Error/Online`
  enum. Replaced by the unified `AdapterState` state machine and the
  diagnostics collector's state reporting.
- The hand-rolled per-axis name pattern matching (`Xact` / `Xpos` /
  `Xmachine` / etc.). The new parser uses the standard MTConnect
  contract — `<Position name="X" subType="ACTUAL|MACHINE">`. Vendors
  that deviate from this may need an additional mapping pass; open an
  issue with a sample response and we'll extend the parser.

## 7. Known limitations

1. **No `/sample` streaming yet.** The adapter uses `/current` on every
   poll. `/sample` with long-poll (MTConnect's native subscription
   mechanism) would reduce load on the Agent and lower latency — a
   worthwhile follow-up.
2. **No `TestConnect` capability exposed.** Same reason as the FOCAS2
   adapter: `ISourceAdapter` contract has no `TestConnectAsync` method.
   Revisit when Phase 4's management API extends the contract.
3. **No license gate on `AddMTConnectSource`.** Per locked decision #4,
   protocols should be "activated by license at DI registration time" —
   Phase 3 work.
4. **Real-Agent smoke test is still pending.** All 38 unit tests + the
   integration test use `FakeMTConnectClient`. A live Brother / Mazak /
   Okuma Agent pilot is the next milestone.

## 8. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `MTCONNECT.HTTP_REQUEST_FAILED` at every poll | Agent not running, firewall, wrong URL | `curl {agentBaseUrl}probe` manually; check host/port |
| `MTCONNECT.HTTP_STATUS` (non-200) | Agent hit an internal error, or wrong device name | Inspect Agent logs; verify `agentDeviceName` matches a real device |
| `MTCONNECT.XML_PARSE_FAILED` | Agent returned malformed XML (rare) or HTML (proxy in the way) | Check for captive portals / reverse proxies between host and Agent |
| `MTCONNECT.NO_DEVICE_STREAM` | Agent returned a valid envelope with no `<DeviceStream>` | Almost always a `/current` request against a non-existent device name; clear `agentDeviceName` or correct it |
| All tags `UNAVAILABLE` at every poll | Agent is up but the machine is off / not wired | Agent's doing its job; once the CNC wakes up, values flow through automatically |
| Points flow but `deviceManufacturer` is missing | Agent's `<Description>` element lacks the attribute | Purely cosmetic — data still flows |

## 9. See also

- `src/ElpisEdgeConnect.Sources.MTConnect/README.md` — dev onboarding
- `docs/adapter-sdk/source-adapter-contract.md` — generic `ISourceAdapter` contract
- `docs/adapter-sdk/focas2-adapter.md` — the other CNC source adapter, for comparison
- `docs/config-authoring.md` — how to write `current.json`
- [MTConnect standard](https://www.mtconnect.org/) — for the authoritative element definitions
