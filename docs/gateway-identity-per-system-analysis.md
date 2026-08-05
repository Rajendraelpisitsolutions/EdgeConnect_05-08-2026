# Analysis — Per-System Gateway Identity ("one machine, one gateway id")

**Status:** IMPLEMENTED (2026-07-30) — Option C shipped via `GatewayIdentityStore`,
wired into both resolvers (`FileSystemGatewayIdentity.InitializeAsync` +
`TryReadPersisted`). Ratified by **ADR-0038**. This document is retained as the
design rationale.
**Author:** engineering (with Claude).
**Scope:** how the gateway UUID is generated and persisted, why the same physical
machine can end up with more than one gateway id today, and options to make it
**one stable id per system**.
**Touches:** ARCHITECTURE_BLUEPRINT Locked Decision **#19** (per-gateway UUID +
customer/site binding) and **ADR-0036** (single-machine license binding). A change
here is architecturally significant and should be ratified with a new ADR before
implementation.

---

## 1. Executive summary

The gateway id is a **random GUID generated once and saved to a file** at
`<dataRoot>/identity`. It is **scoped to the data root**, not to the machine. Because
the data root can differ between how EdgeConnect is launched (installed service vs a
manual/dev run with `EDGECONNECT_DATA_ROOT` set), the **same machine can produce
several different gateway ids** — one per data root. Since licenses are bound to the
gateway id (ADR-0036), a "new" id means an activated license stops validating
(`LICENSE_GATEWAY_MISMATCH`).

The requirement is **one system → one gateway id**, independent of the data root.

**Recommendation:** move the identity to a **single machine-wide, data-root-independent
location** and, on first run, **promote any existing legacy `<dataRoot>/identity`** so
machines that already have an activated license keep the *same* id. Optionally seed a
brand-new machine's id deterministically from the OS machine identifier so it survives
file deletion. Details and trade-offs below.

---

## 2. Current behaviour (as-built)

| Concern | Where | Behaviour |
|---|---|---|
| Identity generation / load | `src/ElpisEdgeConnect.Host/FileSystemGatewayIdentity.cs:88-115` | On start: if `<path>` exists & non-empty → load it; else generate `Guid.NewGuid()` and write it. |
| Identity path (runtime) | `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs:126-129` | `EDGECONNECT_IDENTITY_PATH` → else `<dataRoot>/identity`. |
| Data root | `EdgeConnectComposition.cs:88-91` | `EDGECONNECT_DATA_ROOT` → else `%ProgramData%\EdgeConnect` (Windows) / `/var/lib/edgeconnect` (Linux). |
| Identity path (license layer) | `src/ElpisEdgeConnect.Management/Api/LicenseActivationService.cs:317-329` | Same resolution, duplicated. |
| License binding | `src/ElpisEdgeConnect.Core/Licensing/LicenseManager.cs:297-313` (ADR-0036) | License `gatewayId` must equal the resolved identity (case-insensitive), unless it is `*` (floating). Mismatch → `LICENSE_GATEWAY_MISMATCH`. |
| Machine-wide precedent | `src/ElpisEdgeConnect.Host/ClockAnchorStore.cs` | The clock anchor is *already* stored machine-wide at `%ProgramData%\Elpis\EdgeConnect` — a location that does **not** depend on the data root. |

**Key facts:**

- The id is a **random GUID** — there is **no** hardware/OS derivation. The same
  machine does not reproduce the same id; the id is only stable because the *file*
  is stable.
- The id is **only as stable as `<dataRoot>/identity`**. Change the data root, or
  delete the file, and a new id is minted.
- The identity resolution logic is **duplicated** in the Host and the Management
  license layer (two copies to keep in sync).

---

## 3. Problem statement

Because the identity is **data-root-scoped**, one physical machine can hold several
identities:

| How it's run | Data root | Identity file | Gateway id |
|---|---|---|---|
| Installed Windows service (default) | `C:\ProgramData\EdgeConnect` | `…\EdgeConnect\identity` | e.g. `30bf7c3e-1084-42c7-9ea4-1af041de4eb9` |
| Manual / dev run with `EDGECONNECT_DATA_ROOT` | e.g. `…\Temp\ec-launch` | `…\ec-launch\identity` | a **different** GUID |

Observed on the current dev machine: the service's `…\ProgramData\EdgeConnect\identity`
holds `30bf7c3e-…`, while a temp-root dev run generated its own (different) id — same
machine, two ids.

Consequences:

1. **License mismatch.** A license is issued for one id (say the service's). Any
   instance that resolves a *different* id fails ADR-0036 binding and drops to demo
   mode with `LICENSE_GATEWAY_MISMATCH`.
2. **Operator confusion.** The License page shows "This gateway's ID"; it silently
   differs between launches, and re-issuing a license appears not to help.
3. **Fragility on file loss.** Deleting `…\EdgeConnect\identity` (manual cleanup,
   folder recreated, imaging) silently mints a brand-new id and invalidates the
   installed license — with no warning today.

---

## 4. Requirement

> **One system, one gateway id.** Every EdgeConnect process on the same physical
> machine must resolve to the **same** gateway id, regardless of the configured data
> root, and ideally regardless of accidental deletion of a single file.

Corollary requirements:

- **Must not break already-activated machines.** Machines that already have an
  `identity` file (and licenses issued against it) must keep the **same** id.
- **Cross-platform** (Windows + Linux) per the platform constraint.
- **Deterministic and observable** — first-time generation should be logged clearly.

---

## 5. Options

### Option A — Machine-wide identity file (data-root-independent) — *simplest*

Store the identity at a **single fixed machine-wide path**, independent of the data
root, and read/write it from every instance:

- Windows: `%ProgramData%\Elpis\EdgeConnect\identity` (mirrors `ClockAnchorStore`).
- Linux: a fixed path such as `/etc/edgeconnect/identity` (or `/var/lib/edgeconnect/identity`),
  **not** derived from `EDGECONNECT_DATA_ROOT`.

Keep it a **random GUID**, generated once, at that fixed location.

| Pros | Cons |
|---|---|
| Minimal change; keeps the "opaque random GUID" model (no hardware coupling). | Still a file — **deleting it mints a new id** (though one well-known machine location is less likely to be cleared than a per-root file). |
| Cross-platform, no registry/WMI reads. | VM **clone/image** copies the file → clones share the id (may be desired or not — see §7). |
| Same id for service and any dev run on the machine. | Does not survive an OS re-image that wipes `%ProgramData%`. |

### Option B — Derive deterministically from an OS/machine identifier — *most robust "one system one id"*

Compute the gateway id as a **stable transform** (e.g. **UUIDv5** over a fixed
namespace) of the operating system's machine identifier, so the id is **reproducible**
from the machine itself even if no file exists:

- Windows: `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` (per-Windows-install
  GUID), or SMBIOS system UUID (`Win32_ComputerSystemProduct.UUID`).
- Linux: `/etc/machine-id` (systemd) or `/var/lib/dbus/machine-id`.

Persist the derived id to the machine-wide file as a **cache**; if the file is
missing, re-derive the **same** id.

| Pros | Cons |
|---|---|
| **Survives file deletion** — truly "one system, one id". | Machine identifier can **change**: Windows re-install / sysprep resets `MachineGuid`; motherboard swap changes SMBIOS UUID; `/etc/machine-id` can be regenerated. Any of these changes the id → license breaks. |
| No dependence on a fragile file. | **VM clones share** `MachineGuid` / `machine-id` → **two machines, same id** (licensing collision) unless the image is sysprepped/`machine-id` reset. |
| Reproducible for support ("what's my id?" is deterministic). | Registry/WMI reads are Windows-specific → needs `OperatingSystem.IsWindows()` guards and a Linux path; more code + failure modes (permissions, absent identifier). |
| | The id is **not rotatable** and is derivable by anyone on the box (mild info exposure). |

### Option C — Hybrid (machine-wide file + legacy promotion + optional derived seed) — *recommended*

Resolve the id in this order, writing the result to the machine-wide file:

1. **Machine-wide file present** → use it. *(steady state)*
2. **Else legacy `<dataRoot>/identity` present** → **promote** it (copy the existing
   GUID to the machine-wide file). *(preserves already-licensed machines — critical)*
3. **Else** → generate a new id and persist it machine-wide. The new id is either
   - **C1:** a random GUID (Option A semantics), or
   - **C2:** a UUIDv5 derived from the OS machine identifier (Option B semantics, so a
     later file deletion recovers the same id).

`EDGECONNECT_IDENTITY_PATH` continues to override everything (explicit ops control).

This gives "one system, one id" across data roots (steps 1–2 dominate in the field),
never breaks an existing licensed machine (step 2), and lets us choose how strong the
"survives deletion" guarantee is (C1 vs C2) for **new** machines only.

---

## 6. Machine-identifier sources (for Options B / C2)

| OS | Source | Stable across | Changes on |
|---|---|---|---|
| Windows | `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` | reboots, app reinstall, disk moves within same OS | **Windows reinstall / sysprep**, VM clone shares it |
| Windows | SMBIOS UUID (`Win32_ComputerSystemProduct.UUID`) | OS reinstall | **motherboard replacement**, some VMs report all-zero/duplicate |
| Linux | `/etc/machine-id` | reboots, app reinstall | image clone (should be reset), manual regen |

**Never** use volatile identifiers (MAC address — changes with NIC/USB adapters/VPN;
drive serials — change on disk swap) as the primary seed. If a composite is used,
weight it toward the OS install id.

---

## 7. Cross-cutting impact

- **Licensing (ADR-0036).** The whole point — binding must resolve the *same* id
  everywhere. Both identity resolvers (`EdgeConnectComposition` and
  `LicenseActivationService.ResolveIdentityPath`) must be unified onto the new scheme,
  or they will disagree. **Recommend extracting one shared resolver.**
- **Already-issued licenses.** Step-2 promotion (Option C) is **mandatory** to avoid
  invalidating licenses already issued against existing `…\EdgeConnect\identity`
  values (e.g. `30bf7c3e-…`). Without it, every currently-licensed machine breaks on
  upgrade.
- **VM cloning / golden images.** A file-based id (A/C1) is copied with the image →
  clones collide; a derived id (B/C2) collides unless the OS machine id is reset.
  **Decision needed:** is a cloned VM meant to be the *same* gateway (id travels with
  the image) or a *new* gateway (must re-license)? This determines A/C1 vs C2 and the
  imaging runbook.
- **Backup / diagnostic bundle.** `identity` is captured by the bundle contributor
  (`Management/Bundle/V1Contributors.cs`, "identity"). Restoring a bundle onto a
  different machine would carry the id — confirm that's still intended, and that the
  machine-wide path is the one bundled/restored.
- **Uninstall/reinstall.** MSI does **not** delete `%ProgramData%` today, so the id
  survives reinstall. If we move to `%ProgramData%\Elpis\EdgeConnect`, keep that
  folder out of the uninstaller's owned set too.
- **Permissions.** The service runs as LocalSystem (can write `%ProgramData%`). A
  non-elevated dev run may not be able to write a machine-wide location — needs a
  read-only-fallback path (as `ClockAnchorStore` already tolerates).

---

## 8. Security considerations

- A file-based id is **copyable** — single-machine binding via a file is a deterrent,
  not a hard lock (already true today). A derived id (B/C2) raises the bar slightly
  but is still not tamper-proof (registry/`machine-id` are editable by an admin).
- Deriving from a machine identifier exposes a stable machine fingerprint in the id.
  Using a **UUIDv5 with a private namespace** (not the raw identifier) avoids leaking
  the underlying value.
- None of these options change the license *signature* trust model; they only change
  what the binding compares against.

---

## 9. Recommendation

1. Adopt **Option C (hybrid)** with a **machine-wide file** at
   `%ProgramData%\Elpis\EdgeConnect\identity` (Windows) and a fixed
   `/etc/edgeconnect/identity` (Linux), resolved by **one shared resolver** used by
   both the Host and the license layer.
2. Implement **legacy promotion** (step 2) so existing licensed machines keep their id.
3. For **new** machines, start with **C1 (random GUID)** unless the product decision in
   §7 (VM-clone semantics) calls for **C2 (derived)**. C1 is simpler and matches the
   current "opaque id" model; C2 adds deletion-resilience at the cost of the clone/
   reinstall caveats.
4. Add a **clear first-run log line** ("no gateway identity found at `<path>` —
   generating a NEW id `<id>`; existing licenses will not match") and surface the id +
   its source on the License page.

This satisfies "one system, one id" in the field (no more per-root drift), is safe for
existing installs, and defers the harder hardware-binding decision to an explicit
product choice rather than baking it in silently.

---

## 10. Proposed rollout (once a direction is chosen)

1. **ADR** amending Locked Decision #19 / ADR-0036 to define the machine-wide identity
   and promotion order.
2. Extract a single `GatewayIdentityResolver` (path + read/generate/promote), replacing
   the duplicated logic in `EdgeConnectComposition` and `LicenseActivationService`.
3. Implement machine-wide path + legacy promotion + (optional) derived seed, with the
   read-only fallback pattern from `ClockAnchorStore`.
4. Tests: fresh machine → one id; two data roots → same id; delete machine-wide file →
   (C1) new id logged / (C2) same id re-derived; legacy file present → promoted, id
   unchanged; `EDGECONNECT_IDENTITY_PATH` override still wins; Linux path honoured.
5. Verify end-to-end: activate a license, switch data roots, confirm it stays valid.
6. Migration note in release docs + the imaging runbook (§7 clone decision).

---

## 11. Open questions (decisions needed before implementation)

1. **VM clone semantics** — should a cloned image be the **same** gateway (id travels)
   or a **new** gateway (must re-license)? → picks A/C1 vs C2.
2. **Deletion resilience** — is "survives deleting the identity file" a hard
   requirement? If yes → C2 (derived). If "don't delete the file" is acceptable → C1.
3. **Linux machine-wide path** — `/etc/edgeconnect/identity` vs
   `/var/lib/edgeconnect/identity` vs native `/etc/machine-id` derivation.
4. **Existing dev/test ids** — any machines already licensed against a *non-default*
   data root id that must also be promoted? (Promotion only looks at the default
   legacy path unless we enumerate.)
