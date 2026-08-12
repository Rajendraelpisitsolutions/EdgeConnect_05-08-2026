# 08 — Troubleshooting

If you hit something not listed here, the answer is usually in one of:

- `CLAUDE.md` (locked decisions + refuse-list)
- `docs/ARCHITECTURE_BLUEPRINT.md` (master architecture)
- The newest file in `docs/sessions/` (most recent in-flight context)
- `git log --oneline -20` (what shipped recently)

When all else fails, ping the repo owner with the **error message + the last few commands you ran**. Don't paraphrase the error.

---

## Build failures

### `error CS0234: The type or namespace name 'BulkSourceMerge' does not exist`

Stale build artifacts after a branch switch. Clean and rebuild:

```pwsh
dotnet clean ElpisEdgeConnect.sln
dotnet build ElpisEdgeConnect.sln
```

### `error MUD0002: Illegal Attribute 'X' on MudY`

MudBlazor analyzer caught a Razor mistake. The MudBlazor docs at `mudblazor.com` are authoritative; the project pins MudBlazor 6.x. Use `MudRadioGroup` for groups of `MudRadio`, not bare `MudRadio` with `SelectedOption`.

### `error CA1859: Change type of field`

Performance analyzer wants the concrete collection type (`Dictionary<,>`) instead of the interface (`IReadOnlyDictionary<,>`) for private fields. Change the type; expose the interface via the property.

### `LF will be replaced by CRLF`

Cosmetic Windows-only warning from git's autocrlf. Safe to ignore. The repo's `.gitattributes` pins LF for the files that matter.

---

## License + config failures

### `License loaded` line missing on startup

`EDGECONNECT_LICENSE_PATH` isn't set or points at a non-existent file.

```pwsh
echo $env:EDGECONNECT_LICENSE_PATH
Test-Path $env:EDGECONNECT_LICENSE_PATH
```

If both look right but you still get the error, your binary may have been built with a different embedded public key. Rebuild from a clean tree.

### `license signature invalid`

The license JSON was edited after signing — any byte change breaks the RSA-PSS signature. Regenerate it with `.\scripts\dev\sign-dev-license.ps1` (see [02-dev-license.md](02-dev-license.md)); the file is local and disposable, so there is nothing to repair.

### `License '...' is issued for gateway '...', but this gateway's identity is '...'`

The license is bound to a different machine. Licenses carry a `gatewayId` and are rejected unless it matches (ADR-0036), while the identity is a UUID minted per machine on first run (ADR-0038). Regenerate for this machine:

```pwsh
.\scripts\dev\sign-dev-license.ps1
```

Note that `Gateway.GatewayId` in `current.json` is a **different field** and is not what binding checks — that value comes from `GatewayIdentityStore`.

### The license survives deleting the license file

Almost always because the file you deleted is not the file the process resolved. The path is chosen by a three-step precedence chain, and the first satisfied rule wins outright:

| Rule | Source | Applies when |
|---|---|---|
| 1 | `EDGECONNECT_LICENSE_PATH` | Set and non-blank — **short-circuits everything below** |
| 2 | `<EDGECONNECT_DATA_ROOT>\edgelicense.json` | Data root set, no explicit license path |
| 3 | `C:\ProgramData\EdgeConnect\edgelicense.json` | Neither env var set — the Windows default |

Deletion detection itself is sound: `LicenseTrialEnforcer` re-stats the resolved path once per second, so delete, edit and replace are all picked up without a restart.

Confirm which file is actually in play before deleting anything — the startup log states it verbatim:

```pwsh
Select-String -Path <log> -Pattern "License file"
```

`EDGECONNECT_LICENSE_PATH` set at **User** or **Machine** scope is inherited by every process started from that account, services included, so a stale override can be in play without anyone realising. Check all three scopes:

```pwsh
foreach ($s in 'Process','User','Machine') {
  "$s = " + [Environment]::GetEnvironmentVariables($s)['EDGECONNECT_LICENSE_PATH']
}
```

**Two similarly-named folders** also cause this. They are different directories with different owners, and only one ever holds a license:

| Folder | Purpose | Holds a license? |
|---|---|---|
| `C:\ProgramData\EdgeConnect\` | The app's default data root — buffer, config, logs, identity | **Yes**, at precedence rule 3 |
| `C:\ProgramData\Elpis\EdgeConnect\` | Machine-anchored identity and clock-anchor slots only (ADR-0038) | **Never** |

Finally: reaching demo mode does not look dramatic. See [demo-trial-cutoff.md](../licensing/demo-trial-cutoff.md) — with no sources configured the demo budget never decrements, and the gateway keeps running by design.

### `Could not find current.json`

`EDGECONNECT_DATA_ROOT` not set or no `config/current.json` under it.

```pwsh
.\scripts\dev\setup-dev-config.ps1
```

### `current.json failed schema validation`

A typo / structural error in your `data/config/current.json`. Run the offline validator to get a precise location:

```pwsh
dotnet run --project tools\ValidateConfig -- data\config\current.json
```

---

## Studio runtime failures

### Port 5080 in use

```pwsh
Get-NetTCPConnection -LocalPort 5080
# Find the PID, kill it (or change the port via $env:EDGECONNECT_MANAGEMENT_PORT=5081)
```

### Studio loads but `Connectivity Studio` doesn't show in the nav

Connectivity Studio is license-gated on the `connectivity-studio` module. The bundled dev license enables it. If you regenerated a license without that module, the Studio's Razor pages silently won't materialize.

### MQTT sink shows `Unreachable`

Mosquitto isn't running on `localhost:1883`. See [05-mqtt-integration-tests.md](05-mqtt-integration-tests.md) to install + verify.

### Studio's bulk-import wizard step 1 says "0 sinks"

Your `current.json`'s only MQTT sink has `Enabled=false`. Flip it to `true` from the Sinks page and refresh.

---

## Test failures

### Pre-existing failures in `ConfigSchemaModelTests`

These two tests fail on master because of a pending count assertion update. Task ID `task_b3eda035` tracks the fix. Doesn't block your build — verify the rest of the suite passes.

### MQTT integration tests skipped

Mosquitto isn't reachable on `localhost:1883`. The tests are designed to skip rather than fail when the broker is absent — install Mosquitto and they'll run.

### Test discovery slow / missing

Restart the test runner. VS / Rider sometimes cache stale assembly metadata after a branch switch.

---

## Git issues

### `error: cannot rebase: You have unstaged changes`

Some files (LinkedIn diffs, `aveva-si-certificate.pdf`) live in the working tree across sessions. Stash before rebasing:

```pwsh
git stash push -m "tmp" -- docs/marketing/
git rebase origin/master
git stash pop
```

### Worktrees under `.claude/worktrees/` cluttering Glob results

These are isolated copies created by previous Claude Code agents. Safe to leave alone — they don't affect the main checkout. If you want to clean:

```pwsh
Get-ChildItem .claude\worktrees\ -Directory | ForEach-Object { git worktree remove $_.FullName }
```

---

## When this guide doesn't answer

Open an issue with:

- The exact command you ran.
- The full error message (paste, don't paraphrase).
- Your env var setup (`gci env: | findstr EDGECONNECT`).
- Recent commits (`git log --oneline -5`).
- Your platform (`pwsh -Version`, `dotnet --info`).
