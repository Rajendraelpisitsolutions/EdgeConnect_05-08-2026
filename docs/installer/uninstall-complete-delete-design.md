# Uninstall — "Complete delete" option

- **Status:** DESIGN. Not implemented.
- **Date:** 2026-08-08
- **Scope:** MSI uninstall behaviour only (`installer/ElpisEdgeConnect.wxs`).
- **Requirement:** Add exactly **one** checkbox to uninstall. When ticked, remove the
  program files and the gateway data, **except the license file**.

---

## 1. What changes, in one paragraph

Uninstall gains a single checkbox, **"Also delete all EdgeConnect data"**, unticked by
default. Left unticked, uninstall behaves exactly as it does today — the program files go,
everything under `%ProgramData%\EdgeConnect` stays. Ticked, the data directory is removed
as well: configuration, drafts, history, audit log, store-and-forward buffers, and logs.
The license file is never touched by either path.

---

## 2. What uninstall does today (measured, not assumed)

The MSI removes only what it installed: the files under
`%ProgramFiles%\Elpis EdgeConnect\`, the Start Menu and Desktop shortcuts, and the
`ElpisEdgeConnect` service registration. **Nothing under `%ProgramData%` is authored as an
MSI component, so none of it is removed.**

Measured layout of the data root on a live gateway (`C:\ProgramData\EdgeConnect`):

| Entry | Type | What it holds |
|---|---|---|
| `config\` | dir | `current.json`, `drafts\`, `history\` (versions + `audit.log`) |
| `buffer\` | dir | Per-route SQLite store-and-forward databases (`route-*.db`, `-wal`, `-shm`) |
| `logs\` | dir | Per-source opt-in diagnostic logs |
| `edgelicense.json` | file | **The signed license** — `<dataRoot>\edgelicense.json` |
| `identity` | file | Per-data-root copy of the gateway identity |
| `.clock-anchor-v2` | file | Tamper-detection clock anchor |
| `.edgeconnect-version` | file | Data-format version marker |

The buffers are the bulk: on the measured machine, `buffer\` held ~15 MB across seven
routes, with write-ahead logs of 4 MB each on active routes.

---

## 3. Why a "leftovers" option is worth having

Three consequences of the current behaviour justify the checkbox:

1. **Uninstall followed by reinstall is not a clean slate.** The old configuration,
   including every source, destination and route, comes back — surprising an engineer who
   uninstalled specifically to start over.
2. **Buffers can be large and are meaningless without the product.** They are per-route
   SQLite databases of undelivered payloads; once the gateway is gone they are unreadable
   dead weight.
3. **The audit chain and diagnostic logs are customer process data.** A site decommissioning
   a gateway has a reasonable expectation that "uninstall and tick the box" removes them.

---

## 4. The finding that shapes this design

**The license is node-locked to a gateway id, and the gateway id is anchored in three
places — two of which are outside the data root.**

ADR-0036 binds a license to a specific `gatewayId`; ADR-0038 anchors that id at:

1. `%ProgramData%\Elpis\EdgeConnect\identity` — machine-wide, **authoritative on read**
2. `%ProgramData%\EdgeConnect\identity` — the per-data-root copy
3. `HKLM\SOFTWARE\Elpis\EdgeConnect\GatewayId` — survives deleting any ProgramData folder

Verified on the development machine: all three anchors are present, the registry value
holding `<uuid>|<hmac>`.

**Therefore "keep the license file" is only meaningful if the identity anchors are also
kept.** A preserved `edgelicense.json` beside a regenerated gateway id is a file that can
never validate again — the customer keeps a license that has become scrap. The design
below deletes the per-root `identity` copy (harmless: it is restored from the machine-wide
anchor on next start) and **leaves the machine-wide file and the registry value alone**.

This is a deliberate consequence of the requirement, not an oversight. A true
factory-reset — new gateway id, license invalidated — is a different operation and is
called out in §10 as out of scope.

---

## 5. Exactly what is kept and what is removed

| Path | Uninstall (default) | Uninstall + complete delete |
|---|---|---|
| `%ProgramFiles%\Elpis EdgeConnect\**` | removed | removed |
| Start Menu + Desktop shortcuts | removed | removed |
| `ElpisEdgeConnect` service registration | removed | removed |
| `%ProgramData%\EdgeConnect\config\**` | **kept** | **removed** |
| `%ProgramData%\EdgeConnect\buffer\**` | **kept** | **removed** |
| `%ProgramData%\EdgeConnect\logs\**` | **kept** | **removed** |
| `%ProgramData%\EdgeConnect\.clock-anchor-v2` | kept | removed |
| `%ProgramData%\EdgeConnect\.edgeconnect-version` | kept | removed |
| `%ProgramData%\EdgeConnect\identity` | kept | removed (restored from machine anchor) |
| **`%ProgramData%\EdgeConnect\edgelicense.json`** | **kept** | **KEPT — the requirement** |
| `%ProgramData%\Elpis\EdgeConnect\identity` | kept | **kept** (see §4) |
| `HKLM\SOFTWARE\Elpis\EdgeConnect` | kept | **kept** (see §4) |

The data root itself is left as an empty directory holding only `edgelicense.json`. It is
not removed, because removing it would take the license with it.

---

## 6. User interface

**One checkbox, on the removal confirmation.** The installer already ships `WixUI_FeatureTree`,
whose maintenance path is *MaintenanceWelcomeDlg → MaintenanceTypeDlg → VerifyRemoveDlg →
Progress*. The checkbox is added to **`VerifyRemoveDlg`**, the dialog that already asks
"are you sure". No new dialog and no navigation rewiring — the smallest change that puts
the choice in front of the operator at the moment they confirm.

```
┌─ Remove Elpis EdgeConnect ─────────────────────────────────────┐
│                                                                │
│  Click Remove to remove Elpis EdgeConnect from your computer.  │
│                                                                │
│  ☐  Also delete all EdgeConnect data                           │
│     Removes your configuration, route buffers and logs.        │
│     Your license file is kept.                                 │
│                                                                │
│                          [ Back ]  [ Remove ]  [ Cancel ]      │
└────────────────────────────────────────────────────────────────┘
```

Wording rules that the control must honour:

- **The checkbox says what is deleted, in the operator's nouns** — configuration, buffers,
  logs — not "data directory" or "ProgramData".
- **It states the license is kept**, on the dialog itself. This is the one question an
  operator will otherwise have to guess at, and guessing wrong costs them a support call.
- **It is unticked by default.** Data loss is never the default, and an operator who clicks
  straight through gets today's behaviour.

---

## 7. Mechanism

### Property

A **public** property so it can be set from the command line for unattended uninstall:

```xml
<Property Id="COMPLETEDELETE" Secure="yes" />
```

Unset = unticked. The checkbox binds to it with `CheckBoxValue="1"`.

### Two implementation options

**Option A — declarative, no custom action (RECOMMENDED).**

Use `util:RemoveFolderEx` for the three subdirectories and `RemoveFile` for the loose
files, all inside a component conditioned on `COMPLETEDELETE`. `RemoveFolderEx` resolves
its target from a registry value, so the resolved data root is written to
`HKLM\SOFTWARE\Elpis\EdgeConnect\DataRoot` at install time and read back at uninstall.

The license file is preserved **by never being named in a removal rule** — exclusion by
omission rather than by filtering, which is the safer construction: a future edit cannot
accidentally widen a filter it does not have.

```xml
<Component Id="DataCleanup" Directory="INSTALLFOLDER" Guid="..."
           Condition="COMPLETEDELETE = 1">
  <RegistryValue Root="HKLM" Key="SOFTWARE\Elpis\EdgeConnect"
                 Name="DataRoot" Type="string" Value="[CommonAppDataFolder]EdgeConnect"
                 KeyPath="yes" />
  <util:RemoveFolderEx On="uninstall" Property="ECDATA_CONFIG" />
  <util:RemoveFolderEx On="uninstall" Property="ECDATA_BUFFER" />
  <util:RemoveFolderEx On="uninstall" Property="ECDATA_LOGS" />
  <RemoveFile Id="RmClockAnchor" Directory="ECDATA_ROOT" Name=".clock-anchor-v2" On="uninstall" />
  <RemoveFile Id="RmVersionMark" Directory="ECDATA_ROOT" Name=".edgeconnect-version" On="uninstall" />
  <RemoveFile Id="RmRootIdentity" Directory="ECDATA_ROOT" Name="identity" On="uninstall" />
</Component>
```

*Pros:* no shipped custom-action binary; works identically in silent mode; rollback and
logging handled by the Windows Installer engine; nothing to code-sign.
*Cons:* cannot express "delete everything except X" directly — hence the enumerate-what-goes
construction above. A new data subdirectory added later must be added here too, or it will
be left behind. §9 covers the test that catches that.

**Option B — custom action executable.**

Ship a small console app that resolves the data root (honouring `EDGECONNECT_DATA_ROOT`),
deletes everything except `edgelicense.json`, and logs what it did. Scheduled deferred,
`Impersonate="no"`, after `RemoveFiles`.

*Pros:* honours a relocated data root; "everything except X" is expressed directly; can log
a manifest of what was deleted.
*Cons:* a shipped binary that must be signed and maintained; deferred custom actions cannot
read properties directly (needs a `CustomActionData` marshalling step); failures inside a
deferred CA are awkward to surface; no automatic rollback.

**Recommendation: Option A**, with the data root recorded in the registry at first start so
a relocated `EDGECONNECT_DATA_ROOT` is still honoured. Reach for B only if a customer needs
the deletion manifest.

### Sequencing and guards

The cleanup component must be conditioned on all three of:

```
REMOVE = "ALL"  AND  COMPLETEDELETE = 1  AND  NOT UPGRADINGPRODUCTCODE
```

`NOT UPGRADINGPRODUCTCODE` is **load-bearing**. A major upgrade uninstalls the previous
product as part of its sequence; without this guard, an upgrade could delete the customer's
entire configuration. The property is set by the Windows Installer only during the
uninstall-half of a major upgrade, which is exactly the case to exclude.

The existing `ServiceControl` already stops the service with `Wait="yes"` before file
removal, so the SQLite buffers are closed by the time the cleanup runs. No change needed.

### Unattended uninstall

```
msiexec /x {ProductCode} COMPLETEDELETE=1 /qn
```

Omitting the property gives the default (data kept), matching the UI default.

---

## 8. Failure modes considered

| Risk | Mitigation |
|---|---|
| Major upgrade wipes customer config | `NOT UPGRADINGPRODUCTCODE` guard (§7); dedicated test |
| Operator ticks the box not realising it removes routes | Checkbox text names configuration/buffers/logs explicitly; unticked default |
| License deleted with the rest | License is never named in any removal rule; dedicated test asserts survival |
| Preserved license becomes unusable | Machine-wide identity + registry anchors deliberately kept (§4) |
| A file is locked and deletion fails | Service is stopped with `Wait="yes"` before `RemoveFiles`; Windows Installer reports failures |
| A future data subdirectory is missed | Test 7 in §9 diffs the data root against the removal rules |

---

## 9. Test plan

| # | Scenario | Expected |
|---|---|---|
| 1 | Uninstall, box unticked | Program files gone; `%ProgramData%\EdgeConnect` untouched |
| 2 | Uninstall, box ticked | `config\`, `buffer\`, `logs\` gone; `edgelicense.json` present |
| 3 | Uninstall, box ticked, no license file present | Succeeds; no error about the missing file |
| 4 | Ticked uninstall → reinstall → start | Clean empty config self-provisions; previously issued license still validates against the same gateway id |
| 5 | **Major upgrade** over an existing install | Config, buffers and license all survive regardless of the property |
| 6 | Silent `COMPLETEDELETE=1 /qn` | Same outcome as test 2 |
| 7 | Data-root drift check | Every entry the running product creates under the data root is either in a removal rule or deliberately listed as kept in §5 |
| 8 | Uninstall while the service is running | Service stops first; buffers deleted with no file-in-use error |

Tests 4 and 5 are the two that protect real customer value; neither can be verified by
building the MSI alone — both need an install/uninstall cycle on a clean VM.

---

## 10. Out of scope

- **Factory reset.** Clearing the gateway identity (and thereby invalidating the node-locked
  license) is a different operation with different consequences. If it is wanted, it needs
  its own decision and its own, more strongly worded, confirmation.
- **Deleting the license file.** Explicitly excluded by the requirement.
- **More than one checkbox.** The requirement is one; per-category choices (keep buffers,
  drop logs) would multiply the paths to test for little operator benefit.

---

## 11. Implementation checklist

1. Add the `COMPLETEDELETE` property and the `VerifyRemoveDlg` checkbox override.
2. Record the resolved data root to `HKLM\SOFTWARE\Elpis\EdgeConnect\DataRoot` — at install
   time from the MSI, and at first start from the runtime if it resolves a different root.
3. Add the conditioned cleanup component with the `RemoveFolderEx` / `RemoveFile` rules.
4. Add the three-part condition, including the upgrade guard.
5. Update `docs/installer/creating-the-installer.md` with the new build requirement (none
   expected — `WixToolset.Util.wixext` is already referenced) and the silent-uninstall line.
6. Run the §9 test plan on a clean VM. Tests 4 and 5 are mandatory before shipping.
7. Consider an ADR: uninstall-time data retention is a customer-visible contract, and the
   decision to keep identity anchors while deleting the data root is the kind of choice that
   is painful to reverse once shipped.
