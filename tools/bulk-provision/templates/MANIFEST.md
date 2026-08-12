# Bulk-Provision Templates Manifest

This directory contains the canonical templates consumed by
`tools/bulk-provision/generate.ps1`. Each template is a complete
`gateway.json`-shaped document with `{{ placeholder }}` markers that the
generator substitutes per row in the operator's CSV plus the per-gateway
sidecar.

**Reference:** `docs/sessions/2026-05-21-chip3-provisioning-subsystem-plan-v2.md` §5.1, §5.5.

## Templates shipped in v1

| Template ID         | Protocol      | File                          | Schema version |
|---------------------|---------------|-------------------------------|----------------|
| `template-fanuc`    | `focas2`      | `template-fanuc-v1.json`      | 1              |
| `template-brother`  | `brother-http`| `template-brother-v1.json`    | 1              |
| `template-modbus`   | `modbus-tcp`  | `template-modbus-v1.json`     | 1              |
| `template-mtconnect`| `mtconnect`   | `template-mtconnect-v1.json`  | 1              |

All four are paired with the canonical MQTT sink and a single Route that
fans the source to the sink. Operators editing post-generation may add
additional sinks/routes — the generator only owns the per-row Sources
plus the one canonical sink + route per protocol.

### Offline-vs-Studio CSV column convention

The offline generator (`tools/bulk-provision/generate.ps1`) uses a
**uniform `host` column** across all four templates. For Fanuc/Brother/Modbus
the value is an IP address (e.g. `192.168.10.21`). For MTConnect the value
is a full Agent base URL (e.g. `http://192.168.10.51:5000/` or
`https://example.local/mtconnect/`). The template substitutes the value
verbatim into the protocol-specific Connection field (`ipAddress`,
`baseUrl`, `host`, or `agentBaseUrl`).

The **Studio bulk-import wizard** (Phase 1) uses a `baseUrl` column for
MTConnect rows per its own v3 §3 lock. The Studio wizard is a separate
product surface that does NOT invoke this generator — it has its own
C# `BulkSourceMergeService` that processes CSVs directly. The two
surfaces diverge intentionally; chip-3 v3 architecture amendment
documents the boundary.

## Placeholder taxonomy

Placeholders fall into two scopes. The generator REJECTS any per-row
placeholder appearing outside `Sources[]` or any per-gateway placeholder
appearing inside `Sources[]` (anti-templating-engine guard, v2 §5.5.3).

### Per-row placeholders — substituted once per CSV row

These come from the operator's CSV. Each Source instance gets its own
substitution pass.

| Placeholder           | CSV column   | Type     | Notes |
|-----------------------|--------------|----------|-------|
| `{{ instanceId }}`    | (derived)    | string   | Generator-derived: `{deviceId}-source`. Stable. |
| `{{ deviceId }}`      | `deviceId`   | string   | Operator-supplied; must be unique within the CSV. |
| `{{ deviceName }}`    | `deviceName` | string   | Human label; not used in topics. |
| `{{ host }}`          | `host`       | string   | IPv4 / hostname. Operator validates reachability. |
| `{{ enabled }}`       | `enabled`    | boolean  | `true` / `false`. Emitted unquoted (raw JSON token). |

### Per-gateway placeholders — substituted once for the whole document

These come from the sidecar `gateway.yml` (or operator-supplied at CLI
time, depending on §5.2 spec). One value applies to every Source row.

| Placeholder                    | Sidecar field           | Type    | Notes |
|--------------------------------|-------------------------|---------|-------|
| `{{ gatewayId }}`              | `gatewayId`             | string  | UUID; appears in MQTT topic + 3-way diagnostics. |
| `{{ gatewayName }}`            | `gatewayName`           | string  | Human label. |
| `{{ gatewayProvisioningId }}`  | `gatewayProvisioningId` | string  | Generator's per-run id; used as MQTT clientId suffix + route-id suffix to avoid clashes when reprovisioning. |
| `{{ fleetId }}`                | `fleetId`               | string  | Customer fleet binding (per `_provisioning`). Not yet rendered in body in v1; reserved for future Routes.tags. |
| `{{ site }}`                   | `site`                  | string  | Site label. Reserved for future use; landed in `_provisioning` block only in v1. |
| `{{ mqttHost }}`               | `mqttHost`              | string  | Broker hostname. |
| `{{ mqttPort }}`               | `mqttPort`              | int     | Broker port. Emitted unquoted. |
| `{{ mqttQos }}`                | `mqttQos`               | int     | 0 / 1 / 2. Emitted unquoted. |
| `{{ mqttClientIdPrefix }}`     | `mqttClientIdPrefix`    | string  | MQTT clientId prefix; full clientId is `{prefix}-{gatewayProvisioningId}`. |

## Substitution contract

Substitution is **pure literal string replacement** — no expressions, no
conditionals, no loops, no recursive expansion. The generator scans the
template byte-stream for `{{ <key> }}` markers (whitespace inside braces
is literal; the marker form is fixed). After substitution the result is
parsed as JSON; if parsing fails the generator aborts the run with the
specific placeholder + row identified. See v2 §5.5.3 for the rationale —
this is the boundary that keeps the subsystem out of "we accidentally
built a templating engine" land.

### Scope rule (one-way only)

- **Per-gateway placeholders MUST NOT appear inside `Sources[…]`.**
  These are fleet-wide values; embedding them in a Source instance
  defeats the per-source semantics the operator expects. Violations
  throw `BulkProvision.PlaceholderScopeViolation`.
- **Per-row placeholders MAY appear anywhere in the file.** Because
  the generator writes one `gateway.json` per CSV row, the entire
  output file is for one device — per-row placeholders are the file's
  natural scope. In particular, the canonical templates use
  `{{ instanceId }}` in `Routes[…].SourceInstanceId` for the
  legitimate Source↔Route cross-reference within a single file.

(The original chip 3 v2 spec §5.5.3 wrote a symmetric guard that
forbade per-row markers outside `Sources[…]`. The session 2
`pwsh`-7 foundation smoke exposed that as over-zealous against the
locked template designs; the relaxation is captured here and in
the lib's `Assert-PlaceholderScopes` header.)

## Static-field invariants (anti-templating-engine)

Each template hard-codes protocol-defining static fields. The generator
does NOT permit the CSV or sidecar to override these. If an operator
needs to change them, they fork the template, bump the version (`v2`),
and add a new row to the table above.

| Template            | Static fields locked in template |
|---------------------|----------------------------------|
| `template-fanuc`    | `ProtocolName = "focas2"`, `Connection.port = 8193`, `Polling.IntervalMs = 3000`, `Connection.dataPoints` (current customer production baseline — 10 canonical paths; see [parity artifact](../../../docs/sessions/2026-06-14-bulk-provision-ui-phase1-64-tag-parity.md)) |
| `template-brother`  | `ProtocolName = "brother-http"`, `Polling.IntervalMs = 3000`. **No `Connection.dataPoints` in template** — Brother adapter supports a configurable `DataPoints` list (see `BrotherHttpSourceConfiguration.DataPoints`), but the current customer's Brother config doesn't set one; operators add post-generation if needed. |
| `template-modbus`   | `ProtocolName = "modbus-tcp"`, `Connection.port = 502`, `Connection.unitId = 1`, `Polling.IntervalMs = 1000`, `Connection.tags = []` (per-tag definitions come from the per-tag CSV importer — see chip-3 v2 §1.3) |
| `template-mtconnect`| `ProtocolName = "mtconnect"`, `Polling.IntervalMs = 3000`, `Connection.dataPoints` (same 10 canonical paths as FOCAS2 — `MTConnectSemanticMap.cs:7` documents that the adapter deliberately mirrors FOCAS2 path names so downstream consumers can treat both protocols identically) |

## Version stamping

Each template's filename embeds its schema version (`-v1.json`). The
generator writes `_provisioning.templateId` (e.g. `template-fanuc`) and
`_provisioning.templateSchemaVersion` (e.g. `1`) into the output so any
generated config is traceable back to the exact template it came from.
A future `template-fanuc-v2.json` ships side-by-side; operators pin
their fleet to a specific version explicitly.

## Adding a new template (forward-compat)

1. Author `template-<protocol>-v1.json` mirroring the patterns above.
2. Add a row to the v1 table and the static-field invariants table.
3. Add a `samples/sample-<protocol>.csv` fixture under `samples/`.
4. Add a round-trip test under `tests/` that runs `generate.ps1` against
   the fixture and validates the output via `tools/ValidateConfig`.
5. Ensure the new template participates in the deterministic-output
   guarantee (§5.4.4 byte-identical-modulo-`generatedAt`).
