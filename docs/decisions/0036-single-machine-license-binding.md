# ADR-0036 — Single-machine (node-locked) license binding

**Status:** Accepted (2026-07-07). Implements the previously-reserved
`CORE.LICENSE_GATEWAY_MISMATCH` check (blueprint §7, `license-file-format.md` §12).

## Context

Until now the license `gatewayId` field was **not enforced** — the runtime
validated only the RSA signature, expiry, and module/limit entitlements. There was
no hardware fingerprint, MAC binding, or phone-home. Consequently a single signed
`license.json` worked on **any** machine running a build with the matching embedded
public key: copy it anywhere and it was accepted.

The product needs licenses tied to **one gateway** so a license issued to one
customer/site cannot be reused on other machines.

## Decision

A license is **bound to a single gateway identity**. At license load, after
signature + payload validation, the runtime compares the license `gatewayId` to the
running gateway's identity id (the per-gateway UUID from `IGatewayIdentity`,
persisted at `HostOptions.GatewayIdentityPath`). On mismatch the load **throws
`CORE.LICENSE_GATEWAY_MISMATCH`** and the license does not take effect.

- **Single enforcement point.** The check lives in `LicenseManager.LoadAsync`
  (`EnforceGatewayBinding`). A mismatched license *fails to load*, so `Status`
  stays `NotLoaded`/prior and **every** existing gate (the ADR-0035 trial cutoff,
  DI-registration module checks, the Studio UI) automatically treats the machine as
  unlicensed. No new checks were threaded through the other enforcement points.
- **Identity provider.** `LicenseManager` takes an optional
  `Func<string?> expectedGatewayIdProvider`, resolved lazily at load time. The Host
  wires it to `FileSystemGatewayIdentity.TryReadPersisted(GatewayIdentityPath)` in
  both the eager DI-registration load (`EdgeConnectComposition`) and the
  `CompositionRoot` registration, so both enforcement loads agree.
- **First-start safety.** Before the identity file exists (first boot, prior to the
  `LoadGatewayIdentity` phase), the provider returns `null` and the check is
  skipped — a fresh install has no license yet, and the check resumes once the
  identity is established.
- **Floating licenses.** A license with `gatewayId == "*"` is valid on any gateway,
  letting Elpis issue a deliberate multi-machine/site license when required.
- **Comparison.** Case-insensitive, trimmed, ordinal.
- **Operator UX.** The Studio License page shows *"This gateway's ID: &lt;uuid&gt;"*
  so operators know which id to request a license for, and the Activate flow
  pre-checks binding and rejects a wrong-gateway license with a clear message before
  saving it.

## Issuance flow

1. Install → first start generates the gateway UUID at `<dataRoot>/identity`.
2. Operator reads the id from the Studio License page (or the identity file).
3. Elpis issues a license with `gatewayId` = that id (or `*` for a floating license).
4. Operator activates it; any other machine (different id) rejects it.

## Consequences

- One `license.json` now works on exactly **one** gateway (unless `gatewayId == "*"`).
- Re-imaging / data-root reset regenerates the identity UUID → the old license no
  longer matches; a new license (or a floating one) is required. Document this for
  support.
- Licenses issued before this change with a human id (e.g. `GW-SONY-001`) will fail
  binding on machines whose identity UUID differs — re-issue against the real
  gateway id, or use `*`.
- Identity is still a soft, file-based UUID (not hardware-derived); copying the
  identity file to another machine would move the binding. Hardware-derived identity
  is a future hardening option, out of scope here.

## Alternatives considered

- **Hardware fingerprint (MAC/CPU/disk):** stronger, but brittle across NIC changes
  / VMs and heavier; deferred.
- **Bind to the config `GatewayId`** (human-assigned, e.g. `GW-SONY-001`) instead of
  the identity UUID: easier to issue but trivially reused by setting the same config
  value on many machines — rejected for real node-locking.
