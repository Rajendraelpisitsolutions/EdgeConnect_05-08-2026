# M.P2.2 hot-reload — manual smoke procedure

**Milestone:** M.P2.2 phase 3
**Verifies:** Runtime hot-reload coordinator (ADR-0009) end-to-end on a live gateway, including the apply / rollback reload-outcome surface, per-adapter isolation, and the Studio reload panel.
**Audience:** Engineering / support running the demo gateway.

> This is **manual** by design. The automated test suite (24 tests across phases 1-3) pins the unit-level invariants; this procedure pins the operator-realism path on a real Management process against a real demo config. CI does not run it. The pwsh helpers in `scripts/smoke/` make the procedure reproducible without binding it to a CI integration target.

---

## 1. Prerequisites

- Gateway service / Studio running from `C:\dev\EdgeConnect` (or wherever the M.P2.2 build is).
- Demo config primed at `$env:LOCALAPPDATA\edgeconnect-uademo\config\current.json` (the M.P2.1 phase 3b poison baseline).
- Browser pointed at the Studio (usually `http://127.0.0.1:5080/config`).
- PowerShell 7+ on the PATH (for the helper scripts).
- PowerShell execution policy that allows local scripts. Default Windows policy (`Restricted`) blocks `.ps1` files. One-time fix:

  ```powershell
  Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
  ```

  Does not require admin. Allows locally-authored scripts while still blocking unsigned remote ones. (Alternative: `powershell -ExecutionPolicy Bypass -File .\scripts\smoke\<script>.ps1 ...` for one-off invocations without changing policy.)

- The Management API listening at `http://127.0.0.1:5080` (adjust `-BaseUrl` in the scripts if yours differs).

## 2. Baseline check

Before starting, confirm the demo gateway is in the expected poison state. Two surfaces matter, and they show different things:

### Sources page (`/sources` in the Studio) — full per-source inventory

Renders every configured source with its aggregate health state. Expect:

| Instance | State on Sources page | Reason |
|---|---|---|
| `modbus-demo` | **Running** | Healthy mock source. |
| `modbus-line-2` | **Disabled** | `Enabled = false` in config. |
| `modbus-line-3` | **Running** (with `MODBUS.CONNECT_FAILED` warnings in the gateway log) | The Modbus adapter handles unreachable hosts via a background connect-retry loop with exponential backoff — it stays alive in the supervisor. Operators see the disconnect in logs, not as a "Faulted" state. This is by design (transient network flap shouldn't take a source down permanently). |
| `Modbus-4` | **Faulted** | `CONFIG.SOURCE_WITHOUT_ROUTE` — cross-record validation fault. |

### Diagnostics page (`/diagnostics` in the Studio) or `show-faults.ps1` — configuration-fault registry only

This surface only renders entries in `IConfigurationFaultRegistry` (cross-record validation faults observed at startup + reconcile time). Adapter runtime retry loops do NOT write here. Expect a SINGLE entry:

| Instance | Kind | ErrorCode |
|---|---|---|
| `Modbus-4` | Source | `CONFIG.SOURCE_WITHOUT_ROUTE` |

`modbus-line-3` does NOT appear in the Diagnostics page or `show-faults.ps1` output — its `MODBUS.CONNECT_FAILED` is an adapter-internal signal visible only on the Sources page and in the gateway log.

If either surface diverges from the table above (e.g. someone re-applied a clean config, or `modbus-line-3` got removed), restore by overwriting `current.json` from the seed under `samples/edgeconnect-uademo/` (or whichever directory holds your seed). Otherwise the smoke will exercise a different surface than intended.

## 3. Smoke 1 — clean apply

This apply should land cleanly: every chip green or amber, no red faults, fault registry drained for the formerly-poisoned instances.

### What to do

Prepare an edited copy of the current config (see the **How to edit the config** subsection below for the workflow), then apply it.

The edits this scenario needs:

1. **Remove** the `modbus-line-3` source from `sources[]` (the unreachable one).
2. **Modify** `modbus-demo`'s polling cadence (e.g. change `pollIntervalMs` from `1000` to `500` — the exact field name depends on the seed).
3. **Add** a new working source — e.g. another `modbus-demo`-shaped record with a different instance id (`modbus-line-5`) that points at a reachable address (or a known-good mock).
4. **Add a route** that references `Modbus-4` (the formerly orphaned source). As of M.P2.3 (ADR-0010) this is sufficient — the coordinator's synthesis pre-pass catches startup-skipped instances whose cross-record validity has flipped and re-attempts them automatically. (For M.P2.2-only gateways, also touch any field on Modbus-4's source record; see `docs/ops-runbook.md` §5 for the underlying mechanism.)

Apply via Studio (`/config` → **Import draft from JSON** → file upload or paste → **Validate** → **Apply** → type `APPLY` to confirm) OR via the pwsh helper:

```powershell
.\scripts\smoke\apply-config.ps1 -ConfigPath C:\Temp\smoke-1-draft.json
```

> **Reload panel renders only on Studio-initiated Applies.** The panel binds to the response of `ApplyDraftAsync` / `RollbackToVersionAsync` inside the Razor page — it cannot observe external API calls (no SignalR / push, per phase 3 guardrail). When you apply via `apply-config.ps1`, the `reload` block is in the helper's stdout JSON instead; the Studio page itself shows no panel for that apply.

### How to edit the config

The Studio's **View JSON** is read-only by design — no inline JSON editor lives in the page, which protects untyped fields from typo-driven malformed drafts. Editing happens out-of-band:

```powershell
# 1. Copy current.json to a working file
Copy-Item "$env:LOCALAPPDATA\edgeconnect-uademo\config\current.json" `
          "C:\Temp\smoke-1-draft.json"

# 2. Open it in any editor — VS Code, Notepad++, Notepad, whatever
code "C:\Temp\smoke-1-draft.json"

# 3. Apply edits, save, then apply via the helper (auto-imports + validates + applies)
.\scripts\smoke\apply-config.ps1 -ConfigPath "C:\Temp\smoke-1-draft.json"
```

The helper prints the API response — look at the `reload` block at the bottom to see what came up, restarted, faulted. Refresh `/config` in the browser to see the reload panel.

**Do NOT edit `current.json` directly.** The gateway only re-reads on Apply through the API; an out-of-band file edit would diverge the on-disk state from the running runtime and break the audit chain. Always edit a COPY and apply via the API / helper.

### Manual Studio-only path (if you don't want to touch PowerShell)

1. `/config` → **View JSON** on the active config → select-all → copy.
2. Paste into any editor, apply the edits above, save / keep on the clipboard.
3. `/config` → **Import draft from JSON** → paste → name the draft → submit.
4. The draft appears in the Drafts section. Click **Validate** to confirm clean, then **Apply** and type `APPLY` to confirm.

### What to expect on the reload panel

Above the active-config card, a card appears:

- **"Reload outcome — Completed"** header
- Elapsed time shown (probably 50-500 ms for healthy mocks; up to a couple of seconds if cold-starting a Modbus connection)
- Three chip rows:
  - **Applied (green):** `modbus-line-5`, the new route, the `Modbus-4` route
  - **Restarted (amber):** `modbus-demo` (because its cadence was modified)
  - **Faulted (red):** empty
- The "No runtime work" placeholder text must NOT appear.

### Cross-check

Run `.\scripts\smoke\show-faults.ps1` again. Expected:

- `Modbus-4` — gone from the configuration-fault registry (route added → ClearFor on successful re-init).
- Registry is now **empty** (only `Modbus-4` was ever in it; see §2 above).

Open the Sources page (`/sources`) and check the inventory:

- `modbus-line-3` — gone entirely (instance was removed from config). The `MODBUS.CONNECT_FAILED` warnings should also stop appearing in the gateway log.
- `modbus-demo` — Running, observable metrics rate should have doubled (since cadence halved).
- `modbus-line-5` — Running (newly added).
- `Modbus-4` — Running (now has a route).
- `modbus-line-2` — still Disabled (unchanged).

If `modbus-demo`'s rate did NOT change after the cadence edit, **pause and investigate** — either the seed used a different field name or the restart-on-modify path missed something.

## 4. Smoke 2 — per-adapter isolation

This apply introduces a deliberately-broken Modbus source to verify the **isolation invariant**: a broken adapter must NOT take down its neighbors. Other sources from Smoke 1 must continue running unaffected.

> **Adapter design note.** The Modbus adapter handles unreachable hosts via an async background connect-retry loop (see §2 above), so a broken Modbus source comes up "Running" in the supervisor and surfaces only via gateway log warnings — not via the reload panel's red chip path. That's by design and not a regression. The red-chip path is exercised by the automated tests (`Reconcile_OnInstanceFault_OutcomeContainsFaultedEntry_WithErrorCode` against a mock with `ThrowOnInitialize`). To exercise a true red chip manually, use the alternative scenario at the end of this section.

### What to do

Prepare an edited copy of the current (post-Smoke-1) config using the workflow in §3 → **How to edit the config**:

1. **Add** a new source `modbus-broken` that points at a deliberately-bad address (e.g. `192.0.2.1:502` — the IETF "invalid" reserved range, guaranteed no listener).
2. **Add** a route for it so it's not flagged by the cross-record validator.

Save the draft, **Apply** (via Studio or `apply-config.ps1`).

### What to expect on the reload panel

- **"Reload outcome — Completed"** (the orchestration succeeded; "Completed" is the orchestration-level outcome, not "all healthy")
- Chip rows:
  - **Applied (green):** `modbus-broken` and its new route id (the supervisor's AddAsync succeeded; the connect-retry loop spun up in the background)
  - **Restarted (amber):** empty
  - **Faulted (red):** empty
- Other sources from Smoke 1 (`modbus-demo`, `modbus-line-5`) must NOT appear in any list.

### Cross-check (the load-bearing assertion)

This is what Smoke 2 actually verifies.

1. **Gateway log** — within a few seconds, `MODBUS.CONNECT_FAILED` warnings for `modbus-broken` start appearing on a backoff schedule (2s, 4s, 8s, ...).
2. **Sources page (`/sources`)**:
   - `modbus-broken` — Running (with connect-failure warnings logged).
   - `modbus-demo`, `modbus-line-5`, `Modbus-4` — **still Running, unaffected**.
3. **Diagnostics page** — registry stays empty (no config-validation fault was triggered).
4. **`modbus-demo` metrics rate** — should continue ticking at its post-Smoke-1 cadence. If it stopped or stuttered, the broken source took down a neighbor — that would be the isolation invariant failing, and is a real bug to report.

### Alternative scenario — exercise the red-chip path manually

If you specifically want to see a red chip in the reload panel, induce a synchronous error path the coordinator can catch. Easiest:

1. **Add** a route in the draft that references a source whose `instanceId` doesn't exist in the config (e.g. `nonexistent-src`).
2. Save and Apply.

`BuildOne` will silently fail (registers `CONFIG.ROUTE_REFERENCES_MISSING_SOURCE` and returns null), so this won't actually produce a red chip either — it'll appear in the Diagnostics page instead. The truth is: red chips in production are rare because most adapter / config errors are handled by other channels. The smoke procedure's main job is the isolation assertion above, not chip diversity.

## 5. Smoke 3 — rollback recovery (optional)

If Smoke 2's broken-source state offends, prove rollback works.

### What to do

In `/config` → Version history, click **Rollback** on the version that landed Smoke 1 (the clean post-fix state). Confirm.

### What to expect

- Reload panel reappears with **"Reload outcome — Completed"**
- **Applied (green):** ids that were in Smoke 1 but not Smoke 2 (depends on the broken-source-add net delta)
- **Restarted (amber):** typically empty unless the rollback hit something with a config delta
- **Faulted (red):** empty (you're rolling back to the known-clean state)
- After rollback, `modbus-broken` should be gone from the fault registry (`.\scripts\smoke\show-faults.ps1`).

## 6. Procedure-level pass criteria

The smoke passes when **all** of:

1. Smoke 1's panel showed Completed with green / amber chips and no Faulted entries.
2. `modbus-demo` metrics rate doubled after the cadence change.
3. `modbus-line-3` and `Modbus-4` disappeared from the fault registry after Smoke 1.
4. Smoke 2's panel showed `Reload outcome — Completed` with `modbus-broken` in AppliedInstances. The Modbus adapter's connect-retry loop kicked in for `modbus-broken` (`MODBUS.CONNECT_FAILED` warnings in the gateway log on a backoff schedule).
5. Smoke 2 did NOT cause any other source to stop, fault, or stutter — `modbus-demo`'s metrics rate stayed steady through and after the broken-source apply. **This is the isolation invariant and is the load-bearing assertion of Smoke 2.**
6. (Optional) Smoke 3's rollback panel showed Completed with `modbus-broken` removed from the registry.

If any of those fail, the failure is reportable against M.P2.2 phase 3 — record the gateway logs at the time of the failed apply alongside the response body.

## 7. Reset for next run

To rerun the smoke against the original baseline:

1. Stop the gateway.
2. Overwrite `$env:LOCALAPPDATA\edgeconnect-uademo\config\current.json` with the seed.
3. (Optional) Clear `$env:LOCALAPPDATA\edgeconnect-uademo\audit\` if you want a clean audit chain.
4. Restart the gateway.

---

## References

- ADR-0009 — `docs/decisions/0009-runtime-hot-reload-instance-granularity.md` (architectural shape)
- Phase 3 plan — `docs/sessions/2026-05-16-mp22-phase3-plan.md` v2 (locked)
- Helper scripts — `scripts/smoke/apply-config.ps1`, `wait-for-reload.ps1`, `show-faults.ps1`
- API surface — `docs/config-authoring.md` §8 (draft → apply flow + "What happens after Apply")
- Troubleshooting — `docs/ops-runbook.md` §"Config change didn't take effect"
