# 00 — Prerequisites

You need a Windows or Linux machine with the following installed. The bootstrap script verifies most of these and reports clearly what's missing.

## Required

| Tool | Version | Why |
|---|---|---|
| .NET SDK | **8.0+** | All projects target `net8.0`. |
| Git | Any recent | Repo + worktrees. |
| PowerShell | **7+** (`pwsh`) | Build / test / chip-3 scripts use pwsh 7 features (raw-string-literal here-strings, `&&` / `\|\|` chain operators, ternary). Windows PowerShell 5.1 will fail subtly — install pwsh 7 alongside. |

Verify:

```pwsh
dotnet --version           # expect 8.x.x
pwsh -Version              # expect 7.x.x
git --version              # any recent
```

## Recommended

| Tool | Why |
|---|---|
| Visual Studio 2022 or Rider | Solution-aware C# editing, Razor IntelliSense for MudBlazor. VS Code with C# Dev Kit works for backend but Razor support is weaker. |
| [Mosquitto](https://mosquitto.org/download/) | Local MQTT broker on `localhost:1883` (anonymous). Required to run the MQTT integration tests. Optional if you're only touching backend / Studio UI. |
| Postman / Insomnia / `curl` | Hitting the Studio's API endpoints (`/api/v1/...`) for debugging. |

## Optional (per-track)

| Track | Extra tools |
|---|---|
| MTConnect adapter work | Access to an MTConnect agent or [a sample agent](https://github.com/mtconnect/cppagent) to point at. |
| Modbus TCP adapter work | The repo ships `tools/ModbusSimulator/` — a Python simulator (uses `pymodbus`). The `.venv/` is created on first use. |
| Brother HTTP adapter work | A Brother Speedio CNC or a stub HTTP server returning the documented Brother XML shape. |

## Platform notes

- **Windows 11 is the primary dev OS.** The runtime targets Windows Service on Windows + systemd on Linux; the cross-platform code paths use `OperatingSystem.IsWindows()` guards.
- **WSL2 works for backend / unit tests** but the Studio's per-circuit Blazor Server connection prefers native Windows for hot-reload reliability.
- **macOS** — backend / unit tests build and run; Studio launches but hasn't been thoroughly exercised. File an issue if you find a Mac-only regression.

## Network egress

The build pulls NuGet packages from `nuget.org`. If you're behind a corporate proxy, configure `dotnet` to use it before running `dotnet restore`:

```pwsh
$env:HTTP_PROXY = "http://your-proxy:port"
$env:HTTPS_PROXY = "http://your-proxy:port"
```

Once the repo is set up, the runtime is fully offline — no phone-home, no per-run telemetry. License validation is local.

## Done?

Continue to [01-clone-build-test.md](01-clone-build-test.md).
