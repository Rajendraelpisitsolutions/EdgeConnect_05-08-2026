# Uninstall — "Complete delete" option

- **Status:** **IMPLEMENTED** in v2.1.0.1 (2026-08-12). Built and statically verified;
  the install/uninstall test plan in §9 has **not** been run yet — see §12.
- **Date:** 2026-08-08 (design) / 2026-08-12 (implementation)
- **Scope:** `installer/ElpisEdgeConnect.wxs`, `installer/ElpisEdgeConnect.Bundle.wxs`,
  `installer/theme/HyperlinkTheme.{xml,wxl}`.
- **Requirement:** Add exactly **one** checkbox to uninstall. When ticked, remove the
  program files and the gateway data, **except the license file**.

> **Two things in this document were wrong and are corrected below.** §6 put the
> checkbox on the MSI's `VerifyRemoveDlg`, which can never be seen by anyone who
> installed via `setup.exe`; §7 gated the cleanup in a way that would silently
> delete nothing. Both are marked **SUPERSEDED** in place, with the shipped
> behaviour in §12. The rest of the design was implemented as written.

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

## 6. User interface — ⚠️ SUPERSEDED, see §12.1

> **Why this section is wrong.** It assumes the operator sees the MSI's own
> maintenance UI. They do not. The bundle chains the MSI with
> `ARPSYSTEMCOMPONENT=1`, so Add/Remove Programs lists only the **bundle**, and
> Burn always runs its chained packages with the MSI UI suppressed. A checkbox on
> `VerifyRemoveDlg` would therefore be invisible to every operator who installed
> from `ElpisEdgeConnect-Setup.exe` — i.e. all of them. The checkbox shipped in the
> **bundle theme** instead. The wording rules below were kept verbatim.

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

### Sequencing and guards — ⚠️ the component condition is SUPERSEDED, see §12.2

> **Why conditioning the *component* is wrong.** An MSI component whose condition
> is false at **install** time is never installed — so at uninstall time there is
> nothing to remove and its `RemoveFile` rows never execute. `COMPLETEDELETE` is an
> **uninstall-time** choice, so it is always false during install, and the cleanup
> would therefore never run no matter what the operator ticked. The shipped build
> keeps the component unconditional and gates the **path properties** instead.
>
> The `NOT UPGRADINGPRODUCTCODE` guard below is correct and was implemented.

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

## 12. As shipped (v2.1.0.1)

### 12.1 Where the checkbox actually lives

`installer/theme/HyperlinkTheme.xml`, on the **Modify** page of the setup.exe wizard:

```
☐  Also delete all EdgeConnect data when uninstalling
   Removes your configuration, route buffers and logs.
   Your license file is kept.
```

The theme `Checkbox/@Name` **is** the Burn variable name, so ticking it sets
`CompleteDelete=1`; `ElpisEdgeConnect.Bundle.wxs` forwards it to the MSI as
`<MsiProperty Name="COMPLETEDELETE" Value="[CompleteDelete]" />`. The MSI owns the
removal rules; the bundle only carries the operator's answer.

Modify is used because **WixStdBA's stock themes have no uninstall-confirmation
page** — Modify is the only page shown before the Uninstall button.

**⚠️ Known gap.** Add/Remove Programs → **Uninstall** invokes the bundle's
`UninstallString` (`… /uninstall`), which drives WixStdBA straight to Progress and
therefore **skips the Modify page and the checkbox** — that path always keeps the
data (the safe default). To reach the checkbox, use one of:

| Path | How |
|---|---|
| Add/Remove Programs → **Modify / Change** | tick the box, then **Uninstall** |
| Run `ElpisEdgeConnect-Setup-<version>.exe` on an installed machine | same Modify page |
| Unattended | `ElpisEdgeConnect-Setup-<version>.exe /uninstall CompleteDelete=1 /quiet` |
| MSI directly | `msiexec /x ElpisEdgeConnect-<version>.msi COMPLETEDELETE=1 /qn` |

Closing that gap needs the checkbox on the MSI's `VerifyRemoveDlg` **plus**
`MsiPackage/@DisplayInternalUICondition` so Burn surfaces the MSI UI on uninstall.
That means vendoring the WixUI dialog set, which is a larger change than this one
and was not taken.

### 12.2 How the gate actually works

The cleanup component is **always installed**. What is gated is the *path
properties* it removes, because `RemoveFolderEx` and `RemoveFile` both no-op on an
empty path:

```xml
<SetProperty Id="ECDATA_CONFIG" Value="[ECDATAROOTREG]\config"
             After="AppSearch" Sequence="both"
             Condition="COMPLETEDELETE = 1 AND NOT UPGRADINGPRODUCTCODE" />
```

**The paths come from a registry round-trip, not from `[INSTALLFOLDER]`.** WiX
schedules `Wix4RemoveFoldersEx_X64` at sequence **799 — before `CostInitialize`
(800) and `CostFinalize` (1000)** — because it must populate the `RemoveFile` table
early enough for the installer to cost it. No Directory property is resolved that
early, so anything computed from `[INSTALLFOLDER]` is empty when the action reads
it and the delete silently does nothing. Values recovered by `AppSearch`
(sequence 50) *are* available in time. So the MSI writes its locations at install:

| `HKLM\Software\Elpis IT Solutions\EdgeConnect` | Value |
|---|---|
| `DataRoot` | `[CommonAppDataFolder]EdgeConnect` |
| `InstallDir` | `[INSTALLFOLDER]` |

and reads them back at uninstall via `RegistrySearch`. Useful side effect: if the
runtime ever rewrites `DataRoot` after `EDGECONNECT_DATA_ROOT` relocates it, the
uninstall follows it with no installer change.

Verified statically against the built MSI: `SetEC*` at 51–55, all before 799;
`Wix4RemoveFoldersEx_X64` before `RemoveFiles` (3500); all five gated on both
`COMPLETEDELETE` and `NOT UPGRADINGPRODUCTCODE`; no removal rule anywhere names
the license.

### 12.3 One addition beyond the original design

`ECINSTALLTREE` recursively removes `%ProgramFiles%\Elpis EdgeConnect\` as well.
The MSI already deletes every file it installed; this additionally sweeps anything
left beside them that the MSI does not own, so "delete everything" means it. Also
gated on the checkbox.

### 12.4 Still outstanding

The §9 test plan has **not** been executed — every check so far is static analysis
of the built MSI, which cannot prove runtime deletion behaviour. **Tests 4 and 5
(reinstall-after-complete-delete, and major upgrade preserving data) are mandatory
on a clean VM before this ships to a customer.** Test 5 in particular guards the
one failure mode that would destroy customer configuration.

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
