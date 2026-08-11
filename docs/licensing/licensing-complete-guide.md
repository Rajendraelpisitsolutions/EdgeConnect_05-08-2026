# Elpis EdgeConnect — Licensing: Complete Pin-to-Pin Guide

**Audience:** engineers, release/ops, and anyone issuing or debugging licenses.
**Scope:** the entire licensing subsystem end-to-end — file format, cryptography,
every component, the load/verify pipeline, lifecycle, all enforcement layers,
editions, module keys, and the full activation runbook.
**Status:** descriptive of the code on `Sony_Development`. Every claim is cited to
`file:line`. Where the code and the spec diverge, the code wins and the gap is
flagged.
**Companions:** `license-file-format.md` (locked format spec), `module-catalog.md`
(module-key catalog), `license-manager-architecture.md` (+ `.html` diagram).

---

## Table of contents

1. Executive summary
2. Design principles (locked decisions)
3. The `license.json` file format
4. Cryptography & signing model
5. Component reference (every class)
6. The load & verify pipeline (8 steps)
7. Lifecycle, expiry & warnings
8. The enforcement layers (how it controls the app)
9. License types (editions) — what each one means
10. Module keys — catalog **and the three-way key mismatch** ⚠️
11. How to activate a license — full runbook
12. Production key custody & rotation
13. Error-code reference
14. Testing & verification
15. Known gaps / as-built nuances
16. Quick-reference cheat sheet

---

## 1. Executive summary

EdgeConnect uses **three-layer licensing** (locked decision #5): packaging
(per-edition installers), **runtime activation** (a signed `license.json`), and
UI/API enforcement. This document is about the runtime-activation layer and how it
reaches the other two.

- A single **RSA-PSS / SHA-256** signed JSON file, verified **fully offline**
  against a public key **embedded in the binary**. No phone-home (locked #6, #8).
- Loaded once at startup into an immutable, lock-free **snapshot**.
- Enforcement gates **what runs and what you can configure** — never the data path.
  An expired license blocks config changes but **never stops data flow** (locked #7).
- Namespace: `ElpisEdgeConnect.Core.Licensing`
  (`src/ElpisEdgeConnect.Core/Licensing/`). Issuer CLI: `tools/LicenseGen/`.

---

## 2. Design principles (locked decisions)

From `CLAUDE.md` / `ARCHITECTURE_BLUEPRINT.md` Appendix A:

| # | Decision |
|---|----------|
| 5 | Three-layer licensing: packaging + runtime activation + UI/API enforcement — all three enforced. |
| 6 | RSA-signed JSON license, fully offline, no phone-home. Public key embedded in binary; private key held externally. |
| 7 | License expiration **continues data flow, blocks config changes**. Never cut customer data to enforce licensing. |
| 4 | Modular assemblies activated **by license at DI registration time** (not dynamic plugins). |
| 10 | Per-adapter isolation: a disabled module never affects any other adapter. |

---

## 3. The `license.json` file format

### 3.1 Full example

```json
{
  "licenseId": "LIC-2026-0042",
  "customer": "Sony",
  "gatewayId": "GW-SONY-001",
  "edition": "Professional",
  "issuedAt": "2026-07-06",
  "expiresAt": "2027-07-06",
  "limits": {
    "maxSourceInstances": 50,
    "maxSinkInstances": 5,
    "maxRoutes": 100
  },
  "modules": {
    "source-focas2":       { "enabled": true,  "maxInstances": 20 },
    "source-melsec":       { "enabled": true },
    "source-ethernet-ip":  { "enabled": true },
    "sink-mqtt":           { "enabled": true }
  },
  "features": {
    "storeAndForward": true,
    "transforms.basic": true
  },
  "signature": "base64-rsa-pss-signature"
}
```

### 3.2 Field reference (parsed by `LicenseManager.ParsePayload`, `LicenseManager.cs:304-406`)

| Field | Type | Required | Rule |
|-------|------|----------|------|
| `licenseId` | string | ✅ | non-empty |
| `customer` | string | ✅ | non-empty |
| `gatewayId` | string | ✅ | non-empty (binding not yet *enforced* — see §13 `LICENSE_GATEWAY_MISMATCH`) |
| `edition` | string | ✅ | one of `Starter` / `Professional` / `Enterprise` (case-insensitive); `Unknown` rejected |
| `issuedAt` | date | ✅ | `yyyy-MM-dd`, parsed as **00:00:00.000 UTC** |
| `expiresAt` | date | ✅ | `yyyy-MM-dd`, parsed as **23:59:59.999 UTC**; must be ≥ `issuedAt` |
| `limits.maxSourceInstances` | int ≥ 0 | ✅ | global source cap |
| `limits.maxSinkInstances` | int ≥ 0 | ✅ | global sink cap |
| `limits.maxRoutes` | int ≥ 0 | ✅ | global route cap |
| `modules` | object | ✅ | map of `moduleKey → { enabled: bool, maxInstances?: int≥0 }` |
| `features` | object | optional | flat `string → bool`; **recorded but not enforced in Core today** |
| `signature` | string | ✅ | base64 RSA-PSS over the canonical payload |

Accepted date formats (`LicenseManager.cs:43-48`): `yyyy-MM-dd`,
`yyyy-MM-ddTHH:mm:ssZ`, `yyyy-MM-ddTHH:mm:ss.fffZ`.

### 3.3 Date semantics (locked)

`expiresAt: "2027-07-06"` is **Valid throughout all of 2027-07-06 UTC** and only
enters grace at `2027-07-07 00:00:00 UTC` (`LicenseManager.RequireDate`, `:448-472`).

---

## 4. Cryptography & signing model

Locked in B3 (`LicenseSignatureValidator.cs:30-55`):

| Aspect | Value |
|--------|-------|
| Signature scheme | **RSA-PSS** (`RSASignaturePadding.Pss`) |
| Hash | **SHA-256** |
| MGF | MGF1-SHA256 |
| Salt length | = hash length (32 bytes) |
| Minimum key size | **2048 bits** (enforced on load; smaller key throws) |
| Signature encoding | base64 (standard, padded) |
| Public key | PEM (`SubjectPublicKeyInfo`) compiled into the binary (`EmbeddedPublicKey.Pem`) |

### 4.1 Canonical JSON — the actual signing input

The bytes signed/verified are **not** the file text. Both signer and verifier
reduce the JSON to a canonical form via the *same* `CanonicalJson` class so they
can never drift (`license-file-format.md` §5):

1. Object keys sorted lexicographically (ordinal) at every depth.
2. The top-level `signature` property removed before signing/verifying.
3. No insignificant whitespace.
4. Strings escaped as `System.Text.Json` writes them.
5. Numbers keep source raw text; arrays keep source order.
6. UTF-8, no BOM.

`LicenseManager` calls `CanonicalJson.CanonicalizeRoot(...)` (`:144`);
`LicenseGen` calls `CanonicalJson.Canonicalize(...)` (`Program.cs:108`).

### 4.2 Verify vs sign

- **Verify** (runtime): `LicenseSignatureValidator.Verify(canonical, base64Sig)` →
  `_rsa.VerifyData(payload, sig, SHA256, PSS)` (`:63-77`). Bad base64 → `false`
  (not an exception).
- **Sign** (issuance only): `LicenseSignatureValidator.SignWithPrivateKey(canonical,
  privatePem)` — static, **never called by Core in production**, used by LicenseGen
  and tests (`:101-114`).

### 4.3 Keys

- Keypair generator: `KeyGenerator.Generate(dir)` → RSA-2048, writes
  `public.pem` (`ExportSubjectPublicKeyInfoPem`) and `private.pem`
  (`ExportPkcs8PrivateKeyPem`) (`tools/LicenseGen/KeyGenerator.cs:26-28`).
- Embedded key today is a **DEV key** (`EmbeddedPublicKey.cs:4` — “REPLACE BEFORE
  PRODUCTION”), fingerprint pinned by a unit test so an accidental swap fails the
  build.

---

## 5. Component reference

All under `src/ElpisEdgeConnect.Core/Licensing/` unless noted.

| Type | Kind | Responsibility |
|------|------|----------------|
| `ILicenseManager` | interface | Public contract: `Current`, `Status`, `RemainingGrace`, `WarningRaised`, `LoadFromFileAsync`, `LoadAsync`, `IsModuleEnabled`, `IsFeatureEnabled`, `CheckInstanceLimit`, `Tick`. |
| `LicenseManager` | sealed class, `IDisposable` | Production impl. Loads + verifies + parses + evaluates; holds the atomic snapshot; lock-free reads. |
| `LicenseSignatureValidator` | sealed class, `IDisposable` | RSA-PSS/SHA-256 verify (+ static sign). Owns one `RSA`. |
| `EmbeddedPublicKey` | static class | The PEM public key + SHA-256 fingerprint (DEV today). |
| `CanonicalJson` | static class | Canonicalization + `SignaturePropertyName`. Shared with LicenseGen. |
| `LicenseExpirationTracker` | static class | Pure `Evaluate(now, expiresAt, policy) → LicenseExpirationSnapshot`. |
| `LicenseEnforcementPolicy` | sealed record | Grace period (30d) + warn days ({30,7,1}). `Default` is locked. |
| `LicenseInfo` | sealed record | Immutable snapshot: id, customer, gatewayId, edition, dates, limits, **`FrozenDictionary` Modules/Features**. |
| `LicenseModule` | sealed record | `{ bool Enabled, int? MaxInstances }`. |
| `LicenseLimits` | sealed record | `MaxSourceInstances`, `MaxSinkInstances`, `MaxRoutes`. |
| `LicenseEdition` | enum | `Unknown`/`Starter`/`Professional`/`Enterprise`. |
| `LicenseStatus` | enum | `NotLoaded`/`Valid`/`InGracePeriod`/`Expired`/`Invalid`. |
| `LicenseWarning` / `LicenseWarningLevel` | record / enum | Boundary warning payload; levels `Info`/`Warning`/`Critical`. |
| `LicenseEvaluationResult` | sealed record | `Allow` / `Deny(code, reason)` for `CheckInstanceLimit`. |
| `LicenseModuleKeys` | static class | Canonical **hyphenated** module-key constants. |
| `ILicenseGate` / `LicenseGate` / `AllowAllLicenseGate` | interface / 2 impls | Config-apply gate: walks a `GatewayConfiguration`; AllowAll = permissive default. |
| `LicenseGen` | console tool | `keygen` + `new` — issues keys & signed licenses (`tools/LicenseGen/`). |

---

## 6. The load & verify pipeline (8 steps)

`LoadFromFileAsync(path)` checks existence (`LICENSE_FILE_NOT_FOUND` if missing),
opens the stream, and calls `LoadAsync` (`LicenseManager.cs:92-103`). `LoadAsync`
(`:106-180`) is serialized by `_loadLock` (a `SemaphoreSlim`):

| Step | Action | Failure → |
|------|--------|-----------|
| 0 | `await _loadLock` (serialize loads) | — |
| 1 | `JsonDocument.Parse(json)` | `LICENSE_FILE_CORRUPT` |
| 2 | Extract `signature` string | `LICENSE_SIGNATURE_INVALID` (missing/not-string) |
| 3 | `CanonicalJson.CanonicalizeRoot(payload sans signature)` | — |
| 4 | `_validator.Verify(canonical, sigBase64)` | `LICENSE_SIGNATURE_INVALID` (bad sig/wrong key/tamper) |
| 5 | `ParsePayload` → typed `LicenseInfo` (fields, ranges, dates) | `LICENSE_FILE_CORRUPT` |
| 6 | `LicenseExpirationTracker.Evaluate(utcNow, expiresAt, policy)` | — |
| 7 | `Volatile.Write(ref _snapshot, info)`; set `_status`, `_remainingGrace` | — |
| 8 | Reset `_raisedWarnings`; `MaybeRaiseWarningForSnapshot` | — |

**Fail-safe:** any exception is thrown as `LicenseException(code, msg)` and the
**previous snapshot is preserved** — a bad reload never downgrades a running
gateway. All read APIs use `Volatile.Read` over the snapshot's `FrozenDictionary`,
so they are lock-free and allocation-free.

---

## 7. Lifecycle, expiry & warnings

### 7.1 States (`LicenseStatus`)

| Status | Condition | Config changes | Data flow |
|--------|-----------|----------------|-----------|
| `NotLoaded` | no successful load | only **empty** config allowed | runs (soft-fail) |
| `Valid` | `now ≤ expiresAt` | allowed | runs |
| `InGracePeriod` | `expiresAt < now < expiresAt + 30d` | allowed **+ warning** | runs |
| `Expired` | `now ≥ expiresAt + 30d` | **blocked** | **still runs** |
| `Invalid` | (spec) sig/parse fail | n/a — manifests as an exception + retained prior snapshot | — |

### 7.2 Evaluation math (`LicenseExpirationTracker.Evaluate`, `:62-107`)

```
delta = expiresAt - now
days  = floor(delta.TotalDays)          // negative once past expiry
now ≤ expiresAt              → Valid,          RemainingGrace = null
expiresAt < now < graceEnd  → InGracePeriod,  RemainingGrace = graceEnd - now
now ≥ graceEnd              → Expired
graceEnd = expiresAt + policy.GracePeriod (30 days)
```

### 7.3 Warnings

- Pre-expiry boundaries at **{30, 7, 1}** days (`LicenseEnforcementPolicy.Default`,
  `:32-36`). Level: Info (≤30d), Warning (≤7d), Critical (≤1d).
- Grace/Expired always raise (Warning / Critical).
- Raised via the `WarningRaised` event; **deduped** by `status:days` so each
  boundary fires once until the next load (`LicenseManager.cs:257-291`).
- `Tick()` re-evaluates the wall clock and raises new boundaries; the host calls it
  on a background cadence (`ILicenseManager.Tick`, `LicenseManager.cs:233-244`).

---

## 8. The enforcement layers (how it controls the app)

### Layer 1 — Packaging (build/edition)
Per-edition installers decide which protocol **assemblies ship** at all. Outside
this subsystem.

### Layer 2A — DI-registration gate (**the live runtime gate today**)
For each configured source, the host's per-protocol registration extension checks
the license before wiring the adapter into DI. Example
(`MelsecRegistrationExtensions.cs:61-69`):

```csharp
if (license is { Current: not null }
    && !license.IsModuleEnabled(MelsecSourceConfiguration.LicenseModuleKey)) // "source-melsec"
{
    Console.Error.WriteLine("[license] MELSEC source ... module 'source-melsec' not enabled. Skipping registration.");
    return null;   // adapter never registered -> never starts
}
```

- The module key is a hyphenated constant on the adapter's config, e.g.
  `MelsecSourceConfiguration.LicenseModuleKey = "source-melsec"`
  (`MelsecSourceConfiguration.cs:40`), `"source-focas2"`
  (`Focas2SourceConfiguration.cs:35`), `"source-modbus-tcp"`
  (`ModbusTcpSourceConfiguration.cs:47`).
- Disabled → that adapter is skipped; **every other adapter keeps registering**
  (per-adapter isolation, locked #10).

### Layer 2B — Config-apply gate (`LicenseGate`) — implemented, **not wired live**
`LicenseGate.EvaluateAsync(config)` walks the whole `GatewayConfiguration`
(`LicenseGate.cs:46-177`) and blocks:
- **Expired** → all config changes blocked.
- **NotLoaded** → any non-empty config blocked (empty allowed for first-boot).
- Per source/sink whose module isn't enabled → blocked.
- Per module `count > MaxInstances` → blocked.
- Global `Sources/Sinks/Routes` over `Limits` → blocked.
- **InGracePeriod** → allowed **+ warning**.

It runs as **Stage 3** of `ConfigurationValidator` (after DataAnnotations +
cross-record), turning reasons into `CORE.LICENSE_*` errors
(`ConfigurationValidator.cs:91`).
**As-built:** the host builds `ConfigurationManager` with the default
`ConfigurationValidator()`, which uses **`AllowAllLicenseGate`**
(`ConfigurationManager.cs:94`, `CompositionRoot.cs:87`). So this gate is
**implemented and unit-tested but permissive by default** — see §15.

### Layer 3 — UI / API
- **Edition offering** (new 2026-07): the wizard pickers first hide protocols the
  **edition** doesn't offer at all (Starter → Modbus TCP + MQTT; Professional → all
  sources + MQTT; Enterprise → all sources + MQTT + OPC UA Server) — see §9.6 and
  `LicenseEditionCatalog`.
- **Module lock**: of the tiles that survive the edition filter, any whose
  `RequiredLicenseModule` isn't enabled is downgraded to **RequiresLicense** and loses
  its link — `SourceProtocolPickerModel.Resolve(gate, edition)` /
  `DestinationProtocolPickerModel.Resolve(gate, edition)`.
- Probe / browse / test-connection API services gate on the module key
  (`MqttTestConnectionService.BuildLicenseGate(license)` used across
  `*ProbeService.cs`).
- `LicenseModuleDisabledPanel.razor` renders the disabled state.

### The overriding rule
The canonical pipeline → routing → store-and-forward → sinks path has **no license
check**. Licensing gates configuration/activation only (locked #7, #14).

---

## 9. License types (editions) — what each one means

EdgeConnect has **two independent vocabularies** that people loosely call "license
types." Keep them separate:

- **Edition** — the single `edition` string on the license file
  (`LicenseEdition` enum). A *label* identifying the commercial tier.
- **Module tier** — a packaging category (`Base` / `Standard` / `Premium`) attached
  to each module key in `module-catalog.md`. A *guideline* for which modules a given
  edition typically bundles.

An edition does **not** mechanically switch modules on; the `modules` map does
(see §9.4). The edition is descriptive; the modules are authoritative.

### 9.1 The three license types (editions)

`LicenseEdition` (`LicenseEdition.cs`):

| Type | Int | Positioning | Typically bundles | Intended customer |
|------|-----|-------------|-------------------|-------------------|
| **`Starter`** | 1 | Entry tier — minimum viable feature set. | `core-runtime` + one/few **Standard** sources (e.g. `source-modbus-tcp`, `source-mtconnect`) + `sink-mqtt`. Often **without** `connectivity-studio` (headless). | A single line / pilot needing one protocol to MQTT. |
| **`Professional`** | 2 | Mid/standard tier — all standard protocols and features. | `core-runtime` + all **Standard** sources/sinks + `connectivity-studio` (the Studio UI) + selected **Premium** protocols the customer runs. | A plant with mixed standard PLCs and the management UI. |
| **`Enterprise`** | 3 | Top tier — all features plus fleet management. | Everything: all **Premium** sources (`source-focas2`, `source-s7`, `source-ethernet-ip`, `source-opc-ua-client`, `source-melsec`), `sink-opc-ua-server`, `connectivity-studio`, and reserved integrations (`historian-bridge`) as they ship. | Multi-site / multi-vendor deployments, fleet-managed. |
| `Unknown` | 0 | Not a sellable type — default for an unset value. **Rejected at parse** (`LicenseManager.cs:312-317`). | — | — |

> Positioning text comes from the enum doc comments (`LicenseEdition.cs:20-27`);
> the "typically bundles" column is the **packaging convention** from
> `module-catalog.md`, **not** something the code enforces.

### 9.2 Module tiers (the packaging building blocks)

Each module key carries a tier in `module-catalog.md`. These are **defaults, not
contractual** — sales/commercial decisions override, and any module can be sold as
an add-on to any edition:

| Tier | Meaning | Example modules |
|------|---------|-----------------|
| **Base** | In every edition; gated only on license *validity*. | `core-runtime` |
| **Standard** | Mid-tier and up. | `sink-mqtt`, `sink-http`, `sink-tcp`, `source-modbus-tcp`, `source-mtconnect`, `source-brother-http` |
| **Premium** | Top tier, or sold as add-ons. | `sink-opc-ua-server`, `source-focas2`, `source-s7`, `source-opc-ua-client`, `source-ethernet-ip`, `source-melsec`, `connectivity-studio`, `historian-bridge` |

### 9.3 Example edition → modules composition

A concrete, **illustrative** mapping (adjust per contract). This is what you'd hand
to `LicenseGen --modules` for each type (hyphenated keys — see §10):

```text
Starter       source-modbus-tcp=true, source-mtconnect=true, sink-mqtt=true
              limits: maxSources=5  maxSinks=2  maxRoutes=10

Professional  source-modbus-tcp=true, source-mtconnect=true, source-brother-http=true,
              source-focas2=true:20, sink-mqtt=true, connectivity-studio=true
              limits: maxSources=50 maxSinks=5  maxRoutes=100

Enterprise    source-focas2=true, source-s7=true, source-ethernet-ip=true,
              source-melsec=true, source-opc-ua-client=true,
              sink-mqtt=true, sink-opc-ua-server=true, connectivity-studio=true
              limits: maxSources=200 maxSinks=20 maxRoutes=500
```

### 9.4 Why the edition doesn't gate anything by itself

**Critical:** the edition is used for **UI labelling and sanity-checking only**.
Actual entitlements always come from `LicenseInfo.Modules`, **never** from the
edition (`LicenseEdition.cs:9-14`). Consequences:

- `--edition Enterprise` with an empty/wrong `modules` map enables **nothing** — the
  DI gate (§8) still finds no matching module keys and skips every adapter.
- Two licenses both labelled `Professional` can enable completely different protocol
  sets.
- So the edition is essentially a human-facing tag; **get the `modules` map right**
  and the edition label follows for reporting/UX.

> **Update (2026-07):** the above remains true for runtime **entitlement** (what
> actually activates and what config saves) — that is still driven by the `modules`
> map, never the edition. But the edition now **does** gate which protocols are
> *offered* (selectable) in the Studio wizards and the License Generator. See §9.6.

### 9.5 Special runtime "states" (not license types, but often confused)

Distinct from the sellable types, the runtime also has license **statuses** — see
§7.1 (`NotLoaded`, `Valid`, `InGracePeriod`, `Expired`, `Invalid`). A gateway with
**no license file** runs in a permissive/dev posture (soft-fail, `NotLoaded`): data
flows, but only an *empty* configuration can be applied.

### 9.6 Edition-based protocol *offering* (UI + generator) — `LicenseEditionCatalog`

**New (2026-07).** While runtime *entitlement* still comes from the `modules` map
(§9.4), the **set of protocols an operator may even choose** is now gated by the
**edition**, in both the Studio onboarding wizards and the License Generator. This is
enforcement Layer 3 (UI) only — it never touches the data path or the module gate.

Single source of truth: `LicenseEditionCatalog`
(`src/ElpisEdgeConnect.Core/Licensing/LicenseEditionCatalog.cs`). Offering matrix:

| Edition | Sources offered | Destinations offered |
|---------|-----------------|----------------------|
| **Starter** | **Modbus TCP only** | **MQTT only** |
| **Professional** | all sources | **MQTT only** |
| **Enterprise** | all sources | **MQTT + OPC UA Server** |
| `Unknown` (dev / no license) | all | all |

Rules and nuances:

- Non-offered tiles are **hidden entirely** — distinct from the greyed
  "Requires Premium" **lock** used when a tile *is* offered but its module isn't
  enabled. (Hidden = wrong edition; locked = right edition, module off.)
- Starter surfaces a **single Modbus variant** — Modbus **TCP**, not Modbus **RTU** —
  even though both tiles share the `source-modbus-tcp` module.
- **For destinations the edition is the gate, independent of the `modules` map.** A
  Professional license that *contains* `sink-opc-ua-server` still will **not** show
  OPC UA Server in the wizard: OPC UA Server is Enterprise-only in the UI. To offer it
  as a Professional add-on you must change `LicenseEditionCatalog`, not just the
  license file.
- The Pending **HTTP webhook / TCP socket** destination placeholders are hidden under
  every named edition; they appear only in the dev/`Unknown` posture.
- Sources are **not** restricted for Professional/Enterprise — only **Starter**
  narrows sources (to Modbus TCP).

Where it's applied:

| Surface | Call / file |
|---------|-------------|
| Studio source picker | `SourceProtocolPickerModel.Resolve(gate, edition)` |
| Studio destination picker | `DestinationProtocolPickerModel.Resolve(gate, edition)` |
| Pages read live edition | `ILicenseManager.Current?.Edition` in `ChooseSourceProtocol.razor` / `ChooseDestinationProtocol.razor` |
| License Generator (GUI) | `MainForm.PopulateProtocolsForEdition()` — repopulates the Sources/Destinations dropdowns per edition and auto-ticks the single offered option where there is exactly one |

Helper API on `LicenseEditionCatalog`: `RestrictsSources(edition)`,
`RestrictsSinks(edition)`, `IsSourceModuleOffered(edition, key)`,
`IsSinkModuleOffered(edition, key)`.

**Defense in depth.** For a restricted edition the generator writes *only* the offered
modules, so the runtime DI gate (§8) and the config-save `LicenseGate` reject anything
else too. The UI hide is the **first** of three consistent layers, not the only one.

---

## 10. Module keys — catalog and the three-way mismatch ⚠️

### 10.1 Canonical keys (`LicenseModuleKeys.cs`) — **hyphenated**

Core: `core-runtime`.
Sinks: `sink-mqtt`, `sink-opc-ua-server`, `sink-http`*, `sink-tcp`*.
Sources: `source-modbus-tcp`, `source-focas2`, `source-mtconnect`,
`source-brother-http`, `source-s7`, `source-opc-ua-client`, `source-ethernet-ip`,
`source-melsec`.
Features/UI: `connectivity-studio`, `historian-bridge`*.  (* reserved/future)

These are the strings `IsModuleEnabled` checks and therefore the ones the
**DI + UI gates** require in `license.json`.

### 10.2 The mismatch — read before issuing any license

There are **three** spellings in play. They do **not** all agree:

| Consumer | Key shape | Example | Source |
|----------|-----------|---------|--------|
| DI-registration + UI (**live**) | hyphenated | `source-melsec` | `LicenseModuleKeys` / adapter `LicenseModuleKey` const |
| `LicenseGate` config-walk (not live) | dotted | `source.melsec` | `LicenseGate.SourceModuleKey` (`"source." + protocol`) |
| `LicenseGen` **default** `--modules` | dotted, legacy | `source.focas2`, `source.modbus` | `Program.cs:134-138` |

**Consequence:** a license generated with LicenseGen's *default* module list (no
`--modules`) writes **dotted** keys that the **live DI gate will not match** — the
adapters would be treated as *not licensed*. You must pass `--modules` explicitly
with the **hyphenated** `LicenseModuleKeys` values (LicenseGen writes whatever key
string you give it verbatim).

> ✅ **Rule of thumb:** always issue with explicit hyphenated keys, e.g.
> `--modules source-focas2=true:20,source-melsec=true,sink-mqtt=true`.
> Reconciling the three spellings (and enabling `LicenseGate`) is an open item —
> see §15.

---

## 11. How to activate a license — full runbook

There is **no online registration and no upload UI**. "Activation" = generate a
signed file, place it, restart. Three phases:

### Phase A — Generate a signing keypair (one-time, offline)

```bash
dotnet run --project tools/LicenseGen -- keygen --out keys/
# writes keys/public.pem and keys/private.pem
```
Store `private.pem` in the password manager/HSM — never commit it. (For a real
product build you also embed the matching public key — see §12.)

### Phase B — Issue the signed license

```bash
dotnet run --project tools/LicenseGen -- new \
    --customer   "Sony" \
    --gateway    "GW-SONY-001" \
    --edition    Professional \
    --expires    2027-07-06 \
    --private-key keys/private.pem \
    --out        license.json \
    --max-sources 50 --max-sinks 5 --max-routes 100 \
    --modules "source-focas2=true:20,source-melsec=true,source-ethernet-ip=true,sink-mqtt=true"
```

Arguments (`Program.cs:66-125`):

| Flag | Required | Default | Notes |
|------|----------|---------|-------|
| `--customer` | ✅ | — | customer name |
| `--gateway` | ✅ | — | gatewayId binding |
| `--edition` | ✅ | — | `Starter`/`Professional`/`Enterprise` |
| `--expires` | ✅ | — | `yyyy-MM-dd` |
| `--private-key` | ✅ | — | path to `private.pem` |
| `--out` | ✅ | — | output path for `license.json` |
| `--license-id` | ➖ | `LIC-<utcstamp>` | stable id |
| `--max-sources` / `--max-sinks` / `--max-routes` | ➖ | 50 / 5 / 100 | global caps |
| `--modules` | ➖ | *(dotted legacy defaults — avoid)* | `key=enabled[:max],...`; **use hyphenated keys** |

`issuedAt` is stamped as today (UTC). The tool canonicalizes, signs with your
private key, and writes indented UTF-8 (no BOM) with the `signature` appended.

### Phase C — Install & activate on the gateway

1. Copy `license.json` to the gateway, e.g. `C:\ProgramData\EdgeConnect\license.json`.
2. Point the host at it (either is fine):
   - env var: `EDGECONNECT_LICENSE_PATH = C:\ProgramData\EdgeConnect\license.json`, **or**
   - default path: `<dataRoot>\license.json` (dataRoot = `EDGECONNECT_DATA_ROOT`)
   (`EdgeConnectComposition.cs:125-126`).
3. **Restart the host/service.** On startup it eager-loads and verifies the file
   (`EdgeConnectComposition.cs:150-164`), and `HostStartup`'s `LoadLicense` phase
   reloads the same instance (`HostStartup.cs:206-211`).

### Phase D — Verify activation

- **Startup log:** a failed load prints
  `[startup] License pre-load failed; continuing without enforcement: ...` — its
  **absence** means the file loaded.
- **Per-adapter skips:** `[license] <PROTO> source '<id>' configured but module
  '<key>' is not enabled. Skipping registration.` means a module key mismatch or a
  disabled module (see §10.2 first).
- **Studio:** licensed protocol tiles show **Available**; unlicensed show **Requires
  Premium**.
- Adapters you expect should reach **Running** in the SourceDetail/diagnostics view.

### Updating / renewing
Replace `license.json` and **restart**. There is no hot-reload API; the manager
reloads only on startup (and on explicit `LoadAsync`, which nothing in the host
calls at runtime today).

---

## 12. Production key custody & rotation

The repo ships a **DEV** key (`EmbeddedPublicKey.cs`). To go to production
(`license-file-format.md` §10.2):

1. `dotnet run --project tools/LicenseGen -- keygen --out keys/` on an offline box.
2. Move `keys/private.pem` into the password manager / HSM. Never commit it.
3. Replace the PEM constant in `EmbeddedPublicKey.cs` with the new **public** key.
4. Update `EmbeddedPublicKey.Fingerprint` (SHA-256 of the SubjectPublicKeyInfo).
5. Update the expected fingerprint in
   `LicenseSignatureValidatorTests.EmbeddedKey_FingerprintMatchesExpectedDevValue`.
6. Re-issue all customer licenses signed with the new private key.
7. Tag the release `license-key-rotation-N`.

---

## 13. Error-code reference (`CoreErrors.cs`)

| Code | When |
|------|------|
| `CORE.LICENSE_FILE_NOT_FOUND` | `LoadFromFileAsync` path missing |
| `CORE.LICENSE_FILE_CORRUPT` | JSON parse failed / required field missing / value out of range |
| `CORE.LICENSE_SIGNATURE_INVALID` | signature missing, bad base64, or failed verification |
| `CORE.LICENSE_NOT_LOADED` | check attempted before any successful load |
| `CORE.LICENSE_MODULE_DISABLED` | configured source/sink uses a module not enabled |
| `CORE.LICENSE_INSTANCE_LIMIT_REACHED` | per-module count > `maxInstances` |
| `CORE.LICENSE_GLOBAL_LIMIT_EXCEEDED` | a global `limits` ceiling exceeded |
| `CORE.LICENSE_EXPIRED` | warning while in grace period |
| `CORE.LICENSE_GRACE_EXHAUSTED` | status `Expired`; config writes blocked |
| `CORE.LICENSE_GATEWAY_MISMATCH` | **reserved** — `gatewayId` binding not yet enforced |

All load failures throw `LicenseException(code, message)`.

---

## 14. Testing & verification

- Signature/fingerprint pinned by
  `LicenseSignatureValidatorTests.EmbeddedKey_FingerprintMatchesExpectedDevValue`.
- End-of-day validity pinned by
  `LicenseExpirationTrackerTests.EndOfDay_StillValid_ThroughoutLastDay`.
- The dev **private** key lives in `tests/.../Licensing/TestRsaKeys.cs` for
  hermetic test signing (never used for customer licenses).
- Run: `dotnet test tests/ElpisEdgeConnect.Core.Tests/ --filter FullyQualifiedName~Licensing`.

---

## 15. Known gaps / as-built nuances

1. **Config-apply gate is permissive by default.** `LicenseGate` is complete and
   tested but the host wires `AllowAllLicenseGate` (`ConfigurationManager.cs:94`),
   so live enforcement today = **DI-registration gate (§8 Layer 2A) + UI (§8 Layer
   3)**. Turning on real config-time enforcement is roughly a one-line DI change to
   pass `new ConfigurationValidator(CrossRecordValidator.Instance, new
   LicenseGate(licenseManager))`.
2. **Three-way module-key mismatch (§10.2).** DI/UI want hyphenated; LicenseGate and
   LicenseGen defaults use dotted. Until reconciled, **issue hyphenated keys
   explicitly**. Worth an ADR + aligning `LicenseGate.SourceModuleKey`/`SinkModuleKey`
   and LicenseGen defaults to `LicenseModuleKeys`.
3. **`features` not enforced in Core.** Recorded on the snapshot; gating is left to
   future consumers.
4. **`gatewayId` binding not enforced.** `LICENSE_GATEWAY_MISMATCH` is reserved.
5. **No hot reload.** License changes require a host restart.
6. **DEV signing key in-repo.** Must be rotated before production (§12).

---

## 16. Quick-reference cheat sheet

```text
Generate keys : dotnet run --project tools/LicenseGen -- keygen --out keys/
Issue license : dotnet run --project tools/LicenseGen -- new --customer <c> --gateway <g>
                --edition Professional --expires yyyy-MM-dd --private-key keys/private.pem
                --out license.json --modules "source-focas2=true:20,sink-mqtt=true"
Install       : set EDGECONNECT_LICENSE_PATH=<path>\license.json   (or <dataRoot>\license.json)
Activate      : restart host  ->  verify: no "[startup] License pre-load failed"
Module keys   : HYPHENATED (source-melsec, source-focas2, sink-mqtt) — NOT dotted
Editions      : Starter / Professional / Enterprise  (labelling only; modules decide entitlements)
Expiry        : Valid -> +30d grace (config allowed) -> Expired (config blocked, DATA STILL FLOWS)
Crypto        : RSA-PSS / SHA-256 / >=2048-bit / offline / public key embedded
Never         : blocks the data path; phones home; hot-reloads
```

---

*Every reference in this document was verified against source on the
`Sony_Development` branch. If the code changes, update this file — the code is the
source of truth.*
