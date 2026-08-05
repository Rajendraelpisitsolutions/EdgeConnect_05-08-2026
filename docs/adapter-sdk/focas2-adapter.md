# FOCAS2 source adapter — deployment and configuration guide

**Status:** Phase 2 (closed for lab use; real-hardware pilot pending)
**Project:** `src/ElpisEdgeConnect.Sources.Focas2/`
**Tests:** `tests/ElpisEdgeConnect.Sources.Focas2.Tests/` (75 unit + 1 end-to-end in Integration.Tests)

## 1. What this adapter does

Collects data from Fanuc CNC controllers (FS0i, FS30i, FS31i, FS35i, etc.)
over Ethernet using Fanuc's FOCAS2 native library (`Fwlib64.dll` on
Windows, `libfwlib32.so` on Linux). Emits canonical data points for:

| Collector | Tags emitted |
|---|---|
| Status | `status/run_state`, `status/auto_mode`, `status/emergency_stop`, `status/motion`, `status/alarm_flag` |
| Program | `program/main_program`, `program/running_program` |
| Axes | `axes/{name}/{absolute,machine,relative,distance_to_go}` per axis |
| Spindle | `spindle/speed`, `spindle/load`, `axes/feed_rate` |
| Alarms | `alarms/active`, `alarms/count` |
| Production | `production/cycle_time`, `production/parts_count` |
| Tool | `tool/number`, `tool/offset_{h,d}` |
| MT-LINKi | aggregated rollups for Mitsubishi MT-LINKi compatibility |

Polling-only (FOCAS2 is not a subscription protocol). Browse is supported
(`BrowseTagsAsync`). The `TestConnect` capability is **not** declared
because `ISourceAdapter` does not yet carry a `TestConnectAsync` method —
this will change when the Phase 4 management API lands.

### Studio wizard (M.2b.3)

A guided wizard for adding FOCAS2 sources is available at
`/sources/new/focas2`. It exposes the same configuration surface this
document describes — IP/port, timeout, keep-alive, backoff, data-point
group picker — and produces a draft via `/api/v1/config/drafts` that the
operator then validates and applies via the Configuration page.

The wizard also offers a **Browse Controller** button that runs a
one-shot probe against the configured IP:port. The probe lives behind
`POST /api/v1/sources/browse/focas2` and is implemented by
`Focas2BrowseService` in the Management project. It builds a throwaway
`Focas2SourceAdapter` instance, drives the lifecycle
`Initialize → Start → BrowseTagsAsync → bounded(Stop + Dispose)`, and
returns the discovered axis names + tag definitions + CNC series/type
+ a correlation `ProbeId`.

The probe **overrides** the wizard's runtime values to enforce
interactive responsiveness:

- `MaxConnectRetries = 1` (no retry loop)
- `TimeoutSeconds = min(8, max(1, request.TimeoutSeconds))`
- 15s overall probe budget
- 12s combined Stop+Dispose cleanup bound

Operators should be aware that "I set `TimeoutSeconds: 60` and Browse
finished in 8s" is expected — Browse uses its own probe-only config so
the UI never freezes for a customer-configured production timeout. The
wizard surfaces this distinction via a caption beneath the Browse
button.

See ADR-0011 (`docs/decisions/0011-browse-controller-reuses-browsetagsasync.md`)
for the architectural rationale.

### Demo mode (M.2b.3.1)

For sales demos and dev testing without a real Fanuc CNC or the
`Fwlib64.dll` / `libfwlib32.so` native library installed, set:

```bash
EDGECONNECT_FOCAS2_FAKE_MODE=true
```

When this environment variable is truthy (`true`, `1`, or `yes`,
case-insensitive) at process start, every FOCAS2 source — including
the Browse Controller probe — is backed by an in-process **synthetic
CNC emulator** (`Focas2DemoApi`). No native library is required.

The synthetic CNC is a deterministic, time-driven state machine
running a 60-second cycle:

| Phase | Duration | What it shows |
|---|---|---|
| Reset | 10s | Spindle off, parts count steady, run-state = RESET |
| Start | 40s | Spindle ramps 0→3000 rpm, cutting signal active, axis positions animate sinusoidally |
| Stop | 10s | Spindle off, parts counter increments, run-state = STOP |

It also surfaces tool changes (T1/T5/T9 cycling), a periodic alarm
(SV0432 every 4th cycle), and plausible MtLinki diagnostics (servo
temps ~35±3 °C, fans OK, batteries OK).

**Toggling requires restart.** The env var is read once and cached
for the process lifetime so the demo state is stable across the
whole run.

**Demo mode does NOT bypass licensing.** A FOCAS2 source registered
in demo mode still requires the `source-focas2` license module to
be enabled. Sales-demo distributions run either with no license file
loaded (permissive dev path) or with a demo license that has the
module enabled.

When demo mode is active, four signals make it visible:

1. A distinctive **stderr line** at startup: `[startup][CRITICAL] FOCAS2 FAKE MODE ACTIVE …`. Log monitoring should pattern-match this specific phrase as informational, NOT as a system failure.
2. A sticky amber **banner** across every Studio page.
3. The **Prometheus gauge** `edgeconnect_focas2_fake_mode_enabled` reads `1` (always present; reads `0` in production).
4. A **Studio Diagnostics event** (`GATEWAY.FOCAS2_FAKE_MODE_ACTIVATED`) appended once at boot.

Per-source visibility is surfaced via the adapter's health metric
`demoMode: true` for any source backed by the synthetic emulator.

See ADR-0012 (`docs/decisions/0012-focas2-demo-mode.md`) for the
architectural rationale + the canonical synthetic-CNC profile.

## 2. Prerequisites: the Fanuc native library

The FOCAS2 library is **not open-source**. It must be obtained from Fanuc
under their licensing terms. Typical distribution names:

| Platform | File name | Notes |
|---|---|---|
| Windows x64 | `Fwlib64.dll` | Default — ships with most modern CNCs |
| Windows x86 | `Fwlib32.dll` | 32-bit installs only |
| Linux | `libfwlib32.so` | Name kept as-is even on 64-bit kernels |

### Deployment

Place the library file **next to the host binary** or on the platform's
standard library search path:

- **Windows:** drop `Fwlib64.dll` alongside `ElpisEdgeConnect.Host.exe`.
  No registration required.
- **Linux:** place `libfwlib32.so` in the same directory, or install to
  `/usr/local/lib` and run `ldconfig`.

The adapter uses `NativeLibrary.SetDllImportResolver` (see
`Focas2Interop.cs` — `ResolveFocasLibrary`) to pick the right file name
at runtime. If no candidate loads, it throws `DllNotFoundException` with
a message listing the file names it tried, so the operator knows which
one Fanuc shipped them.

### Verifying the deployment

```powershell
# Windows — verify the DLL is visible to the host's loader
dumpbin /dependents Fwlib64.dll

# Linux
ldd libfwlib32.so
```

A missing dependency (e.g. `MSVCR120.dll` on Windows) will manifest as
`DllNotFoundException` at first connect. Install the Visual C++ 2013
redistributable if needed.

## 3. Configuration

FOCAS2 sources are declared in the gateway's `current.json` under
`sources`. The per-source `connection` object holds FOCAS2-specific
fields.

### Minimal example

```json
{
  "gateway": { "gatewayId": "gw-factory-1", "gatewayName": "Factory Gateway" },
  "sources": [
    {
      "instanceId": "focas-lathe-1",
      "protocolName": "focas2",
      "deviceId": "lathe1",
      "deviceName": "Mori Seiki NL2500",
      "polling": { "intervalMs": 2000, "maxConsecutiveErrors": 5 },
      "connection": {
        "ipAddress": "192.168.1.101"
      }
    }
  ],
  "sinks": [
    { "instanceId": "mqtt-primary", "protocolName": "mqtt", "connection": { ... } }
  ],
  "routes": [
    {
      "routeId": "lathe-1-to-mqtt",
      "sourceInstanceId": "focas-lathe-1",
      "sinkInstanceIds": ["mqtt-primary"],
      "enabled": true
    }
  ]
}
```

Only `ipAddress` is required in the `connection` block. All other fields
have defaults; override them when the device or network demands it.

### Full `connection` field reference

| Field | Type | Default | Notes |
|---|---|---|---|
| `ipAddress` | string | **required** | IPv4 of the CNC |
| `port` | int | `8193` | FOCAS2 port, rarely changed |
| `timeoutSeconds` | int | `10` | Socket connect timeout |
| `keepAlive` | bool | `true` | Keep handle open across polls (~5 ms/poll) vs. reconnect each poll (~20–50 ms) |
| `dataPoints` | string[] | `[]` (all) | Hierarchical prefixes to collect. Examples: `["Status/RunState", "Axes/", "Spindle/Speed"]` |
| `initialBackoffMs` | int | `5000` | Delay after first failed connect |
| `maxBackoffMs` | int | `120000` | Cap on exponential backoff |
| `backoffMultiplier` | double | `2.0` | Applied on every consecutive failure |
| `maxConnectRetries` | int | `5` | Handle-allocation attempts per connect pass |

### `polling` field

The outer `polling` object controls pacing:

- `intervalMs` (default `1000`): minimum wall-clock time between poll
  *starts*. The adapter tracks the start of the previous poll; if the
  current poll is invoked before `intervalMs` has elapsed, it waits. The
  FOCAS2 library itself has no hard minimum, but **do not go below
  ~1000 ms in production** — aggressive polling shortens the CNC's
  Ethernet hardware life and can trigger controller watchdog resets.

### Selecting `dataPoints`

The `dataPoints` list is a prefix filter. An entry like `"Axes/"`
collects every axis-related tag; an empty list collects every available
tag. The Status collector always runs regardless (5 tags per poll) —
it's the handle liveness check.

Common profiles:

- **Status + alarms only** (cheap, ~6 tags/poll):
  `["Status/RunState", "Alarms/Active"]`
- **Production monitoring**: add `["Production/CycleTime", "Production/PartsCount"]`
- **Full machine state**: leave empty

## 4. Identity and routing

Every point emitted by this adapter carries:

| Field | Source |
|---|---|
| `GatewayId` | `IGatewayIdentity.GatewayId` from the host (persisted UUID under `{dataRoot}/identity`) |
| `SourceInstanceId` | The `instanceId` field above |
| `ProtocolName` | Always `"focas2"` |
| `DeviceId` | The `deviceId` field above |
| `DeviceName` | The `deviceName` field above (optional) |
| `TagName` / `TagPath` | Per-collector (see §1 table) |

The default MQTT sink's PerTag topic template
(`eremos/{gatewayId}/cnc/{sourceId}/{tagName}`) resolves to topics like:

```
eremos/3f0d1de0-…/cnc/focas-lathe-1/status_run_state
eremos/3f0d1de0-…/cnc/focas-lathe-1/axes_x_absolute
```

Slashes inside the tag name (`status/run_state`) are sanitized to
underscores by `MqttTopicResolver` so each canonical point lands on
exactly one topic level.

## 5. Operations

### Backoff behavior

- First connect failure → wait `initialBackoffMs` before next attempt
- Every subsequent failure → multiply wait by `backoffMultiplier`, capped at `maxBackoffMs`
- Successful connect → reset counter to 0

The adapter stays in `Running` (or `Degraded`) through backoff — it does
not move to `Failed`. The design assumes "cable unplugged for five
minutes" is a recoverable condition and customer data should keep
flowing elsewhere.

### Handle thread-safety

FOCAS2 calls are **not** thread-safe per handle. All calls for a given
handle are serialized on a dedicated `Focas2Thread` instance (one per
adapter). This is a hard library requirement, not a design choice —
violating it causes silent data corruption and stuck handles.

### Metrics surfaced via `CheckHealthAsync`

```
pollAttempts                 long    total PollAsync invocations
pollSuccesses                long    invocations that returned points
pollFailures                 long    invocations that hit a fatal error
consecutiveConnectFailures   int     resets on any successful connect
connected                    bool    current handle state
endpoint                     string  "{ip}:{port}"
cncSeries, cncType, axisCount  (populated after first successful connect)
```

These propagate to Prometheus via the diagnostics collector.

## 6. Migration from the legacy `FanucCncDataBridge`

The Phase 2 adapter is a structural migration of
`src/ElpisEdgeConnect/DataSources/Focas2DllDataSource.cs` and
`src/ElpisEdgeConnect/Focas2/Focas2Interop.cs`. Functional mapping:

| Legacy file | New file | Change |
|---|---|---|
| `Focas2DllDataSource.cs` | `Focas2SourceAdapter.cs` | Implements `ISourceAdapter` instead of the legacy data-source interface; splits collection into per-topic collectors; adopts the canonical data model |
| `Focas2Interop.cs` (P/Invoke) | `Focas2Interop.cs` (same name, new project) | Added `NativeLibrary.SetDllImportResolver` for cross-platform DLL resolution; behavior of P/Invoke signatures is identical |
| `Focas2Connection.cs` (inline) | `Focas2ConnectionManager.cs` | Extracted into a dedicated type; adds exponential backoff with `MaxConnectRetries`, `InitialBackoffMs`, `BackoffMultiplier`, `MaxBackoffMs`; `HandleFatalError` path formalized |
| `Focas2Thread.cs` (implicit) | `Focas2Thread.cs` | Explicit dedicated thread per adapter for handle affinity; previously the legacy code relied on implicit thread ordering |
| (scattered collectors) | `Collectors/*.cs` | Eight collectors — `Status`, `Program`, `Axis`, `Spindle`, `Alarm`, `Production`, `Tool`, `MtLinki` — each with a single `Collect(…)` entry point. Behavior preserved, value mappings (e.g. `Run=3 → "Running"`) identical. |
| (config records in `Focas2/`) | `Focas2SourceConfiguration.cs` | Inherits `Core.Adapters.SourceConfiguration`; adds `FromSourceInstance(SourceInstanceConfig)` for JSON-driven launches |
| n/a | `IFocas2Api.cs` + `Focas2NativeApi.cs` | New abstraction separating the P/Invoke seam from the adapter logic, enabling unit tests via `FakeFocas2Api` |

### Not ported

The legacy codebase included one-off diagnostic dump routines and a
manual "fill the gap" backfill path that were tied to the legacy
in-process database. These are not ported — the canonical pipeline's
store-and-forward buffer (`SqliteBuffer`) handles the same use case
generically for every source.

## 7. Known limitations

1. **Real-hardware verification is still pending.** All 75 unit tests +
   the integration test use `FakeFocas2Api`. Once the customer provides
   CNC access and `Fwlib64.dll`, a live-hardware smoke test should run
   against at least one series (preferably 0i-F or 30i-B).
2. **`TestConnect` capability not exposed.** The flag is gated behind
   `ISourceAdapter` getting a `TestConnectAsync` method — Phase 4.
3. **No license gate on `AddFocas2Source`.** Per locked decision #4,
   protocols should be "activated by license at DI registration time."
   This is explicitly Phase 3 work.
4. **Single-CNC per adapter instance.** To monitor multiple CNCs, declare
   one `sources` entry per device. This is by design — each adapter
   owns one handle + thread for FOCAS2 thread-safety.

## 8. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `DllNotFoundException: Could not load the Fanuc FOCAS2 native library` at startup | Missing DLL on the load path | Confirm `Fwlib64.dll` (Win) or `libfwlib32.so` (Linux) is beside the host binary |
| Adapter stuck in `Degraded`, `consecutiveConnectFailures` rising | Controller unreachable or returning `EW_HANDLE` | Check IP/port, verify FOCAS2 Ethernet is enabled on the CNC, look for competing FOCAS2 clients (hard limit of ~8 simultaneous handles on most controllers) |
| All polls return empty but `connected = true` | `StatusCollector` failed silently — typically a PMC parameter mismatch | Check host logs at `Debug` level; most status-read failures are recoverable |
| Points flow but `cncSeries = null` | `EnsureSystemInfo` failed on first connect | Usually benign; system info reads succeed on the next connect pass |
| Timestamps drift | `deviceTimestamp` uses host `DateTime.UtcNow` | FOCAS2 doesn't expose a device clock; Phase 5 will wire in NTP-synced timestamping if the customer needs it |

## 9. See also

- `src/ElpisEdgeConnect.Sources.Focas2/README.md` — dev onboarding + build instructions
- `docs/adapter-sdk/source-adapter-contract.md` — the generic `ISourceAdapter` contract every adapter implements
- `docs/ARCHITECTURE_BLUEPRINT.md` §4.2 — canonical model and adapter responsibilities
- `tests/ElpisEdgeConnect.Integration.Tests/Focas2ToMqttEndToEndTests.cs` — end-to-end scenario to reference when debugging wiring
