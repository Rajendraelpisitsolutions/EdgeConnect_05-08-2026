# 04 — Running the Connectivity Studio

The Studio runs in the same process as the runtime. Launching `ElpisEdgeConnect.Management` starts the protocol adapters (per the dev config), the canonical pipeline, and the Blazor Server UI.

## Launch

From the repo root, with `EDGECONNECT_DATA_ROOT` and `EDGECONNECT_LICENSE_PATH` set (see [03-dev-config.md](03-dev-config.md)):

```pwsh
dotnet run --project src\ElpisEdgeConnect.Management
```

You should see something like:

```
[Composition] Loaded gateway identity GW-... from data/identity/gateway-identity.json
[Composition] Loaded license: customer=DevOnly edition=Enterprise expires=2031-12-31
[Composition] No sources configured (current.json Sources[] is empty).
[Composition] One sink configured: dev-mqtt
[Studio] Listening on http://127.0.0.1:5080/
```

Open `http://127.0.0.1:5080/` in your browser. The Studio's Overview page should load with the dev gateway info.

## Walk-through (5 minutes)

Once you're on the Overview page:

1. **Sources** — empty list (we ship zero sources). Click **Add Source** to step through any of the protocol wizards (FOCAS2 / Brother / Modbus / MTConnect / OPC UA Client / S7).
2. **Sinks** — one disabled MQTT sink. Click it to edit; flip `Enabled=true` if you have Mosquitto running.
3. **Routes** — empty until you add a Source and wire it.
4. **Bulk import** — click the new "Bulk import" button on the Sources page to exercise the chip-3 wizard (Phase 1 final deliverable).
5. **Configuration → Drafts** — every wizard save creates a draft; you apply it from this page.
6. **Diagnostics** — the 3-way diagnostics view (source / pipeline / sink) once data is flowing.

## Hot-reload during development

`dotnet watch` works for Razor + C#:

```pwsh
dotnet watch --project src\ElpisEdgeConnect.Management run
```

Razor file edits hot-swap. C# edits in the Studio project rebuild + restart the host (~3-5 s). Edits to `ElpisEdgeConnect.Core` or to adapter modules trigger a full restart.

## Common first-launch failures

| Symptom | Fix |
|---|---|
| `License loaded` line missing | `EDGECONNECT_LICENSE_PATH` not set or pointing at the wrong file. |
| `license signature invalid` | The license JSON was edited (any byte change breaks the signature). Restore from `docs/onboarding/dev-license.json`. |
| `Could not find current.json` | `EDGECONNECT_DATA_ROOT` not set or no `current.json` in `$DataRoot/config/`. Run `setup-dev-config.ps1`. |
| Port 5080 in use | Another Studio instance is already running. `Get-NetTCPConnection -LocalPort 5080` to find it. |
| MQTT sink reports `Unreachable` | Expected — Mosquitto isn't running. See [05-mqtt-integration-tests.md](05-mqtt-integration-tests.md) to install. |

[Troubleshooting](08-troubleshooting.md) covers more.

## Headless runtime (no Studio)

For backend-only testing the headless host is faster to spin up:

```pwsh
dotnet run --project src\ElpisEdgeConnect.Host
```

It uses the same `EDGECONNECT_DATA_ROOT` + `EDGECONNECT_LICENSE_PATH`, has no UI, and logs to stdout. Useful for adapter-level work.

## Done?

Continue to [05-mqtt-integration-tests.md](05-mqtt-integration-tests.md) when you're ready to exercise the MQTT contract, or jump to [06-codebase-tour.md](06-codebase-tour.md) for the lay of the land.
