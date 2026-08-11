# Configuration authoring guide (bootstrap)

**Status:** Minimal bootstrap — covers the `current.json` shape every
customer needs. Expand when a protocol or scenario isn't covered.

**Audience:** Anyone writing or reviewing an EdgeConnect configuration
file.

---

## 1. File layout

EdgeConnect reads its active configuration from one file:

```
{data-root}/config/current.json
```

Default `{data-root}` is `%ProgramData%\EdgeConnect` on Windows,
`/var/lib/edgeconnect` on Linux. Override via `EDGECONNECT_CONFIG_DIR`.

There are no other config files. Sources, sinks, routes, buffers,
delivery policies — all in one JSON document.

## 2. Top-level shape

```jsonc
{
  "gateway": {
    "gatewayId": "gw-factory-a",
    "gatewayName": "Factory A Line 1"
  },
  "sources":  [ /* per-protocol source blocks */ ],
  "sinks":    [ /* per-protocol sink blocks */ ],
  "routes":   [ /* wire each source to one or more sinks */ ]
}
```

Every field name is camelCase. The deserializer is case-insensitive but
camelCase is the convention.

## 3. Sources

Each source describes one configured use of a protocol against one
physical device.

```jsonc
{
  "instanceId":   "focas-lathe-1",      // stable id, referenced by routes
  "protocolName": "focas2",             // must match an installed adapter's ProtocolName
  "enabled":      true,
  "deviceId":     "lathe1",             // the physical device this source reads
  "deviceName":   "Mori Seiki NL2500",  // optional, for the UI
  "polling":      { "intervalMs": 2000, "maxConsecutiveErrors": 5 },
  "connection":   { /* protocol-specific; see per-protocol docs */ },
  "tags":         [ "shift-a", "critical" ]  // optional free-form labels
}
```

### Connection blocks per protocol

The `connection` object is protocol-specific. Consult the adapter doc
for the full field reference:

- **focas2** → [`docs/adapter-sdk/focas2-adapter.md`](adapter-sdk/focas2-adapter.md#3-configuration)
- **mtconnect** → *(bootstrap doc pending; see `Mtconnect` adapter README)*

## 4. Sinks

```jsonc
{
  "instanceId":   "mqtt-primary",
  "protocolName": "mqtt",
  "enabled":      true,
  "batchSize":    100,        // optional, default 100
  "batchIntervalMs": 250,     // optional, default 250 ms
  "connection":   { /* protocol-specific */ }
}
```

### MQTT sink example (PerTag mode — EREMOS V2 compatibility)

```jsonc
{
  "instanceId":   "mqtt-primary",
  "protocolName": "mqtt",
  "connection": {
    "brokerHost":          "mqtt.internal.example.com",
    "brokerPort":          1883,
    "useTls":              false,
    "publishMode":         "PerTag",
    "perTagTopicTemplate": "eremos/{gatewayId}/cnc/{sourceId}/{tagName}",
    "qosLevel":            1,
    "keepAliveSeconds":    30,
    "reconnectDelayMs":    5000
  }
}
```

### MQTT sink example (Batch mode — JSON array per batch)

```jsonc
{
  "connection": {
    "brokerHost":    "mqtt.internal.example.com",
    "publishMode":   "Batch",
    "topicTemplate": "edgeconnect/{gatewayId}/data",
    "qosLevel":      1
  }
}
```

## 5. Routes

Each route binds one source's intake to one or more sinks. Fan-out is
independent per sink — a failing sink never blocks a healthy one.

```jsonc
{
  "routeId":           "lathe-1-to-mqtt",
  "name":              "Lathe 1 → MQTT",
  "sourceInstanceId":  "focas-lathe-1",
  "sinkInstanceIds":   [ "mqtt-primary", "audit-sink" ],
  "enabled":           true,
  "filter":            { /* tag filter, optional */ },
  "buffer":            { /* buffering policy, optional */ },
  "delivery":          { /* delivery policy, optional */ }
}
```

### Buffering policy (optional)

```jsonc
"buffer": {
  "mode":       "StoreAndForward",   // or "InMemory"
  "maxDepth":   20000,               // 0 = unbounded (StoreAndForward only)
  "onOverflow": "DropOldest"         // or "DropNewest", "BlockProducer"
}
```

Default is `StoreAndForward` with `MaxDepth = 0` (unbounded, disk-bound).
Use `InMemory` for high-throughput routes where durability is not
required; use `BlockProducer` when backpressure is preferable to drops.

### Delivery policy (optional)

```jsonc
"delivery": {
  "mode":              "AtLeastOnce",  // or "AtMostOnce"
  "maxRetries":        10,
  "initialBackoffMs":  500,
  "maxBackoffMs":      30000,
  "backoffMultiplier": 2.0,
  "jitterPercent":     10
}
```

`ExactlyOnce` is **not** supported in v1 (blueprint locked decision
#12). Config validation rejects it.

## 6. Minimal working example

```json
{
  "gateway": { "gatewayId": "gw-demo", "gatewayName": "Demo Gateway" },
  "sources": [
    {
      "instanceId": "focas-lathe-1",
      "protocolName": "focas2",
      "deviceId": "lathe1",
      "connection": { "ipAddress": "192.168.1.101" }
    }
  ],
  "sinks": [
    {
      "instanceId": "mqtt-primary",
      "protocolName": "mqtt",
      "connection": {
        "brokerHost": "localhost",
        "brokerPort": 1883,
        "publishMode": "PerTag",
        "perTagTopicTemplate": "eremos/{gatewayId}/cnc/{sourceId}/{tagName}"
      }
    }
  ],
  "routes": [
    {
      "routeId": "r1",
      "sourceInstanceId": "focas-lathe-1",
      "sinkInstanceIds": [ "mqtt-primary" ]
    }
  ]
}
```

Drop this at `{data-root}/config/current.json`, start the host, and
points flow.

## 7. Validation rules

Most rules surface as readable errors at startup (and later, at draft
validation time). The ones worth knowing up front:

- `instanceId` must match regex `^[A-Za-z0-9][A-Za-z0-9._-]*$`, max 128 chars.
- `protocolName` must be lowercase, start with a letter, match an installed adapter's declared `ProtocolName`.
- `routes[].sourceInstanceId` must reference an existing source.
- `routes[].sinkInstanceIds[]` must reference existing sinks.
- `buffer.mode = StoreAndForward` + `delivery.mode = AtMostOnce` is a config error (durable buffer with fire-and-forget semantics is nonsensical).
- `delivery.mode = ExactlyOnce` is rejected outright.
- Disabled sources / sinks are allowed — their routes won't run but the file still validates.

## 8. Draft → Apply → Rollback

The Core configuration manager supports the full
draft / validate / apply / rollback flow, exposed through the
Management REST API and the Studio's Configuration page. The recommended
change path is:

1. **Draft** — `POST /api/v1/config/drafts` (or use the Studio's import / wizard / "Duplicate as draft").
2. **Validate** — `POST /api/v1/config/drafts/{id}/validate`. The Studio's Validate button surfaces structured errors / warnings.
3. **Apply** — `POST /api/v1/config/drafts/{id}/apply`. The response carries the new version id, audit metadata, AND the runtime reload outcome (see "What happens after Apply" below).
4. **Rollback** if needed — `POST /api/v1/config/history/{versionId}/rollback`. Same response shape as Apply; rollback IS an apply.

Stopping the host is no longer required. The Phase-2 hot-reload
coordinator (M.P2.2) drives the running supervisors + routing engine to
converge on the new config in-process. The audit chain still records
every applied version exactly as it did before.

### What happens after Apply

A successful Apply returns `200 OK` with `ApplyResultDto`. The body
carries the new version id, the change list, audit metadata, and an
optional `reload` block surfacing the runtime reconcile outcome:

| `reload.status` | Meaning |
|---|---|
| `"Completed"` | Reconcile finished. `appliedInstances` / `restartedInstances` / `faultedInstances` list what came up, what restarted, and what failed. |
| `"InProgress"` | The 10 s wait window elapsed before the reconcile finished. Poll `/api/v1/diagnostics/configuration-faults` for the terminal state. |
| `"Skipped"` | A newer Apply superseded this one before its reconcile started. `supersededBy` carries the winning version id. |

If `reload` is **absent** from the response (the field is omitted, not
null), the gateway did not have a runtime reload registry to consult —
e.g. you're running Management standalone or against a non-hosted
process. That is semantically distinct from `"InProgress"`: absent means
"no observation surface"; InProgress means "still running, go poll".

In the Studio, the Configuration page renders a panel above the active-
config card after every Apply / Rollback. Green / amber / red chip rows
mirror `appliedInstances` / `restartedInstances` / `faultedInstances`.
Faulted chips link to `/diagnostics#{instanceId}` for the full fault
detail. The panel hides itself when `reload` is absent.

**Operational expectations:**

- A healthy reconcile resolves in well under a second for most config
  changes (cadence tweak, tag-table edit, single-source add). Cold-start
  adapters against slow devices can push past the 10 s wait window —
  the apply succeeds and you'll see `"InProgress"`; the gateway is
  still converging.
- A failed reconcile does NOT roll back the apply. The new
  configuration is durable; runtime state reflects whatever came up.
  Address the fault via the Studio (or another Apply) and re-converge.
- Two rapid Applies can result in the first one being `Skipped` — that
  is the coordinator declining to converge a state that's already
  obsolete. The second Apply's reconcile is the authoritative one.

## 9. See also

- `docs/adapter-sdk/focas2-adapter.md` — FOCAS2 source `connection` field reference
- `docs/ops-runbook.md` — starting, stopping, monitoring the host
- `docs/ARCHITECTURE_BLUEPRINT.md` §8 — full configuration model and validation contract
- `src/ElpisEdgeConnect.Core/Configuration/` — authoritative record definitions (`GatewayConfiguration`, `SourceInstanceConfig`, `SinkInstanceConfig`, `RouteConfig`, `BufferPolicyConfig`, `DeliveryPolicyConfig`)
