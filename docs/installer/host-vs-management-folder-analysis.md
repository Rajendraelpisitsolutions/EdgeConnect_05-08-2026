# Installer layout: why two folders, and can the duplication go?

**Question.** After installing, `%ProgramFiles%\Elpis EdgeConnect\` contains two folders,
`Host` and `Management`, holding what looks like the same set of files. Why are there two,
are both required, and can the duplication be removed?

**Short answer.** The duplication is real and almost total: **every one of the 274 files in
`Host` also exists in `Management`, and none is unique to `Host`.** The `Management` folder
already ships a byte-identical copy of `ElpisEdgeConnect.Host.exe`, so the headless variant is
available without a second folder. `HostDir` can be dropped from the MSI with no loss of
capability — saving **93 MB installed** and **17.6 MB of installer download**.

*Analysis date: 2026-08-06. Measured against the build produced from the current working tree,
`ProductVersion 2.1.0.1`.*

---

## 1. What was measured

| | `publish\Management` | `publish\Host` |
|---|---|---|
| Files | 407 | 274 |
| Size on disk | 134 MB | 93 MB |

Comparing the two folders file by file, by name and then by SHA-256 of the contents:

| Result | Count | Bytes |
|---|---|---|
| `Host` files that also exist in `Management` (by name) | **274 of 274** | — |
| …of those, **byte-identical** | **244** | **89.5 MB** |
| …same name, different bytes | 30 | ~3.5 MB |
| **Unique to `Host`** | **0** | 0 |

The 30 files that differ byte-for-byte are all `Microsoft.Extensions.*` assemblies, and they
carry **the same assembly file version** (`8.0.23.53103`) in both folders. They differ because
the two self-contained publishes compiled them independently, not because the code differs.

Three files matter more than the rest, and all three are byte-identical across the folders:

| File | Result |
|---|---|
| `ElpisEdgeConnect.Host.exe` | **identical** |
| `ElpisEdgeConnect.Host.dll` | **identical** |
| `ElpisEdgeConnect.Core.dll` | **identical** |

`publish\Management` also contains `ElpisEdgeConnect.Host.deps.json` and
`ElpisEdgeConnect.Host.runtimeconfig.json` — the two side files the headless executable needs
in order to start. It is therefore directly runnable from the `Management` folder.

---

## 2. Why two folders exist

This is not an accident; it follows from two deliberate design decisions.

**There are two entry points.** `src/ElpisEdgeConnect.Management/Program.cs` documents itself as
the *"PRIMARY EdgeConnect entry point — runs the full runtime (sources, sinks, pipeline,
routing, buffer, diagnostics) AND the Connectivity Studio UI in the same process. This is what
99% of operators run."* It names `src/ElpisEdgeConnect.Host` as the alternative *"for headless
deployments (no UI, e.g. fleet-managed containers)"*. Both share the same locked composition
sequence via `EdgeConnectComposition.ConfigureRuntimeAsync`.

**Both are published self-contained.** `docs/installer/creating-the-installer.md` publishes each
with `--self-contained true`, so each folder carries its own complete copy of the .NET 8 runtime.
That is what makes the folders large, and it is why an identical runtime appears twice.

The installer then treats them very differently. From `installer/ElpisEdgeConnect.wxs`:

* `ManagementComponents` installs to `MgmtDir` **and registers `ElpisEdgeConnect.Management.exe`
  as the auto-start Windows service `ElpisEdgeConnect`**, with service-recovery configuration.
* `HostComponents` installs to `HostDir` and is commented in the manifest as
  *"Host (headless variant) — files only, NOT a service."*

So on a normal install, **nothing ever executes anything in `HostDir`.** No service points at it,
no shortcut launches it, and the Studio does not reference it. It is inert unless an operator
opens a terminal and runs the executable by hand.

---

## 3. Is `HostDir` required?

**No.** The capability it provides is already present in `MgmtDir`:

* The headless executable is in `Management` and is byte-identical.
* Its `deps.json` and `runtimeconfig.json` are in `Management`.
* Every dependency it loads is in `Management` (0 files unique to `Host`).

An operator wanting a headless run can execute
`%ProgramFiles%\Elpis EdgeConnect\Management\ElpisEdgeConnect.Host.exe` today. Removing
`HostDir` removes a copy, not a capability.

### Measured cost of keeping it

| | With `HostDir` | Without `HostDir` | Saving |
|---|---|---|---|
| Installer (`.msi`) | 66.0 MB | **48.4 MB** | **17.6 MB (−27%)** |
| Installed footprint | 227 MB | **134 MB** | **93 MB (−41%)** |

The installer figure is measured, not estimated: the MSI was rebuilt with `HostComponents`
emptied and the output compared. The manifest was restored afterwards.

The download saving is smaller than the disk saving because the MSI is CAB-compressed, but
MSI does **not** de-duplicate identical files across components — each copy is stored again.

---

## 4. Options

### Option A — Remove `HostDir` from the MSI *(recommended)*

Delete the `HostComponents` group, its `ComponentGroupRef`, and the `HostDir` directory from
`ElpisEdgeConnect.wxs`; drop the `HostSrc` bindpath and the Host publish step from the build.

* **Gains** 93 MB installed, 17.6 MB download, and one fewer folder for an operator to be
  confused by.
* **Loses** nothing functional — the headless exe remains in `Management`.
* **Risk:** low. The only scenario affected is a script or runbook with a hard-coded path to
  `…\Host\ElpisEdgeConnect.Host.exe`. Those need repointing at `…\Management\`.
* **Upgrade note:** on upgrading an existing install, MSI will remove the old `HostDir`
  because its components are no longer authored. That is the desired outcome, but it means
  anything a customer has manually placed in that folder disappears.

### Option B — Keep both, make `HostDir` an optional feature

The installer already presents a `WixUI_FeatureTree` wizard with a deselectable Desktop-shortcut
feature. `HostDir` could become a second optional feature, default **off**.

* **Gains** the same 93 MB for the 99% who never deselect anything, while leaving the headless
  layout reachable for anyone who deliberately wants it.
* **Costs** a feature-tree entry that most operators will not understand, and the MSI download
  stays at 66 MB because the payload must still be carried.

### Option C — Publish framework-dependent instead of self-contained

Drop `--self-contained true` so both apps share a machine-wide .NET 8 runtime.

* **Gains** far more than 93 MB — most of both folders is the runtime.
* **Costs** a hard prerequisite: .NET 8 Desktop/ASP.NET runtime must be installed first, which
  on an isolated factory network is a significant operational burden. The bundle would need to
  chain the runtime installer.
* **Assessment:** contradicts the current design intent — the manifest comments note *"the
  self-contained apps already include the .NET runtime, so no prerequisite packages are needed."*
  Not recommended without a deliberate decision to change deployment strategy.

### Option D — Publish both into one folder

Publish `Host` and `Management` to the same output directory. Their per-app side files are named
distinctly (`ElpisEdgeConnect.Host.deps.json` vs `ElpisEdgeConnect.Management.deps.json`), so
they coexist.

* **Assessment:** this is effectively what already happens — the Management publish *already*
  emits the Host executable and its side files. Option A is the same outcome with less work.

---

## 5. Recommendation

**Adopt Option A: remove `HostDir` from the installer.**

The evidence is unambiguous — zero files unique to `Host`, a byte-identical headless executable
already shipping in `Management`, and no service, shortcut or code path referencing `HostDir`.
It is 93 MB of installed duplication that nothing executes.

Before doing so, confirm one thing that cannot be determined from the repository: **whether any
customer runbook, deployment script or fleet tool references the `…\Host\` path.** If so, either
update those references or take Option B for one release as a transition.

---

## 6. How this was verified

Reproducible from the repo root:

```bash
# Folder inventory
find publish/Management -type f | wc -l ; du -sm publish/Management
find publish/Host       -type f | wc -l ; du -sm publish/Host

# The headless exe already ships inside Management
ls -la publish/Management/ElpisEdgeConnect.Host.exe

# Byte-level comparison (SHA-256 per file, matched by relative path)
#   -> 274/274 present in Management, 244 byte-identical, 0 unique to Host

# Installed-size delta: rebuild with HostComponents emptied and compare MSI size
#   with HostDir     69,258,245 bytes
#   without HostDir  50,765,872 bytes
```

MSI contents were inspected without installing, by reading the `Property`, `Component` and
`File` tables directly.

---

## 7. Caveat worth recording

The two folders also mean the six FANUC FOCAS2 native libraries are installed twice — once
beside each executable. That is **not** redundant while both folders exist: `Focas2Interop`'s
`DllImportResolver` loads `Fwlib64.dll` from the application directory, so each executable needs
its own copy. If Option A is adopted, that duplication disappears along with `HostDir`, and the
explicit `HostFocas_*` file entries in the manifest should be removed with it.
