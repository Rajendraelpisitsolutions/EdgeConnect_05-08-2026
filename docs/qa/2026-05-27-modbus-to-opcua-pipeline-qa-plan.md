# QA Test Plan — Modbus TCP → EdgeConnect → OPC UA Server → OPC UA Client (UaExpert)

**Document version:** 1.0
**Date:** 2026-05-27
**Product:** Elpis EdgeConnect (Connectivity Studio)
**Scope:** End-to-end pipeline from a Modbus TCP source through EdgeConnect's routing engine to an OPC UA Server sink consumed by an external OPC UA client.
**Target tester:** QA engineer (manual execution).
**Estimated execution time:** ~2 working days for full P1+P2 coverage; P3 cases add another day.

---

## 1. Pipeline under test

```
┌──────────────┐    Modbus     ┌──────────────────┐   Canonical    ┌─────────────────┐    OPC UA      ┌──────────┐
│   Modbus     │ ───TCP/502─→  │   EdgeConnect    │ ──pipeline──→  │   OPC UA Server │ ──opc.tcp───→  │ UaExpert │
│  simulator   │   (poll)      │  Source Adapter  │    + buffer    │   Sink Adapter  │   (subscribe)  │  client  │
│  (pymodbus)  │               │                  │                │                 │                │          │
└──────────────┘               └──────────────────┘                └─────────────────┘                └──────────┘
```

Every test case below exercises one or more layers of this pipeline.

---

## 2. Test environment

### 2.1 Hardware / OS
- **EdgeConnect host:** Windows 10/11 (production target). Tests should also pass on Linux when documented.
- **CPU:** 4 cores minimum
- **RAM:** 8 GB minimum
- **Disk:** 10 GB free (for SQLite store-and-forward buffer + logs)

### 2.2 Software prerequisites

| Component | Version | Purpose |
|-----------|---------|---------|
| EdgeConnect | This branch / latest master | System under test |
| Python | 3.12 | Modbus simulator runtime |
| pymodbus | `>=3.6,<3.8` | Simulator dependency |
| UaExpert | 1.7 or later | OPC UA client |
| Mosquitto (optional) | Latest | If MQTT-fanout regression tests are added later |
| Browser | Chrome / Edge latest | Connectivity Studio UI |

### 2.3 Network
- All components on `localhost` for primary testing.
- Repeat critical tests across machines (EdgeConnect + Modbus sim on host A, UaExpert on host B) to validate non-loopback behaviour.

### 2.4 Setup commands

**Modbus simulator:**
```powershell
cd C:\dev\EdgeConnect\tests\ElpisEdgeConnect.Integration.Tests\ModbusSimulator
py -3.12 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install "pymodbus>=3.6,<3.8"
py server.py
```
Listens on `0.0.0.0:5020`. Background thread drives random walks on numeric tags every second.

**EdgeConnect:**
```powershell
cd C:\dev\EdgeConnect
dotnet build ElpisEdgeConnect.sln
cd src\ElpisEdgeConnect.Management\bin\Debug\net8.0
.\ElpisEdgeConnect.Management.exe
```
Studio at `http://127.0.0.1:5080`. (Or `EDGECONNECT_MANAGEMENT_PORT` env var to override.)

**UaExpert:** Launch from desktop. Add server with discovery URL `opc.tcp://localhost:4840/edgeconnect`.

### 2.5 Simulator tag map (reference)

| Tag | Class | Address | Datatype | Byte order | Initial value | Unit |
|-----|-------|---------|----------|-----------|---------------|------|
| `running` | Coil | 0 | bool | — | true | — |
| `alarm_active` | Coil | 1 | bool | — | false | — |
| `door_closed` | DI | 0 | bool | — | true | — |
| `tool_in_spindle` | DI | 1 | bool | — | true | — |
| `spindle_rpm` | HR | 0 | uint16 | AB | 1450 | rpm |
| `spindle_load` | HR | 1 | int16 | AB | -15 | % |
| `feed_rate` | HR | 10 | float32 | ABCD | 250.5 | mm/min |
| `parts_count` | HR | 20 | uint32 | CDAB | 1 234 567 | — |
| `cycle_time` | HR | 30 | float32 | ABCD | 42.75 | s |
| `energy_kwh` | HR | 40 | float32 | ABCD | 128.4 | kWh |
| `alarm_code` | HR | 50 | int16 | AB | 0 | — |
| `mode` | HR | 60 | string(16) | — | "AUTO" | — |
| `part_name` | HR | 100 | string(8) | — | "SHAFT-7X" | — |
| `temperature` | IR | 0 | int16 scale 0.1 | AB | 42.0 (raw 420) | °C |

13 tags total — exhaustive coverage of every Modbus class + datatype + byte order combination.

### 2.6 Defect severity classification

| Severity | Definition | Example |
|----------|-----------|---------|
| **S1 (Blocker)** | No workaround; pipeline doesn't function | Service crashes on start, no tags ever appear in OPC UA |
| **S2 (Critical)** | Major function broken; impractical workaround | Data corruption (wrong values delivered), reconnect doesn't recover |
| **S3 (Major)** | Function broken with workaround | One tag type decodes wrong, UI field doesn't save |
| **S4 (Minor)** | Cosmetic / UX inconvenience | Validation banner copy is awkward, button positioned oddly |
| **S5 (Trivial)** | Nice-to-have | Better helper text |

---

## 3. Test phases

| Phase | Description | Duration |
|-------|-------------|----------|
| Phase A | Smoke (P1 cases only) | 2 hours |
| Phase B | Full functional + configuration | 1 day |
| Phase C | Reliability + recovery + performance | 1 day |
| Phase D | Security + diagnostics + edge cases | 0.5 day |

---

## 4. Functional test cases (TC-F)

Tests verifying the pipeline produces correct data end-to-end.

---

### TC-F-001 — Add Modbus source via wizard
**Priority:** P1 (smoke gate)
**Pre-condition:** EdgeConnect running, Modbus simulator running on `127.0.0.1:5020`.

**Steps:**
1. Open Studio → Sources tab → Add source.
2. Pick "Modbus TCP" protocol card.
3. Fill: Instance id = `modbus-sim-1`, Device id = `simulator`, Device class = `plc`, Poll interval = `1000`.
4. Fill Connection: Host = `127.0.0.1`, Port = `5020`, Unit id = `1`.
5. Add 3 tags: `spindle_rpm` (HR/0/uint16/AB), `feed_rate` (HR/10/float32/ABCD), `temperature` (IR/0/int16/AB, scale 0.1).
6. Routing: "Do not wire yet" (we'll wire separately).
7. Save as draft.
8. Navigate to Config page → Validate draft → Apply.

**Expected:**
- Wizard saves without errors.
- Config page shows draft with the new source.
- Apply succeeds.
- Sources tab shows `modbus-sim-1` with State = `Running` within 5 seconds.

**Pass criteria:** State = `Running` and no Faulted/Degraded.

---

### TC-F-002 — Test Connection probe succeeds (valid host)
**Priority:** P1
**Pre-condition:** Modbus simulator running.

**Steps:**
1. Open Modbus source wizard. Fill Host = `127.0.0.1`, Port = `5020`, Unit id = `1`.
2. Click "Test Connection" button in footer.

**Expected:**
- Inline result panel appears in Connection section.
- Severity = Success.
- Probe ID + elapsed ms shown.
- No snackbar (per ADR-0015 Rule 6).

---

### TC-F-003 — Test Connection probe fails (invalid host)
**Priority:** P1

**Steps:**
1. Open Modbus source wizard. Host = `192.0.2.1` (RFC 5737 test address — guaranteed unreachable), Port = `5020`.
2. Click Test Connection.

**Expected:**
- Inline result panel, Severity = Error.
- Error code shown (likely `MODBUS.CONNECT_TIMEOUT`).
- Remediation hint visible.

---

### TC-F-004 — Add OPC UA Server destination
**Priority:** P1

**Steps:**
1. Studio → Destinations tab → Add destination → "OPC UA Server".
2. Fill: Instance id = `opcua-edge`, Endpoint URL = `opc.tcp://0.0.0.0:4840/edgeconnect`, Application URI = `urn:elpis:edgeconnect`.
3. Security = None.
4. Save as draft → Config page → Validate → Apply.

**Expected:**
- Destination appears in Destinations tab with State = `Running`.
- TCP port 4840 listening: `netstat -an | findstr :4840`.

---

### TC-F-005 — Create route wiring source → sink
**Priority:** P1

**Steps:**
1. Studio → Routes tab → Add route.
2. Route id = `modbus-to-opcua`, Source = `modbus-sim-1`, Sinks = `[opcua-edge]`.
3. Save → Validate → Apply.

**Expected:**
- Route appears, State = `Running`.
- No errors in routes diagnostics.

---

### TC-F-006 — Tags visible in UaExpert
**Priority:** P1 (headline smoke test)

**Steps:**
1. Open UaExpert. Add server `opc.tcp://localhost:4840/edgeconnect`.
2. Use security `None` + Anonymous user token.
3. Connect.
4. Browse: Objects → EdgeConnect → plc → modbus-sim-1 → ...

**Expected:**
- All 3 tags (`spindle_rpm`, `feed_rate`, `temperature`) appear as variable nodes.
- Tags carry the correct OPC UA datatypes (UInt16, Float, Float).
- Browse path matches wizard's `BrowsePathTemplate` default `{deviceClass}/{sourceId}/{tagName}`.

---

### TC-F-007 — Tag values update live in UaExpert
**Priority:** P1

**Steps:**
1. In UaExpert, drag `spindle_rpm` and `feed_rate` to the Data Access View.
2. Observe values for 60 seconds.

**Expected:**
- Values update at ~1s cadence (simulator random walk).
- spindle_rpm hovers near 1450, feed_rate near 250.5.
- Server Timestamp + Source Timestamp present on each update.
- No `Bad` quality codes.

---

### TC-F-008 — All 4 register classes decode correctly
**Priority:** P2

**Steps:** Configure a source with one tag from each register class: `running` (Coil), `door_closed` (DI), `spindle_rpm` (HR), `temperature` (IR). Wire to OPC UA sink. Subscribe in UaExpert.

**Expected:**
- All 4 tags present, all show correct values matching the simulator's expected state.
- Boolean tags appear as Boolean datatype in UaExpert (not Int32).

---

### TC-F-009 — Byte order variations decode correctly
**Priority:** P2

**Steps:** Configure a source with tags exercising all 4 byte orders the simulator provides: `spindle_rpm` (AB), `feed_rate` (ABCD), `parts_count` (CDAB), `cycle_time` (ABCD).

**Expected:**
- `parts_count` reads as 1,234,567 (NOT a swapped/byte-reversed value).
- All float32 tags read with reasonable values (not NaN or wildly out-of-range).
- Tag values cross-checked against the pymodbus quick-verify snippet (README section "Quick verification").

---

### TC-F-010 — Scale + offset applied to temperature
**Priority:** P2

**Steps:** Configure `temperature` tag with scale=0.1, offset=0. Subscribe in UaExpert.

**Expected:**
- Raw register value at simulator address IR:0 = 420.
- UaExpert shows value as **42.0** (NOT 420).
- If you change the tag's scale to 1.0 in the wizard, value should now show as 420.0.

---

### TC-F-011 — String tags decoded
**Priority:** P2

**Steps:** Add `mode` (string length 16) and `part_name` (string length 8) tags. Subscribe in UaExpert.

**Expected:**
- `mode` shows as `"AUTO"` (possibly padded with null/space characters — verify trim behaviour).
- `part_name` shows as `"SHAFT-7X"`.
- OPC UA datatype = String.

---

### TC-F-012 — Edit source via pencil icon
**Priority:** P1

**Steps:**
1. Sources tab → click pencil icon on `modbus-sim-1` row.
2. Wizard opens pre-filled.
3. Change PollIntervalMs from `1000` to `500`.
4. Click "Save changes".

**Expected:**
- Wizard navigates to source detail page.
- Snackbar confirms save with reload classification.
- In UaExpert, value update frequency visibly doubles (now ~500 ms cadence).

---

### TC-F-013 — Edit sink via pencil icon
**Priority:** P2

**Steps:**
1. Destinations tab → pencil on `opcua-edge` row.
2. Change `RootFolder` from `EdgeConnect` to `EdgeConnect-v2`.
3. Save changes.

**Expected:**
- UaExpert needs to rebrowse — old tree gone, new `EdgeConnect-v2` folder appears with the same tags.

---

### TC-F-014 — Edit route via pencil icon
**Priority:** P2

**Steps:**
1. Routes tab → pencil on the route.
2. Edit wizard pre-fills.
3. Verify InstanceId is **disabled** (immutable).
4. Cancel → no changes applied.

**Expected:**
- RouteId field shows as disabled with helper text.
- Cancel discards any changes; route remains as configured before opening.

---

### TC-F-015 — Disable source stops data flow
**Priority:** P1

**Steps:**
1. Sources tab → click "Disable" button on `modbus-sim-1` row.
2. Confirm in drawer.
3. Observe UaExpert subscription.

**Expected:**
- Disable confirms within ~2 seconds.
- UaExpert tag values stop updating (quality goes to `Uncertain` or `BadNoCommunication` depending on subscription mode).
- Sources tab shows State = `Stopped`.

---

### TC-F-016 — Re-enable source resumes flow
**Priority:** P1

**Steps:** Continuing from F-015, click "Enable" on the row.

**Expected:**
- Source goes Running within 5 seconds.
- UaExpert tags resume updating with fresh values.

---

### TC-F-017 — Fanout to multiple sinks (add MQTT alongside)
**Priority:** P2
**Pre-condition:** Mosquitto running on `localhost:1883`.

**Steps:**
1. Add second sink: MQTT destination pointing at `localhost:1883`, PerTag mode.
2. Edit the existing route → add MQTT sink to the sinks list (multi-select).
3. Apply.
4. Open UaExpert (still subscribed) AND subscribe to MQTT topic `eremos/+/cnc/+/+` with `mosquitto_sub`.

**Expected:**
- Both subscribers receive updates independently.
- Failing one (kill Mosquitto) does NOT block the other (OPC UA keeps flowing).
- Per ADR locked decision #9 (fanout independent, non-transactional).

---

### TC-F-018 — Validation banner shows errors before save
**Priority:** P2

**Steps:**
1. Open Modbus source wizard.
2. Clear InstanceId field.
3. Observe the banner above sections.

**Expected:**
- WizardValidationBanner appears with severity = Error.
- Message lists "Instance id is required" (or similar; check exact copy).

---

### TC-F-019 — Validation banner scroll-to-field
**Priority:** P2

**Steps:**
1. Open MQTT or OpcUa or Modbus wizard with multiple validation errors visible (leave required fields empty).
2. Scroll down past Section 4+.
3. Click a message in the banner.

**Expected:**
- Page smoothly scrolls back up to the referenced field.
- Field gains focus (cursor in the input, or visual focus ring).

---

### TC-F-020 — StaleEditWarningBanner on concurrent edit
**Priority:** P2

**Steps:**
1. Open two browser tabs, both at `/destinations/opcua-edge/edit`.
2. In tab 1, change RootFolder to `tab1-test`, click Save changes.
3. In tab 2 (unchanged), change RootFolder to `tab2-test`, click Save changes.

**Expected:**
- Tab 1 saves successfully.
- Tab 2 receives 409 → renders StaleEditWarningBanner at top with:
  - "Configuration was updated by another session"
  - Current version + base version + timestamp (correctly formatted, e.g. `27-05-2026 14:23:11`)
  - Reload button
- Clicking Reload navigates back, wizard re-hydrates with tab 1's saved values.

---

## 5. Configuration test cases (TC-C)

---

### TC-C-001 — Draft → Validate → Apply → Rollback cycle
**Priority:** P1

**Steps:**
1. Save 3 changes (add source, sink, route) as separate drafts.
2. Validate first draft → Apply.
3. Validate second → Apply.
4. Validate third → Apply.
5. On Config page, click "Rollback" against the second-most-recent applied version.

**Expected:**
- Each Apply increments the version id.
- Rollback creates a new draft with the prior version's content.
- After applying the rollback draft, current config matches the rolled-back state.

---

### TC-C-002 — Apply rejects invalid config
**Priority:** P2

**Steps:**
1. Manually craft a draft (via API or by editing the draft JSON) with `BrokerPort = 99999` (invalid range).
2. Click Validate.

**Expected:**
- Validation fails with clear error message naming the field and the constraint.
- Apply button stays disabled.

---

### TC-C-003 — Configuration history
**Priority:** P2

**Steps:** Apply 5+ different configurations over the test session. Navigate to Config → History tab.

**Expected:**
- All 5 versions listed with timestamps, version ids, actor names.
- Each row clickable to view contents.

---

### TC-C-004 — Backup export
**Priority:** P2

**Steps:**
1. Navigate to Config → click "Export backup" (or equivalent).
2. Download the zip / tar archive.

**Expected:**
- File downloads successfully.
- Archive contains: `gateway.json`, history entries, license public key, NO secrets (passwords, API keys redacted).

---

### TC-C-005 — Backup restore
**Priority:** P2

**Steps:** With a working configuration, export a backup. Make destructive changes (delete source, sink). Use backup to restore.

**Expected:**
- Restore reproduces the original config bit-for-bit (modulo timestamps).
- Data flow resumes after restore + service restart if needed.

---

### TC-C-006 — Filter rules drop expected tags
**Priority:** P3

**Steps:** Configure a route with a filter that excludes `parts_count`. Apply.

**Expected:**
- UaExpert sees all other tags update; `parts_count` shows no updates (or its NodeId never materialises).

---

### TC-C-007 — Transform deadband suppresses small changes
**Priority:** P3

**Steps:** Add a deadband transform on the route with threshold = 5 for `spindle_load`. Apply.

**Expected:**
- Small spindle_load fluctuations (< 5 from last published value) do not propagate to UaExpert.
- Large jumps (≥ 5) propagate immediately.

---

## 6. Performance test cases (TC-P)

---

### TC-P-001 — Baseline 10 tags @ 1s scan rate
**Priority:** P1

**Setup:** 10 tags configured, scan rate 1000 ms, 1 route, 1 OPC UA sink, 1 UaExpert subscriber.

**Steps:** Run for 1 hour. Capture metrics every 5 minutes:
- EdgeConnect process CPU%
- EdgeConnect RSS memory
- Number of polls completed (from source detail page or Prometheus)
- Number of dropped messages (from buffer metrics)

**Expected:**
- Sustained CPU < 5% (single core).
- RAM growth < 50 MB over the hour.
- Drop rate = 0.

---

### TC-P-002 — 50 tags @ 500 ms scan rate
**Priority:** P2

**Setup:** Add 50 tags (re-use simulator addresses; the simulator returns canned data for unspecified addresses but read errors are also informative).

**Steps:** Run 1 hour. Same metrics as P-001.

**Expected:**
- CPU < 20%.
- RAM growth < 200 MB.
- Drop rate = 0.
- All 50 tags visible + updating in UaExpert.

---

### TC-P-003 — Multiple OPC UA clients
**Priority:** P2

**Setup:** 1 source, 10 tags, 1 sink. Connect 5 instances of UaExpert simultaneously (or 5 different OPC UA clients).

**Steps:** Each client subscribes to all 10 tags. Run 30 minutes.

**Expected:**
- All 5 clients receive identical update streams.
- No client experiences "Bad" quality codes.
- EdgeConnect CPU usage remains stable.

---

### TC-P-004 — OPC UA Read latency
**Priority:** P3

**Steps:** Using UaExpert's "Read" function on a single tag, measure the time from button click to response display. Repeat 50 times. Compute average + p99.

**Expected:**
- Average < 50 ms over localhost.
- p99 < 200 ms.

---

### TC-P-005 — Subscription notification latency
**Priority:** P3

**Steps:** Configure simulator to write a known sentinel value at a precise time (manual write, or via the simulator's pymodbus client). Measure time between simulator write and UaExpert client receiving the new value.

**Expected:**
- p99 latency < 2 × poll interval (e.g. < 2 seconds for 1000 ms scan rate).

---

## 7. Reliability test cases (TC-R)

---

### TC-R-001 — Modbus simulator killed mid-stream
**Priority:** P1

**Steps:**
1. Pipeline running, UaExpert receiving updates.
2. Kill Python simulator process (Ctrl+C or Task Manager).
3. Observe Studio + UaExpert for 60 seconds.
4. Restart simulator.
5. Observe recovery.

**Expected:**
- Within 10 seconds of kill: source goes to `Faulted` or `Reconnecting` state.
- UaExpert tag quality goes to `Uncertain` or `BadNoCommunication`.
- Diagnostic event logged with error code.
- Within 5 seconds of simulator restart: source returns to `Running`.
- UaExpert subscriptions resume (no need to reconnect from UaExpert side).

---

### TC-R-002 — OPC UA Server restart preserves NodeId stability
**Priority:** P2

**Steps:**
1. Pipeline running. Note a tag's NodeId in UaExpert (e.g. `ns=2;s=...`).
2. Disable the OPC UA destination → wait for Drained → Re-enable.
3. UaExpert reconnects automatically.
4. Re-browse the tag, note its NodeId.

**Expected:**
- NodeId is **identical** before and after.
- Programmatic clients (which pin against NodeId) would still work.

---

### TC-R-003 — EdgeConnect service restart preserves data
**Priority:** P1

**Steps:**
1. Pipeline running. Note last few tag values in UaExpert.
2. Stop EdgeConnect (Ctrl+C in terminal, or stop the Windows Service).
3. UaExpert subscription drops (server is down).
4. Restart EdgeConnect.
5. UaExpert reconnects automatically.

**Expected:**
- Configuration is preserved (sources, sinks, routes all still configured).
- Data flow resumes within 30 seconds.
- No data loss for messages buffered during the outage (store-and-forward semantics — only relevant if MQTT sink is in play; OPC UA Server is in-memory and resumes from "now").

---

### TC-R-004 — Network partition with store-and-forward (MQTT route)
**Priority:** P2
**Pre-condition:** Add MQTT sink to the route alongside OPC UA.

**Steps:**
1. With Mosquitto running, pipeline flowing.
2. Stop Mosquitto.
3. Continue pipeline for 5 minutes (Modbus keeps polling, OPC UA keeps serving).
4. Restart Mosquitto.

**Expected:**
- During outage: source still Running, OPC UA still serving, but MQTT sink shows `Faulted` or `Buffering`.
- Buffer depth grows visibly (Routes detail or diagnostics).
- On restart: buffered messages drain to Mosquitto in order (sequence preserved per route per ADR Lock #11).
- After drain: buffer depth returns to 0.

---

### TC-R-005 — 8-hour soak test
**Priority:** P2

**Setup:** 30 tags, 500 ms scan rate, 1 OPC UA sink, 1 UaExpert subscriber.

**Steps:** Run continuously 8 hours. Capture metrics hourly.

**Expected:**
- No process crashes.
- Memory plateau within 2 hours, no monotonic growth thereafter.
- No accumulated lag (subscription updates remain timely throughout).
- Configuration unchanged from start.

---

### TC-R-006 — 24-hour soak (extended)
**Priority:** P3

Same as R-005 but 24 hours. Pass criteria same. Document any memory step-changes (could indicate slow leak).

---

### TC-R-007 — Concurrent edits from two browsers
**Priority:** P3

Already covered in TC-F-020. Verified here as a reliability concern (no data corruption, no crash).

---

## 8. Recovery test cases (TC-RC)

---

### TC-RC-001 — Source faulted → disable → re-enable
**Priority:** P1

**Steps:**
1. Configure source pointing at wrong port (e.g. 9999). Apply.
2. Source goes Faulted within 10 seconds.
3. Click Disable on the row.
4. Edit source via pencil — change port back to `5020`.
5. Click Enable.

**Expected:**
- Source transitions through `Stopped` → `Starting` → `Running`.
- UaExpert tags begin updating once Running.

---

### TC-RC-002 — Drain state during disable
**Priority:** P2

**Steps:** Apply a slow sink (or saturate the network) so the route is mid-publishing. Click Disable on the sink.

**Expected:**
- Sink shows transient `Draining` state.
- Action column shows "Disable" button **disabled** with tooltip "Drain in progress. Wait for stop to complete." (Locked I per ADR-0014).
- After drain completes (variable time, depends on buffer depth), state moves to `Stopped`.

---

### TC-RC-003 — Apply with invalid config preserves previous
**Priority:** P2

**Steps:** Manually craft a config draft with broken JSON (or a constraint violation), attempt Apply.

**Expected:**
- Apply fails with clear diagnostic.
- Current running config unchanged (verify via `/api/v1/config`).
- Pipeline continues uninterrupted.

---

### TC-RC-004 — Service kill during apply
**Priority:** P3

**Steps:** Start a config apply. Within the 1-2 second apply window, kill the EdgeConnect process forcibly (Task Manager → End Process).

**Expected:**
- Restart picks up either the previous fully-applied config OR the new one — NEVER a mix.
- No torn state (e.g. half-deleted source).
- History shows either a successful apply or a clearly-marked failed apply, not silently lost.

---

### TC-RC-005 — Disk full during draft save
**Priority:** P3

**Steps:** Fill the SQLite buffer directory volume to near 100%. Attempt to save a draft.

**Expected:**
- Save fails with clear error (not a silent corruption).
- Existing buffered data is preserved.

---

## 9. Security test cases (TC-S)

---

### TC-S-001 — OPC UA Security None + Anonymous
**Priority:** P1
**Covered by:** TC-F-006 (pass with default config).

---

### TC-S-002 — OPC UA Sign mode shows runtime warning
**Priority:** P2
**Note:** Current state — Sign and SignAndEncrypt are accepted in config but not enforced at runtime (Milestone K work).

**Steps:** Open OpcUa wizard, set SecurityMode = Sign. Save.

**Expected:**
- Wizard shows the "Release prerequisite" Warning alert in Section 4.
- On Apply (or on adapter restart), an event `OPCUA.SECURITY_NOT_YET_IMPLEMENTED` is logged.
- UaExpert connecting with Security = None still works (server may still accept None even though config says Sign — verify documented behavior).

---

### TC-S-003 — UaExpert rejects Sign mode (when Milestone K lands)
**Priority:** P3
**Note:** Deferred until Milestone K implements Sign/SignAndEncrypt enforcement.

---

### TC-S-004 — Studio basic auth (if enabled)
**Priority:** P2

**Steps:** Configure EdgeConnect with `Auth.Mode = Basic`, restart. Open Studio at `127.0.0.1:5080`.

**Expected:**
- Browser prompts for username/password.
- Wrong credentials → 401, no UI rendered.
- Right credentials → Studio loads.
- All API endpoints (`/api/v1/*`) require auth.

---

### TC-S-005 — License module gating
**Priority:** P3
**Pre-condition:** Generate a license missing the `modbus-tcp` module.

**Steps:** Apply the license. Restart EdgeConnect. Attempt to add a Modbus source via wizard.

**Expected:**
- Either: (a) Modbus protocol card hidden from picker, or (b) save fails with clear license-disabled error.
- Existing Modbus sources continue running (data flow preserved per ADR Lock #7).
- Configuration changes blocked (per ADR Lock #7).

---

### TC-S-006 — Secrets not exposed in backup
**Priority:** P2

**Steps:** Configure an MQTT sink with username/password. Export backup. Inspect backup contents.

**Expected:**
- `password` field is either redacted (`"***REDACTED***"`) or omitted.
- No plaintext credentials anywhere in the archive.

---

## 10. Diagnostics / observability test cases (TC-D)

---

### TC-D-001 — Source state transitions visible
**Priority:** P1

**Steps:** Stop and start simulator. Watch Sources tab Status column.

**Expected:**
- States observed: `Running` → `Faulted` (or `Reconnecting`) → `Running`.
- State transitions appear in source detail page event log.

---

### TC-D-002 — Diagnostic events logged
**Priority:** P2

**Steps:** Navigate to Diagnostics tab. Filter by source `modbus-sim-1`.

**Expected:**
- Events visible: CONNECT, DISCONNECT, FAULT, RECOVER (depending on session).
- Each event has timestamp, code, message, severity.

---

### TC-D-003 — Prometheus metrics endpoint
**Priority:** P2

**Steps:** `curl http://127.0.0.1:5080/metrics` (or the correct port if overridden).

**Expected:**
- Standard Prometheus text format.
- Metrics include: source poll counters, sink publish counters, buffer depth, error counts.

---

### TC-D-004 — Commissioning checklist
**Priority:** P3

**Steps:** Navigate to Commissioning / Checklist tab (location depends on Studio layout).

**Expected:**
- Each check shows ✓ or ✗ with explanation.
- At least: "Gateway identity set", "License loaded", "≥1 source configured", "≥1 sink configured", "≥1 route configured".

---

### TC-D-005 — Route ingest/egress counters
**Priority:** P3

**Steps:** Routes tab → click into a route's detail page.

**Expected:**
- Counters increment as data flows.
- Reset on apply / service restart per design.

---

## 11. UX / wizard test cases (TC-U)

These overlap with the M.2d.4 visual-regression smoke gate. Covered here in case QA runs this plan post-merge.

---

### TC-U-001 — All 5 protocol wizards render with WizardShell
**Priority:** P2

**Steps:** Open each of:
- `/sources/new/focas2`
- `/sources/new/brother-http`
- `/sources/new/modbus`
- `/destinations/new/mqtt`
- `/destinations/new/opcua`

**Expected for each:**
- Header band with back arrow, protocol icon, title, subtitle.
- Numbered sections (1., 2., 3., ...) with consistent visual style.
- Footer with Cancel + Save buttons (+ Test Connection where applicable, except OpcUa).

---

### TC-U-002 — Save button copy
**Priority:** P3

**Steps:** Open each wizard in Add mode then in Edit mode (via pencil icon).

**Expected:**
- Add mode: "Save as draft".
- Edit mode: "Save changes".

---

### TC-U-003 — Edit mode disables InstanceId
**Priority:** P2

**Steps:** For each wizard, enter Edit mode via pencil icon.

**Expected:**
- InstanceId field disabled (gray, not editable).
- Helper text: "Instance id is immutable once the source/destination/route exists."

---

### TC-U-004 — Test Connection button hidden for OpcUa Server
**Priority:** P3

**Steps:** Open OPC UA Server wizard.

**Expected:**
- Footer has only Cancel + Save (no Test Connection).
- Info alert explains: "OPC UA Server destinations do not support Test Connection..."

---

### TC-U-005 — Cancel discards changes
**Priority:** P3

**Steps:** Open Add wizard, fill fields, click Cancel.

**Expected:**
- Navigates back without saving.
- No partial config persisted (verify by reopening — fields are default).

---

### TC-U-006 — Browser tab close discards changes
**Priority:** P3

**Steps:** Open Add wizard, fill fields, close browser tab without saving. Reopen same URL.

**Expected:**
- Fields are default (no localStorage persistence — ADR-0015 Rule 8).

---

## 12. Edge cases / boundary tests (TC-E)

---

### TC-E-001 — Empty configuration / first-run state
**Priority:** P2

**Steps:** Reset config to empty (delete `gateway.json` and restart). Verify Studio still loads and lets you add the first source.

---

### TC-E-002 — Maximum tag count
**Priority:** P3

**Steps:** Configure source with 200+ tags. Apply. Subscribe in UaExpert.

**Expected:**
- All tags visible. Studio remains responsive. Performance metrics within reasonable bounds.

---

### TC-E-003 — Boundary values for port
**Priority:** P3

**Steps:** Try Modbus port = 1, then port = 65535. Then port = 0 (should be invalid), port = 65536 (invalid).

**Expected:**
- 1 and 65535 accepted.
- 0 and 65536 rejected with validation error.

---

### TC-E-004 — Long instance ids
**Priority:** P3

**Steps:** Try instance id = 256 characters. Try special characters not in the regex.

**Expected:**
- Length / regex constraints enforced by validation, clear error messages.

---

### TC-E-005 — Tag name with special characters
**Priority:** P3

**Steps:** Add a tag with name `axes/x/absolute` (slashes allowed per CanonicalDataPoint convention) and `axes-x absolute` (space — invalid?).

**Expected:**
- Documented valid character set enforced.
- Invalid names rejected with clear error.

---

### TC-E-006 — Reload Studio while config apply in progress
**Priority:** P3

**Steps:** Initiate an apply. Within the 1-2 second window, refresh the browser (F5).

**Expected:**
- Studio re-loads, shows current state correctly (either pre-apply or post-apply, never mid).
- No torn UI state.

---

## 13. Exit criteria

The pipeline is QA-approved when:

- [ ] All P1 cases pass with no S1/S2 defects.
- [ ] ≥ 90% of P2 cases pass with no S1 or S2 defects open.
- [ ] All P3 cases executed (pass or document defect).
- [ ] Performance baselines (TC-P-001, P-002) meet stated criteria.
- [ ] 8-hour soak (TC-R-005) passes with documented metrics.
- [ ] Defect log delivered with each defect classified per §2.6.

---

## 14. Out of scope

Explicitly NOT covered by this plan:

- **Authentication beyond Basic auth.** OAuth/OIDC integrations are future work.
- **OPC UA Sign / SignAndEncrypt enforcement.** Configurable but not runtime-enforced until Milestone K.
- **TLS for MQTT sink.** Documented as available; not exercised in this plan.
- **High availability / clustering.** Single-instance deployment only.
- **Other source protocols** (FOCAS2, Brother HTTP, MT-LINKi, MTConnect). Separate test plans.
- **Other sinks** (HTTP, TCP). MQTT covered tangentially in F-017; full coverage is separate.
- **Performance beyond 100 tags.** This plan validates the headline operational range; capacity testing for 1000+ tags is a separate planning exercise.

---

## 15. Defect reporting template

Each defect should include:

```
TC ID: TC-?-???
Severity: S1 / S2 / S3 / S4 / S5
Title: <one-line summary>

Pre-conditions:
- ...

Steps to reproduce:
1. ...
2. ...

Expected:
- ...

Actual:
- ...

Environment:
- EdgeConnect commit: <SHA>
- OS: Windows 11 build XXXX (or Linux distro/version)
- pymodbus: <version>
- UaExpert: <version>
- Browser: Chrome 121.x / Edge ...

Logs / screenshots:
- Attach EdgeConnect log excerpt
- Attach UaExpert screenshot if UI-related
- Attach Studio screenshot if Studio-related

Notes:
- Frequency: Always / Intermittent / Once
- Workaround (if any): ...
```

---

## 16. Glossary

| Term | Definition |
|------|-----------|
| Canonical Data Point | The internal normalised representation of a tag value flowing through EdgeConnect. Protocol-agnostic. |
| Sink | A destination — receives canonical data and pushes to an external system (MQTT, OPC UA Server, HTTP, etc.). UI says "Destination". |
| Source | A protocol adapter that polls or subscribes to an external device and emits canonical data points. |
| Route | A wiring object — one source fanning out to one or more sinks, with optional filter + transform pipeline. |
| Store-and-forward | The per-route SQLite buffer that holds messages while downstream sinks are unreachable. |
| Drafts | Pending config changes — must be Validate'd and Apply'd via the Config page before they take effect. |
| Wizard | A multi-step form for authoring a Source / Sink / Route. Five protocol wizards + one wiring wizard (Route). |
| WizardShell / WizardSection / WizardValidationBanner / WizardActions | Shared M.2d.1 primitives every wizard composes from. Locked in ADR-0015. |

---

## 17. Cross-references

- ADR-0015: `docs/decisions/0015-wizard-contract.md`
- M.2d.4 plan: `docs/sessions/2026-05-27-m2d4-cross-wizard-sweep-plan-v2.1.md`
- M.2d.4 handoff: `docs/sessions/2026-05-27-m2d4-impl-handoff.md`
- Architecture blueprint: `docs/ARCHITECTURE_BLUEPRINT.md`
- Modbus simulator README: `tests/ElpisEdgeConnect.Integration.Tests/ModbusSimulator/README.md`
- Cross-wizard audit checklist: `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md`
