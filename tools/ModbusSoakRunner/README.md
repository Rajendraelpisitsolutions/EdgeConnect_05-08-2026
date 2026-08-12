# ModbusSoakRunner

Long-running EdgeConnect harness for the Phase A' soak. Brings up the
real Host (same composition root as production) against a `gateway.json`,
captures per-minute `AdapterHealth` snapshots to CSV, and prints a
pass/fail summary against the KepServer-benchmarked acceptance criteria.

## Usage

```powershell
# 5-minute connectivity smoke against the cloud broker
dotnet run --project tools/ModbusSoakRunner -c Release -- `
  --config docs/samples/gateway-modbus-s7-1200.json `
  --duration-min 5 `
  --csv soak-smoke.csv

# 4-hour soak
dotnet run --project tools/ModbusSoakRunner -c Release -- `
  --config docs/samples/gateway-modbus-s7-1200.json `
  --duration-min 240 `
  --csv soak-4h.csv

# 4-hour soak WITH end-to-end EREMOS V2 verification (Azure host)
# Set credentials via env vars first so they don't end up in shell history
$env:EREMOS_USERNAME = "admin@eremos.com"
$env:EREMOS_PASSWORD = "<your-password>"

dotnet run --project tools/ModbusSoakRunner -c Release -- `
  --config docs/samples/gateway-modbus-s7-1200.json `
  --duration-min 240 `
  --csv soak-4h.csv `
  --eremos-api http://eremosv2.centralindia.cloudapp.azure.com/api/v1/ `
  --eremos-device-class plc
```

The runner POSTs to `{api}/auth/login` on startup, captures the bearer
token, sends it on every subsequent historian request, and re-auths
transparently on 401. Tokens that expire mid-soak are handled
automatically.

## Acceptance criteria evaluated

| Criterion                         | Threshold     | When applied |
|-----------------------------------|---------------|--------------|
| Source delivery ratio             | ≥ 99.9%       | always |
| Publish delivery ratio            | ≥ 99.9%       | always |
| RSS at end of run                 | ≤ 150 MB      | always |
| RSS growth over the soak          | ≤ 20%         | always |
| Average CPU per-core              | ≤ 5%          | always |
| End-to-end delivery (EREMOS)      | ≥ 99.0%       | only when `--eremos-api` supplied |

### End-to-end EREMOS V2 verification

When `--eremos-api <baseUrl>` is supplied, the runner closes the loop
beyond "we published" to "they actually ingested":

1. On startup, calls `GET {baseUrl}/api/v1/historian?deviceClass=plc&since={soakStart}`
   to capture a baseline tag count.
2. Every minute, polls again for the latest count and logs the delta.
3. At the end, asserts `(eremos_delta / publish_count) >= 99.0%`.

The 99.0% threshold (vs. 99.9% for the local source/publish ratios) leaves
margin for one-off network / broker hiccups between EdgeConnect and EREMOS.
If the API is unreachable or the response shape isn't recognized, the
runner skips the criterion (logs SKIPPED) but does not fail the soak.

Auth: pass a JWT via `--eremos-jwt <token>` if the API requires it. The
runner sends it as `Authorization: Bearer <token>`.

Per-block RTT (p50 / p95) is captured by F5's diagnostics into
`AdapterHealth.Metrics["blockMetrics"]`; the CSV records one row per
minute summarizing the rolled-up counters. For p95 trend analysis,
import the CSV into a spreadsheet — the column set is stable enough to
chart directly.

## Exit codes

| Code | Meaning |
|------|---------|
| 0    | All acceptance criteria passed |
| 1    | One or more criteria failed, or fatal runtime error |
| 2    | Argument or config-load error |

## Workflow

1. Start the [pymodbus simulator](../../tests/ElpisEdgeConnect.Integration.Tests/ModbusSimulator/README.md)
   on the same box: `python server.py`
2. Edit `docs/samples/gateway-modbus-s7-1200.json` so the source's
   `connection.host` is `127.0.0.1` and `connection.port` is `5020`.
3. Make sure the MQTT broker reachable from the sink config block —
   for the cloud broker that's `20.197.8.189:1883` (anonymous).
4. Run the smoke, eyeball the CSV / summary.
5. If smoke is green, run the 4-hour soak. Walk away. Come back to a
   PASS / FAIL line at the bottom.

## What gets witnessed externally

The runner only sees what `AdapterHealth.Metrics` reports — that's
enough to prove "the adapter polled" and "the sink published". To prove
the broker actually accepted and EREMOS ingested, witness from outside:

```bash
mosquitto_sub -h 20.197.8.189 -p 1883 -t 'eremos/+/plc/+/+' -v -C 50
```

Or query EREMOS V2 directly:

```bash
curl 'https://eremos-api/api/v1/historian?deviceClass=plc&limit=50'
```

(EREMOS V2's `?deviceClass=plc` filter shipped on
`feature/mqtt-device-class-segment` commit `ea1113b`.)
