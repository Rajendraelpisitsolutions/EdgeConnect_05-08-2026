# Elpis EdgeConnect — License File Validation & Tamper-Prevention Plan

**Purpose:** define exactly how a `license.json` is validated at runtime, **how
tampering is prevented and detected**, the enforcement points, the threat model, and
the test/QA plan.
**Grounded in:** `src/ElpisEdgeConnect.Core/Licensing/` (as-built).
**Companions:** `license-file-format.md`, `licensing-complete-guide.md`,
`license-manager-architecture.md`, ADR-0035 (trial cutoff), ADR-0036 (single-machine binding).

---

## 1. Validation goal

A license must be accepted **only if** all of the following hold; otherwise it is
rejected and the gateway behaves as unlicensed:

1. It is **authentic** — signed by Elpis's private key (not forged, not edited).
2. It is **well-formed** — required fields present, types/ranges valid, dates parse.
3. It is **in date** — not past the expiry + grace window.
4. It is **for this gateway** — `gatewayId` matches this machine (or is floating `*`).
5. Its **entitlements** (modules, limits) are honoured by the runtime.

---

## 2. License contents & machine scope (single vs multiple machines)

### 2.1 What is saved to `license.json`

Everything selected in the **License Generator** is written into the file, plus two
auto-included modules and the signature. Nothing is stored anywhere else — the file
is the whole license.

| In the generator app | Stored in `license.json` |
|----------------------|--------------------------|
| Client name | `customer` |
| License type | `edition` (`Starter` / `Professional` / `Enterprise`) |
| Gateway ID | `gatewayId` |
| Expiry date | `expiresAt` |
| License ID | `licenseId` |
| **Sources** ticked | entries under `modules` (e.g. `source-focas2`, `source-melsec`) |
| **Destinations** ticked | entries under `modules` (e.g. `sink-mqtt`, `sink-opc-ua-server`) |
| Limits (max sources / sinks / routes) | `limits` |
| *(auto-included)* | `modules.core-runtime`, `modules.connectivity-studio` |
| *(computed at submit)* | `issuedAt` (today, UTC), `signature` (RSA-PSS) |

Once written, the file is **signed** (§3). Any later hand-edit invalidates it.

### 2.2 Single machine vs multiple machines — decided by the Gateway ID

The same generator produces **either** a single-machine or a multi-machine license.
It is controlled entirely by the **Gateway ID** field:

| Gateway ID entered | Machine scope | Behaviour |
|--------------------|---------------|-----------|
| A specific gateway's identity id (the UUID shown on that gateway's Studio **License** page, e.g. `44d3a7fa-…`) | **Single machine** | Activates only on that one gateway; rejected on any other with `CORE.LICENSE_GATEWAY_MISMATCH` (ADR-0036) |
| `*` | **Multiple machines (floating)** | Activates on **any** gateway |

- **Default / recommended:** enter the target gateway's real ID → the license is
  **locked to that one machine**.
- **Deliberate multi-machine license:** enter `*` → a floating license valid
  everywhere.

> **Scope of the single-machine lock.** Binding is enforced against the gateway's
> identity UUID (`<dataRoot>/identity`), which is a **soft, file-based** id — strong
> against casual copying of the license, but not hardware-derived. Copying the
> identity file itself to another machine would move the binding. Hardware-derived
> identity is a documented hardening option (§7).

---

## 3. The core anti-tamper mechanism: digital signature

### 3.1 Can the `license.json` be edited? — yes, but it is self-verifying, not locked

The license is a plain text file, so anyone **can** open and edit it. EdgeConnect
does **not** try to lock, hide, or encrypt the file, and it does not need to. The
file is **self-verifying**: any edit is detected at load and the license is
**rejected**, so the gateway falls back to *unlicensed*. Editing therefore gains an
attacker nothing.

- Every field is covered by the signature (§3.2) — change one byte and verification
  fails with `CORE.LICENSE_SIGNATURE_INVALID`.
- To make an edit "stick" you must produce a **new valid signature**, which requires
  **Elpis's private key** — never present in the shipped product (it lives only in
  the License Generator / signing environment).
- A rejected file cannot even replace a currently-loaded license (fail-safe, §4).

"Editing gains nothing" — representative attempts (full list in §5):

| If someone edits… | Result |
|-------------------|--------|
| Extend `expiresAt` | rejected (signature mismatch) |
| Enable an unpurchased protocol in `modules` | rejected |
| Raise a `limits` value | rejected |
| Retarget `gatewayId` to another machine | rejected (signature + binding) |
| Delete or replace the `signature` | rejected |
| Re-sign with a non-Elpis key | rejected (not the embedded public key) |

**Restriction model:** we do not restrict *writing* the file — we make any modified
file **invalid**. The only way to produce a license EdgeConnect will accept is to
sign it with Elpis's private key. The one residual limit is that verification is
client-side (a patched binary could bypass it) — see §7 for that and the dev-key
note, plus hardening options.

### 3.2 The signature, precisely

The license is protected by an **RSA-PSS / SHA-256** digital signature over the
**canonical** form of the payload. This is the single most important control.

| Property | Value | File |
|----------|-------|------|
| Scheme | RSA-PSS | `LicenseSignatureValidator.cs` |
| Hash / MGF | SHA-256 / MGF1-SHA256 | ” |
| Min key size | 2048 bits (enforced on load) | ” |
| Signed bytes | **canonical JSON** of the payload, `signature` field removed | `CanonicalJson.cs` |
| Public key | PEM compiled into the binary | `EmbeddedPublicKey.cs` |
| Private key | held **externally** by Elpis (never in shipped binaries) | — |

**Why this stops tampering:** the signature is computed over the *canonical bytes* of
every field — `customer`, `gatewayId`, `edition`, `expiresAt`, `limits`, `modules`,
everything except `signature`. Changing **any** byte of any field changes the
canonical bytes, so the stored signature no longer verifies against the embedded
public key → the license is **rejected** (`CORE.LICENSE_SIGNATURE_INVALID`). An
attacker cannot produce a new valid signature because they do not have the private
key.

### Why "canonical" JSON matters
The signer and verifier both reduce the document to a deterministic canonical form
(`CanonicalJson`): keys sorted lexicographically at every depth, the `signature`
property removed, no insignificant whitespace, UTF-8 without BOM. This removes any
wiggle room — you cannot smuggle a change past the signature by reordering keys,
reformatting, or adding whitespace. Signer (`LicenseGen` / License Generator app) and
verifier (`LicenseManager`) call the **same** `CanonicalJson` class, so they can
never disagree on what bytes were signed.

### Why the public key can't be swapped
The trusted public key is **compiled into the binary** (`EmbeddedPublicKey.Pem`). An
attacker can't point the gateway at their own key without rebuilding the product. A
unit test (`LicenseSignatureValidatorTests.EmbeddedKey_FingerprintMatchesExpectedDevValue`)
pins the key's SHA-256 fingerprint, so an accidental or malicious key swap in source
**fails the build**.

---

## 4. The 8-step validation pipeline (`LicenseManager.LoadAsync`)

| Step | Check | Reject code on failure |
|------|-------|------------------------|
| 0 | Serialize loads (single-flight lock) | — |
| 1 | `JsonDocument.Parse` | `CORE.LICENSE_FILE_CORRUPT` |
| 2 | Extract `signature` (present + string) | `CORE.LICENSE_SIGNATURE_INVALID` |
| 3 | `CanonicalJson.CanonicalizeRoot` (payload sans signature) | — |
| 4 | **Verify RSA-PSS signature** against embedded public key | `CORE.LICENSE_SIGNATURE_INVALID` |
| 5 | Parse typed payload — required fields, integer ranges ≥ 0, date formats, `expiresAt ≥ issuedAt`, known edition | `CORE.LICENSE_FILE_CORRUPT` |
| 5b | **Single-machine binding** — `gatewayId` == this gateway's identity (or `*`) | `CORE.LICENSE_GATEWAY_MISMATCH` |
| 6 | Evaluate expiry vs. wall clock → status | — |
| 7 | Atomic snapshot swap; raise warnings | — |

**Fail-safe:** every failure throws `LicenseException(code, …)` **before** the
snapshot swap, so the previously-loaded license is untouched. A tampered or wrong
reload can never downgrade a running, licensed gateway.

---

## 5. What each tampering attempt produces

| Attempt | Outcome |
|---------|---------|
| Edit `expiresAt` to a later date | Signature no longer matches → `LICENSE_SIGNATURE_INVALID` (rejected) |
| Add/flip a module (`"enabled": true`) | Signature mismatch → rejected |
| Raise a `limits` value | Signature mismatch → rejected |
| Change `customer` / `gatewayId` | Signature mismatch → rejected |
| Re-order keys / reformat / add whitespace | No effect — canonicalization normalizes it; signature still verifies (not a tamper) |
| Remove the `signature` field | `LICENSE_SIGNATURE_INVALID` (missing signature) |
| Replace `signature` with garbage / another license's sig | Verification fails → `LICENSE_SIGNATURE_INVALID` |
| Sign with a different (attacker) private key | Doesn't match embedded public key → `LICENSE_SIGNATURE_INVALID` |
| Truncate / corrupt the file | `LICENSE_FILE_CORRUPT` or `LICENSE_SIGNATURE_INVALID` |
| Use a valid license from **another machine** | `LICENSE_GATEWAY_MISMATCH` (ADR-0036) |
| Present an expired license | Loads but status `InGracePeriod`/`Expired`; config blocked after grace; **unlicensed cutoff** applies (ADR-0035) |
| **Wind the system clock BACK** (resurrect an expired license) | Detected against the persisted **clock anchor** high-water mark → runtime flags tamper and drops to **demo mode** (§5.1) |
| **Wind the clock FORWARD then correct it** | Anchor advance is **capped to real (monotonic) elapsed time**, so a forward jump can't "poison" the anchor; correcting the clock clears the flag (§5.1) |
| Swap the embedded public key in source | Build fails (fingerprint test) |

In every rejection case the gateway is treated as **unlicensed**: the ADR-0035 trial
cutoff runs, protocol adapters are not licensed, and the Studio shows the unlicensed
state.

### 5.1 Clock-rollback anti-tamper (system-date bypass)

Expiry is evaluated against the system clock, so an expired license would come back to
life if the machine's date were wound back before `expiresAt`. To stop that, the
runtime keeps a persisted **clock anchor** — the highest UTC time it has ever observed:

- **Storage** — `ClockAnchorStore` writes the anchor to **two** locations (the gateway
  data root and a machine-wide `%ProgramData%\Elpis\EdgeConnect` folder), each
  **HMAC-SHA256-tagged** with a key derived from the embedded public key, so
  hand-editing the stored value is detected and ignored. Deleting one copy doesn't
  reset it (the latest valid value across locations wins).
- **Detection** — `ClockRollbackPolicy.IsRollback(now, anchor, tolerance)` is true when
  `now < anchor − tolerance` (default tolerance **5 min** for NTP drift). On rollback
  the runtime sets a **clock-tampered** flag and drops to **demo mode** (surfaced in
  the Studio); when the clock is sane again the flag clears.
- **Forward-poison guard (2026-07)** — `ClockRollbackPolicy.Advance` caps how far the
  anchor may move forward to the **real monotonic elapsed time** (`Environment.TickCount64`)
  plus slack. Without this, setting the clock far into the future would push the anchor
  into the future and make a later *correction* look like a permanent rollback. The
  anchor file was bumped to `.clock-anchor-v2` so any anchor poisoned by the earlier
  build is ignored and a fresh, capped one starts.
- **Where** — evaluated periodically by `LicenseTrialEnforcer` (host), not at file load;
  logic is pure/unit-tested in `ClockRollbackPolicy` (Core).

A determined attacker with admin rights can still defeat any purely local anti-rollback
scheme; the goal here is to stop the trivial "change the system date" bypass.

---

## 6. Enforcement points (validation is not just at load)

| Layer | What it enforces | Where |
|-------|------------------|-------|
| **Load/verify** | signature, well-formedness, expiry, gateway binding | `LicenseManager.LoadAsync` |
| **DI-registration gate** (live) | per-adapter: `IsModuleEnabled(moduleKey)` — a disabled/unlicensed protocol is not wired up | `Add{Protocol}SourcesFromGatewayConfig` |
| **Config-apply gate** | module enabled? per-module `maxInstances`? global `limits`? (implemented; wired permissive by default today) | `LicenseGate` / `ConfigurationValidator` |
| **Unlicensed runtime cutoff** | stops the gateway after 2h with no valid license | `LicenseTrialEnforcer` (ADR-0035) |
| **UI** | wizard tiles + Activate flow gate on module keys; activation pre-checks binding & signature before saving | Studio License page / pickers |

Because a tampered license **fails to load**, `Status` stays `NotLoaded` and *all*
of these layers automatically treat the gateway as unlicensed — one failure point,
consistent behaviour.

---

## 7. Threat model & honest limitations

**Strong against:**
- Editing any license field (defeated by the signature).
- Forging a license (needs Elpis's private key).
- Reusing a license across machines (defeated by gateway binding, ADR-0036).
- Pointing the gateway at a rogue signing key (needs a rebuild; fingerprint pinned).

**Known limitations (documented, with mitigations / roadmap):**
1. **Dev signing key currently in the repo.** Must be rotated to a production key
   held in a password manager / HSM before shipping (`license-file-format.md` §10).
   Until then, anyone with the repo can mint valid licenses.
2. **Gateway identity is a soft file UUID** (`<dataRoot>/identity`), not
   hardware-derived. Copying that file to another machine moves the binding.
   *Mitigation/roadmap:* hardware-derived identity (MAC/CPU/disk) is a future
   hardening option.
3. **No revocation / no phone-home** (offline by design). A leaked license cannot be
   remotely revoked before its `expiresAt`. *Mitigation:* short expiries; re-issue on
   renewal; the binding limits blast radius to one gateway.
4. **Client-side verification.** As with any offline licensing, a determined attacker
   with the binary could patch the in-process verifier. *Mitigation (out of current
   scope):* Authenticode-sign the binaries, anti-tamper/obfuscation, integrity checks.
5. **Config-apply `LicenseGate` is permissive by default** in the host today (the
   `AllowAll` gate). Live enforcement is the DI-registration gate + UI; enabling the
   full config gate is a one-line DI change (see licensing-complete-guide §15).
6. **Module-key divergence:** OPC UA Client is gated on `source-opcua-client` (adapter
   constant) vs. `source-opc-ua-client` (`LicenseModuleKeys` catalog). Licenses must
   use the adapter value. **Action:** reconcile the two in code.

### Recommended hardening (roadmap)

Each item strengthens the controls above; all are optional and additive.

1. **Hardware-bind the gateway identity.** Derive the gateway identity from stable
   hardware (MAC address, CPU id, disk serial, or a TPM) instead of — or in addition
   to — the file-based UUID, so a **single-machine license cannot be moved by copying
   the `identity` file**. Closes limitation #2.
2. **Authenticode-sign the binaries and installer.** Code-sign the EdgeConnect
   executables and the MSI / setup.exe so a **patched or tampered binary is
   detectable** (and flagged by Windows), raising the bar against bypassing the
   client-side verifier. Mitigates limitation #4.
3. **Rotate to a production signing key in an HSM.** Replace the embedded dev key and
   keep the private key in an HSM / password manager; re-issue licenses. Closes
   limitation #1.
4. **Enable the config-apply `LicenseGate`.** Wire the real gate (instead of AllowAll)
   so module / instance / global-limit violations are also blocked at config-apply
   time. Closes limitation #5.
5. **Short-lived licenses + renewal.** Issue shorter expiries and renew on payment,
   bounding the blast radius of a leaked license (partial mitigation for the
   no-revocation limitation #3).

---

## 8. Validation / QA plan

### 8.1 Automated tests (exist / to maintain)
- **Signature & fingerprint:** `LicenseSignatureValidatorTests` — verify good sig,
  reject bad base64/sig, embedded-key fingerprint pinned.
- **Load pipeline:** `LicenseManagerTests` — corrupt JSON, missing/invalid signature,
  missing fields, range/date validation.
- **Binding (ADR-0036):** `LicenseManagerBindingTests` — match, mismatch →
  `LICENSE_GATEWAY_MISMATCH`, floating `*`, no-identity skip, case-insensitive.
- **Expiry:** `LicenseExpirationTrackerTests` — valid / grace / expired boundaries,
  end-of-day.
- **Trial cutoff (ADR-0035):** `LicenseTrialEnforcerTests` — stop after window for
  every non-Valid status; reset on valid.
- **Gate:** `LicenseGateTests` — module disabled / instance / global limits.

### 8.2 Manual / integration checklist (per release)
1. Generate a license for gateway **A** → activate on A → **Valid**. ✅
2. Take A's license to gateway **B** → activation **rejected** (mismatch). ✅
3. Hand-edit an activated license (bump `expiresAt`, add a module) → restart →
   **rejected** (`LICENSE_SIGNATURE_INVALID`); previous snapshot retained. ✅
4. Delete the `signature` field → **rejected**. ✅
5. Floating `*` license → activates on any gateway. ✅
6. Expired license → in grace: config warnings; past grace: config blocked, data
   flows; unlicensed cutoff after the trial window. ✅
7. No license → 2-hour cutoff stops the gateway (ADR-0035). ✅
8. Wrong-key license (signed with a non-Elpis key) → **rejected**. ✅

### 8.3 Release gates
- All licensing tests green (`dotnet test --filter FullyQualifiedName~Licensing`).
- Embedded public key fingerprint matches the intended production key.
- Production private key **not** present in any shipped artifact.
- `LicenseGen` / License Generator app sign with the production key.

---

## 9. Summary — "how tampering is maintained"

Tampering is **prevented by an offline RSA-PSS signature over canonical JSON**, with
the **trusted public key embedded in the binary** and the **private key held only by
Elpis**. Any change to any field breaks the signature and the license is rejected;
forgery requires the private key; reuse across machines is blocked by gateway
binding; time abuse is bounded by expiry + the unlicensed cutoff. The residual risks
(soft identity, dev key in repo, no revocation, client-side verification) are known,
documented, and each has a mitigation or roadmap item.
