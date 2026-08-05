# Change record — Kepware ↔ EdgeConnect OPC UA, buffer fix, Studio nav

**Date:** 2026-08-03 (with earlier work on 2026-07-31)
**Gateway:** `gw-elpis-006` on `ELPIS-006`
**Installed build:** `2.1.0.1+67bc31e8` at `C:\Program Files\Elpis EdgeConnect\`
**This source tree:** `2.1.0.4` — **does not match the installed build** (see §6)

---

## 1. Source-code changes (this repo)

Only two files are currently modified. Both are needed for the active-nav-tab
feature and neither is live until this tree is built and deployed.

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Components/Layout/NavMenu.razor` | Added `NavClass(href)`, `@inject NavigationManager`, `@implements IDisposable`, and a `LocationChanged` subscription. Each of the 10 text tabs now uses `Class="@NavClass("/…")"` instead of a static `Class="nav-btn"`. Corrected a comment that wrongly claimed active state was "handled by MudBlazor's NavLink ActiveClass" — these are `MudButton`s and had no active state at all. |
| `src/ElpisEdgeConnect.Management/wwwroot/css/site.css` | Added `.nav-menu .nav-btn.nav-btn-active` (light sky-blue fill `#E0F2FE`, text `#0369A1`, weight 600) plus a hover variant. `color` needs `!important` because each button carries an inline `style="color:#4B5563"`. |

A child route keeps its parent tab lit — `/sources/Modbus/edit` still highlights
**Sources** — via `path.StartsWith(href + "/")`.

### Reverted (no longer modified)

- `Components/Shared/WizardShell.razor` — the `wizard-shell-header` styling hook was removed
- `Components/Pages/Tap.razor` — `.tap-stream` restored to `max-height: 60vh`

Both belonged to a fixed-header/footer app-shell experiment that was **fully
reverted** at the user's request (§4).

### Lost when the folder was replaced

Two 2026-07-31 edits are **no longer present** in this tree and would need
re-applying if still wanted:

- `scripts/dev/sign-dev-license.ps1` — emitted only `source-opc-ua-client`, but the
  adapter gates on **`source-opcua-client`** (`OpcUaClientSourceConfiguration.LicenseModuleKey`),
  and `IsModuleEnabled` does an exact-string lookup. Without both keys, a repo-built
  dev gateway returns 403 from OPC UA Test Connection before Kepware is ever contacted.
- `docs/onboarding/dev-license-opcua.json` — a signed dev licence carrying the correct key.

---

## 2. Installed gateway — static assets

Deployed directly because they are static files, **not** compiled into any assembly,
so the `2.1.0.4` vs `2.1.0.1` mismatch carries no risk.

| File | State | Backup |
|---|---|---|
| `wwwroot\css\site.css` | 3292 bytes — active-tab styling | `site.css.bak-nav` (2689 bytes, original) |
| `wwwroot\js\wizardValidation.js` | 4106 bytes — `ELPIS-NAV-ACTIVE-SHIM` appended | `wizardValidation.js.bak-nav` (2707 bytes) |

The JS shim applies `nav-btn-active` by matching `location.pathname` against each
tab's `href`, re-running every 300 ms because Blazor re-renders the nav on
navigation and would drop the class. It mirrors `NavMenu.NavClass()` exactly.
**Delete it once the gateway is rebuilt from source** — the C# version supersedes it.

No assemblies were modified.

---

## 3. Runtime configuration

### KEPServerEX V6

- **Configuration API enabled** — `settings.ini` `[Config API Service]`:
  `Enabled=0 → 1`, `AllowInsecureCommunications=0 → 1`. Backup: `settings.ini.bak-claude`.
- **Channel `EdgeSim`** (Simulator driver) → **device `Machine1`** → **11 tags**,
  each carrying its Modbus register in the description:

  | Tag | Address | Type |
  |---|---|---|
  | `actual_temperature` | `SINE(1000,140,180,1,0)` | Float |
  | `machine_on_loading_to_unloading` | `RANDOM(3000,0,1)` | Short |
  | `spare_1` | `K0009` | Short, R/W |
  | `total_cycle_time_min` | `RANDOM(5000,8,15)` | Float |
  | `curing_set_time_sec` | `K0017` | Long, R/W |
  | `curing_actual_time_sec` | `RAMP(1000,0,600,10)` | Long |
  | `loading_time_min` | `RANDOM(5000,1,5)` | Float |
  | `unloading_time_min` | `RANDOM(5000,1,4)` | Float |
  | `set_pressure` | `K0033` | Float, R/W |
  | `actual_pressure` | `SINE(500,145,155,1,0)` | Float |
  | `cycle_count` | `RAMP(10000,0,9999,1)` | Long |

  The three `K` registers are writable setpoints and sit at `0` until written —
  they produce almost no subscription updates by design.
- **`PROJECT_OPC_UA_ANONYMOUS_LOGIN`: `False → True`**
- **Client certificate trusted** — the adapter's cert copied into
  `V6\UA\Server\cert\`. NodeIds are `ns=2;s=EdgeSim.Machine1.<tag>`.

### EdgeConnect

Source `OPC_UA_Client` + `route-OPC_UA_Client` created through the
draft → validate → apply pipeline. Endpoint `opc.tcp://127.0.0.1:49320`,
**SignAndEncrypt / Basic256Sha256 / Anonymous** — no security downgrade.

Final state:

```
sources: Modbus (modbustcp), AlenBradely (ethernetip), OPC_UA_Client (opcua-client)
sinks  : ModbusTesting, Alen, O
routes : route-Modbus, route-AlenBradely, route-OPC_UA_Client
```

### Store-and-forward buffers — orphaned cursors pruned

Retention advances only to the **slowest** cursor, and cursors are never removed
when a sink is deleted or renamed. `route-OPC_UA_Client` held three:

```
before: [('OpcUa', 1477), ('OPC_Mqtt', 1480), ('O', 11477)]   points = 10000 (full)
after:  [('O', 11477)]                                        depth  = 1
```

`OpcUa` froze at 1477 and pinned `tail_sequence` there, so the buffer could never
reclaim, sat at its 10 000 ceiling, and silently dropped every new point.
Only rows in the `cursors` table were deleted — **no data points**. Backups:
`route-*.db.bak-20260803-171001` in `C:\ProgramData\EdgeConnect\buffer\`.

---

## 4. Reverted in full — fixed header/footer app shell

An app-shell layout (`html,body{overflow:hidden}` + `.mud-main-content` as the
scroll container, with sticky wizard header/action bar) was built, deployed, and
then **completely reverted**. Installed `site.css` went 6467 → 2689 bytes;
repo and both components restored.

Worth recording, because it will recur if attempted again:

- `MudAppBar` carries `mud-appbar-dense`, so it is **48px**
  (`calc(var(--mud-appbar-height) - var(--mud-appbar-height)/4)`), while
  `MudMainContent` pads `pt-16` = **64px**. That 16px mismatch is what let page
  content show through beside a sticky band.
- `body{overflow:hidden}` makes MudBlazor's scroll lock inert — it tests
  `window.innerWidth > document.body.clientWidth` — so **dialogs and drawers stop
  freezing the page behind them**. Inherent to any app-shell layout here; needs a
  component fix, not CSS.
- `AddOpcUaClientSource.razor:210` pins its summary bar at `top:0`, which lands
  behind the AppBar once the scroll container changes.

---

## 5. Known defects found (not fixed — all need a rebuild)

1. **Orphaned buffer cursors wedge a route permanently.** Deleting or renaming a
   sink strands its cursor; the route fills to `maxDepth` and silently drops live
   data. Surfaces as a misleading MQTT error rather than pointing at the cursor.
   Hit three times in this session.
2. **OPC UA Test Connection can never succeed against a cert-requiring server.**
   `OpcUaClientTestConnectionService.cs:260` uses `$"opcua-probe-{probeId}"` as the
   instance id, which keys the cert store path, so **every click mints a throwaway
   certificate**. Ten orphaned `opcua-probe-*` stores were observed on disk. Fix:
   a stable instance id, or expose `ApplicationCertificateStorePath` on the probe DTO.
3. **`_gatewayId` is never assigned** — `OpcUaClientSourceAdapter.cs:69` initialises
   it to `"edgeconnect-unknown-gateway"` and only ever reads it, so OPC UA points
   publish to the wrong MQTT topic. Compare `ModbusTcpSourceAdapter.cs:188`, which
   resolves it correctly.
4. **Connection-property changes do not hot-reload.** `clientId` and `qosLevel`
   edits applied with `applied=[]` and required a service restart; the apply reports
   success while the running adapter keeps the old settings.
5. **Stale `lastError` is shown as if current.** The Studio renders the sticky last
   error with no ageing — a 19-minute-old MQTT blip was displayed as a live fault.

---

## 6. Why code fixes were not applied

```
Installed : every assembly 2.1.0.1 + 67bc31e8, built 2026-08-03 12:33
This tree : 2.1.0.4, no matching git commit
SDK       : only .NET 10.0.300; global.json pins 8.0.100 (rollForward: latestFeature)
```

Dropping a `2.1.0.4` assembly beside eleven `2.1.0.1` siblings risks
`MissingMethodException` on any signature that drifted between commits. To fix
items 1–5 above: obtain the `67bc31e8` source, install the .NET 8 SDK, then
rebuild and redeploy through the normal installer.

---

## 7. Open item

**KEPServerEX V6's Configuration API is still enabled over plain HTTP on
`localhost:57412`, and its `Administrator` account has no password** — a blank
password authenticates. It was enabled to create the 11 tags. Either set
`Enabled=0` (restore `settings.ini.bak-claude`) or set an Administrator password.

---

## 8. Rollback quick reference

| To undo | Do |
|---|---|
| Active-tab highlight | Restore `site.css.bak-nav` and `wizardValidation.js.bak-nav` |
| Kepware Config API | Restore `settings.ini.bak-claude`, restart `KEPServerEXConfigAPI6` + `KEPServerEXV6` |
| Buffer cursor prune | Restore `route-*.db.bak-20260803-171001` (service stopped) |
| OPC UA source | Delete `OPC_UA_Client` + `route-OPC_UA_Client` via the config draft pipeline |
| Kepware tags | Delete channel `EdgeSim` via Config API or the Configuration GUI |
