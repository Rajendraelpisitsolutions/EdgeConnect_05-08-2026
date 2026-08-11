# Elpis EdgeConnect — License File Format

**Status:** B3 — locked. Any change to this document is a breaking change for every issued license.
**Reference:** `ARCHITECTURE_BLUEPRINT.md` §7, `PHASE1_EXECUTION_PLAN.md` Milestone B3.

---

## 1. Purpose

The license file is the single source of truth for what a binary build of Elpis EdgeConnect is allowed to do at runtime. It controls which protocol modules activate, how many instances may be configured, which features are enabled, and when the gateway must be re-licensed. The file is loaded fully offline; the runtime never contacts a server to validate it.

---

## 2. Location

The host (Phase 2+) reads `GatewaySettings.LicenseFile` from `current.json` to find the license file on disk and passes it to `ILicenseManager.LoadFromFileAsync`. The B3 deliverable does not assume any particular location.

---

## 3. Algorithm choices (LOCKED — B3)

| Aspect | Value |
|---|---|
| Signature scheme | RSA-PSS |
| Hash | SHA-256 |
| MGF | MGF1-SHA256 |
| Salt length | hash length (32 bytes) |
| Minimum key size | 2048 bits |
| Encoding of payload bytes | UTF-8, no BOM |
| Encoding of signature in JSON | base64 (standard alphabet, with padding) |
| Public key embedding | PEM string compiled into the binary (`EmbeddedPublicKey.cs`) |

These values are not configurable at runtime. Changing any of them is a key rotation event and requires re-issuing every customer license.

---

## 4. JSON shape

```json
{
  "licenseId": "LIC-2026-0042",
  "customer": "Menon Manufacturing",
  "gatewayId": "GW-MENON-001",
  "edition": "Professional",
  "issuedAt": "2026-04-07",
  "expiresAt": "2027-04-07",
  "limits": {
    "maxSourceInstances": 50,
    "maxSinkInstances": 5,
    "maxRoutes": 100
  },
  "modules": {
    "source.focas2":      { "enabled": true,  "maxInstances": 20 },
    "source.modbus":      { "enabled": true,  "maxInstances": 10 },
    "source.s7":          { "enabled": false },
    "sink.mqtt":          { "enabled": true },
    "sink.opcuaserver":   { "enabled": false }
  },
  "features": {
    "storeAndForward":    true,
    "transforms.basic":   true,
    "transforms.advanced": false
  },
  "signature": "base64-rsa-pss-signature"
}
```

### 4.1 Required top-level fields

| Field | Type | Notes |
|---|---|---|
| `licenseId` | string | Stable, customer-visible id. |
| `customer` | string | Customer name. |
| `gatewayId` | string | Gateway this license is bound to. |
| `edition` | string | One of `Starter`, `Professional`, `Enterprise`. |
| `issuedAt` | date | `yyyy-MM-dd`. UTC. |
| `expiresAt` | date | `yyyy-MM-dd`. **Interpreted as 23:59:59.999 UTC of that day.** See §6. |
| `limits` | object | Global instance limits. All three sub-fields required. |
| `modules` | object | Per-module entitlements. Keys are `source.{name}` or `sink.{name}`. |
| `features` | object | Optional. Map of feature key → bool. |
| `signature` | string | Base64 RSA-PSS signature over the canonical payload. |

### 4.2 `limits`

```jsonc
"limits": {
  "maxSourceInstances": 50,  // total across all source modules
  "maxSinkInstances":   5,
  "maxRoutes":          100
}
```

All three are required and must be non-negative integers.

### 4.3 `modules`

Each entry is a JSON object:

```jsonc
"source.focas2": { "enabled": true, "maxInstances": 20 }
```

- `enabled` (required, bool) — when `false`, the module is treated as not present.
- `maxInstances` (optional, int ≥ 0) — per-module instance cap. Omit for "unlimited within the global cap."

Module identifier rules: lowercase, `source.{protocolName}` or `sink.{protocolName}`, where `protocolName` matches the regex enforced by `SourceInstanceConfig.ProtocolName` (lowercase letters, digits, hyphens).

### 4.4 `features`

A flat map of `string → bool`. Feature keys are lowercase. B3 records features but does not enforce them — feature gating is exercised by C1+ and the protocol modules in Phase 2.

---

## 5. Canonical JSON (signing input)

The bytes that the signer signs and the verifier verifies are NOT the literal license file content. Both sides reduce the document to a canonical form first. Both sides reference `ElpisEdgeConnect.Core.Licensing.CanonicalJson` so they cannot drift.

Rules:

1. Object keys are sorted lexicographically (ordinal, case-sensitive) at every depth.
2. The top-level `signature` property is **removed** before signing/verifying. Nested `signature` keys are not special.
3. No insignificant whitespace.
4. Strings are emitted exactly as `System.Text.Json`'s writer does (UTF-8, JSON-escaped).
5. Numbers preserve their source raw text (we do not normalize numeric form).
6. Arrays preserve their source order.
7. Output is UTF-8 with no BOM.

The bytes produced by `CanonicalJson.Canonicalize` are passed directly to RSA-PSS / SHA-256.

---

## 6. Date semantics (LOCKED — B3)

`issuedAt` and `expiresAt` are dates with no time component. The runtime interprets them as follows:

- `issuedAt` → `00:00:00.000 UTC` of that day.
- **`expiresAt` → `23:59:59.999 UTC` of that day.**

A license with `expiresAt: "2026-04-07"` is therefore `Valid` throughout the entirety of 2026-04-07 (UTC) and only enters `InGracePeriod` at the first instant of 2026-04-08. This is pinned by `LicenseExpirationTrackerTests.EndOfDay_StillValid_ThroughoutLastDay`.

---

## 7. Lifecycle

| Status | Condition | Behaviour |
|---|---|---|
| `NotLoaded` | No `LoadAsync` call has succeeded yet | Empty configurations are allowed (first-boot). Any non-empty config is rejected by `LicenseGate`. |
| `Valid` | `now ≤ expiresAt` | All checks active. Warnings raised at 30/7/1 days before expiry. |
| `InGracePeriod` | `expiresAt < now < expiresAt + 30 days` | Config changes still allowed; `LicenseGate` returns `Allowed=true` with a non-empty `Warnings` list. |
| `Expired` | `now ≥ expiresAt + 30 days` | All config changes blocked. **Data flow continues** — Core never blocks the data path on a license check (blueprint §7.4). |
| `Invalid` | Signature failed verification or parse failed | `LoadAsync` throws and the previous snapshot (if any) is preserved. |

The grace period and warn-day boundaries live in `LicenseEnforcementPolicy.Default`. Both are LOCKED at 30 days and `{30, 7, 1}` respectively.

---

## 8. Validation flow

`ILicenseManager.LoadAsync` performs:

1. Read full file as UTF-8.
2. `JsonDocument.Parse` — failure → `CORE.LICENSE_FILE_CORRUPT`.
3. Extract `signature` field — missing → `CORE.LICENSE_SIGNATURE_INVALID`.
4. `CanonicalJson.CanonicalizeRoot` over the payload (sans `signature`).
5. `LicenseSignatureValidator.Verify(canonical, base64Sig)` — failure → `CORE.LICENSE_SIGNATURE_INVALID`.
6. Decode typed `LicenseInfo` (required fields, integer ranges, date parsing) — failure → `CORE.LICENSE_FILE_CORRUPT`.
7. `LicenseExpirationTracker.Evaluate(now, expiresAt, policy)` to compute the initial status.
8. Atomic snapshot swap; reset warning dedupe set; raise initial warning if at a boundary.

A failure in any step throws `LicenseException` with the appropriate `CoreErrors` code. The previously-loaded snapshot (if any) is preserved.

---

## 9. Enforcement points (`LicenseGate`)

`LicenseGate` is the production `ILicenseGate` implementation. It is invoked by `ConfigurationValidator` as the third stage of the apply pipeline (after DataAnnotations and cross-record validation).

For each apply:

1. If status is `Expired` → block all configurations.
2. If status is `NotLoaded` → block any configuration containing sources, sinks, or routes; allow empty configs.
3. Per source: build module key `source.{protocolName.ToLowerInvariant()}`; reject if not enabled.
4. Per sink: same with `sink.` prefix.
5. Per module: count occurrences and reject if > `MaxInstances`.
6. Global limits: reject if `Sources.Count > MaxSourceInstances`, etc.
7. If status is `InGracePeriod` → still allowed if all of the above pass, but a `Warnings` entry is attached.

---

## 10. Key custody

### 10.1 Current state — DEV KEY

The repo currently embeds a development RSA-2048 public key in `EmbeddedPublicKey.cs`, fingerprint `2A5983CBC51FDB77F0D146A8F289A473A15F9F703338B4041D407EA77E010311`. The matching dev private key is committed to `tests/ElpisEdgeConnect.Core.Tests/Licensing/TestRsaKeys.cs` for reproducible test signing only.

**This key MUST NOT be used to issue customer licenses.** It exists so that the test suite is hermetic and reproducible across machines.

`LicenseSignatureValidatorTests.EmbeddedKey_FingerprintMatchesExpectedDevValue` pins the fingerprint so any accidental key swap fails the build.

### 10.2 Production hand-off (Phase 4)

When the product enters customer hands:

1. Generate a new RSA-2048 keypair on an offline machine using LicenseGen:
   ```
   dotnet run --project tools/LicenseGen -- keygen --out keys/
   ```
2. Move `keys/private.pem` into the production password manager / HSM. Never commit it.
3. Replace the PEM constant in `EmbeddedPublicKey.cs` with the new public key.
4. Update `EmbeddedPublicKey.Fingerprint` with the new SHA-256 of the SubjectPublicKeyInfo.
5. Update the expected fingerprint in `LicenseSignatureValidatorTests.EmbeddedKey_FingerprintMatchesExpectedDevValue` to match.
6. Re-issue all customer licenses signed with the new private key.
7. Tag the release `license-key-rotation-N` so the rotation event is auditable.

The dev key + test private key may remain in the repo on a separate `dev-keys` branch for local testing.

---

## 11. LicenseGen CLI

`tools/LicenseGen/` is an internal command-line tool that generates keypairs and signed license files. It references `ElpisEdgeConnect.Core` so the canonicalization logic cannot drift.

```
LicenseGen keygen --out keys/

LicenseGen new \
    --customer "ACME Corp" \
    --gateway  "GW-ACME-001" \
    --edition  Professional \
    --expires  2027-04-07 \
    --private-key keys/private.pem \
    --out license.json \
    [--license-id LIC-2026-0042] \
    [--max-sources 50] [--max-sinks 5] [--max-routes 100] \
    [--modules source.focas2=true:20,source.modbus=true,sink.mqtt=true]
```

LicenseGen is not shipped to customers and is excluded from any release artifact.

---

## 12. Error codes

| Code | When |
|---|---|
| `CORE.LICENSE_FILE_NOT_FOUND` | Path passed to `LoadFromFileAsync` does not exist. |
| `CORE.LICENSE_FILE_CORRUPT` | JSON parse failed, required field missing, or value out of range. |
| `CORE.LICENSE_SIGNATURE_INVALID` | Signature missing, malformed base64, or did not verify against the embedded key. |
| `CORE.LICENSE_NOT_LOADED` | Operation attempted before any license was loaded. |
| `CORE.LICENSE_MODULE_DISABLED` | A configured source/sink uses a module not enabled by the license. |
| `CORE.LICENSE_INSTANCE_LIMIT_REACHED` | Per-module instance count exceeds `maxInstances`. |
| `CORE.LICENSE_GLOBAL_LIMIT_EXCEEDED` | One of the global `limits` ceilings is exceeded. |
| `CORE.LICENSE_EXPIRED` | Reported in warnings while in grace period. |
| `CORE.LICENSE_GRACE_EXHAUSTED` | Reported when status is `Expired`. All config writes blocked. |
| `CORE.LICENSE_GATEWAY_MISMATCH` | Reserved for the future check that `gatewayId` matches the running gateway identity. Not yet enforced in B3. |
