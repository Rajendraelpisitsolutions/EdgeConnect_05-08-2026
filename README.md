# Elpis EdgeConnect

**Industrial Edge Integration Platform** — a high-performance, scalable .NET 8 service that collects real-time data from CNC machines via multiple industrial protocols (MT-LINKi, FOCAS2, MTConnect, Brother HTTP) and publishes telemetry to an MQTT broker.

## Features

- **Scalable**: Handles 100s of CNC machines with bounded concurrency and back-pressure
- **Resilient**: Per-machine retry + circuit breaker — one failing machine won't affect others
- **Secure**: TLS everywhere, encrypted secrets, certificate pinning, non-root container
- **Observable**: Structured logging (Serilog), MQTT diagnostics topic, health checks
- **Hot-Reloadable**: Add/remove machines without restarting the service
- **Containerized**: Docker + docker-compose ready

## Quick Start

### Prerequisites

- .NET 8 SDK
- MQTT Broker (Mosquitto, HiveMQ, EMQX, etc.)
- CNC machines with supported protocols (FOCAS2, MT-LINKi, MTConnect, or Brother HTTP)

### 1. Generate Encryption Key

```bash
dotnet run --project src/ElpisEdgeConnect -- generate-key
# Output: New AES-256 key: <base64-key>
# Set as environment variable:
export EDGECONNECT_ENCRYPTION_KEY="<base64-key>"
```

### 2. Encrypt Machine Passwords

```bash
dotnet run --project src/ElpisEdgeConnect -- encrypt "my-secret-password"
# Output: enc:<iv>:<ciphertext>
# Paste into appsettings.json
```

### 3. Configure Machines

Edit `appsettings.json` — add your CNC machines:

```json
{
  "Machines": [
    {
      "MachineId": "CNC-001",
      "MachineName": "Lathe Bay 1",
      "BaseUrl": "https://192.168.1.101:8193/",
      "Username": "operator",
      "Password": "enc:<iv>:<ciphertext>",
      "AuthType": "Basic",
      "PollIntervalMs": 1000,
      "Enabled": true,
      "DataPoints": [
        "Program/MainProgram",
        "Program/RunningStatus",
        "Axes/Position/Absolute",
        "Spindle/Speed",
        "Spindle/Load",
        "Alarms/Active",
        "CycleTime",
        "PartsCount"
      ]
    }
  ]
}
```

### 4. Configure MQTT Broker

```json
{
  "MqttSettings": {
    "BrokerHost": "your-broker.example.com",
    "BrokerPort": 8883,
    "UseTls": true,
    "Username": "bridge_user",
    "Password": "env:MQTT_PASSWORD",
    "TopicPrefix": "factory/cnc"
  }
}
```

### 5. Run

```bash
# Direct
dotnet run --project src/ElpisEdgeConnect

# Docker
docker-compose up -d
```

## MQTT Topic Structure

```
factory/cnc/{machineId}/data          # Machine telemetry (JSON)
factory/cnc/$bridge/status            # Bridge online/offline (retained)
factory/cnc/$bridge/diagnostics       # Bridge health metrics (retained)
```

### Sample Payload

```json
{
  "machineId": "CNC-001",
  "machineName": "Lathe Bay 1",
  "timestamp": "2026-02-10T14:30:00.000Z",
  "status": "Online",
  "mainProgram": "O1234",
  "runningStatus": "Running",
  "axes": {
    "X": { "axisName": "X", "absolutePosition": 125.456 },
    "Z": { "axisName": "Z", "absolutePosition": -50.123 }
  },
  "spindle": { "speed": 3500.0, "load": 45.2 },
  "activeAlarms": [],
  "cycleTimeSeconds": 142.5,
  "partsCount": 1247,
  "feedRate": 200.0,
  "tags": ["lathe", "bay-1"],
  "collectionDurationMs": 45
}
```

## Secret Management

Three methods for providing secrets (passwords, tokens):

| Method | Format | Example |
|--------|--------|---------|
| **Environment variable** | `env:VAR_NAME` | `"Password": "env:CNC001_PASSWORD"` |
| **AES-encrypted** | `enc:<iv>:<ciphertext>` | Use the `encrypt` CLI command |
| **Plain text** | Raw string | Not recommended for production |

## Scaling to 100+ Machines

| Setting | Default | Recommended (100+ machines) |
|---------|---------|----------------------------|
| `MaxParallelPolls` | 50 | 80-100 |
| `ChannelCapacity` | 10,000 | 50,000 |
| `PublishBatchSize` | 100 | 200-500 |
| `PollIntervalMs` | 1000 | 1000-2000 |

For **1000+ machines**, run multiple bridge instances with partitioned machine lists:
- Instance 1: CNC-001 through CNC-500
- Instance 2: CNC-501 through CNC-1000

## Health Check

```bash
curl http://localhost:8080/health
```

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed design documentation.

## Tools

- **[`tools/bulk-provision/`](tools/bulk-provision/README.md)** — offline provisioner that generates `gateway.json` configs in bulk from a CSV of devices + a per-gateway sidecar. Use it for fleets of CNCs / PLCs / gateways. Templates ship for FOCAS2, Brother HTTP, and Modbus TCP.
- **[`tools/ValidateConfig/`](tools/ValidateConfig/)** — CLI validator for `gateway.json` against the canonical schema, including ADR-0030 suspect-roots warnings with typo suggestions.

## License

MIT
