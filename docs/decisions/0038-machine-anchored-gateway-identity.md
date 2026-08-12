# 0038 — Machine-anchored gateway identity (one machine, one gateway id)

**Status:** Accepted (2026-07-30)
**Amends:** Locked Decision #19 (per-gateway UUID, established at first start);
complements ADR-0036 (single-machine license binding).
**Supersedes analysis:** `docs/gateway-identity-per-system-analysis.md` (Option C).

## Context

The gateway id is a random GUID generated once and persisted to
`<dataRoot>/identity`. It was **scoped to the data root**, not the machine, so
the *same physical machine* could produce **several** gateway ids — one per data
root (installed service vs. a manual/dev run with `EDGECONNECT_DATA_ROOT` set) —
and deleting the single identity file (or clearing `C:\ProgramData\EdgeConnect`)
minted a brand-new id. Because licenses bind to the gateway id (ADR-0036), a new
id silently breaks an activated license (`LICENSE_GATEWAY_MISMATCH`). The
identity-resolution logic was also duplicated between the Host and the
Management license layer.

The requirement: **one system → one gateway id**, independent of the data root,
resilient to deleting a single copy, without breaking already-licensed machines.

## Decision

Adopt **Option C** from the analysis: resolve and persist the gateway id through
a single **machine-anchored store** (`GatewayIdentityStore`), used by **both**
the identity service (`FileSystemGatewayIdentity.InitializeAsync`) and the
license layer (`FileSystemGatewayIdentity.TryReadPersisted`).

The store:

1. Persists the id to **several machine-anchored locations**: a machine-wide
   file (`%ProgramData%\Elpis\EdgeConnect\identity`, mirroring `ClockAnchorStore`),
   the per-data-root file, and — on Windows — the **HKLM registry**
   (`SOFTWARE\Elpis\EdgeConnect\GatewayId`), which survives deleting any
   ProgramData folder.
2. **HMAC-tags** every record over the id **and** a per-machine fingerprint
   (`MachineFingerprint`: Windows `MachineGuid` / Linux `machine-id`), so a
   record only validates on the **same** machine — a copy transplanted to a
   different machine is rejected, preserving ADR-0036 binding.
3. On startup resolves from the first surviving valid record; else **promotes a
   legacy plain-id file** (so already-licensed machines keep their id); else
   mints a new id (logged clearly). The result is **re-written to every
   location** (self-healing).

New machines use a **random GUID** (Option C1), not a machine-derived id (C2):
it keeps the opaque-id model and avoids the VM-clone / OS-reinstall collision
hazards of deriving from `MachineGuid`. Deletion-resilience comes from the
redundant locations (esp. the registry), not from re-derivation.
`EDGECONNECT_IDENTITY_PATH` still overrides everything.

## Consequences

- **Stable per machine.** Every process on the machine resolves the same id
  regardless of data root; the id survives deleting the per-root file or
  clearing `C:\ProgramData\EdgeConnect` (recovered from the registry / machine-
  wide file). It does **not** survive an OS reinstall/sysprep (registry cleared)
  — a new id is minted and logged.
- **Existing installs safe.** Legacy `<dataRoot>/identity` files are promoted, so
  machines already licensed against their old GUID keep it.
- **Single resolver.** Host and license layer no longer drift — both call the
  store.
- **VM clones share the id** (the registry/file image travels with the clone
  and `MachineGuid` is shared) — matches the previous file-copy behaviour;
  imaging that must yield a *new* gateway must reset the machine identity. Noted
  for the imaging runbook.
- **Registry write needs elevation.** The service (LocalSystem) writes HKLM; a
  non-elevated dev run degrades gracefully to the machine-wide + per-root files
  (best-effort, never fatal).
- **Not tamper-proof.** An admin can still edit the registry/files; this stops
  the trivial "I deleted the identity file and my license died" failure without
  weakening the binding model.

## Tests

`tests/ElpisEdgeConnect.Host.Tests/GatewayIdentityStoreTests.cs`: fresh → null;
write/read stable; survives deleting one slot; legacy plain-id promoted;
different-machine record rejected; tampered record rejected.
