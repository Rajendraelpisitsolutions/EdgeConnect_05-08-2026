# 03 — Dev gateway config

The Studio + runtime both expect a **gateway data root** — a directory that holds the active config (`current.json`), the gateway identity file, the buffer database, and the audit log. Production sites have this in `C:\ProgramData\Elpis\EdgeConnect\` or `/var/lib/edgeconnect/`; for development you'll keep it in `data/` under the repo root.

## One-liner setup

From the repo root:

```pwsh
.\scripts\dev\setup-dev-config.ps1
```

This script:

1. Creates `data/config/`, `data/buffer/`, and `data/identity/`.
2. Copies the template at `scripts/dev/templates/dev-current.json` to `data/config/current.json`.
3. Sets `EDGECONNECT_DATA_ROOT` for the current shell session.
4. Prints the env vars you should set permanently.

To make the env vars persist across shell sessions, add to your PowerShell `$PROFILE`:

```pwsh
$env:EDGECONNECT_DATA_ROOT     = "C:\dev\EdgeConnect\data"
$env:EDGECONNECT_LICENSE_PATH  = "C:\dev\EdgeConnect\docs\onboarding\dev-license.json"
```

Linux equivalent in `.bashrc` / `.zshrc`:

```bash
export EDGECONNECT_DATA_ROOT="$HOME/code/EdgeConnect/data"
export EDGECONNECT_LICENSE_PATH="$HOME/code/EdgeConnect/docs/onboarding/dev-license.json"
```

## What's in the template?

`scripts/dev/templates/dev-current.json` is intentionally minimal:

- One **Gateway** record with `GatewayId = "dev-gateway"`, `GatewayName = "Dev gateway"`.
- One **MQTT sink** pointing at `localhost:1883` (where Mosquitto runs locally). Disabled by default — flip `Enabled` to `true` once you have Mosquitto running.
- **Zero sources, zero routes.** You add these via the Studio wizards as you exercise the UI.

This is enough state for the Studio to start, the bulk-import wizard to find an MQTT sink, and the runtime to come up without crashing.

## Customize it

Want to start with a richer fixture? Drop in any of the chip-3 sample fixtures:

```pwsh
copy tools\bulk-provision\tests\fixtures\expected\fanuc\cnc-001.gateway.json data\config\current.json
```

These fixtures are also frozen for the deterministic-output test, so they always have valid structure.

## .gitignore

The `data/` directory is in `.gitignore` — every dev has their own local state. Don't commit your `data/config/current.json` even if you've customized it; if you want to share a fixture, add it to `scripts/dev/templates/` instead.

## Done?

Continue to [04-running-studio.md](04-running-studio.md).
