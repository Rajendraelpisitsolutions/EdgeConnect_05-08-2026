# Elpis EdgeConnect — Creating the Windows Installer (MSI)

**Purpose:** step-by-step runbook to build the EdgeConnect MSI from source, using
WiX. Reflects the exact process and pitfalls encountered building v1.1.0.0.
**Output** (both names carry the version, so artifacts from different releases
cannot be confused in a downloads folder or on a customer's desktop):
- `publish/ElpisEdgeConnect-<version>.msi` — a per-machine x64 installer that deploys
  the app, registers the auto-start Windows service, and adds a Start Menu shortcut
  to the Studio UI.
- `publish/ElpisEdgeConnect-Setup-<version>.exe` — a double-clickable bootstrapper
  `.exe` that wraps the MSI in an install wizard (§5.2). **This is the artifact to
  hand to customers** — it is the only one whose uninstall offers the
  "also delete all data" checkbox (§10).

e.g. `ElpisEdgeConnect-2.1.0.1.msi` / `ElpisEdgeConnect-Setup-2.1.0.1.exe`.

**Manifests:** `installer/ElpisEdgeConnect.wxs` (MSI),
`installer/ElpisEdgeConnect.Bundle.wxs` (EXE bundle).

> **`.wxs` vs `.msi`/`.exe`:** the `.wxs` files are the WiX **source** (XML recipe);
> they are never shipped. `wix build` compiles them into the actual installer
> (`.msi`, and the `.exe` bundle) — the same way `.cs` compiles to `.exe`.

---

## 0. Topology you must understand first

EdgeConnect ships two executables, but **only one runs as the service**:

| Executable | What it is | In the installer |
|------------|------------|------------------|
| `ElpisEdgeConnect.Management.exe` | **Full runtime pipeline + Studio UI** (superset). Serves the UI on `http://127.0.0.1:5080`. | Registered as the auto-start service **`ElpisEdgeConnect`**. |
| `ElpisEdgeConnect.Host.exe` | Headless runtime pipeline only (no UI). | Installed as **plain files** for optional manual/headless use — **not** a service. |

> ⚠️ **Do not run both against the same data root.** Management already runs the
> same pipeline as Host (via `EdgeConnectComposition.ConfigureRuntimeAsync`, which
> registers `HostStartup` + the source/sink supervisors as hosted services). Two
> processes would contend on the same SQLite buffers under
> `C:\ProgramData\EdgeConnect`. The service points at Management; leave `Host.exe`
> alone while the service runs.

---

## 1. Prerequisites

| Requirement | Notes |
|-------------|-------|
| Windows x64 | Build + install target. |
| .NET 8 SDK | `dotnet --version` ≥ 8.0. |
| WiX Toolset **v5** CLI | Installed as a dotnet global tool (§3). **Use v5, not v6/v7** — see the OSMF note below. |
| `WixToolset.Util.wixext` **v5** | WiX extension for the Start Menu URL shortcut. Version must match the WiX CLI major version. |
| `WixToolset.BootstrapperApplications.wixext` **v5** | *(Only for the `.exe` bundle, §5.2.)* Provides the standard bootstrapper UI. |
| Admin rights | Only needed to *install/test* the installer, not to *build* it. |

> 🛑 **OSMF gotcha (important).** The newest WiX — **v6 and v7** — requires accepting
> FireGiant's **Open Source Maintenance Fee (OSMF) EULA** to run, and `dotnet tool
> install --global wix` (no version) pulls the newest by default. Building with it
> fails with `WIX7015: You must accept the Open Source Maintenance Fee (OSMF) EULA`.
> **WiX v5 remains free** with no such requirement — pin to it (§3).

---

## 2. Step 1 — Publish both apps (Release, self-contained)

Self-contained `win-x64` bundles the .NET 8 runtime, so target machines need **no**
.NET install. Run from the repo root:

```bash
# Studio (the service): full runtime + UI
dotnet publish src/ElpisEdgeConnect.Management/ElpisEdgeConnect.Management.csproj \
    -c Release -r win-x64 --self-contained true -o publish/Management

# NOTE: the Host (headless) project is NOT published separately any more.
# The Management publish already emits a byte-identical ElpisEdgeConnect.Host.exe
# plus its deps.json/runtimeconfig.json, so a second self-contained copy was 93 MB
# of duplicate files that nothing executed. See host-vs-management-folder-analysis.md.
```

Tip: delete the target folder first (`rm -rf publish/Management`) so stale files
from a previous publish aren't harvested into the MSI.

### 2.1 One-time code prerequisite (already applied)

For the Studio to run as a Windows service it must integrate with the Service
Control Manager, or the service start times out (error 1053). Two changes make it
service-capable (already committed to the Management project):

- `src/ElpisEdgeConnect.Management/Program.cs` — `builder.Host.UseWindowsService();`
  right after `WebApplication.CreateBuilder(args)`. It is a **no-op** when launched
  from a console, so `dotnet run` and manual EXE launch still work.
- `ElpisEdgeConnect.Management.csproj` — `PackageReference` to
  `Microsoft.Extensions.Hosting.WindowsServices`.

---

## 3. Step 2 — Install the WiX v5 toolchain

```bash
# WiX v5 CLI (free; avoids the v6/v7 OSMF fee)
dotnet tool install --global wix --version "5.*"
wix --version        # -> 5.0.2+...

# Util extension (Start Menu URL shortcut) — must match the CLI major version
wix extension add -g WixToolset.Util.wixext/5.0.2
wix extension list -g   # -> WixToolset.Util.wixext 5.0.2
```

If a wrong-version extension was added earlier, remove it first:
`wix extension remove -g WixToolset.Util.wixext`.

> The `wix` tool installs under `%USERPROFILE%\.dotnet\tools`. If `wix` isn't on
> PATH in your shell, add that folder (e.g. `export PATH="$PATH:$HOME/.dotnet/tools"`).

---

## 4. Step 3 — The installer manifest (`installer/ElpisEdgeConnect.wxs`)

The manifest is already authored. Key pieces and *why*:

| Element | Purpose |
|---------|---------|
| `<Package ... Version="1.1.0.0" UpgradeCode="{fixed GUID}" Scope="perMachine">` | Per-machine x64 install. **UpgradeCode must stay constant** across versions for upgrades to work. |
| `<MajorUpgrade .../>` | Uninstalls the old version and installs the new one automatically. |
| `<MediaTemplate EmbedCab="yes" />` | Embeds the payload CAB inside the single .msi file. |
| `<Component Id="StudioServiceExe">` with `<File ... KeyPath="yes">` + `<ServiceInstall>` + `<ServiceControl>` | Registers `ElpisEdgeConnect.Management.exe` as the auto-start service **`ElpisEdgeConnect`** (LocalSystem; start on install, stop+remove on uninstall). The exe must be the component KeyPath — the service binary path is taken from it. |
| `<Files Include="!(bindpath.MgmtSrc)\**"><Exclude .../></Files>` | Harvests every *other* Studio file; excludes the exe already placed in the service component (a file can live in only one component). **`Exclude` is a child element in WiX v5, not an attribute.** |
| `<util:InternetShortcut ... Target="http://127.0.0.1:5080/">` in `ProgramMenuFolder` | Start Menu shortcut that opens the Studio UI. Needs `xmlns:util` + the Util extension. The component uses an HKLM `RegistryValue` as its KeyPath and a `RemoveFolder` for clean uninstall. |
| *(no `<Feature>`)* | WiX auto-creates a default feature containing all components. |

Two authoring pitfalls we hit (already fixed in the manifest):

1. **`Files/@Exclude` is invalid** → use a child `<Exclude Files="..."/>`.
2. **`Files/@Include` resolves relative to the install directory tree**, not the repo
   root → pass source folders as **bindpaths** and reference them as
   `!(bindpath.NAME)\**` (see the build command).

---

## 5. Step 4 — Build the MSI

From the repo root:

```bash
ROOT="$(pwd)"
VERSION="$(sed -n 's:.*<Version>\\(.*\\)</Version>.*:\\1:p' Directory.Build.props | head -1)"
wix build installer/ElpisEdgeConnect.wxs \
    -arch x64 \
    -ext WixToolset.Util.wixext \
    -ext WixToolset.UI.wixext \
    -d "Version=$VERSION" \
    -bindpath "MgmtSrc=$ROOT/publish/Management" \
    -bindpath "FocasSrc=$ROOT/fwlib0iD64" \
    -o publish/ElpisEdgeConnect.msi
```

- `-ext WixToolset.Util.wixext` — required for the `util:InternetShortcut`.
- `-ext WixToolset.UI.wixext` — required for `<ui:WixUI Id="WixUI_FeatureTree" />`.
  Without it the build fails with `WIX0200: unhandled extension element 'WixUI'`.
- `-d "Version=..."` — the manifest declares `Version="$(Version)"`; without this the
  build fails with `WIX0150: Undefined preprocessor variable`. Read it from
  `Directory.Build.props` rather than typing it, so the MSI version and the stamped
  assembly versions cannot drift apart.
- **No `HostSrc` bindpath.** `HostDir` was removed from the MSI — the headless
  `ElpisEdgeConnect.Host.exe` ships inside the Management folder and is byte-identical to
  the one the separate publish produced. Dropping it saved 93 MB installed / 17.6 MB
  download. Evidence: `host-vs-management-folder-analysis.md`.
- `-bindpath NAME=path` — maps the `!(bindpath.NAME)` references in the manifest to
  the publish folders. **This is what makes file harvesting resolve correctly.**
- `-bindpath "FocasSrc=..."` — the FANUC FOCAS2 native libraries, laid down beside
  the executable. `Focas2Interop`'s DllImportResolver loads `Fwlib64.dll` from the
  application directory, and `Fwlib64.dll` in turn loads the per-CNC-model libraries
  (`fwlib0iD64.dll`, `fwlib30i64.dll`, …) at runtime — so all six ship together.
  Omit this bindpath and the build FAILS on the unresolved `!(bindpath.FocasSrc)`
  reference, rather than quietly producing an MSI that cannot talk to a FANUC CNC.

  > **The Management harvest must exclude `fwlib*.dll`.**
  > `ElpisEdgeConnect.Sources.Focas2.csproj` copies these six DLLs into the output of
  > *every* referencing project (`Content` + `Link`, so they land flat beside the exe
  > — added so `dotnet run` / F5 also find them). That means `publish/Management`
  > now contains them too, and harvesting the same six files from both `MgmtSrc` and
  > `FocasSrc` is a hard failure: **`WIX0091` duplicate `File`/`Component`
  > identifiers**, because the generated ids derive from the destination path.
  > The manifest therefore carries `<Exclude Files="!(bindpath.MgmtSrc)\fwlib*.dll" />`.
  > Excluding here — rather than deleting the `FocasSrc` harvest — is deliberate: it
  > keeps `FocasSrc` **required**, preserving the loud-failure guarantee above. (The
  > csproj block is itself guarded on the folder existing, so without that guard a
  > clone lacking the licensed DLLs would silently build a FANUC-blind MSI.)

Success produces `publish/ElpisEdgeConnect.msi` (~53 MB, CAB-compressed;
~674 files across both app trees).

### 5.1 Bumping the version for upgrades

If a previous version is installed, **increment `Version`** in the manifest (e.g.
`1.1.0.0` → `1.2.0.0`) before rebuilding, so `MajorUpgrade` recognises the new MSI
as an upgrade. Keep the **UpgradeCode unchanged**. (When you also ship the `.exe`
bundle, bump the `Version` in `ElpisEdgeConnect.Bundle.wxs` to match.)

### 5.2 (Optional) Wrap the MSI in a double-clickable `setup.exe`

Some customers prefer a friendly `setup.exe` over an `.msi`. WiX's **Burn**
bootstrapper wraps the MSI in an `.exe` with an install wizard (welcome → Install →
progress → finish), auto-elevation via UAC, and standard switches. It reuses the
**already-built MSI** — build the MSI first (§5), then:

```bash
# one-time: the bootstrapper extension (v5 to match the CLI)
wix extension add -g WixToolset.BootstrapperApplications.wixext/5.0.2

# build the setup.exe from installer/ElpisEdgeConnect.Bundle.wxs
wix build installer/ElpisEdgeConnect.Bundle.wxs -arch x64 \
    -ext WixToolset.BootstrapperApplications.wixext \
    -o publish/ElpisEdgeConnect-Setup.exe
```

The bundle manifest (`installer/ElpisEdgeConnect.Bundle.wxs`) is minimal — a
`<Bundle>` with its **own** `UpgradeCode` (distinct from the MSI's), a standard
bootstrapper UI, and a `<Chain>` containing one `<MsiPackage SourceFile="publish/ElpisEdgeConnect.msi">`.
Because the apps are self-contained, no prerequisite packages (.NET runtime, VC++)
need chaining.

Result: `publish/ElpisEdgeConnect-Setup.exe` (~54 MB) — the same install as the MSI,
just double-clickable. Switches:

```
ElpisEdgeConnect-Setup.exe             # interactive wizard (default)
ElpisEdgeConnect-Setup.exe /passive    # progress bar only
ElpisEdgeConnect-Setup.exe /quiet      # fully silent
ElpisEdgeConnect-Setup.exe /uninstall  # remove
ElpisEdgeConnect-Setup.exe /repair
ElpisEdgeConnect-Setup.exe /log <file> # verbose log
```

**EXE vs MSI — which to hand out:**
- **EXE** — friendliest for a human double-clicking; can bootstrap prerequisites if
  ever needed. Typical customer-facing download.
- **MSI** — better for IT/enterprise rollout (Group Policy, SCCM, `msiexec /qn`).
- Both install the *identical* payload; the EXE simply runs the MSI inside.

---

## 6. Step 5 — Install, verify, uninstall

Installing/updating requires an **elevated** (admin) prompt.

```powershell
# Interactive install
msiexec /i publish\ElpisEdgeConnect.msi

# Silent install with a verbose log (for rollout / CI)
msiexec /i publish\ElpisEdgeConnect.msi /qn /norestart /l*v publish\install.log

# Uninstall
msiexec /x publish\ElpisEdgeConnect.msi /qn
```
Exit code `0` = success, `3010` = success (reboot advised), `1603` = fatal,
`1730/1925` = needs elevation.

### 6.1 Verify

```powershell
# Service is running (auto-start), pointing at Management.exe
Get-CimInstance Win32_Service -Filter "Name='ElpisEdgeConnect'" |
  Select-Object State, StartMode, PathName

# Studio UI responds
(Invoke-WebRequest http://127.0.0.1:5080/ -UseBasicParsing).StatusCode   # 200

# Start Menu shortcut
Test-Path "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Elpis EdgeConnect\Elpis EdgeConnect Studio.url"
```

Expected: service **Running / Auto**, bin path
`C:\Program Files\Elpis EdgeConnect\Management\ElpisEdgeConnect.Management.exe`,
UI returns HTTP 200 (title *"Connectivity Studio — Elpis EdgeConnect"*), shortcut
present.

### 6.2 What the install does on the machine

- Files → `C:\Program Files\Elpis EdgeConnect\{Management,Host}\`
- Service `ElpisEdgeConnect` (LocalSystem, auto-start) → starts immediately and on boot
- Start Menu → *Elpis EdgeConnect → Elpis EdgeConnect Studio* → opens `http://127.0.0.1:5080`
- Add/Remove Programs entry: *Elpis EdgeConnect*, publisher *Elpis IT Solutions*
- Runtime data root (created by the service on first run):
  `C:\ProgramData\EdgeConnect\` (config, buffers, identity, license)

> **Uninstall preserves data.** `C:\ProgramData\EdgeConnect` (including any
> `license.json`) is **not** owned by the MSI and is left in place.

---

## 7. Post-install: launching & licensing

- **Nothing to launch** — the service auto-starts with Windows and serves the UI.
  Open the Studio via the Start Menu shortcut or `http://127.0.0.1:5080`.
- **Service control:** `Start-Service` / `Stop-Service` / `Restart-Service
  ElpisEdgeConnect` (restart after dropping in a new `license.json`).
- **Licensing:** place the signed `license.json` at
  `C:\ProgramData\EdgeConnect\license.json` (or set `EDGECONNECT_LICENSE_PATH`) and
  restart the service. See `docs/licensing/licensing-complete-guide.md`.

---

## 8. One-shot build script (optional)

PowerShell (the primary dev shell on Windows):

```powershell
$ErrorActionPreference = 'Stop'
$ROOT = (Get-Location).Path
$VERSION = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version |
           Where-Object { $_ }

if (Test-Path publish\Management) { Remove-Item -Recurse -Force publish\Management }
dotnet publish src\ElpisEdgeConnect.Management\ElpisEdgeConnect.Management.csproj `
    -c Release -r win-x64 --self-contained true -o publish\Management

wix build installer\ElpisEdgeConnect.wxs -arch x64 `
    -ext WixToolset.Util.wixext -ext WixToolset.UI.wixext `
    -d "Version=$VERSION" `
    -bindpath "MgmtSrc=$ROOT\publish\Management" `
    -bindpath "FocasSrc=$ROOT\fwlib0iD64" `
    -o "publish\ElpisEdgeConnect-$VERSION.msi"

# Wrap the MSI in a double-clickable setup.exe
wix build installer\ElpisEdgeConnect.Bundle.wxs -arch x64 `
    -ext WixToolset.BootstrapperApplications.wixext `
    -d "Version=$VERSION" `
    -d "MsiPath=$ROOT\publish\ElpisEdgeConnect-$VERSION.msi" `
    -o "publish\ElpisEdgeConnect-Setup-$VERSION.exe"

Write-Output "Built: publish\ElpisEdgeConnect-$VERSION.msi and publish\ElpisEdgeConnect-Setup-$VERSION.exe"
```

Both `wix build` calls need `-d "Version=..."` (each manifest declares `Version="$(Version)"`),
and the MSI additionally needs `-ext WixToolset.UI.wixext` for `<ui:WixUI>`. Reading
`$VERSION` from `Directory.Build.props` is what keeps the MSI version, the bundle
version, and the stamped assembly versions from drifting apart.

---

## 9. Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `WIX7015 ... OSMF EULA` | You're on WiX v6/v7. Reinstall v5: `dotnet tool install --global wix --version "5.*"`. |
| `WIX0144: extension ... could not be found` | Util extension missing or wrong version. `wix extension add -g WixToolset.Util.wixext/5.0.2`. |
| `WIX6101: Could not find package root folder wixext5` | Util extension major version ≠ WiX CLI major version. Match both to 5.x. |
| `WIX0004: Files element ... unexpected attribute 'Exclude'` | Use a child `<Exclude Files="..."/>`, not an attribute. |
| `WIX0091: Duplicate File/Component with identifier 'fls…'` (6 pairs, pointing at the two `<Files>` elements) | The same file is harvested from two bindpaths. Caused by the FOCAS2 natives now being copied into `publish/Management` by the Focas2 csproj — keep `<Exclude Files="!(bindpath.MgmtSrc)\fwlib*.dll" />` on the Management harvest (§5). Generally: any file that lands in both `MgmtSrc` and another bindpath must be excluded from one of them. |
| `WIX8601: Missing directory for harvesting files` | `Files/@Include` didn't resolve. Use `!(bindpath.NAME)\**` + pass `-bindpath NAME=...`. |
| Service installs but won't start (1053 timeout) | The service exe lacks `UseWindowsService()`. Ensure §2.1 changes are in and republish. |
| `msiexec` returns 1730/1925 | Not elevated. Run from an admin prompt. |
| Upgrade install doesn't replace old version | `Version` not incremented, or `UpgradeCode` changed. Bump Version, keep UpgradeCode. |

---

---

## 10. Uninstall — the "also delete all data" checkbox

By default uninstall removes only what the MSI installed; everything under
`%ProgramData%\EdgeConnect` (configuration, route buffers, logs, **and the
license**) survives. The setup.exe wizard's **Modify** page carries one opt-in:

```
☐  Also delete all EdgeConnect data when uninstalling
   Removes your configuration, route buffers and logs.
   Your license file is kept.
```

Ticked, uninstall additionally removes `config\`, `buffer\`, `logs\`, the
`identity` / `.clock-anchor-v2` / `.edgeconnect-version` markers, and sweeps the
`%ProgramFiles%\Elpis EdgeConnect\` tree. **`edgelicense.json` is preserved** — it
is never named in any removal rule, so there is no filter that could widen to
include it.

**⚠️ Add/Remove Programs → *Uninstall* skips the checkbox** and keeps the data (the
safe default): that path runs the bundle's `/uninstall` directly, and WixStdBA's
theme has no uninstall-confirmation page. To use the option, pick **Modify /
Change** in Add/Remove Programs, or re-run the setup.exe, then tick and Uninstall.

Unattended equivalents:

```powershell
ElpisEdgeConnect-Setup-2.1.0.1.exe /uninstall CompleteDelete=1 /quiet
msiexec /x publish\ElpisEdgeConnect-2.1.0.1.msi COMPLETEDELETE=1 /qn
```

Omitting the property gives the default (data kept). A **major upgrade never
deletes data** regardless of the property — the rules are guarded on
`NOT UPGRADINGPRODUCTCODE`.

Design, mechanism and the remaining test plan:
`docs/installer/uninstall-complete-delete-design.md`.

> **Not yet runtime-tested.** The behaviour above is verified by static analysis of
> the built MSI only. An install/uninstall cycle on a clean VM — especially
> "major upgrade preserves data" — is still required before shipping.

---

*Last verified against a clean build of `ElpisEdgeConnect-2.1.0.1.msi` (WiX 5.0.2,
.NET SDK 8.0.423) — 390 files, 48.6 MB MSI / 49.1 MB setup.exe. Update this doc if the
manifest, publish layout, or tool versions change.*
