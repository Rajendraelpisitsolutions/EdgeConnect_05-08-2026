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

The license JSON was edited after signing — any byte change breaks the RSA-PSS signature. Restore from `docs/onboarding/dev-license.json`. If the bundled file is also failing, the dev keypair has been rotated and the file needs to be regenerated (see [02-dev-license.md](02-dev-license.md)).

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
