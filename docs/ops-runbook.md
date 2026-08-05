# Operations runbook (bootstrap)

**Status:** Minimal bootstrap — covers the surfaces an operator hits first.
Expand sections when you encounter a scenario that isn't here.

**Audience:** Anyone running an EdgeConnect gateway in production or pilot.

---

## 1. Layout

Out of the box, EdgeConnect reads/writes under one data root:

- **Windows default:** `%ProgramData%\EdgeConnect\` (i.e. `C:\ProgramData\EdgeConnect\`)
- **Linux default:** `/var/lib/edgeconnect/`
- **Override:** set env var `EDGECONNECT_DATA_ROOT=/some/path`

Inside the data root:

```
{root}/
├── config/
│   ├── current.json          Active gateway configuration (read at startup + hot-reload)
│   ├── drafts/               Pending config changes before apply
│   └── history/              Applied versions (retained per ConfigurationManager retention policy)
├── buffer/
│   └── {routeId}.db          Per-route SQLite store-and-forward buffer
├── identity                  Persistent gateway UUID (generated on first start)
└── license.json              Signed offline license (optional until Phase 3 enforces it)
```

Each path has its own override env var — `EDGECONNECT_CONFIG_DIR`,
`EDGECONNECT_IDENTITY_PATH`, `EDGECONNECT_LICENSE_PATH` — use them when
the defaults don't fit.

## 2. Starting and stopping

### Windows service (production)

```powershell
# First install
sc.exe create "Elpis EdgeConnect" binPath= "C:\Program Files\Elpis\EdgeConnect\ElpisEdgeConnect.Host.exe"

# Lifecycle
Start-Service "Elpis EdgeConnect"
Stop-Service "Elpis EdgeConnect"
Get-Service "Elpis EdgeConnect"
```

### Windows console (development / pilot)

```powershell
& "C:\path\to\ElpisEdgeConnect.Host.exe"
# Ctrl+C for graceful shutdown (walks the locked shutdown sequence in reverse)
```

### Linux (systemd)

```bash
sudo systemctl start  elpis-edgeconnect
sudo systemctl stop   elpis-edgeconnect
sudo systemctl status elpis-edgeconnect
```

### Shutdown contract

A clean stop walks every startup phase in reverse, with a 30-second
graceful-shutdown budget. If the budget is exceeded, in-flight batches
are abandoned but the store-and-forward buffer durability guarantees
no data loss — the next start replays from each sink's cursor.

## 3. Observability

### Readiness / health / metrics endpoints

Default bind: `http://localhost:9100/` (configurable via
`HostOptions.EndpointsListenUrl`, or `EnableEndpointsServer=false` to
disable entirely).

| Endpoint | Returns |
|---|---|
| `GET /healthz` | `200 OK` if the host is up, `503` while the readiness gate is closed |
| `GET /readyz`  | `200 OK` only after every startup phase completes; `503` otherwise |
| `GET /metrics` | Prometheus exposition of every `System.Diagnostics.Metrics` instrument the host publishes |

Scrape `/metrics` with Prometheus, Grafana Agent, or `curl`:

```bash
curl -s http://localhost:9100/metrics | grep -E "^elpis_edgeconnect_"
```

### Logs

`Microsoft.Extensions.Logging` under the hood. Configure via standard
`Logging` section in `appsettings.json` or env vars:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "ElpisEdgeConnect": "Debug"
    }
  }
}
```

On Windows the host logs to stdout (console host) or the Event Log
(Windows service). On Linux, journalctl:

```bash
journalctl -u elpis-edgeconnect -f
```

### Startup phase ordering

The host walks a locked phase sequence (see `StartupPhase.cs`):

1. ParseEnvironment → 2. BuildContainer → 3. LoadGatewayIdentity →
4. LoadConfiguration → 5. LoadLicense → 6. ConstructDiagnostics →
7. ConstructBufferFactory → 8. RegisterRoutes → 9. StartSourceSupervisor →
10. StartRoutingEngine → 11. MarkReady → 12. StartMetricsEndpoint

A startup failure logs the phase in which it failed. Use that as your
first diagnostic — e.g. `LoadConfiguration` → inspect `current.json`;
`LoadLicense` → check `license.json` signature; `StartSourceSupervisor`
→ check adapter init logs.

## 4. Common scenarios

### "Adapter X is in Degraded"

- **Check:** `/metrics` for `consecutiveConnectFailures` on that adapter; logs for the root error code.
- **Typical causes:** network unreachable, authentication failure, exhausted handle pool (FOCAS2), MQTT broker unreachable.
- **Remediation:** fix the upstream; the adapter's backoff will retry automatically. No manual restart required.

### "Route is Stopped / buffer growing without end"

- **Check:** the sink's health state (`/metrics` → `elpis_edgeconnect_sink_state`); the bad sink's last error; buffer depth per route.
- **If all sinks on the route are degraded:** upstream system-of-record outage. Buffer absorbs until `BufferPolicy.MaxDepth`, then drops per `DropPolicy` (newest-first or oldest-first, config-driven).
- **Remediation:** restore the downstream system. The route drains automatically through the buffer when sinks recover.

### "Buffer file size is huge / disk is filling up"

- **Where buffer files live:** `{dataRoot}/buffer/{routeId}.db` plus `-wal` and `-shm` siblings while the buffer is open. One file per route.
- **Two ways the file grows:** real backlog (sink genuinely behind, depth incrementing) or WAL not checkpointing (rare; usually a long-running read or pinned cursor).
- **Tell which:** read `currentDepth` from the diagnostics surface. If it's near zero but the `.db-wal` is large, force a checkpoint by stopping and restarting the host (graceful shutdown checkpoints + closes per the C2b durability spec, D11). If `currentDepth` is large, see "Route is Stopped / buffer growing without end" above.
- **Bound the worst case:** every route has `MaxDepth` (rows) and `MaxAgeDays` (oldest acceptable point) caps. Both default to sensible values; tighten them in `gateway.json` if the disk budget is tight.

### "Buffer file is corrupt / `quick_check` fails on open"

- **Symptom:** host startup fails with `BUFFER_SCHEMA_MISMATCH` or `BUFFER_INTEGRITY_FAILED` at the LoadConfiguration / RegisterRoutes phase.
- **Cause:** the SQLite file was edited by an external tool, or the underlying disk had an integrity event.
- **Remediation:** with the host stopped, **rename** (don't delete — preserve the bad file for forensics) `{dataRoot}/buffer/{routeId}.db*` to `{routeId}.db.bad-{timestamp}`. On next start the route opens a fresh empty buffer. **This loses all queued-but-unpublished data for that route**, so attempt `sqlite3 file.db ".recover" | sqlite3 recovered.db` first if the data matters.

### "Migrating buffer files between hosts"

- **DON'T copy a buffer file onto a different gateway-id.** The points inside carry the source gateway id; publishing them under a new gateway id changes the topic shape and confuses downstream consumers.
- **DO**: stop the host, copy `{routeId}.db` plus `-wal` and `-shm` to the new host's `{dataRoot}/buffer/`, start the new host. Cursors carry over; the route resumes where it left off.

### "License expired"

- Data flow continues (locked decision #7 — never cut customer data to enforce licensing).
- Config applies are blocked by the configuration manager.
- **Remediation:** install a new signed license file at the configured path and restart the host (live license reload is Phase 4+ work).

### "Config change didn't take effect"

The gateway hot-reloads `current.json` in-process as of M.P2.2 — no host restart needed for any draft → apply path. If a change appears not to have applied, walk this triage:

1. **Did the Apply land at all?** Check `current.json`'s version id (the timestamp segment) or the Studio's Configuration page → Active configuration. If the version did not advance, the apply itself failed — look for a 409 response or an error snackbar.

2. **Did the reconcile complete?** After Apply, the response body's `reload` field (or the Studio's reload-outcome panel above the active-config card) tells you exactly what happened:

   - `status = "Completed"` — reconcile finished. Cross-check `appliedInstances` / `restartedInstances` / `faultedInstances` against what you expected.
   - `status = "InProgress"` — the 10 s wait window elapsed before reconcile finished. Poll `GET /api/v1/diagnostics/configuration-faults` for the terminal state, or open the Studio's Diagnostics page.
   - `status = "Skipped"` — a newer apply superseded this one. Look at the version that won (`supersededBy`) and check its outcome instead.
   - `reload` absent / null — Management is running standalone (no runtime registry). Restart the gateway service to pick up the change, OR run Management in-process with the host.

3. **Is the instance faulted?** A red chip on the reload panel (or an entry under `/api/v1/diagnostics/configuration-faults`) means the runtime rejected the new config. Click the chip → diagnostics page → read the structured `ErrorCode` and `Message`. The apply is durable; the runtime is the problem. Common cases:
   - `MODBUS.CONNECT_FAILED` — device unreachable. Verify network, then re-apply to re-try.
   - `CONFIG.SOURCE_WITHOUT_ROUTE` — the new config has a source no route references. Add a route and re-apply. As of M.P2.3 (ADR-0010), the coordinator handles the startup-skipped case automatically; the route-only Apply will re-attempt the source.
   - `CONFIG.SINK_WITHOUT_ROUTE` — same shape for sinks. Add a referencing route and re-apply.
   - `CONFIG.ROUTE_REFERENCES_MISSING_SOURCE` / `CONFIG.ROUTE_REFERENCES_MISSING_SINK` — a route points at a source / sink that isn't in the supervisor. Add the referenced entity to the config and re-apply; M.P2.3's synthesis pass adds the referencing route once its dependency exists.
   - `HOST.RECONCILE_FAILED` — adapter init threw. Read the message for the underlying cause.

4. **Tuning the wait window** — for long cold-start adapters that consistently hit `InProgress`, raise `HostOptions.ReloadOutcomeWaitMs` (default 10 000) on host bootstrap. The cap protects the HTTP response timing; the actual reconcile is not bounded by it.

### "Config apply / bundle generation fails — data root not writable"

Symptom: a config apply/rollback or a diagnostic-bundle generation fails, and the
Studio Diagnostics page shows a **Gateway** fault `CORE.CONFIG_DATA_NOT_WRITABLE`
(also logged at startup as `Gateway data root is not writable …`). The named path
is usually `…\config\history\audit.log` or `…\config\current.json`.

- **Why:** the runtime appends `audit.log` (and replaces `current.json`) in place.
  On Windows, files created in `C:\ProgramData\EdgeConnect` under an **elevated**
  context are owned by `Administrators` and grant `Users` read-only — so a later
  **non-admin** runtime user can read but not write them. The data pipeline keeps
  running (fail-soft, ADR-0003), but any audit-writing operation fails until the
  data root is writable.
- **Confirm:** `Get-Acl "C:\ProgramData\EdgeConnect\config\history\audit.log"` —
  look for `Users` with only `ReadAndExecute`.
- **Remediation (pick one):**
  - From an **elevated** terminal, grant the runtime account Modify on the tree:
    `icacls "C:\ProgramData\EdgeConnect" /grant "<DOMAIN\user>:(OI)(CI)M" /T`
  - Or point the gateway at a user-writable data root and copy the existing data
    there: set `EDGECONNECT_DATA_ROOT` to e.g. `C:\Users\<you>\EdgeConnectData`,
    copy `C:\ProgramData\EdgeConnect\*` into it, and restart.
  - Run the gateway consistently as the identity that owns the data files.
- After remediation, restart the gateway — the startup pre-flight re-checks and
  the Gateway fault clears.
- **Prevention:** run the gateway under a single, consistent identity from first
  start (don't alternate elevated/non-elevated). The Phase 3 Windows-service
  installer sets the data-dir ACLs for the service account.

### "Gateway identity changed unexpectedly"

- The identity file (`{root}/identity`) is generated on first start and should not be touched.
- Deleting it causes a new UUID to be generated on next start — all historical data attribution breaks. Only do this if the gateway is being moved to a new customer/site.

## 5. Known operational limits

- **FOCAS2:** ~8 simultaneous handles per controller. Limit one EdgeConnect source per CNC, or coordinate with other FOCAS2 clients on the same network.
- **MQTT:** broker per-topic message ordering is only within one client. Fan-out to multiple brokers does not preserve ordering across them.
- **Buffer:** default per-route `MaxDepth = 10_000` (per `BufferPolicyConfig` defaults), `MaxAgeDays = 7`. Both apply to InMemory and StoreAndForward; with StoreAndForward the file is also bounded by the data-root disk's free space.
- **Hot-reload + startup-skipped instances (resolved in M.P2.3, ADR-0010):** Sources / sinks / routes that fail cross-record validation at gateway startup (any of `CONFIG.SOURCE_WITHOUT_ROUTE`, `CONFIG.SINK_WITHOUT_ROUTE`, `CONFIG.ROUTE_REFERENCES_MISSING_SOURCE`, `CONFIG.ROUTE_REFERENCES_MISSING_SINK`) are skipped by M.P2.1 fail-soft and never added to their runtime registry. As of M.P2.3 the `RuntimeReloadCoordinator` runs a synthesis pre-pass on each Apply that catches cross-record-validity flips and re-attempts the affected entities automatically — no operator workaround required for these four codes. A route-only Apply that fixes the missing reference now brings the previously-skipped source / sink / route up cleanly.

  Faults registered for other reasons (adapter-ctor exceptions during the bind phase, license-check failures, runtime adapter init failures) are out of scope for the synthesis pass; those still require the entity's own config to change (or a gateway restart) to be re-attempted.

## 6. OPC UA Server — cert + trust list (Milestone K)

When the OPC UA Server sink runs with `SecurityMode != None` (i.e.
`Sign` or `SignAndEncrypt`), every connecting client is verified
against an explicit trust list. The OPC UA library auto-creates the
required directories under the configured paths on first start.

### Cert store layout

Default paths (override per-sink via the security block in
`gateway.json`):

```
%LocalApplicationData%\EdgeConnect\OpcUa\pki\        (Windows)
~/.local/share/EdgeConnect/OpcUa/pki/                (Linux equivalent)
├── own/         The server's own application certificate (private key on disk; ACL it)
├── trusted/     X.509 certs of clients the operator has explicitly accepted
├── rejected/    X.509 certs from connecting clients whose acceptance is pending review
└── issuer/      Trusted CA / issuer certs (optional; for chains rooted at a customer CA)
```

The server's own cert is auto-generated as a 2048-bit RSA self-signed
certificate on first start. Subject `CN=<ApplicationName>`; SAN
includes the configured `ApplicationUri` per OPC UA spec.

### Operator workflow: a new client connects under Sign / SignAndEncrypt

1. **Client tries to connect.** Library rejects the session
   (`BadCertificateUntrusted`). Logs at WARN level on EdgeConnect:
   `OPC UA: untrusted certificate from <SubjectName>; placed in rejected/`.
2. **Locate the rejected cert.** It lands in
   `pki/rejected/certs/<thumbprint>.der`.
3. **Verify out-of-band** (this is the trust step — only the
   operator should do this): confirm the cert's SubjectName matches
   the client the operator expects, e.g. by phoning the SCADA team
   or comparing against the cert the SCADA software shows.
4. **Promote** by moving the file from `rejected/certs/` to
   `trusted/certs/`. No restart required — the library re-reads the
   trust list on the next connection attempt.
5. **Client reconnects** — the session establishes successfully.

### Operator workflow: rotating the server's own cert

The MVP-K cert manager does NOT rotate automatically. Manual
procedure:

1. **Stop the gateway** (Windows service / systemd unit / console
   process). This ensures no in-flight sessions hold the old cert.
2. **Back up** `pki/own/` to a dated folder so you can roll back.
3. **Delete the contents of `pki/own/`** (the private key + cert).
4. **Restart the gateway.** A new self-signed cert is generated on
   first start.
5. **Re-trust the new cert** at every connecting client. Every
   SCADA/MES needs to accept the new cert's thumbprint — the old
   trust relationship is invalidated.

A future milestone will add overlap-window rotation (generate the
new cert while the old one is still active, both valid for ~7 days,
swap the active key when all clients have re-trusted).

### Operator workflow: enabling Username authentication

In `gateway.json`, on the OPC UA Server sink:

```json
"security": {
  "mode": "SignAndEncrypt",
  "userTokenPolicies": ["UserName"],
  "credentials": [
    { "username": "scada", "password": "<long-random-string>", "displayName": "SCADA HMI" },
    { "username": "mes",   "password": "<long-random-string>", "displayName": "MES Backend" }
  ]
}
```

**Credential storage at MVP-K is plain text in `gateway.json`.**
Operators MUST restrict the config directory:
- **Windows:** ACL `%ProgramData%\EdgeConnect\config\` so only the
  service account (e.g. `NT SERVICE\EdgeConnect`) and admins can read.
- **Linux:** `chmod 600 /var/lib/edgeconnect/config/current.json`
  and ensure the file is owned by the gateway's service user.

A future milestone will replace plain-text passwords with hashed
storage and an admin-only management API for rotation.

### Operator workflow: enabling Certificate authentication

Set `userTokenPolicies: ["Certificate"]`. The client's identity
certificate is validated against the same `trusted/` store used for
application certs. The promote-from-rejected workflow above applies.
For multi-tenant SCADA environments, point `trustedClientsPath` at a
per-tenant directory.

### Common failures

| Symptom | Likely cause | Fix |
|---|---|---|
| Client gets `BadCertificateUntrusted` | Cert is in `rejected/certs/`, not yet promoted | Move to `trusted/certs/` |
| Client gets `BadUserAccessDenied` | Wrong username or password | Verify against the `credentials` block |
| Server logs `OPCUA.USERNAME_POLICY_WITHOUT_CREDENTIALS` at start | `userTokenPolicies` includes `UserName` but `credentials` is empty | Add at least one credential, or remove `UserName` from the policy list |
| `BadIdentityTokenRejected` with policy disabled | Client offered a token type the operator didn't enable | Add the policy to `userTokenPolicies` if intentional |

### Promoting the OPC UA module to production

Before customer demo or pilot soak: confirm OPC Foundation Corporate
membership is in hand (see `docs/licensing/module-catalog.md`
§ Third-party library licensing). The OPCFoundation NuGet packages
are dual-licensed (GPL-2.0 / RCL); binary distribution to customers
requires the RCL terms which only Corporate Members may use.

## 7. When things go wrong

1. Capture the failing phase (from logs).
2. Capture `/metrics` output at the moment of failure.
3. Capture the last 200 log lines before the failure.
4. Capture `current.json` and a directory listing of `buffer/`.

That bundle is enough to reconstruct the failure without needing live
access to the gateway.

## 8. See also

- `docs/ARCHITECTURE_BLUEPRINT.md` — master architecture reference
- `docs/PHASE1_EXECUTION_PLAN.md` §6 — supervisor + startup details
- `docs/adapter-sdk/source-adapter-contract.md` — adapter lifecycle contract
- `docs/adapter-sdk/focas2-adapter.md` — FOCAS2-specific operational notes (DLL deployment, backoff tuning, troubleshooting table)
- `docs/config-authoring.md` — writing `current.json`
